namespace QuickER.CodeGen.CSharp;

/// <summary>
/// 生成ファイルの using を「バケット単位」で解決する唯一の正。
/// </summary>
/// <remarks>
/// ファイルの using ＝そのファイルに含まれる全バケットが必要とする外部（System.* / Microsoft.*）using の和集合
/// ＋ バケット依存グラフから導いた他ファイルの名前空間（クロス using）。
/// これによりバケットを含まないファイルへ SqlClient / EntityFrameworkCore / DependencyInjection 等が漏れない。
/// 各バケットの必要 using は <c>Templates/CSharpRuntime.scriban</c> の実際の型参照を根拠に確定している
/// （オプションで出力が変わるバケットはその条件も反映する）。生成コードは <c>// &lt;auto-generated /&gt;</c> のため
/// 未使用 using 警告は抑止され、和集合による軽微な過剰付与は無害。削りすぎ（＝コンパイルエラー）だけを避ける方針。
/// </remarks>
internal static class GeneratedFileUsings
{
    /// <summary>
    /// 指定ファイル（名前空間・含有バケット・クロス名前空間）とオプションから、冒頭に出力する using 一覧を解決する。
    /// </summary>
    public static IReadOnlyList<string> Resolve(
        GeneratedFileSpec spec,
        CodeGenerationOptions options
    )
    {
        var external = new HashSet<string>(StringComparer.Ordinal);

        foreach (var bucket in spec.Buckets)
        {
            foreach (var ns in FrameworkUsings(bucket, spec, options))
            {
                external.Add(ns);
            }
        }

        // System を先頭、続いて System.* を序数順、最後に Microsoft.* 等を序数順で安定的に並べる。
        // その後にクロス名前空間 using（プランナが依存グラフから解決済み・昇順）を続ける。
        var ordered = external
            .OrderByDescending(ns => ns == "System")
            .ThenByDescending(ns => ns.StartsWith("System", StringComparison.Ordinal))
            .ThenBy(ns => ns, StringComparer.Ordinal)
            .ToList();

        ordered.AddRange(spec.CrossNamespaceUsings);

        // パッケージ参照モード: 固定 infra（Runtime バケット・契約・方言エンジン・EF 部品）を出力しないため、
        // 生成コードは固定名前空間の型を using で参照する。プランナはこのモードで Runtime バケットのファイルを
        // 計画せず、Runtime を指すクロス using も付けない（PackageRuntimeUsings が唯一の供給元）。
        if (options.UseRuntimePackages)
        {
            foreach (var ns in PackageRuntimeUsings(spec, options))
            {
                if (!ordered.Contains(ns, StringComparer.Ordinal))
                {
                    ordered.Add(ns);
                }
            }
        }

        return ordered;
    }

    /// <summary>
    /// パッケージ参照モードで、指定ファイル（含有バケット・方言・生成対象）が参照すべき固定名前空間
    /// （<see cref="RuntimePackages"/>）の一覧を返す（唯一の正）。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>共有基盤・方言中立契約（EntityBase・属性・VO 基底・IRepository・SqlQuery・ISqlExecutor 等）→ <see cref="RuntimePackages.Core"/>。
    ///     いずれかのバケットを含むファイルに常に必要（Entity のみでも EntityBase／属性を参照するため）</item>
    ///   <item>Repository バケットを含み自作 Repository 実装を出す（<c>GenerateRepositories</c>）ファイル → その方言の
    ///     方言エンジン（<see cref="RuntimePackages.SqlServer"/> / <see cref="RuntimePackages.Sqlite"/>）。
    ///     エンティティ別実装が方言 Repository 基底・接続ファクトリ・実行器を参照する</item>
    ///   <item>EfCore バケットを含むファイル → <see cref="RuntimePackages.EntityFrameworkCore"/>
    ///     （DbContext・EF 版実装が EF 共通部品を参照する）</item>
    /// </list>
    /// マルチターゲット時は方言実装スペックが各自の方言エンジンだけを参照する（spec.Dialect による）。
    /// </remarks>
    private static IEnumerable<string> PackageRuntimeUsings(
        GeneratedFileSpec spec,
        CodeGenerationOptions options
    )
    {
        if (spec.Buckets.Count == 0)
        {
            yield break;
        }

        // コア（共通基盤＋方言中立契約）はいずれのバケットでも必要。
        yield return RuntimePackages.Core;

        // 自作 Repository 実装を出すファイルは、その方言の方言エンジンパッケージを参照する。
        // 契約のみ（マルチ方言の契約スペック・EF 単独出力）は方言エンジンを参照しない（コアの契約で足りる）。
        if (
            spec.Buckets.Contains(GenerationBucket.Repository)
            && options.GenerateRepositories
            && !spec.ContractOnly
        )
        {
            yield return IsSqliteDialect(spec) ? RuntimePackages.Sqlite : RuntimePackages.SqlServer;
        }

        // EF 生成物（DbContext・EF 版実装）は EF 共通部品パッケージを参照する。
        if (spec.Buckets.Contains(GenerationBucket.EfCore))
        {
            yield return RuntimePackages.EntityFrameworkCore;
        }
    }

