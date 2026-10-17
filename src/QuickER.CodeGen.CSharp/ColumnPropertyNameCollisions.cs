using QuickER.Model;

namespace QuickER.CodeGen.CSharp;

/// <summary>
/// 1 つのテーブルの列のうち、生成するプロパティ名が同じになるか大文字小文字だけ違う列の組を求める。
/// </summary>
/// <remarks>
/// <para>
/// 列名はパスカルケースへ変換してプロパティ名にするので、<c>user-id</c>・<c>user_id</c>・<c>USER_ID</c> は同じ <c>UserId</c> になり
/// （同名メンバーの二重宣言＝CS0102）、<c>user_id</c> と <c>userid</c> は大文字小文字だけ違う <c>UserId</c> と <c>Userid</c> になる。
/// 後者はコンパイルできるが、エンティティを読み書きする JSON の設定（リモートの転送・SQL Server の Include の読み込み）が
/// プロパティ名の大文字小文字を区別しないので、実行時に型の情報を組み立てる時点で例外になる。どちらも生成時に止める。
/// </para>
/// <para>
/// 判定はここだけが持ち、生成前の検証（<c>CSharpCodeGenerationService.ValidateColumnPropertyNameUniqueness</c>）がエラーにし、
/// 型名の大小違いの警告（<see cref="GeneratedTypeNames.Analyze"/>）がエラーにした列を出どころから外す。
/// </para>
/// </remarks>
internal static class ColumnPropertyNameCollisions
{
    private static readonly CSharpNameConverter NameConverter = new();

    /// <summary>テーブルの列のうち、プロパティ名が同じか大文字小文字だけ違う列の組（列の並び順で最初に現れた名前から）</summary>
    internal static IReadOnlyList<ColumnPropertyNameCollision> Find(Entity entity)
    {
        // 生成する識別子どうしの比較（C# と JSON の規則）で、図の名前の同一性ではない
        var groups = new Dictionary<string, (List<string> PropertyNames, List<string> Columns)>(
            StringComparer.OrdinalIgnoreCase
        );
        var order = new List<string>();

        foreach (
            var column in entity.Columns.Where(column => !string.IsNullOrWhiteSpace(column.Name))
        )
        {
            var propertyName = NameConverter.ToPropertyName(column.Name);

            if (!groups.TryGetValue(propertyName, out var group))
            {
                group = ([], []);
                groups.Add(propertyName, group);
                order.Add(propertyName);
            }

            if (!group.PropertyNames.Contains(propertyName, StringComparer.Ordinal))
            {
                group.PropertyNames.Add(propertyName);
            }

            group.Columns.Add(column.Name);
        }

        return order
            .Select(key => groups[key])
            .Where(group => group.Columns.Count > 1)
            .Select(group => new ColumnPropertyNameCollision(group.PropertyNames, group.Columns))
            .ToList();
    }
}

/// <summary>プロパティ名が同じか大文字小文字だけ違う列の組</summary>
/// <param name="PropertyNames">生成するプロパティ名（綴りの違うものを出現順に。1 つなら同じ名前の重複）</param>
/// <param name="Columns">その列の名前（列の並び順）</param>
internal sealed record ColumnPropertyNameCollision(
    IReadOnlyList<string> PropertyNames,
    IReadOnlyList<string> Columns
);
