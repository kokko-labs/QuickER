using Microsoft.CodeAnalysis;
using QuickER.CodeGen.CSharp;
using QuickER.Dialects;
using static QuickER.Tests.CodeGen.CSharp.CodegenConfig.CodegenConfigDimensions;

namespace QuickER.Tests.CodeGen.CSharp.CodegenConfig.Judges;

/// <summary>
/// 生成コードの意味（Roslyn の意味モデル：型・メンバー・属性・基底）を、図の形と有効な機能から決まる規則と照合する
/// </summary>
/// <remarks>
/// <para>規則と出どころ（docs/code-generation.md と CLAUDE.md の節）：</para>
/// <list type="bullet">
///   <item>表ごとに <c>{幹}Entity</c>（常に）・<c>{幹}EditModel</c>・<c>{幹}Mapper</c>（各オプション）。型名は docs「Generated type names」。</item>
///   <item>Repository 契約 <c>I{幹}Repository</c> は契約を生成するとき単一主キーの表だけ（docs「What gets generated」の前提）。
///   QuickER 版の実装 <c>{幹}Repository</c> は方言ごとに 1 つ（方言の名前空間）、<c>EfCore{幹}Repository</c>・<c>InMemory{幹}Repository</c>・
///   <c>I{幹}RemoteRepository</c>・<c>Http{幹}RemoteRepository</c>・<c>Journaling{幹}Repository</c> は各機能（CLAUDE.md「DB アクセスの排他と単独出力」
///   「リモート契約生成」「HTTP クライアントのバケット分離」・docs「Bidirectional sync support」）。</item>
///   <item>同期の記述子 <c>GeneratedSyncTables</c>、EF Core の <c>QuickErDbContext</c> と主キーを持つ表ごとの <c>DbSet</c>（Fluent の構成の中身は見ない）。</item>
///   <item>DI の拡張メソッド：<c>AddGeneratedSqlServerRepositories</c> / <c>AddGeneratedSqliteRepositories</c>（方言ごと）・
///   <c>AddGeneratedEfCoreRepositories</c>・<c>AddGeneratedInMemoryRepositories</c>・<c>AddGeneratedHttpRemoteRepositories</c>。</item>
///   <item>列の属性：計算列に <c>[ComputedColumn]</c>、主キーでない採番列に <c>[IdentityColumn]</c>、行バージョンに解決した列にだけ
///   <c>[StoreGeneratedColumn]</c>、除外 ON の無制限バイナリに <c>[UnboundedBinaryColumn]</c>（CLAUDE.md「DB が値を作る列の書き込み除外」）、
///   注釈 ON で <c>[DbColumnMeta]</c>（CLAUDE.md「DB 定義メタ属性」）、親参照ナビゲーションの <c>[JsonIgnore]</c> はオプションどおり。</item>
///   <item>値オブジェクト：外部キーの列は親の列の値オブジェクト型を共有する（CLAUDE.md「FK 列の値オブジェクト型統一」）。
///   GuidKey は文字列の主キーの値オブジェクトの基底が <c>ValueObjectGuidKeyBase</c>（docs「GUID keys for string primary keys」）。</item>
/// </list>
/// <para>名前空間は名前空間の規則（<see cref="CodegenNamespaces"/>）と同じものを期待する。</para>
/// </remarks>
internal static class SemanticJudge
{
    /// <summary>意味を照合する（問題を返す）</summary>
    /// <param name="shape">図の形</param>
    /// <param name="options">実効の生成オプション</param>
    /// <param name="result">生成結果</param>
    /// <param name="manualSources">手動実装のソース</param>
    public static IReadOnlyList<string> Check(
        ShapeDiagram shape,
        CodeGenerationOptions options,
        CodeGenerationResult result,
        IReadOnlyList<GeneratedFile> manualSources
    )
    {
        var compilation = GeneratedCodeCompiler.CreateCompilation(
            result
                .Files.Where(file => file.FileName.EndsWith(".g.cs", StringComparison.Ordinal))
                .Concat(manualSources)
                .Select(file => (FileLayoutOracle.PathOf(file), file.Content)),
            $"QuickER.CodegenConfig.Semantic.{Guid.NewGuid():N}"
        );
        var types = new List<INamedTypeSymbol>();
        CollectTypes(compilation.Assembly.GlobalNamespace, types);
        var byName = types.ToLookup(type => type.Name, StringComparer.Ordinal);
        var namespaces = new CodegenNamespaces(options);
        var problems = new List<string>();
        var quicker = options.GenerateRepositories;
        var contract = CodegenOptionFacts.GeneratesRepositoryContract(options);
        var remote = contract && CodegenOptionFacts.GeneratesRemote(options);
        var services = contract && options.GenerateRemoteServices;
        var sync = contract && options.GenerateSyncSupport;
        var dialects = CodegenOptionFacts.Dialects(options);

        void ExpectTypes(string name, IReadOnlyList<string> expectedNamespaces)
        {
            var actual = byName[name]
                .Select(type => type.ContainingNamespace.ToDisplayString())
                .Order(StringComparer.Ordinal)
                .ToList();
            var expected = expectedNamespaces.Order(StringComparer.Ordinal).ToList();

            if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
            {
                problems.Add(
                    $"意味: 型 {name} の名前空間が [{string.Join(", ", actual)}]（期待 [{string.Join(", ", expected)}]）"
                );
            }
        }

        IReadOnlyList<string> If(bool condition, params string[] namespacesWhenTrue) =>
            condition ? namespacesWhenTrue : [];

        foreach (var table in shape.Tables)
        {
            var stem = table.Stem;
            var single = table.HasSinglePrimaryKey;

            ExpectTypes(table.EntityClass, [namespaces.Entity]);
            ExpectTypes(stem + "EditModel", If(options.GenerateEditModels, namespaces.EditModel));
            ExpectTypes(stem + "Mapper", If(options.GenerateMappers, namespaces.Mapper));
            ExpectTypes($"I{stem}Repository", If(contract && single, namespaces.Contract));
            ExpectTypes(
                $"{stem}Repository",
                quicker && single ? dialects.Select(namespaces.DialectImplementation).ToList() : []
            );
            ExpectTypes(
                $"EfCore{stem}Repository",
                If(options.GenerateEfCoreRepositories && single, namespaces.EfCore)
            );
            ExpectTypes(
                $"InMemory{stem}Repository",
                If(options.GenerateInMemoryRepositories && single, namespaces.InMemory)
            );
            ExpectTypes($"I{stem}RemoteRepository", If(remote && single, namespaces.Contract));
            ExpectTypes($"Http{stem}RemoteRepository", If(services && single, namespaces.Http));
            ExpectTypes($"Journaling{stem}Repository", If(sync && single, namespaces.Sync));
        }

        ExpectTypes("GeneratedSyncTables", If(sync, namespaces.Sync));
        ExpectTypes("QuickErDbContext", If(options.GenerateEfCoreRepositories, namespaces.EfCore));

        if (options.GenerateEfCoreRepositories)
        {
            var context = byName["QuickErDbContext"].FirstOrDefault();
            var dbSets =
                context
                    ?.GetMembers()
                    .OfType<IPropertySymbol>()
                    .Select(property => property.Type)
                    .OfType<INamedTypeSymbol>()
                    .Where(type => type.Name == "DbSet" && type.TypeArguments.Length == 1)
                    .Select(type => type.TypeArguments[0].Name)
                    .ToHashSet(StringComparer.Ordinal)
                ?? [];

            foreach (var table in shape.Tables.Where(table => table.PrimaryKey.Count > 0))
            {
                if (!dbSets.Contains(table.EntityClass))
                {
                    problems.Add($"意味: QuickErDbContext に DbSet<{table.EntityClass}> が無い");
                }
            }
        }

        var methodNames = types
            .Where(type => type.IsStatic)
            .SelectMany(type => type.GetMembers().OfType<IMethodSymbol>())
            .Select(method => method.Name)
            .ToHashSet(StringComparer.Ordinal);

        void ExpectMethod(string name, bool expected)
        {
            if (methodNames.Contains(name) != expected)
            {
                problems.Add(
                    $"意味: DI の拡張メソッド {name} が{(expected ? "無い" : "ある（期待していない）")}"
                );
            }
        }

        ExpectMethod(
            "AddGeneratedSqlServerRepositories",
            quicker && dialects.Contains(DialectNames.SqlServer)
        );
        ExpectMethod(
            "AddGeneratedSqliteRepositories",
            quicker && dialects.Contains(DialectNames.Sqlite)
        );
        ExpectMethod("AddGeneratedEfCoreRepositories", options.GenerateEfCoreRepositories);
        ExpectMethod("AddGeneratedInMemoryRepositories", options.GenerateInMemoryRepositories);
        ExpectMethod("AddGeneratedHttpRemoteRepositories", services);

        CheckColumnAttributes(shape, options, byName, problems);
        CheckValueObjects(shape, options, byName, problems);
        return problems;
    }

