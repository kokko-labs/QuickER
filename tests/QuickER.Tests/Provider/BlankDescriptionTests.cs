using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
using QuickER.Tests.Resources;
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.Provider;

/// <summary>
/// 空白だけの説明（テーブル・列）を、DDL・差分・同期スクリプトが同じく「説明なし」として扱うことを検証する
/// </summary>
/// <remarks>
/// 判定は <see cref="DescriptionText"/> の 1 つだけ（写しを置かない＝<see cref="DescriptionPredicate_IsOnlyInDescriptionText"/>
/// が src を走査して固定する）。実 DB の往復は <c>BlankDescriptionIntegrationTests</c>。
/// </remarks>
public class BlankDescriptionTests
{
    private const string Blank = " \t ";

    /// <summary>5 方言のプロバイダ</summary>
    public static TheoryData<string> Dialects() =>
        new() { "sqlserver", "postgresql", "mysql", "oracle", "sqlite" };

    /// <summary>説明を持つ 4 方言のプロバイダ</summary>
    public static TheoryData<string> DescriptionDialects() =>
        new() { "sqlserver", "postgresql", "mysql", "oracle" };

    /// <summary>空白だけの説明の図の DDL は、説明の無い図の DDL と同じになることを検証する</summary>
    [Theory(DisplayName = "空白だけの説明: DDL は説明の無い図と同じ（説明を書かない）")]
    [MemberData(nameof(Dialects))]
    public void Ddl_BlankDescription_IsSameAsNone(string dialect)
    {
        var provider = ProviderFor(dialect);

        Ddl(provider, Table(Blank, Blank)).Should().Be(Ddl(provider, Table(null, null)));
        Ddl(provider, Table("note", "note"))
            .Should()
            .NotBe(Ddl(provider, Table(null, null)), "説明のある図は説明を書く（対照）");
    }

    /// <summary>
    /// 差分は空白だけの説明を空と同じとみなし、図・DB のどちらが空白だけでも説明の差分を出さないことを検証する。
    /// 説明のある DB を空白だけの図へ合わせる差分は、説明の削除（新しい値は空）になる。
    /// </summary>
    [Theory(DisplayName = "空白だけの説明: 差分は空と同じとみなし、説明のある DB からは削除になる")]
    [MemberData(nameof(DescriptionDialects))]
    public void Diff_BlankDescription_IsSameAsNone(string dialect)
    {
        var capabilities = ProviderFor(dialect).SyncCapabilities;

        DescriptionItems(Diff(Table(null, null), Table(Blank, Blank), capabilities))
            .Should()
            .BeEmpty();
        DescriptionItems(Diff(Table(Blank, Blank), Table(null, null), capabilities))
            .Should()
            .BeEmpty();

        var removal = DescriptionItems(
            Diff(Table("note", "note"), Table(Blank, Blank), capabilities)
        );
        removal
            .Select(i => i.Kind)
            .Should()
            .BeEquivalentTo([
                SchemaDiffKind.SetTableDescription,
                SchemaDiffKind.SetColumnDescription,
            ]);
        removal.Should().OnlyContain(i => i.NewDescription == string.Empty);
    }

    /// <summary>
    /// 新しい表・新しい列の空白だけの説明は、説明の差分項目を作らないことを検証する
    /// </summary>
    [Theory(DisplayName = "空白だけの説明: 新しい表・列でも説明の差分項目を作らない")]
    [MemberData(nameof(DescriptionDialects))]
    public void Diff_BlankDescriptionOnNewTableAndColumn_AddsNoDescriptionItem(string dialect)
    {
        var capabilities = ProviderFor(dialect).SyncCapabilities;
        var withNewColumn = Table(Blank, Blank);
        withNewColumn.Columns.Add(
            new Column
            {
                Name = "w",
                DataType = "int",
                IsNullable = true,
                Description = Blank,
            }
        );

        DescriptionItems(
                new SchemaDiffService().Compute([], [], [Table(Blank, Blank)], [], capabilities)
            )
            .Should()
            .BeEmpty();
        DescriptionItems(Diff(Table(null, null), withNewColumn, capabilities)).Should().BeEmpty();
    }

    /// <summary>
    /// 同期スクリプトは、新しい値が空白だけの説明の項目を、空の値（説明の削除）と同じ文にすることを検証する
    /// </summary>
    [Theory(DisplayName = "空白だけの説明: 同期スクリプトは説明の削除と同じ文を書く")]
    [MemberData(nameof(DescriptionDialects))]
    public void SyncScript_BlankNewDescription_IsRemoval(string dialect)
    {
        var provider = ProviderFor(dialect);

        Script(provider, Blank).Should().Be(Script(provider, string.Empty));
        Script(provider, "note")
            .Should()
            .NotBe(Script(provider, string.Empty), "説明は書く（対照）");
    }

