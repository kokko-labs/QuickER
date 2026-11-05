using System.Collections;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using QuickER.CodeGen.CSharp;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.CodeGen.CSharp.CodegenConfig.Judges;

/// <summary>探針（生成コードを読み込んで動かす）の種類</summary>
internal enum CodegenProbe
{
    /// <summary>
    /// GuidKey の値オブジェクトの <c>Create()</c>（GUID の採番・一意）・<c>Create(string)</c>（宣言幅までの値の保持・超過の検証）・
    /// <c>TryCreate</c>（正常で true・超過で false と結果 null とエラー 1 件）・等値・順序
    /// </summary>
    GuidKeyCreate,

    /// <summary>
    /// <c>EntitySaveMetadata</c> の書き込み除外（計算列・主キーでない採番列は方言に依らず INSERT / UPDATE から外れ、
    /// 行バージョンは SQL Server のエンジンだけが外す）
    /// </summary>
    SaveMetadataWriteExclusion,

    /// <summary>
    /// インメモリのシーダーでシードしたストアが主キー・一意制約・外部キーを守り、DI 登録したインメモリ Repository で
    /// 全行を読めて <c>UpdateAsync</c> で保存し直せる
    /// </summary>
    InMemorySeed,

    /// <summary>
    /// 生成した <c>QuickErDbContext</c> を SQLite のプロバイダで作り、<c>Model</c> を組める（モデル検証が通る）。
    /// 外部キーと主キーの CLR 型が一致し、図の関係が外部キーとして親の列へ結合する
    /// </summary>
    EfCoreModel,

    /// <summary>同期の記述子の <c>FormatKey</c> / <c>ParseKey</c> がキーを往復し、書き直し（<c>NormalizeKeyText</c>）は日時と decimal の主キーの表だけが持つ</summary>
    SyncKeyText,
}

/// <summary>決まった行が探針へ渡す追加の事実（元のテストが名指しで見ていたもの）</summary>
/// <param name="ModelFacts">EF Core のモデルの事実（<see cref="CodegenProbe.EfCoreModel"/>）</param>
/// <param name="SeedRowCounts">
/// エンティティの型名 → シードの行数（<see cref="CodegenProbe.InMemorySeed"/>。null なら図の形の行は <see cref="ShapeDiagram.SeedRowCounts"/>、
/// 図の形の無い決まった行は全エンティティが <c>RowsPerEntity</c> 行）
/// </param>
internal sealed record CodegenProbeFacts(
    IReadOnlyList<EfCoreModelFact>? ModelFacts = null,
    IReadOnlyDictionary<string, int>? SeedRowCounts = null
);

/// <summary>
/// 探針の本体（<see cref="GeneratedCodeCompiler.CompileAndLoad"/> で読み込んで動かす）
/// </summary>
/// <remarks>
/// <para>
/// GuidKey：docs「GUID keys for string primary keys」・CLAUDE.md「GuidKey 値オブジェクトの長さ検証」（列幅 36 以上で引数なし <c>Create()</c> が GUID を採番）。
/// </para>
/// <para>
/// 書き込み除外：CLAUDE.md「DB が値を作る列の書き込み除外」の表（計算列・採番列は方言のゲートなし、行バージョンは
/// <c>dialect_assigns_store_generated</c> のゲートあり＝採番するのは SQL Server だけ）。<c>EntitySaveMetadata</c> は方言のエンジンの固定 infra に
/// あり、インラインならエンジンの名前空間（<see cref="CodegenNamespaces.DialectEngine"/>）、パッケージ参照モードなら <c>QuickER.Runtime.{方言}</c>。
/// </para>
/// <para>
/// インメモリのシード：CLAUDE.md「インメモリのシード値」（主キー・一意制約の構成列は行ごとに違う値・外部キーは親の i 行目を指す・
/// 3 種類の値を作れない列だけの主キー・一意制約を持つ表と、それを参照する子だけが行数を減らす・主キーが 1 列でない表と、それを
/// 直接・間接に参照する表は入れない）。図の形の行の期待の行数は <see cref="ShapeDiagram.SeedRowCounts"/>。
/// </para>
/// <para>
/// EF Core のモデル：CLAUDE.md「FK 列の値オブジェクト型統一」（EF Core のモデル検証は外部キーと主キーの CLR 型の一致を要求する）と、
/// 親の列が主キーでない外部キーの <c>HasPrincipalKey</c>（外部キーは図の列ペアどおりの親の列へ結合する）。代替キーの書き込みの内部には依らない。
/// </para>
/// <para>
/// 同期のキーのテキスト：src/QuickER.Runtime.Sync/CLAUDE.md「キーのテキスト」（日時は Kind を落とし、decimal は指数表記を使わず、
/// 書き直しは日時・decimal の主キーの表だけが持つ）。
/// </para>
/// </remarks>
internal static class Probes
{
    /// <summary>探針が読み込む生成物（<c>.g.cs</c> と手動実装のソース）をコンパイルして読み込む</summary>
    /// <param name="result">生成結果</param>
    /// <param name="manualSources">手動実装のソース</param>
    public static Assembly Load(
        CodeGenerationResult result,
        IReadOnlyList<GeneratedFile> manualSources
    ) =>
        GeneratedCodeCompiler.CompileAndLoad(
            new CodeGenerationResult
            {
                Files = result
                    .Files.Where(file => file.FileName.EndsWith(".g.cs", StringComparison.Ordinal))
                    .ToList(),
            },
            $"QuickER.CodegenConfig.Probe.{Guid.NewGuid():N}",
            manualSources.Select(file => file.Content).ToArray()
        );

