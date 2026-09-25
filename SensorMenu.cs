using System.Text.RegularExpressions;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.CoolerControl;

/// <summary>
/// Builds the <c>CoolerControl.Sensor</c> part of the editor menu: readings are sorted by
/// component (CPU, GPU, Memory, Storage, Mainboard, Cooling, Network, Other), then by device where
/// a component has several (drives, fan hubs), then by quantity (Temperature, Clock, Load, Power,
/// Fans), and every entry gets a name that is unique within its submenu. Each entry stores the
/// reading's key (<see cref="CoolerSensor.Key"/>).
/// </summary>
internal static partial class SensorMenu
{
    /// <summary>A quantity inside a component.</summary>
    private sealed record Section(string Name, int Rank);

    private static readonly string[] SectionOrder = ["Temperature", "Clock", "Load", "Power", "Fans"];

    /// <summary>Words that repeat the component and are dropped from the names
    /// ("CPU Temp Tctl" under CPU → "Temp Tctl").</summary>
    private static readonly Dictionary<string, string[]> ComponentWords = new()
    {
        [Components.Cpu] = ["CPU"],
        [Components.Gpu] = ["GPU"]
    };

    /// <summary>Words that repeat the quantity, the daemon's own ("Temp", "Freq") included.</summary>
    private static readonly Dictionary<string, string[]> SectionWords = new()
    {
        ["Temperature"] = ["Temperature", "Temp"],
        ["Clock"] = ["Clock", "Freq"],
        ["Load"] = ["Load"],
        ["Power"] = ["Power"]
    };

    /// <summary>Aggregates of a quantity; named after it ("Clock max") so the name says what it is.</summary>
    private static readonly string[] Aggregates = ["max", "avg", "min"];

    public static List<MenuNode> Build(IReadOnlyList<CoolerSensor> sensors)
    {
        List<SensorName> named = Name(sensors);

        List<MenuNode> components = [];
        foreach (string component in Components.Order)
        {
            List<SensorName> ofComponent = named.Where(n => n.Component == component).ToList();

            List<MenuNode> children = [];
            foreach (IGrouping<string?, SensorName> device in ofComponent.GroupBy(n => n.Device))
            {
                List<MenuNode> sections = SectionNodes(device.ToList());
                if (device.Key is null)
                    children.AddRange(sections);
                else if (sections is [{ CommandName: not null } entry])
                {
                    // A device with one reading is that reading ("DIMM 2").
                    children.Add(new MenuNode
                        { Name = device.Key, CommandName = entry.CommandName, Parameters = entry.Parameters });
                }
                else
                    children.Add(new MenuNode { Name = device.Key, Children = sections });
            }

            if (children.Count > 0)
                components.Add(new MenuNode { Name = component, Children = children });
        }

        return components;
    }

    private static List<MenuNode> SectionNodes(List<SensorName> names)
    {
        List<IGrouping<string, SensorName>> sections = names.GroupBy(n => n.Section).ToList();

        List<MenuNode> children = [];
        foreach (IGrouping<string, SensorName> section in sections)
        {
            List<MenuNode> nodes = section.Select(n => new MenuNode
            {
                Name = n.MenuName,
                CommandName = CoolerControlSensorCommand.CommandName,
                Parameters = new Dictionary<string, string> { { "Sensor", n.Sensor.Key } }
            }).ToList();

            if (sections.Count == 1 || nodes.Count == 1)
                children.AddRange(nodes);  // no extra level for a lone quantity or a lone entry
            else
                children.Add(new MenuNode { Name = section.Key, Children = nodes });
        }

        return children;
    }

    /// <summary>
    /// Names every reading the menu offers, in menu order. Tile labels are derived from these names
    /// so the menu and the tile call a reading the same thing.
    /// </summary>
    public static List<SensorName> Name(IReadOnlyList<CoolerSensor> sensors)
    {
        List<SensorName> named = [];
        foreach (string component in Components.Order)
        {
            // A device level only where the component has several devices (drives, fan hubs).
            List<IGrouping<string, CoolerSensor>> devices = sensors
                .Where(s => Components.Of(s) == component)
                .GroupBy(s => s.DeviceUid)
                .ToList();
            Dictionary<string, string?> deviceNames = DeviceNames(devices);

            foreach (IGrouping<string, CoolerSensor> device in devices)
                named.AddRange(DeviceEntries(component, deviceNames[device.Key], device.ToList()));
        }

        return named;
    }

    /// <summary>Display names of the devices, unique within the component; null when there is
    /// only one device.</summary>
    private static Dictionary<string, string?> DeviceNames(List<IGrouping<string, CoolerSensor>> devices)
    {
        Dictionary<string, string?> names = [];
        if (devices.Count == 1)
        {
            names[devices[0].Key] = null;
            return names;
        }

        Dictionary<string, int> used = new(StringComparer.OrdinalIgnoreCase);
        foreach (IGrouping<string, CoolerSensor> device in devices)
        {
            // Memory modules report only their driver ("spd5118"); they are numbered instead.
            string name = Components.Of(device.First()) == Components.Memory ? "DIMM" : Components.DeviceName(device.First());
            if (name.Length == 0)
                name = device.Key;

            int count = used.GetValueOrDefault(name) + 1;
            used[name] = count;
            names[device.Key] = count == 1 && name != "DIMM" ? name : $"{name} {count}";
        }

        return names;
    }

