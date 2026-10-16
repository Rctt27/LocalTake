using System.Globalization;
using Microsoft.Extensions.Logging;

namespace LocalTake.Infrastructure.Logging;

/// <summary>
/// A plain text log, one file per day, kept a week. It stays on this computer: LocalTake sends nothing anywhere. A
/// user can attach it to a bug report.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private const string Prefix = "localtake-";
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    private readonly string _folder;
    private readonly TimeProvider _clock;
    private readonly Lock _gate = new();

    public FileLoggerProvider(string folder, TimeProvider clock)
    {
        _folder = folder;
        _clock = clock;
        try
        {
            Directory.CreateDirectory(folder);
            DeleteOldFiles();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logging must never prevent LocalTake from starting.
        }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
    }

    internal void Write(LogLevel level, string category, string message, Exception? exception)
    {
        var now = _clock.GetLocalNow();
        var line = string.Create(CultureInfo.InvariantCulture, $"{now:yyyy-MM-dd HH:mm:ss.fff} {LevelName(level),-5} {category}: {message}");
        if (exception is not null)
            line += Environment.NewLine + exception;
        var file = Path.Combine(_folder, Prefix + now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");
        lock (_gate)
        {
            try
            {
                File.AppendAllText(file, line + Environment.NewLine);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A log line is never worth an error.
            }
        }
    }

    private void DeleteOldFiles()
    {
        var oldest = _clock.GetLocalNow().Date - Retention;
        foreach (var file in Directory.EnumerateFiles(_folder, Prefix + "*.log"))
        {
            var day = Path.GetFileNameWithoutExtension(file)[Prefix.Length..];
            if (DateTime.TryParseExact(day, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date < oldest)
                File.Delete(file);
        }
    }

    private static string LevelName(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRACE",
        LogLevel.Debug => "DEBUG",
        LogLevel.Information => "INFO",
        LogLevel.Warning => "WARN",
        LogLevel.Error => "ERROR",
        _ => "CRIT"
    };

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel is >= LogLevel.Information and < LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                provider.Write(logLevel, category, formatter(state, exception), exception);
        }
    }
}
