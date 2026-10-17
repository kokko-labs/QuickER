using QuickER.Model;

namespace QuickER.CodeGen.CSharp;

/// <summary>
/// 図のテーブル・列から決まる、生成コードが宣言する型の名前（<see cref="GeneratedFixedTypeNames"/> の固定名と対）。
/// </summary>
/// <remarks>
/// <para>
/// 生成前に型名の衝突を止めるための名簿で、2 つの検査が共有する。射影 DTO の型名が生成物の既存の型と同じ名前空間で
/// 衝突するかの検査（<see cref="Queries.QueryDefinitionRules.Validate"/> と生成器）と、テーブル・列から決まる型名どうし・
/// 固定名とのぶつかりの検査（<see cref="Analyze"/>）。生成オプション（Repository の種類・EditModel・Mapper・
/// 値オブジェクト・同期支援）と出力の形（分割・層別）で出る型と名前空間は変わるが、ここは<b>出し得る名前をすべて</b>返す。
/// オプションを見て絞ると、保存時（GUI・MCP はオプションを知らない）と生成時で判定が割れ、後からオプションを
/// 変えた瞬間に生成できていた図が壊れるため。その代わり、ある構成ではコンパイルできる組も断る。
/// </para>
/// <para>
/// 名前の派生規則は生成器の各所（Repository・リモート・同期・値オブジェクト）が持つ規則の写しなので、
/// 実際の生成出力が宣言する型名がここと固定名の和に収まることを <c>GeneratedTypeNameDriftTests</c> が表明する。
/// </para>
/// </remarks>
public static class GeneratedTypeNames
{
    private static readonly CSharpNameConverter NameConverter = new();

    /// <summary>エンティティクラス名から Repository の基本名を得る（末尾の <c>Entity</c> を外す）</summary>
    internal static string ToRepositoryName(string entityClassName) =>
        entityClassName.EndsWith("Entity", StringComparison.Ordinal)
            ? entityClassName[..^"Entity".Length]
            : entityClassName;

    /// <summary>
    /// テーブル名から決まるリモートのルート名（Repository の基本名＝エンドポイントの <c>{prefix}/{ルート名}/{操作}</c>）
    /// </summary>
    internal static string ToRemoteRouteName(string tableName) =>
        ToRepositoryName(NameConverter.ToEntityClassName(tableName));

