using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using MySqlConnector;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>MySQL の取込忠実性（取込範囲・警告）の統合テスト。</summary>
[Trait("Category", "Integration")]
[Collection(MySqlContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class MySqlImportFidelityIntegrationTests(MySqlContainerFixture fixture)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>取込範囲（接続先データベース）の外を参照する外部キーの扱いを検証する</summary>
    /// <remarks>
    /// MySQL の「スキーマ」はデータベースそのものなので、他 DB を参照する外部キーは実在しうる。
    /// 参照先テーブルが図に無い以上リレーションは作れないが、従来は <c>REFERENCED_TABLE_SCHEMA</c> を
    /// 見ていなかったため、同名テーブルが接続先 DB にあると<b>別 DB の外部キーを取り違えて</b>結び付け、
    /// 無ければ列解決に失敗して黙って消えていた。
    /// </remarks>
    [Fact(
        DisplayName = "[Integration] A: 取込範囲外を参照する外部キーは除外し、警告として報告する"
    )]
    public async Task Import_ForeignKeyToOtherDatabase_IsExcludedWithWarning()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        // Testcontainers の通常ユーザーは対象 DB にしか権限が無いため、別 DB の作成と
        // そこへの REFERENCES 権限の付与は root で行う（ユーザー名は接続文字列から取る）
        var appUser = new MySqlConnectionStringBuilder(fixture.ConnectionString).UserID;

        await fixture.ExecuteAsRootAsync(
            $"""
            DROP DATABASE IF EXISTS other_scope;
            CREATE DATABASE other_scope;
            CREATE TABLE other_scope.vendor (id int NOT NULL PRIMARY KEY) ENGINE=InnoDB;
            GRANT SELECT, REFERENCES ON other_scope.* TO '{appUser}'@'%';
            """,
            Ct
        );

        try
        {
            // 接続先 DB にも同名テーブルを置く＝スキーマを見ずに突き合わせると取り違える構成
            await fixture.ExecuteAsync(
                """
                CREATE TABLE vendor (id int NOT NULL PRIMARY KEY) ENGINE=InnoDB;
                CREATE TABLE po (
                    id int NOT NULL PRIMARY KEY,
                    vendor_id int NULL,
                    CONSTRAINT FK_po_vendor FOREIGN KEY (vendor_id)
                        REFERENCES other_scope.vendor (id)
                ) ENGINE=InnoDB;
                """,
                Ct
            );

            await using var conn = await fixture.OpenConnectionAsync(Ct);
            var result = await new MySqlSchemaImporter().ImportAsync(conn, Ct);

            result.Entities.Select(e => e.TableName).Should().BeEquivalentTo("vendor", "po");
            result
                .Relationships.Should()
                .BeEmpty(
                    "参照先は別 DB の vendor なので、同名の自 DB の vendor へ結んではいけない"
                );

            var warning = result.Warnings.Should().ContainSingle().Subject;
            warning.Kind.Should().Be(SchemaImportWarningKind.ForeignKeyOutsideScope);
            warning.TableName.Should().Be("po");
            warning.Subject.Should().Be("FK_po_vendor");
            warning.Detail.Should().Be("other_scope.vendor");
        }
        finally
        {
            // 子側を先に落とさないと別 DB の親を落とせない
            await fixture.ExecuteAsync("DROP TABLE IF EXISTS po;", Ct);
            await fixture.ExecuteAsRootAsync("DROP DATABASE IF EXISTS other_scope;", Ct);
        }
    }

    /// <summary>同じ DB 内の外部キーは従来どおり取り込まれる（スキーマ絞りが効きすぎていないことの対照）</summary>
    [Fact(DisplayName = "[Integration] A: 接続先 DB 内の外部キーは従来どおり取り込まれる")]
    public async Task Import_ForeignKeyWithinDatabase_IsStillImported()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ExecuteAsync(
            """
            CREATE TABLE vendor (id int NOT NULL PRIMARY KEY) ENGINE=InnoDB;
            CREATE TABLE po (
                id int NOT NULL PRIMARY KEY,
                vendor_id int NULL,
                CONSTRAINT FK_po_vendor FOREIGN KEY (vendor_id) REFERENCES vendor (id)
            ) ENGINE=InnoDB;
            """,
            Ct
        );

        await using var conn = await fixture.OpenConnectionAsync(Ct);
        var result = await new MySqlSchemaImporter().ImportAsync(conn, Ct);

        result.Relationships.Should().ContainSingle();
        result.Warnings.Should().BeEmpty();
    }

    /// <summary>
    /// 大文字小文字だけが違う同名テーブルを、列を混ぜずに片方だけ取り込み、警告として報告することを検証する。
    /// </summary>
    /// <remarks>
    /// MySQL も <c>lower_case_table_names = 0</c>（Linux の既定・このコンテナも 0）では
    /// <c>Dup</c> と <c>dup</c> が共存する。取込のテーブル辞書は 5 方言共通の大文字小文字非依存なので、
    /// 素の上書きだと 1 エンティティへ潰れて両テーブルの列が混ざる。
    /// </remarks>
    [Fact(
        DisplayName = "[Integration] A: 大文字小文字だけ違う同名テーブルは列を混ぜず片方を捨てて報告する"
    )]
    public async Task Import_TableNamesDifferingOnlyInCase_KeepsOneAndWarns()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        // lower_case_table_names = 1 の環境（Windows / macOS の既定）では 2 つ目の CREATE が
        // 「テーブルが既に在る」で失敗する＝そもそも再現しない構成なので、その場合は検証を飛ばす
        await using (var probe = await fixture.OpenConnectionAsync(Ct))
        {
            await using var cmd = probe.CreateCommand();
            cmd.CommandText = "SELECT @@lower_case_table_names;";
            var lowerCaseTableNames = Convert.ToInt32(await cmd.ExecuteScalarAsync(Ct));

            Assert.SkipWhen(
                lowerCaseTableNames != 0,
                $"lower_case_table_names = {lowerCaseTableNames} のサーバーでは同名テーブルが共存しない"
            );
        }

        await fixture.ExecuteAsync(
            """
            CREATE TABLE `Dup` (upper_only int NULL) ENGINE=InnoDB;
            CREATE TABLE `dup` (lower_only varchar(10) NULL) ENGINE=InnoDB;
            """,
            Ct
        );

        await using var conn = await fixture.OpenConnectionAsync(Ct);
        var result = await new MySqlSchemaImporter().ImportAsync(conn, Ct);

        // BINARY 昇順で先に来る "Dup"（'D' < 'd'）を採り、後着の "dup" は捨てる
        var entity = result.Entities.Should().ContainSingle().Subject;
        entity.TableName.Should().Be("Dup");
        entity
            .Columns.Select(c => c.Name)
            .Should()
            .BeEquivalentTo(["upper_only"], "両テーブルの列が 1 エンティティへ混ざってはいけない");

        var warning = result.Warnings.Should().ContainSingle().Subject;
        warning.Kind.Should().Be(SchemaImportWarningKind.TableNameCollision);
        warning.Subject.Should().Be("dup");
        warning.Detail.Should().Be("Dup");
    }
}
