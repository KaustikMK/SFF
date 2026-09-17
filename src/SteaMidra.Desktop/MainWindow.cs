using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace SteaMidra.Desktop;

/// <summary>A native, dependency-free-from-Python desktop shell for SteaMidra.</summary>
public sealed class MainWindow : Window
{
    private readonly ContentControl _content = new();
    private readonly TextBlock _title = new() { FontSize = 24, FontWeight = FontWeight.SemiBold };
    private readonly TextBlock _status = new() { Foreground = Brushes.LightGray };

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
        _title.Text = page;
        _status.Text = page switch
        {
            "Home" => "Native desktop UI · no Python runtime required",
            "Library" => "Choose a Steam library folder in Settings to begin scanning.",
            "Downloads" => "No active downloads.",
            _ => "This native page is ready for its service integration."
        };

        var panel = new StackPanel { Spacing = 18 };
        panel.Children.Add(_title);
        panel.Children.Add(_status);
        panel.Children.Add(page == "Home" ? HomeContent() : PagePlaceholder(page));
        _content.Content = panel;
    }

    private Control HomeContent()
    {
        var cards = new WrapPanel { ItemWidth = 240, ItemHeight = 132 };
        cards.Children.Add(ActionCard("Steam library", "Select and scan your Steam library.", () => Navigate("Library")));
        cards.Children.Add(ActionCard("Browse manifests", "Find games and depot manifests.", () => Navigate("Store")));
        cards.Children.Add(ActionCard("Downloads", "View active and completed work.", () => Navigate("Downloads")));
        cards.Children.Add(ActionCard("Settings", "Configure Steam and app preferences.", () => Navigate("Settings")));
        return cards;
    }

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
