using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace QuickER.UndoRedo;

/// <summary>Undo / Redo を 2 本のスタックで管理するクラス</summary>
/// <remarks>
/// <para><see cref="Execute"/> はコマンドを実行したうえで Undo スタックへ積む</para>
/// <para>
/// ドラッグのように既に状態変更が済んでいるケースでは <see cref="Push"/> を使い
/// <c>Execute</c> を再実行せず履歴のみ登録する
/// </para>
/// </remarks>
public partial class UndoRedoManager : ObservableObject
{
    /// <summary>Undo 用スタック（適用済みコマンドを後入れ先出しで保持する）</summary>
    private readonly Stack<IUndoableCommand> _undo = new();

    /// <summary>Redo 用スタック（Undo されたコマンドを保持する）</summary>
    private readonly Stack<IUndoableCommand> _redo = new();

    /// <summary>Undo 可能なコマンドの有無</summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>Redo 可能なコマンドの有無</summary>
    public bool CanRedo => _redo.Count > 0;

    /// <summary>状態変更（Execute / Push / Undo / Redo / Clear）ごとに増加する世代カウンタ</summary>
    /// <remarks>
    /// 「読込／保存以降に変更があったか」（ダーティ判定）に用いる。値が変わったかどうかだけが意味を持ち、
    /// 具体的な数値そのものに意味はない。Undo で保存時点へ戻っても世代は進むため、ダーティ判定は安全側
    /// （変更あり扱い）になる。単調増加のため状態変化の検知にそのまま使える。
    /// </remarks>
    public int ChangeGeneration { get; private set; }

    /// <summary>世代カウンタを 1 進め、Undo/Redo の可否と併せて変更通知を発行する</summary>
    private void NotifyStateChanged()
    {
        ChangeGeneration++;
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(ChangeGeneration));
    }

    /// <summary>コマンドを実行し Undo スタックへ積む（Redo スタックは破棄する）</summary>
    /// <param name="command">実行するコマンド</param>
    public void Execute(IUndoableCommand command)
    {
        command.Execute();
        _undo.Push(command);
        _redo.Clear();
        NotifyStateChanged();
    }

    /// <summary>適用済みコマンドを Undo スタックへ登録する（ドラッグ終了時など）</summary>
    /// <param name="command">登録するコマンド</param>
    public void Push(IUndoableCommand command)
    {
        _undo.Push(command);
        _redo.Clear();
        NotifyStateChanged();
    }

    /// <summary>直近の操作を元に戻す</summary>
    public void Undo()
    {
        if (!CanUndo)
        {
            return;
        }

        var c = _undo.Pop();
        c.Undo();
        _redo.Push(c);
        NotifyStateChanged();
    }

    /// <summary>直前に Undo した操作をやり直す</summary>
    public void Redo()
    {
        if (!CanRedo)
        {
            return;
        }

        var c = _redo.Pop();
        c.Execute();
        _undo.Push(c);
        NotifyStateChanged();
    }

    /// <summary>履歴へ積まずに変更世代だけを 1 進める（Undo 非対象だが保存文書に影響する変更用）</summary>
    /// <remarks>
    /// <para>
    /// 名前付きクエリの差し替え・エンティティ表示幅の変更のように、Undo 履歴へは登録しない一方で
    /// 保存文書（スキーマ＋レイアウト）の内容を変える操作で呼ぶ。呼ばないと「変更したのにダーティでない」
    /// 状態になり、外部変更の自動再読込や新規作成で無警告に失われる。
    /// </para>
    /// <para>
    /// Undo / Redo スタックには一切触れないため <see cref="CanUndo"/> / <see cref="CanRedo"/> は変わらず、
    /// <see cref="ChangeGeneration"/> だけが進む（＝ダーティ判定のみが「変更あり」へ動く）。
    /// </para>
    /// </remarks>
    public void MarkChanged()
    {
        NotifyStateChanged();
    }

    /// <summary>Undo / Redo スタックをすべてクリアする</summary>
    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        NotifyStateChanged();
    }
}
