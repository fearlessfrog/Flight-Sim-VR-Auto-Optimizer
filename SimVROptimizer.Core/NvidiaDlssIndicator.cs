using Microsoft.Win32;

namespace SimVROptimizer.Core;

public sealed record NvidiaDlssIndicatorState(bool Available, bool Enabled, uint RawValue, string Status);

public static class NvidiaDlssIndicator
{
    public const string RegistryPath = @"SOFTWARE\NVIDIA Corporation\Global\NGXCore";
    public const string RegistryValueName = "ShowDlssIndicator";
    public const uint EnabledValue = 1024;
    public const uint DisabledValue = 0;

    public static NvidiaDlssIndicatorState Read()
    {
        if (!OperatingSystem.IsWindows())
            return new(false, false, 0, "The NVIDIA DLSS information overlay is available only on Windows.");

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(RegistryPath, writable: false);
            if (key is null)
                return new(false, false, 0, "The NVIDIA NGX driver registry key was not found.");

            var rawValue = ToUInt32(key.GetValue(RegistryValueName, DisabledValue));
            return new(true, IsEnabledValue(rawValue), rawValue,
                IsEnabledValue(rawValue)
                    ? "DLSS information overlay is ON globally."
                    : "DLSS information overlay is OFF globally.");
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return new(false, false, 0, "The NVIDIA DLSS information overlay state could not be read: " + exception.Message);
        }
    }

    public static NvidiaDlssIndicatorState SetEnabled(bool enabled)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The NVIDIA DLSS information overlay is available only on Windows.");

        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var existingKey = baseKey.OpenSubKey(RegistryPath, writable: false)
            ?? throw new InvalidOperationException("The NVIDIA NGX driver registry key was not found.");
        existingKey.Close();

        using var key = baseKey.OpenSubKey(RegistryPath, writable: true)
            ?? throw new UnauthorizedAccessException("Administrator access is required to change the NVIDIA DLSS information overlay.");
        var value = enabled ? EnabledValue : DisabledValue;
        key.SetValue(RegistryValueName, unchecked((int)value), RegistryValueKind.DWord);
        key.Flush();

        var verified = ToUInt32(key.GetValue(RegistryValueName, DisabledValue));
        if (verified != value)
            throw new IOException("The NVIDIA DLSS information overlay registry value could not be verified.");
        return new(true, enabled, verified,
            enabled ? "DLSS information overlay is ON globally." : "DLSS information overlay is OFF globally.");
    }

    public static bool IsEnabledValue(uint value) => value > 0;

    private static uint ToUInt32(object? value)
    {
        try { return Convert.ToUInt32(value); }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException) { return 0; }
    }
}
