using System.Text.RegularExpressions;
using LoupixDeck.Plugin.CoolerControl.Rendering.Pixel;
using LoupixDeck.Plugin.CoolerControl.Rendering.Tiles;
using LoupixDeck.Plugin.CoolerControl.Telemetry;

namespace LoupixDeck.Plugin.CoolerControl.Rendering;

/// <summary>
/// The labels a CoolerControl.Sensor tile shows, derived from the names the menu gives the readings
/// (<see cref="SensorMenu.Name"/>), so the tile and the menu speak the same language. Each label
/// is the component ("CPU", "GPU", …) followed by the menu's entry name: the CPU's and the GPU's
/// load read "CPU Load" and "GPU Load" instead of both "Load". The quantity is left to the unit
/// printed beside the value.
///
/// <para>A tile header takes about eleven characters, a row label of a multi-reading tile about
/// eight, so the row label is abbreviated ("CPU max", "GPU Hot"). Labels are unique among the
/// readings that share a unit; where the words alone would collide, a running number is added.</para>
/// </summary>
internal static partial class TileLabels
{
    public sealed record Labels(string Header, string Short);

    private sealed record Cache(IReadOnlyList<CoolerSensor> Sensors, Dictionary<string, Labels> Labels);

    /// <summary>Width of a single-reading tile's header text (the header band minus its margins).</summary>
    private const int HeaderRoom = TileDrawing.W - 2;

    /// <summary>Characters a row label keeps beside a two-digit value at 2×; longer labels are cut
    /// when drawn, so <see cref="Shorten"/> makes the part that tells them apart fit first.</summary>
    private const int ShortBudget = 8;

    private static volatile Cache? _cache;

    private static readonly Dictionary<string, string> ComponentTags = new()
    {
        [Components.Cpu] = "CPU",
        [Components.Gpu] = "GPU",
        [Components.Memory] = "RAM",
        [Components.Storage] = "Disk",
        [Components.Network] = "Net"
    };

    /// <summary>Row-label abbreviations, applied as whole words, case-insensitively.</summary>
    private static readonly (string Word, string Short)[] Abbreviations =
    [
        ("Hotspot", "Hot"), ("Hot Spot", "Hot"), ("Junction", "Junc"),
        ("Temperature", "Temp"),
        ("Clock", "Clk"),
        ("Memory", "Mem"),
        ("Power", "Pwr"),
        ("Package", "Pkg"),
        ("Composite", "Comp"),
        ("Liquid", "Liq"),
        ("Average", "Avg"), ("Maximum", "Max"), ("Minimum", "Min"),
        ("Graphics", "Gfx")
    ];

    /// <summary>The labels of the reading under <paramref name="key"/> (<see cref="CoolerSensor.Key"/>),
    /// or null when the menu does not offer it. Computed once per sensor snapshot.</summary>
    public static Labels? For(IReadOnlyList<CoolerSensor> sensors, string key)
    {
        Cache? cache = _cache;
        if (cache is null || !ReferenceEquals(cache.Sensors, sensors))
            _cache = cache = new Cache(sensors, Compute(sensors));

        return cache.Labels.GetValueOrDefault(key);
    }

    private static Dictionary<string, Labels> Compute(IReadOnlyList<CoolerSensor> sensors)
    {
        List<SensorName> named = SensorMenu.Name(sensors);
        List<string> headers = [];
        List<string> shorts = [];
        foreach (SensorName name in named)
        {
            string tag = ComponentTags.GetValueOrDefault(name.Component, string.Empty);
            string entry = EntryWords(name);
            string header = Join(tag, entry);
            string compact = Join(tag, Abbreviate(entry));

            headers.Add(PixelFont.Measure(header) > HeaderRoom ? compact : header);
            shorts.Add(Shorten(Join(tag, Aggregate().Replace(Abbreviate(entry), "$1")), tag.Length > 0));
        }

        Number(headers, named);
        Number(shorts, named);

        Dictionary<string, Labels> labels = [];
        for (int i = 0; i < named.Count; i++)
            labels[MetricKeys.ForSensor(named[i].Sensor)] = new Labels(headers[i], shorts[i]);

        return labels;
    }

    /// <summary>The entry part of the label: the menu's entry name without its unit, already free
    /// of the quantity's words. Where a lone entry took its submenu's name in the menu, the label
    /// keeps the entry's own ("Liquid" rather than "Temperature").</summary>
    private static string EntryWords(SensorName name)
    {
        string entry = name.Name;

        // "RAM DIMM 2" rather than "RAM Temperature 2".
        if (name.Component == Components.Memory && name.Device is not null)
            entry = name.Device;

        return Whitespace().Replace(entry.Replace('(', ' ').Replace(")", ""), " ").Trim();
    }

    private static string Abbreviate(string entry)
    {
        string text = entry;
        foreach ((string word, string abbreviation) in Abbreviations)
            text = ReplaceWord(text, word, abbreviation);

        return Whitespace().Replace(text, " ").Trim();
    }

    /// <summary>
    /// Cuts a row label to <see cref="ShortBudget"/> characters word by word, so every word keeps a
    /// few letters instead of the tail being lost: "Vorne Unten" → "Vor Unte". Never touches the
    /// component tag or trailing digits.
    /// </summary>
    private static string Shorten(string label, bool hasTag)
    {
        List<string> words = label.Split(' ').ToList();
        int first = hasTag ? 1 : 0;

        while (string.Join(' ', words).Length > ShortBudget)
        {
            // The longest word other than the last with more than three letters, else the last one.
            int pick = -1;
            for (int i = first; i < words.Count - 1; i++)
            {
                if (Letters(words[i]) > 3 && (pick < 0 || words[i].Length > words[pick].Length))
                    pick = i;
            }

            if (pick < 0 && words.Count - 1 >= first && Letters(words[^1]) > 3)
                pick = words.Count - 1;

            if (pick < 0)
                break;

            words[pick] = DropLetter(words[pick]);
        }

        return string.Join(' ', words);
    }

    private static int Letters(string word) => word.Count(char.IsLetter);

    /// <summary>Removes the last letter before any trailing digits ("Chassis1" → "Chassi1").</summary>
    private static string DropLetter(string word)
    {
        int end = word.Length;
        while (end > 0 && !char.IsLetter(word[end - 1]))
            end--;

        return (word[..(end - 1)] + word[end..]).TrimEnd('-', '.', ',', '_');
    }

    /// <summary>Appends a running number to labels that collide among readings of the same unit.</summary>
    private static void Number(List<string> labels, List<SensorName> named)
    {
        foreach (IGrouping<(string, string), int> clash in Enumerable.Range(0, labels.Count)
                     .GroupBy(i => (labels[i].ToUpperInvariant(), SensorMenu.UnitTag(named[i].Sensor)))
                     .Where(g => g.Count() > 1))
        {
            int number = 1;
            foreach (int i in clash)
                labels[i] = $"{labels[i]} {number++}";
        }
    }

    private static string Join(string tag, string entry)
    {
        if (tag.Length == 0 || entry.Length == 0)
            return tag.Length == 0 ? entry : tag;

        // Names sometimes start with the component already.
        return entry.StartsWith(tag + " ", StringComparison.OrdinalIgnoreCase) ? entry : $"{tag} {entry}";
    }

    private static string ReplaceWord(string text, string word, string replacement) =>
        Regex.Replace(text, $@"(?<![A-Za-z]){Regex.Escape(word)}(?![A-Za-z])", replacement,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>"Clk max" → "max": in a row label the unit already says clock.</summary>
    [GeneratedRegex(@"^Clk\s+(max|min|avg)$")]
    private static partial Regex Aggregate();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
