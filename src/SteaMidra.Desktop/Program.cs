using Avalonia;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace SteaMidra.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            StartupDiagnostics.Report(eventArgs.ExceptionObject as Exception ?? new Exception("Unknown unhandled exception."));
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            StartupDiagnostics.Report(eventArgs.Exception);
            eventArgs.SetObserved();
        };

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception exception)
        {
            StartupDiagnostics.Report(exception, showDialog: true);
        }
    }

    private static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

        // ANGLE/D3D composition is a frequent cause of a process exiting before
        // its first window in Wine/Winlator. Skia's software backend is slower,
        // but is reliable on Wine and more than sufficient for this UI.
        if (StartupDiagnostics.IsRunningUnderWine())
        {
            builder.With(new Win32PlatformOptions
            {
                RenderingMode = new[] { Win32RenderingMode.Software },
                CompositionMode = new[] { Win32CompositionMode.RedirectionSurface },
                ShouldRenderOnUIThread = true,
                DpiAwareness = Win32DpiAwareness.SystemDpiAware,
            });
        }

        return builder;
    }
}

internal static class StartupDiagnostics
{
    private const uint MessageBoxOkError = 0x10;

    public static bool IsRunningUnderWine()
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            if (!NativeLibrary.TryLoad("ntdll.dll", out var module)) return false;
            try { return NativeLibrary.TryGetExport(module, "wine_get_version", out _); }
            finally { NativeLibrary.Free(module); }
        }
        catch { return false; }
    }

    public static void Report(Exception exception, bool showDialog = false, string context = "runtime")
    {
        var message = $"{DateTimeOffset.UtcNow:O}{Environment.NewLine}" +
            $"SteaMidra {Assembly.GetExecutingAssembly().GetName().Version} failed during {context}.{Environment.NewLine}" +
            exception + Environment.NewLine + Environment.NewLine;
        var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteaMidra", "startup.log");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.AppendAllText(logPath, message, Encoding.UTF8);
        }
        catch
        {
            try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "startup.log"), message, Encoding.UTF8); }
            catch { /* Reporting must never prevent shutdown. */ }
        }

        if (showDialog && OperatingSystem.IsWindows())
        {
            try { MessageBoxW(IntPtr.Zero, $"SteaMidra could not start.\n\nDetails were written to:\n{logPath}", "SteaMidra startup error", MessageBoxOkError); }
            catch { /* Wine may not have a functioning user32 at this point. */ }
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
