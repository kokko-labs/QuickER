namespace QuickER.Model;

/// <summary>
/// 制約名の安全化と、名前を持たない制約の既定名（外部キー・主キー）を組み立てる規則の正本
/// </summary>
/// <remarks>
/// <para>
/// DDL 生成・DB 同期・GUI のリレーション作成・内蔵チャットと MCP のツールが、同じテーブルに対して同じ名前を
/// 作らなければならない。とくに同期は「DDL が作ったはずの名前」で制約を探して外すので、片方だけ規則が
/// ずれると、存在しない名前の制約を外そうとして失敗する。
/// </para>
/// <para>
/// <b>既定名はここのメソッドで組み立てること</b>（<c>$"FK_{…}"</c> / <c>$"PK_{…}"</c> の補間や
/// <c>"FK_" + …</c> の連結を呼び出し側で書かない）。規則が写されるとそれぞれが別々に古くなるため、
/// 写しが無いことは <c>ConstraintNamesGuardTests</c> がソースを走査して確かめる。
/// 一意制約の合成名（<see cref="UniqueConstraint.SynthesizeName"/>）も同じ <see cref="SafeName"/> を通す。
/// </para>
/// <para>
/// モデル側に置くのは、Provider を参照しない MCP ツールホストも同じ規則を使うため
/// （<see cref="UniqueConstraint.SynthesizeName"/> と同じ理由）。
/// 識別子の囲み方（クォート）は方言ごとに本当に違うので、ここへは入れず各方言に残す。
/// </para>
/// </remarks>
public static class ConstraintNames
{
    /// <summary>名前を制約名の部品として安全な形へ整える</summary>
    /// <param name="name">テーブル名・列名（<c>null</c> は空文字として扱う）</param>
    /// <returns>前後の空白を除き、<c>.</c> と空白を <c>_</c> へ置き換えた名前</returns>
    /// <remarks>
    /// 前後の空白を先に除くのは、同期の計画がテーブル名を <c>Trim</c> してから制約名を組み立てるため
    /// （除かないと先頭の空白が <c>_</c> になり、DDL の名前と一致しない）。
    /// 冪等（2 回通しても変わらない）なので、安全化済みの名前をもう一度通しても結果は同じ。
    /// </remarks>
    public static string SafeName(string? name) =>
        (name ?? string.Empty).Trim().Replace(".", "_").Replace(" ", "_");

    /// <summary>名前を持たない外部キーの既定名を組み立てる</summary>
    /// <param name="childTable">参照する側（外部キーを持つ側）のテーブル名</param>
    /// <param name="parentTable">参照される側のテーブル名</param>
    /// <returns><c>FK_{子}_{親}</c>（それぞれ <see cref="SafeName"/> を通したもの）</returns>
    public static string ForeignKey(string childTable, string parentTable) =>
        "FK_" + SafeName(childTable) + "_" + SafeName(parentTable);

    /// <summary>主キーの既定名を組み立てる</summary>
    /// <param name="tableName">テーブル名</param>
    /// <returns><c>PK_{テーブル}</c>（<see cref="SafeName"/> を通したもの）</returns>
    public static string PrimaryKey(string tableName) => "PK_" + SafeName(tableName);
}
