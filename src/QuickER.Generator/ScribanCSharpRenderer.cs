using System.Text;
using System.Text.RegularExpressions;
using Scriban;

namespace QuickER.Generator;

/// <summary>1 ファイル分の描画スコープ。名前空間・using と、出力するバケットの選択を表す</summary>
internal sealed class RenderScope
{
    /// <summary>このファイルの名前空間</summary>
    public required string NamespaceName { get; init; }

    /// <summary>このファイル冒頭に出力する using 名前空間一覧</summary>
    public required IReadOnlyList<string> Usings { get; init; }

    /// <summary>共有基盤（属性・基底・VO 基底・RowState）を出力するか</summary>
    public required bool Runtime { get; init; }

    /// <summary>値オブジェクトの具象クラスを出力するか</summary>
    public required bool ValueObjects { get; init; }

    /// <summary>Entity クラスを出力するか</summary>
    public required bool Entities { get; init; }

    /// <summary>EditModel クラスを出力するか</summary>
    public required bool EditModels { get; init; }

    /// <summary>Mapper クラスを出力するか</summary>
    public required bool Mappers { get; init; }

    /// <summary>Repository バケット（共通契約＋自作 SQL Server 実装）を出力するか</summary>
    public required bool Repositories { get; init; }

    /// <summary>EF Core 用コード（DbContext・構成）を出力するか</summary>
    public required bool EfCore { get; init; }

    /// <summary>自作 SQL Server 実装（SqlClient 依存）を出力するか。Repository バケット内でこのフラグにより契約と実装を出し分ける</summary>
    public required bool SqlServerImpl { get; init; }
}

/// <summary>生成モデルを Scriban テンプレートで C# ソースコードへレンダリングするレンダラー</summary>
internal sealed class ScribanCSharpRenderer
{
    /// <summary>Entity / EditModel / Mapper / Repository を一括出力する Scriban テンプレート本文</summary>
    /// <remarks>
    /// テンプレート本文はソースに埋め込まず、埋め込みリソース（Templates/CSharpRuntime.scriban）として保持する。
    /// インデントは半角スペース 4 つで統一する（タブは使用しない）。
    /// </remarks>
    private static readonly string TemplateText = LoadTemplate();

    /// <summary>埋め込みリソースから Scriban テンプレート本文を読み込む</summary>
    private static string LoadTemplate()
    {
        const string resourceName = "QuickER.Generator.Templates.CSharpRuntime.scriban";
        var assembly = typeof(ScribanCSharpRenderer).Assembly;
        using var stream =
            assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"埋め込みリソース '{resourceName}' が見つかりません。{Environment.NewLine}"
                    + $"アセンブリ '{assembly.GetName().Name}' に Templates/CSharpRuntime.scriban が "
                    + "EmbeddedResource として含まれているか確認してください。"
            );
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>テンプレートは固定なので一度だけ解析してキャッシュする（分割時は同じテンプレートを範囲を変えて複数回描画する）</summary>
    private static readonly Template ParsedTemplate = ParseTemplate();

    /// <summary>テンプレートを解析し、解析エラーがあれば例外を投げる</summary>
    private static Template ParseTemplate()
    {
        var template = Template.Parse(TemplateText);
        if (template.HasErrors)
        {
            var message = string.Join(
                Environment.NewLine,
                template.Messages.Select(m => m.ToString())
            );
            throw new InvalidOperationException(
                $"C# 生成テンプレートの解析に失敗しました。{Environment.NewLine}{message}"
            );
        }

        return template;
    }

