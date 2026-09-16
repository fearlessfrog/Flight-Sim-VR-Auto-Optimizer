using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SimVROptimizer.Core;

public sealed record ApplicationUpdateInfo(
    Version CurrentVersion,
    Version LatestVersion,
    string ReleaseName,
    Uri ReleaseUri,
    ReleaseDownloadAsset? VerifiedInstaller = null)
{
    public bool IsUpdateAvailable => LatestVersion > CurrentVersion;
}

public sealed record ReleaseDownloadAsset(
    string Name,
    Uri DownloadUri,
    long SizeBytes,
    string ChecksumName,
    Uri ChecksumUri);

public sealed record VerifiedUpdateDownload(string Path, string Sha256, long SizeBytes);

public sealed class ApplicationUpdateChecker
{
    public const string LatestReleaseApiUrl =
        "https://api.github.com/repos/macbrowndog/Flight-Sim-VR-Auto-Optimizer/releases/latest";

    private readonly HttpClient _httpClient;

    public ApplicationUpdateChecker(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    public async Task<ApplicationUpdateInfo> CheckAsync(
        Version installedVersion,
        CancellationToken cancellationToken = default)
    {
        var current = Normalize(installedVersion);
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApiUrl);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("VR-Auto-Optimizer", current.ToString(3)));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var tag = RequiredString(root, "tag_name");
        var latest = ParseReleaseVersion(tag);
        var releaseName = root.TryGetProperty("name", out var nameElement)
            ? nameElement.GetString()?.Trim()
            : null;
        var releaseUri = ValidateReleaseUri(RequiredString(root, "html_url"));
        var verifiedInstaller = FindVerifiedInstaller(root);

        return new(current, latest, string.IsNullOrWhiteSpace(releaseName) ? tag : releaseName, releaseUri, verifiedInstaller);
    }

    public static Version ParseReleaseVersion(string tag)
    {
        var value = tag.Trim().TrimStart('v', 'V');
        var suffix = value.IndexOfAny(['-', '+']);
        if (suffix >= 0) value = value[..suffix];
        if (!Version.TryParse(value, out var version))
            throw new InvalidDataException($"The latest release tag '{tag}' is not a valid version.");
        return Normalize(version);
    }

    private static Version Normalize(Version version) =>
        new(version.Major, Math.Max(0, version.Minor), Math.Max(0, version.Build));

    private static string RequiredString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element)
            || string.IsNullOrWhiteSpace(element.GetString()))
            throw new InvalidDataException($"The GitHub release response did not include {propertyName}.");
        return element.GetString()!.Trim();
    }

    private static Uri ValidateReleaseUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || !uri.AbsolutePath.StartsWith(
                "/macbrowndog/Flight-Sim-VR-Auto-Optimizer/releases/",
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("GitHub returned an invalid release address.");
        return uri;
    }

    private static ReleaseDownloadAsset? FindVerifiedInstaller(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assetsElement) || assetsElement.ValueKind != JsonValueKind.Array)
            return null;
        var assets = assetsElement.EnumerateArray()
            .Select(element => new
            {
                Name = element.TryGetProperty("name", out var name) ? name.GetString()?.Trim() ?? "" : "",
                Url = element.TryGetProperty("browser_download_url", out var url) ? url.GetString()?.Trim() ?? "" : "",
                Size = element.TryGetProperty("size", out var size) && size.TryGetInt64(out var bytes) ? bytes : 0
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Name) && !string.IsNullOrWhiteSpace(item.Url))
            .ToArray();
        var installer = assets.FirstOrDefault(item => item.Name.EndsWith("-Setup.exe", StringComparison.OrdinalIgnoreCase));
        if (installer is null) return null;
        var checksum = assets.FirstOrDefault(item =>
            item.Name.Equals(installer.Name + ".sha256", StringComparison.OrdinalIgnoreCase));
        if (checksum is null) return null;
        return new(installer.Name, ValidateDownloadUri(installer.Url), installer.Size,
            checksum.Name, ValidateDownloadUri(checksum.Url));
    }

    public static Uri ValidateDownloadUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || !uri.AbsolutePath.StartsWith(
                "/macbrowndog/Flight-Sim-VR-Auto-Optimizer/releases/download/",
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("GitHub returned an invalid release download address.");
        return uri;
    }
}

