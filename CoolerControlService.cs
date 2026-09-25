using Newtonsoft.Json.Linq;

namespace LoupixDeck.Plugin.CoolerControl;

/// <summary>
/// Polls the CoolerControl daemon once a second for the status of every device and flattens it
/// into <see cref="CoolerSensor"/> readings. The device list (names, labels, which channels are
/// fans) comes from <c>/devices</c>, fetched on connect, when an unknown device shows up and every
/// <see cref="DeviceRefresh"/>. When the daemon is unreachable the service reports
/// <see cref="IsAvailable"/> == false and keeps retrying.
/// </summary>
public sealed class CoolerControlService(CoolerControlApiController controller)
{
    private static readonly TimeSpan PollDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan DeviceRefresh = TimeSpan.FromSeconds(30);

    /// <summary>Order of the device types in the snapshot and so in the menu.</summary>
    private static readonly string[] TypeOrder = ["CPU", "GPU", "Liquidctl", "Hwmon", "CustomSensors", "ServicePlugin"];

    private CancellationTokenSource? _cts;
    private Task? _pollTask;

    private Dictionary<string, DeviceInfo> _devices = [];
    private DateTime _devicesFetched = DateTime.MinValue;

    private volatile IReadOnlyList<CoolerSensor> _sensors = [];
    private volatile bool _isAvailable;
    private volatile string? _lastError;

    public IReadOnlyList<CoolerSensor> Sensors => _sensors;

    public bool IsAvailable => _isAvailable;

    /// <summary>Why the last poll failed, or null after a successful one.</summary>
    public string? LastError => _lastError;

    public void Start()
    {
        if (_pollTask != null)
            return;

        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;
        _pollTask = Task.Run(() => PollLoop(token), token);
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _pollTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _pollTask = null;
        _cts?.Dispose();
        _cts = null;
        _isAvailable = false;
        _sensors = [];
    }

    /// <summary>Forgets the device list, so the next poll reads it again (after a settings change).</summary>
    public void Reset() => _devicesFetched = DateTime.MinValue;

