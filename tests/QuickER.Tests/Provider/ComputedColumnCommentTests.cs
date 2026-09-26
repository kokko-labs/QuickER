using System;
using System.Collections.Generic;
using System.Linq;
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
/// 計算列・生成列（<see cref="Column.IsComputed"/>）の列定義行へ添える注意コメントを、
/// DDL 生成（5 方言）と同期スクリプト生成（AddTable / AddColumn / SQLite の再構築）で固定するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 意味モデルは式を持たないため、取り込んだ計算列から生成する DDL は<b>普通の列</b>になる。
/// 黙って出すとその DDL を当てた DB では計算列が消えるため、列定義行の直前に注意を書き添える。
/// 文面の正本は <see cref="ComputedColumnComment"/>。
/// </para>
/// <para>
/// 両アームを固定する＝計算列を持たない図の出力にはこのコメントが 1 つも出ない
/// （既存の <c>*DdlGeneratorTests</c> / <c>*SyncScriptBuilderTests</c> はいずれも計算列を使わないため、
/// それらが緑のままであることと合わせて「計算列のない図の出力はバイト不変」を担保する）。
/// </para>
/// </remarks>
public class ComputedColumnCommentTests
{
    /// <summary>注意コメントの識別に使う接頭辞（文面の正本は <see cref="ComputedColumnComment"/>）</summary>
    private const string NotePrefix = "-- Computed column 'total'";

    /// <summary>1 方言分の DDL 生成器と同期スクリプト生成器</summary>
    private sealed record Dialect(
        string Name,
        Func<IDdlGenerator> Ddl,
        Func<ISyncScriptBuilder> Sync
    );

    private static readonly Dialect[] Dialects =
    [
        new("SqlServer", () => new SqlServerDdlGenerator(), () => new SqlServerSyncScriptBuilder()),
        new(
            "PostgreSql",
            () => new PostgreSqlDdlGenerator(),
            () => new PostgreSqlSyncScriptBuilder()
        ),
        new("MySql", () => new MySqlDdlGenerator(), () => new MySqlSyncScriptBuilder()),
        new("Oracle", () => new OracleDdlGenerator(), () => new OracleSyncScriptBuilder()),
        new("Sqlite", () => new SqliteDdlGenerator(), () => new SqliteSyncScriptBuilder()),
    ];

    /// <summary>計算列 1 本を持つ（または持たない）1 テーブルの図を組み立てる</summary>
    private static Entity BuildEntity(bool computed) =>
        new()
        {
            TableName = "items",
            Columns =
            {
                new Column
                {
                    Name = "item_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "total",
                    DataType = "decimal(21,2)",
                    IsNullable = true,
                    IsComputed = computed,
                },
            },
        };

    private static ErDiagram BuildDiagram(bool computed) =>
        new() { Entities = { BuildEntity(computed) } };

    private static string BuildScript(ISyncScriptBuilder builder, params SchemaDiffItem[] items) =>
        builder.Build(new SyncPlanner().BuildPlan(items, new SyncDialectCapabilities()));

    /// <summary>5 方言の DDL が計算列の直前に注意コメントを出すことを検証する</summary>
    [Fact(DisplayName = "DDL: 5 方言とも計算列の直前に注意コメントを出す")]
    public void Ddl_ComputedColumn_EmitsNoteBeforeColumnLine()
    {
        foreach (var dialect in Dialects)
        {
            var sql = dialect.Ddl().Build(BuildDiagram(computed: true));
            var lines = sql.Split('\n').Select(line => line.TrimEnd('\r')).ToList();

            var noteIndex = lines.FindIndex(line =>
                line.Contains(NotePrefix, StringComparison.Ordinal)
            );
            noteIndex.Should().BeGreaterThan(-1, $"{dialect.Name} は注意コメントを出す");

            // 直後の行が当の列定義（コメントは列行の直前に置く）
            lines[noteIndex + 1]
                .Should()
                .Contain("total", $"{dialect.Name} はコメントの直後に計算列の定義を出す");

            // 計算列でない列には付かない
            sql.Should().NotContain("-- Computed column 'item_id'", $"{dialect.Name}");
        }
    }

    /// <summary>計算列を持たない図の DDL には注意コメントが 1 つも出ないことを検証する</summary>
    [Fact(DisplayName = "DDL: 計算列が無ければ注意コメントは出ない")]
    public void Ddl_NoComputedColumn_EmitsNoNote()
    {
        foreach (var dialect in Dialects)
        {
            dialect
                .Ddl()
                .Build(BuildDiagram(computed: false))
                .Should()
                .NotContain("-- Computed column", $"{dialect.Name}");
        }
    }

