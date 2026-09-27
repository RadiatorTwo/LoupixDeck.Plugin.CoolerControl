using System.Text.RegularExpressions;

namespace LoupixDeck.Plugin.CoolerControl.Telemetry;

/// <summary>
/// The derived metrics the component pages show (CPU temperature, GPU clock …). Each is picked
/// out of the CoolerControl snapshot by component (<see cref="Components"/>), quantity and the
/// daemon's fixed channel names ("CPU Load", "CPU Freq Max", "GPU Temp"); labels only where the
/// kernel supplies them ("Tctl", "Package id 0"). CoolerControl reports no memory usage and no
/// transfer rates, so there are no RAM and NET pages and the disk page shows temperatures only.
/// </summary>
internal static class PageMetrics
{
    public const string CpuTemp = "cpu.temp";
    public const string CpuClock = "cpu.clock";
    public const string CpuFan = "cpu.fan";
    public const string CpuLoad = "cpu.load";
    public const string CpuPower = "cpu.power";
    public const string GpuTemp = "gpu.temp";
    public const string GpuClock = "gpu.clock";
    public const string GpuFan = "gpu.fan";
    public const string GpuLoad = "gpu.load";
    public const string DiskTemp = "disk.temp";

    /// <summary>One derived metric: how to read it from a snapshot and how to describe it.</summary>
    public sealed record Definition(
        string Id,
        Func<IReadOnlyList<CoolerSensor>, double?> Read,
        Func<double, MetricInfo> Describe);

    public static IReadOnlyList<Definition> All { get; } =
    [
        // Tctl on AMD, the package on Intel; else the hottest reading.
        new(CpuTemp,
            s => First(Of(s, Components.Cpu, Quantity.Temperature).Where(x => Says(x, "Tctl") || Says(x, "Package")))
                 ?? Max(Of(s, Components.Cpu, Quantity.Temperature)),
            tj => new MetricInfo(MetricFormat.Temperature, 30, tj, ThresholdKind.CpuTemperature)),
        // The fastest core.
        new(CpuClock,
            s => Named(Of(s, Components.Cpu, Quantity.Clock), "CPU Freq Max") ?? Max(Of(s, Components.Cpu, Quantity.Clock)),
            _ => new MetricInfo(MetricFormat.ClockMhz, 800, 5800, Smooth: true, GrowToPeak: true)),
        new(CpuFan,
            CpuFanSpeed,
            _ => new MetricInfo(MetricFormat.Rpm, 0, 2500, ThresholdKind.CpuFanStall, GrowToPeak: true)),
        new(CpuLoad,
            s => Named(Of(s, Components.Cpu, Quantity.Load), "CPU Load") ?? First(Of(s, Components.Cpu, Quantity.Load)),
            _ => new MetricInfo(MetricFormat.Percent, 0, 100, Smooth: true)),
        new(CpuPower,
            s => First(Of(s, Components.Cpu, Quantity.Power)),
            _ => new MetricInfo(MetricFormat.Watt, 0, 100, GrowToPeak: true)),

        new(GpuTemp,
            s => First(OfGpu(s, Quantity.Temperature).Where(SensorMetrics.IsGpuCore)) ?? First(OfGpu(s, Quantity.Temperature)),
            _ => new MetricInfo(MetricFormat.Temperature, 30, 95, ThresholdKind.GpuTemperature)),
        // "GPU Freq" on AMD; NVIDIA reports its graphics clock first.
        new(GpuClock,
            s => Named(OfGpu(s, Quantity.Clock), "GPU Freq") ?? First(OfGpu(s, Quantity.Clock)),
            _ => new MetricInfo(MetricFormat.ClockMhz, 200, 3200, Smooth: true, GrowToPeak: true)),
        new(GpuFan,
            s => First(OfGpu(s, Quantity.FanSpeed)),
            _ => new MetricInfo(MetricFormat.Rpm, 0, 3300, ThresholdKind.GpuFanStall, GrowToPeak: true)),
        new(GpuLoad,
            s => Named(OfGpu(s, Quantity.Load), "GPU Load") ?? First(OfGpu(s, Quantity.Load)),
            _ => new MetricInfo(MetricFormat.Percent, 0, 100, Smooth: true)),

        // All drives: the hottest one.
        new(DiskTemp,
            s => Max(Of(s, Components.Storage, Quantity.Temperature)),
            _ => new MetricInfo(MetricFormat.Temperature, 20, 80, ThresholdKind.StorageTemperature))
    ];

    private static List<CoolerSensor> Of(IReadOnlyList<CoolerSensor> sensors, string component, Quantity quantity) =>
        sensors.Where(s => s.Quantity == quantity && Components.Of(s) == component).ToList();

    private static List<CoolerSensor> OfGpu(IReadOnlyList<CoolerSensor> sensors, Quantity quantity)
    {
        if (Components.PrimaryGpu(sensors) is not { } gpu)
            return [];

        return sensors.Where(s => s.DeviceUid == gpu && s.Quantity == quantity).ToList();
    }

    /// <summary>A fan named for the CPU, else the pump of a water cooler, else the first mainboard fan.</summary>
    private static double? CpuFanSpeed(IReadOnlyList<CoolerSensor> sensors)
    {
        List<CoolerSensor> fans = sensors
            .Where(s => s.Quantity == Quantity.FanSpeed && Components.Of(s) != Components.Gpu)
            .ToList();
        return First(fans.Where(s => Says(s, "CPU")))
               ?? First(fans.Where(s => Says(s, "Pump")))
               ?? First(fans.Where(s => Components.Of(s) == Components.Mainboard));
    }

    /// <summary>Whether the label or the name holds <paramref name="word"/> as a whole word
    /// ("CPU_FAN", "Pump", "CPU Temp Tctl").</summary>
    private static bool Says(CoolerSensor sensor, string word) => HasWord(sensor.Label, word) || HasWord(sensor.Name, word);

    private static bool HasWord(string text, string word) =>
        Regex.IsMatch(text, $@"(?<![A-Za-z]){Regex.Escape(word)}(?![A-Za-z])", RegexOptions.IgnoreCase);

    private static double? Named(IEnumerable<CoolerSensor> sensors, string name) =>
        Value(sensors.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));

    private static double? First(IEnumerable<CoolerSensor> sensors) => Value(sensors.FirstOrDefault());

    private static double? Max(IEnumerable<CoolerSensor> sensors)
    {
        double? max = null;
        foreach (CoolerSensor sensor in sensors)
        {
            if (Value(sensor) is { } value && (max is null || value > max))
                max = value;
        }

        return max;
    }

    /// <summary>The reading's native value, or null when it has none.</summary>
    private static double? Value(CoolerSensor? sensor)
    {
        if (sensor is null)
            return null;

        double value = SensorMetrics.NativeValue(sensor);
        return double.IsNaN(value) ? null : value;
    }
}
