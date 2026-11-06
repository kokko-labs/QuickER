using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using QuickER.CodeGen.CSharp;
using QuickER.CodeReverse.CSharp;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
using QuickER.Services;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Gui.Services.FormatRoundTrip;

/// <summary>形式が出力したもの（テキストの写しと、取込へ渡す本体）</summary>
/// <param name="Text">注入・カルチャ・記法の照合に使うテキストの写し（Excel はセルを並べた写し・C# は生成ファイルの連結）</param>
/// <param name="Native">取込へ渡す本体（文字列・Excel のブック）。破棄が要るものは <see cref="Dispose"/> が破棄する</param>
internal sealed record FormatArtifact(string Text, object Native) : IDisposable
{
    /// <inheritdoc/>
    public void Dispose() => (Native as IDisposable)?.Dispose();
}

/// <summary>取り込んだもの</summary>
/// <param name="Diagram">取り込んだ図</param>
/// <param name="Warnings">取込の警告（C# リバースの警告。ほかの形式は空）</param>
/// <param name="Extra">マージの経路が使う取込の付帯情報（C# リバースの結果。ほかは null）</param>
internal sealed record FormatImport(
    ErDiagram Diagram,
    IReadOnlyList<string> Warnings,
    object? Extra = null
);

/// <summary>形の行が形式ごとに渡す設定（使わない形式は読まない）</summary>
/// <param name="CSharpOptions">C# の生成のオプション（null なら <see cref="CSharpReverseAdapter.DefaultOptions"/>）</param>
/// <param name="CSharpDialect">C# の生成とリバースの方言（null なら図の対象 DBMS）</param>
internal sealed record FormatSettings(
    CodeGenerationOptions? CSharpOptions = null,
    string? CSharpDialect = null
)
{
    /// <summary>既定の設定</summary>
    public static FormatSettings Default { get; } = new();
}

/// <summary>運ばない面の宣言（面と、運ばないと言える根拠の出どころ）</summary>
/// <param name="Facet">運ばない面</param>
/// <param name="Evidence">根拠（CLAUDE.md・docs・製品のコードのコメントの出どころ）</param>
internal sealed record NotCarriedFacet(Facet Facet, string Evidence);

/// <summary>告知するが、取込が別の手がかりから推論し直すので往復で同じ値に戻ることがある種類の宣言</summary>
/// <param name="Kind">告知の種類</param>
/// <param name="Evidence">根拠（docs・CLAUDE.md の出どころ）</param>
internal sealed record ReinferredOmission(ExportOmissionKind Kind, string Evidence);

/// <summary>形式をまたいで同じ根拠を持つ宣言</summary>
internal static class SharedDeclarations
{
    /// <summary>名前の無いリレーションの既定名がふさがっているときに取込が名前を付ける（取込のある形式すべて）</summary>
    public static NotCarriedFacet UnnamedRelationshipNaming { get; } =
        new(
            Facet.ConstraintNameFilled,
            "docs/import-export.ja.md「形式に共通の規則」：同じ 2 テーブル間の名前の無い 2 本目のように既定名が既に使われているときは、取込が名前を付ける"
        );
}

/// <summary>形式のアダプタ（形式の違いはこのメンバーだけで表し、行と表明の中で形式を名指しして分岐しない）</summary>
internal abstract class FormatAdapter : IMatrixAdapter
{
    /// <inheritdoc/>
    public abstract string AxisValue { get; }

    /// <summary>出力する</summary>
    public abstract FormatArtifact Export(ErDiagram diagram, FormatSettings settings);

    /// <summary>取込ができるか</summary>
    public virtual bool CanImport => true;

    /// <summary>出力したもの（または規則の行の入力）を取り込む</summary>
    /// <param name="artifact">成果物</param>
    /// <param name="targetDbms">取込に渡す対象 DBMS（形式が運ばないときの既定）</param>
    /// <param name="settings">設定</param>
    public abstract FormatImport Import(
        FormatArtifact artifact,
        string targetDbms,
        FormatSettings settings
    );

    /// <summary>取込が警告を返すか（取込の結果に警告の口を持つ形式だけ真）</summary>
    public virtual bool ReportsImportWarnings => false;