    private async Task PollLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            bool ok = false;
            try
            {
                _sensors = await FetchAsync().ConfigureAwait(false);
                _isAvailable = true;
                _lastError = null;
                ok = true;
            }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested)
                    return;

                if (_lastError != ex.Message)
                    Console.WriteLine($"CoolerControlService: status failed, will retry ({ex.Message}).");
                _lastError = ex.Message;
            }

            if (!ok)
            {
                _isAvailable = false;
                _sensors = [];
                Reset();
            }

            try { await Task.Delay(ok ? PollDelay : ReconnectDelay, token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task<IReadOnlyList<CoolerSensor>> FetchAsync()
    {
        JArray status = await controller.GetStatus().ConfigureAwait(false);

        bool unknownDevice = status.Any(d => !_devices.ContainsKey(d.Value<string>("uid") ?? string.Empty));
        if (unknownDevice || DateTime.UtcNow - _devicesFetched > DeviceRefresh)
        {
            _devices = ParseDevices(await controller.GetDevices().ConfigureAwait(false));
            _devicesFetched = DateTime.UtcNow;
        }

        List<(DeviceInfo Device, JToken Latest)> devices = [];
        foreach (JToken device in status)
        {
            string uid = device.Value<string>("uid") ?? string.Empty;
            JToken? latest = (device["status_history"] as JArray)?.LastOrDefault();
            if (latest is not null && _devices.TryGetValue(uid, out DeviceInfo? info))
                devices.Add((info, latest));
        }

        List<CoolerSensor> sensors = [];
        foreach ((DeviceInfo device, JToken latest) in devices
                     .OrderBy(d => TypeRank(d.Device.Type))
                     .ThenBy(d => d.Device.TypeIndex))
        {
            AddReadings(device, latest, sensors);
        }

        return sensors;
    }

    private static void AddReadings(DeviceInfo device, JToken latest, List<CoolerSensor> sensors)
    {
        foreach (JToken temp in latest["temps"] as JArray ?? [])
        {
            string name = temp.Value<string>("name") ?? string.Empty;
            if (temp["temp"]?.Type is JTokenType.Float or JTokenType.Integer)
                sensors.Add(Reading(device, name, "temp", device.TempLabel(name), Quantity.Temperature, temp.Value<double>("temp")));
        }

        foreach (JToken channel in latest["channels"] as JArray ?? [])
        {
            string name = channel.Value<string>("name") ?? string.Empty;
            string label = device.ChannelLabel(name);
            bool fan = device.IsFan(name) || channel["rpm"]?.Type == JTokenType.Integer;

            // A fan's speed first, then its duty; the menu keeps them side by side.
            AddValue("rpm", Quantity.FanSpeed);
            AddValue("duty", fan ? Quantity.FanDuty : Quantity.Load);
            AddValue("freq", Quantity.Clock);
            AddValue("watts", Quantity.Power);

            void AddValue(string field, Quantity quantity)
            {
                if (channel[field]?.Type is JTokenType.Float or JTokenType.Integer)
                    sensors.Add(Reading(device, name, field, label, quantity, channel.Value<double>(field)));
            }
        }
    }

    private static CoolerSensor Reading(DeviceInfo device, string name, string field, string label,
        Quantity quantity, double value) =>
        new($"{device.Uid}:{field}:{name}", device.Uid, device.Type, device.Name, device.Model, name, label,
            quantity, value);

    private static int TypeRank(string type)
    {
        int rank = Array.IndexOf(TypeOrder, type);
        return rank >= 0 ? rank : TypeOrder.Length;
    }

    private static Dictionary<string, DeviceInfo> ParseDevices(JArray devices)
    {
        Dictionary<string, DeviceInfo> result = new(StringComparer.Ordinal);
        foreach (JToken device in devices)
        {
            string uid = device.Value<string>("uid") ?? string.Empty;
            JToken? info = device["info"];

            Dictionary<string, string> temps = [];
            foreach (JProperty temp in (info?["temps"] as JObject)?.Properties() ?? [])
                temps[temp.Name] = temp.Value.Value<string>("label") ?? string.Empty;

            Dictionary<string, (string Label, bool Fan)> channels = [];
            foreach (JProperty channel in (info?["channels"] as JObject)?.Properties() ?? [])
            {
                string label = channel.Value["label"]?.Type == JTokenType.String
                    ? channel.Value.Value<string>("label") ?? string.Empty
                    : string.Empty;
                bool fan = channel.Value["speed_options"] is JObject;
                channels[channel.Name] = (label, fan);
            }

            result[uid] = new DeviceInfo(
                uid,
                device.Value<string>("d_type") ?? string.Empty,
                device["type_index"]?.Type == JTokenType.Integer ? device.Value<int>("type_index") : 0,
                (device.Value<string>("name") ?? string.Empty).Trim(),
                info?["model"]?.Type == JTokenType.String ? (info.Value<string>("model") ?? string.Empty).Trim() : string.Empty,
                temps,
                channels);
        }

        return result;
    }

    /// <summary>What <c>/devices</c> says about one device.</summary>
    private sealed record DeviceInfo(
        string Uid,
        string Type,
        int TypeIndex,
        string Name,
        string Model,
        Dictionary<string, string> Temps,
        Dictionary<string, (string Label, bool Fan)> Channels)
    {
        public string TempLabel(string name) =>
            Temps.GetValueOrDefault(name) is { Length: > 0 } label ? label.Trim() : name;

        public string ChannelLabel(string name) =>
            Channels.TryGetValue(name, out (string Label, bool Fan) channel) && channel.Label.Length > 0
                ? channel.Label.Trim()
                : name;

        /// <summary>A channel CoolerControl can drive at a speed (fans, pumps) or named like one.</summary>
        public bool IsFan(string name) =>
            (Channels.TryGetValue(name, out (string Label, bool Fan) channel) && channel.Fan)
            || name.StartsWith("fan", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("pump", StringComparison.OrdinalIgnoreCase);
    }
}
