using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// SqlClient が発行するコマンドの本文を DiagnosticListener で捕まえる（指定した語を含むものだけ）。終わったコマンドごとに
/// 呼ぶ処理も渡せる。
/// </summary>
internal sealed class SqlCommandObserver
    : IObserver<DiagnosticListener>,
        IObserver<KeyValuePair<string, object?>>,
        IDisposable
{
    private readonly string _marker;
    private readonly Action<string>? _afterCommand;
    private readonly List<string> _commands = [];
    private readonly List<IDisposable> _subscriptions = [];

    public SqlCommandObserver(string marker, Action<string>? afterCommand = null)
    {
        _marker = marker;
        _afterCommand = afterCommand;
        _subscriptions.Add(DiagnosticListener.AllListeners.Subscribe(this));
    }

    /// <summary>捕まえたコマンドの本文</summary>
    public IReadOnlyList<string> Commands
    {
        get
        {
            lock (_commands)
            {
                return _commands.ToList();
            }
        }
    }

    public void OnNext(DiagnosticListener value)
    {
        if (value.Name == "SqlClientDiagnosticListener")
        {
            lock (_subscriptions)
            {
                _subscriptions.Add(value.Subscribe(this));
            }
        }
    }

    public void OnNext(KeyValuePair<string, object?> value)
    {
        var before = value.Key.EndsWith("WriteCommandBefore", StringComparison.Ordinal);
        var after = value.Key.EndsWith("WriteCommandAfter", StringComparison.Ordinal);

        if (!before && !after)
        {
            return;
        }

        if (
            value.Value?.GetType().GetProperty("Command")?.GetValue(value.Value)
                is not System.Data.Common.DbCommand { CommandText: { } text }
            || !text.Contains(_marker, StringComparison.Ordinal)
        )
        {
            return;
        }

        if (before)
        {
            lock (_commands)
            {
                _commands.Add(text);
            }
        }
        else
        {
            _afterCommand?.Invoke(text);
        }
    }

    public void OnCompleted() { }

    public void OnError(Exception error) { }

    public void Dispose()
    {
        lock (_subscriptions)
        {
            foreach (var subscription in _subscriptions)
            {
                subscription.Dispose();
            }

            _subscriptions.Clear();
        }
    }
}