    /// <summary>規則の行の文字列の入力を成果物にする（文字列で書けない形式は断る）</summary>
    public virtual FormatArtifact FromText(string text) => new(text, text);

    /// <summary>欠落を告知する（告知しない形式は null）</summary>
    public virtual IReadOnlyList<ExportOmissionKind>? DetectOmissions(ErDiagram diagram) => null;

    /// <summary>その形式が告知しうる種類（告知しない形式は空）</summary>
    public virtual IReadOnlySet<ExportOmissionKind> ReportableOmissions { get; } =
        new HashSet<ExportOmissionKind>();

    /// <summary>
    /// 告知するが取込が推論し直す種類と根拠（この種類は、往復で同じ値に戻っても、元の図に中身があれば告知してよい）
    /// </summary>
    public virtual IReadOnlyList<ReinferredOmission> Reinferred { get; } = [];

    /// <summary>告知せずに運ばない面と根拠（告知する形式は空＝運ばない面はすべて告知が説明する）</summary>
    public virtual IReadOnlyList<NotCarriedFacet> NotCarried { get; } = [];

    /// <summary>マージ取込の経路（無い形式は null）</summary>
    public virtual Func<ErDiagram, FormatImport, FormatMergeOutcome>? Merge => null;

    /// <summary>マージ取込が現在の図から戻す面</summary>
    public virtual IReadOnlySet<Facet> MergeRestores { get; } = new HashSet<Facet>();

    /// <summary>形式が素で書けない文字（これを含む名前・説明は出力にそのまま出てはいけない。無い形式は空）</summary>
    public virtual char[] MustEscape => [];

    /// <summary>運ばない面の集合</summary>
    public IReadOnlySet<Facet> NotCarriedFacets =>
        NotCarried.Select(item => item.Facet).ToHashSet();
}

/// <summary>DBML</summary>
internal sealed class DbmlAdapter : FormatAdapter
{
    /// <inheritdoc/>
    public override char[] MustEscape => ['\r', '\n'];

    /// <inheritdoc/>
    public override string AxisValue => nameof(DiagramExportFormat.Dbml);

    /// <inheritdoc/>
    public override FormatArtifact Export(ErDiagram diagram, FormatSettings settings)
    {
        var text = DbmlExporter.Build(diagram);
        return new FormatArtifact(text, text);
    }

    /// <inheritdoc/>
    public override FormatImport Import(
        FormatArtifact artifact,
        string targetDbms,
        FormatSettings settings
    ) => new(DbmlImporter.Parse((string)artifact.Native, targetDbms), []);

    /// <inheritdoc/>
    public override IReadOnlyList<ExportOmissionKind>? DetectOmissions(ErDiagram diagram) =>
        DbmlExporter.DetectOmissions(diagram);

    /// <inheritdoc/>
    public override IReadOnlySet<ExportOmissionKind> ReportableOmissions { get; } =
        new HashSet<ExportOmissionKind>
        {
            ExportOmissionKind.TableMemo,
            ExportOmissionKind.NamedQuery,
            ExportOmissionKind.ForeignKeyMarkerWithoutRelationship,
            ExportOmissionKind.ComputedColumn,
            ExportOmissionKind.IdentityColumn,
            ExportOmissionKind.RelationshipWithoutColumns,
        };

    /// <inheritdoc/>
    public override IReadOnlyList<NotCarriedFacet> NotCarried { get; } =
    [
        SharedDeclarations.UnnamedRelationshipNaming,
        new(
            Facet.NameSanitized,
            "src/QuickER.Gui/Services/CLAUDE.md「出力のエスケープ」：Mermaid / DBML の識別子・ラベルは ExportTextSanitizer.Sanitize で改行・制御文字を空白へ畳む"
        ),
        new(
            Facet.DescriptionLineBreaks,
            "src/QuickER.Gui/Services/CLAUDE.md「出力のエスケープ」：DBML のリテラルは改行を空白へ畳む"
        ),
        new(
            Facet.PrimaryKeyNullability,
            "src/QuickER.Gui/Services/DbmlImporter.cs（419〜420 行）：取込は pk の列を NOT NULL にする（docs 未記載）"
        ),
        new(
            Facet.ManyToManyPairs,
            "docs/import-export.ja.md「DBML」（エクスポート）：多 対 多のリレーションは各テーブルの先頭列で書き出し、取込はこの列を読まない"
        ),
        new(
            Facet.ForeignKeyMarkerDerived,
            "docs/import-export.ja.md「DBML（.dbml）」（読むもの）：外部キーの印は設定でなく、リレーションの子側の列であることから立てる"
        ),
        new(
            Facet.PlaceholderColumn,
            "docs/import-export.ja.md「DBML（.dbml）」（読むもの）：列を持たないテーブルには既定の PK 列（ID int）が補われる"
        ),
    ];
}

