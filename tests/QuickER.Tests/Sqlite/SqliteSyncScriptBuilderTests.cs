using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Sqlite;
using Xunit;

namespace QuickER.Tests.Sqlite;

/// <summary>
/// <see cref="SqliteSyncScriptBuilder"/> が実行計画（<see cref="SyncPlan"/>）から出力する SQLite 同期スクリプトの
/// 構造（PRAGMA ラップ・再構築ブロックの文順・データ移送・FK インライン・補助オブジェクト再作成）を検証する。
/// </summary>
public class SqliteSyncScriptBuilderTests
{
    private static Column Pk(string name) =>
        new()
        {
            Name = name,
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };

    private static Column Col(string name, string type) =>
        new()
        {
            Name = name,
            DataType = type,
            IsNullable = true,
        };

    private static string Build(SyncPlan plan) => new SqliteSyncScriptBuilder().Build(plan);

    /// <summary>
    /// 合成後の定義へ一意制約を足す（構成列は列名で引き当てる）。
    /// </summary>
    /// <remarks>
    /// 一意制約は補助オブジェクトではなく意味モデル（<see cref="Entity.UniqueConstraints"/>）が正本のため、
    /// テストの入力も再構築計画の <see cref="TableRebuildPlan.NewDefinition"/> 側へ組み立てる。
    /// </remarks>
    private static Entity WithUnique(Entity entity, string? name, params string[] columnNames)
    {
        entity.UniqueConstraints.Add(
            new UniqueConstraint
            {
                Name = name,
                ColumnIds = columnNames
                    .Select(n => entity.Columns.Single(c => c.Name == n).Id)
                    .ToList(),
            }
        );
        return entity;
    }

    /// <summary>空の計画は空文字列を返すことを検証する</summary>
    [Fact(DisplayName = "空の計画は空文字列を返す")]
    public void EmptyPlan_ReturnsEmptyString()
    {
        Build(new SyncPlan()).Should().BeEmpty();
    }

    /// <summary>スクリプトが PRAGMA ヘッダで始まり foreign_key_check → foreign_keys=ON で終わることを検証する</summary>
    [Fact(DisplayName = "PRAGMA ヘッダ／フッタで包む")]
    public void Script_IsWrappedWithPragmas()
    {
        var plan = new SyncPlan
        {
            Rebuilds =
            [
                new TableRebuildPlan
                {
                    TableName = "invoice",
                    NewDefinition = new Entity { TableName = "invoice", Columns = { Pk("id") } },
                    CreateOnly = true,
                },
            ],
        };

        var script = Build(plan);

        script.Should().StartWith("PRAGMA foreign_keys=OFF;");
        script.Should().Contain("PRAGMA foreign_key_check;");
        script.Should().Contain("PRAGMA foreign_keys=ON;");
        script
            .IndexOf("PRAGMA foreign_key_check;", System.StringComparison.Ordinal)
            .Should()
            .BeLessThan(script.IndexOf("PRAGMA foreign_keys=ON;", System.StringComparison.Ordinal));
    }

    /// <summary>CreateOnly（新規テーブル）は FK 句インラインの CREATE TABLE のみで、移送・入替を伴わないことを検証する</summary>
    [Fact(DisplayName = "CreateOnly は FK 句インラインの CREATE のみ")]
    public void CreateOnly_EmitsInlineForeignKeyCreateWithoutMigration()
    {
        var plan = new SyncPlan
        {
            Rebuilds =
            [
                new TableRebuildPlan
                {
                    TableName = "invoice",
                    NewDefinition = new Entity
                    {
                        TableName = "invoice",
                        Columns = { Pk("id"), Col("orders_id", "int") },
                    },
                    ForeignKeys =
                    [
                        new TableRebuildForeignKey(
                            "FK_invoice_orders",
                            ["orders_id"],
                            "orders",
                            ["id"],
                            ForeignKeyReferentialAction.NoAction,
                            ForeignKeyReferentialAction.NoAction
                        ),
                    ],
                    CreateOnly = true,
                },
            ],
        };

        var script = Build(plan);

        script.Should().Contain("CREATE TABLE \"invoice\" (");
        script.Should().Contain("FOREIGN KEY (\"orders_id\")");
        script.Should().Contain("REFERENCES \"orders\" (\"id\")");
        // 新規テーブルはデータ移送・一時テーブル入替を行わない
        script.Should().NotContain("INSERT INTO");
        script.Should().NotContain("_quicker_rebuild");
    }

