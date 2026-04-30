using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ERDesigner.UndoRedo;

/// <summary>
/// Undo / Redo を二つのスタックで管理するクラスです。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Execute"/> を使うと、コマンドを実行した上で Undo スタックに積みます。
/// </para>
/// <para>
/// 一方、ドラッグのように「すでに位翮が変更されている」ケースでは
/// <see cref="Push"/> を使い、<c>Execute</c> を再び走らずに履歴のみ登録します。
/// </para>
/// </remarks>
public partial class UndoRedoManager : ObservableObject
{
    private readonly Stack<IUndoableCommand> _undo = new();
    private readonly Stack<IUndoableCommand> _redo = new();

    /// <summary>Undo できるコマンドがあるかどうか。</summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>Redo できるコマンドがあるかどうか。</summary>
    public bool CanRedo => _redo.Count > 0;

    /// <summary>
    /// コマンドを実行し、Undo スタックに積みます。Redo スタックは破棄されます。
    /// </summary>
    /// <param name="command">実行するコマンド。</param>
    public void Execute(IUndoableCommand command)
    {
        command.Execute();
        _undo.Push(command);
        _redo.Clear();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    /// <summary>
    /// すでに適用済みのコマンドを Undo スタックに登録します（ドラッグ終了時用）。
    /// </summary>
    /// <param name="command">登録するコマンド。</param>
    public void Push(IUndoableCommand command)
    {
        _undo.Push(command);
        _redo.Clear();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    /// <summary>直近の操作を元に戻します。</summary>
    public void Undo()
    {
        if (!CanUndo) return;
        var c = _undo.Pop();
        c.Undo();
        _redo.Push(c);
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    /// <summary>直前に Undo した操作をやり直します。</summary>
    public void Redo()
    {
        if (!CanRedo) return;
        var c = _redo.Pop();
        c.Execute();
        _undo.Push(c);
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    /// <summary>Undo / Redo スタックをすべてクリアします。</summary>
    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }
}
