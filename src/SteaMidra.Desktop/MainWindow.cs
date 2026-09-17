using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace SteaMidra.Desktop;

/// <summary>A native, dependency-free-from-Python desktop shell for SteaMidra.</summary>
public sealed class MainWindow : Window
{
    private readonly ContentControl _content = new();
    private TextBlock _status = new() { Foreground = Brushes.LightGray };
    private static readonly string[] LumaCoreDlls = ["dwmapi.dll", "xinput1_4.dll", "LumaCore.dll", "LumaCorePayload.dll"];

    public MainWindow()
    {
        Title = "SteaMidra";
        Width = 1180;
        Height = 760;
        MinWidth = 900;
        MinHeight = 600;
        Background = Brush.Parse("#16181D");

        var sidebar = new StackPanel { Spacing = 4, Margin = new Thickness(12) };
        sidebar.Children.Add(new TextBlock
        {
            Text = "STEAMIDRA",
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            Foreground = Brush.Parse("#80BFFF"),
            Margin = new Thickness(8, 12, 8, 22)
        });
        foreach (var page in new[] { "Home", "Store", "Library", "Downloads", "Fix Game", "Cloud Saves", "Settings" })
            sidebar.Children.Add(NavigationButton(page));
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
        Content = page,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Left,
        Padding = new Thickness(14, 10),
        Background = Brushes.Transparent,
        Foreground = Brushes.White,
        Command = new DelegateCommand(() => { if (action is null) Navigate(page); else action(); })
    };

    private void Navigate(string page)
    {
        // A control can belong to only one visual tree. Clear the old page before
        // constructing the replacement, then use fresh header controls for it.
        _content.Content = null;

        var title = new TextBlock { Text = page, FontSize = 24, FontWeight = FontWeight.SemiBold };
        _status = new TextBlock
        {
            Foreground = Brushes.LightGray,
            Text = page switch
            {
                "Home" => "Native desktop UI · no Python runtime required",
                "Library" => "Choose a Steam library folder in Settings to begin scanning.",
                "Downloads" => "No active downloads.",
                "Auto LC Setup" => "Choose your Steam folder, then install or update LumaCore.",
                _ => "This native page is ready for its service integration."
            }
        };

        var panel = new StackPanel { Spacing = 18 };
        panel.Children.Add(title);
        panel.Children.Add(_status);
        panel.Children.Add(page switch
        {
            "Home" => HomeContent(),
            "Auto LC Setup" => LumaCoreSetupContent(),
            _ => PagePlaceholder(page)
        });
        _content.Content = panel;
    }

    private Control HomeContent()
    {
        var cards = new WrapPanel { ItemWidth = 240, ItemHeight = 132 };
        cards.Children.Add(ActionCard("Steam library", "Select and scan your Steam library.", () => Navigate("Library")));
        cards.Children.Add(ActionCard("Browse manifests", "Find games and depot manifests.", () => Navigate("Store")));
        cards.Children.Add(ActionCard("Downloads", "View active and completed work.", () => Navigate("Downloads")));
        cards.Children.Add(ActionCard("Settings", "Configure Steam and app preferences.", () => Navigate("Settings")));
        cards.Children.Add(ActionCard("Auto LC Setup", "Install or update LumaCore in your Steam folder.", () => Navigate("Auto LC Setup")));
        return cards;
    }

    private Control LumaCoreSetupContent()
    {
        var steamPath = new TextBox
        {
            Text = FindSteamPath(),
            Watermark = @"Steam folder, e.g. C:\Program Files (x86)\Steam",
            MinWidth = 480
        };
        var install = new Button
        {
            Content = "Install LumaCore",
            Padding = new Thickness(14, 10),
            Background = Brush.Parse("#3478C8"),
            Foreground = Brushes.White
        };
        install.Click += async (_, _) =>
        {
            install.IsEnabled = false;
            try { await InstallLumaCoreAsync(steamPath.Text); }
            finally { install.IsEnabled = true; }
        };

        return new Border
        {
            Background = Brush.Parse("#20232A"),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(24),
            Child = new StackPanel
            {
                Spacing = 14,
                Children =
                {
                    new TextBlock { Text = "Auto LC Setup", FontSize = 18, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White },
                    new TextBlock { Text = "The installer closes Steam, removes legacy injector files, and downloads the latest LumaCore release.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray },
                    steamPath,
                    install
                }
            }
        };
    }

    private static string FindSteamPath()
    {
        if (!OperatingSystem.IsWindows()) return string.Empty;
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("ProgramFiles(x86)") is { Length: > 0 } programFilesX86 ? Path.Combine(programFilesX86, "Steam") : string.Empty,
            Environment.GetEnvironmentVariable("ProgramFiles") is { Length: > 0 } programFiles ? Path.Combine(programFiles, "Steam") : string.Empty
        };
        return candidates.FirstOrDefault(Directory.Exists) ?? string.Empty;
    }

