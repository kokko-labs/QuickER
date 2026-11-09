namespace QuickER.ViewModels;

/// <summary>
/// プロパティパネルの名前の書き換えを、同じ名前（<see cref="QuickER.Model.SchemaNames.AreSame"/> の規則）の要素があるため
/// 受け付けなかったことを知らせるイベント引数
/// </summary>
public sealed class NameChangeRejectedEventArgs : EventArgs
{
    /// <summary>名前の書き換えの拒否を表す引数を作る</summary>
    /// <param name="requestedName">書き換えようとした名前（前後の空白を除いた形）</param>
    /// <param name="existingName">すでにある同じ名前の要素の名前</param>
    /// <param name="tableName">列の名前の書き換えなら、その列を持つテーブル名。テーブル名の書き換えなら <c>null</c></param>
    public NameChangeRejectedEventArgs(string requestedName, string existingName, string? tableName)
        : this(requestedName, existingName, tableName, isConstraintName: false) { }

    private NameChangeRejectedEventArgs(
        string requestedName,
        string existingName,
        string? tableName,
        bool isConstraintName
    )
    {
        RequestedName = requestedName;
        ExistingName = existingName;
        TableName = tableName;
        IsConstraintName = isConstraintName;
    }

    /// <summary>制約名（一意制約の名前・外部キーの制約名）の書き換えの拒否を表す引数を作る</summary>
    /// <param name="requestedName">書き換えようとした名前（前後の空白を除いた形。図の中のほかの制約名とぶつかる）</param>
    public static NameChangeRejectedEventArgs ForConstraintName(string requestedName) =>
        new(requestedName, requestedName, null, isConstraintName: true);

    /// <summary>書き換えようとした名前（前後の空白を除いた形）</summary>
    public string RequestedName { get; }

    /// <summary>すでにある同じ名前の要素の名前（制約名の拒否では書き換えようとした名前と同じ）</summary>
    public string ExistingName { get; }

    /// <summary>列の名前の書き換えなら、その列を持つテーブル名。テーブル名・制約名の書き換えなら <c>null</c></summary>
    public string? TableName { get; }

    /// <summary>制約名（一意制約の名前・外部キーの制約名）の書き換えの拒否か</summary>
    public bool IsConstraintName { get; }
}