    /// <summary>図の全テーブル・全列から決まる生成型名をすべて集める</summary>
    public static HashSet<string> Collect(IEnumerable<Entity> entities)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entity in entities)
        {
            AddForTable(entity.TableName, names);

            foreach (var column in entity.Columns)
            {
                names.Add(NameConverter.ToValueObjectClassName(column.Name));
            }
        }

        return names;
    }

    /// <summary>1 つのテーブルから決まる生成型名（エンティティ・EditModel・Mapper・Repository 系）を足す</summary>
    internal static void AddForTable(string tableName, ISet<string> names)
    {
        foreach (var name in ForTable(tableName))
        {
            names.Add(name);
        }
    }

    /// <summary>1 つのテーブルから決まる生成型名（エンティティ・EditModel・Mapper・Repository 系）</summary>
    private static IEnumerable<string> ForTable(string tableName)
    {
        var entityClassName = NameConverter.ToEntityClassName(tableName);
        var repositoryName = ToRepositoryName(entityClassName);

        yield return entityClassName;
        yield return NameConverter.ToEditModelClassName(tableName);
        yield return NameConverter.ToMapperClassName(tableName);
        yield return $"I{repositoryName}Repository";
        yield return $"{repositoryName}Repository";
        yield return $"I{repositoryName}RemoteRepository";
        yield return $"Http{repositoryName}RemoteRepository";
        yield return $"InMemory{repositoryName}Repository";
        yield return $"EfCore{repositoryName}Repository";
        yield return $"Journaling{repositoryName}Repository";
        yield return $"{repositoryName}RepositoryBinaryStreamExtensions";
        yield return $"{repositoryName}UniquenessConstraints";
    }

    /// <summary>
    /// テーブル・列から決まる型名どうしのぶつかり、固定名とのぶつかり、大文字小文字だけ違うルート名と型名を調べる
    /// </summary>
    /// <remarks>
    /// <para>
    /// 判定は構成と名前空間に依らない（名簿と同じく出し得る名前をすべて見る）。型名の比較は C# と同じ Ordinal で、
    /// 大文字小文字を区別しない比較はリモートのルート名にだけ掛ける（ASP.NET のルーティングが大小を区別しない。
    /// 型名をほかに大小を区別せずに読む後段は無い）。ルート名は、リモートを生成しない構成でも同じ規則で止める
    /// （リモートを有効にした瞬間に図が壊れないため）。
    /// </para>
    /// <para>
    /// 値オブジェクトの型名は列名から作るので、別のテーブルの同じ綴りの列は同じ型を共有する（衝突ではない）。
    /// 綴りが大文字小文字だけ違う型名は、止めずに警告の対象として返す（エラーになる名前と、ルート名でエラーにした表の名前と、
    /// プロパティ名が同じか大小だけ違うためにエラーにした列〔<see cref="ColumnPropertyNameCollisions"/>〕の値オブジェクトの名前を除く）。
    /// </para>
    /// </remarks>
    internal static GeneratedTypeNameReport Analyze(IEnumerable<Entity> entities)
    {
        var entries = new List<(string Name, GeneratedTypeNameSource Source)>();
        var tables = new List<string>();
        var refusedColumns = new HashSet<GeneratedTypeNameSource>();

        foreach (
            var entity in entities.Where(entity => !string.IsNullOrWhiteSpace(entity.TableName))
        )
        {
            tables.Add(entity.TableName);

            // プロパティ名が同じか大小だけ違う列は生成前の検証がエラーにするので、警告の出どころから外す
            foreach (var collision in ColumnPropertyNameCollisions.Find(entity))
            {
                refusedColumns.UnionWith(
                    collision.Columns.Select(column => new GeneratedTypeNameSource(
                        entity.TableName,
                        column
                    ))
                );
            }

            foreach (var name in ForTable(entity.TableName))
            {
                entries.Add((name, new GeneratedTypeNameSource(entity.TableName, null)));
            }

            foreach (
                var column in entity.Columns.Where(column =>
                    !string.IsNullOrWhiteSpace(column.Name)
                )
            )
            {
                entries.Add(
                    (
                        NameConverter.ToValueObjectClassName(column.Name),
                        new GeneratedTypeNameSource(entity.TableName, column.Name)
                    )
                );
            }
        }

        // 同じ綴りの名前を、出どころごとにまとめる（出現順を保つ）
        var byName = new Dictionary<string, List<GeneratedTypeNameSource>>(StringComparer.Ordinal);
        var nameOrder = new List<string>();

        foreach (var (name, source) in entries)
        {
            if (!byName.TryGetValue(name, out var sources))
            {
                sources = [];
                byName.Add(name, sources);
                nameOrder.Add(name);
            }

            if (!sources.Contains(source))
            {
                sources.Add(source);
            }
        }

        var collisions = CollectCollisions(nameOrder, byName);
        var reserved = nameOrder
            .Where(GeneratedFixedTypeNames.IsReserved)
            .Select(name => new GeneratedTypeNameUse(name, byName[name]))
            .ToList();
        var routeCollisions = CollectRouteCaseCollisions(tables);

        var refused = new HashSet<string>(
            collisions
                .SelectMany(collision => collision.Names)
                .Concat(reserved.Select(use => use.Name)),
            StringComparer.Ordinal
        );
        var refusedTables = new HashSet<string>(
            routeCollisions.SelectMany(collision => collision.Tables),
            SchemaNames.Comparer
        );
        var remaining = nameOrder
            .Where(name => !refused.Contains(name))
            .Select(name => new GeneratedTypeNameUse(
                name,
                byName[name]
                    .Where(source =>
                        source.ColumnName is null
                            ? !refusedTables.Contains(source.TableName)
                            : !refusedColumns.Contains(source)
                    )
                    .ToList()
            ))
            .Where(use => use.Sources.Count > 0)
            .ToList();
        var caseVariants = remaining
            .GroupBy(use => use.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => (IReadOnlyList<GeneratedTypeNameUse>)group.ToList())
            .ToList();

        return new GeneratedTypeNameReport(collisions, reserved, routeCollisions, caseVariants);
    }

    /// <summary>
    /// 同じ綴りの型名を、2 つ以上の出どころが作る組を集める（同じ出どころの組の名前は 1 件にまとめる）
    /// </summary>
    /// <remarks>
    /// 出どころはテーブルごとに 1 つ。値オブジェクトの型名は、作る列が何本あっても 1 つの出どころに数える
    /// （同じ綴りの列は型を共有する設計のため）。<c>customer</c> と <c>customers</c> のように表の型名がまとめてぶつかる組は、
    /// ぶつかる名前を並べた 1 件になる。
    /// </remarks>
    private static List<GeneratedTypeNameCollision> CollectCollisions(
        List<string> nameOrder,
        Dictionary<string, List<GeneratedTypeNameSource>> byName
    )
    {
        var collisions = new List<GeneratedTypeNameCollision>();

        foreach (var name in nameOrder)
        {
            var sources = byName[name];
            var owners =
                sources
                    .Where(source => source.ColumnName is null)
                    .Select(source => source.TableName)
                    .Distinct(SchemaNames.Comparer)
                    .Count() + (sources.Any(source => source.ColumnName is not null) ? 1 : 0);

            if (owners < 2)
            {
                continue;
            }

            var existing = collisions.FirstOrDefault(collision =>
                collision.Sources.SequenceEqual(sources)
            );

            if (existing is null)
            {
                collisions.Add(new GeneratedTypeNameCollision([name], sources));
            }
            else
            {
                existing.Names.Add(name);
            }
        }

        return collisions;
    }

    /// <summary>リモートのルート名が大文字小文字だけ違うテーブルの組を集める</summary>
    private static List<RemoteRouteNameCaseCollision> CollectRouteCaseCollisions(
        List<string> tables
    ) =>
        tables
            .GroupBy(ToRemoteRouteName, StringComparer.OrdinalIgnoreCase)
            .Where(group =>
                group.Select(ToRemoteRouteName).Distinct(StringComparer.Ordinal).Count() > 1
            )
            .Select(group => new RemoteRouteNameCaseCollision(
                group.Select(ToRemoteRouteName).Distinct(StringComparer.Ordinal).ToList(),
                group.ToList()
            ))
            .ToList();
}