/// <summary>Mermaid</summary>
internal sealed class MermaidAdapter : FormatAdapter
{
    /// <inheritdoc/>
    public override char[] MustEscape => ['\r', '\n'];

    /// <inheritdoc/>
    public override string AxisValue => nameof(DiagramExportFormat.Mermaid);

    /// <inheritdoc/>
    public override FormatArtifact Export(ErDiagram diagram, FormatSettings settings)
    {
        var text = MermaidExporter.Build(diagram);
        return new FormatArtifact(text, text);
    }

    /// <inheritdoc/>
    public override FormatImport Import(
        FormatArtifact artifact,
        string targetDbms,
        FormatSettings settings
    ) => new(MermaidImporter.Parse((string)artifact.Native, targetDbms), []);

    /// <inheritdoc/>
    public override IReadOnlyList<ExportOmissionKind>? DetectOmissions(ErDiagram diagram) =>
        MermaidExporter.DetectOmissions(diagram);

    /// <inheritdoc/>
    public override IReadOnlySet<ExportOmissionKind> ReportableOmissions { get; } =
        Enum.GetValues<ExportOmissionKind>()
            .Except([
                ExportOmissionKind.ForeignKeyMarkerWithoutRelationship,
                ExportOmissionKind.RelationshipWithoutColumns,
            ])
            .ToHashSet();

    /// <inheritdoc/>
    public override IReadOnlyList<ReinferredOmission> Reinferred { get; } =
    [
        new(
            ExportOmissionKind.ForeignKeyColumnPairs,
            "docs/import-export.ja.md「形式ごとに運ばれる情報」：外部キーの列対応は Mermaid で「告知して落とす（取込は親の主キーから推論する）」"
        ),
    ];

    /// <inheritdoc/>
    public override IReadOnlyList<NotCarriedFacet> NotCarried { get; } =
    [
        SharedDeclarations.UnnamedRelationshipNaming,
        new(
            Facet.ManyToManyPairs,
            "docs/import-export.ja.md「Mermaid（.mmd / .mermaid）」：多対多のリレーションは列対応を持たない"
        ),
        new(
            Facet.NameSanitized,
            "src/QuickER.Gui/Services/CLAUDE.md「出力のエスケープ」：Mermaid / DBML の識別子・ラベルは ExportTextSanitizer.Sanitize で改行・制御文字を空白へ畳む"
        ),
        new(
            Facet.PrimaryKeyNullability,
            "src/QuickER.Gui/Services/MermaidImporter.cs（217 行）：取込は PK の標識の列を NOT NULL にする（docs 未記載）"
        ),
        new(
            Facet.BrokenColumnPairs,
            "src/QuickER.Model/CLAUDE.md「リレーションの列ペア」：解決不能な列の FK は DDL・差分の対象外。取込は引けない列を指す列ペアを持ち越さない（2026-11-05 レビュー役の判断）"
        ),
        new(
            Facet.ForeignKeyMarkerDerived,
            "docs/import-export.ja.md「Mermaid（.mmd / .mermaid）」：取込は列名から子の列を推論してペアにし、その子の列に外部キーの印を付ける"
        ),
        new(
            Facet.PlaceholderColumn,
            "src/QuickER.Gui/Services/MermaidImporter.cs（377 行付近の EnsureEntitiesHaveColumns）：カラムを持たないエンティティに既定の主キー列を補う（docs 未記載）"
        ),
    ];
}

