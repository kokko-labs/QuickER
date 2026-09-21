using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
using Xunit;

namespace QuickER.Tests.Provider;

/// <summary>
/// 図の名前（テーブル名・列名・制約名）に改行・制御文字が含まれるとき、DDL 生成・同期スクリプト生成が
/// <b>1 行も出さずに名指しで失敗する</b>ことを検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// コメント行のサニタイズ（<see cref="SqlComment.Sanitize"/>）と識別子のエスケープ（方言の <c>Escape</c>）は
/// 出力側の防壁だが、埋め込み箇所は DDL・同期の全方言に散っており、1 箇所でも通し忘れるとそこが即座に
/// 武器になる（実際、SQLite の再構築見出しだけがサニタイズ漏れだった）。そこで入口でも同じ検査を行い、
/// 「改行入りの名前はそもそも出力へ到達しない」を構造で担保する（型表記の <see cref="SqlTypeText"/> と同位置）。
/// </para>
/// <para>
/// 止めるのは改行・制御文字だけで、<c>"</c> や <c>]</c> や <c>'</c> は対象にしない（DB 取込で実在し得る名前で、
/// 出力側のエスケープで正しく扱える）。C# コード生成側の同じ線引きは生成前診断の Error として実装されている
/// （<c>NameTrustBoundaryGenerationTests</c>）。
/// </para>
/// </remarks>
public class SqlNameTextTests
{
    /// <summary>改行の後に実行可能な SQL を置いた名前</summary>
    private const string Injected = "a\nDROP TABLE users; --";

    // ---------------- 判定そのもの ----------------

    [Theory(DisplayName = "改行・制御文字を含む名前は安全でない")]
    [InlineData("a\nb")]
    [InlineData("a\rb")]
    [InlineData("a\tb")]
    [InlineData("a\u0000b")]
    [InlineData("a\u007Fb")]
    // C# / 表示系が改行として扱う Unicode の行区切り（C0 の外側なので明示的に含める）
    [InlineData("a\u0085b")]
    [InlineData("a\u2028b")]
    [InlineData("a\u2029b")]
    public void IsSafe_ControlCharacterInName_Rejected(string name) =>
        SqlNameText.IsSafe(name).Should().BeFalse();

    [Theory(DisplayName = "クォート・記号を含むだけの名前は安全（DB 取込で実在し得る）")]
    [InlineData("orders")]
    [InlineData("o'brien")]
    [InlineData("we\"ird")]
    [InlineData("a]b")]
    [InlineData("sales.orders")]
    [InlineData("注文")]
    [InlineData("")]
    [InlineData(null)]
    public void IsSafe_QuotesAndSymbols_Pass(string? name) =>
        SqlNameText.IsSafe(name).Should().BeTrue();

    // ---------------- DDL 生成の関門 ----------------

    [Fact(DisplayName = "5 方言の DDL 生成は改行入りのテーブル名を名指しで拒否する")]
    public void DdlGenerators_RejectControlCharacterInTableName()
    {
        var diagram = BuildDiagram(tableName: Injected, columnName: "memo");

        foreach (var generator in DdlGenerators())
        {
            var act = () => generator.Build(diagram);

            act.Should().Throw<InvalidOperationException>().WithMessage("*TABLE*DROP TABLE users*");
        }
    }

    [Fact(DisplayName = "5 方言の DDL 生成は改行入りの列名を名指しで拒否する")]
    public void DdlGenerators_RejectControlCharacterInColumnName()
    {
        var diagram = BuildDiagram(tableName: "orders", columnName: Injected);

        foreach (var generator in DdlGenerators())
        {
            var act = () => generator.Build(diagram);

            act.Should().Throw<InvalidOperationException>().WithMessage("*COLUMN*orders*");
        }
    }

    [Fact(DisplayName = "DDL 生成は改行入りの一意制約名・外部キー制約名も拒否する")]
    public void DdlGenerators_RejectControlCharacterInConstraintNames()
    {
        var diagram = BuildDiagram(tableName: "orders", columnName: "memo");
        diagram
            .Entities[0]
            .UniqueConstraints.Add(
                new UniqueConstraint
                {
                    Name = Injected,
                    ColumnIds = [diagram.Entities[0].Columns[1].Id],
                }
            );

        var act = () => new SqlServerDdlGenerator().Build(diagram);

        act.Should().Throw<InvalidOperationException>().WithMessage("*UNIQUE*");
    }

