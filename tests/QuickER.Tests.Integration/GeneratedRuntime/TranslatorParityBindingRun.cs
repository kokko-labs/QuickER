using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>値の束縛の生成の表の 1 行を流したときに、コマンドが束縛したパラメータ 1 つ</summary>
/// <param name="Name">名前</param>
/// <param name="DbType">DbType</param>
/// <param name="ProviderType">プロバイダの型（SqlDbType・NpgsqlDbType など。プロバイダが持たなければ null）</param>
/// <param name="Precision">精度</param>
/// <param name="Scale">スケール</param>
/// <param name="Size">大きさ</param>
/// <param name="Value">値（日時は tick までの往復の書式と Kind・リストは要素を並べる）</param>
public sealed record CapturedParameter(
    string Name,
    string DbType,
    string? ProviderType,
    byte Precision,
    byte Scale,
    int Size,
    string Value
)
{
    /// <inheritdoc/>
    public override string ToString() =>
        $"{Name} {DbType}{(ProviderType is null ? "" : "/" + ProviderType)}({Precision},{Scale},{Size}) {Value}";
}

/// <summary>値の束縛の生成の表の 1 行の結果（一致したか・例外・捕まえたパラメータ）</summary>
/// <param name="Path">クエリの経路の水準</param>
/// <param name="Matched">C# の評価と一致したか（例外は不一致）</param>
/// <param name="Expected">C# の評価（経路ごとの書き方。Query は当たる行のキー・ExecuteDelete は消した数と残る行のキー・ExecuteUpdate は当たる行の数）</param>
/// <param name="Actual">実装先の答え（例外なら null）</param>
/// <param name="ExceptionType">例外の型の名前（例外でなければ null）</param>
/// <param name="ExceptionMessage">例外の文（例外でなければ null）</param>
/// <param name="Parameters">経路の操作のコマンドが束縛したパラメータ（SQL Server の QuickER 版と EF Core 版だけ。SQLite の QuickER 版とインメモリは空）</param>
public sealed record BindingRunOutcome(
    string Path,
    bool Matched,
    string Expected,
    string? Actual,
    string? ExceptionType,
    string? ExceptionMessage,
    IReadOnlyList<CapturedParameter> Parameters
)
{
    /// <summary>経路の操作のコマンドの文（パラメータと同じ実装先だけ。コマンドの順）</summary>
    public IReadOnlyList<string> CommandTexts { get; init; } = [];

    /// <summary>比べた結果</summary>
    public static BindingRunOutcome Compared(
        string path,
        string expected,
        string actual,
        IReadOnlyList<CapturedParameter> parameters
    ) => new(path, expected == actual, expected, actual, null, null, parameters);

    /// <summary>例外で終わった結果（C# は例外にしないので不一致）</summary>
    public static BindingRunOutcome Threw(
        string path,
        string expected,
        Exception exception,
        IReadOnlyList<CapturedParameter> parameters
    )
    {
        // 反射で呼んだ操作が同期で投げた例外は、呼び出しの包みを外して元の例外を残す
        var thrown = exception
            is System.Reflection.TargetInvocationException { InnerException: { } inner }
            ? inner
            : exception;

        return new(
            path,
            false,
            expected,
            null,
            thrown.GetType().FullName,
            thrown.Message,
            parameters
        );
    }

    /// <summary>キーの並びの書き方（小さい順）</summary>
    public static string Keys(IEnumerable<long> keys) =>
        "["
        + string.Join(", ", keys.Order().Select(key => key.ToString(CultureInfo.InvariantCulture)))
        + "]";

    /// <summary>報告の文（期待・答え・例外・パラメータ）</summary>
    public string Describe()
    {
        var lines = new List<string>
        {
            $"経路 {Path}: {(Matched ? "一致" : "不一致")}",
            $"  C#: {Expected}",
            ExceptionType is null
                ? $"  実装先: {Actual}"
                : $"  実装先: 例外 {ExceptionType}: {ExceptionMessage}",
        };
        lines.AddRange(
            Parameters.Count == 0
                ? ["  パラメータ: （捕まえていない）"]
                : Parameters.Select(parameter => $"  パラメータ: {parameter}")
        );
        lines.AddRange(CommandTexts.Select(text => $"  文: {text.ReplaceLineEndings(" ")}"));

        return string.Join("\n", lines);
    }
}

/// <summary>
/// コマンドが束縛したパラメータを、いまの非同期の流れ（<see cref="AsyncLocal{T}"/>）の範囲の中でだけ集める（製品に観測の口を足さない）
/// </summary>
/// <remarks>
/// <para>
/// SQL Server の QuickER 版は SqlClient の診断の通知（<c>SqlClientDiagnosticListener</c> の <c>WriteCommandBefore</c>）で、EF Core 版はテストの側の
/// <see cref="EfCoreInterceptor"/>（コマンドを流した後に読む＝生成のインターセプターが型を変えた後のパラメータ）で捕まえる。どちらも通知を受けた
/// 流れの範囲（<see cref="Begin"/>）にだけ書くので、並列に流れるほかのテストのコマンドは混ざらない（自己検査は
/// <c>TranslatorParityRuntimeTestsBase.BindingCapture_KeepsParallelRunsApart</c>）。
/// </para>
/// <para>SQLite の QuickER 版とインメモリは捕まえない（結果だけを比べる）。</para>
/// </remarks>
public static class BindingParameterCapture
{
    /// <summary>SqlClient の診断の通知の名前</summary>
    public const string SqlClientListenerName = "SqlClientDiagnosticListener";

