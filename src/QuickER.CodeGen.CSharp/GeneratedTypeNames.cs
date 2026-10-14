using QuickER.Model;

namespace QuickER.CodeGen.CSharp;

/// <summary>
/// 図のテーブル・列から決まる、生成コードが宣言する型の名前（<see cref="GeneratedFixedTypeNames"/> の固定名と対）。
/// </summary>
/// <remarks>
/// <para>
/// 射影 DTO の型名が、生成物の既存の型と同じ名前空間で衝突するのを生成前に止めるための名簿
/// （<see cref="Queries.QueryDefinitionRules.Validate"/> と生成器が共有する）。生成オプション（Repository の種類・EditModel・
/// Mapper・値オブジェクト・同期支援）で出る型は変わるが、ここは<b>出し得る名前をすべて</b>返す。
/// オプションを見て絞ると、保存時（GUI・MCP はオプションを知らない）と生成時で判定が割れ、後からオプションを
/// 変えた瞬間に既存のクエリが壊れるため。
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
        var entityClassName = NameConverter.ToEntityClassName(tableName);
        var repositoryName = ToRepositoryName(entityClassName);

        names.Add(entityClassName);
        names.Add(NameConverter.ToEditModelClassName(tableName));
        names.Add(NameConverter.ToMapperClassName(tableName));
        names.Add($"I{repositoryName}Repository");
        names.Add($"{repositoryName}Repository");
        names.Add($"I{repositoryName}RemoteRepository");
        names.Add($"Http{repositoryName}RemoteRepository");
        names.Add($"InMemory{repositoryName}Repository");
        names.Add($"EfCore{repositoryName}Repository");
        names.Add($"Journaling{repositoryName}Repository");
        names.Add($"{repositoryName}RepositoryBinaryStreamExtensions");
        names.Add($"{repositoryName}UniquenessConstraints");
    }
}
