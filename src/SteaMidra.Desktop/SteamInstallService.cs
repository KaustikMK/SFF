using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

namespace SteaMidra.Desktop;

/// <summary>Downloads a manifest bundle and installs the files that Steam/LumaCore consume.</summary>
internal static class SteamInstallService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    /// <summary>
    /// Fetches the authenticated Hubcap bundle, then writes its Lua, depot keys, and manifests to Steam.
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

        reportProgress?.Invoke("Installing Lua, depot keys, and manifests into Steam…");
        var luaDirectory = Path.Combine(steamPath, "config", "stplug-in");
        var depotCache = Path.Combine(steamPath, "depotcache");
        var configDepotCache = Path.Combine(steamPath, "config", "depotcache");
        Directory.CreateDirectory(luaDirectory);
        Directory.CreateDirectory(depotCache);
        Directory.CreateDirectory(configDepotCache);

        WriteAtomically(Path.Combine(luaDirectory, $"{appId}.lua"), files.Lua);
        reportProgress?.Invoke("Installing depot decryption keys…");
        var keyCount = InstallDepotKeys(steamPath, files.Lua);
        foreach (var manifest in files.Manifests)
        {
            WriteAtomically(Path.Combine(depotCache, manifest.Name), manifest.Contents);
            // LumaCore and older Steam configurations also read this cache.
            WriteAtomically(Path.Combine(configDepotCache, manifest.Name), manifest.Contents);
        }

        return new SteamInstallResult(files.Manifests.Count, keyCount);
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
        Regex.IsMatch(name, @"^\d+_\d+\.manifest$", RegexOptions.IgnoreCase);

    /// <summary>
    /// Mirrors the regular Windows install workflow by copying the depot keys
    /// embedded in the Lua into Steam's config.vdf. LumaCore reads the Lua,
    /// while Steam itself needs these keys to decrypt the downloaded depots.
    /// </summary>
    private static int InstallDepotKeys(string steamPath, byte[] lua)
    {
        var keys = ReadDepotKeys(lua);
        if (keys.Count == 0) return 0;

        var configPath = Path.Combine(steamPath, "config", "config.vdf");
        if (!File.Exists(configPath))
            throw new FileNotFoundException("Steam config.vdf was not found. Start Steam once, then try again.", configPath);

        var config = File.ReadAllText(configPath, Encoding.UTF8);
        var steamBlock = FindBlock(config, "Steam")
            ?? throw new InvalidDataException("Steam config.vdf does not contain the Steam configuration block.");
        var depotsBlock = FindBlock(config, "depots", steamBlock.Start, steamBlock.End);
        var indent = GetIndentation(config, steamBlock.Start) + "\t";

        if (depotsBlock is null)
        {
            var depotEntries = FormatDepotEntries(keys, indent + "\t");
            config = config.Insert(steamBlock.End, $"\n{indent}\"depots\"\n{indent}{{\n{depotEntries}{indent}}}\n");
        }
        else
        {
            var existingDepotsBlock = depotsBlock.Value;
            var depotEntries = FormatDepotEntries(
                keys,
                GetIndentation(config, existingDepotsBlock.Start) + "\t",
                config[existingDepotsBlock.Start..existingDepotsBlock.End]);
            if (depotEntries.Length > 0)
                config = config.Insert(existingDepotsBlock.End, $"\n{depotEntries}");
        }

        WriteAtomically(configPath, Encoding.UTF8.GetBytes(config));
        return keys.Count;
    }

    private static Dictionary<string, string> ReadDepotKeys(byte[] lua)
    {
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        var luaText = Encoding.UTF8.GetString(lua);
        foreach (Match match in Regex.Matches(luaText,
                     """addappid\s*\(\s*(?<depot>\d+)\s*,\s*[01]\s*,\s*[\"'](?<key>[0-9a-fA-F]{64})[\"']\s*\)""",
                     RegexOptions.IgnoreCase))
        {
            keys[match.Groups["depot"].Value] = match.Groups["key"].Value.ToLowerInvariant();
        }
        return keys;
    }

    private static string FormatDepotEntries(IReadOnlyDictionary<string, string> keys, string indent, string existing = "") =>
        string.Concat(keys
            .Where(pair => !Regex.IsMatch(existing, $"\\\"{Regex.Escape(pair.Key)}\\\"\\s*\\{{", RegexOptions.IgnoreCase))
            .Select(pair => $"{indent}\"{pair.Key}\"\n{indent}{{\n{indent}\t\"DecryptionKey\"\t\t\"{pair.Value}\"\n{indent}}}\n"));

    private static (int Start, int End)? FindBlock(string vdf, string name, int start = 0, int? end = null)
    {
        var limit = end ?? vdf.Length;
        var key = Regex.Match(vdf[start..limit], $"\\\"{Regex.Escape(name)}\\\"\\s*\\{{", RegexOptions.IgnoreCase);
        if (!key.Success) return null;
        var openBrace = start + key.Index + key.Length - 1;
        var closeBrace = FindMatchingBrace(vdf, openBrace, limit);
        return closeBrace < 0 ? null : (openBrace, closeBrace);
    }

    private static int FindMatchingBrace(string text, int openBrace, int limit)
    {
        var depth = 0;
        var inQuote = false;
        for (var index = openBrace; index < limit; index++)
        {
            if (text[index] == '"' && (index == 0 || text[index - 1] != '\\')) inQuote = !inQuote;
            if (inQuote) continue;
            if (text[index] == '{') depth++;
            else if (text[index] == '}' && --depth == 0) return index;
        }
        return -1;
    }

    private static string GetIndentation(string text, int index)
    {
        var lineStart = text.LastIndexOf('\n', Math.Max(0, index - 1)) + 1;
        var length = 0;
        while (lineStart + length < text.Length && (text[lineStart + length] == '\t' || text[lineStart + length] == ' ')) length++;
        return text.Substring(lineStart, length);
    }

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

internal sealed record SteamInstallResult(int ManifestCount, int DepotKeyCount);