    /// <summary>コマンドを流す前の通知の名前の末尾</summary>
    public const string WriteCommandBefore = "WriteCommandBefore";

    private static readonly AsyncLocal<Sink?> Current = new();
    private static readonly Lazy<IDisposable> SqlClientSubscription = new(
        () => DiagnosticListener.AllListeners.Subscribe(new SqlClientObserver()),
        LazyThreadSafetyMode.ExecutionAndPublication
    );

    /// <summary>EF Core 版の DbContext に足すインターセプター（範囲の外のコマンドは読まない）</summary>
    public static DbCommandInterceptor EfCoreInterceptor { get; } = new CaptureInterceptor();

    /// <summary>いまの流れで集め始める（破棄で前の範囲へ戻す）</summary>
    public static Scope Begin()
    {
        _ = SqlClientSubscription.Value;
        var previous = Current.Value;
        var sink = new Sink();
        Current.Value = sink;

        return new Scope(sink, previous);
    }

    /// <summary>コマンドのパラメータと文を、いまの流れの範囲へ書く（範囲の外なら何もしない）</summary>
    /// <param name="command">コマンド</param>
    public static void Record(DbCommand command)
    {
        var sink = Current.Value;

        if (sink is null)
        {
            return;
        }

        var parameters = command.Parameters.Cast<DbParameter>().Select(Describe).ToList();

        lock (sink)
        {
            sink.Parameters.AddRange(parameters);
            sink.CommandTexts.Add(command.CommandText);
        }
    }

    /// <summary>1 つの範囲が集めたもの</summary>
    internal sealed class Sink
    {
        public List<CapturedParameter> Parameters { get; } = [];

        public List<string> CommandTexts { get; } = [];
    }

    private static CapturedParameter Describe(DbParameter parameter)
    {
        var type = parameter.GetType();
        var providerType = new[]
        {
            "SqlDbType",
            "NpgsqlDbType",
            "OracleDbType",
            "MySqlDbType",
            "SqliteType",
        }
            .Select(name => type.GetProperty(name)?.GetValue(parameter)?.ToString())
            .FirstOrDefault(value => value is not null);

        return new CapturedParameter(
            parameter.ParameterName,
            parameter.DbType.ToString(),
            providerType,
            parameter.Precision,
            parameter.Scale,
            parameter.Size,
            ValueText(parameter.Value)
        );
    }

    private static string ValueText(object? value) =>
        value switch
        {
            null or DBNull => "NULL",
            DateTime day =>
                $"{day.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture)}({day.Kind})",
            string text => $"'{text}'",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            IEnumerable items => "["
                + string.Join(", ", items.Cast<object?>().Select(ValueText))
                + "]",
            _ => value.ToString() ?? "",
        };

    /// <summary>集める範囲（破棄で前の範囲へ戻す）</summary>
    public sealed class Scope : IDisposable
    {
        private readonly Sink _sink;
        private readonly Sink? _previous;

        internal Scope(Sink sink, Sink? previous)
        {
            _sink = sink;
            _previous = previous;
        }

        /// <summary>集めたパラメータ（コマンドの順）</summary>
        public IReadOnlyList<CapturedParameter> Parameters
        {
            get
            {
                lock (_sink)
                {
                    return [.. _sink.Parameters];
                }
            }
        }

        /// <summary>集めたコマンドの文（コマンドの順）</summary>
        public IReadOnlyList<string> CommandTexts
        {
            get
            {
                lock (_sink)
                {
                    return [.. _sink.CommandTexts];
                }
            }
        }

        /// <inheritdoc/>
        public void Dispose() => Current.Value = _previous;
    }

    /// <summary>SqlClient の診断の通知を受ける（名前が <see cref="SqlClientListenerName"/> の通知のすべて）</summary>
    private sealed class SqlClientObserver
        : IObserver<DiagnosticListener>,
            IObserver<KeyValuePair<string, object?>>
    {
        public void OnNext(DiagnosticListener value)
        {
            if (value.Name == SqlClientListenerName)
            {
                value.Subscribe(this);
            }
        }

        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (
                value.Key.EndsWith(WriteCommandBefore, StringComparison.Ordinal)
                && value.Value?.GetType().GetProperty("Command")?.GetValue(value.Value)
                    is DbCommand command
            )
            {
                Record(command);
            }
        }

        public void OnCompleted() { }

        public void OnError(Exception error) { }
    }

    /// <summary>EF Core のコマンドを流した後（失敗も）にパラメータを読む</summary>
    private sealed class CaptureInterceptor : DbCommandInterceptor
    {
        public override DbDataReader ReaderExecuted(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result
        )
        {
            Record(command);
            return result;
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default
        )
        {
            Record(command);
            return ValueTask.FromResult(result);
        }

        public override int NonQueryExecuted(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result
        )
        {
            Record(command);
            return result;
        }

        public override ValueTask<int> NonQueryExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            Record(command);
            return ValueTask.FromResult(result);
        }

        public override object? ScalarExecuted(
            DbCommand command,
            CommandExecutedEventData eventData,
            object? result
        )
        {
            Record(command);
            return result;
        }

        public override ValueTask<object?> ScalarExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            object? result,
            CancellationToken cancellationToken = default
        )
        {
            Record(command);
            return ValueTask.FromResult(result);
        }

        public override void CommandFailed(DbCommand command, CommandErrorEventData eventData) =>
            Record(command);

        public override Task CommandFailedAsync(
            DbCommand command,
            CommandErrorEventData eventData,
            CancellationToken cancellationToken = default
        )
        {
            Record(command);
            return Task.CompletedTask;
        }
    }
}