    /// <summary>探針を動かす（問題を返す）</summary>
    /// <param name="probe">探針</param>
    /// <param name="shape">図の形（書き込み除外の探針が読む。EF Core・インメモリの探針は図の形の関係・表も読む。決まった行は null）</param>
    /// <param name="options">実効の生成オプション</param>
    /// <param name="assembly">読み込んだ生成物（行の探針で共有する）</param>
    /// <param name="facts">決まった行の追加の事実</param>
    public static IReadOnlyList<string> Run(
        CodegenProbe probe,
        ShapeDiagram? shape,
        CodeGenerationOptions options,
        Assembly assembly,
        CodegenProbeFacts? facts
    )
    {
        try
        {
            return probe switch
            {
                CodegenProbe.GuidKeyCreate => GuidKeyCreate(assembly),
                CodegenProbe.SaveMetadataWriteExclusion => SaveMetadata(assembly, shape!, options),
                CodegenProbe.InMemorySeed => InMemorySeed(
                    assembly,
                    facts?.SeedRowCounts ?? shape?.SeedRowCounts
                ),
                CodegenProbe.EfCoreModel => EfCoreModel(assembly, shape, facts?.ModelFacts ?? []),
                CodegenProbe.SyncKeyText => SyncKeyText(assembly),
                _ => [$"探針: 知らない探針 {probe}"],
            };
        }
        catch (TargetInvocationException ex) when (ex.InnerException is { } inner)
        {
            // 反射で呼んだ生成物の例外は包まれるので、中身を名指しする
            return
            [
                $"探針（{probe}）: 生成物が {inner.GetType().Name} を投げた（{inner.Message}）",
            ];
        }
    }

    // ===== GuidKey =====

    private static IReadOnlyList<string> GuidKeyCreate(Assembly assembly)
    {
        var problems = new List<string>();
        var guidKeyTypes = assembly
            .GetTypes()
            .Where(type =>
                type.BaseType is { IsGenericType: true } baseType
                && baseType.GetGenericTypeDefinition().Name == "ValueObjectGuidKeyBase`1"
            )
            .ToList();

        if (guidKeyTypes.Count == 0)
        {
            return ["探針（GuidKey）: 基底が ValueObjectGuidKeyBase の値オブジェクトが無い"];
        }

        foreach (var type in guidKeyTypes)
        {
            var create = type.GetMethod(
                "Create",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null
            );

            if (create is null)
            {
                problems.Add($"探針（GuidKey）: {type.Name} に引数なし Create() が無い");
                continue;
            }

            var value = type.GetProperty("Value")!;
            var first = (string)value.GetValue(create.Invoke(null, null))!;
            var second = (string)value.GetValue(create.Invoke(null, null))!;

            if (first.Length != 36 || !Guid.TryParse(first, out _) || !Guid.TryParse(second, out _))
            {
                problems.Add($"探針（GuidKey）: {type.Name}.Create() が GUID でない値 {first}");
            }

            if (first == second)
            {
                problems.Add($"探針（GuidKey）: {type.Name}.Create() が 2 回とも同じ値 {first}");
            }

            var many = Enumerable
                .Range(0, 100)
                .Select(_ => (string)value.GetValue(create.Invoke(null, null))!)
                .Distinct(StringComparer.Ordinal)
                .Count();

            if (many != 100)
            {
                problems.Add(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"探針（GuidKey）: {type.Name}.Create() を 100 回呼んで違う値が {many} 個"
                    )
                );
            }

            problems.AddRange(GuidKeyDeclaredWidth(type, value));
            problems.AddRange(GuidKeyTryCreate(type, value));
            problems.AddRange(GuidKeyEqualityAndOrder(type));
        }

