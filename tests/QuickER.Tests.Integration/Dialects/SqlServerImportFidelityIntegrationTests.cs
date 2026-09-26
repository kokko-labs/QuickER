using AwesomeAssertions;
using Microsoft.Data.SqlClient;
using QuickER.Provider;
using QuickER.Provider.SqlServer;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>SQL Server の取込忠実性（テーブル名の大文字小文字衝突）の統合テスト。</summary>
[Trait("Category", "Integration")]
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class SqlServerImportFidelityIntegrationTests(SqlServerContainerFixture fixture)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>
    /// 大文字小文字だけが違う同名テーブルを、列を混ぜずに片方だけ取り込み、警告として報告することを検証する。
    /// </summary>
    /// <remarks>
    /// フィクスチャの既定データベースは大文字小文字を区別しない照合順序（<c>SQL_Latin1_General_CP1_CI_AS</c>）
    /// のため <c>Dup</c> と <c>dup</c> は同名テーブルとして共存できない。ここでは大文字小文字を区別する照合順序
    /// （<c>Latin1_General_100_CS_AS</c>）の専用データベースを作り、その中で共存させて再現する。
    /// テストの後始末として、作成したデータベースは必ず削除する（他テストへ影響を残さない）。
    /// </remarks>
    [Fact(
        DisplayName = "[Integration] A: 大文字小文字だけ違う同名テーブルは列を混ぜず片方を捨てて報告する"
    )]
    public async Task Import_TableNamesDifferingOnlyInCase_KeepsOneAndWarns()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);

        const string dbName = "quicker_case_sensitive_test";
        var caseSensitiveConnectionString = new SqlConnectionStringBuilder(fixture.ConnectionString)
        {
            InitialCatalog = dbName,
        }.ConnectionString;

        try
        {
            // 既定データベース（master）上で、大文字小文字を区別する照合順序の専用 DB を作る
            await fixture.ExecuteAsync(
                $"""
                IF DB_ID(N'{dbName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{dbName}];
                END
                CREATE DATABASE [{dbName}] COLLATE Latin1_General_100_CS_AS;
                """,
                Ct
            );

            await using (var setupConn = new SqlConnection(caseSensitiveConnectionString))
            {
                await setupConn.OpenAsync(Ct);
                await using var cmd = setupConn.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE [Dup] (
                        a int NOT NULL PRIMARY KEY,
                        code nvarchar(20) NULL,
                        CONSTRAINT UQ_Dup_code UNIQUE (code)
                    );
                    CREATE TABLE [dup] (
                        b nvarchar(20) NULL
                    );
                    CREATE TABLE [Child] (
                        id int NOT NULL PRIMARY KEY,
                        dup_ref int NULL,
                        CONSTRAINT FK_Child_Dup FOREIGN KEY (dup_ref) REFERENCES [Dup] (a)
                    );
                    EXEC sys.sp_addextendedproperty
                        @name = N'MS_Description',
                        @value = N'Winning duplicate table',
                        @level0type = N'SCHEMA', @level0name = N'dbo',
                        @level1type = N'TABLE', @level1name = N'Dup';
                    """;
                await cmd.ExecuteNonQueryAsync(Ct);
            }

            await using var conn = new SqlConnection(caseSensitiveConnectionString);
            await conn.OpenAsync(Ct);
            var result = await new SqlServerSchemaImporter().ImportAsync(conn, Ct);

            // "dup" は 1 エンティティへ潰れず、生存は BIN2 昇順で先に来る "Dup"（'D' < 'd'）
            result.Entities.Select(e => e.TableName).Should().BeEquivalentTo("Dup", "Child");

            var dupEntity = result.Entities.Single(e => e.TableName == "Dup");
            dupEntity
                .Columns.Select(c => c.Name)
                .Should()
                .BeEquivalentTo(
                    ["a", "code"],
                    "両テーブルの列が 1 エンティティへ混ざってはいけない"
                );
            dupEntity.Description.Should().Be("Winning duplicate table");
            dupEntity.Columns.Single(c => c.Name == "a").IsPrimaryKey.Should().BeTrue();
            dupEntity
                .UniqueConstraints.Should()
                .ContainSingle("UNIQUE 制約は生存側にのみ正しく付く");

            var childEntity = result.Entities.Single(e => e.TableName == "Child");
            var relationship = result.Relationships.Should().ContainSingle().Subject;
            relationship.SourceEntityId.Should().Be(dupEntity.Id, "参照先（PK 側）が起点");
            relationship.TargetEntityId.Should().Be(childEntity.Id, "FK 保有テーブルが終点");

            var warning = result.Warnings.Should().ContainSingle().Subject;
            warning.Kind.Should().Be(SchemaImportWarningKind.TableNameCollision);
            warning.Subject.Should().Be("dup");
            warning.Detail.Should().Be("Dup");
        }
        finally
        {
            // 作成した専用 DB は必ず削除し、他テストへ影響を残さない
            await fixture.ExecuteAsync(
                $"""
                IF DB_ID(N'{dbName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{dbName}];
                END
                """,
                Ct
            );
        }
    }

    /// <summary>
    /// 計算列が <see cref="QuickER.Model.Column.IsComputed"/> つきで取り込まれ、式が落ちることが
    /// 警告として報告されることを検証する。
    /// </summary>
    /// <remarks>
    /// 計算列を普通の列として取り込むと、生成 Repository の INSERT / UPDATE がその列を書きに行き
    /// 「computed column のため変更できない」で<b>そのテーブルへの全書き込みが失敗</b>する。
    /// 型・NULL 許容は従来どおり取り込むことも同時に固定する（運ぶのは「書き込めない」事実だけ）。
    /// </remarks>
    [Fact(
        DisplayName = "[Integration] SQL Server: 計算列は IsComputed つきで取り込み、式の喪失を報告する"
    )]
    public async Task Import_ComputedColumn_IsMarkedAndWarns()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ExecuteAsync(
            """
            CREATE TABLE invoices (
                invoice_id int NOT NULL PRIMARY KEY,
                qty int NOT NULL,
                price decimal(18,2) NOT NULL,
                total AS (qty * price),
                total_persisted AS (qty * price) PERSISTED
            );
            """,
            Ct
        );

        await using var conn = await fixture.OpenConnectionAsync(Ct);
        var result = await new SqlServerSchemaImporter().ImportAsync(conn, Ct);

        var entity = result.Entities.Should().ContainSingle().Subject;
        var computed = entity.Columns.Where(c => c.IsComputed).Select(c => c.Name).ToList();
        computed.Should().BeEquivalentTo(["total", "total_persisted"]);

        // 計算列でない列は従来どおり（マーカーは付かない）
        entity.Columns.Single(c => c.Name == "qty").IsComputed.Should().BeFalse();

        // 型・NULL 許容は従来どおり取り込む（運ぶのは「書き込めない」事実だけ）
        entity.Columns.Single(c => c.Name == "total").DataType.Should().Be("decimal(29,2)");

        // 式は意味モデルに載らない＝落ちたことを列ごとに名指しする
        var warnings = result
            .Warnings.Where(w => w.Kind == SchemaImportWarningKind.ComputedColumnExpressionLost)
            .ToList();
        warnings.Select(w => w.Subject).Should().BeEquivalentTo(["total", "total_persisted"]);
        warnings.Should().OnlyContain(w => w.TableName == "invoices");
        // SQL Server は式を持ち帰れる
        warnings
            .Single(w => w.Subject == "total")
            .Detail.Should()
            .Contain("qty")
            .And.Contain("price");
    }

    /// <summary>計算列を持たないテーブルでは計算列の警告が出ないことを検証する（回帰なし）</summary>
    [Fact(DisplayName = "[Integration] SQL Server: 計算列が無ければ計算列の警告は出ない")]
    public async Task Import_NoComputedColumn_NoWarning()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ExecuteAsync(
            "CREATE TABLE plain (id int NOT NULL PRIMARY KEY, qty int NOT NULL);",
            Ct
        );

        await using var conn = await fixture.OpenConnectionAsync(Ct);
        var result = await new SqlServerSchemaImporter().ImportAsync(conn, Ct);

        result.Entities.Single().Columns.Should().OnlyContain(c => !c.IsComputed);
        result
            .Warnings.Should()
            .NotContain(w => w.Kind == SchemaImportWarningKind.ComputedColumnExpressionLost);
    }
}
