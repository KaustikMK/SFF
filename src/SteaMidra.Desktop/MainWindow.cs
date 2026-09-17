using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace SteaMidra.Desktop;

/// <summary>Native desktop interface for browsing the local Steam catalog and library.</summary>
public sealed class MainWindow : Window
{
    private static readonly string[] LumaCoreDlls = ["dwmapi.dll", "xinput1_4.dll", "LumaCore.dll", "LumaCorePayload.dll"];
    private readonly ContentControl _content = new();
    private TextBlock _status = new() { Foreground = Brushes.LightGray };
    private DesktopSettings _settings;
    private readonly List<DownloadItem> _downloads = [];

    public MainWindow()
    {
        _settings = LoadSettings();
        Title = "SteaMidra";
        Width = 1180;
        Height = 760;
        MinWidth = 900;
        MinHeight = 600;
        Background = Brush.Parse("#16181D");

        var sidebar = new StackPanel { Spacing = 4, Margin = new Thickness(12) };
        sidebar.Children.Add(new TextBlock { Text = "STEAMIDRA", FontSize = 20, FontWeight = FontWeight.Bold, Foreground = Brush.Parse("#80BFFF"), Margin = new Thickness(8, 12, 8, 22) });
        foreach (var page in new[] { "Home", "Store", "Library", "Downloads", "Fix Game", "Cloud Saves", "Settings" }) sidebar.Children.Add(NavigationButton(page));
        sidebar.Children.Add(new Separator { Margin = new Thickness(8, 16) });
        sidebar.Children.Add(NavigationButton("Restart Steam", RestartSteam));

        _content.Margin = new Thickness(38, 30);
        var main = new Grid();
        main.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(210)));
        main.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        main.Children.Add(new Border { Background = Brush.Parse("#20232A"), Child = sidebar });
        Grid.SetColumn(_content, 1);
        main.Children.Add(_content);
        Content = main;
        Navigate("Home");
    }

    private Button NavigationButton(string page, Action? action = null) => new()
    {
        Content = page, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
        Padding = new Thickness(14, 10), Background = Brushes.Transparent, Foreground = Brushes.White,
        Command = new DelegateCommand(() => { if (action is null) Navigate(page); else action(); })
    };

    private void Navigate(string page)
    {
        _content.Content = null;
        _status = new TextBlock { Foreground = Brushes.LightGray, Text = PageStatus(page) };
        var panel = new StackPanel { Spacing = 18 };
        panel.Children.Add(new TextBlock { Text = page, FontSize = 24, FontWeight = FontWeight.SemiBold });
        panel.Children.Add(_status);
        panel.Children.Add(page switch
        {
            "Home" => HomeContent(), "Store" => StoreContent(), "Library" => LibraryContent(), "Downloads" => DownloadsContent(),
            "Fix Game" => FixGameContent(), "Cloud Saves" => CloudSavesContent(), "Settings" => SettingsContent(), "Auto LC Setup" => LumaCoreSetupContent(),
            _ => InformationCard("Not available.")
        });
        _content.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
    }

    private static string PageStatus(string page) => page switch
    {
        "Home" => "Choose a task to manage your Steam installation.",
        "Store" => "Search the bundled catalog and install its Lua and manifests into Steam.",
        "Library" => "Scan every configured Steam library for installed applications.",
        "Downloads" => "Review Lua and manifest installs started from the Store.",
        "Auto LC Setup" => "Choose your Steam folder, then install or update LumaCore.",
        _ => "Ready."
    };

    private Control HomeContent()
    {
        var cards = new WrapPanel { ItemWidth = 240, ItemHeight = 132 };
        cards.Children.Add(ActionCard("Steam library", "Scan installed applications in every Steam library.", () => Navigate("Library")));
        cards.Children.Add(ActionCard("Browse store", "Search the bundled Steam catalog.", () => Navigate("Store")));
        cards.Children.Add(ActionCard("Downloads", "View Lua and manifest installs started here.", () => Navigate("Downloads")));
        cards.Children.Add(ActionCard("Settings", "Set or detect your Steam folder.", () => Navigate("Settings")));
        cards.Children.Add(ActionCard("Auto LC Setup", "Install the latest LumaCore release safely.", () => Navigate("Auto LC Setup")));
        return cards;
    }

    private Control StoreContent()
    {
        var query = new TextBox { Watermark = "Search by title or App ID", MinWidth = 360 };
        var results = new StackPanel { Spacing = 8 };
        var search = new Button { Content = "Search", Padding = new Thickness(14, 9), Background = Brush.Parse("#3478C8"), Foreground = Brushes.White };
        var searching = false;
        async Task RunSearch()
        {
            if (searching) return;
            searching = true;
            search.IsEnabled = false;
            try
            {
                _status.Text = "Searching bundled catalog…";
                var term = query.Text?.Trim() ?? string.Empty;
                var matches = await SearchCatalogAsync(term);
                results.Children.Clear();
                foreach (var game in matches) results.Children.Add(StoreResult(game));
                _status.Text = matches.Count == 0 ? "No matching products found." : $"Showing {matches.Count} product{(matches.Count == 1 ? string.Empty : "s")}.";
            }
            catch (Exception exception)
            {
                StartupDiagnostics.Report(exception, context: "store search");
                _status.Text = $"Could not search the bundled catalog: {exception.Message}";
            }
            finally { searching = false; search.IsEnabled = true; }
        }
        search.Click += async (_, _) => await RunSearch();
        query.KeyDown += async (_, eventArgs) => { if (eventArgs.Key == Avalonia.Input.Key.Enter) await RunSearch(); };
        return new StackPanel { Spacing = 12, Children = { new WrapPanel { Orientation = Orientation.Horizontal, Children = { query, search } }, results } };
    }

    private Control StoreResult(StoreGame game)
    {
        var install = new Button { Content = "Install with Steam", Padding = new Thickness(10, 6) };
        install.Click += async (_, _) => await InstallWithSteamAsync(game, install);
        var open = new Button { Content = "Open in Steam", Padding = new Thickness(10, 6) };
        open.Click += (_, _) => OpenSteamProduct(game);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { install, open } };
        return new Border { Background = Brush.Parse("#20232A"), CornerRadius = new CornerRadius(8), Padding = new Thickness(14), Child = new DockPanel
        {
            Children = { actions, new StackPanel { Spacing = 3, Children = { new TextBlock { Text = game.Name, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White }, new TextBlock { Text = $"App ID {game.AppId} · {game.Type ?? "Game"}", Foreground = Brushes.LightGray } } } }
        }};
    }

    private void OpenSteamProduct(StoreGame game) => OpenSteamUri(game, $"steam://store/{game.AppId}", "Opened in Steam", $"Opened {game.Name} in Steam. Purchase or install it from the Steam client.");

    private async Task InstallWithSteamAsync(StoreGame game, Button install)
    {
        install.IsEnabled = false;
        try
        {
            var steamPath = _settings.SteamPath ?? FindSteamPath();
            _status.Text = $"Preparing {game.Name}…";
            var result = await SteamInstallService.InstallAsync(
                steamPath, game.AppId, _settings.HubcapApiKey ?? string.Empty,
                progress => Dispatcher.UIThread.Post(() => _status.Text = progress));
            _downloads.Add(new DownloadItem(game.Name, game.AppId, $"Installed {result.ManifestCount} manifest(s)"));
            _status.Text = $"Installed Lua and {result.ManifestCount} manifest(s) for {game.Name}. Restart Steam if it is already running.";
        }
        catch (Exception exception)
        {
            StartupDiagnostics.Report(exception, context: "Steam manifest install");
            _status.Text = $"Installation failed: {exception.Message}";
        }
        finally { install.IsEnabled = true; }
    }

    private void OpenSteamUri(StoreGame game, string uri, string status, string message)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            _downloads.Add(new DownloadItem(game.Name, game.AppId, status));
            _status.Text = message;
        }
        catch (Exception exception) { _status.Text = $"Could not open Steam: {exception.Message}"; }
    }

    private Control LibraryContent()
    {
        var games = new StackPanel { Spacing = 8 };
        var scan = new Button { Content = "Scan libraries", Padding = new Thickness(14, 9), Background = Brush.Parse("#3478C8"), Foreground = Brushes.White };
        scan.Click += async (_, _) =>
        {
            scan.IsEnabled = false;
            try
            {
                _status.Text = "Scanning Steam library folders…";
                var found = await Task.Run(ScanLibrary);
                games.Children.Clear();
                foreach (var game in found) games.Children.Add(InformationCard($"{game.Name}\nApp ID {game.AppId} · {game.InstallDirectory}"));
                _status.Text = found.Count == 0 ? "No installed Steam applications were found." : $"Found {found.Count} installed application(s).";
            }
            catch (Exception exception) { _status.Text = $"Library scan failed: {exception.Message}"; }
            finally { scan.IsEnabled = true; }
        };
        return new StackPanel { Spacing = 12, Children = { scan, games } };
    }

    private Control DownloadsContent()
    {
        var items = new StackPanel { Spacing = 8 };
        if (_downloads.Count == 0) items.Children.Add(InformationCard("No Lua or manifest installs have been started from this session."));
        foreach (var item in _downloads.AsEnumerable().Reverse()) items.Children.Add(InformationCard($"{item.Name}\nApp ID {item.AppId} · {item.Status}"));
        return items;
    }

    private Control FixGameContent() => InformationCard("Game repair tools are not included in the native client yet. Use the supported Steam client tools to verify files: Library → Properties → Installed Files → Verify integrity of game files.");

    private Control CloudSavesContent() => InformationCard("Cloud saves are managed by the official Steam client. Enable or resolve Steam Cloud conflicts from each game’s Properties → General page.");

    private Control SettingsContent()
    {
        var steamPath = new TextBox { Text = _settings.SteamPath ?? FindSteamPath(), Watermark = @"Steam folder, e.g. C:\Program Files (x86)\Steam", MinWidth = 480 };
        var hubcapApiKey = new TextBox { Text = _settings.HubcapApiKey, PasswordChar = '●', Watermark = "Hubcap API key (required for Install with Steam)", MinWidth = 480 };
        var save = new Button { Content = "Save settings", Padding = new Thickness(14, 9), Background = Brush.Parse("#3478C8"), Foreground = Brushes.White };
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(steamPath.Text) || !Directory.Exists(steamPath.Text)) { _status.Text = "Choose an existing Steam folder before saving."; return; }
            _settings = new DesktopSettings(Path.GetFullPath(steamPath.Text), hubcapApiKey.Text?.Trim()); SaveSettings(_settings); _status.Text = "Steam folder and API key saved.";
        };
        return new StackPanel { Spacing = 12, Children = { new TextBlock { Text = "Steam folder", Foreground = Brushes.White }, steamPath, new TextBlock { Text = "Hubcap API key", Foreground = Brushes.White }, hubcapApiKey, save } };
    }

    private Control LumaCoreSetupContent()
    {
        var steamPath = new TextBox { Text = _settings.SteamPath ?? FindSteamPath(), Watermark = @"Steam folder, e.g. C:\Program Files (x86)\Steam", MinWidth = 480 };
        var install = new Button { Content = "Install LumaCore", Padding = new Thickness(14, 10), Background = Brush.Parse("#3478C8"), Foreground = Brushes.White };
        install.Click += async (_, _) => { install.IsEnabled = false; try { await InstallLumaCoreAsync(steamPath.Text); } finally { install.IsEnabled = true; } };
        return new Border { Background = Brush.Parse("#20232A"), CornerRadius = new CornerRadius(10), Padding = new Thickness(24), Child = new StackPanel { Spacing = 14, Children =
        {
            new TextBlock { Text = "Auto LC Setup", FontSize = 18, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White },
            new TextBlock { Text = "Installs the LumaCore files bundled with this SteaMidra release. Steam is closed only when the files are ready to copy.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray }, steamPath, install
        }}};
    }

    private async Task InstallLumaCoreAsync(string? steamPathText)
    {
        if (!OperatingSystem.IsWindows()) { _status.Text = "LumaCore is only available on Windows."; return; }
        if (string.IsNullOrWhiteSpace(steamPathText) || !Directory.Exists(steamPathText)) { _status.Text = "Choose a valid Steam folder before installing LumaCore."; return; }
        var steamPath = Path.GetFullPath(steamPathText);
        try
        {
            _status.Text = "Validating bundled LumaCore files…";
            var files = LoadBundledLumaCoreFiles();
            _status.Text = "Closing Steam…";
            await Task.Run(CloseSteam);
            await Task.Run(() => InstallLumaCoreFiles(steamPath, files));
            _settings = _settings with { SteamPath = steamPath }; SaveSettings(_settings);
            _status.Text = "LumaCore installed. Start Steam normally when ready.";
        }
        catch (Exception exception) { _status.Text = $"LumaCore installation failed: {exception.Message}"; }
    }

    private static Dictionary<string, byte[]> LoadBundledLumaCoreFiles()
    {
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in LumaCoreDlls)
        {
            var path = new[] { Path.Combine(AppContext.BaseDirectory, "lumacore", name), Path.Combine(Directory.GetCurrentDirectory(), "lumacore", name) }.FirstOrDefault(File.Exists);
            if (path is null) throw new FileNotFoundException($"The bundled LumaCore file {name} is missing. Reinstall this SteaMidra release.");
            files[name] = File.ReadAllBytes(path);
        }
        return files;
    }

    private static void InstallLumaCoreFiles(string steamPath, IReadOnlyDictionary<string, byte[]> files)
    {
        var staging = Path.Combine(Path.GetTempPath(), $"SteaMidra-LumaCore-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var (name, contents) in files) File.WriteAllBytes(Path.Combine(staging, name), contents);
            CleanLegacyInjectionFiles(steamPath);
            foreach (var name in LumaCoreDlls) File.Move(Path.Combine(staging, name), Path.Combine(steamPath, name), overwrite: true);
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
    }

    private List<InstalledGame> ScanLibrary()
    {
        var steamPath = _settings.SteamPath ?? FindSteamPath();
        if (string.IsNullOrWhiteSpace(steamPath) || !Directory.Exists(steamPath)) throw new InvalidOperationException("Set a valid Steam folder in Settings first.");
        var steamApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.Combine(steamPath, "steamapps") };
        var libraries = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (File.Exists(libraries)) foreach (Match match in Regex.Matches(File.ReadAllText(libraries), "\\\"path\\\"\\s+\\\"(?<path>[^\\\"]+)\\\"", RegexOptions.IgnoreCase)) steamApps.Add(Path.Combine(match.Groups["path"].Value.Replace("\\\\", "\\"), "steamapps"));
        var games = new List<InstalledGame>();
        foreach (var folder in steamApps.Where(Directory.Exists)) foreach (var manifest in Directory.EnumerateFiles(folder, "appmanifest_*.acf"))
        {
            var text = File.ReadAllText(manifest);
            var id = Regex.Match(text, "\\\"appid\\\"\\s+\\\"(?<value>\\d+)\\\"").Groups["value"].Value;
            var name = Regex.Match(text, "\\\"name\\\"\\s+\\\"(?<value>[^\\\"]+)\\\"").Groups["value"].Value;
            var directory = Regex.Match(text, "\\\"installdir\\\"\\s+\\\"(?<value>[^\\\"]+)\\\"").Groups["value"].Value;
            if (!string.IsNullOrEmpty(id)) games.Add(new InstalledGame(id, string.IsNullOrEmpty(name) ? $"App {id}" : name, Path.Combine(folder, "common", directory)));
        }
        return games.OrderBy(game => game.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static async Task<List<StoreGame>> SearchCatalogAsync(string term)
    {
        var path = FindCatalogPath() ?? throw new FileNotFoundException("store_metadata/games.json was not found. Reinstall the application.");
        var matches = new List<StoreGame>(50);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 64 * 1024, useAsync: true);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        await foreach (var game in JsonSerializer.DeserializeAsyncEnumerable<StoreGame>(stream, options).ConfigureAwait(false))
        {
            if (game is null || game.Nsfw) continue;
            if (!string.IsNullOrEmpty(term) && !game.Name.Contains(term, StringComparison.OrdinalIgnoreCase) && !game.AppId.Contains(term, StringComparison.Ordinal)) continue;
            matches.Add(game);
            if (matches.Count == 50) break;
        }
        return matches;
    }

    private static string? FindCatalogPath()
    {
        var candidates = new[] { Path.Combine(AppContext.BaseDirectory, "store_metadata", "games.json"), Path.Combine(Directory.GetCurrentDirectory(), "store_metadata", "games.json") };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string FindSteamPath()
    {
        if (!OperatingSystem.IsWindows()) return string.Empty;
        var candidates = new[] { Environment.GetEnvironmentVariable("ProgramFiles(x86)"), Environment.GetEnvironmentVariable("ProgramFiles") }.Where(path => !string.IsNullOrWhiteSpace(path)).Select(path => Path.Combine(path!, "Steam"));
        return candidates.FirstOrDefault(Directory.Exists) ?? string.Empty;
    }

    private static void CloseSteam() { foreach (var process in Process.GetProcessesByName("steam")) { process.Kill(); process.WaitForExit(10_000); } }

    private static void CleanLegacyInjectionFiles(string steamPath)
    {
        foreach (var name in new[] { "GreenLuma_2024_x64.dll", "GreenLuma_2024_x86.dll", "GreenLuma_2025_x64.dll", "GreenLuma_2025_x86.dll", "GreenLuma.dll", "GreenLumaSettings_2025.exe", "DLLInjector.exe", "DLLInjector.ini", "SteamKillInject.exe" }) { var path = Path.Combine(steamPath, name); if (File.Exists(path)) File.Delete(path); }
        foreach (var name in new[] { "AppList", "GreenLuma2025_Files" }) { var path = Path.Combine(steamPath, name); if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
    }

    private static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteaMidra", "desktop-settings.json");
    private static DesktopSettings LoadSettings() { try { return JsonSerializer.Deserialize<DesktopSettings>(File.ReadAllText(SettingsPath)) ?? new DesktopSettings(null); } catch { return new DesktopSettings(null); } }
    private static void SaveSettings(DesktopSettings settings) { Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!); File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings)); }
    private static Control InformationCard(string text) => new Border { Background = Brush.Parse("#20232A"), CornerRadius = new CornerRadius(10), Padding = new Thickness(18), Child = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White } };
    private static Button ActionCard(string heading, string description, Action action) => new() { Margin = new Thickness(0, 0, 14, 14), Padding = new Thickness(18), Background = Brush.Parse("#29313D"), Foreground = Brushes.White, HorizontalContentAlignment = HorizontalAlignment.Left, Content = new StackPanel { Spacing = 8, Children = { new TextBlock { Text = heading, FontWeight = FontWeight.Bold, FontSize = 16 }, new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray } } }, Command = new DelegateCommand(action) };
    private void RestartSteam() { try { CloseSteam(); _status.Text = "Steam processes were stopped. Start Steam normally when ready."; } catch (Exception exception) { _status.Text = $"Could not restart Steam: {exception.Message}"; } }

    private sealed record StoreGame(string AppId, string Name, string? Type, bool Nsfw);
    private sealed record InstalledGame(string AppId, string Name, string InstallDirectory);
    private sealed record DownloadItem(string Name, string AppId, string Status);
    private sealed record DesktopSettings(string? SteamPath, string? HubcapApiKey = null);
}

internal sealed class DelegateCommand(Action execute) : System.Windows.Input.ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
}