    private async Task InstallLumaCoreAsync(string? steamPathText)
    {
        if (!OperatingSystem.IsWindows())
        {
            _status.Text = "LumaCore is only available on Windows.";
            return;
        }

        if (string.IsNullOrWhiteSpace(steamPathText) || !Directory.Exists(steamPathText))
        {
            _status.Text = "Choose a valid Steam folder before installing LumaCore.";
            return;
        }

        var steamPath = Path.GetFullPath(steamPathText);
        try
        {
            _status.Text = "Closing Steam...";
            await Task.Run(() => CloseSteam());
            _status.Text = "Downloading the latest LumaCore release...";
            await Task.Run(() => DownloadAndInstallLumaCore(steamPath));
            _status.Text = "LumaCore installed. Start Steam normally when ready.";
        }
        catch (Exception exception)
        {
            _status.Text = $"LumaCore installation failed: {exception.Message}";
        }
    }

    private static void CloseSteam()
    {
        foreach (var process in Process.GetProcessesByName("steam"))
        {
            process.Kill();
            process.WaitForExit(10_000);
        }
    }

    private static void DownloadAndInstallLumaCore(string steamPath)
    {
        CleanLegacyInjectionFiles(steamPath);
        foreach (var name in LumaCoreDlls)
        {
            var oldFile = Path.Combine(steamPath, name);
            if (File.Exists(oldFile)) File.Delete(oldFile);
        }

        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SteaMidra-Desktop");
        var release = client.GetFromJsonAsync<LumaCoreRelease>("https://api.github.com/repos/KoriaPolis/LumaCore/releases/latest").GetAwaiter().GetResult()
            ?? throw new InvalidOperationException("GitHub did not return a LumaCore release.");
        var asset = release.Assets.FirstOrDefault(item => item.Name.Equals("release.zip", StringComparison.OrdinalIgnoreCase))
            ?? release.Assets.FirstOrDefault(item => item.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The LumaCore release does not contain a ZIP asset.");

        var archive = client.GetByteArrayAsync(asset.BrowserDownloadUrl).GetAwaiter().GetResult();
        using var zip = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read);
        foreach (var name in LumaCoreDlls)
        {
            var entry = zip.Entries.FirstOrDefault(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"The release archive is missing {name}.");
            entry.ExtractToFile(Path.Combine(steamPath, name), overwrite: true);
        }
    }

    private static void CleanLegacyInjectionFiles(string steamPath)
    {
        foreach (var name in new[]
        {
            "GreenLuma_2024_x64.dll", "GreenLuma_2024_x86.dll", "GreenLuma_2025_x64.dll",
            "GreenLuma_2025_x86.dll", "GreenLuma.dll", "GreenLumaSettings_2025.exe",
            "DLLInjector.exe", "DLLInjector.ini", "SteamKillInject.exe"
        })
        {
            var path = Path.Combine(steamPath, name);
            if (File.Exists(path)) File.Delete(path);
        }

        foreach (var name in new[] { "AppList", "GreenLuma2025_Files" })
        {
            var path = Path.Combine(steamPath, name);
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
    }

    private sealed record LumaCoreRelease(List<LumaCoreReleaseAsset> Assets);
    private sealed record LumaCoreReleaseAsset(string Name, string BrowserDownloadUrl);

    private static Control PagePlaceholder(string page) => new Border
    {
        Background = Brush.Parse("#20232A"),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(24),
        Child = new TextBlock
        {
            Text = $"{page} is presented by the native Avalonia desktop application.\nIts UI no longer loads QWebEngine, PySide, or a Python bridge.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.White,
            FontSize = 16
        }
    };

    private static Button ActionCard(string heading, string description, Action action) => new()
    {
        Margin = new Thickness(0, 0, 14, 14),
        Padding = new Thickness(18),
        Background = Brush.Parse("#29313D"),
        Foreground = Brushes.White,
        HorizontalContentAlignment = HorizontalAlignment.Left,
        Content = new StackPanel { Spacing = 8, Children = { new TextBlock { Text = heading, FontWeight = FontWeight.Bold, FontSize = 16 }, new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray } } },
        Command = new DelegateCommand(action)
    };

    private void RestartSteam()
    {
        try
        {
            foreach (var process in Process.GetProcessesByName("steam")) process.Kill();
            _status.Text = "Steam processes were stopped. Start Steam normally when ready.";
        }
        catch (Exception exception) { _status.Text = $"Could not restart Steam: {exception.Message}"; }
    }
}

internal sealed class DelegateCommand(Action execute) : System.Windows.Input.ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
}
