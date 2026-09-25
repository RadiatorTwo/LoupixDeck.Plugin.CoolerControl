namespace LoupixDeck.Plugin.CoolerControl;

/// <summary>What a reading measures.</summary>
public enum Quantity
{
    /// <summary>A temperature in °C (a device's <c>temps</c>).</summary>
    Temperature,
    /// <summary>The <c>duty</c> of a channel that is no fan: CPU or GPU load in %.</summary>
    Load,
    /// <summary>The <c>duty</c> of a fan or pump channel: its speed setting in %.</summary>
    FanDuty,
    /// <summary>The <c>rpm</c> of a fan or pump channel.</summary>
    FanSpeed,
    /// <summary>The <c>freq</c> of a channel in MHz.</summary>
    Clock,
    /// <summary>The <c>watts</c> of a channel.</summary>
    Power
}

/// <summary>
/// One reading of the CoolerControl daemon: a temperature of a device, or one value of a device's
/// channel (a channel can carry several — a fan reports its speed and its duty).
/// </summary>
/// <param name="Key">The reference buttons store: <c>DeviceUid:Field:Name</c>, where Field is the
/// daemon's name of the value (<c>temp</c>, <c>duty</c>, <c>rpm</c>, <c>freq</c>, <c>watts</c>) and
/// Name is the temperature's or channel's name ("temp1", "fan2", "CPU Load"). The device UID is a
/// hash the daemon keeps stable across restarts.</param>
/// <param name="DeviceUid">The UID of the device.</param>
/// <param name="DeviceType">The daemon's device type: CPU, GPU, Liquidctl, Hwmon, CustomSensors or
/// ServicePlugin.</param>
/// <param name="DeviceName">The device's name: the product for CPUs, GPUs and liquidctl devices,
/// the hwmon driver ("nct6798", "nvme") for hwmon devices.</param>
/// <param name="DeviceModel">The model the daemon found for a hwmon device ("Samsung SSD 980 PRO
/// 1TB"), else empty.</param>
/// <param name="Name">The temperature's or channel's name.</param>
/// <param name="Label">The label CoolerControl shows ("CPU Temp Tctl", "Pump"); the name when the
/// device gives none.</param>
public sealed record CoolerSensor(
    string Key,
    string DeviceUid,
    string DeviceType,
    string DeviceName,
    string DeviceModel,
    string Name,
    string Label,
    Quantity Quantity,
    double Value);
