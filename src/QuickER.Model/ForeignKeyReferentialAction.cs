namespace QuickER.Model;

/// <summary>
/// 外部キー制約の参照アクション（<c>ON DELETE</c> / <c>ON UPDATE</c>）を表す列挙型
/// </summary>
public enum ForeignKeyReferentialAction
{
    /// <summary>親行の更新・削除時に連動処理を行わない既定値</summary>
    NoAction,

    /// <summary>親行の更新・削除を子行へ連鎖適用する</summary>
    Cascade,

    /// <summary>親行の更新・削除時に子行の外部キー列へ <c>NULL</c> を設定する</summary>
    SetNull,

    /// <summary>親行の更新・削除時に子行の外部キー列へ既定値を設定する</summary>
    SetDefault,
}

/// <summary>
/// <see cref="ForeignKeyReferentialAction"/> と文字列表現の相互変換ヘルパー
/// </summary>
public static class ForeignKeyReferentialActionHelper
{
    /// <summary>文字列を参照アクションへ変換する</summary>
    /// <param name="value">DB 取込値などの文字列表現（表記ゆれを許容する）</param>
    /// <returns>対応する参照アクション、解釈できない場合は <see cref="ForeignKeyReferentialAction.NoAction"/></returns>
    public static ForeignKeyReferentialAction Parse(string? value)
    {
        return Normalize(value) switch
        {
            "CASCADE" => ForeignKeyReferentialAction.Cascade,
            "SET NULL" => ForeignKeyReferentialAction.SetNull,
            "SET DEFAULT" => ForeignKeyReferentialAction.SetDefault,
            _ => ForeignKeyReferentialAction.NoAction,
        };
    }

    /// <summary>SQL 句で使用する表記（例: <c>SET NULL</c>）へ変換する</summary>
    public static string ToSqlText(this ForeignKeyReferentialAction action)
    {
        return action switch
        {
            ForeignKeyReferentialAction.Cascade => "CASCADE",
            ForeignKeyReferentialAction.SetNull => "SET NULL",
            ForeignKeyReferentialAction.SetDefault => "SET DEFAULT",
            _ => "NO ACTION",
        };
    }

    /// <summary>画面表示用の表記へ変換する（現状は SQL 表記と同一）</summary>
    public static string ToDisplayText(this ForeignKeyReferentialAction action) =>
        action.ToSqlText();

    /// <summary>外部キー制約の <c>ON DELETE</c> / <c>ON UPDATE</c> 句を組み立てる</summary>
    /// <returns>
    /// 両方が <see cref="ForeignKeyReferentialAction.NoAction"/> の場合は空文字、
    /// それ以外は先頭に半角スペースを含む句（例: <c>" ON DELETE CASCADE"</c>）
    /// </returns>
    public static string BuildReferentialActionClause(
        ForeignKeyReferentialAction onDelete,
        ForeignKeyReferentialAction onUpdate
    )
    {
        var clauses = new List<string>();

        if (onDelete != ForeignKeyReferentialAction.NoAction)
        {
            clauses.Add($"ON DELETE {onDelete.ToSqlText()}");
        }

        if (onUpdate != ForeignKeyReferentialAction.NoAction)
        {
            clauses.Add($"ON UPDATE {onUpdate.ToSqlText()}");
        }

        return clauses.Count == 0 ? string.Empty : " " + string.Join(" ", clauses);
    }

    /// <summary>比較用に表記ゆれ（前後空白・アンダースコア・ハイフン・大小文字）を正規化する</summary>
    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value
                .Trim()
                .Replace("_", " ", StringComparison.Ordinal)
                .Replace("-", " ", StringComparison.Ordinal)
                .ToUpperInvariant();
    }
}
