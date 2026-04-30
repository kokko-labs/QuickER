namespace ERDesigner.UndoRedo;

/// <summary>
/// 「Undo / Redo 可能な操作」を表すコマンドパターンのインターフェースです。
/// </summary>
/// <remarks>
/// エンティティ追加・削除・移動・プロパティ変更、
/// リレーション追加・削除はすべてこのインターフェースを実装したクラスで表現します。
/// </remarks>
public interface IUndoableCommand
{
    /// <summary>履歴一覧に表示するための人間が読める説明。</summary>
    string Description { get; }

    /// <summary>操作を適用します（追加・削除を行うなど）。</summary>
    void Execute();

    /// <summary>適用された操作を元に戻します。</summary>
    void Undo();
}