    /// <summary>
    /// 逐次 DDL 方言（SQL Server / PostgreSQL / MySQL / Oracle）の AddTable が
    /// 計算列の直前に注意コメントを出すことを検証する。
    /// </summary>
    /// <remarks>SQLite の AddTable は再構築（CreateOnly）へ畳まれるため別テストで固定する。</remarks>
    [Fact(DisplayName = "同期 AddTable: 逐次 4 方言とも計算列の直前に注意コメントを出す")]
    public void Sync_AddTable_ComputedColumn_EmitsNote()
    {
        foreach (var dialect in Dialects.Where(d => d.Name != "Sqlite"))
        {
            var item = new SchemaDiffItem
            {
                Kind = SchemaDiffKind.AddTable,
                TableName = "items",
                Entity = BuildEntity(computed: true),
                IsSelected = true,
            };

            BuildScript(dialect.Sync(), item).Should().Contain(NotePrefix, $"{dialect.Name}");
        }
    }

    /// <summary>5 方言の AddColumn が計算列の直前に注意コメントを出すことを検証する</summary>
    [Fact(DisplayName = "同期 AddColumn: 5 方言とも計算列の直前に注意コメントを出す")]
    public void Sync_AddColumn_ComputedColumn_EmitsNote()
    {
        foreach (var dialect in Dialects)
        {
            var item = new SchemaDiffItem
            {
                Kind = SchemaDiffKind.AddColumn,
                TableName = "items",
                Column = new Column
                {
                    Name = "total",
                    DataType = "decimal(21,2)",
                    IsNullable = true,
                    IsComputed = true,
                },
                IsSelected = true,
            };

            var sql = BuildScript(dialect.Sync(), item);
            sql.Should().Contain(NotePrefix, $"{dialect.Name}");

            // コメントは ALTER TABLE 行の直前（同じ行に混ざらない）
            var lines = sql.Split('\n').Select(line => line.TrimEnd('\r')).ToList();
            var noteIndex = lines.FindIndex(line =>
                line.Contains(NotePrefix, StringComparison.Ordinal)
            );
            lines[noteIndex + 1].Should().StartWith("ALTER TABLE", $"{dialect.Name}");
        }
    }

    /// <summary>計算列でない列の AddColumn には注意コメントが出ないことを検証する</summary>
    [Fact(DisplayName = "同期 AddColumn: 計算列でなければ注意コメントは出ない")]
    public void Sync_AddColumn_NonComputedColumn_EmitsNoNote()
    {
        foreach (var dialect in Dialects)
        {
            var item = new SchemaDiffItem
            {
                Kind = SchemaDiffKind.AddColumn,
                TableName = "items",
                Column = new Column
                {
                    Name = "total",
                    DataType = "decimal(21,2)",
                    IsNullable = true,
                },
                IsSelected = true,
            };

            BuildScript(dialect.Sync(), item)
                .Should()
                .NotContain("-- Computed column", $"{dialect.Name}");
        }
    }

    /// <summary>SQLite のテーブル再構築が作り直す CREATE TABLE にも注意コメントが出ることを検証する</summary>
    [Fact(DisplayName = "同期 SQLite 再構築: 計算列の直前に注意コメントを出す")]
    public void Sync_SqliteRebuild_ComputedColumn_EmitsNote()
    {
        var plan = new SyncPlan
        {
            Rebuilds =
            [
                new TableRebuildPlan
                {
                    TableName = "items",
                    NewDefinition = BuildEntity(computed: true),
                    CreateOnly = true,
                },
            ],
        };

        new SqliteSyncScriptBuilder().Build(plan).Should().Contain(NotePrefix);
    }

    /// <summary>SQLite の再構築でも計算列が無ければ注意コメントが出ないことを検証する</summary>
    [Fact(DisplayName = "同期 SQLite 再構築: 計算列が無ければ注意コメントは出ない")]
    public void Sync_SqliteRebuild_NoComputedColumn_EmitsNoNote()
    {
        var plan = new SyncPlan
        {
            Rebuilds =
            [
                new TableRebuildPlan
                {
                    TableName = "items",
                    NewDefinition = BuildEntity(computed: false),
                    CreateOnly = true,
                },
            ],
        };

        new SqliteSyncScriptBuilder().Build(plan).Should().NotContain("-- Computed column");
    }

