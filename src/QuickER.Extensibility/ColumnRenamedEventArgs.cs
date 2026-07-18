namespace QuickER.Extensibility;

/// <summary>
/// カラム名がユーザー編集で変更されたことをフィーチャーモジュールへ通知するイベント引数。
/// </summary>
/// <remarks>
/// 名前付きクエリの条件式（ミニ DSL）追従など、カラム名に依存する状態を保守するモジュールが
/// <see cref="IErDiagramHost.ColumnRenamed"/> を購読して利用する。
/// </remarks>
public sealed class ColumnRenamedEventArgs : EventArgs
{
    /// <summary>名前が変更された列を保持するエンティティの識別子</summary>
    public Guid EntityId { get; }

    /// <summary>変更前のカラム名</summary>
    public string OldName { get; }

    /// <summary>変更後のカラム名</summary>
    public string NewName { get; }

    /// <summary><see cref="ColumnRenamedEventArgs"/> を生成する</summary>
    /// <param name="entityId">名前が変更された列を保持するエンティティの識別子</param>
    /// <param name="oldName">変更前のカラム名</param>
    /// <param name="newName">変更後のカラム名</param>
    public ColumnRenamedEventArgs(Guid entityId, string oldName, string newName)
    {
        EntityId = entityId;
        OldName = oldName;
        NewName = newName;
    }
}