    /// <summary>既存テーブル再構築ブロックが「CREATE 一時 → INSERT SELECT → DROP → RENAME → 補助再作成」の順であることを検証する</summary>
    [Fact(DisplayName = "再構築ブロックの文順が正しい")]
    public void RebuildBlock_HasExpectedStatementOrder()
    {
        var plan = new SyncPlan
        {
            Rebuilds =
            [
                new TableRebuildPlan
                {
                    TableName = "orders",
                    // 一意制約はモデル正本（Entity.UniqueConstraints）から出力する
                    NewDefinition = WithUnique(
                        new Entity
                        {
                            TableName = "orders",
                            Columns =
                            {
                                Pk("id"),
                                Col("customer_id", "int"),
                                Col("note", "varchar(100)"),
                            },
                        },
                        name: null,
                        "note"
                    ),
                    ForeignKeys =
                    [
                        new TableRebuildForeignKey(
                            "FK_orders_customer",
                            ["customer_id"],
                            "customer",
                            ["id"],
                            ForeignKeyReferentialAction.Cascade,
                            ForeignKeyReferentialAction.NoAction
                        ),
                    ],
                    CreateOnly = false,
                    CopyColumns = ["id", "customer_id", "note"],
                    AuxiliaryObjects =
                    [
                        new SchemaAuxiliaryObject
                        {
                            TableName = "orders",
                            Name = "idx_orders_note",
                            Kind = SchemaAuxiliaryObjectKind.Index,
                            CreateSql = "CREATE INDEX \"idx_orders_note\" ON \"orders\" (\"note\")",
                        },
                    ],
                },
            ],
        };

        var script = Build(plan);

        // 見出し
        script.Should().Contain("-- ===== RebuildTable: orders =====");

        // FK インライン・一意制約のテーブルレベル UNIQUE 再現
        script.Should().Contain("FOREIGN KEY (\"customer_id\")");
        script.Should().Contain("REFERENCES \"customer\" (\"id\") ON DELETE CASCADE");
        // モデル正本化に伴い、無名の UNIQUE (...) から DDL 生成と同じ名前付き制約行へ変わった
        // （制約名は UniqueConstraintNaming が UQ_{実テーブル名}_{列…} を合成する＝一時テーブル名を使わない）
        script.Should().Contain("CONSTRAINT \"UQ_orders_note\" UNIQUE (\"note\")");

        // データ移送は交差列のみ
        script
            .Should()
            .Contain(
                "INSERT INTO \"orders_quicker_rebuild\" (\"id\", \"customer_id\", \"note\") "
                    + "SELECT \"id\", \"customer_id\", \"note\" FROM \"orders\";"
            );

        // 文順: CREATE 一時 → INSERT → DROP → RENAME → 補助 CREATE INDEX
        var iCreate = script.IndexOf(
            "CREATE TABLE \"orders_quicker_rebuild\"",
            System.StringComparison.Ordinal
        );
        var iInsert = script.IndexOf(
            "INSERT INTO \"orders_quicker_rebuild\"",
            System.StringComparison.Ordinal
        );
        var iDrop = script.IndexOf("DROP TABLE \"orders\";", System.StringComparison.Ordinal);
        var iRename = script.IndexOf(
            "ALTER TABLE \"orders_quicker_rebuild\" RENAME TO \"orders\";",
            System.StringComparison.Ordinal
        );
        var iAux = script.IndexOf(
            "CREATE INDEX \"idx_orders_note\"",
            System.StringComparison.Ordinal
        );

        iCreate.Should().BeGreaterThan(-1);
        iCreate.Should().BeLessThan(iInsert);
        iInsert.Should().BeLessThan(iDrop);
        iDrop.Should().BeLessThan(iRename);
        iRename.Should().BeLessThan(iAux);

        // sqlite_master の sql はセミコロン無しのため、補助文末にセミコロンを補う
        script.Should().Contain("CREATE INDEX \"idx_orders_note\" ON \"orders\" (\"note\");");
    }

