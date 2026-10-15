using System;
using System.Linq;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.CodeGen.CSharp.Resources;
using QuickER.Dialects;
using QuickER.Model;
using Xunit;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// DB が採番する列（<see cref="Column.IsIdentity"/>）の生成結果を検証するテストクラス。
/// </summary>
/// <remarks>
/// 固定するのは、主キーでない採番列へのマーカー属性 <c>[IdentityColumn]</c> の付与と
/// <c>EntitySaveMetadata</c> の書き込み除外、EF Core Fluent の <c>ValueGeneratedOnAddOrUpdate()</c>、
/// EditModel / Mapper の扱い、そして生成時診断（除外した列の Info・主キーの採番列の Warning）。
/// 主キーの採番列は生成 Repository がキーを書くため印を付けず、生成は止めずに警告する。
/// いずれも「採番列がある図／無い図」の両アームを名指しする。
/// </remarks>
public class IdentityColumnGenerationTests
{
    /// <summary>採番列を持ち得る最小ダイアグラム</summary>
    /// <param name="identity">true なら NOT NULL の <c>seq</c> 列（値型）を採番列にする</param>
    /// <param name="identityPrimaryKey">true なら主キー <c>item_id</c> を採番列にする</param>
    private static ErDiagram Diagram(bool identity, bool identityPrimaryKey = false) =>
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
                            IsPrimaryKey = true,
                            IsNullable = false,
                            IsIdentity = identityPrimaryKey,
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
                            Name = "seq",
                            DataType = "bigint",
                            IsNullable = false,
                            IsIdentity = identity,
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

    private static bool HasPrimaryKeyWarning(CodeGenerationResult result) =>
        result.Diagnostics.Any(d =>
            d.Severity == GenerationDiagnosticSeverity.Warning
            && d.Message
                == string.Format(
                    Strings.CodeGen_Warning_IdentityPrimaryKeyColumn,
                    "items",
                    "item_id"
                )
        );

    /// <summary>主キーでない採番列にだけマーカー属性が付き、書き込み集合から外れることを検証する</summary>
    [Fact(DisplayName = "採番列: [IdentityColumn] 付与・書き込み集合から除外（SELECT は残す）")]
    public void Generate_IdentityColumn_MarksAttributeAndExcludesFromWrite()
    {
        var result = Generate(Diagram(identity: true));

        result.HasErrors.Should().BeFalse();
        var content = SingleFile(result);

        // マーカー属性クラスの定義が出る
        content.Should().Contain("public sealed class IdentityColumnAttribute : Attribute");
        // 採番列のプロパティにだけマーカーが付く
        content.Should().MatchRegex(@"\[IdentityColumn\]\s*\r?\n\s*public long Seq");
        Regex
            .Matches(content, @"\[IdentityColumn\]")
            .Count.Should()
            .Be(1, "付与対象は採番列 seq の 1 列だけ");
        // EntitySaveMetadata が採番列を検出し、INSERT / UPDATE の書き込み集合から除外する
        content
            .Should()
            .Contain("GetCustomAttribute<IdentityColumnAttribute>()")
            .And.Contain("readOnlyColumns");
    }

    /// <summary>採番列が無い図ではマーカーが 1 つも付かないことを検証する（属性定義自体は常時出る）</summary>
    [Fact(DisplayName = "採番列なし: [IdentityColumn] は 1 つも付かない")]
    public void Generate_NoIdentityColumn_DoesNotMarkAnyProperty()
    {
        var content = SingleFile(Generate(Diagram(identity: false)));

        // 固定 infra が参照するため属性型の定義は常に出る
        content.Should().Contain("public sealed class IdentityColumnAttribute : Attribute");
        Regex.Matches(content, @"\[IdentityColumn\]").Count.Should().Be(0);
    }

    /// <summary>採番列を除外したことが Info 診断で名指しされることを検証する</summary>
    [Fact(DisplayName = "採番列: 除外した列を Info 診断が名指しする")]
    public void Generate_IdentityColumn_ReportsInfoDiagnostic()
    {
        var result = Generate(Diagram(identity: true));

        result
            .Diagnostics.Should()
            .ContainSingle(d =>
                d.Severity == GenerationDiagnosticSeverity.Info
                && d.Message.StartsWith(
                    Strings.CodeGen_Info_IdentityColumns.Replace("{0}", string.Empty),
                    StringComparison.Ordinal
                )
                && d.Message.Contains("items.seq", StringComparison.Ordinal)
            );
    }

