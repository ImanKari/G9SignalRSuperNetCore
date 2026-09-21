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

    /// <summary>
    ///     <c>--stateful-reconnect</c> asks for SignalR's stateful reconnect (2.8); the sample server allows it on the
    ///     chat hub. Statics, because the client reads them in <c>ConfigureConnectionOptions</c>, which runs inside
    ///     the base constructor.
    /// </summary>
    public static bool UseStatefulReconnect { get; private set; }

    /// <summary><c>--websockets-first</c> connects without the negotiate request and falls back to negotiation (2.8).</summary>
    public static bool UseWebSocketsFirst { get; private set; }

    private static readonly string[] OwnSwitches = ["--messagepack", "--stateful-reconnect", "--websockets-first"];

    [STAThread]
    public static void Main(string[] args)
    {
        UseMessagePack = args.Contains("--messagepack", StringComparer.OrdinalIgnoreCase)
                         || string.Equals(Environment.GetEnvironmentVariable("G9_SAMPLE_PROTOCOL"), "messagepack", StringComparison.OrdinalIgnoreCase);
        UseStatefulReconnect = args.Contains("--stateful-reconnect", StringComparer.OrdinalIgnoreCase);
        UseWebSocketsFirst = args.Contains("--websockets-first", StringComparer.OrdinalIgnoreCase);
        BuildAvaloniaApp().StartWithConsoleLifetime(args.Where(a => !OwnSwitches.Contains(a, StringComparer.OrdinalIgnoreCase)).ToArray());
    }

    /// <summary>Builds the Avalonia application configured for the Consolonia (terminal) backend.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseConsolonia()
            .UseAutoDetectedConsole()
            .LogToException();
}