    /// <summary>
    /// 生成モデルとオプションを、指定スコープ（名前空間・using・出力するバケット）でテンプレートへ流し込み、C# ソースコード文字列を生成する
    /// </summary>
    /// <remarks>非分割時は全バケットを 1 回で、分割時はファイルごとにバケットを絞って複数回呼び出す</remarks>
    public string Render(
        CSharpGenerationModel model,
        CodeGenerationOptions options,
        RenderScope scope
    )
    {
        var template = ParsedTemplate;

        // 独自属性 NavigationReference は (1) Entity のナビゲーションプロパティへの付与、
        // (2) 共通契約の EntitySaveMetadata / SqlQuery によるナビゲーション除外・Include 復元（リフレクション走査）で参照される。
        // リレーションが無くても契約（自作 Repository か EF Core のいずれか）を生成する場合は属性定義が必要なため、その条件も含める。
        var emitNavRefAttr =
            (options.GenerateEntityClasses && model.EntityClasses.Any(c => c.Navigations.Count > 0))
            || options.GenerateRepositories
            || options.GenerateEfCore;

        // SqlColumnType 属性は Entity プロパティに DB 列のメタ情報（SqlDbType・Size・Precision・Scale）を載せる。
        // ランタイムの EntitySaveMetadata が明示 SqlParameter を組み立てるのに使うほか、利用者コードが列メタ情報
        // （最大長・桁数）を参照する用途も兼ねるため、Repository 生成時 または IncludeDataAnnotations 時のいずれか、
        // かつ SqlDbType が判明したプロパティが 1 つでもある場合に属性定義と付与を出力する。
        var emitSqlColumnTypeAttr =
            (options.GenerateRepositories || options.IncludeDataAnnotations)
            && model.EntityClasses.Any(c => c.Properties.Any(p => p.SqlDbTypeName is not null));

        // EF Core の using はこのファイルが EfCore バケットを含むときのみ付与する。
        // これにより GenerateEfCore=false や、分割時の非 EfCore ファイルへ EF 依存が漏れない
        var usings = scope.Usings;
        if (scope.EfCore)
        {
            // 値オブジェクトの文字列メソッド翻訳プラグイン（IMethodCallTranslatorPlugin 等）で使う
            // EF Core の Query / Storage / Infrastructure / Diagnostics 名前空間も併せて補う。
            string[] efNamespaces =
            [
                "Microsoft.EntityFrameworkCore",
                "Microsoft.EntityFrameworkCore.Diagnostics",
                "Microsoft.EntityFrameworkCore.Infrastructure",
                "Microsoft.EntityFrameworkCore.Query",
                "Microsoft.EntityFrameworkCore.Query.SqlExpressions",
                "Microsoft.EntityFrameworkCore.Storage",
                "Microsoft.Extensions.DependencyInjection",
                "Microsoft.Extensions.DependencyInjection.Extensions",
            ];
            var missing = efNamespaces.Where(ns => !usings.Contains(ns)).ToArray();
            usings = missing.Length == 0 ? usings : [.. usings, .. missing];
        }

        var scriptObject = new Scriban.Runtime.ScriptObject
        {
            ["namespace_name"] = scope.NamespaceName,
            ["usings"] = usings,
            ["entity_classes"] = model.EntityClasses,
            ["edit_model_classes"] = model.EditModelClasses,
            ["mapper_classes"] = model.MapperClasses,
            ["repository_classes"] = model.RepositoryClasses,
            ["include_data_annotations"] = options.IncludeDataAnnotations,
            ["include_json_ignore_on_parent_navigation"] =
                options.IncludeJsonIgnoreOnParentNavigation,
            ["emit_nav_ref_attr"] = emitNavRefAttr,
            ["emit_sql_column_type_attr"] = emitSqlColumnTypeAttr,
            ["generate_value_objects"] = options.GenerateValueObjects,
            ["value_object_classes"] = model.ValueObjectClasses,
            ["ef_core"] = model.EfCore,
            // 出力するバケットの絞り込み（分割時はファイルごとに切り替える。非分割時は全 true）
            ["render_runtime"] = scope.Runtime,
            ["render_value_objects"] = scope.ValueObjects,
            ["render_entities"] = scope.Entities,
            ["render_edit_models"] = scope.EditModels,
            ["render_mappers"] = scope.Mappers,
            ["render_repositories"] = scope.Repositories,
            ["render_ef_core"] = scope.EfCore,
            // 共通契約（インターフェイス・SqlQuery・メタデータ・グラフセーバ・RawSqlMapper 等）を出力するか。
            // 契約は Repository バケットに属し、分割時も Repository バケットのファイルにのみ出力する（EF 側は using で参照）
            ["render_contract"] = scope.Repositories,
            // 自作 SQL Server 実装（SqlClient 依存）を出力するか。Repository バケット内でこのフラグにより契約と実装を出し分ける
            // （EF 単独出力＝false のとき SqlClient 依存のコードを一切生成しない）
            ["repositories"] = scope.SqlServerImpl,
        };

        // テンプレートは本ライブラリ内に固定で持つ信頼済みのものであり、ループ回数・出力量は ER 図の規模に
        // 応じて正当に増減する。Scriban 既定の上限のままだと大規模スキーマで出力が無言で打ち切られるため、
        // 関連する上限をすべて無効化（0 = 無制限）して全件を確実に出力する。
        //   - LoopLimit: ループ反復回数の上限（既定 1000）
        //   - LimitToString: レンダリング出力長の上限（既定 1MB = 1048576 文字。超過分は "..." で切り捨て）
        var context = new TemplateContext { LoopLimit = 0, LimitToString = 0 };

        context.PushGlobal(scriptObject);
        var rendered =
            template.Render(context).ReplaceLineEndings(Environment.NewLine).TrimEnd()
            + Environment.NewLine;

        // 条件ブロック（{{ if }}）のスキップ時などに生じる連続空行を 1 行へ正規化する。
        // C# では 2 行以上連続する空行は不要で、CSharpier も 1 行へ畳むため、それに合わせる。
        return Regex.Replace(
            rendered,
            $"(?:{Regex.Escape(Environment.NewLine)}){{3,}}",
            Environment.NewLine + Environment.NewLine
        );
    }
}