/// <summary>生成型名 1 つの出どころ（テーブルの名前から決まる型名なら列は null＝値オブジェクトの型名は列が決める）</summary>
internal sealed record GeneratedTypeNameSource(string TableName, string? ColumnName);

/// <summary>型名と、その型名を作る出どころ</summary>
internal sealed record GeneratedTypeNameUse(
    string Name,
    IReadOnlyList<GeneratedTypeNameSource> Sources
);

/// <summary>2 つ以上の出どころが同じ綴りで作る型名（同じ出どころの組の名前をまとめる）</summary>
internal sealed record GeneratedTypeNameCollision(
    List<string> Names,
    IReadOnlyList<GeneratedTypeNameSource> Sources
);

/// <summary>大文字小文字だけ違うリモートのルート名と、それを作るテーブル</summary>
internal sealed record RemoteRouteNameCaseCollision(
    IReadOnlyList<string> RouteNames,
    IReadOnlyList<string> Tables
);

/// <summary><see cref="GeneratedTypeNames.Analyze"/> の結果</summary>
/// <param name="Collisions">同じ綴りの型名を 2 つ以上の出どころが作る組（エラー）</param>
/// <param name="Reserved">生成コードの固定の型名と同じ型名（エラー）</param>
/// <param name="RouteCaseCollisions">大文字小文字だけ違うリモートのルート名（エラー）</param>
/// <param name="CaseVariants">エラーにならずに残る型名のうち、大文字小文字だけ違う綴りの組（警告）</param>
internal sealed record GeneratedTypeNameReport(
    IReadOnlyList<GeneratedTypeNameCollision> Collisions,
    IReadOnlyList<GeneratedTypeNameUse> Reserved,
    IReadOnlyList<RemoteRouteNameCaseCollision> RouteCaseCollisions,
    IReadOnlyList<IReadOnlyList<GeneratedTypeNameUse>> CaseVariants
);
