namespace QuickER.Model;

/// <summary>
/// リレーション（関連）の多重度を表す列挙型
/// </summary>
public enum RelationshipType
{
    /// <summary>1 対 1 の関連</summary>
    OneToOne,

    /// <summary>1 対 多 の関連</summary>
    OneToMany,

    /// <summary>多 対 多 の関連</summary>
    ManyToMany,
}

/// <summary>リレーションの種別を名前（文字列）から読む</summary>
/// <remarks>
/// GUI と MCP の 2 つの ER 図操作ツール（<c>relationship_type</c> 引数）が共有する規則。断るときの文言は各ツールが持つ。
/// </remarks>
public static class RelationshipTypeNames
{
    /// <summary>
    /// 列挙子の名前と大文字小文字を無視して一致すれば、その種別を返す（数字の表記・前後の空白は受け付けない）
    /// </summary>
    /// <param name="text">種別の名前</param>
    /// <param name="type">読めた種別（読めなければ既定値）</param>
    /// <returns>読めたら true。null・空・どの列挙子とも一致しないときは false</returns>
    /// <remarks>
    /// <see cref="Enum.TryParse{TEnum}(string?, bool, out TEnum)"/> を使わないのは、"1" のような数字の表記や
    /// 定義に無い数まで受け付けるため。
    /// </remarks>
    public static bool TryParse(string? text, out RelationshipType type)
    {
        foreach (var candidate in Enum.GetValues<RelationshipType>())
        {
            if (string.Equals(candidate.ToString(), text, StringComparison.OrdinalIgnoreCase))
            {
                type = candidate;
                return true;
            }
        }

        type = default;
        return false;
    }
}
