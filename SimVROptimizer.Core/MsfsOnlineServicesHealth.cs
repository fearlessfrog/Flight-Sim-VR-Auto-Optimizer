using System.Net;
using System.Net.NetworkInformation;

namespace SimVROptimizer.Core;

public sealed record OnlineHealthItem(string Component, DiagnosticHealth Health, string State, string Detail);

public sealed record MsfsOnlineHealthReport(
    DateTimeOffset CheckedAt,
    DiagnosticHealth Health,
    string Summary,
    IReadOnlyList<OnlineHealthItem> Items);

public sealed class MsfsOnlineServicesHealthChecker
{
    private static readonly string[] ServiceNames =
    [
        "GamingServices", "GamingServicesNet", "XblAuthManager", "XblGameSave", "XboxNetApiSvc", "TokenBroker", "wlidsvc"
    ];

    private static readonly string[] DnsHosts = ["login.live.com", "xboxlive.com"];
    private readonly ICommandRunner _commands;
    private readonly MsfsOfficialServiceStatusClient _officialStatus;

    public MsfsOnlineServicesHealthChecker(ICommandRunner commands, MsfsOfficialServiceStatusClient? officialStatus = null)
    {
        _commands = commands;
        _officialStatus = officialStatus ?? new MsfsOfficialServiceStatusClient();
    }

    public async Task<MsfsOnlineHealthReport> CheckAsync(CancellationToken cancellationToken = default)
    {
        var officialStatusTask = _officialStatus.CheckAsync(cancellationToken);
        var items = new List<OnlineHealthItem>();
        items.Add(NetworkInterface.GetIsNetworkAvailable()
            ? new("Windows network", DiagnosticHealth.Ready, "AVAILABLE", "Windows reports an active network connection.")
            : new("Windows network", DiagnosticHealth.Problem, "OFFLINE", "Windows does not report an active network connection."));

        foreach (var host in DnsHosts)
        {
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken)
                    .WaitAsync(TimeSpan.FromSeconds(4), cancellationToken).ConfigureAwait(false);
                items.Add(addresses.Length > 0
                    ? new($"DNS / {host}", DiagnosticHealth.Ready, "RESOLVED", $"Resolved to {addresses.Length} address(es).")
                    : new($"DNS / {host}", DiagnosticHealth.Review, "NO ADDRESS", "DNS returned no address."));
            }
            catch (Exception exception) when (exception is System.Net.Sockets.SocketException or TimeoutException)
            {
                items.Add(new($"DNS / {host}", DiagnosticHealth.Review, "FAILED", exception.Message));
            }
        }

        foreach (var serviceName in ServiceNames)
            items.Add(await CheckServiceAsync(serviceName, cancellationToken).ConfigureAwait(false));

        var officialStatus = await officialStatusTask.ConfigureAwait(false);
        items.InsertRange(0, officialStatus.Items);

        var health = items.Any(item => item.Health == DiagnosticHealth.Problem)
            ? DiagnosticHealth.Problem
            : items.Any(item => item.Health == DiagnosticHealth.Review)
                ? DiagnosticHealth.Review
                : DiagnosticHealth.Ready;
        var problems = items.Count(item => item.Health == DiagnosticHealth.Problem);
        var reviews = items.Count(item => item.Health == DiagnosticHealth.Review);
        var officialOperational = officialStatus.Items.Count == 4
            && officialStatus.Items.All(item => item.Health == DiagnosticHealth.Ready);
        var summary = health == DiagnosticHealth.Ready
            ? "Local Microsoft/Xbox connectivity is ready and official MSFS 2024 services report operational."
            : officialOperational
                ? $"Official MSFS 2024 services report operational; local checks found {problems} problem(s) and {reviews} item(s) to review."
            : $"Online-services check found {problems} problem(s) and {reviews} item(s) to review.";
        return new(DateTimeOffset.Now, health, summary, items);
    }

    public async Task<MsfsOnlineHealthReport> RepairSafeAsync(CancellationToken cancellationToken = default)
    {
        await _commands.RunAsync("ipconfig.exe", ["/flushdns"], cancellationToken).ConfigureAwait(false);
        foreach (var serviceName in ServiceNames)
        {
            var query = await _commands.RunAsync("sc.exe", ["query", serviceName], cancellationToken).ConfigureAwait(false);
            if (ParseServiceState(query.StandardOutput, query.StandardError, query.ExitCode) != "STOPPED") continue;
            var configuration = await _commands.RunAsync("sc.exe", ["qc", serviceName], cancellationToken).ConfigureAwait(false);
            if (IsDisabledService(configuration.StandardOutput)) continue;
            await _commands.RunAsync("sc.exe", ["start", serviceName], cancellationToken).ConfigureAwait(false);
        }
        return await CheckAsync(cancellationToken).ConfigureAwait(false);
    }

    public static string ParseServiceState(string standardOutput, string standardError, int exitCode)
    {
        var text = standardOutput + Environment.NewLine + standardError;
        if (text.Contains("1060", StringComparison.OrdinalIgnoreCase)
            || text.Contains("does not exist", StringComparison.OrdinalIgnoreCase)) return "NOT INSTALLED";
        if (text.Contains("RUNNING", StringComparison.OrdinalIgnoreCase)) return "RUNNING";
        if (text.Contains("STOPPED", StringComparison.OrdinalIgnoreCase)) return "STOPPED";
        return exitCode == 0 ? "INSTALLED" : "UNAVAILABLE";
    }

    public static bool IsDisabledService(string output) =>
        output.Contains("DISABLED", StringComparison.OrdinalIgnoreCase)
        || output.Contains("START_TYPE", StringComparison.OrdinalIgnoreCase)
            && output.Contains(" 4 ", StringComparison.OrdinalIgnoreCase);

    private async Task<OnlineHealthItem> CheckServiceAsync(string serviceName, CancellationToken cancellationToken)
    {
        var query = await _commands.RunAsync("sc.exe", ["query", serviceName], cancellationToken).ConfigureAwait(false);
        var state = ParseServiceState(query.StandardOutput, query.StandardError, query.ExitCode);
        if (state is "NOT INSTALLED" or "UNAVAILABLE")
            return new(serviceName, DiagnosticHealth.Review, state,
                "This Xbox support service was not found. Windows editions and simulator installations vary.");

        var configuration = await _commands.RunAsync("sc.exe", ["qc", serviceName], cancellationToken).ConfigureAwait(false);
        if (IsDisabledService(configuration.StandardOutput))
            return new(serviceName, DiagnosticHealth.Problem, "DISABLED",
                "The service is installed but disabled; Microsoft online features may not be able to start it.");

        return state == "RUNNING"
            ? new(serviceName, DiagnosticHealth.Ready, state, "The service is installed and running.")
            : new(serviceName, DiagnosticHealth.Ready, "TRIGGER READY",
                "The service is installed and can be started by Windows when required.");
    }
}
