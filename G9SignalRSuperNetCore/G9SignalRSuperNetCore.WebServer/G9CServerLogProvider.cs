using Microsoft.Extensions.Logging;

namespace G9SignalRSuperNetCore.WebServer;

/// <summary>
///     <see cref="ILoggerProvider"/> that forwards every formatted log entry into the shared
///     <see cref="G9CServerLogFeed"/>. Registered through
///     <c>builder.Logging.AddProvider(new G9CServerLogProvider(feed))</c> on app startup.
/// </summary>
public sealed class G9CServerLogProvider : ILoggerProvider
{
    private readonly G9CServerLogFeed _feed;

    /// <summary>Initializes the provider.</summary>
    public G9CServerLogProvider(G9CServerLogFeed feed) => _feed = feed;

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new G9CFeedLogger(_feed, categoryName);

    /// <inheritdoc />
    public void Dispose() { }

    private sealed class G9CFeedLogger : ILogger
    {
        private readonly G9CServerLogFeed _feed;
        private readonly string _category;

        public G9CFeedLogger(G9CServerLogFeed feed, string category)
        {
            _feed = feed;
            _category = category;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var message = formatter(state, exception) ?? string.Empty;
            var level = logLevel switch
            {
                LogLevel.Trace => "trace",
                LogLevel.Debug => "debug",
                LogLevel.Information => "info",
                LogLevel.Warning => "warn",
                LogLevel.Error => "error",
                LogLevel.Critical => "crit",
                _ => "info"
            };

            _feed.Append(new G9DtServerLogEntry(
                UtcTimestamp: DateTime.UtcNow,
                Level: level,
                Category: _category,
                Message: message,
                Exception: exception?.GetType().Name + ": " + exception?.Message));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
