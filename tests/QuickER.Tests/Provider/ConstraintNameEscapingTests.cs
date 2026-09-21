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
/// 合成する制約名（<c>PK_{テーブル名}</c>）も、他の識別子と同じく<b>クォート時にエスケープされる</b>ことを
/// 5 方言まとめて固定する。
/// </summary>
/// <remarks>
/// <para>
/// 制約名の合成は <c>SafeName</c>（<c>.</c> と空白を <c>_</c> へ置換するだけ）を通るが、<c>SafeName</c> は
/// 終端クォート文字（<c>]</c> / <c>"</c> / <c>`</c>）を二重化しない。合成結果を手書きのクォートへ直接埋めると
/// テーブル名 <c>X]</c> が <c>[PK_X]…]</c> のようにクォートを閉じ、以降が SQL のコードとして解釈される。
/// </para>
/// <para>
/// UNIQUE 制約名・外部キー制約名は当初からエスケープを通っており、主キーの合成名だけが素通しだった
/// （DDL 生成の基底 <c>DdlGeneratorBase</c> は <c>QuoteConstraintName</c> 経由で正しく、同期スクリプト生成の
/// 4 方言と SQLite の DDL だけが非対称だった）。その非対称を解消したことをここで固定する。
/// </para>
/// </remarks>
public class ConstraintNameEscapingTests
{
    /// <summary>1 方言分のクォート文字と同期スクリプトビルダー</summary>
    /// <param name="Dialect">方言名（失敗メッセージ用）</param>
    /// <param name="Open">クォート開始文字</param>
    /// <param name="Close">クォート終了文字（＝二重化の対象）</param>
    /// <param name="Builder">同期スクリプトビルダー</param>
    /// <param name="NamesAddedPrimaryKey">
    /// 主キー付与文が制約名を持つか（MySQL の主キー名は <c>PRIMARY</c> 固定のため名前を書かない）
    /// </param>
    private sealed record SyncDialect(
        string Dialect,
        string Open,
        string Close,
        ISyncScriptBuilder Builder,
        bool NamesAddedPrimaryKey
    );

    /// <summary>同期スクリプトを生成する 4 方言（SQLite は再構築方式のため DDL 側で検証する）</summary>
    private static readonly SyncDialect[] SyncDialects =
    [
        new("SqlServer", "[", "]", new SqlServerSyncScriptBuilder(), true),
        new("PostgreSql", "\"", "\"", new PostgreSqlSyncScriptBuilder(), true),
        new("MySql", "`", "`", new MySqlSyncScriptBuilder(), false),
        new("Oracle", "\"", "\"", new OracleSyncScriptBuilder(), true),
    ];

    [Fact(DisplayName = "AddTable の主キー制約名は終端クォート文字を二重化する（4 方言）")]
    public void CreateTable_PrimaryKeyConstraintName_IsEscaped()
    {
        foreach (var dialect in SyncDialects)
        {
            var table = "t" + dialect.Close;
            var sql = dialect.Builder.Build(
                new SyncPlanner().BuildPlan(
                    [
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AddTable,
                            TableName = table,
                            Entity = PkTable(table),
                            IsSelected = true,
                        },
                    ],
                    new SyncDialectCapabilities()
                )
            );

            sql.Should()
                .Contain(
                    ExpectedConstraint(dialect.Open, dialect.Close),
                    $"{dialect.Dialect} の CREATE TABLE は合成した主キー制約名もエスケープすべき"
                );
        }
    }

    [Fact(
        DisplayName = "AlterPrimaryKey の付与文は主キー制約名の終端クォート文字を二重化する（3 方言）"
    )]
    public void AddPrimaryKey_ConstraintName_IsEscaped()
    {
        foreach (var dialect in SyncDialects.Where(d => d.NamesAddedPrimaryKey))
        {
            var table = "t" + dialect.Close;
            var sql = dialect.Builder.Build(
                new SyncPlanner().BuildPlan(
                    [
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AlterPrimaryKey,
                            TableName = table,
                            Entity = PkTable(table),
                            IsSelected = true,
                        },
                    ],
                    new SyncDialectCapabilities()
                )
            );

            sql.Should()
                .Contain(
                    ExpectedConstraint(dialect.Open, dialect.Close),
                    $"{dialect.Dialect} の主キー付与文は合成した制約名もエスケープすべき"
                );
        }
    }

    [Fact(DisplayName = "SQLite の DDL は主キー制約名の二重引用符を二重化する")]
    public void SqliteDdl_PrimaryKeyConstraintName_IsEscaped()
    {
        var table = "t\"";
        var sql = new SqliteDdlGenerator().Build(new ErDiagram { Entities = [PkTable(table)] });

        sql.Should().Contain(ExpectedConstraint("\"", "\""));
    }

    /// <summary>単一主キーを持つ最小のエンティティを組み立てる</summary>
    private static Entity PkTable(string tableName)
    {
        var entity = new Entity { TableName = tableName };
        entity.Columns.Add(
            new Column
            {
                Name = "id",
                DataType = "integer",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );

        return entity;
    }

    /// <summary>期待する制約名の表記（テーブル名末尾の終端クォート文字が二重化されている形）</summary>
    private static string ExpectedConstraint(string open, string close) =>
        $"CONSTRAINT {open}PK_t{close}{close}{close}";
}
