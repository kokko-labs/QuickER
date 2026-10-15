using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.Provider;

/// <summary>
/// DB が値を採番する列（<see cref="Column.IsIdentity"/>）の注記を、DDL 生成と同期スクリプト生成
/// （AddTable / AddColumn / SQLite の再構築）で固定するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 意味モデルは採番の定義を持たないため、出力するのは普通の列になる。扱いの表
/// （<see cref="UnsupportedTreatments"/>）は SQL Server・PostgreSQL・Oracle を「普通の列＋注記」、
/// MySQL・SQLite を「なし」（印の付いた列があり得ない）とする。期待は方言ごとに書き下ろし、
/// 表との一致は別のテストで確かめる（表を読んで期待を作ると、表と出力が同時に崩れたとき緑のまま残る）。
/// </para>
/// <para>
/// 採番列を持たない図の出力にはこの注記が 1 つも出ない（既存の <c>*DdlGeneratorTests</c> /
/// <c>*SyncScriptBuilderTests</c> が緑のままであることと合わせて、採番列の無い図の出力はバイト不変）。
/// </para>
/// </remarks>
public class IdentityColumnCommentTests
{
    /// <summary>注記の識別に使う接頭辞（文面の正本は <see cref="IdentityColumnComment"/>）</summary>
    private const string NotePrefix = "-- Identity column 'seq'";

    /// <summary>主キーでない NOT NULL の採番列にだけ足す文</summary>
    private const string NotNullSentence = "fail on its NOT NULL constraint";

    /// <summary>1 方言分の DDL 生成器・同期スクリプト生成器と、注記を出すかの期待</summary>
    private sealed record Dialect(
        string Name,
        Func<IDdlGenerator> Ddl,
        Func<ISyncScriptBuilder> Sync,
        bool Annotates
    );

    private static readonly Dialect[] Dialects =
    [
        new(
            DialectNames.SqlServer,
            () => new SqlServerDdlGenerator(),
            () => new SqlServerSyncScriptBuilder(),
            Annotates: true
        ),
        new(
            DialectNames.PostgreSql,
            () => new PostgreSqlDdlGenerator(),
            () => new PostgreSqlSyncScriptBuilder(),
            Annotates: true
        ),
        new(
            DialectNames.MySql,
            () => new MySqlDdlGenerator(),
            () => new MySqlSyncScriptBuilder(),
            Annotates: false
        ),
        new(
            DialectNames.Oracle,
            () => new OracleDdlGenerator(),
            () => new OracleSyncScriptBuilder(),
            Annotates: true
        ),
        new(
            DialectNames.Sqlite,
            () => new SqliteDdlGenerator(),
            () => new SqliteSyncScriptBuilder(),
            Annotates: false
        ),
    ];