    /// <summary>このスペックの方言が SQLite かどうか（ADO using の出し分けに使う）</summary>
    private static bool IsSqliteDialect(GeneratedFileSpec spec) =>
        string.Equals(spec.Dialect, "sqlite", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 1 バケットが必要とする外部（System.* / Microsoft.*）using 集合を、テンプレートの型参照を根拠に返す。
    /// </summary>
    /// <remarks>
    /// System / System.Collections.Generic / System.Linq は共有フレームワークに常時含まれ生成コードのほぼ全構成で使うため、
    /// 該当バケットへ広めに付与する（既存方針を踏襲）。オプションで出力が変わる箇所（VO 有無・DataAnnotations・
    /// 自作 Repository 実装の有無）はその条件を反映する。マルチ方言時は ADO using をスペックの方言に応じて、
    /// かつ方言実装スペック（<see cref="GeneratedFileSpec.ContractOnly"/> でない）にのみ付与する。
    /// </remarks>
    private static IEnumerable<string> FrameworkUsings(
        GenerationBucket bucket,
        GeneratedFileSpec spec,
        CodeGenerationOptions options
    )
    {
        switch (bucket)
        {
            // Runtime（共有基盤）: 属性・EntityBase・EditModelBase・VO 基底・RowState・各種 JSON コンバータ。
            //   EntityBase: 値比較 StructuralComparisons（System.Collections）、値プロパティのキャッシュ
            //     （System.Collections.Concurrent / System.Reflection）、ToJson/Clone（System.Text.Json(.Serialization)）
            //   EditModelBase: INotifyPropertyChanged/INotifyDataErrorInfo（System.ComponentModel）、
            //     EditModelCollection の ObservableCollection（System.Collections.ObjectModel）、GetErrors（System.Collections）
            //   SqlColumnType 属性: SqlDbType（System.Data）
            case GenerationBucket.Runtime:
                yield return "System";
                yield return "System.Collections";
                yield return "System.Collections.Concurrent";
                yield return "System.Collections.Generic";
                yield return "System.Collections.ObjectModel";
                yield return "System.ComponentModel";
                yield return "System.Data";
                yield return "System.Linq";
                yield return "System.Reflection";
                yield return "System.Text.Json";
                yield return "System.Text.Json.Serialization";
                break;

            // 値オブジェクト（具象）: 生成コードは Runtime の VO 基底を継承するだけで、外部型は BCL の基本のみ
            case GenerationBucket.ValueObject:
                yield return "System";
                yield return "System.Collections.Generic";
                break;

            // Entity: [Table]/[Key]/[Column]/[Required]/[MaxLength]（DataAnnotations(.Schema)）、
            //   [SqlColumnType] の SqlDbType（System.Data）、親参照ナビの [JsonIgnore]（System.Text.Json.Serialization）、
            //   ICollection<T>（System.Collections.Generic）
            case GenerationBucket.Entity:
                yield return "System";
                yield return "System.Collections.Generic";
                yield return "System.Data";
                yield return "System.Text.Json.Serialization";

                if (options.IncludeDataAnnotations)
                {
                    yield return "System.ComponentModel.DataAnnotations";
                    yield return "System.ComponentModel.DataAnnotations.Schema";
                }

                break;

            // EditModel: 生成コードは Runtime の EditModelBase / EditModelCollection を使うだけで、
            //   自ファイルの外部参照は BCL の基本のみ（コレクション型は Runtime 側の名前空間で解決）
            case GenerationBucket.EditModel:
                yield return "System";
                yield return "System.Collections.Generic";
                break;

            // Mapper: Entity↔EditModel 変換で LINQ（Select 等・System.Linq）とジェネリックコレクションを使う
            case GenerationBucket.Mapper:
                yield return "System";
                yield return "System.Collections.Generic";
                yield return "System.Linq";
                break;

            // Repository バケット: 共通契約（インターフェイス・SqlQuery・メタデータ・グラフセーバ・RawSqlMapper 等）は
            //   常に出力され、自作 SQL Server 実装は options.GenerateRepositories のときのみ加わる。
            //   契約側で使う外部型:
            //     LINQ 式ツリー（System.Linq.Expressions）、リフレクション（System.Reflection）、
            //     ConcurrentDictionary（System.Collections.Concurrent）、CultureInfo（System.Globalization）、
            //     非同期（System.Threading / System.Threading.Tasks）、RawSqlMapper の DbDataReader/DbCommand（System.Data.Common）、
            //     属性参照（DataAnnotations(.Schema)）。契約のみ（EF 単独）では SqlClient / DI は不要。
            case GenerationBucket.Repository:
                yield return "System";
                yield return "System.Collections.Generic";
                yield return "System.Collections.Concurrent";
                yield return "System.Data.Common";
                yield return "System.Globalization";
                yield return "System.Linq";
                yield return "System.Linq.Expressions";
                yield return "System.Reflection";
                yield return "System.Threading";
                yield return "System.Threading.Tasks";

                if (options.IncludeDataAnnotations)
                {
                    yield return "System.ComponentModel.DataAnnotations";
                    yield return "System.ComponentModel.DataAnnotations.Schema";
                }

                // 自作 Repository 実装（SqlExecutor / 方言別 Repository 基底 / 接続ファクトリ / AddGeneratedRepositories）:
                //   ADO 型（方言依存: SQL Server=Microsoft.Data.SqlClient / SQLite=Microsoft.Data.Sqlite）、
                //   DI 登録（Microsoft.Extensions.DependencyInjection）、さらに実装が使う
                //   IStructuralEquatable（System.Collections）・DataTable 相当（System.Data）。
                //   JSON（System.Text.Json / System.Text.Json.Serialization.Metadata）は FOR JSON 復元を使う
                //   SQL Server 方言のみで必要（SQLite はプレーン SELECT＋DataReader 実体化のため不要）。
                // 契約のみのスペック（マルチ方言の契約ファイル・EF 単独出力）は ADO / DI を出さない。
                // 方言実装スペック（!ContractOnly）だけがその方言の ADO を出す（依存排他ガードの一般化）。
                // パッケージ参照モードでは方言 Repository 基底・実行器・接続ファクトリ（ADO を使う固定 infra）は
                // 方言エンジンパッケージが持つため、生成側の ADO / JSON 直接依存は不要。DI 登録拡張だけが残るため
                // Microsoft.Extensions.DependencyInjection のみを付ける（SqlClient / Sqlite / System.Text.Json は落とす）。
                if (options.GenerateRepositories && !spec.ContractOnly)
                {
                    yield return "Microsoft.Extensions.DependencyInjection";

                    if (!options.UseRuntimePackages)
                    {
                        yield return "System.Collections";
                        yield return "System.Data";

                        if (IsSqliteDialect(spec))
                        {
                            yield return "Microsoft.Data.Sqlite";
                        }
                        else
                        {
                            yield return "System.Text.Json";
                            yield return "System.Text.Json.Serialization.Metadata";
                            yield return "Microsoft.Data.SqlClient";
                        }
                    }
                }

                // インメモリ Repository（InMemoryDataStore・InMemory{Entity}Repository・シーダー・
                // AddGeneratedInMemoryRepositories）: DI 登録拡張のため DependencyInjection を付ける。
                // 述語・並び順の式木 Compile（System.Linq.Expressions）・リフレクション（System.Reflection）・
                // LINQ（System.Linq）は契約バケットで既に付与済み。ADO・EF 依存は一切出さない（方言非依存）。
                if (spec.InMemory)
                {
                    yield return "Microsoft.Extensions.DependencyInjection";
                }

                break;

            // EfCore: DbContext / DbSet / ModelBuilder（Microsoft.EntityFrameworkCore）、AddGeneratedEfCoreRepositories の
            //   DI（Microsoft.Extensions.DependencyInjection(.Extensions)）、ADO 実行器の DbDataReader/DbCommand（System.Data.Common）、
            //   リフレクション・LINQ 式・非同期。VO の文字列メソッド翻訳プラグイン（IMethodCallTranslatorPlugin 等）で
            //   EF Core の Query / Storage / Infrastructure / Diagnostics 名前空間も使うため、EfCore バケットには EF 系一式を付与する
            case GenerationBucket.EfCore:
                yield return "System";
                yield return "System.Collections.Generic";
                yield return "System.Data.Common";
                yield return "System.Linq";
                yield return "System.Linq.Expressions";
                yield return "System.Reflection";
                yield return "System.Threading";
                yield return "System.Threading.Tasks";
                yield return "Microsoft.EntityFrameworkCore";
                yield return "Microsoft.EntityFrameworkCore.Diagnostics";
                yield return "Microsoft.EntityFrameworkCore.Infrastructure";
                yield return "Microsoft.EntityFrameworkCore.Query";
                yield return "Microsoft.EntityFrameworkCore.Query.SqlExpressions";
                yield return "Microsoft.EntityFrameworkCore.Storage";
                yield return "Microsoft.Extensions.DependencyInjection";
                yield return "Microsoft.Extensions.DependencyInjection.Extensions";
                break;
        }
    }
}
