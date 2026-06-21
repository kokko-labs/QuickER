using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ERDesigner.UndoRedo;

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

    /// <summary>コマンドを実行し Undo スタックへ積む（Redo スタックは破棄する）</summary>
    /// <param name="command">実行するコマンド</param>
    public void Execute(IUndoableCommand command)
    {
        command.Execute();
        _undo.Push(command);
        _redo.Clear();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    /// <summary>適用済みコマンドを Undo スタックへ登録する（ドラッグ終了時など）</summary>
    /// <param name="command">登録するコマンド</param>
    public void Push(IUndoableCommand command)
    {
        // グループ ID 付きのプロパティ変更は同一グループの履歴へ集約する
        if (command is PropertyChangeCommand propertyChange && propertyChange.GroupId is not null)
        {
            // スタック先頭が同一グループの複合コマンドなら、そこへマージして履歴の肥大化を防ぐ
            if (
                _undo.TryPeek(out var last)
                && last is CompositeUndoableCommand composite
                && Equals(composite.GroupId, propertyChange.GroupId)
            )
            {
                composite.Upsert(propertyChange);
                _redo.Clear();
                OnPropertyChanged(nameof(CanUndo));
                OnPropertyChanged(nameof(CanRedo));
                return;
            }

            var grouped = new CompositeUndoableCommand(
                propertyChange.GroupId,
                propertyChange.Description
            );
            grouped.Upsert(propertyChange);
            _undo.Push(grouped);
            _redo.Clear();
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
            return;
        }

        _undo.Push(command);
        _redo.Clear();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
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
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
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
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    /// <summary>Undo / Redo スタックをすべてクリアする</summary>
    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    /// <summary>同時に発生した複数のプロパティ変更を 1 履歴として扱う複合コマンド</summary>
    private sealed class CompositeUndoableCommand(object groupId, string description)
        : IUndoableCommand
    {
        /// <summary>集約済みの個別プロパティ変更コマンド</summary>
        private readonly List<PropertyChangeCommand> _commands = new();

        /// <summary>集約判定に用いるグループ ID</summary>
        public object GroupId { get; } = groupId;

        /// <inheritdoc />
        public string Description { get; } = description;

        /// <inheritdoc />
        public void Execute()
        {
            foreach (var command in _commands)
            {
                command.Execute();
            }
        }

        /// <inheritdoc />
        public void Undo()
        {
            // Undo は適用と逆順で巻き戻し、相互依存するプロパティの整合性を保つ
            for (var i = _commands.Count - 1; i >= 0; i--)
            {
                _commands[i].Undo();
            }
        }

        /// <summary>同一対象・同一プロパティの変更は最新値で置換し、それ以外は追加する</summary>
        public void Upsert(PropertyChangeCommand command)
        {
            var existingIndex = _commands.FindIndex(x =>
                ReferenceEquals(x.Target, command.Target) && x.PropertyName == command.PropertyName
            );

            if (existingIndex >= 0)
            {
                _commands[existingIndex] = command;
            }
            else
            {
                _commands.Add(command);
            }

            var ordered = _commands.ToList();
            _commands.Clear();
            _commands.AddRange(ordered);
        }
    }
}