    /// <summary>構成列が削除された一意制約は再現せず、逆に残っている一意制約は UNIQUE 句へ復元することを検証する</summary>
    [Fact(DisplayName = "削除列を含む一意制約は再現しない")]
    public void UniqueConstraint_ReferencingRemovedColumn_IsDropped()
    {
        // 合成後は id と keep のみ（gone は列削除で消えている）
        var newDefinition = new Entity
        {
            TableName = "t",
            Columns = { Pk("id"), Col("keep", "int") },
        };
        WithUnique(newDefinition, name: null, "keep");

        // 構成列が合成後の定義に存在しない一意制約（＝解決不能）は黙って除外される
        newDefinition.UniqueConstraints.Add(
            new UniqueConstraint { ColumnIds = [Col("gone", "int").Id] }
        );

        var plan = new SyncPlan
        {
            Rebuilds =
            [
                new TableRebuildPlan
                {
                    TableName = "t",
                    NewDefinition = newDefinition,
                    CreateOnly = false,
                    CopyColumns = ["id", "keep"],
                },
            ],
        };

        var script = Build(plan);

        script.Should().Contain("CONSTRAINT \"UQ_t_keep\" UNIQUE (\"keep\")");
        script.Should().NotContain("\"gone\"");
    }

    /// <summary>列追加・テーブル削除は逐次セクション（ADD COLUMN / DROP TABLE）として出力されることを検証する</summary>
    [Fact(DisplayName = "AddColumn / DropTable は逐次セクションになる")]
    public void AddColumnAndDropTable_AreEmittedAsSections()
    {
        var plan = new SyncPlan
        {
            Sections =
            [
                new SyncPlanSection
                {
                    Kind = SchemaDiffKind.AddColumn,
                    Items =
                    [
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AddColumn,
                            TableName = "customer",
                            ColumnName = "email",
                            Column = Col("email", "text"),
                            IsSelected = true,
                        },
                    ],
                },
                new SyncPlanSection
                {
                    Kind = SchemaDiffKind.DropTable,
                    Items =
                    [
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.DropTable,
                            TableName = "legacy",
                            IsSelected = true,
                        },
                    ],
                },
            ],
        };

        var script = Build(plan);

        script.Should().Contain("ALTER TABLE \"customer\" ADD COLUMN \"email\" text NULL;");
        script.Should().Contain("DROP TABLE \"legacy\";");
    }

    /// <summary>並べ替え済み NewDefinition の列順どおりに CREATE TABLE の列が並ぶことを検証する（再構築機構に乗るスモーク）</summary>
    [Fact(DisplayName = "並べ替え済み NewDefinition の列順どおりに CREATE 列が並ぶ")]
    public void ReorderedDefinition_EmitsColumnsInGivenOrder()
    {
        // プランナーが並べ替えた結果（id, c, a, b）を NewDefinition としてそのまま渡す
        var plan = new SyncPlan
        {
            Rebuilds =
            [
                new TableRebuildPlan
                {
                    TableName = "t",
                    NewDefinition = new Entity
                    {
                        TableName = "t",
                        Columns = { Pk("id"), Col("c", "int"), Col("a", "int"), Col("b", "int") },
                    },
                    CreateOnly = false,
                    CopyColumns = ["id", "c", "a", "b"],
                    AuxiliaryObjects = [],
                },
            ],
        };

        var script = Build(plan);

        // CREATE 内の列出現位置が id → c → a → b の順であること
        var iId = script.IndexOf("\"id\"", System.StringComparison.Ordinal);
        var iC = script.IndexOf("\"c\"", System.StringComparison.Ordinal);
        var iA = script.IndexOf("\"a\"", System.StringComparison.Ordinal);
        var iB = script.IndexOf("\"b\"", System.StringComparison.Ordinal);
        iId.Should().BeLessThan(iC);
        iC.Should().BeLessThan(iA);
        iA.Should().BeLessThan(iB);
    }

    /// <summary>非数値引数の宣言型（NVARCHAR(MAX) 等）はダブルクォートで包まれることを検証する（DDL 生成と同一整形）</summary>
    [Fact(DisplayName = "NVARCHAR(MAX) 等の宣言型はクォートされる")]
    public void UnboundedType_IsQuotedInRebuild()
    {
        var plan = new SyncPlan
        {
            Rebuilds =
            [
                new TableRebuildPlan
                {
                    TableName = "orders",
                    NewDefinition = new Entity
                    {
                        TableName = "orders",
                        Columns = { Pk("id"), Col("payload", "NVARCHAR(MAX)") },
                    },
                    CreateOnly = false,
                    CopyColumns = ["id"],
                    AuxiliaryObjects = [],
                },
            ],
        };

        var script = Build(plan);

        // 非数値引数の型は "NVARCHAR(MAX)" とクォートされ syntax error を避ける
        script.Should().Contain("\"payload\" \"NVARCHAR(MAX)\"");
    }
}
