using Microsoft.Win32;

namespace SimVROptimizer.Core;

public sealed record GpuDriverInfo(
    string Vendor,
    string Name,
    string InstalledVersion,
    string DisplayVersion,
    string DriverDate);

/// <summary>Reads installed NVIDIA and AMD display-adapter driver details without changing system state.</summary>
public static class GpuDriverInfoReader
{
    private const string DisplayClassPath = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    public static IReadOnlyList<GpuDriverInfo> Read()
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<GpuDriverInfo>();

        var results = new List<GpuDriverInfo>();
        try
        {
            using var displayClass = Registry.LocalMachine.OpenSubKey(DisplayClassPath);
            if (displayClass is null) return results;

            foreach (var keyName in displayClass.GetSubKeyNames().Where(IsAdapterKey))
            {
                try
                {
                    using var adapter = displayClass.OpenSubKey(keyName);
                    if (adapter is null) continue;
                    var deviceId = adapter.GetValue("MatchingDeviceId") as string ?? string.Empty;
                    var provider = adapter.GetValue("ProviderName") as string ?? string.Empty;
                    var vendor = DetectVendor(deviceId, provider);
                    if (vendor is null) continue;

                    var name = adapter.GetValue("DriverDesc") as string;
                    var version = adapter.GetValue("DriverVersion") as string;
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(version)) continue;
                    var date = adapter.GetValue("DriverDate") as string ?? "Unknown date";
                    results.Add(new(vendor, name.Trim(), version.Trim(),
                        FormatDriverVersion(vendor, version), date.Trim()));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }

        return results
            .DistinctBy(item => (item.Vendor, item.Name, item.InstalledVersion))
            .OrderBy(item => item.Vendor.Equals("NVIDIA", StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string FormatDriverVersion(string vendor, string installedVersion)
    {
        var value = installedVersion.Trim();
        if (!vendor.Equals("NVIDIA", StringComparison.OrdinalIgnoreCase)) return value;

        var parts = value.Split('.');
        if (parts.Length == 4
            && int.TryParse(parts[2], out var branch)
            && int.TryParse(parts[3], out var build))
        {
            var release = (branch % 10 * 10000) + build;
            return $"{release / 100}.{release % 100:00}";
        }
        return value;
    }

    private static bool IsAdapterKey(string name) => name.Length == 4 && name.All(char.IsDigit);

    private static string? DetectVendor(string deviceId, string provider)
    {
        if (deviceId.Contains("VEN_10DE", StringComparison.OrdinalIgnoreCase)
            || provider.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)) return "NVIDIA";
        if (deviceId.Contains("VEN_1002", StringComparison.OrdinalIgnoreCase)
            || provider.Contains("AMD", StringComparison.OrdinalIgnoreCase)
            || provider.Contains("Advanced Micro Devices", StringComparison.OrdinalIgnoreCase)) return "AMD";
        return null;
    }
}
