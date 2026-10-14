using System;
using System.Collections.Concurrent;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace QuickER.Tests.Integration;

/// <summary>ホストのログを溜めるテスト用のプロバイダ（レベル・カテゴリ・例外の型を見る）。</summary>
internal sealed class RecordingLoggerProvider : ILoggerProvider
{
    /// <summary>1 件のログ</summary>
    public sealed record Entry(
        LogLevel Level,
        string Category,
        string Message,
        Exception? Exception
    );

    /// <summary>溜まったログ</summary>
    public ConcurrentQueue<Entry> Entries { get; } = new();

    /// <summary>リモートサーバーのエンジンが書いた、指定のレベルのログ</summary>
    public Entry[] Engine(LogLevel level) =>
        Entries.Where(e => e.Category == "QuickER.RemoteServer" && e.Level == level).ToArray();

    /// <summary>Error 以上のログ（カテゴリを問わない）</summary>
    public Entry[] Errors() => Entries.Where(e => e.Level >= LogLevel.Error).ToArray();

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose() { }

    private sealed class Logger(RecordingLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (IsEnabled(logLevel))
            {
                owner.Entries.Enqueue(
                    new Entry(logLevel, category, formatter(state, exception), exception)
                );
            }
        }
    }
}
