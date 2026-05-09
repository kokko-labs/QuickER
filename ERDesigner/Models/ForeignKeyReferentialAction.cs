namespace ERDesigner.Models;

/// <summary>
/// 外部キー制約の参照アクションを表します。
/// </summary>
public enum ForeignKeyReferentialAction
{
    /// <summary>親行更新・削除時に特別な連動を行わない既定値です。</summary>
    NoAction,

    /// <summary>親行の更新・削除を子行へ連鎖適用します。</summary>
    Cascade,

    /// <summary>子行の外部キー列へ <c>NULL</c> を設定します。</summary>
    SetNull,

    /// <summary>子行の外部キー列へ既定値を設定します。</summary>
    SetDefault,
}

/// <summary>
/// <see cref="ForeignKeyReferentialAction" /> の変換ヘルパーです。
/// </summary>
public static class ForeignKeyReferentialActionHelper
{
    /// <summary>文字列を参照アクションへ変換します。</summary>
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

    /// <summary>SQL 句向けの表記へ変換します。</summary>
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

    /// <summary>表示用の表記へ変換します。</summary>
    public static string ToDisplayText(this ForeignKeyReferentialAction action) => action.ToSqlText();

    /// <summary>比較しやすいように表記ゆれを正規化します。</summary>
    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Replace("_", " ", StringComparison.Ordinal).Replace("-", " ", StringComparison.Ordinal).ToUpperInvariant();
    }
}