/// <summary>Excel のテーブル定義書</summary>
internal sealed class ExcelAdapter : FormatAdapter
{
    /// <summary>定義書の言語（出力の見出し・文言）</summary>
    public static CultureInfo DocumentCulture { get; } = new("en");

    /// <inheritdoc/>
    public override string AxisValue => nameof(DiagramExportFormat.Excel);

    /// <inheritdoc/>
    public override FormatArtifact Export(ErDiagram diagram, FormatSettings settings)
    {
        var workbook = TableDefinitionDocumentExporter.BuildWorkbook(diagram, DocumentCulture);
        return new FormatArtifact(Describe(workbook), workbook);
    }

    /// <inheritdoc/>
    public override FormatImport Import(
        FormatArtifact artifact,
        string targetDbms,
        FormatSettings settings
    ) => new(TableDefinitionDocumentImporter.Load((XLWorkbook)artifact.Native, targetDbms), []);

    /// <summary>ブックはテキストで書けない（規則の行はブックを組む）</summary>
    public override FormatArtifact FromText(string text) =>
        throw new NotSupportedException("Excel の規則の行はブックを組んで渡す");

    /// <summary>ブックの全シートの使われているセルを並べる</summary>
    public static string Describe(XLWorkbook workbook)
    {
        var builder = new StringBuilder();

        foreach (var sheet in workbook.Worksheets)
        {
            foreach (var cell in sheet.CellsUsed())
            {
                builder
                    .Append(sheet.Name)
                    .Append('!')
                    .Append(cell.Address.ToString())
                    .Append('=')
                    .Append(cell.Value.ToString(CultureInfo.InvariantCulture))
                    .Append('\n');
            }
        }

        return builder.ToString();
    }

    /// <inheritdoc/>
    public override IReadOnlyList<NotCarriedFacet> NotCarried { get; } =
    [
        SharedDeclarations.UnnamedRelationshipNaming,
        new(
            Facet.ManyToManyPairs,
            "src/QuickER.Gui/Services/TableDefinitionDocumentImporter.cs（464 行）：取込は多対多の行の列を列ペアとして読まない（docs 未記載）"
        ),
        new(
            Facet.BrokenColumnPairs,
            "src/QuickER.Model/CLAUDE.md「リレーションの列ペア」：解決不能な列の FK は DDL・差分の対象外。取込は引けない列を指す列ペアを持ち越さない（2026-11-05 レビュー役の判断）"
        ),
        new(
            Facet.ForeignKeyMarkerDerived,
            "src/QuickER.Gui/Services/TableDefinitionDocumentImporter.cs（544 行）：リレーション一覧の子の列に外部キーの印を立てる（docs 未記載）"
        ),
        new(
            Facet.UniqueName,
            "docs/import-export.ja.md「Excel 定義書（.xlsx）」（インポート）：制約名は定義書に載らないため未設定"
        ),
        new(
            Facet.Query,
            "docs/import-export.ja.md「形式ごとに運ばれる情報」：名前付きクエリは Excel 定義書で「マージ取込で温存する」"
        ),
        new(
            Facet.UniqueOrder,
            "docs/import-export.ja.md「Excel 定義書（.xlsx）」（インポート）：定義書は制約の構成列の順序を載せない"
        ),
        new(
            Facet.PrimaryKeyOrder,
            "docs/import-export.ja.md「Excel 定義書（.xlsx）」（インポート）：定義書は複合主キーの順序を載せない"
        ),
        new(
            Facet.Computed,
            "docs/import-export.ja.md「Excel 定義書（.xlsx）」（インポート）：定義書は計算列の指定を載せない"
        ),
        new(
            Facet.IdentityNonKey,
            "docs/import-export.ja.md「Excel 定義書（.xlsx）」（インポート）：定義書は DB が採番する列の指定を載せない"
        ),
        new(
            Facet.IdentityKey,
            "docs/import-export.ja.md「Excel 定義書（.xlsx）」（インポート）：定義書は DB が採番する列の指定を載せない"
        ),
    ];

    /// <inheritdoc/>
    public override Func<ErDiagram, FormatImport, FormatMergeOutcome>? Merge => MergeExcel;