/// <summary>Downloads a release installer only when its separately published SHA-256 checksum matches.</summary>
public sealed class VerifiedUpdateDownloader
{
    private const long MaximumInstallerBytes = 512L * 1024 * 1024;
    private readonly HttpClient _httpClient;

    public VerifiedUpdateDownloader(HttpClient? httpClient = null) =>
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

    public async Task<VerifiedUpdateDownload> DownloadAsync(
        ReleaseDownloadAsset asset,
        string destinationDirectory,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var assetUri = ApplicationUpdateChecker.ValidateDownloadUri(asset.DownloadUri.AbsoluteUri);
        var checksumUri = ApplicationUpdateChecker.ValidateDownloadUri(asset.ChecksumUri.AbsoluteUri);
        if (asset.SizeBytes < 0 || asset.SizeBytes > MaximumInstallerBytes)
            throw new InvalidDataException("The published installer size is outside the permitted range.");
        var fileName = Path.GetFileName(asset.Name);
        if (!fileName.Equals(asset.Name, StringComparison.Ordinal) || !fileName.EndsWith("-Setup.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The published installer filename is invalid.");

        var expectedHash = await DownloadChecksumAsync(checksumUri, fileName, cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(destinationDirectory);
        var destination = Path.Combine(destinationDirectory, fileName);
        var partial = destination + ".partial";
        try
        {
            using var response = await _httpClient.GetAsync(assetUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var contentLength = response.Content.Headers.ContentLength ?? asset.SizeBytes;
            if (contentLength > MaximumInstallerBytes)
                throw new InvalidDataException("The downloaded installer is larger than the permitted limit.");
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (var target = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[128 * 1024];
                long total = 0;
                while (true)
                {
                    var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read == 0) break;
                    total += read;
                    if (total > MaximumInstallerBytes)
                        throw new InvalidDataException("The downloaded installer exceeded the permitted limit.");
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    if (contentLength > 0) progress?.Report(Math.Clamp(total / (double)contentLength, 0, 1));
                }
            }

            var actualSize = new FileInfo(partial).Length;
            if (asset.SizeBytes > 0 && actualSize != asset.SizeBytes)
                throw new InvalidDataException($"Installer size mismatch: expected {asset.SizeBytes} bytes, received {actualSize} bytes.");
            string actualHash;
            await using (var verificationStream = File.OpenRead(partial))
                actualHash = Convert.ToHexString(await SHA256.HashDataAsync(verificationStream, cancellationToken).ConfigureAwait(false));
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(expectedHash), Convert.FromHexString(actualHash)))
                throw new InvalidDataException("The downloaded installer failed SHA-256 verification and was deleted.");

            File.Move(partial, destination, true);
            progress?.Report(1);
            return new(destination, actualHash, actualSize);
        }
        catch
        {
            if (File.Exists(partial)) File.Delete(partial);
            throw;
        }
    }

    private async Task<string> DownloadChecksumAsync(Uri checksumUri, string assetName, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(checksumUri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (text.Length > 16 * 1024) throw new InvalidDataException("The checksum file is unexpectedly large.");
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = Regex.Match(line, @"^(?<hash>[A-Fa-f0-9]{64})(?:\s+[*]?\s*(?<name>.+))?$");
            if (!match.Success) continue;
            var name = match.Groups["name"].Value.Trim();
            if (string.IsNullOrWhiteSpace(name) || Path.GetFileName(name).Equals(assetName, StringComparison.OrdinalIgnoreCase))
                return match.Groups["hash"].Value.ToUpperInvariant();
        }
        throw new InvalidDataException("The release checksum file does not contain a valid SHA-256 value for the installer.");
    }
}