    private static List<SensorName> DeviceEntries(string component, string? device, List<CoolerSensor> sensors)
    {
        Dictionary<Section, List<CoolerSensor>> bySection = [];
        foreach (CoolerSensor sensor in sensors)
        {
            Section section = SectionFor(sensor.Quantity);
            if (!bySection.TryGetValue(section, out List<CoolerSensor>? entries))
                bySection[section] = entries = [];

            entries.Add(sensor);
        }

        List<KeyValuePair<Section, List<CoolerSensor>>> sections = bySection.OrderBy(pair => pair.Key.Rank).ToList();

        List<SensorName> named = [];
        foreach ((Section section, List<CoolerSensor> entries) in sections)
        {
            List<SensorName> names = SectionNames(component, device, section, entries);

            // A submenu with one entry is noise: the entry takes the submenu's place and name
            // (unless it is the device's only quantity, which gets no submenu either).
            if (sections.Count > 1 && names.Count == 1)
                names[0] = names[0] with { MenuName = section.Name };

            named.AddRange(names);
        }

        return named;
    }

    private static Section SectionFor(Quantity quantity)
    {
        string name = quantity switch
        {
            Quantity.Temperature => "Temperature",
            Quantity.Clock => "Clock",
            Quantity.Load => "Load",
            Quantity.Power => "Power",
            _ => "Fans"
        };

        return new Section(name, Array.IndexOf(SectionOrder, name));
    }

    private static List<SensorName> SectionNames(string component, string? device, Section section,
        List<CoolerSensor> sensors)
    {
        // The daemon lists a fan's speed before its duty, so snapshot order already interleaves them.
        List<string> baseNames = sensors.Select(s => BaseName(s, component, section)).ToList();

        // Name the unit only where it tells entries apart: "Pump (RPM)" beside "Pump (%)",
        // but plain "Tctl" among temperatures.
        bool showUnit = sensors.Select(UnitTag).Distinct().Count() > 1;
        List<string> tags = sensors.Select(s => showUnit ? UnitTag(s) : "").ToList();

        // Readings named identically get a running number per unit.
        foreach (IGrouping<string, int> clash in Enumerable.Range(0, sensors.Count)
                     .GroupBy(i => Compose(baseNames[i], tags[i]).ToUpperInvariant())
                     .Where(g => g.Count() > 1))
        {
            int number = 1;
            foreach (int i in clash)
                baseNames[i] = $"{baseNames[i]} {number++}";
        }

        List<string> names = Enumerable.Range(0, sensors.Count).Select(i => Compose(baseNames[i], tags[i])).ToList();

        // Last resort for anything still ambiguous: the daemon's name of the reading.
        foreach (IGrouping<string, int> clash in Enumerable.Range(0, sensors.Count)
                     .GroupBy(i => names[i], StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Count() > 1))
        {
            foreach (int i in clash)
            {
                names[i] = $"{names[i]} #{sensors[i].Name}";
                baseNames[i] = $"{baseNames[i]} #{sensors[i].Name}";
            }
        }

        return Enumerable.Range(0, sensors.Count)
            .Select(i => new SensorName(sensors[i], component, device, section.Name, baseNames[i], names[i]))
            .ToList();
    }

    private static string Compose(string name, string unitTag) =>
        unitTag.Length == 0 ? name : $"{name} ({unitTag})";

    /// <summary>
    /// The entry name before units and numbering: the label with the component and quantity words
    /// removed ("CPU Temp Tctl" → "Tctl"), an aggregate named after its quantity ("CPU Freq Max" →
    /// "Clock max"), or the quantity's name where nothing else is left ("GPU Load" → "Load").
    /// </summary>
    private static string BaseName(CoolerSensor sensor, string component, Section section)
    {
        string label = sensor.Label.Trim();

        // A reading the driver gave no label keeps the daemon's name: "temp1" says no more than its
        // quantity, "fan2" reads "Fan 2".
        if (label == sensor.Name)
            label = RawTemperature().Replace(RawFan().Replace(label, "Fan $1"), "");

        if (ComponentWords.TryGetValue(component, out string[]? words))
            label = RemoveWords(label, words);

        if (SectionWords.TryGetValue(section.Name, out string[]? quantityWords))
        {
            string stripped = RemoveWords(label, quantityWords);
            // Kept where only a number would be left ("Temp 1").
            if (stripped.Any(char.IsLetter))
                label = stripped;
            else if (stripped.Length == 0)
                label = string.Empty;
        }

        if (label.Length == 0)
            return section.Name;

        string? aggregate = Aggregates.FirstOrDefault(a => a.Equals(label, StringComparison.OrdinalIgnoreCase));
        return aggregate is null ? label : $"{section.Name} {aggregate}";
    }

    private static string RemoveWords(string label, string[] words)
    {
        foreach (string word in words)
            label = Regex.Replace(label, $@"(?<![A-Za-z]){Regex.Escape(word)}(?![A-Za-z])", "", RegexOptions.IgnoreCase);

        return Whitespace().Replace(label, " ").Trim();
    }

    /// <summary>The unit of a reading, as the menu and the labels tell readings apart by it.</summary>
    public static string UnitTag(CoolerSensor sensor) => sensor.Quantity switch
    {
        Quantity.Temperature => "°C",
        Quantity.Load or Quantity.FanDuty => "%",
        Quantity.FanSpeed => "RPM",
        Quantity.Clock => "MHz",
        Quantity.Power => "W",
        _ => string.Empty
    };

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"^temp\d+$")]
    private static partial Regex RawTemperature();

    [GeneratedRegex(@"^fan(\d+)$")]
    private static partial Regex RawFan();
}

/// <summary>
/// How the menu names a reading. <paramref name="Device"/> is the device level (null where the
/// component has one device); <paramref name="Name"/> is the entry name without the unit that sets
/// it apart from its neighbours ("Pump"); <paramref name="MenuName"/> is what the menu shows
/// ("Pump (RPM)", or the quantity's name when the entry replaces a one-entry submenu).
/// </summary>
internal sealed record SensorName(CoolerSensor Sensor, string Component, string? Device, string Section,
    string Name, string MenuName);
