using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SimVROptimizer.Core;

public sealed record SupportPackageResult(string Path, int FileCount, long SizeBytes);

public sealed record SupportPackageContext(
    AppConfig Configuration,
    CpuProfile? Cpu,
    IReadOnlyList<RunningAppCandidate> Applications,
    IReadOnlyList<ServiceCandidate> Services,
    string OptimizerVersion);

public sealed record SupportWorkload(
    string Name,
    string DisplayName,
    bool Selected,
    string Guidance,
    string Impact,
    string Identity,
    string Reason,
    string Safety);

/// <summary>Creates a privacy-scrubbed, text-only troubleshooting archive.</summary>
public sealed class SupportPackageExporter
{
    private const long MaximumSourceBytes = 8 * 1024 * 1024;
    private readonly AppPaths _paths;
    private readonly SupportPrivacyScrubber _scrubber;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public SupportPackageExporter(AppPaths paths, SupportPrivacyScrubber? scrubber = null)
    {
        _paths = paths;
        _scrubber = scrubber ?? SupportPrivacyScrubber.ForCurrentUser();
    }

    public async Task<SupportPackageResult> ExportAsync(
        string destinationPath,
        SupportPackageContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationPath))!);
        if (File.Exists(destinationPath)) File.Delete(destinationPath);

        var fileCount = 0;
        await using (var file = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: false))
        {
            await AddTextAsync(archive, "README.txt", BuildReadme(), cancellationToken);
            fileCount++;
            await AddJsonAsync(archive, "support-summary.json", BuildSummary(context), cancellationToken);
            fileCount++;
            await AddJsonAsync(archive, "configuration.json", context.Configuration, cancellationToken);
            fileCount++;
            await AddJsonAsync(archive, "hardware-and-drivers.json", BuildHardware(context), cancellationToken);
            fileCount++;
            await AddJsonAsync(archive, "detected-workloads.json", BuildWorkloads(context), cancellationToken);
            fileCount++;

            fileCount += await AddExistingFileAsync(archive, _paths.RestorationReportFile,
                "restoration/last-restoration-report.json", cancellationToken);
            fileCount += await AddExistingFileAsync(archive, _paths.JournalFile,
                "restoration/active-session.json", cancellationToken);
            fileCount += await AddExistingFileAsync(archive, _paths.PerformanceHistoryFile,
                "history/performance-history.json", cancellationToken);

            foreach (var logPath in _paths.LogFiles)
                fileCount += await AddExistingFileAsync(archive, logPath,
                    "logs/" + Path.GetFileName(logPath), cancellationToken);

            if (Directory.Exists(_paths.TelemetryDirectory))
            {
                foreach (var telemetryPath in Directory.EnumerateFiles(_paths.TelemetryDirectory, "*.csv")
                             .Select(path => new FileInfo(path))
                             .OrderByDescending(info => info.LastWriteTimeUtc)
                             .Take(3)
                             .Select(info => info.FullName))
                    fileCount += await AddExistingFileAsync(archive, telemetryPath,
                        "telemetry/" + Path.GetFileName(telemetryPath), cancellationToken);
            }
        }

        return new(destinationPath, fileCount, new FileInfo(destinationPath).Length);
    }

    private static object BuildSummary(SupportPackageContext context) => new
    {
        PackageFormat = 1,
        CreatedAtUtc = DateTimeOffset.UtcNow,
        context.OptimizerVersion,
        SelectedSimulator = context.Configuration.SelectedSimulatorId ?? "Not selected",
        ActiveProfile = context.Configuration.ActiveSavedProfileName ?? "No named profile",
        ApplicationCount = context.Applications.Count,
        SelectedApplicationCount = context.Applications.Count(item => item.Selected),
        ServiceCount = context.Services.Count,
        SelectedServiceCount = context.Services.Count(item => item.Selected),
        Privacy = "Usernames, computer names and personal Windows folder paths were replaced before export."
    };

    private static object BuildHardware(SupportPackageContext context)
    {
        OpenXrDiagnosticReport? openXr = null;
        try { openXr = OpenXrDiagnostics.Read(); } catch { }
        return new
        {
            OperatingSystem = RuntimeInformation.OSDescription,
            OSArchitecture = RuntimeInformation.OSArchitecture.ToString(),
            ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            DotNetRuntime = RuntimeInformation.FrameworkDescription,
            Environment.ProcessorCount,
            Cpu = context.Cpu,
            DisplayDrivers = GpuDriverInfoReader.Read(),
            OpenXr = openXr
        };
    }

    private static object BuildWorkloads(SupportPackageContext context) => new
    {
        Applications = context.Applications.Select(item => new SupportWorkload(
            item.ProcessName, item.DisplayName, item.Selected, item.ClassificationLabel,
            item.ImpactLabel, item.IdentityLabel, item.ClassificationReason, item.RestartSafetyDetail)).ToArray(),
        Services = context.Services.Select(item => new SupportWorkload(
            item.ServiceName, item.DisplayName, item.Selected, item.ClassificationLabel,
            item.ImpactLabel, item.IdentityLabel, item.ClassificationReason, item.DependencyDetails)).ToArray()
    };

    private string BuildReadme() => _scrubber.Scrub($"""
        VR Auto-Optimizer support package
        Created: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}

        This archive contains diagnostic text only. It does not contain screenshots, saved credentials,
        registry exports or executable files. Usernames, the computer name and personal Windows folder
        paths were replaced automatically before export. Review the files before sharing if desired.
        """);

    private async Task AddJsonAsync(ZipArchive archive, string entryName, object value, CancellationToken cancellationToken) =>
        await AddTextAsync(archive, entryName, JsonSerializer.Serialize(value, JsonOptions), cancellationToken).ConfigureAwait(false);

    private async Task<int> AddExistingFileAsync(
        ZipArchive archive,
        string sourcePath,
        string entryName,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(sourcePath)) return 0;
        try
        {
            await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var bytesToRead = (int)Math.Min(source.Length, MaximumSourceBytes);
            var buffer = new byte[bytesToRead];
            var total = 0;
            while (total < buffer.Length)
            {
                var read = await source.ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                total += read;
            }
            var text = Encoding.UTF8.GetString(buffer, 0, total);
            if (source.Length > MaximumSourceBytes)
                text += $"{Environment.NewLine}[TRUNCATED: source exceeded {MaximumSourceBytes / 1024 / 1024} MB]";
            await AddTextAsync(archive, entryName, text, cancellationToken).ConfigureAwait(false);
            return 1;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    private async Task AddTextAsync(ZipArchive archive, string entryName, string text, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteAsync(_scrubber.Scrub(text).AsMemory(), cancellationToken).ConfigureAwait(false);
    }
}

