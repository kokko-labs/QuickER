namespace QuickER.UndoRedo;

/// <summary>Undo / Redo 可能な操作を表すコマンドパターンのインターフェース</summary>
/// <remarks>
/// エンティティの追加・削除・移動・プロパティ変更、リレーションの追加・削除を
/// すべて本インターフェースの実装クラスで表現する
/// </remarks>
public interface IUndoableCommand
{
    /// <summary>履歴一覧に表示する操作の説明</summary>
    string Description { get; }

    /// <summary>操作を適用する</summary>
    void Execute();

    /// <summary>適用済みの操作を元に戻す</summary>
    void Undo();
}