    /// <summary>採番列が無い図では除外の Info 診断が出ないことを検証する</summary>
    [Fact(DisplayName = "採番列なし: 除外の Info 診断は出ない")]
    public void Generate_NoIdentityColumn_DoesNotReportInfoDiagnostic()
    {
        Generate(Diagram(identity: false))
            .Diagnostics.Should()
            .NotContain(d => d.Message.Contains("items.seq"), "採番列が無ければ何も通知しない");
    }

    /// <summary>
    /// 主キーの採番列は印を付けずに書き込み対象のまま残し、生成を止めずに警告することを検証する
    /// </summary>
    /// <remarks>
    /// 生成 Repository はキーをアプリ側で決めて書くため、主キーを書き込みから外すと INSERT がキーを送らなくなる。
    /// 指定した値を拒む DB では挿入が失敗するので、生成時に名指しする（計算列の主キーと違い Error にしない）。
    /// </remarks>
    [Fact(DisplayName = "主キーの採番列は印を付けず、生成を止めずに Warning で名指しする")]
    public void Generate_IdentityPrimaryKey_WarnsWithoutMarking()
    {
        var result = Generate(Diagram(identity: false, identityPrimaryKey: true));

        result.HasErrors.Should().BeFalse("警告であって生成は止めない");
        HasPrimaryKeyWarning(result).Should().BeTrue();
        Regex.Matches(SingleFile(result), @"\[IdentityColumn\]").Count.Should().Be(0);
        result
            .Diagnostics.Should()
            .NotContain(
                d => d.Message.Contains("items.item_id", StringComparison.Ordinal),
                "主キーは除外しないので Info には載らない"
            );
    }

    /// <summary>主キーが採番列でなければ Warning は出ないことを検証する（両アームの対照）</summary>
    [Fact(DisplayName = "主キーが採番列でなければ Warning は出ない")]
    public void Generate_NonIdentityPrimaryKey_DoesNotWarn()
    {
        HasPrimaryKeyWarning(Generate(Diagram(identity: true))).Should().BeFalse();
    }

    /// <summary>
    /// Repository 契約を生成しない構成では、主キーの採番列の Warning を出さないことを検証する
    /// </summary>
    /// <remarks>キーを書き込む生成コードが無いので、警告の前提（キーをアプリが決めて書く）が効かない。</remarks>
    [Fact(DisplayName = "Repository 契約を生成しない構成では、主キーの採番列の Warning は出ない")]
    public void Generate_IdentityPrimaryKey_WithoutRepositoryContract_DoesNotWarn()
    {
        var result = new CSharpCodeGenerationService().Generate(
            Diagram(identity: false, identityPrimaryKey: true),
            new CodeGenerationOptions { RootNamespace = "Sample.Domain" }
        );

        result.HasErrors.Should().BeFalse();
        HasPrimaryKeyWarning(result).Should().BeFalse();
    }

    /// <summary>マルチターゲットの生成で NOT NULL の採番列を名指しする警告</summary>
    private static bool HasMirrorWarning(CodeGenerationResult result) =>
        result.Diagnostics.Any(d =>
            d.Severity == GenerationDiagnosticSeverity.Warning
            && d.Message.StartsWith(
                Strings.CodeGen_Warning_NotNullIdentityColumns.Split("{0}")[0],
                StringComparison.Ordinal
            )
            && d.Message.Contains("(items.seq)", StringComparison.Ordinal)
        );

