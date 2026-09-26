using System;
using System.Linq;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using Xunit;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 計算列・生成列（<see cref="Column.IsComputed"/>）の生成結果を検証するテストクラス。
/// </summary>
/// <remarks>
/// 固定するのは 4 点:
/// マーカー属性 <c>[ComputedColumn]</c> の付与、<c>EntitySaveMetadata</c> の書き込み除外、
/// EF Core Fluent の <c>ValueGeneratedOnAddOrUpdate()</c>、そして生成時診断
/// （主キーが計算列ならエラー・除外した列の Info 通知）。
/// いずれも「計算列がある図／無い図」の両アームを名指しする（ドリフト検知を承認にしないため）。
/// </remarks>
public class ComputedColumnGenerationTests
{
    /// <summary>計算列 1 本を持つ最小ダイアグラム</summary>
    /// <param name="computed">true なら <c>total</c> 列を計算列にする</param>
    /// <param name="computedIsPrimaryKey">true なら計算列を主キーにする（診断エラーの検証用）</param>
    private static ErDiagram Diagram(bool computed, bool computedIsPrimaryKey = false) =>
        new()
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "items",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "item_id",
                            DataType = "int",
                            IsPrimaryKey = !computedIsPrimaryKey,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "qty",
                            DataType = "int",
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "total",
                            DataType = "decimal(21,2)",
                            IsNullable = true,
                            IsComputed = computed,
                            IsPrimaryKey = computedIsPrimaryKey,
                        },
                    ],
                },
            ],
        };

    private static CodeGenerationResult Generate(
        ErDiagram diagram,
        bool efCore = false,
        bool editModels = false
    ) =>
        new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = !efCore,
                GenerateEfCoreRepositories = efCore,
                GenerateEditModels = editModels,
                GenerateMappers = editModels,
            }
        );

    private static string SingleFile(CodeGenerationResult result) =>
        result.Files.Single(f => f.FileName.EndsWith(".g.cs", StringComparison.Ordinal)).Content;

    /// <summary>計算列にだけマーカー属性が付き、書き込み集合から外れることを検証する</summary>
    [Fact(DisplayName = "計算列: [ComputedColumn] 付与・書き込み集合から除外（SELECT は残す）")]
    public void Generate_ComputedColumn_MarksAttributeAndExcludesFromWrite()
    {
        var result = Generate(Diagram(computed: true));

        result.HasErrors.Should().BeFalse();
        var content = SingleFile(result);

        // マーカー属性クラスの定義が出る
        content.Should().Contain("public sealed class ComputedColumnAttribute : Attribute");
        // 計算列のプロパティにだけマーカーが付く
        content.Should().MatchRegex(@"\[ComputedColumn\]\s*\r?\n\s*public decimal\? Total");
        Regex
            .Matches(content, @"\[ComputedColumn\]")
            .Count.Should()
            .Be(1, "付与対象は計算列 total の 1 列だけ");
        // EntitySaveMetadata が計算列を検出し、INSERT / UPDATE の書き込み集合から除外する
        content
            .Should()
            .Contain("GetCustomAttribute<ComputedColumnAttribute>()")
            .And.Contain("readOnlyColumns");
        // SELECT 系: Total プロパティは通常どおり生成され読める（除外は書き込みのみ）
        content.Should().Contain("public decimal? Total");
    }

    /// <summary>計算列が無い図ではマーカーが 1 つも付かないことを検証する（属性定義自体は常時出る）</summary>
    [Fact(DisplayName = "計算列なし: [ComputedColumn] は 1 つも付かない")]
    public void Generate_NoComputedColumn_DoesNotMarkAnyProperty()
    {
        var content = SingleFile(Generate(Diagram(computed: false)));

        // 固定 infra が参照するため属性型の定義は常に出る
        content.Should().Contain("public sealed class ComputedColumnAttribute : Attribute");
        // 付与はゼロ
        Regex.Matches(content, @"\[ComputedColumn\]").Count.Should().Be(0);
    }

    /// <summary>計算列を除外したことが Info 診断で名指しされることを検証する</summary>
    [Fact(DisplayName = "計算列: 除外した列を Info 診断が名指しする")]
    public void Generate_ComputedColumn_ReportsInfoDiagnostic()
    {
        var result = Generate(Diagram(computed: true));

        result
            .Diagnostics.Should()
            .ContainSingle(d =>
                d.Severity == GenerationDiagnosticSeverity.Info && d.Message.Contains("items.total")
            );
    }

    /// <summary>計算列が無い図では除外の Info 診断が出ないことを検証する</summary>
    [Fact(DisplayName = "計算列なし: 除外の Info 診断は出ない")]
    public void Generate_NoComputedColumn_DoesNotReportInfoDiagnostic()
    {
        var result = Generate(Diagram(computed: false));

        result
            .Diagnostics.Should()
            .NotContain(d => d.Message.Contains("items.total"), "計算列が無ければ何も通知しない");
    }

    /// <summary>主キー列が計算列の図は生成時エラーになることを検証する</summary>
    [Fact(DisplayName = "主キーが計算列の図は生成時エラーになる")]
    public void Generate_ComputedPrimaryKey_FailsWithError()
    {
        var result = Generate(Diagram(computed: true, computedIsPrimaryKey: true));

        result.HasErrors.Should().BeTrue();
        result
            .Diagnostics.Should()
            .Contain(d =>
                d.Severity == GenerationDiagnosticSeverity.Error
                && d.Message.Contains("items")
                && d.Message.Contains("total")
            );
        result.Files.Should().BeEmpty("エラー検出時はファイルを 1 つも出さない");
    }

    /// <summary>主キーでない計算列は生成時エラーにならないことを検証する（両アームの対照）</summary>
    [Fact(DisplayName = "主キーでない計算列は生成時エラーにならない")]
    public void Generate_NonPrimaryKeyComputedColumn_Succeeds()
    {
        Generate(Diagram(computed: true)).HasErrors.Should().BeFalse();
    }

    /// <summary>EF Core の Fluent 構成が計算列を DB 生成値として宣言することを検証する</summary>
    [Fact(DisplayName = "EF Core: 計算列へ ValueGeneratedOnAddOrUpdate が出る")]
    public void Generate_EfCore_ComputedColumn_ConfiguresValueGenerated()
    {
        var content = SingleFile(Generate(Diagram(computed: true), efCore: true));

        content
            .Should()
            .Contain(
                "entity.Property(e => e.Total).HasColumnName(\"total\").HasPrecision(21, 2).ValueGeneratedOnAddOrUpdate();"
            );
    }

    /// <summary>計算列が無ければ ValueGeneratedOnAddOrUpdate は出ないことを検証する（両アームの対照）</summary>
    [Fact(DisplayName = "EF Core: 計算列が無ければ ValueGeneratedOnAddOrUpdate は出ない")]
    public void Generate_EfCore_NoComputedColumn_DoesNotConfigureValueGenerated()
    {
        SingleFile(Generate(Diagram(computed: false), efCore: true))
            .Should()
            .NotContain("ValueGeneratedOnAddOrUpdate");
    }

    /// <summary>
    /// EditModel / Mapper が計算列を rowversion と同じ規則で扱うことを検証する
    /// （入力必須にしない・入力があるときだけ実体へ代入する）。
    /// </summary>
    [Fact(DisplayName = "計算列: EditModel は必須にせず Mapper は入力があるときだけ代入する")]
    public void Generate_ComputedColumn_EditModelIsNotRequiredAndMapperSkipsAbsentInput()
    {
        var content = SingleFile(Generate(Diagram(computed: true), editModels: true));

        // 列テーブルの必須フラグ（表示名・Binding 名に続く 2 つの bool のうち先頭）が false
        content
            .Should()
            .MatchRegex(@"nameof\(Total\),\s*\r?\n\s*nameof\(BindingTotal\),\s*\r?\n\s*false,");
        // Mapper は未入力なら実体の現在値を保つ
        content
            .Should()
            .Contain("if (editModel.Total is not null)")
            .And.Contain("entity.Total = editModel.Total;");
    }

    /// <summary>
    /// 計算列が無ければ NOT NULL 列は従来どおり必須のままであることを検証する（両アームの対照）。
    /// </summary>
    [Fact(DisplayName = "計算列でない NOT NULL 列は EditModel の必須のまま")]
    public void Generate_NonComputedNotNullColumn_StaysRequired()
    {
        var content = SingleFile(Generate(Diagram(computed: false), editModels: true));

        content
            .Should()
            .MatchRegex(@"nameof\(Qty\),\s*\r?\n\s*nameof\(BindingQty\),\s*\r?\n\s*true,");
    }

    /// <summary>NOT NULL の計算列（値型）1 本を持つ最小ダイアグラム（テンポラルテーブルの期間列の形）</summary>
    private static ErDiagram NotNullComputedDiagram() =>
        new()
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "temporal_items",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "period_start",
                            DataType = "datetime2",
                            IsNullable = false,
                            IsComputed = true,
                        },
                    ],
                },
            ],
        };

    /// <summary>
    /// Mapper が NOT NULL の値型の計算列を <c>.Value</c> で取り出すことを検証する。
    /// </summary>
    /// <remarks>
    /// EditModel の確定値は値型なら常に <c>Nullable&lt;T&gt;</c> なので、素の代入は CS0266 になる
    /// （行バージョン列・除外バイナリ列はいずれも <c>byte[]</c>＝参照型でこの経路を踏まない）。
    /// 実コンパイル水準の網は <c>GeneratedCodeCompilationTests</c> の共通図が持つ。
    /// </remarks>
    [Fact(DisplayName = "計算列: NOT NULL の値型は Mapper が .Value で取り出す")]
    public void Generate_NotNullValueTypeComputedColumn_MapperUnwrapsNullable()
    {
        var content = SingleFile(
            new CSharpCodeGenerationService().Generate(
                NotNullComputedDiagram(),
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    GenerateRepositories = true,
                    GenerateEditModels = true,
                    GenerateMappers = true,
                }
            )
        );

        content
            .Should()
            .Contain("if (editModel.PeriodStart is not null)")
            .And.Contain("entity.PeriodStart = editModel.PeriodStart.Value;");
    }

    /// <summary>警告の検証用に、方言・同期支援だけを差し替えて生成する</summary>
    private static CodeGenerationResult GenerateFor(
        ErDiagram diagram,
        string[] dialects,
        bool syncSupport = false
    ) =>
        new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
                RepositoryDialects = dialects,
                GenerateSyncSupport = syncSupport,
            }
        );

    /// <summary>警告本文の識別に使う語（文面の正本は resx）</summary>
    private const string NotNullComputedWarningMarker = "temporal_items.period_start";

    private static bool HasNotNullComputedWarning(CodeGenerationResult result) =>
        result.Diagnostics.Any(d =>
            d.Severity == GenerationDiagnosticSeverity.Warning
            && d.Message.Contains(NotNullComputedWarningMarker, StringComparison.Ordinal)
        );

    /// <summary>
    /// マルチターゲット構成では NOT NULL の計算列が Warning で名指しされることを検証する。
    /// </summary>
    /// <remarks>
    /// ミラー側（式を持たない方言の DB）には普通の列として作られるため、送られない列の NOT NULL 制約で
    /// 行の追加が必ず落ちる。Error でなく Warning なのは、ミラー側を既定値や同じ式で整えてある構成が
    /// 正当に存在し、図からはそれを判定できないため。
    /// </remarks>
    [Fact(DisplayName = "マルチターゲット: NOT NULL の計算列を Warning が名指しする")]
    public void Generate_MultiTarget_NotNullComputedColumn_Warns()
    {
        var result = GenerateFor(NotNullComputedDiagram(), ["sqlserver", "sqlite"]);

        result.HasErrors.Should().BeFalse("警告であって生成は止めない");
        HasNotNullComputedWarning(result).Should().BeTrue();
    }

    /// <summary>同期支援を有効にした構成でも同じ Warning が出ることを検証する</summary>
    [Fact(DisplayName = "同期支援: NOT NULL の計算列を Warning が名指しする")]
    public void Generate_SyncSupport_NotNullComputedColumn_Warns()
    {
        var result = GenerateFor(
            NotNullComputedDiagram(),
            ["sqlserver", "sqlite"],
            syncSupport: true
        );

        result.HasErrors.Should().BeFalse("警告であって生成は止めない");
        HasNotNullComputedWarning(result).Should().BeTrue();
    }

    /// <summary>単一方言・同期なしの構成では Warning が出ないことを検証する</summary>
    /// <remarks>式を持つ DB しか相手にしないため、予告すべき乖離が実在しない。</remarks>
    [Fact(DisplayName = "単一方言: NOT NULL の計算列でも Warning は出ない")]
    public void Generate_SingleDialect_NotNullComputedColumn_DoesNotWarn()
    {
        HasNotNullComputedWarning(GenerateFor(NotNullComputedDiagram(), ["sqlserver"]))
            .Should()
            .BeFalse();
    }

    /// <summary>NULL 許容の計算列だけならマルチターゲットでも Warning が出ないことを検証する</summary>
    /// <remarks>ミラー側は NULL のまま書けるため＝これが利用者へ案内する逃げ道そのもの。</remarks>
    [Fact(DisplayName = "マルチターゲット: NULL 許容の計算列だけなら Warning は出ない")]
    public void Generate_MultiTarget_NullableComputedColumn_DoesNotWarn()
    {
        GenerateFor(Diagram(computed: true), ["sqlserver", "sqlite"])
            .Diagnostics.Should()
            .NotContain(
                d =>
                    d.Severity == GenerationDiagnosticSeverity.Warning
                    && d.Message.Contains("items.total", StringComparison.Ordinal),
                "NULL 許容の計算列はミラー側へ NULL のまま書けるため予告しない"
            );
    }
}
