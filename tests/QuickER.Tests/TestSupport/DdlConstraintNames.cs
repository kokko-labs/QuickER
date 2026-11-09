using System.Text.RegularExpressions;
using QuickER.Model;
using QuickER.Provider.SqlServer;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// 図の SQL Server の DDL が付ける制約名（<c>CONSTRAINT [..]</c>）の重複を数える共有の補助
/// </summary>
/// <remarks>
/// 制約名は SQL Server ではスキーマ単位で種類（主キー・一意制約・外部キー）をまたいで共有されるので、同じ名前が 2 回出る DDL は
/// 適用で断られる。同じ名前かは名前の同一性の規則（<see cref="SchemaNames.Comparer"/>）で束ねる。
/// 形式の往復のエンジン（取り込んだ図）と操作の往復の表（各段の図）が使う。
/// </remarks>
internal static class DdlConstraintNames
{
    /// <summary>SQL Server の角括弧の識別子（閉じ角括弧は 2 つ重ねて書く）</summary>
    private static readonly Regex ConstraintPattern = new(
        @"CONSTRAINT \[((?:[^\]]|\]\])+)\]",
        RegexOptions.CultureInvariant
    );

    /// <summary>図の DDL に 2 回以上出る制約名（最初に出た綴り）</summary>
    /// <param name="diagram">対象の図</param>
    /// <returns>重複した制約名（無ければ空）</returns>
    /// <exception cref="InvalidOperationException">図から DDL を作れない（DDL の入口が断る図）</exception>
    public static IReadOnlyList<string> Duplicates(ErDiagram diagram) =>
        ConstraintPattern
            .Matches(new SqlServerDdlGenerator().Build(diagram))
            .Select(match => match.Groups[1].Value.Replace("]]", "]", StringComparison.Ordinal))
            .GroupBy(name => name, SchemaNames.Comparer)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

    /// <summary>図の DDL に 2 回以上出る制約名を求める（DDL を作れない図は偽）</summary>
    /// <param name="diagram">対象の図</param>
    /// <param name="duplicates">重複した制約名（DDL を作れないときは空）</param>
    /// <returns>DDL を作れたか</returns>
    public static bool TryFindDuplicates(ErDiagram diagram, out IReadOnlyList<string> duplicates)
    {
        try
        {
            duplicates = Duplicates(diagram);
            return true;
        }
        catch (InvalidOperationException)
        {
            duplicates = [];
            return false;
        }
    }
}
