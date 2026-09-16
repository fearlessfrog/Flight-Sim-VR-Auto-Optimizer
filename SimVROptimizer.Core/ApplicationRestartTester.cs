using System.Diagnostics;

namespace SimVROptimizer.Core;

public sealed record ApplicationRestartTestResult(bool Success, string Detail);

/// <summary>Closes and immediately relaunches one application to verify its recorded restart command.</summary>
public sealed class ApplicationRestartTester
{
    private readonly IApplicationRestarter _restarter;

    public ApplicationRestartTester(IApplicationRestarter? restarter = null) =>
        _restarter = restarter ?? new ApplicationRestarter();

    public async Task<ApplicationRestartTestResult> TestAsync(
        RunningAppCandidate application,
        CancellationToken cancellationToken = default)
    {
        if (!application.CanRestartAfterFlight)
            return new(false, "No reliable restart command is available for this application.");

        foreach (var process in Process.GetProcessesByName(application.ProcessName))
        {
            using (process)
            {
                try { process.CloseMainWindow(); }
                catch (InvalidOperationException) { }
            }
        }

        await Task.Delay(750, cancellationToken).ConfigureAwait(false);
        foreach (var process in Process.GetProcessesByName(application.ProcessName))
        {
            using (process)
            {
                try
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) { }
            }
        }

        await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        var restarted = await _restarter.RestartAndVerifyAsync(
            application.ProcessName, application.RestartCommand, cancellationToken).ConfigureAwait(false);
        return restarted
            ? new(true, $"{application.DisplayName} closed and restarted successfully.")
            : new(false, $"{application.DisplayName} closed, but the recorded restart command did not relaunch it. Start it manually and leave After Flight set to Left Closed.");
    }
}
