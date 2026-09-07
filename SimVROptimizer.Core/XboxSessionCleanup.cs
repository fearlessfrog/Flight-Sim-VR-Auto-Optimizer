namespace SimVROptimizer.Core;

public interface IXboxSessionCleanup
{
    event Action<string>? StatusChanged;
    Task CleanupAsync(CancellationToken cancellationToken = default);
}

public sealed class XboxSessionCleanup : IXboxSessionCleanup
{
    private readonly FileLogger _logger;
    public XboxSessionCleanup(FileLogger logger) => _logger = logger;

    public event Action<string>? StatusChanged;

    public async Task CleanupAsync(CancellationToken cancellationToken = default)
    {
        await ReportAsync(
            "MSFS has exited; preserving Xbox, Game Bar, Gaming Services, authentication, game-save and networking components for the next launch. No Xbox process or service was terminated.",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ReportAsync(string message, CancellationToken cancellationToken)
    {
        StatusChanged?.Invoke(message);
        await _logger.WriteAsync(message, cancellationToken).ConfigureAwait(false);
    }
}
