using Avalonia;
using Consolonia;

namespace G9SignalRSuperNetCore.ConsoleClient;

/// <summary>
///     Entry point for the Consolonia-based test harness. Runs an Avalonia-XAML UI inside
///     a normal terminal window so we can verify the typed source-generated SignalR client
///     end-to-end against the WebServer sample.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
        => BuildAvaloniaApp().StartWithConsoleLifetime(args);

    /// <summary>Builds the Avalonia application configured for the Consolonia (terminal) backend.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseConsolonia()
            .UseAutoDetectedConsole()
            .LogToException();
}