    /// <summary>子の表（orders）の列の属性と、親参照ナビゲーションの [JsonIgnore]</summary>
    private static void CheckColumnAttributes(
        ShapeDiagram shape,
        CodeGenerationOptions options,
        ILookup<string, INamedTypeSymbol> byName,
        List<string> problems
    )
    {
        var order = byName[shape.Orders.EntityClass].FirstOrDefault();

        if (order is null)
        {
            problems.Add($"意味: {shape.Orders.EntityClass} が無い");
            return;
        }

        void Expect(string propertyName, string attribute, bool expected)
        {
            var property = order
                .GetMembers(propertyName)
                .OfType<IPropertySymbol>()
                .FirstOrDefault();

            if (property is null)
            {
                problems.Add($"意味: {order.Name}.{propertyName} が無い");
                return;
            }

            var has = property
                .GetAttributes()
                .Any(a => a.AttributeClass?.Name == attribute + "Attribute");

            if (has != expected)
            {
                problems.Add(
                    $"意味: {order.Name}.{propertyName} の [{attribute}] が{(expected ? "無い" : "ある（期待していない）")}"
                );
            }
        }

        Expect("Amount", "DbColumnMeta", options.IncludeDataAnnotations);

        if (shape.ComputedColumn is { } computed)
        {
            Expect(Pascal(computed.Name), "ComputedColumn", true);
        }

        if (shape.IdentityColumn is { } identity)
        {
            Expect(Pascal(identity.Name), "IdentityColumn", true);
        }

        if (shape.RowVersionColumn is { } rowVersion)
        {
            Expect(
                Pascal(rowVersion.Name),
                "StoreGeneratedColumn",
                shape.RowVersionIsStoreGenerated(options)
            );
        }

        if (shape.BinaryColumn is { } binary)
        {
            Expect(
                Pascal(binary.Name),
                "UnboundedBinaryColumn",
                options.ExcludeUnboundedBinaryColumns && binary.Spelling.IsUnboundedBinary
            );
        }

        // 親参照ナビゲーション（複合外部キーの関係はナビゲーションを作らない）
        var parentRelation = shape.Relations.First(relation =>
            relation.Parent == shape.Customers && relation.Child == shape.Orders
        );

        if (!parentRelation.IsComposite)
        {
            Expect(
                parentRelation.ParentNavigationOnChild!,
                "JsonIgnore",
                options.IncludeJsonIgnoreOnParentNavigation
            );
        }
    }

