namespace QuickER.Provider;

/// <summary>
/// DB 型表記（<c>Column.DataType</c>）どうしを「同じ型宣言か」で比較する共有規則。
/// </summary>
/// <remarks>
/// 前後の空白と大文字・小文字の差は型宣言の意味を変えないため、同じ型として扱う。
/// これはスキーマ同期が <c>ALTER COLUMN</c> を出すかどうかの判定そのもの（<c>SchemaDiffService</c>）であり、
/// 「型表記が実害のある形で変わったか」を語る箇所はすべてこの規則へ揃える
/// （<c>CanonicalTypeTokenAttacher</c> が「トークン経由の書き戻しで綴りが変わる列」を判定するのにも使う。
/// ここを厳密一致にすると、<c>TryFormat</c> が大文字を返す SQLite 方言の図で全列が「変わった」と判定される）。
/// </remarks>
public static class DbTypeText
{
    /// <summary>2 つの型表記が同じ型宣言を表すか（前後の空白を無視し、大文字・小文字も区別しない）</summary>
    /// <param name="left">比較対象の型表記（<c>null</c> は空文字として扱う）</param>
    /// <param name="right">比較対象の型表記（<c>null</c> は空文字として扱う）</param>
    public static bool AreEquivalent(string? left, string? right) =>
        string.Equals(
            (left ?? string.Empty).Trim(),
            (right ?? string.Empty).Trim(),
            StringComparison.OrdinalIgnoreCase
        );
}
