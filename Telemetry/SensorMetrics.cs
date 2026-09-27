namespace LoupixDeck.Plugin.CoolerControl.Telemetry;

/// <summary>
/// Describes a single CoolerControl reading as a metric: how to format it and which alert rule
/// applies. Decided by the <see cref="Quantity"/> and the component (<see cref="Components"/>).
/// Used for every reading, so any sensor a user puts on a tile gets a history and a state.
/// </summary>
internal static class SensorMetrics
{
    /// <summary>The value to track. The daemon reports every quantity in the unit
    /// <see cref="Describe"/> formats: °C, %, RPM, MHz and W.</summary>
    public static double NativeValue(CoolerSensor sensor) => sensor.Value;

    public static MetricInfo Describe(CoolerSensor sensor, double tjMax)
    {
        string component = Components.Of(sensor);
        return sensor.Quantity switch
        {
            Quantity.Temperature => Temperature(sensor, component, tjMax),

            Quantity.Load => new MetricInfo(MetricFormat.Percent, 0, 100, Smooth: true),
            Quantity.FanDuty => new MetricInfo(MetricFormat.Percent, 0, 100),

            Quantity.FanSpeed when component == Components.Gpu =>
                new MetricInfo(MetricFormat.Rpm, 0, 3300, ThresholdKind.GpuFanStall, GrowToPeak: true),
            Quantity.FanSpeed =>
                new MetricInfo(MetricFormat.Rpm, 0, 3000, ThresholdKind.CpuFanStall, GrowToPeak: true),

            Quantity.Clock when component == Components.Gpu =>
                new MetricInfo(MetricFormat.ClockMhz, 0, 3200, Smooth: true, GrowToPeak: true),
            Quantity.Clock => new MetricInfo(MetricFormat.ClockMhz, 0, 6000, Smooth: true, GrowToPeak: true),

            Quantity.Power => new MetricInfo(MetricFormat.Watt, 0, 100, GrowToPeak: true),

            _ => new MetricInfo(MetricFormat.Generic, 0, 0)
        };
    }

    private static MetricInfo Temperature(CoolerSensor sensor, string component, double tjMax) => component switch
    {
        Components.Cpu => new MetricInfo(MetricFormat.Temperature, 30, tjMax, ThresholdKind.CpuTemperature),
        // Only the GPU core has the design's 80/88 limits; hot spot and memory run hotter by design.
        Components.Gpu => new MetricInfo(MetricFormat.Temperature, 30, 95,
            IsGpuCore(sensor) ? ThresholdKind.GpuTemperature : ThresholdKind.None),
        Components.Storage => new MetricInfo(MetricFormat.Temperature, 20, 80, ThresholdKind.StorageTemperature),
        _ => new MetricInfo(MetricFormat.Temperature, 20, 100)
    };

    /// <summary>The GPU core temperature: "GPU Temp" on NVIDIA, the "edge" sensor on AMD.</summary>
    public static bool IsGpuCore(CoolerSensor sensor) =>
        sensor.Quantity == Quantity.Temperature
        && (sensor.Name.Equals("GPU Temp", StringComparison.OrdinalIgnoreCase)
            || sensor.Label.Equals("GPU Temp", StringComparison.OrdinalIgnoreCase)
            || sensor.Label.Contains("edge", StringComparison.OrdinalIgnoreCase));
}
