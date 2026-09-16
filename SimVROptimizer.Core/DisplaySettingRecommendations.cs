using System.Globalization;
using System.Text.RegularExpressions;

namespace SimVROptimizer.Core;

public enum PerformanceBalance
{
    InsufficientData,
    LikelyCpuLimited,
    Balanced,
    LikelyGpuLimited
}

public sealed record DisplaySettingRecommendation(
    PerformanceBalance Balance,
    string BalanceLabel,
    string DlssRecommendation,
    string RenderScaleRecommendation,
    string FrameRateTarget,
    string Evidence,
    string SafetyNote);

public static class DisplaySettingRecommendationEngine
{
    public static DisplaySettingRecommendation Analyze(
        MsfsGraphicsDisplaySetting currentVr,
        PerformanceSessionSummary? session,
        string runtimeRefreshRate)
    {
        if (session?.AverageFrameTimeMs is null || session.AverageMainThreadMs is null || session.AverageFps is null)
            return new(PerformanceBalance.InsufficientData, "INSUFFICIENT PERFORMANCE DATA",
                "Keep the current anti-aliasing and DLSS mode until a monitored VR flight has completed.",
                "Keep the current render scaling. Record a representative flight before changing it.",
                TargetFromRefresh(runtimeRefreshRate, null, null),
                "A completed monitored session with FPS and MainThread readings is required.",
                "Recommendation only — no graphics setting has been changed.");

        var ratio = session.AverageMainThreadMs.Value / Math.Max(0.1, session.AverageFrameTimeMs.Value);
        var balance = ratio >= 0.78
            ? PerformanceBalance.LikelyCpuLimited
            : ratio <= 0.55
                ? PerformanceBalance.LikelyGpuLimited
                : PerformanceBalance.Balanced;
        var target = TargetFromRefresh(runtimeRefreshRate, session.AverageFps, session.OnePercentLowFps);
        var dlss = RecommendDlss(currentVr, balance, session.AverageFps.Value, ParseTarget(target));
        var scaling = balance switch
        {
            PerformanceBalance.LikelyCpuLimited => "Maintain render scaling; lowering resolution is unlikely to relieve a MainThread limit.",
            PerformanceBalance.LikelyGpuLimited => "Test 5% lower render scaling, then compare FPS and clarity. Avoid changing more than 10% per test.",
            _ => "Maintain render scaling. Change only if the target cannot be held consistently."
        };
        var label = balance switch
        {
            PerformanceBalance.LikelyCpuLimited => "LIKELY CPU / MAINTHREAD LIMITED",
            PerformanceBalance.LikelyGpuLimited => "LIKELY GPU / GRAPHICS LIMITED",
            _ => "BALANCED / MIXED LIMIT"
        };
        var evidence = $"Latest matching session: {session.AverageFps:0.0} average FPS, {session.OnePercentLowFps?.ToString("0.0") ?? "—"} 1% low, " +
            $"{session.AverageFrameTimeMs:0.0} ms frame time and {session.AverageMainThreadMs:0.0} ms MainThread ({ratio * 100:0}% of frame time).";
        return new(balance, label, dlss, scaling, target, evidence,
            "Recommendation only — test one change at a time. VR Auto-Optimizer does not rewrite UserCfg.opt.");
    }

    private static string RecommendDlss(MsfsGraphicsDisplaySetting current, PerformanceBalance balance, double averageFps, double? target)
    {
        var isDlss = current.AntiAliasing.Equals("DLSS", StringComparison.OrdinalIgnoreCase);
        var mode = current.DlssMode.ToUpperInvariant();
        if (balance == PerformanceBalance.LikelyCpuLimited)
            return isDlss
                ? $"Keep DLSS {mode}; a more aggressive DLSS mode is unlikely to improve a MainThread limit."
                : "Keep the current TAA setting unless a separate image-quality test favours DLSS; resolution reduction will not resolve a MainThread limit.";
        if (balance == PerformanceBalance.Balanced)
            return isDlss ? $"Keep DLSS {mode} as the baseline." : "Keep TAA as the baseline; compare DLSS Quality only if additional headroom is needed.";

        if (!isDlss) return "Test DLSS Quality first; compare cockpit clarity and 1% low FPS against the current TAA baseline.";
        if (mode is "QUALITY" or "DLAA") return "Test DLSS Balanced and compare clarity plus 1% low FPS against the saved baseline.";
        if (mode == "BALANCED" && target.HasValue && averageFps < target.Value * 0.9)
            return "Test DLSS Performance; revert if cockpit text or distant detail becomes unacceptable.";
        return $"Keep DLSS {mode}; adjust render scaling in small steps before selecting a more aggressive DLSS mode.";
    }

    private static string TargetFromRefresh(string refreshText, double? averageFps, double? onePercentLow)
    {
        var refresh = ParseNumber(refreshText);
        if (!refresh.HasValue)
            return averageFps.HasValue
                ? $"Target a stable {ChooseTarget(onePercentLow ?? averageFps.Value):0} FPS based on the recorded 1% low; headset refresh was not exposed."
                : "Use a stable headset-supported target (for example 45 FPS at 90 Hz with reprojection) after collecting performance data.";

        if (!averageFps.HasValue) return $"Initial target: {refresh.Value / 2:0.#} FPS at {refresh.Value:0.#} Hz; validate reprojection behaviour in the headset.";
        var sustainable = onePercentLow ?? averageFps.Value;
        var target = sustainable >= refresh.Value * 0.8
            ? refresh.Value
            : sustainable >= refresh.Value * 0.4
                ? refresh.Value / 2
                : refresh.Value / 3;
        return $"Target {target:0.#} FPS for the detected {refresh.Value:0.#} Hz refresh rate; prioritise stable 1% lows over peak FPS.";
    }

    private static double ChooseTarget(double sustainable)
    {
        foreach (var candidate in new[] { 120d, 90d, 80d, 72d, 60d, 45d, 40d, 36d, 30d })
            if (sustainable >= candidate * 0.9) return candidate;
        return 30;
    }

    private static double? ParseNumber(string text)
    {
        var match = Regex.Match(text ?? string.Empty, @"\d+(?:\.\d+)?");
        return match.Success && double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static double? ParseTarget(string text)
    {
        var match = Regex.Match(text, @"Target\s+(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
        return match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
    }
}

public static class MsfsUserCfgBackup
{
    public static string Create(string configPath, DateTimeOffset? timestamp = null)
    {
        if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
            throw new FileNotFoundException("UserCfg.opt could not be found.", configPath);
        var time = timestamp ?? DateTimeOffset.Now;
        var directory = Path.GetDirectoryName(configPath) ?? throw new InvalidOperationException("UserCfg.opt has no parent directory.");
        var baseName = $"{Path.GetFileName(configPath)}.backup-{time:yyyyMMdd-HHmmss}";
        var destination = Path.Combine(directory, baseName);
        for (var suffix = 1; File.Exists(destination); suffix++) destination = Path.Combine(directory, $"{baseName}-{suffix}");
        File.Copy(configPath, destination, overwrite: false);
        return destination;
    }
}
