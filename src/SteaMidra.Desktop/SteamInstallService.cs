using System.IO.Compression;
using System.Net.Http.Headers;

namespace SteaMidra.Desktop;

/// <summary>Downloads a manifest bundle and installs the files that Steam/LumaCore consume.</summary>
internal static class SteamInstallService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    /// <summary>
    /// Fetches the authenticated Hubcap bundle, then writes its Lua and depot manifests to Steam.
    /// No Steam URI is opened: Steam can discover the files when it next reads its configuration.
    /// </summary>
    internal static async Task<SteamInstallResult> InstallAsync(
        string steamPath, string appId, string hubcapApiKey, Action<string>? reportProgress = null,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Install with Steam is available only on Windows.");
        if (!Directory.Exists(steamPath))
            throw new DirectoryNotFoundException("Choose a valid Steam folder in Settings first.");
        if (string.IsNullOrWhiteSpace(hubcapApiKey))
            throw new InvalidOperationException("Add your Hubcap API key in Settings before installing.");

        reportProgress?.Invoke("Downloading Lua and manifest bundle…");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://hubcapmanifest.com/api/v1/manifest/{appId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", hubcapApiKey.Trim());
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"The manifest service returned {(int)response.StatusCode} ({response.ReasonPhrase}).");

        var bundle = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        reportProgress?.Invoke("Validating downloaded Lua and manifests…");
        var files = ReadBundle(bundle, appId);

        reportProgress?.Invoke("Installing Lua and manifests into Steam…");
        var luaDirectory = Path.Combine(steamPath, "config", "stplug-in");
        var depotCache = Path.Combine(steamPath, "depotcache");
        var configDepotCache = Path.Combine(steamPath, "config", "depotcache");
        Directory.CreateDirectory(luaDirectory);
        Directory.CreateDirectory(depotCache);
        Directory.CreateDirectory(configDepotCache);

        WriteAtomically(Path.Combine(luaDirectory, $"{appId}.lua"), files.Lua);
        foreach (var manifest in files.Manifests)
        {
            WriteAtomically(Path.Combine(depotCache, manifest.Name), manifest.Contents);
            // LumaCore and older Steam configurations also read this cache.
            WriteAtomically(Path.Combine(configDepotCache, manifest.Name), manifest.Contents);
        }

        return new SteamInstallResult(files.Manifests.Count);
    }

    private static BundleFiles ReadBundle(byte[] bundle, string appId)
    {
        try
        {
            using var archive = new ZipArchive(new MemoryStream(bundle, writable: false), ZipArchiveMode.Read);
            var lua = archive.Entries.FirstOrDefault(entry =>
                entry.Name.Equals($"{appId}.lua", StringComparison.OrdinalIgnoreCase))
                ?? archive.Entries.FirstOrDefault(entry => entry.Name.EndsWith(".lua", StringComparison.OrdinalIgnoreCase));
            if (lua is null) throw new InvalidDataException("The downloaded bundle does not contain a Lua file.");

            var manifests = archive.Entries
                .Where(entry => entry.Name.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase))
                .Select(entry => new BundleFile(Path.GetFileName(entry.Name), ReadEntry(entry)))
                .Where(file => IsManifestName(file.Name))
                .ToList();
            if (manifests.Count == 0) throw new InvalidDataException("The downloaded bundle does not contain any depot manifests.");
            return new BundleFiles(ReadEntry(lua), manifests);
        }
        catch (InvalidDataException exception) when (exception.Message.Contains("archive", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The manifest service did not return a valid ZIP bundle.", exception);
        }
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        if (entry.Length == 0) throw new InvalidDataException($"Bundle file '{entry.Name}' is empty.");
        using var input = entry.Open();
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }

    private static bool IsManifestName(string name) =>
        System.Text.RegularExpressions.Regex.IsMatch(name, @"^\d+_\d+\.manifest$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static void WriteAtomically(string path, byte[] contents)
    {
        var temporaryPath = path + ".new-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllBytes(temporaryPath, contents);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private sealed record BundleFile(string Name, byte[] Contents);
    private sealed record BundleFiles(byte[] Lua, List<BundleFile> Manifests);
}

internal sealed record SteamInstallResult(int ManifestCount);