    [Fact(DisplayName = "DDL 生成は改行を含まない図をそのまま生成する")]
    public void DdlGenerators_AcceptNamesWithoutControlCharacters()
    {
        var diagram = BuildDiagram(tableName: "o'rders", columnName: "we\"ird");

        foreach (var generator in DdlGenerators())
        {
            generator.Build(diagram).Should().Contain("CREATE TABLE");
        }
    }

    // ---------------- 同期スクリプト生成の関門 ----------------

    [Fact(DisplayName = "5 方言の同期スクリプト生成は改行入りのテーブル名を名指しで拒否する")]
    public void SyncScriptBuilders_RejectControlCharacterInTableName()
    {
        var diagram = BuildDiagram(tableName: Injected, columnName: "memo");
        var plan = new SyncPlanner().BuildPlan(
            [
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AddTable,
                    TableName = Injected,
                    Entity = diagram.Entities[0],
                    IsSelected = true,
                },
            ],
            new SyncDialectCapabilities()
        );

        foreach (var builder in SyncScriptBuilders())
        {
            var act = () => builder.Build(plan);

            act.Should().Throw<InvalidOperationException>().WithMessage("*TABLE*");
        }
    }

    [Fact(DisplayName = "同期スクリプト生成は再構築計画の改行入りテーブル名も拒否する")]
    public void SyncScriptBuilders_RejectControlCharacterInRebuildPlan()
    {
        var plan = new SyncPlan
        {
            Rebuilds =
            [
                new TableRebuildPlan
                {
                    TableName = Injected,
                    NewDefinition = BuildDiagram(Injected, "memo").Entities[0],
                    CreateOnly = false,
                    CopyColumns = ["id"],
                },
            ],
        };

        var act = () => new SqliteSyncScriptBuilder().Build(plan);

        act.Should().Throw<InvalidOperationException>().WithMessage("*TABLE*");
    }

    [Fact(DisplayName = "同期スクリプト生成は差分項目単体の改行入り列名も拒否する")]
    public void SyncScriptBuilders_RejectControlCharacterOnSingleColumn()
    {
        var plan = new SyncPlanner().BuildPlan(
            [
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AlterColumn,
                    TableName = "orders",
                    ColumnName = Injected,
                    Column = new Column
                    {
                        Name = Injected,
                        DataType = "int",
                        IsNullable = true,
                    },
                    IsSelected = true,
                },
            ],
            new SyncDialectCapabilities { SupportsAlterColumn = true }
        );

        var act = () => new SqlServerSyncScriptBuilder().Build(plan);

        act.Should().Throw<InvalidOperationException>().WithMessage("*COLUMN*orders*");
    }

    /// <summary>5 方言の DDL 生成器</summary>
    private static IDdlGenerator[] DdlGenerators() =>
        [
            new SqlServerDdlGenerator(),
            new PostgreSqlDdlGenerator(),
            new MySqlDdlGenerator(),
            new OracleDdlGenerator(),
            new SqliteDdlGenerator(),
        ];

    /// <summary>5 方言の同期スクリプト生成器</summary>
    private static ISyncScriptBuilder[] SyncScriptBuilders() =>
        [
            new SqlServerSyncScriptBuilder(),
            new PostgreSqlSyncScriptBuilder(),
            new MySqlSyncScriptBuilder(),
            new OracleSyncScriptBuilder(),
            new SqliteSyncScriptBuilder(),
        ];

    /// <summary>主キー列と指定名の列を 1 本ずつ持つ最小の図を組み立てる</summary>
    private static ErDiagram BuildDiagram(string tableName, string columnName) =>
        new()
        {
            Entities =
            [
                new Entity
                {
                    TableName = tableName,
                    Columns =
                    [
                        new Column
                        {
                            Name = "id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Name = columnName,
                            DataType = "int",
                            IsNullable = true,
                        },
                    ],
                },
            ],
        };
}
