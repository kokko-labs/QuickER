namespace QuickER.CodeGen.CSharp.Queries;

/// <summary>
/// 名前付きクエリ条件（ミニ DSL）内の列参照を、図上の列リネームに追従して書き換えるヘルパー
/// </summary>
/// <remarks>
/// 条件はミニ DSL の文字列で、列名だけは自由テキストになるため、GUI で列名が変更されたときに
/// 参照を新名へ寄せてやる必要がある。<see cref="QueryConditionParser.Parse"/> が返す列参照の
/// 位置情報（<see cref="ColumnReference.Position"/> / <see cref="ColumnReference.Length"/>）を
/// 使ってスパン置換するため、パラメータ名や文字列リテラル中にたまたま同名の文字列があっても
/// 巻き込まない。パース不能（構文エラー）な条件は解析結果が信用できないため原文のまま返す。
/// </remarks>
public static class QueryConditionRenamer
{
    /// <summary>
    /// 条件式内の列参照（旧列名・大文字小文字無視）を新列名へ置換する。パース不能な条件はそのまま返す
    /// </summary>
    /// <param name="conditionText">ミニ DSL の条件式</param>
    /// <param name="oldColumnName">置換対象の旧列名</param>
    /// <param name="newColumnName">置換後の新列名</param>
    public static string RenameColumn(
        string conditionText,
        string oldColumnName,
        string newColumnName
    )
    {
        ArgumentNullException.ThrowIfNull(conditionText);
        ArgumentNullException.ThrowIfNull(oldColumnName);
        ArgumentNullException.ThrowIfNull(newColumnName);

        // 旧名と新名が同一（大文字小文字も含めて）なら書き換え不要
        if (string.Equals(oldColumnName, newColumnName, StringComparison.Ordinal))
        {
            return conditionText;
        }

        var parsed = QueryConditionParser.Parse(conditionText);

        // 構文エラーの条件は列参照の位置が信用できないため、書き換えず原文を返す
        if (parsed.Root is null)
        {
            return conditionText;
        }

        // 旧名に一致する列参照だけを対象にする（大文字小文字は区別しない）
        var targets = parsed
            .ColumnReferences.Where(reference =>
                string.Equals(reference.Text, oldColumnName, StringComparison.OrdinalIgnoreCase)
            )
            // 後ろのスパンから置換して、前方の Position がずれないようにする
            .OrderByDescending(reference => reference.Position)
            .ToList();

        if (targets.Count == 0)
        {
            return conditionText;
        }

        var text = conditionText;

        foreach (var reference in targets)
        {
            text =
                text[..reference.Position]
                + newColumnName
                + text[(reference.Position + reference.Length)..];
        }

        return text;
    }
}