    /// <inheritdoc/>
    public override IReadOnlySet<Facet> MergeRestores { get; } =
        new HashSet<Facet>
        {
            Facet.UniqueName,
            Facet.UniqueOrder,
            Facet.PrimaryKeyOrder,
            Facet.Computed,
            Facet.IdentityNonKey,
            Facet.IdentityKey,
            Facet.Query,
        };

    /// <summary>Excel のマージ取込（製品の <c>DiagramImportService.ImportExcelMerging</c> と同じ並び）</summary>
    private static FormatMergeOutcome MergeExcel(ErDiagram current, FormatImport imported)
    {
        var merged = DiagramMergeReconciler.Reconcile(
            current,
            imported.Diagram.Entities,
            imported.Diagram.Relationships,
            preserveExistingMemo: false
        );
        TableDefinitionMergePostProcessor.Apply(current, merged.Entities);

        return new FormatMergeOutcome(
            new ErDiagram
            {
                TargetDbms = imported.Diagram.TargetDbms,
                Entities = merged.Entities.ToList(),
                Relationships = merged.Relationships.ToList(),
                Queries = merged.SurvivingQueries.ToList(),
            },
            merged
        );
    }
}

/// <summary>HTML のテーブル定義書（出力だけ）</summary>
internal sealed class HtmlAdapter : FormatAdapter
{
    /// <inheritdoc/>
    public override char[] MustEscape => ['<', '>', '&'];

    /// <inheritdoc/>
    public override string AxisValue => nameof(DiagramExportFormat.Html);

    /// <inheritdoc/>
    public override FormatArtifact Export(ErDiagram diagram, FormatSettings settings)
    {
        var text = TableDefinitionHtmlExporter.Build(diagram, ExcelAdapter.DocumentCulture);
        return new FormatArtifact(text, text);
    }

    /// <inheritdoc/>
    public override bool CanImport => false;

    /// <inheritdoc/>
    public override FormatImport Import(
        FormatArtifact artifact,
        string targetDbms,
        FormatSettings settings
    ) => throw new NotSupportedException("HTML の定義書は取り込めない");
}

/// <summary>C# リバース（図から C# を生成し、リバースで読み戻す）</summary>
internal sealed class CSharpReverseAdapter : FormatAdapter
{
    /// <inheritdoc/>
    public override char[] MustEscape => ['\r', '\n'];

    /// <summary>軸の値（製品の出力形式の列挙に無い、テスト側の 1 値）</summary>
    public const string Value = "CSharpReverse";

    /// <summary>既定の生成のオプション（1 ファイル・データ注釈あり・EditModel と Mapper なし。リバースが読むのはエンティティだけ）</summary>
    public static CodeGenerationOptions DefaultOptions { get; } =
        new()
        {
            RootNamespace = "QuickER.Tests.FormatRoundTrip",
            SplitFilesByCategory = false,
            IncludeDataAnnotations = true,
            GenerateEditModels = false,
            GenerateMappers = false,
        };

    /// <summary>方言の名前からプロバイダを引く</summary>
    public static DatabaseProviderRegistry Providers { get; } =
        new([
            new SqlServerProvider(),
            new PostgreSqlProvider(),
            new MySqlProvider(),
            new OracleProvider(),
            new SqliteProvider(),
        ]);

    /// <inheritdoc/>
    public override string AxisValue => Value;

    /// <inheritdoc/>
    public override bool ReportsImportWarnings => true;

    /// <inheritdoc/>
    public override FormatArtifact Export(ErDiagram diagram, FormatSettings settings)
    {
        var provider = Providers.Get(settings.CSharpDialect ?? diagram.TargetDbms);
        var generation = DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            diagram,
            settings.CSharpOptions ?? DefaultOptions
        );

        if (generation.HasErrors)
        {
            throw new InvalidOperationException(
                "C# の生成がエラーで止まった: "
                    + string.Join(
                        " / ",
                        generation
                            .Diagnostics.Where(d =>
                                d.Severity == GenerationDiagnosticSeverity.Error
                            )
                            .Select(d => d.Message)
                    )
            );
        }

