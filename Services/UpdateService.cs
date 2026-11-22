using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using PCCare.Models;

namespace PCCare.Services;

public sealed class UpdateService
{
    private const string LatestReleaseUrl =
        "https://api.github.com/repos/DemirBogucanin/pc-care/releases/latest";

    private const string AssetName = "PC_Care_Setup.exe";

    private static readonly HttpClient Http = CreateClient();

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    /// <summary>
    /// True when the app runs from Program Files, i.e. it was installed by the setup.
    /// Portable copies cannot update themselves silently.
    /// </summary>
    public static bool IsInstalledCopy
    {
        get
        {
            var directory = AppContext.BaseDirectory;

            foreach (var root in new[]
                     {
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                     })
            {
                if (!string.IsNullOrWhiteSpace(root) &&
                    directory.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await Http.GetAsync(LatestReleaseUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken));
            var root = document.RootElement;

            if (!root.TryGetProperty("tag_name", out var tagElement))
            {
                return null;
            }

            var tag = (tagElement.GetString() ?? string.Empty).Trim().TrimStart('v', 'V');
            if (!Version.TryParse(tag, out var latest) || !IsNewer(latest, CurrentVersion))
            {
                return null;
            }

            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            string? downloadUrl = null;
            string? sha256 = null;

            foreach (var asset in assets.EnumerateArray())
            {
                if (!asset.TryGetProperty("name", out var nameElement) ||
                    !string.Equals(nameElement.GetString(), AssetName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                downloadUrl = asset.TryGetProperty("browser_download_url", out var urlElement)
                    ? urlElement.GetString()
                    : null;

                sha256 = asset.TryGetProperty("digest", out var digestElement)
                    ? digestElement.GetString()?.Replace("sha256:", string.Empty, StringComparison.OrdinalIgnoreCase)
                    : null;

                break;
            }

            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                return null;
            }

            var notes = root.TryGetProperty("body", out var bodyElement)
                ? bodyElement.GetString() ?? string.Empty
                : string.Empty;

            return new UpdateInfo(latest, tag, downloadUrl, sha256, notes);
        }
        catch
        {
            // Offline, rate limited or blocked - the app keeps working as before.
            return null;
        }
    }

    public async Task<string> DownloadAsync(
        UpdateInfo info,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var uri = new Uri(info.DownloadUrl);
        if (uri.Scheme != Uri.UriSchemeHttps ||
            !(uri.Host.EndsWith("github.com", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.EndsWith("githubusercontent.com", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("The update is not hosted on GitHub.");
        }

        var folder = Path.Combine(Path.GetTempPath(), "PCCareUpdate");
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, AssetName);

        using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1;

        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var file = File.Create(target))
        {
            var buffer = new byte[81920];
            long received = 0;
            int read;

            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                received += read;

                if (total > 0)
                {
                    progress?.Report(received * 100.0 / total);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(info.Sha256))
        {
            await using var check = File.OpenRead(target);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(check, cancellationToken));
            if (!hash.Equals(info.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(target);
                throw new InvalidOperationException("The downloaded installer failed verification.");
            }
        }

        return target;
    }

    public bool LaunchInstaller(string installerPath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS",
                UseShellExecute = true
            });

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsNewer(Version latest, Version current)
    {
        if (latest.Major != current.Major)
        {
            return latest.Major > current.Major;
        }

        if (latest.Minor != current.Minor)
        {
            return latest.Minor > current.Minor;
        }

        return Math.Max(latest.Build, 0) > Math.Max(current.Build, 0);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PC-Care-Updater/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