        return problems;
    }

    /// <summary>
    /// GuidKey の値オブジェクトの値あり <c>Create(string)</c> が、宣言幅以内の値（GUID の 36 文字・宣言幅ちょうど）をそのまま保持し、
    /// 宣言幅超過を <c>ValueObjectValidationException</c>（長さの文言）で断る
    /// （CLAUDE.md「GuidKey 値オブジェクトの長さ検証」＝素通しした MaxLength が検証を駆動する）
    /// </summary>
    /// <remarks>
    /// 宣言幅は図の側と共有の <see cref="GuidKeyDeclaredWidthValue"/>（ShapeDiagram の文字列キーと Cases の GuidKey の図がこの幅で宣言する）。
    /// </remarks>
    private static IReadOnlyList<string> GuidKeyDeclaredWidth(Type type, PropertyInfo value)
    {
        var create = StringCreate(type);

        if (create is null)
        {
            return [$"探針（GuidKey）: {type.Name} に値あり Create(string) が無い"];
        }

        var problems = new List<string>();
        var exact = new string('k', GuidKeyDeclaredWidthValue);

        foreach (var kept in new[] { "my-key-123", Guid.NewGuid().ToString(), exact })
        {
            try
            {
                var created = (string)value.GetValue(create.Invoke(null, [kept]))!;

                if (created != kept)
                {
                    problems.Add($"探針（GuidKey）: {type.Name}.Create(\"{kept}\") が値を変えた");
                }
            }
            catch (TargetInvocationException ex)
            {
                problems.Add(
                    $"探針（GuidKey）: {type.Name}.Create(\"{kept}\") が {ex.InnerException?.GetType().Name} で断った"
                );
            }
        }

        var tooLong = exact + "k";

        try
        {
            create.Invoke(null, [tooLong]);
            problems.Add(
                $"探針（GuidKey）: {type.Name}.Create(宣言幅超過) が断らない（長さ検証が無い）"
            );
        }
        catch (TargetInvocationException ex)
            when (ex.InnerException?.GetType().Name != "ValueObjectValidationException")
        {
            problems.Add(
                $"探針（GuidKey）: {type.Name}.Create(宣言幅超過) が ValueObjectValidationException でなく {ex.InnerException?.GetType().Name} で断った"
            );
        }
        catch (TargetInvocationException ex)
            when (!ex.InnerException!.Message.Contains(
                    LengthMessage(GuidKeyDeclaredWidthValue)
                        + string.Create(
                            CultureInfo.InvariantCulture,
                            $" (currently {tooLong.Length}"
                        ),
                    StringComparison.Ordinal
                )
            )
        {
            problems.Add(
                $"探針（GuidKey）: {type.Name}.Create(宣言幅超過) の文言が長さの文言でない（{ex.InnerException.Message}）"
            );
        }
        catch (TargetInvocationException)
        {
            // 期待どおり ValueObjectValidationException（長さの文言）で断った
        }

        return problems;
    }

    /// <summary><c>TryCreate(string)</c> が正常な値で true・結果あり・エラー空、宣言幅超過で false・結果 null・長さのエラー 1 件</summary>
    private static IReadOnlyList<string> GuidKeyTryCreate(Type type, PropertyInfo value)
    {
        var tryCreate = type.GetMethods(
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy
            )
            .SingleOrDefault(method =>
                method.Name == "TryCreate"
                && method.GetParameters() is { Length: 3 } parameters
                && parameters[0].ParameterType == typeof(string)
            );

        if (tryCreate is null)
        {
            return [$"探針（GuidKey）: {type.Name} に TryCreate(string, out, out) が無い"];
        }

        var problems = new List<string>();
        var valid = new object?[] { "abc", null, null };
        var validOk = (bool)tryCreate.Invoke(null, valid)!;

        if (
            !validOk
            || valid[1] is null
            || (string)value.GetValue(valid[1])! != "abc"
            || ((IEnumerable<string>)valid[2]!).Any()
        )
        {
            problems.Add(
                $"探針（GuidKey）: {type.Name}.TryCreate(\"abc\") が true・結果 \"abc\"・エラー空でない"
            );
        }

        var tooLong = new object?[] { new string('x', GuidKeyDeclaredWidthValue + 1), null, null };
        var tooLongOk = (bool)tryCreate.Invoke(null, tooLong)!;
        var errors = ((IEnumerable<string>)tooLong[2]!).ToList();

        if (
            tooLongOk
            || tooLong[1] is not null
            || errors.Count != 1
            || !errors[0]
                .Contains(LengthMessage(GuidKeyDeclaredWidthValue), StringComparison.Ordinal)
        )
        {
            problems.Add(
                $"探針（GuidKey）: {type.Name}.TryCreate(宣言幅超過) が false・結果 null・長さのエラー 1 件でない（エラー [{string.Join(" / ", errors)}]）"
            );
        }

        return problems;
    }

    /// <summary>同じ値の <c>Create</c> どうしは等しく（ハッシュも同じ）、違う値とは等しくない。<c>CompareTo</c> は序数の順で、null より大きい</summary>
    private static IReadOnlyList<string> GuidKeyEqualityAndOrder(Type type)
    {
        var create = StringCreate(type);

        if (create is null)
        {
            return [];
        }

        var problems = new List<string>();
        var same = create.Invoke(null, ["same"])!;
        var sameAgain = create.Invoke(null, ["same"])!;
        var other = create.Invoke(null, ["other"])!;

        if (!same.Equals(sameAgain) || same.GetHashCode() != sameAgain.GetHashCode())
        {
            problems.Add(
                $"探針（GuidKey）: {type.Name} の同じ値どうしが等しくない（Equals / GetHashCode）"
            );
        }

        if (same.Equals(other))
        {
            problems.Add($"探針（GuidKey）: {type.Name} の違う値どうしが等しい");
        }

        if (create.Invoke(null, ["aaa"]) is not IComparable aaa)
        {
            problems.Add($"探針（GuidKey）: {type.Name} が IComparable でない");
            return problems;
        }

        var bbb = create.Invoke(null, ["bbb"])!;
        var aaaAgain = create.Invoke(null, ["aaa"])!;

        if (
            aaa.CompareTo(bbb) >= 0
            || ((IComparable)bbb).CompareTo(aaa) <= 0
            || aaa.CompareTo(aaaAgain) != 0
            || aaa.CompareTo(null) <= 0
        )
        {
            problems.Add(
                $"探針（GuidKey）: {type.Name}.CompareTo が序数の順（aaa < bbb・同じ値は 0・null より大きい）でない"
            );
        }

        return problems;
    }

    private static MethodInfo? StringCreate(Type type) =>
        type.GetMethod(
            "Create",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy,
            binder: null,
            types: [typeof(string)],
            modifiers: null
        );

    /// <summary>値オブジェクトの長さの検証の文言（生成物の固定文は英語が正本）</summary>
    private static string LengthMessage(int maxLength) =>
        string.Create(CultureInfo.InvariantCulture, $"Enter at most {maxLength} characters.");

    /// <summary>GuidKey の探針が前提にする文字列キーの宣言幅（図の側で同じ幅を宣言する）</summary>
    public const int GuidKeyDeclaredWidthValue = 50;

    // ===== 書き込み除外 =====

    private static IReadOnlyList<string> SaveMetadata(
        Assembly assembly,
        ShapeDiagram shape,
        CodeGenerationOptions options
    )
    {
        var problems = new List<string>();
        var namespaces = new CodegenNamespaces(options);
        var entity = assembly
            .GetTypes()
            .SingleOrDefault(type =>
                type.Name == shape.Orders.EntityClass && type.Namespace == namespaces.Entity
            );

        if (entity is null)
        {
            return [$"探針（書き込み除外）: {namespaces.Entity}.{shape.Orders.EntityClass} が無い"];
        }

        foreach (var dialect in CodegenOptionFacts.Dialects(options))
        {
            var suffix = CodegenOptionFacts.DialectSuffix(dialect);
            var metadataType = options.UseRuntimePackages
                ? Type.GetType(
                    $"QuickER.Runtime.{suffix}.EntitySaveMetadata, QuickER.Runtime.{suffix}"
                )
                : assembly.GetType($"{namespaces.DialectEngine(dialect)}.EntitySaveMetadata");

            if (metadataType is null)
            {
                problems.Add($"探針（書き込み除外）: {dialect} の EntitySaveMetadata が無い");
                continue;
            }

            var metadata = metadataType.GetMethod("For", [typeof(Type)])!.Invoke(null, [entity])!;
            var insert = Names(metadataType, metadata, "InsertProperties");
            var update = Names(metadataType, metadata, "NonKeyProperties");

            void Expect(string column, bool excluded)
            {
                var property = SemanticJudge.Pascal(column);

                foreach (var (what, names) in new[] { ("INSERT", insert), ("UPDATE", update) })
                {
                    if (names.Contains(property) == excluded)
                    {
                        problems.Add(
                            $"探針（書き込み除外）: {dialect} の {what} の列に {property} が{(excluded ? "ある（外れるはず）" : "無い（書くはず）")}"
                        );
                    }
                }
            }

            Expect("amount", excluded: false);

            if (shape.ComputedColumn is { } computed)
            {
                Expect(computed.Name, excluded: true);
            }

            if (shape.IdentityColumn is { } identity)
            {
                Expect(identity.Name, excluded: true);
            }

            if (shape.RowVersionColumn is { } rowVersion)
            {
                // 採番するのは SQL Server だけ（ほかの方言のエンジンでは通常の列として書く）
                Expect(
                    rowVersion.Name,
                    excluded: shape.RowVersionIsStoreGenerated(options)
                        && dialect == QuickER.Dialects.DialectNames.SqlServer
                );
            }
        }

        return problems;
    }

    private static HashSet<string> Names(Type metadataType, object metadata, string property) =>
        ((IEnumerable<PropertyInfo>)metadataType.GetProperty(property)!.GetValue(metadata)!)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

    // ===== インメモリのシード =====

    /// <summary>
    /// シーダーでシードしたストアを <see cref="SeededSampleDataInvariants"/> で検査し、行数を照合し、DI 登録した
    /// インメモリ Repository で全行を読み、<c>MarkUpdated</c> して <c>UpdateAsync</c> で保存し直す（一意制約の重複があれば断られる）
    /// </summary>
    private static IReadOnlyList<string> InMemorySeed(
        Assembly assembly,
        IReadOnlyDictionary<string, int>? expectedRowCounts
    )
    {
        var types = assembly.GetTypes();
        var seeder = types.Single(type => type.Name == "InMemorySampleData");
        var entityTypes = types
            .Where(type =>
                type.IsClass
                && !type.IsAbstract
                && type.GetCustomAttribute<TableAttribute>() is not null
            )
            .ToList();
        var (violations, rowCounts) = SeededSampleDataInvariants.Check(seeder, entityTypes);
        var problems = violations
            .Select(violation => $"探針（インメモリのシード）: {violation}")
            .ToList();
        var rowsPerEntity = (int)seeder.GetField("RowsPerEntity")!.GetRawConstantValue()!;
        var expected =
            expectedRowCounts
            ?? entityTypes.ToDictionary(
                type => type.Name,
                _ => rowsPerEntity,
                StringComparer.Ordinal
            );

        foreach (var (entity, count) in expected)
        {
            if (rowCounts.GetValueOrDefault(entity) != count)
            {
                problems.Add(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"探針（インメモリのシード）: {entity} の行数 {rowCounts.GetValueOrDefault(entity)}（期待 {count}）"
                    )
                );
            }
        }

        var services = new ServiceCollection();
        types
            .Single(type => type.Name == "GeneratedInMemoryRepositoryServiceCollectionExtensions")
            .GetMethod("AddGeneratedInMemoryRepositories")!
            .Invoke(
                null,
                BindingFlags.OptionalParamBinding,
                binder: null,
                parameters: [services, Type.Missing],
                culture: null
            );

        var contracts = services
            .Where(descriptor =>
                descriptor.ServiceType.IsInterface
                && descriptor.ImplementationType?.Name.StartsWith(
                    "InMemory",
                    StringComparison.Ordinal
                ) == true
            )
            .Select(descriptor => descriptor.ServiceType)
            .ToList();

        if (contracts.Count == 0)
        {
            problems.Add("探針（インメモリのシード）: インメモリ Repository の登録が無い");
        }

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        foreach (var contract in contracts)
        {
            var repository = scope.ServiceProvider.GetRequiredService(contract);
            var getAll = ContractMethod(contract, "GetAllAsync");
            var update = ContractMethod(contract, "UpdateAsync");
            var rows = ((IEnumerable)Await(getAll.Invoke(repository, [CancellationToken.None])!)!)
                .Cast<object>()
                .ToList();
            var entityName = rows.FirstOrDefault()?.GetType().Name;

            if (entityName is not null && rowCounts.GetValueOrDefault(entityName) != rows.Count)
            {
                problems.Add(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"探針（インメモリのシード）: {contract.Name}.GetAllAsync が {rows.Count} 行（シード {rowCounts.GetValueOrDefault(entityName)} 行）"
                    )
                );
            }

            foreach (var row in rows)
            {
                row.GetType().GetMethod("MarkUpdated")!.Invoke(row, null);
                var arguments = update
                    .GetParameters()
                    .Select((_, index) => index == 0 ? row : Type.Missing)
                    .ToArray();
                var updated = (bool)
                    Await(
                        update.Invoke(
                            repository,
                            BindingFlags.OptionalParamBinding,
                            binder: null,
                            parameters: arguments,
                            culture: null
                        )!
                    )!;

                if (!updated)
                {
                    problems.Add(
                        $"探針（インメモリのシード）: {contract.Name}.UpdateAsync がシードした行を保存し直せない"
                    );
                }
            }
        }

        return problems;
    }

    /// <summary>契約（またはその基底の契約）が宣言するメソッドを引く</summary>
    private static MethodInfo ContractMethod(Type contract, string name) =>
        contract
            .GetInterfaces()
            .Prepend(contract)
            .Select(type => type.GetMethod(name))
            .First(method => method is not null)!;

    /// <summary>タスクの完了を待ち、結果（無ければ null）を返す</summary>
    private static object? Await(object task)
    {
        ((Task)task).GetAwaiter().GetResult();
        return task.GetType().GetProperty("Result")?.GetValue(task);
    }

    // ===== EF Core のモデル =====

    /// <summary>
    /// 生成した <c>QuickErDbContext</c> を SQLite のプロバイダ（実接続はしない）で作って <c>Model</c> を組み、外部キーと主キーの
    /// CLR 型の一致・図の形の表と関係の写り方・決まった行の事実を見る
    /// </summary>
    private static IReadOnlyList<string> EfCoreModel(
        Assembly assembly,
        ShapeDiagram? shape,
        IReadOnlyList<EfCoreModelFact> facts
    )
    {
        var contextType = assembly
            .GetTypes()
            .SingleOrDefault(type => type.Name == "QuickErDbContext");

        if (contextType is null)
        {
            return ["探針（EF Core のモデル）: QuickErDbContext が無い"];
        }

        var builder = (DbContextOptionsBuilder)
            Activator.CreateInstance(
                typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType)
            )!;
        builder.UseSqlite("Data Source=:memory:").IgnoreServiceProviderCacheLimit();

        using var context = (DbContext)Activator.CreateInstance(contextType, builder.Options)!;
        IModel model;

        try
        {
            // Model へのアクセスでモデル構築＋検証が走る（不整合な外部キー・キーの無い型はここで例外）
            model = context.Model;
        }
        catch (Exception ex)
        {
            return
            [
                $"探針（EF Core のモデル）: Model を組めない（{ex.GetType().Name}: {ex.Message}）",
            ];
        }

        var problems = new List<string>();

        foreach (
            var foreignKey in model
                .GetEntityTypes()
                .SelectMany(type => type.GetDeclaredForeignKeys())
        )
        {
            var dependent = foreignKey.Properties.Select(p => Underlying(p.ClrType));
            var principal = foreignKey.PrincipalKey.Properties.Select(p => Underlying(p.ClrType));

            if (!dependent.SequenceEqual(principal))
            {
                problems.Add(
                    $"探針（EF Core のモデル）: {foreignKey.DeclaringEntityType.ClrType.Name} の外部キー ({string.Join(", ", foreignKey.Properties.Select(p => p.Name))}) と親のキーの CLR 型が違う"
                );
            }
        }

        if (shape is not null)
        {
            problems.AddRange(ShapeModel(model, shape));
        }

        problems.AddRange(facts.SelectMany(fact => fact.Check(model)));
        return problems;
    }

    /// <summary>図の形の全部の表がモデルにあり、複合でない関係は子の列 → 親の列（列ペアどおり）の外部キーになる</summary>
    private static IEnumerable<string> ShapeModel(IModel model, ShapeDiagram shape)
    {
        foreach (var table in shape.Tables)
        {
            if (EfCoreModelFact.Find(model, table.EntityClass) is null)
            {
                yield return $"探針（EF Core のモデル）: {table.EntityClass} がモデルに無い";
            }
        }

        foreach (var relation in shape.Relations.Where(relation => !relation.IsComposite))
        {
            var child = EfCoreModelFact.Find(model, relation.Child.EntityClass);

            if (child is null)
            {
                continue;
            }

            var expected = relation
                .Pairs.Select(pair =>
                    (
                        Dependent: SemanticJudge.Pascal(pair.Child.Name),
                        Principal: SemanticJudge.Pascal(pair.Parent.Name)
                    )
                )
                .ToHashSet();
            var found = child
                .GetForeignKeys()
                .Any(foreignKey =>
                    foreignKey.PrincipalEntityType.ClrType.Name == relation.Parent.EntityClass
                    && foreignKey
                        .Properties.Zip(foreignKey.PrincipalKey.Properties)
                        .Select(pair => (Dependent: pair.First.Name, Principal: pair.Second.Name))
                        .ToHashSet()
                        .SetEquals(expected)
                );

            if (!found)
            {
                yield return $"探針（EF Core のモデル）: {relation.Child.EntityClass} に {relation.Parent.EntityClass} の ({string.Join(", ", expected.Select(pair => pair.Principal))}) へ結合する外部キー ({string.Join(", ", expected.Select(pair => pair.Dependent))}) が無い";
            }
        }
    }

    private static Type Underlying(Type type) => Nullable.GetUnderlyingType(type) ?? type;

    // ===== 同期のキーのテキスト =====

    /// <summary>
    /// decimal のキーの往復と以前の形の書き直し（値・いまの形・以前の指数表記の形。元の <c>SyncDecimalKeyExecutionTests</c> の行）
    /// </summary>
    private static readonly (decimal Value, string Text, string Legacy)[] DecimalKeySamples =
    [
        (0.000001m, "0.000001", "1E-06"),
        (-0.000001m, "-0.000001", "-1E-06"),
        (0.0000000000000000000000000001m, "0.0000000000000000000000000001", "1E-28"),
        (1.5m, "1.5", "1.5"),
        (100m, "100", "100"),
    ];

    /// <summary>
    /// 生成した <c>GeneratedSyncTables</c> の全記述子について、キーの型の見本を <c>FormatKey</c> → <c>ParseKey</c> で往復させ、
    /// 書き直し（<c>NormalizeKeyText</c>）の有無がキーの型（日時・decimal だけが持つ）と合うことを見る
    /// </summary>
    private static IReadOnlyList<string> SyncKeyText(Assembly assembly)
    {
        var tables = assembly
            .GetTypes()
            .SingleOrDefault(type => type.Name == "GeneratedSyncTables");

        if (tables is null)
        {
            return ["探針（同期のキー）: GeneratedSyncTables が無い"];
        }

        var problems = new List<string>();
        var descriptors = tables
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(property => property.PropertyType.GetProperty("FormatKey") is not null)
            .ToList();

        if (descriptors.Count == 0)
        {
            problems.Add("探針（同期のキー）: 記述子が 1 つも無い");
        }

        foreach (var property in descriptors)
        {
            var descriptor = property.GetValue(null)!;
            var descriptorType = descriptor.GetType();
            var format = (Delegate)descriptorType.GetProperty("FormatKey")!.GetValue(descriptor)!;
            var parse = (Delegate)descriptorType.GetProperty("ParseKey")!.GetValue(descriptor)!;
            var normalize = (Delegate?)
                descriptorType.GetProperty("NormalizeKeyText")!.GetValue(descriptor);
            var keyType = descriptorType
                .GetProperty("FormatKey")!
                .PropertyType.GetGenericArguments()[0];
            var valueObjectCreate = ValueObjectCreate(keyType);
            var underlying = valueObjectCreate?.GetParameters()[0].ParameterType ?? keyType;
            var name = $"{property.Name}（{underlying.Name}）";
            var expectsNormalizer = underlying == typeof(decimal) || underlying == typeof(DateTime);

            if ((normalize is not null) != expectsNormalizer)
            {
                problems.Add(
                    $"探針（同期のキー）: {name} の NormalizeKeyText が{(expectsNormalizer ? "無い" : "ある（日時・decimal の主キーの表だけが持つ）")}"
                );
            }

            var samples = KeySamples(underlying);

            if (samples is null)
            {
                problems.Add($"探針（同期のキー）: {name} のキーの型の見本が無い（探針に足す）");
                continue;
            }

            foreach (var sample in samples)
            {
                object key;

                try
                {
                    key = valueObjectCreate is null
                        ? sample
                        : valueObjectCreate.Invoke(null, [sample])!;
                }
                catch (TargetInvocationException ex)
                {
                    problems.Add(
                        $"探針（同期のキー）: {name} の見本 {Show(sample)} から値オブジェクトを作れない（{ex.InnerException?.Message}）"
                    );
                    continue;
                }

                var text = (string)format.DynamicInvoke(key)!;
                var parsed = parse.DynamicInvoke(text);

                if (!SameKey(parsed, key))
                {
                    problems.Add(
                        $"探針（同期のキー）: {name} の {Show(sample)} が '{text}' から元の値へ戻らない（{Show(parsed)}）"
                    );
                }

                if (normalize is not null && (string?)normalize.DynamicInvoke(text) != text)
                {
                    problems.Add(
                        $"探針（同期のキー）: {name} の書き直しがいまの形 '{text}' を変えた"
                    );
                }
            }

            if (underlying == typeof(decimal))
            {
                problems.AddRange(
                    DecimalKeyText(name, format, parse, normalize, valueObjectCreate)
                );
            }
        }

        return problems;
    }

    /// <summary>decimal のキーは指数表記を使わずに書き、以前の指数表記のテキストを読み、いまの形へ書き直す</summary>
    private static IEnumerable<string> DecimalKeyText(
        string name,
        Delegate format,
        Delegate parse,
        Delegate? normalize,
        MethodInfo? valueObjectCreate
    )
    {
        foreach (var (value, text, legacy) in DecimalKeySamples)
        {
            var key = valueObjectCreate is null ? value : valueObjectCreate.Invoke(null, [value])!;
            var formatted = (string)format.DynamicInvoke(key)!;

            if (formatted != text)
            {
                yield return $"探針（同期のキー）: {name} の {Show(value)} を '{formatted}' と書いた（期待 '{text}'）";
            }

            if (!SameKey(parse.DynamicInvoke(legacy), key))
            {
                yield return $"探針（同期のキー）: {name} が以前の形 '{legacy}' を {Show(value)} と読まない";
            }

            if (normalize is not null && (string?)normalize.DynamicInvoke(legacy) != text)
            {
                yield return $"探針（同期のキー）: {name} の書き直しが '{legacy}' を '{text}' にしない";
            }
        }
    }

    /// <summary>値オブジェクトのキーなら、中身の値から作る <c>Create(値)</c>（値オブジェクトでなければ null）</summary>
    private static MethodInfo? ValueObjectCreate(Type keyType)
    {
        if (!keyType.GetInterfaces().Any(type => type.Name == "IValueObjectCore"))
        {
            return null;
        }

        var valueType = keyType.GetProperty("Value")!.PropertyType;
        return keyType.GetMethod(
            "Create",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy,
            binder: null,
            types: [valueType],
            modifiers: null
        );
    }

    /// <summary>キーの型の見本（往復で値が変わりやすい境界の値。知らない型は null）</summary>
    private static IReadOnlyList<object>? KeySamples(Type type) =>
        type switch
        {
            _ when type == typeof(int) => [0, 1, -7, int.MaxValue, int.MinValue],
            _ when type == typeof(long) => [0L, 1L, -7L, long.MaxValue, long.MinValue],
            _ when type == typeof(short) => [(short)0, (short)-7, short.MaxValue],
            _ when type == typeof(byte) => [(byte)0, byte.MaxValue],
            _ when type == typeof(string) => ["abc", "k-001", "a,b|c", "Café"],
            _ when type == typeof(decimal) => DecimalKeySamples
                .Select(sample => (object)sample.Value)
                .ToList(),
            _ when type == typeof(Guid) => [new Guid("0f8fad5b-d9cb-469f-a165-70867728950e")],
            _ when type == typeof(DateTime) =>
            [
                new DateTime(2026, 10, 7, 13, 45, 30, 123, DateTimeKind.Unspecified),
                new DateTime(2026, 10, 7, 13, 45, 30, DateTimeKind.Utc),
                new DateTime(2026, 10, 7, 13, 45, 30, DateTimeKind.Local),
            ],
            _ when type == typeof(DateTimeOffset) =>
            [
                new DateTimeOffset(2026, 10, 7, 13, 45, 30, TimeSpan.FromHours(9)),
            ],
            // 元の SyncSupportGenerationTests.SyncKeyText_TimeLikePrimaryKeys_RoundTrip の見本
            _ when type == typeof(TimeSpan) =>
            [
                new TimeSpan(2, 13, 45, 30, 123).Add(TimeSpan.FromTicks(4)),
                TimeSpan.FromHours(-3),
                TimeSpan.Zero,
            ],
            _ when type == typeof(TimeOnly) =>
            [
                new TimeOnly(13, 45, 30, 123).Add(TimeSpan.FromTicks(4)),
                TimeOnly.MinValue,
            ],
            _ when type == typeof(DateOnly) => [new DateOnly(2026, 10, 7), DateOnly.MaxValue],
            _ when type == typeof(byte[]) => [new byte[] { 0x00, 0x7F, 0xFF }],
            _ => null,
        };

    /// <summary>読み戻したキーが元のキーと同じか（バイト列は中身で比べる）</summary>
    private static bool SameKey(object? parsed, object key) =>
        parsed is byte[] left && key is byte[] right
            ? left.SequenceEqual(right)
            : Equals(parsed, key);

    private static string Show(object? value) =>
        value is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.InvariantCulture)
            : value?.ToString() ?? "null";
}