public sealed class SupportPrivacyScrubber
{
    private readonly string _userName;
    private readonly string _computerName;
    private readonly IReadOnlyList<(string Path, string Replacement)> _personalPaths;

    public SupportPrivacyScrubber(string userName, string computerName, IEnumerable<(string Path, string Replacement)> personalPaths)
    {
        _userName = userName;
        _computerName = computerName;
        _personalPaths = personalPaths
            .Where(item => !string.IsNullOrWhiteSpace(item.Path))
            .OrderByDescending(item => item.Path.Length)
            .ToArray();
    }

    public static SupportPrivacyScrubber ForCurrentUser()
    {
        var paths = new List<(string, string)>();
        Add(Environment.SpecialFolder.Desktop, "<DESKTOP>");
        Add(Environment.SpecialFolder.MyDocuments, "<DOCUMENTS>");
        Add(Environment.SpecialFolder.LocalApplicationData, "<LOCALAPPDATA>");
        Add(Environment.SpecialFolder.ApplicationData, "<APPDATA>");
        Add(Environment.SpecialFolder.UserProfile, "<USERPROFILE>");
        return new(Environment.UserName, Environment.MachineName, paths);

        void Add(Environment.SpecialFolder folder, string replacement)
        {
            var path = Environment.GetFolderPath(folder);
            if (!string.IsNullOrWhiteSpace(path)) paths.Add((path, replacement));
        }
    }

    public string Scrub(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var scrubbed = value;
        foreach (var item in _personalPaths)
        {
            scrubbed = ReplaceInsensitive(scrubbed, item.Path, item.Replacement);
            scrubbed = ReplaceInsensitive(scrubbed, item.Path.Replace("\\", "\\\\"), item.Replacement);
            scrubbed = ReplaceInsensitive(scrubbed, item.Path.Replace('\\', '/'), item.Replacement);
        }

        scrubbed = Regex.Replace(scrubbed,
            @"(?i)([A-Z]:[\\/]+Users[\\/]+)[^\\/\r\n\""']+", "$1<USER>");
        if (!string.IsNullOrWhiteSpace(_userName))
            scrubbed = Regex.Replace(scrubbed, $@"(?i)(?<![\p{{L}}\p{{N}}]){Regex.Escape(_userName)}(?![\p{{L}}\p{{N}}])", "<USER>");
        if (!string.IsNullOrWhiteSpace(_computerName))
            scrubbed = Regex.Replace(scrubbed, $@"(?i)(?<![\p{{L}}\p{{N}}]){Regex.Escape(_computerName)}(?![\p{{L}}\p{{N}}])", "<COMPUTER>");
        return scrubbed;
    }

    private static string ReplaceInsensitive(string value, string oldValue, string newValue) =>
        value.Replace(oldValue, newValue, StringComparison.OrdinalIgnoreCase);
}
