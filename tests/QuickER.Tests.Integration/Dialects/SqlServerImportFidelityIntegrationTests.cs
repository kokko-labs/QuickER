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

    /// <summary>
    /// <c>NOCHECK CONSTRAINT</c> で無効化された外部キーは取り込まず警告として報告する一方、
    /// <c>WITH NOCHECK ADD CONSTRAINT</c>（未信頼だが有効）な外部キーは従来どおり取り込むことを検証する。
    /// </summary>
    /// <remarks>
    /// SQL Server は PRIMARY KEY / UNIQUE 制約を無効化する構文を持たない
    /// （<c>ALTER TABLE ... NOCHECK CONSTRAINT</c> は FOREIGN KEY / CHECK にしか効かない）ため、
    /// この告知は外部キーのみが対象になる。
    /// </remarks>
    [Fact(
        DisplayName = "[Integration] SQL Server: NOCHECK で無効化された FK は除外し報告する／未信頼だが有効な FK は取り込む"
    )]
    public async Task Import_DisabledForeignKey_IsExcludedButUntrustedForeignKeyIsImported()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ExecuteAsync(
            """
            CREATE TABLE parent_t (id int NOT NULL PRIMARY KEY);

            CREATE TABLE disabled_child (id int NOT NULL PRIMARY KEY, pid int NULL);
            ALTER TABLE disabled_child ADD CONSTRAINT FK_disabled_child_parent
                FOREIGN KEY (pid) REFERENCES parent_t (id);
            ALTER TABLE disabled_child NOCHECK CONSTRAINT FK_disabled_child_parent;

            CREATE TABLE untrusted_child (id int NOT NULL PRIMARY KEY, pid int NULL);
            ALTER TABLE untrusted_child WITH NOCHECK ADD CONSTRAINT FK_untrusted_child_parent
                FOREIGN KEY (pid) REFERENCES parent_t (id);
            """,
            Ct
        );

        await using var conn = await fixture.OpenConnectionAsync(Ct);
        var result = await new SqlServerSchemaImporter().ImportAsync(conn, Ct);

        // 無効化された FK だけが除外される（未信頼だが有効な FK は残る）
        var relationship = result.Relationships.Should().ContainSingle().Subject;
        var untrustedChild = result.Entities.Single(e => e.TableName == "untrusted_child");
        relationship.TargetEntityId.Should().Be(untrustedChild.Id);

        var warning = result
            .Warnings.Should()
            .ContainSingle(w => w.Kind == SchemaImportWarningKind.DisabledConstraintExcluded)
            .Subject;
        warning.TableName.Should().Be("disabled_child");
        warning.Subject.Should().Be("FK_disabled_child_parent");
        warning.Detail.Should().Be("FOREIGN KEY");
    }

    /// <summary>
    /// テンポラルテーブル（システムバージョニング）の履歴表は取り込まず本表名つきで告げ、
    /// 本表の期間列は <see cref="QuickER.Model.Column.IsComputed"/> つきで取り込まれ、
    /// その生成規則が計算列と同じ警告種別で報告されることを検証する。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] SQL Server: テンポラルテーブルの履歴表は除外し、期間列は IsComputed つきで取り込む"
    )]
    public async Task Import_TemporalTable_ExcludesHistoryAndMarksPeriodColumns()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        try
        {
            await fixture.ExecuteAsync(
                """
                CREATE TABLE products (
                    product_id int NOT NULL PRIMARY KEY,
                    name nvarchar(100) NOT NULL,
                    valid_from datetime2 GENERATED ALWAYS AS ROW START NOT NULL,
                    valid_to datetime2 GENERATED ALWAYS AS ROW END NOT NULL,
                    PERIOD FOR SYSTEM_TIME (valid_from, valid_to)
                )
                WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = dbo.products_history));
                """,
                Ct
            );

            await using var conn = await fixture.OpenConnectionAsync(Ct);
            var result = await new SqlServerSchemaImporter().ImportAsync(conn, Ct);

            // 履歴表は独立したエンティティとして取り込まれない（本表だけが残る）
            result.Entities.Select(e => e.TableName).Should().BeEquivalentTo("products");

            var historyWarning = result
                .Warnings.Should()
                .ContainSingle(w => w.Kind == SchemaImportWarningKind.TemporalHistoryTableExcluded)
                .Subject;
            historyWarning.TableName.Should().Be("products_history");
            historyWarning.Detail.Should().Be("products");

            var entity = result.Entities.Single();
            var periodColumns = entity
                .Columns.Where(c => c.Name is "valid_from" or "valid_to")
                .ToList();
            periodColumns.Should().OnlyContain(c => c.IsComputed);

            // 通常列は従来どおり計算列にならない
            entity.Columns.Single(c => c.Name == "product_id").IsComputed.Should().BeFalse();
            entity.Columns.Single(c => c.Name == "name").IsComputed.Should().BeFalse();

            var periodWarnings = result
                .Warnings.Where(w => w.Kind == SchemaImportWarningKind.ComputedColumnExpressionLost)
                .ToList();
            periodWarnings.Should().OnlyContain(w => w.TableName == "products");
            periodWarnings
                .Single(w => w.Subject == "valid_from")
                .Detail.Should()
                .Be("GENERATED ALWAYS AS ROW START");
            periodWarnings
                .Single(w => w.Subject == "valid_to")
                .Detail.Should()
                .Be("GENERATED ALWAYS AS ROW END");
        }
        finally
        {
            // SYSTEM_VERSIONING を切らない限り本表・履歴表とも DROP TABLE できない
            // （後続テストの ResetSchemaAsync が失敗しないよう、ここで解除しておく）
            await fixture.ExecuteAsync(
                """
                IF OBJECT_ID('dbo.products', 'U') IS NOT NULL
                BEGIN
                    ALTER TABLE dbo.products SET (SYSTEM_VERSIONING = OFF);
                END
                """,
                Ct
            );
        }
    }

    /// <summary>
    /// <c>HIDDEN</c> 修飾つきの期間列も、カタログ照会（<see cref="QuickER.Provider.SqlServer.SqlServerSchemaImporter"/>
    /// が使う <c>INFORMATION_SCHEMA.COLUMNS</c> / <c>sys.columns</c>）からは通常どおり取得できることを検証する。
    /// </summary>
    /// <remarks>
    /// <c>HIDDEN</c> が変えるのは <c>SELECT *</c> の可視性だけで、カタログビューの列挙には影響しない
    /// （<c>generated_always_type</c> はカタログの列としてそのまま現れる）。生成コードの SELECT は
    /// 常に明示的な列名リストを組み立てる（<c>SelectColumns</c>）ため、この事実は取込の正しさに
    /// 直結する＝ここで確かめておかないと「HIDDEN な期間列だけ取り込み漏れする」回帰に気付けない。
    /// </remarks>
    [Fact(
        DisplayName = "[Integration] SQL Server: HIDDEN な期間列もカタログから取得でき、IsComputed つきで取り込む"
    )]
    public async Task Import_TemporalTableWithHiddenPeriodColumns_PeriodColumnsAreStillImported()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        try
        {
            await fixture.ExecuteAsync(
                """
                CREATE TABLE orders (
                    order_id int NOT NULL PRIMARY KEY,
                    amount decimal(18,2) NOT NULL,
                    valid_from datetime2 GENERATED ALWAYS AS ROW START HIDDEN NOT NULL,
                    valid_to datetime2 GENERATED ALWAYS AS ROW END HIDDEN NOT NULL,
                    PERIOD FOR SYSTEM_TIME (valid_from, valid_to)
                )
                WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = dbo.orders_history));
                """,
                Ct
            );

            await using var conn = await fixture.OpenConnectionAsync(Ct);
            var result = await new SqlServerSchemaImporter().ImportAsync(conn, Ct);

            var entity = result.Entities.Single(e => e.TableName == "orders");
            // HIDDEN でも SELECT * には現れない列がカタログには現れ、通常どおり取り込める
            entity
                .Columns.Select(c => c.Name)
                .Should()
                .BeEquivalentTo(["order_id", "amount", "valid_from", "valid_to"]);
            entity
                .Columns.Where(c => c.Name is "valid_from" or "valid_to")
                .Should()
                .OnlyContain(c => c.IsComputed);
        }
        finally
        {
            await fixture.ExecuteAsync(
                """
                IF OBJECT_ID('dbo.orders', 'U') IS NOT NULL
                BEGIN
                    ALTER TABLE dbo.orders SET (SYSTEM_VERSIONING = OFF);
                END
                """,
                Ct
            );
        }
    }
}
