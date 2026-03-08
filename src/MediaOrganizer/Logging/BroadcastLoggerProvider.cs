using Microsoft.Extensions.Logging;

namespace MediaOrganizer.Logging;

public sealed class BroadcastLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly LogBroadcaster _broadcaster;
    private IExternalScopeProvider _scopeProvider = new LoggerExternalScopeProvider();

    public BroadcastLoggerProvider(LogBroadcaster broadcaster)
    {
        _broadcaster = broadcaster;
    }

    public ILogger CreateLogger(string categoryName) => new BroadcastLogger(categoryName, _broadcaster, () => _scopeProvider);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
    {
        _scopeProvider = scopeProvider ?? new LoggerExternalScopeProvider();
    }

    public void Dispose()
    {
        // No-op
    }

    private sealed class BroadcastLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly LogBroadcaster _broadcaster;
        private readonly Func<IExternalScopeProvider> _getScopeProvider;

        public BroadcastLogger(string categoryName, LogBroadcaster broadcaster, Func<IExternalScopeProvider> getScopeProvider)
        {
            _categoryName = categoryName;
            _broadcaster = broadcaster;
            _getScopeProvider = getScopeProvider;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            var scopeProvider = _getScopeProvider();
            return scopeProvider.Push(state);
        }

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var message = formatter(state, exception);

            // Include scopes (often request ids etc.) in a readable way.
            string? scopeText = null;
            try
            {
                var scopeProvider = _getScopeProvider();
                var scopes = new List<string>();
                scopeProvider.ForEachScope((scope, collected) =>
                {
                    if (scope is null)
                        return;
                    collected.Add(scope.ToString()!);
                }, scopes);

                if (scopes.Count > 0)
                {
                    scopeText = string.Join(" => ", scopes);
                }
            }
            catch
            {
                // Ignore scope formatting failures.
            }

            // Single-line message for SSE consumption.
            var combined = message;
            if (!string.IsNullOrWhiteSpace(scopeText))
                combined = $"{combined} (scope: {scopeText})";
            if (exception is not null)
                combined = $"{combined} | {exception}";
            combined = combined.Replace("\r\n", "\n").Replace("\r", "\n");

            _broadcaster.Publish(new LogEvent(
                Timestamp: DateTimeOffset.Now,
                Level: logLevel,
                Category: _categoryName,
                Message: combined,
                Exception: null));
        }
    }
}
