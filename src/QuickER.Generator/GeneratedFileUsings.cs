namespace QuickER.Generator;

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
            foreach (var ns in FrameworkUsings(bucket, options))
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
        return ordered;
    }

    /// <summary>
    /// 1 バケットが必要とする外部（System.* / Microsoft.*）using 集合を、テンプレートの型参照を根拠に返す。
    /// </summary>
    /// <remarks>
    /// System / System.Collections.Generic / System.Linq は共有フレームワークに常時含まれ生成コードのほぼ全構成で使うため、
    /// 該当バケットへ広めに付与する（既存方針を踏襲）。オプションで出力が変わる箇所（VO 有無・DataAnnotations・
    /// 自作 SQL Server 実装の有無）はその条件を反映する。
    /// </remarks>
    private static IEnumerable<string> FrameworkUsings(
        GenerationBucket bucket,
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

                // 自作 SQL Server 実装（SqlExecutor / SqlServerRepository / 接続ファクトリ / AddGeneratedRepositories）:
                //   SqlConnection/SqlParameter（Microsoft.Data.SqlClient）、DI 登録（Microsoft.Extensions.DependencyInjection）、
                //   さらに実装が使う IStructuralEquatable（System.Collections）・DataTable 相当（System.Data）・
                //   JSON（System.Text.Json / System.Text.Json.Serialization.Metadata）
                if (options.GenerateRepositories)
                {
                    yield return "System.Collections";
                    yield return "System.Data";
                    yield return "System.Text.Json";
                    yield return "System.Text.Json.Serialization.Metadata";
                    yield return "Microsoft.Data.SqlClient";
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