    /// <summary>値オブジェクト：外部キーの型の共有と GuidKey の基底</summary>
    private static void CheckValueObjects(
        ShapeDiagram shape,
        CodeGenerationOptions options,
        ILookup<string, INamedTypeSymbol> byName,
        List<string> problems
    )
    {
        if (!options.GenerateValueObjects)
        {
            return;
        }

        ITypeSymbol? PropertyType(ShapeTable table, string column) =>
            byName[table.EntityClass]
                .FirstOrDefault()
                ?.GetMembers(Pascal(column))
                .OfType<IPropertySymbol>()
                .FirstOrDefault()
                ?.Type;

        foreach (var relation in shape.Relations)
        {
            foreach (var (parent, child) in relation.Pairs)
            {
                var parentType = PropertyType(relation.Parent, parent.Name);
                var childType = PropertyType(relation.Child, child.Name);

                if (
                    parentType is null
                    || childType is null
                    || !SymbolEqualityComparer.Default.Equals(parentType, childType)
                )
                {
                    problems.Add(
                        $"意味: 外部キー {relation.Child.Name}.{child.Name} の型 {childType?.ToDisplayString()} が親 {relation.Parent.Name}.{parent.Name} の型 {parentType?.ToDisplayString()} と同じでない"
                    );
                }
            }
        }

        if (shape.Levels.Key == KeyLevels.String)
        {
            var keyType = PropertyType(shape.Customers, shape.Customers.PrimaryKey[0].Name);
            var isGuidKey = keyType?.BaseType?.Name == "ValueObjectGuidKeyBase";

            if (isGuidKey != options.UseGuidKeyForStringPrimaryKey)
            {
                problems.Add(
                    $"意味: 文字列の主キーの値オブジェクト {keyType?.ToDisplayString()} の基底が {keyType?.BaseType?.ToDisplayString()}（GuidKey={options.UseGuidKeyForStringPrimaryKey}）"
                );
            }
        }
    }

    /// <summary>列名をプロパティ名にする（docs「Entity member names」：<c>user_id</c> → <c>UserId</c>）</summary>
    public static string Pascal(string column) =>
        string.Concat(
            column
                .Split('_', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..])
        );

    private static void CollectTypes(INamespaceSymbol @namespace, List<INamedTypeSymbol> types)
    {
        types.AddRange(@namespace.GetTypeMembers());

        foreach (var child in @namespace.GetNamespaceMembers())
        {
            CollectTypes(child, types);
        }
    }
}
