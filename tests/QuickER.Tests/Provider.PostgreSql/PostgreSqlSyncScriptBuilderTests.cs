using System;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.PostgreSql;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Provider.PostgreSql;

/// <summary>
/// <see cref="PostgreSqlSyncScriptBuilder"/> が差分から生成する PostgreSQL DDL のうち、実 DB で流しても観測できない文字列の形を検証するテストクラス
/// </summary>
/// <remarks>
/// 実 DB で流して結果を観測できる文の形は、同期計画の期待の行（<c>SyncPlanExpectationRows.RealPostgreSql.cs</c> の
/// <c>realdb-pg-script-*</c> など）が実 PostgreSQL で流して見る。
/// </remarks>
public class PostgreSqlSyncScriptBuilderTests
{
    private static string Build(params SchemaDiffItem[] items) =>
        new PostgreSqlSyncScriptBuilder().Build(
            new SyncPlanner().BuildPlan(items, TestDialectCapabilities.Neutral)
        );

    /// <summary>
    /// 同じ系統の型の変更・解析できない旧型・旧型が分からない項目には <c>USING 列::新型</c> を付けないことを検証する
    /// （付けても付けなくても実行の結果が変わらないか、差分からは作れない入力）。
    /// </summary>
    [Theory(DisplayName = "AlterColumn は型の系統をまたぐ変更にだけ USING を付ける")]
    [InlineData("integer", "bigint")]
    [InlineData("timestamp(6)", "date")]
    [InlineData("geometry(Point,4326)", "integer")]
    [InlineData(null, "integer")]
    public void AlterColumn_AddsUsingOnlyAcrossTypeFamilies(string? oldType, string newType)
    {
        var sql = Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.AlterColumn,
                TableName = "customer",
                ColumnName = "code",
                Column = new Column
                {
                    Name = "code",
                    DataType = newType,
                    IsNullable = true,
                },
                OldColumn = oldType is null
                    ? null
                    : new Column
                    {
                        Name = "code",
                        DataType = oldType,
                        IsNullable = true,
                    },
                IsSelected = true,
            }
        );

        sql.Should().Contain($"ALTER TABLE \"customer\" ALTER COLUMN \"code\" TYPE {newType};");
    }

    /// <summary>
    /// 文字列型への列の変更の照合順序の読み直しが、ドット付きの表名をスキーマごと <c>to_regclass</c> へ渡して
    /// ALTER TABLE と同じ表に絞ること、文字列型でない型への変更は照合順序を読まないことを検証する
    /// </summary>
    [Fact(DisplayName = "AlterColumn は文字列型への変更で、型の既定と違う照合順序だけを書き添える")]
    public void AlterColumn_StringType_ReadsBackNonDefaultCollation()
    {
        SchemaDiffItem Alter(string type) =>
            new()
            {
                Kind = SchemaDiffKind.AlterColumn,
                TableName = "sales.orders",
                ColumnName = "code",
                Column = new Column
                {
                    Name = "code",
                    DataType = type,
                    IsNullable = true,
                },
                IsSelected = true,
            };

        Build(Alter("text")).Should().Contain("a.attrelid = to_regclass('\"sales\".\"orders\"')");
        Build(Alter("integer")).Should().NotContain("attcollation");
    }

    /// <summary>制約名不明時に DO ブロックでカタログ逆引き削除を生成することを検証する</summary>
    [Fact(DisplayName = "DropForeignKey は制約名不明時に DO ブロックで逆引き削除する")]
    public void DropForeignKey_UsesDoBlockWhenNameUnknown()
    {
        var customer = new Entity { TableName = "customer" };
        var order = new Entity { TableName = "order" };

        var sql = Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.DropForeignKey,
                TableName = "order",
                ParentEntity = customer,
                ChildEntity = order,
                IsSelected = true,
            }
        );

        sql.Should().Contain("DO $$");
        sql.Should().Contain("pg_constraint");
        sql.Should().Contain("con.conrelid = to_regclass('\"order\"')");
        sql.Should().Contain("con.confrelid = to_regclass('\"customer\"');");
    }

    /// <summary>
    /// 逆引きは ALTER TABLE と同じ 1 つの表に絞る＝ドット付きの名前はスキーマごとクォートして <c>to_regclass</c> へ渡し、
    /// 名前の <c>'</c> は文字列リテラルとして二重化することを検証する（public に固定しない）。
    /// </summary>
    [Fact(
        DisplayName = "逆引き: ドット付きの表名はスキーマごと to_regclass へ渡す（public に固定しない）"
    )]
    public void RuntimeLookup_DottedTableName_UsesRegclassOfQualifiedName()
    {
        var primaryKey = Build(AlterPk("sales.orders", PkTarget("sales.orders", "code")));
        var foreignKey = Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.DropForeignKey,
                TableName = "sales.lines",
                ParentEntity = new Entity { TableName = "sales.o'rders" },
                ChildEntity = new Entity { TableName = "sales.lines" },
                IsSelected = true,
            }
        );

        primaryKey.Should().Contain("con.conrelid = to_regclass('\"sales\".\"orders\"');");
        foreignKey.Should().Contain("con.conrelid = to_regclass('\"sales\".\"lines\"')");
        foreignKey.Should().Contain("con.confrelid = to_regclass('\"sales\".\"o''rders\"');");
        (primaryKey + foreignKey).Should().NotContain("regnamespace");
    }

    /// <summary>RebuildTable は情報表示専用で SQL を生成しないことを検証する</summary>
    [Fact(DisplayName = "RebuildTable は SQL を生成しない")]
    public void RebuildTable_GeneratesNothing()
    {
        var sql = Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.RebuildTable,
                TableName = "customer",
                IsSelected = true,
            }
        );

        sql.Should().NotContain("customer");
    }

    // ---------------- 主キー変更（AlterPrimaryKey） ----------------

    /// <summary>指定の主キー列を持つ target エンティティを組み立てる</summary>
    private static Entity PkTarget(string table, params string[] pkColumns)
    {
        var e = new Entity { TableName = table };
        e.Columns.Add(
            new Column
            {
                Name = "memo",
                DataType = "text",
                IsNullable = true,
            }
        );

        foreach (var name in pkColumns)
        {
            e.Columns.Add(
                new Column
                {
                    Name = name,
                    DataType = "integer",
                    IsPrimaryKey = true,
                    IsNullable = false,
                }
            );
        }

        return e;
    }

    /// <summary>主キー変更の差分項目を生成する（Entity＝新しい主キー構成の源）</summary>
    private static SchemaDiffItem AlterPk(string table, Entity target) =>
        new()
        {
            Kind = SchemaDiffKind.AlterPrimaryKey,
            TableName = table,
            Entity = target,
            IsSelected = true,
        };

    // ---------------- 動的 SQL のリテラルエスケープ ----------------

    /// <summary>制約名不明の外部キー削除でも、DO ブロックのテーブル名がリテラル用にエスケープされることを検証する</summary>
    [Fact(DisplayName = "DropForeignKey の DO ブロックはテーブル名の ' を二重化する")]
    public void DropForeignKey_TableNameWithQuote_IsEscapedInDynamicSql()
    {
        var item = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.DropForeignKey,
            TableName = "o'rders",
            ParentEntity = new Entity { TableName = "customer" },
            ChildEntity = new Entity { TableName = "o'rders" },
            IsSelected = true,
        };

        var sql = Build(item);

        sql.Should()
            .Contain(
                "EXECUTE 'ALTER TABLE \"o''rders\" DROP CONSTRAINT ' || quote_ident(fk_name);"
            );
        sql.Should().NotContain("EXECUTE 'ALTER TABLE \"o'rders\"");
    }

    // ---------------- 実行時にカタログから読んだ制約名の埋め込み ----------------

    /// <summary>
    /// 名前不明の外部キー削除で実行時にカタログから読んだ制約名（<c>fk_name</c>）が、動的 SQL へ必ず <c>quote_ident</c> を通して
    /// 埋められ、生の連結が残らないことを検証する。
    /// </summary>
    /// <remarks>
    /// 制約名は図の外（DB）から来る値で、<c>"</c> を含み得る（QuickER 自身も表 <c>a"b</c> に主キー名 <c>PK_a"b</c> を付ける）。
    /// <c>'"' || fk_name || '"'</c> のように引用符で挟むだけでは、名前の <c>"</c> が識別子の引用を閉じる。
    /// </remarks>
    [Fact(DisplayName = "実行時に読んだ制約名は quote_ident を通して動的 SQL へ埋める")]
    public void RuntimeConstraintName_IsAlwaysQuoteIdented()
    {
        var dropForeignKey = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.DropForeignKey,
            TableName = "lines",
            ParentEntity = new Entity { TableName = "orders" },
            ChildEntity = new Entity { TableName = "lines" },
            IsSelected = true,
        };

        var sql = Build(AlterPk("orders", PkTarget("orders", "id")), dropForeignKey);

        sql.Should().Contain("quote_ident(fk_name)");
        // 変数を quote_ident を通さずに文字列へ連結している箇所が無いこと
        sql.Should().NotContain("|| fk_name");
    }

    // ---------------- DO ブロックのドルクォートタグ ----------------

    /// <summary>制約名不明の外部キー削除でも、DO ブロックが <c>$$</c> 入りテーブル名で終端しないことを検証する</summary>
    /// <remarks>
    /// ドルクォートは<b>タグ文字列そのもの</b>で終端する。タグを <c>$$</c> 固定にすると、本文へ埋めた名前の
    /// <c>$$</c> がブロックを閉じ、以降が最上位の SQL 文として解釈される（リテラルエスケープでは防げない＝
    /// <c>'</c> の二重化はドルクォートの終端と無関係）。タグは本文と衝突しないものを決定的に選ぶべき。
    /// </remarks>
    [Fact(DisplayName = "DropForeignKey の DO ブロックは $$ 入りテーブル名でも終端されない")]
    public void DropForeignKey_TableNameWithDollarTag_DoesNotTerminateBlock()
    {
        var sql = Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.DropForeignKey,
                TableName = "o$$rders",
                ParentEntity = new Entity { TableName = "customer" },
                ChildEntity = new Entity { TableName = "o$$rders" },
                IsSelected = true,
            }
        );

        AssertDollarQuotedBlockIsIntact(sql);
    }

    /// <summary>衝突しないタグは既定の <c>$$</c> のままであること（通常の図の出力を変えない）</summary>
    [Fact(DisplayName = "DO ブロックのタグは衝突が無ければ $$ のまま")]
    public void DoBlock_WithoutCollision_KeepsDefaultTag()
    {
        var sql = Build(AlterPk("orders", PkTarget("orders")));

        sql.Should().Contain("DO $$");
        sql.Should().Contain("END $$;");
    }

    /// <summary>
    /// 生成された <c>DO</c> ブロックの本文にドルクォートタグが現れない（＝ブロックが途中で閉じない）ことを検証する。
    /// </summary>
    private static void AssertDollarQuotedBlockIsIntact(string sql)
    {
        var lines = sql.Replace("\r\n", "\n").Split('\n');
        var openIndex = Array.FindIndex(
            lines,
            line => line.StartsWith("DO $", StringComparison.Ordinal)
        );

        openIndex.Should().BeGreaterThanOrEqualTo(0, "DO ブロックが生成されること");

        var tag = lines[openIndex]["DO ".Length..].Trim();
        tag.Should().MatchRegex(@"^\$[A-Za-z0-9_]*\$$", "ドルクォートタグとして妥当な形であること");

        var closeIndex = Array.FindIndex(
            lines,
            openIndex + 1,
            line => line.Trim() == $"END {tag};"
        );

        closeIndex.Should().BeGreaterThan(openIndex, "同じタグで閉じられていること");

        string.Join("\n", lines[(openIndex + 1)..closeIndex])
            .Should()
            .NotContain(
                tag,
                "本文にタグが現れるとブロックがそこで終端し、以降が実行される SQL になる"
            );
    }
}