    private static CodeGenerationResult GenerateRepositories(
        ErDiagram diagram,
        params string[] dialects
    ) =>
        new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
                RepositoryDialects = dialects,
            }
        );

    /// <summary>
    /// 採番列を持てない方言（SQLite）を含むマルチターゲットで、NOT NULL の採番列を Warning で名指しすることを検証する
    /// </summary>
    /// <remarks>
    /// 生成コードはその列を INSERT から外し、ミラーの DDL は普通の列を作るので、ミラーへの行の追加が NOT NULL で落ちる。
    /// </remarks>
    [Fact(
        DisplayName = "採番列を持てない方言を含むマルチターゲットでは、NOT NULL の採番列を Warning で名指しする"
    )]
    public void Generate_MultiTargetWithSqlite_NotNullIdentityColumn_Warns()
    {
        var result = GenerateRepositories(
            Diagram(identity: true),
            DialectNames.SqlServer,
            DialectNames.Sqlite
        );

        result.HasErrors.Should().BeFalse();
        HasMirrorWarning(result).Should().BeTrue();
    }

    /// <summary>単一方言・NULL 許容の採番列では、ミラーの警告を出さないことを検証する（対照）</summary>
    [Fact(DisplayName = "単一方言・NULL 許容の採番列では、ミラーの Warning は出ない")]
    public void Generate_MirrorWarning_IsNotEmittedWithoutMirrorOrForNullableColumn()
    {
        HasMirrorWarning(GenerateRepositories(Diagram(identity: true), DialectNames.SqlServer))
            .Should()
            .BeFalse("ミラーが無い");

        var nullable = Diagram(identity: true);
        nullable.Entities[0].Columns.Single(c => c.Name == "seq").IsNullable = true;

        HasMirrorWarning(
                GenerateRepositories(nullable, DialectNames.SqlServer, DialectNames.Sqlite)
            )
            .Should()
            .BeFalse("NULL 許容の列はミラーへ NULL のまま書ける");
    }

    /// <summary>EF Core の Fluent 構成が主キーでない採番列を DB 生成値として宣言することを検証する</summary>
    [Fact(DisplayName = "EF Core: 採番列へ ValueGeneratedOnAddOrUpdate が出る（主キーには出ない）")]
    public void Generate_EfCore_IdentityColumn_ConfiguresValueGenerated()
    {
        var content = SingleFile(
            Generate(Diagram(identity: true, identityPrimaryKey: true), efCore: true)
        );

        content
            .Should()
            .Contain(
                "entity.Property(e => e.Seq).HasColumnName(\"seq\").IsRequired().ValueGeneratedOnAddOrUpdate();"
            );
        Regex.Matches(content, @"ValueGeneratedOnAddOrUpdate").Count.Should().Be(1);
    }

    /// <summary>採番列が無ければ ValueGeneratedOnAddOrUpdate は出ないことを検証する（両アームの対照）</summary>
    [Fact(DisplayName = "EF Core: 採番列が無ければ ValueGeneratedOnAddOrUpdate は出ない")]
    public void Generate_EfCore_NoIdentityColumn_DoesNotConfigureValueGenerated()
    {
        SingleFile(Generate(Diagram(identity: false), efCore: true))
            .Should()
            .NotContain("ValueGeneratedOnAddOrUpdate");
    }

    /// <summary>
    /// EditModel / Mapper が主キーでない採番列を計算列と同じ規則で扱うことを検証する
    /// （入力必須にしない・入力があるときだけ実体へ代入する・値型は <c>.Value</c> で取り出す）。
    /// </summary>
    [Fact(
        DisplayName = "採番列: EditModel は必須にせず Mapper は入力があるときだけ .Value で代入する"
    )]
    public void Generate_IdentityColumn_EditModelIsNotRequiredAndMapperSkipsAbsentInput()
    {
        var content = SingleFile(Generate(Diagram(identity: true), editModels: true));

        // 列テーブルの必須フラグ（表示名・Binding 名に続く 2 つの bool のうち先頭）が false
        content
            .Should()
            .MatchRegex(@"nameof\(Seq\),\s*\r?\n\s*nameof\(BindingSeq\),\s*\r?\n\s*false,");
        // Mapper は未入力なら実体の現在値を保つ（EditModel の確定値は Nullable<long>）
        content
            .Should()
            .Contain("if (editModel.Seq is not null)")
            .And.Contain("entity.Seq = editModel.Seq.Value;");
    }

    /// <summary>採番列でない NOT NULL 列は EditModel の必須のままであることを検証する（両アームの対照）</summary>
    [Fact(DisplayName = "採番列でない NOT NULL 列は EditModel の必須のまま")]
    public void Generate_NonIdentityNotNullColumn_StaysRequired()
    {
        var content = SingleFile(Generate(Diagram(identity: false), editModels: true));

        content
            .Should()
            .MatchRegex(@"nameof\(Seq\),\s*\r?\n\s*nameof\(BindingSeq\),\s*\r?\n\s*true,");
    }
}
