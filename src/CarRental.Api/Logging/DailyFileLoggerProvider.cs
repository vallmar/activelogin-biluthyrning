using Microsoft.Extensions.Logging;

public sealed class DailyFileLoggerProvider : ILoggerProvider
{
    private static readonly object Sync = new();
    private readonly string logDirectory;

    public DailyFileLoggerProvider(string logDirectory)
    {
        this.logDirectory = logDirectory;
        Directory.CreateDirectory(logDirectory);
    }

    public ILogger CreateLogger(string categoryName)
        => new DailyFileLogger(categoryName, logDirectory);

    public void Dispose()
    {
    }

    private sealed class DailyFileLogger(string categoryName, string logDirectory) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel)
            => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var timestamp = DateTimeOffset.Now;
            var filePath = Path.Combine(logDirectory, $"car-rental-{timestamp:yyyy-MM-dd}.log");
            var message = formatter(state, exception);
            var line = $"{timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{logLevel}] {categoryName}: {message}";

            if (exception is not null)
                line += Environment.NewLine + exception;

            lock (Sync)
            {
                Directory.CreateDirectory(logDirectory);
                File.AppendAllText(filePath, line + Environment.NewLine);
            }
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