    /// <summary>採番列 1 本を持つ（または持たない）1 テーブルを組み立てる</summary>
    private static Entity BuildEntity(bool identity, bool nullable = true, bool computed = false) =>
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
                    Name = "seq",
                    DataType = "int",
                    IsNullable = nullable,
                    IsIdentity = identity,
                    IsComputed = computed,
                },
            },
        };

    private static ErDiagram BuildDiagram(bool identity, bool nullable = true) =>
        new() { Entities = { BuildEntity(identity, nullable) } };

    private static string BuildScript(ISyncScriptBuilder builder, params SchemaDiffItem[] items) =>
        builder.Build(new SyncPlanner().BuildPlan(items, TestDialectCapabilities.Neutral));

    private static SchemaDiffItem AddColumnItem(bool identity, bool computed = false) =>
        new()
        {
            Kind = SchemaDiffKind.AddColumn,
            TableName = "items",
            Column = new Column
            {
                Name = "seq",
                DataType = "int",
                IsNullable = true,
                IsIdentity = identity,
                IsComputed = computed,
            },
            IsSelected = true,
        };

    private static List<string> Lines(string sql) =>
        sql.Split('\n').Select(line => line.TrimEnd('\r')).ToList();

    [Fact(
        DisplayName = "DDL: SQL Server・PostgreSQL・Oracle は採番列の直前に注記を出し、MySQL・SQLite は出さない"
    )]
    public void Ddl_IdentityColumn_IsAnnotatedOnlyWhereTheTableSays()
    {
        foreach (var dialect in Dialects)
        {
            var sql = dialect.Ddl().Build(BuildDiagram(identity: true));

            if (!dialect.Annotates)
            {
                sql.Should().NotContain("-- Identity column", dialect.Name);
                continue;
            }

            var lines = Lines(sql);
            var noteIndex = lines.FindIndex(line =>
                line.Contains(NotePrefix, StringComparison.Ordinal)
            );
            noteIndex.Should().BeGreaterThan(-1, $"{dialect.Name} は注記を出す");
            lines[noteIndex + 1]
                .Should()
                .Contain("seq", $"{dialect.Name} は注記の直後に採番列の定義を出す");
            sql.Should().NotContain("-- Identity column 'item_id'", dialect.Name);
        }
    }

    [Fact(DisplayName = "DDL: 採番列が無ければ注記は出ない")]
    public void Ddl_NoIdentityColumn_EmitsNoNote()
    {
        foreach (var dialect in Dialects)
        {
            dialect
                .Ddl()
                .Build(BuildDiagram(identity: false))
                .Should()
                .NotContain("-- Identity column", dialect.Name);
        }
    }

    [Fact(
        DisplayName = "同期 AddTable: SQL Server・PostgreSQL・Oracle は採番列に注記を出し、MySQL は出さない"
    )]
    public void Sync_AddTable_IdentityColumn_IsAnnotatedOnlyWhereTheTableSays()
    {
        foreach (var dialect in Dialects.Where(d => d.Name != DialectNames.Sqlite))
        {
            var item = new SchemaDiffItem
            {
                Kind = SchemaDiffKind.AddTable,
                TableName = "items",
                Entity = BuildEntity(identity: true),
                IsSelected = true,
            };

            var sql = BuildScript(dialect.Sync(), item);

            if (dialect.Annotates)
            {
                sql.Should().Contain(NotePrefix, dialect.Name);
            }
            else
            {
                sql.Should().NotContain("-- Identity column", dialect.Name);
            }
        }
    }

    [Fact(
        DisplayName = "同期 AddColumn: SQL Server・PostgreSQL・Oracle は ALTER TABLE の直前に注記を出し、MySQL・SQLite は出さない"
    )]
    public void Sync_AddColumn_IdentityColumn_IsAnnotatedOnlyWhereTheTableSays()
    {
        foreach (var dialect in Dialects)
        {
            var sql = BuildScript(dialect.Sync(), AddColumnItem(identity: true));

            if (!dialect.Annotates)
            {
                sql.Should().NotContain("-- Identity column", dialect.Name);
                continue;
            }

            var lines = Lines(sql);
            var noteIndex = lines.FindIndex(line =>
                line.Contains(NotePrefix, StringComparison.Ordinal)
            );
            noteIndex.Should().BeGreaterThan(-1, dialect.Name);
            lines[noteIndex + 1].Should().StartWith("ALTER TABLE", dialect.Name);
        }
    }

    [Fact(DisplayName = "同期 AddColumn: 採番列でなければ注記は出ない")]
    public void Sync_AddColumn_NonIdentityColumn_EmitsNoNote()
    {
        foreach (var dialect in Dialects)
        {
            BuildScript(dialect.Sync(), AddColumnItem(identity: false))
                .Should()
                .NotContain("-- Identity column", dialect.Name);
        }
    }

    [Fact(DisplayName = "同期 SQLite 再構築: 採番列に注記は出さない（扱いの表が「なし」）")]
    public void Sync_SqliteRebuild_IdentityColumn_EmitsNoNote()
    {
        var plan = new SyncPlan
        {
            Rebuilds =
            [
                new TableRebuildPlan
                {
                    TableName = "items",
                    NewDefinition = BuildEntity(identity: true),
                    CreateOnly = true,
                },
            ],
        };

        new SqliteSyncScriptBuilder().Build(plan).Should().NotContain("-- Identity column");
    }

    [Fact(
        DisplayName = "同期 AddColumn: 計算列でもある採番列は、計算列の注記と採番列の注記を両方 ALTER TABLE の前に出す"
    )]
    public void Sync_AddColumn_ComputedIdentityColumn_EmitsBothNotes()
    {
        foreach (var dialect in Dialects.Where(d => d.Annotates))
        {
            var lines = Lines(
                BuildScript(dialect.Sync(), AddColumnItem(identity: true, computed: true))
            );
            var computedIndex = lines.FindIndex(line =>
                line.StartsWith("-- Computed column 'seq'", StringComparison.Ordinal)
            );
            var identityIndex = lines.FindIndex(line =>
                line.StartsWith(NotePrefix, StringComparison.Ordinal)
            );

            computedIndex.Should().BeGreaterThan(-1, dialect.Name);
            identityIndex.Should().Be(computedIndex + 1, dialect.Name);
            lines[identityIndex + 1].Should().StartWith("ALTER TABLE", dialect.Name);
        }
    }

    [Fact(DisplayName = "注記を出す方言は、扱いの表の DDL・同期のマスが「注記」の方言と一致する")]
    public void AnnotatingDialects_MatchTreatmentTable()
    {
        foreach (var feature in new[] { UnsupportedFeature.Ddl, UnsupportedFeature.Sync })
        {
            Dialects
                .Where(d => d.Annotates)
                .Select(d => d.Name)
                .Should()
                .BeEquivalentTo(
                    DialectNames.All.Where(dialect =>
                        UnsupportedTreatments.For(
                            UnsupportedDefinitionKind.IdentityRejectingValues,
                            feature,
                            dialect
                        ) == UnsupportedTreatment.Annotation
                    ),
                    feature.ToString()
                );
        }
    }

    [Fact(DisplayName = "注記: NULL 許容の採番列は 1 文だけ")]
    public void Note_NullableIdentityColumn_HasOneSentence()
    {
        IdentityColumnComment
            .Build(
                new Column
                {
                    Name = "seq",
                    DataType = "int",
                    IsNullable = true,
                    IsIdentity = true,
                },
                DialectNames.SqlServer,
                UnsupportedFeature.Ddl
            )
            .Should()
            .Be(
                "-- Identity column 'seq': the database assigns its values, but the diagram does not record"
                    + " the identity definition; this creates a plain column. Add the identity property by hand."
            );
    }

    [Fact(
        DisplayName = "注記: 主キーでない NOT NULL の採番列は、生成コードの INSERT が失敗することも告げる"
    )]
    public void Note_NotNullIdentityColumn_WarnsAboutInsertFailure()
    {
        foreach (var dialect in Dialects.Where(d => d.Annotates))
        {
            dialect
                .Ddl()
                .Build(BuildDiagram(identity: true, nullable: false))
                .Should()
                .Contain(NotNullSentence, dialect.Name);
            dialect
                .Ddl()
                .Build(BuildDiagram(identity: true, nullable: true))
                .Should()
                .NotContain(NotNullSentence, dialect.Name);
        }
    }

    [Fact(
        DisplayName = "注記: 主キーの採番列は生成コードが値を送るので、INSERT が失敗する文を足さない"
    )]
    public void Note_PrimaryKeyIdentityColumn_HasNoInsertFailureSentence()
    {
        var note = IdentityColumnComment.Build(
            new Column
            {
                Name = "id",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
                IsIdentity = true,
            },
            DialectNames.PostgreSql,
            UnsupportedFeature.Ddl
        );

        note.Should().NotBeNull().And.NotContain(NotNullSentence);
    }

    [Fact(DisplayName = "注記の列名は改行を空白へ畳む")]
    public void Note_ColumnNameWithNewline_IsFoldedToOneLine()
    {
        var note = IdentityColumnComment.Build(
            new Column
            {
                Name = "a\nDROP TABLE users; --",
                DataType = "int",
                IsIdentity = true,
            },
            DialectNames.Oracle,
            UnsupportedFeature.Sync
        );

        note.Should().NotBeNull();
        note.Should().NotContain("\n").And.NotContain("\r");
        note.Should().Contain("a DROP TABLE users; --");
    }
}
