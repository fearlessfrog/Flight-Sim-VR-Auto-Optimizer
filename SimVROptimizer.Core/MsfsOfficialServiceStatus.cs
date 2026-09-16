using System.Net;
using System.Text.RegularExpressions;

namespace SimVROptimizer.Core;

public sealed record MsfsOfficialStatusReport(string LastUpdated, IReadOnlyList<OnlineHealthItem> Items);

/// <summary>Reads the manually maintained official MSFS online-services forum status.</summary>
public sealed class MsfsOfficialServiceStatusClient
{
    public const string StatusPageUrl = "https://forums.flightsimulator.com/t/online-services-monitoring/486800";
    private static readonly string[] ServiceNames = ["Live Weather", "Live Traffic", "Multiplayer", "Online Services"];
    private readonly HttpClient _httpClient;

    public MsfsOfficialServiceStatusClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("VR-Auto-Optimizer/2.3");
    }

    public async Task<MsfsOfficialStatusReport> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var html = await _httpClient.GetStringAsync(StatusPageUrl, cancellationToken).ConfigureAwait(false);
            var report = ParseStatusHtml(html);
            if (report.Items.Count > 0) return report;
            return Unavailable("The official page was reached, but its MSFS 2024 status could not be read.");
        }
        catch (HttpRequestException exception)
        {
            return Unavailable("The official status page could not be reached: " + exception.Message);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Unavailable("The official status page timed out: " + exception.Message);
        }
    }

    public static MsfsOfficialStatusReport ParseStatusHtml(string html)
    {
        var withLines = Regex.Replace(html, @"</?(?:h1|h2|h3|p|div|li|br)\b[^>]*>", "\n", RegexOptions.IgnoreCase);
        var text = WebUtility.HtmlDecode(Regex.Replace(withLines, @"<[^>]+>", " "));
        text = Regex.Replace(text, @"[ \t]+", " ");
        text = Regex.Replace(text, @"(?:\r?\n){2,}", "\n");

        var updated = Regex.Match(text, @"Last Updated:\s*([^\r\n]+)", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
        if (string.IsNullOrWhiteSpace(updated)) updated = "not stated";

        var items = new List<OnlineHealthItem>();
        var searchFrom = 0;
        for (var index = 0; index < ServiceNames.Length; index++)
        {
            var name = ServiceNames[index];
            var start = text.IndexOf(name, searchFrom, StringComparison.OrdinalIgnoreCase);
            if (start < 0) continue;
            searchFrom = start + name.Length;
            var end = index + 1 < ServiceNames.Length
                ? text.IndexOf(ServiceNames[index + 1], start + name.Length, StringComparison.OrdinalIgnoreCase)
                : Math.Min(text.Length, start + 700);
            if (end < 0) end = Math.Min(text.Length, start + 700);
            var section = text[start..end];
            var match = Regex.Match(section, @"MSFS\s*2024\s*:\s*([^\r\n]+)", RegexOptions.IgnoreCase);
            if (!match.Success) continue;

            var state = NormalizeState(Regex.Replace(match.Groups[1].Value, @":[a-z_]+:", string.Empty, RegexOptions.IgnoreCase));
            if (state.Length > 80) state = state[..80].TrimEnd();
            var health = state == "OPERATIONAL" ? DiagnosticHealth.Ready : DiagnosticHealth.Problem;
            items.Add(new("OFFICIAL MSFS 2024 / " + name.ToUpperInvariant(), health, state,
                $"Microsoft Flight Simulator forum status; manually updated. Last updated: {updated}."));
        }
        return new(updated, items);
    }

    private static string NormalizeState(string state) => Regex.Replace(state.Trim(), @"\s+", " ").ToUpperInvariant();

    private static MsfsOfficialStatusReport Unavailable(string detail) =>
        new("unavailable", [new("OFFICIAL MSFS 2024 STATUS", DiagnosticHealth.Review, "UNAVAILABLE", detail)]);
}