        var text = string.Concat(generation.Files.Select(file => file.Content));
        return new FormatArtifact(text, text);
    }

    /// <inheritdoc/>
    public override FormatImport Import(
        FormatArtifact artifact,
        string targetDbms,
        FormatSettings settings
    )
    {
        var dialect = settings.CSharpDialect ?? targetDbms;
        var provider = Providers.Get(dialect);
        var reversed = new CSharpReverseParser().Parse(
            (string)artifact.Native,
            provider.TypeCatalog,
            IdentifierLengthLimits.ForDbms(provider.Name)
        );

        return new FormatImport(
            new ErDiagram
            {
                TargetDbms = dialect,
                Entities = reversed.Entities.ToList(),
                Relationships = reversed.Relationships.ToList(),
            },
            reversed.Warnings,
            reversed
        );
    }

    /// <inheritdoc/>
    public override IReadOnlyList<NotCarriedFacet> NotCarried { get; } =
    [
        SharedDeclarations.UnnamedRelationshipNaming,
        new(
            Facet.DescriptionLineBreaks,
            "docs/import-export.ja.md「C# コード（.cs）」：複数行の説明はコード生成の時点で空白へ畳まれる"
        ),
        new(
            Facet.PrimaryKeyNullability,
            "src/QuickER.CodeGen.CSharp/CLAUDE.md「主キーの列の NULL 許容」：コード生成は主キーの列を NULL 許容の印に依らず NOT NULL とみなす（プロパティは NULL を許さない型）ので、C# コードから取り込むと主キーの列は NOT NULL になる"
        ),
        new(
            Facet.BrokenColumnPairs,
            "src/QuickER.Model/CLAUDE.md「リレーションの列ペア」：解決不能な列の FK は DDL・差分の対象外。取込は引けない列を指す列ペアを持ち越さない（2026-11-05 レビュー役の判断）"
        ),
        new(
            Facet.ForeignKeyMarkerDerived,
            "src/QuickER.CodeReverse.CSharp/CSharpReverseParser.cs（939 行）：リバースは [NavigationReference] の子の列に外部キーの印を立てる（docs 未記載）"
        ),
        new(
            Facet.ForeignKeyMarkerOutsideRelationship,
            "src/QuickER.CodeReverse.CSharp/CSharpReverseParser.cs（939 行）：リバースが外部キーの印を立てるのは [NavigationReference] の子の列だけ（docs 未記載）"
        ),
        new(
            Facet.PairlessRelationship,
            "src/QuickER.CodeGen.CSharp/CSharpGenerationModelBuilder.cs（1073 行）：生成はキーが不明（列ペアが無い）なリレーションのナビゲーションを飛ばして警告し、取り込むとリレーションは消える（C# リバースの仕様として受ける・2026-11-05 ユーザーの判断）"
        ),
        new(
            Facet.CompositeRelationship,
            "docs/import-export.ja.md「C# コード（.cs）」：複合外部キー（列ペアが 2 組以上）はコード生成が飛ばすため、C# コードから取り込むと図から消える"
        ),
        new(
            Facet.Memo,
            "docs/import-export.ja.md「C# コード（.cs）」：復元元の表にメモが無い（src/QuickER.CodeGen.UI/CodeReverseCommandService.cs：コードは Memo を持たない）"
        ),
        new(
            Facet.Query,
            "docs/import-export.ja.md「C# コード（.cs）」：復元元の表に名前付きクエリが無い"
        ),
        new(
            Facet.ManyToMany,
            "docs/import-export.ja.md「C# コード（.cs）」：多対多リレーションはコードに存在しない"
        ),
        new(
            Facet.IdentityKey,
            "src/QuickER.CodeReverse.CSharp/ReverseMergePostProcessor.cs：生成コードは主キーの列に採番の印を刻まない"
        ),
        new(
            Facet.UniqueNameFilled,
            "docs/import-export.ja.md「C# コード（.cs）」：コード生成は名前の無い制約にも合成した名前を Name に書くため、生成コードから取り込んだ制約はその合成名を名前として持つ"
        ),
        new(
            Facet.TypeCase,
            "src/QuickER.CodeGen.CSharp/CLAUDE.md「同義の型表記の保存（verbatim 併記）」：綴り一致の判定は DbTypeText.AreEquivalent（Trim ＋大小無視）で、大小だけ違う列には NativeType を刻まない。リバースはトークンを方言の表記（SQLite は大文字）で書き戻し、マージ取込が現在図の綴りへ戻す"
        ),
    ];

    /// <inheritdoc/>
    public override Func<ErDiagram, FormatImport, FormatMergeOutcome>? Merge => MergeReverse;

    /// <inheritdoc/>
    public override IReadOnlySet<Facet> MergeRestores { get; } =
        new HashSet<Facet> { Facet.Memo, Facet.ManyToMany, Facet.IdentityKey, Facet.TypeCase };

    /// <summary>C# リバースのマージ取込（製品の <c>CodeReverseCommandService</c> と同じ並び）</summary>
    private static FormatMergeOutcome MergeReverse(ErDiagram current, FormatImport imported)
    {
        var reversed = (CodeReverseResult)imported.Extra!;
        var merged = DiagramMergeReconciler.Reconcile(
            current,
            imported.Diagram.Entities,
            imported.Diagram.Relationships,
            preserveExistingMemo: true
        );
        var relationships = ReverseMergePostProcessor.Apply(
            current,
            merged.Entities,
            merged.Relationships,
            reversed.RelationshipMetadata
        );
        ReverseMergePostProcessor.ApplyPrimaryKeyOrder(
            current,
            merged.Entities,
            reversed.TablesWithPrimaryKeyOrder
        );
        ReverseMergePostProcessor.ApplyPrimaryKeyIdentityMarks(current, merged.Entities);
        ReverseMergePostProcessor.ApplyTypeSpellings(current, merged.Entities);

        return new FormatMergeOutcome(
            new ErDiagram
            {
                TargetDbms = imported.Diagram.TargetDbms,
                Entities = merged.Entities.ToList(),
                Relationships = relationships.ToList(),
                Queries = merged.SurvivingQueries.ToList(),
            },
            merged
        );
    }
}

