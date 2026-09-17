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
    /// <summary>
    ///     <c>--messagepack</c> (or <c>G9_SAMPLE_PROTOCOL=messagepack</c>) connects with the binary MessagePack hub
    ///     protocol instead of JSON. The sample server offers both.
    /// </summary>
    public static bool UseMessagePack { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        UseMessagePack = args.Contains("--messagepack", StringComparer.OrdinalIgnoreCase)
                         || string.Equals(Environment.GetEnvironmentVariable("G9_SAMPLE_PROTOCOL"), "messagepack", StringComparison.OrdinalIgnoreCase);
        BuildAvaloniaApp().StartWithConsoleLifetime(args.Where(a => !a.Equals("--messagepack", StringComparison.OrdinalIgnoreCase)).ToArray());
    }

    /// <summary>Builds the Avalonia application configured for the Consolonia (terminal) backend.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseConsolonia()
            .UseAutoDetectedConsole()
            .LogToException();
}
