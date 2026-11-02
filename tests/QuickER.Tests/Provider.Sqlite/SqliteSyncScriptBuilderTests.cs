using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.Sqlite;
using Xunit;

namespace QuickER.Tests.Provider.Sqlite;

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

    /// <summary>
    /// 既存テーブルの作り直しを含むときだけ、前後を legacy_alter_table の ON / OFF で挟むことを検証する
    /// （作り直す表を参照するビューがあっても RENAME が失敗しないようにする）。
    /// </summary>
    [Fact(
        DisplayName = "既存テーブルの作り直しは legacy_alter_table の ON / OFF で挟み、新規作成だけなら挟まない"
    )]
    public void Rebuild_IsWrappedWithLegacyAlterTableOnlyForExistingTables()
    {
        var rebuild = Build(
            new SyncPlan
            {
                Rebuilds =
                [
                    new TableRebuildPlan
                    {
                        TableName = "invoice",
                        NewDefinition = new Entity
                        {
                            TableName = "invoice",
                            Columns = { Pk("id") },
                        },
                        CreateOnly = false,
                        CopyColumns = ["id"],
                    },
                ],
            }
        );
        var createOnly = Build(
            new SyncPlan
            {
                Rebuilds =
                [
                    new TableRebuildPlan
                    {
                        TableName = "invoice",
                        NewDefinition = new Entity
                        {
                            TableName = "invoice",
                            Columns = { Pk("id") },
                        },
                        CreateOnly = true,
                    },
                ],
            }
        );

        rebuild
            .Should()
            .StartWith(
                "PRAGMA foreign_keys=OFF;"
                    + System.Environment.NewLine
                    + "PRAGMA legacy_alter_table=ON;"
            );
        rebuild
            .IndexOf("PRAGMA legacy_alter_table=OFF;", System.StringComparison.Ordinal)
            .Should()
            .BeGreaterThan(rebuild.IndexOf("RENAME TO", System.StringComparison.Ordinal));
        createOnly.Should().NotContain("legacy_alter_table");
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
}