    /// <summary>コメントへ載せる列名は制御文字を畳んでからでないと行を突き破ることを検証する</summary>
    /// <remarks>
    /// 名前は入口（<see cref="SqlNameText"/>）でも拒否されるが、この文面自体が
    /// <see cref="SqlComment.Sanitize"/> を通っていることをヘルパー直呼びで固定する
    /// （入口ゲートがあるとこの防壁は出力側から観測できないため）。
    /// </remarks>
    [Fact(DisplayName = "注意コメントの列名は改行を空白へ畳む")]
    public void Note_ColumnNameWithNewline_IsFoldedToOneLine()
    {
        var note = ComputedColumnComment.Build(
            new Column
            {
                Name = "a\nDROP TABLE users; --",
                DataType = "int",
                IsComputed = true,
            }
        );

        note.Should().NotBeNull();
        note.Should().NotContain("\n").And.NotContain("\r");
        note.Should().Contain("a DROP TABLE users; --");
    }

    /// <summary>
    /// NULL 許容の計算列の注意コメントは従来の 1 文だけ（NOT NULL 向けの追記が混ざらない）ことを検証する。
    /// </summary>
    /// <remarks>
    /// この列の出力はバイト不変でなければならない（既存の図の DDL・同期スクリプトを動かさないため）。
    /// </remarks>
    [Fact(DisplayName = "注意コメント: NULL 許容の計算列は従来の 1 文だけ")]
    public void Note_NullableComputedColumn_HasNoNotNullSentence()
    {
        ComputedColumnComment
            .Build(
                new Column
                {
                    Name = "total",
                    DataType = "decimal(21,2)",
                    IsNullable = true,
                    IsComputed = true,
                }
            )
            .Should()
            .Be(
                "-- Computed column 'total': "
                    + "the expression is not part of the diagram; add it to the schema by hand."
            );
    }

    /// <summary>
    /// NOT NULL の計算列には「式を足すまで生成 INSERT が NOT NULL で失敗する」1 文が続くことを検証する。
    /// </summary>
    /// <remarks>
    /// 生成コードは計算列を INSERT の対象から外すため、式のない普通の列として作った DB では
    /// そのテーブルへの追加が必ず落ちる。DDL を読む時点で告げるのがいちばん早い。
    /// </remarks>
    [Fact(DisplayName = "注意コメント: NOT NULL の計算列は INSERT が失敗することも告げる")]
    public void Note_NotNullComputedColumn_WarnsAboutInsertFailure()
    {
        var note = ComputedColumnComment.Build(
            new Column
            {
                Name = "period_start",
                DataType = "datetime2",
                IsNullable = false,
                IsComputed = true,
            }
        );

        note.Should()
            .Be(
                "-- Computed column 'period_start': "
                    + "the expression is not part of the diagram; add it to the schema by hand."
                    + " Until the expression is added, generated INSERT statements leave this column out"
                    + " and fail on its NOT NULL constraint."
            );

        // 1 行に収まる（コメント行を突き破らない）
        note.Should().NotContain("\n").And.NotContain("\r");
    }

    /// <summary>
    /// 5 方言の DDL で、NOT NULL の計算列だけに追記が出る（NULL 許容には出ない）ことを検証する。
    /// </summary>
    [Fact(DisplayName = "DDL: NOT NULL の計算列にだけ INSERT 失敗の追記が出る")]
    public void Ddl_NotNullComputedColumn_EmitsNotNullSentence()
    {
        const string NotNullSentence = "fail on its NOT NULL constraint";

        var notNull = new ErDiagram
        {
            Entities =
            {
                new Entity
                {
                    TableName = "temporal_items",
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
                            Name = "period_start",
                            DataType = "datetime2",
                            IsNullable = false,
                            IsComputed = true,
                        },
                    },
                },
            },
        };

        foreach (var dialect in Dialects)
        {
            dialect.Ddl().Build(notNull).Should().Contain(NotNullSentence, $"{dialect.Name}");

            // NULL 許容の計算列（既存の items 図）には追記が出ない
            dialect
                .Ddl()
                .Build(BuildDiagram(computed: true))
                .Should()
                .NotContain(NotNullSentence, $"{dialect.Name}");
        }
    }

    /// <summary>計算列でない列にはコメントを作らないことを検証する</summary>
    [Fact(DisplayName = "注意コメントは計算列でない列には作られない")]
    public void Note_NonComputedColumn_IsNull()
    {
        ComputedColumnComment
            .Build(new Column { Name = "total", DataType = "int" })
            .Should()
            .BeNull();
    }
}