    /// <summary>
    /// MySQL の列の追加は説明（COMMENT）を書かないことを検証する（説明は説明の同期の項目だけが書く）
    /// </summary>
    [Fact(DisplayName = "MySQL: 列の追加は説明を書かない")]
    public void MySqlAddColumn_DoesNotWriteComment()
    {
        var item = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AddColumn,
            TableName = "t",
            ColumnName = "w",
            Column = new Column
            {
                Name = "w",
                DataType = "int",
                IsNullable = true,
                Description = "note",
            },
            IsSelected = true,
        };

        var script = new MySqlSyncScriptBuilder().Build(
            new SyncPlanner().BuildPlan([item], TestDialectCapabilities.Neutral)
        );

        script.Should().Contain("ADD COLUMN `w` int NULL;");
        script.Should().NotContain("COMMENT");
    }

    /// <summary>
    /// 説明の空欄の判定は <see cref="DescriptionText"/> の 1 つだけで、src の Provider 系に写しが無いことを検証する
    /// </summary>
    /// <remarks>
    /// 拾う形は、説明（名前に <c>Description</c> / <c>Desc</c> を含む式）に対する <c>string.IsNullOrEmpty</c> /
    /// <c>string.IsNullOrWhiteSpace</c> と、<c>?? string.Empty</c> での空文字への置き換え。説明の比較・書き出しの前に
    /// 経路ごとの判定を書くと、DDL・差分・同期スクリプトの扱いが食い違う。
    /// </remarks>
    [Fact(DisplayName = "空白だけの説明: 説明の空欄の判定は DescriptionText の 1 つだけ")]
    public void DescriptionPredicate_IsOnlyInDescriptionText()
    {
        var src = Path.Combine(NeutralResxFiles.FindRepositoryRoot(), "src");
        var patterns = new[]
        {
            new Regex(
                @"string\.IsNullOr(?:Empty|WhiteSpace)\([^()]*Desc",
                RegexOptions.CultureInvariant
            ),
            new Regex(@"Desc\w*\s*\?\?\s*string\.Empty", RegexOptions.CultureInvariant),
        };
        var found = Directory
            .GetDirectories(src, "QuickER.Provider*")
            .SelectMany(d => Directory.GetFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(f =>
                !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && Path.GetFileName(f) != "DescriptionText.cs"
            )
            .SelectMany(f =>
                File.ReadAllLines(f)
                    .Select((line, index) => (f, line, index))
                    .Where(x => patterns.Any(p => p.IsMatch(x.line)))
                    .Select(x => $"{Path.GetRelativePath(src, x.f)}:{x.index + 1}: {x.line.Trim()}")
            )
            .ToList();

        found.Should().BeEmpty();
    }

    private static Entity Table(string? tableDescription, string? columnDescription) =>
        new()
        {
            TableName = "t",
            Description = tableDescription!,
            Columns =
            {
                new Column
                {
                    Name = "id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "v",
                    DataType = "int",
                    IsNullable = true,
                    Description = columnDescription!,
                },
            },
        };

    private static string Ddl(IDatabaseProvider provider, Entity table)
    {
        var diagram = new ErDiagram { TargetDbms = provider.Name };
        diagram.Entities.Add(table);
        return string.Join(
            "\n",
            provider
                .DdlGenerator.Build(diagram)
                .Split('\n')
                .Where(line => !line.StartsWith("-- Generated at", StringComparison.Ordinal))
        );
    }

    private static SchemaDiff Diff(
        Entity live,
        Entity target,
        SyncDialectCapabilities capabilities
    ) => new SchemaDiffService().Compute([live], [], [target], [], capabilities);

    private static List<SchemaDiffItem> DescriptionItems(SchemaDiff diff) =>
        [
            .. diff.Items.Where(i =>
                i.Kind is SchemaDiffKind.SetTableDescription or SchemaDiffKind.SetColumnDescription
            ),
        ];

    private static string Script(IDatabaseProvider provider, string newDescription)
    {
        var items = new List<SchemaDiffItem>
        {
            new()
            {
                Kind = SchemaDiffKind.SetTableDescription,
                TableName = "t",
                NewDescription = newDescription,
                OldDescription = "old",
                IsSelected = true,
            },
            new()
            {
                Kind = SchemaDiffKind.SetColumnDescription,
                TableName = "t",
                ColumnName = "v",
                NewDescription = newDescription,
                OldDescription = "old",
                IsSelected = true,
            },
        };
        return provider.SyncScriptBuilder.Build(
            new SyncPlanner().BuildPlan(items, provider.SyncCapabilities)
        );
    }

    private static IDatabaseProvider ProviderFor(string dialect) =>
        dialect switch
        {
            "postgresql" => new PostgreSqlProvider(),
            "mysql" => new MySqlProvider(),
            "oracle" => new OracleProvider(),
            "sqlite" => new SqliteProvider(),
            _ => new SqlServerProvider(),
        };
}