/// <summary>形式の軸（正本＝製品の出力形式の列挙＋C# リバース）</summary>
internal static class FormatAxis
{
    /// <summary>正本の値（製品の <see cref="DiagramExportFormat"/> の名前の並び＋<see cref="CSharpReverseAdapter.Value"/>）</summary>
    public static IReadOnlyList<string> Names { get; } =
    [.. Enum.GetNames<DiagramExportFormat>(), CSharpReverseAdapter.Value];

    /// <summary>アダプタを持たない値と理由</summary>
    public static IReadOnlyDictionary<string, string> Excluded { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [nameof(DiagramExportFormat.Png)] =
                "入力が画面の表示と座標で意味モデルの往復の外（ImageExportServiceTests・DiagramExportServiceTests が持つ）",
            [nameof(DiagramExportFormat.Svg)] =
                "入力が画面の表示と座標で意味モデルの往復の外（ImageExportServiceTests が持つ）",
            [nameof(DiagramExportFormat.Sql)] =
                "DDL は方言のエンジン（Dialect）と名前の DDL の往復（Name/DdlRoundTrip）が持つ",
            [nameof(DiagramExportFormat.SchemaJson)] =
                "図のファイルのエンジン（DocumentFile）が持つ",
        };

    /// <summary>形式の軸（値ごとにちょうど 1 つのアダプタ）</summary>
    public static MatrixAxis<FormatAdapter> Axis { get; } =
        new(
            "Format",
            Names,
            [
                new DbmlAdapter(),
                new MermaidAdapter(),
                new ExcelAdapter(),
                new HtmlAdapter(),
                new CSharpReverseAdapter(),
            ],
            Excluded
        );

    /// <summary>取込ができる形式の正本（製品の <see cref="DiagramImportFormat"/> の名前＋<see cref="CSharpReverseAdapter.Value"/>）</summary>
    public static IReadOnlyList<string> ImportableNames { get; } =
    [.. Enum.GetNames<DiagramImportFormat>(), CSharpReverseAdapter.Value];
}
