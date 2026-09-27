using LoupixDeck.Plugin.CoolerControl.Rendering.Tiles;
using LoupixDeck.Plugin.CoolerControl.Telemetry;

namespace LoupixDeck.Plugin.CoolerControl.Rendering;

/// <summary>
/// Turns a persisted <c>CoolerControl.Sensor</c> command parameter into the <see cref="SensorRow"/>
/// a tile draws. The parameter is the reading's key (<see cref="CoolerSensor.Key"/>). Values, units,
/// history and alert state come from the <see cref="TelemetrySampler"/>, which tracks every reading
/// under that same key.
/// </summary>
internal static class CoolerReadingBuilder
{
    private const string Fallback = "COOLERCTL";

    public static SensorRow Build(string? parameter, IReadOnlyList<CoolerSensor> sensors)
    {
        string key = parameter?.Trim() ?? string.Empty;
        CoolerSensor? sensor = key.Length == 0 ? null : sensors.FirstOrDefault(s => s.Key == key);
        if (sensor is null)
            return Placeholder(Fallback);

        // The menu's name for the reading; a reading the menu does not offer keeps its own label.
        if (TileLabels.For(sensors, key) is { } labels)
            return new SensorRow(labels.Header, labels.Short, key);

        return new SensorRow(sensor.Label, sensor.Label, MetricKeys.ForSensor(sensor));
    }

    private static SensorRow Placeholder(string header) => new(header, header, null);
}
