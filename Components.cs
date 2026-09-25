namespace LoupixDeck.Plugin.CoolerControl;

/// <summary>
/// Sorts CoolerControl devices into the components the menu and the pages use. CPUs, GPUs and
/// liquidctl devices (AIO coolers, fan hubs) come with their own device type; hwmon devices are
/// told apart by their kernel driver's name ("nvme", "drivetemp", "nct6798", "spd5118" …), never
/// by a label, which the user may rename.
/// </summary>
internal static class Components
{
    public const string Cpu = "CPU";
    public const string Gpu = "GPU";
    public const string Memory = "Memory";
    public const string Storage = "Storage";
    public const string Mainboard = "Mainboard";
    public const string Cooling = "Cooling";
    public const string Network = "Network";
    public const string Other = "Other";

    public static readonly string[] Order = [Cpu, Gpu, Memory, Storage, Mainboard, Cooling, Network, Other];

    private static readonly string[] StorageDrivers = ["nvme", "drivetemp"];

    private static readonly string[] MemoryDrivers = ["spd5118", "jc42", "ee1004"];

    /// <summary>Super I/O chips, board vendors' embedded controllers and the ACPI thermal zone.</summary>
    private static readonly string[] MainboardDrivers =
    [
        "nct", "it8", "w83", "f71", "f75", "sch5", "asus", "gigabyte", "dell", "hp", "thinkpad",
        "acpitz", "macsmc"
    ];

    /// <summary>Kernel drivers of fan hubs, pumps and water-cooling controllers.</summary>
    private static readonly string[] CoolingDrivers =
    [
        "nzxt", "corsair", "aquacomputer", "d5next", "farbwerk", "octo", "quadro", "highflow", "leakshield",
        "kraken", "gigabyte_waterforce", "asus_rog_ryujin"
    ];

    private static readonly string[] NetworkDrivers =
        ["iwlwifi", "r8169", "r8125", "igc", "igb", "e1000", "atlantic", "mt79", "mt76", "ath1", "rtw", "ixgbe", "bnxt"];

    public static string Of(CoolerSensor sensor) => sensor.DeviceType switch
    {
        "CPU" => Cpu,
        "GPU" => Gpu,
        "Liquidctl" => Cooling,
        "Hwmon" => OfDriver(sensor.DeviceName),
        _ => Other
    };

    private static string OfDriver(string driver)
    {
        if (StartsWithAny(driver, StorageDrivers))
            return Storage;
        if (StartsWithAny(driver, MemoryDrivers))
            return Memory;
        // Before the mainboard: "gigabyte_waterforce" and "asus_rog_ryujin" are coolers.
        if (StartsWithAny(driver, CoolingDrivers))
            return Cooling;
        if (StartsWithAny(driver, MainboardDrivers))
            return Mainboard;
        if (StartsWithAny(driver, NetworkDrivers))
            return Network;
        return Other;
    }

    /// <summary>The name of a device in the menu: the model where the daemon found one (drives),
    /// else the device's name.</summary>
    public static string DeviceName(CoolerSensor sensor) =>
        sensor.DeviceModel.Length > 0 ? sensor.DeviceModel : sensor.DeviceName;

    /// <summary>The GPU the pages show: a discrete card before an Intel one, else the first GPU.</summary>
    public static string? PrimaryGpu(IReadOnlyList<CoolerSensor> sensors)
    {
        string? first = null;
        foreach (CoolerSensor sensor in sensors)
        {
            if (Of(sensor) != Gpu)
                continue;

            first ??= sensor.DeviceUid;
            if (!sensor.DeviceName.Contains("Intel", StringComparison.OrdinalIgnoreCase))
                return sensor.DeviceUid;
        }

        return first;
    }

    private static bool StartsWithAny(string driver, string[] prefixes) =>
        prefixes.Any(prefix => driver.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}
