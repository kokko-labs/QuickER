using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using MySqlConnector;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// MySQL の同期で、外部キー・主キーの暗黙の外し直し（列の型変更・主キー変更）が実 DB で成り立つことを検証する統合テスト。
/// </summary>
/// <remarks>
/// MySQL は DDL が暗黙コミットされるため、外部キーを外したあとに付け直しが失敗すると外部キーが消えたまま残る。
/// ここでは「通るべき変更が通る」ことと「通らない変更は何も変えずに失敗する」ことの両方を固定する。
/// 図は GUI と同じく live と別の Guid 空間に作る。Docker 必須（<see cref="MySqlContainerFixture"/>）。
/// </remarks>
[Trait("Category", "Integration")]
[Collection(MySqlContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class MySqlSyncDependencyIntegrationTests(MySqlContainerFixture fixture)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly MySqlProvider _provider = new();

    /// <summary>外部キーの両端の列を同じ型へ変える同期が、外部キーを外して付け直して成功することを検証する</summary>
    [Fact(
        DisplayName = "[Integration] MySQL 同期: 外部キーの両端を同じ型へ変える同期が外部キーを保って成功する"
    )]
    public async Task ForeignKeyColumnTypeChange_BothEnds_Succeeds()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);
        await CreateParentChildAsync();

        var (result, script, plan) = await SyncAsync(
            (entities, _) =>
            {
                entities.Single(e => e.TableName == "md_parent").Columns[0].DataType = "bigint";
                entities.Single(e => e.TableName == "md_child").Columns[1].DataType = "bigint";
            },
            item => item.Kind == SchemaDiffKind.AlterColumn
        );

        plan.Warnings.Should().BeEmpty();
        result.Committed.Should().BeTrue($"{result.Error}{System.Environment.NewLine}{script}");

        var live = await ImportAsync();
        live.Entities.Single(e => e.TableName == "md_parent")
            .Columns.Single(c => c.Name == "id")
            .DataType.Should()
            .Be("bigint");
        live.Entities.Single(e => e.TableName == "md_child")
            .Columns.Single(c => c.Name == "parent_id")
            .DataType.Should()
            .Be("bigint");
        live.Relationships.Should().ContainSingle().Which.ConstraintName.Should().Be("fk_md");
    }

    /// <summary>
    /// 外部キーの片側の文字列型の長さだけを変える同期が、外部キーを外して付け直して成功することを検証する。
    /// MySQL は両端の文字列型の長さの違いを許すが、varchar の長さが 255 バイトの境界を越える変更は外部キーを外さないと
    /// エラー 1832 になる。以前は長さの違いを「型の食い違い」とみなして外し直さず、警告つきで失敗していた。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] MySQL 同期: 外部キーの片側の文字列の長さだけを変える同期は外部キーを保って成功する"
    )]
    public async Task ForeignKeyStringLengthChange_OneEnd_Succeeds()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ExecuteAsync(
            "CREATE TABLE `md_parent` (`code` varchar(50) NOT NULL, PRIMARY KEY (`code`));",
            Ct
        );
        await fixture.ExecuteAsync(
            "CREATE TABLE `md_child` (`id` int NOT NULL, `parent_code` varchar(50) NOT NULL, PRIMARY KEY (`id`), "
                + "CONSTRAINT `fk_md` FOREIGN KEY (`parent_code`) REFERENCES `md_parent` (`code`));",
            Ct
        );
        await fixture.ExecuteAsync("INSERT INTO `md_parent` (`code`) VALUES ('a');", Ct);
        await fixture.ExecuteAsync(
            "INSERT INTO `md_child` (`id`, `parent_code`) VALUES (1, 'a');",
            Ct
        );

        var (result, script, plan) = await SyncAsync(
            (entities, _) =>
                entities
                    .Single(e => e.TableName == "md_child")
                    .Columns.Single(c => c.Name == "parent_code")
                    .DataType = "varchar(100)",
            item => item.Kind == SchemaDiffKind.AlterColumn
        );

        plan.Warnings.Should().BeEmpty();
        result.Committed.Should().BeTrue($"{result.Error}{System.Environment.NewLine}{script}");
        var live = await ImportAsync();
        live.Entities.Single(e => e.TableName == "md_child")
            .Columns.Single(c => c.Name == "parent_code")
            .DataType.Should()
            .Be("varchar(100)");
        live.Relationships.Should().ContainSingle().Which.ConstraintName.Should().Be("fk_md");
    }

    /// <summary>
    /// 外部キーの片側の列だけ型を変える同期は警告され、実行は列の変更で失敗して、外部キーも型も元のまま残ることを検証する
    /// （外部キーを外してから付け直しに失敗すると、MySQL では外部キーが消えたまま残る）。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] MySQL 同期: 外部キーの片側だけ型を変えると警告し、実行しても外部キーと型は元のまま"
    )]
    public async Task ForeignKeyColumnTypeChange_OneEnd_WarnsAndKeepsForeignKey()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);
        await CreateParentChildAsync();

        var (result, _, plan) = await SyncAsync(
            (entities, _) =>
                entities.Single(e => e.TableName == "md_parent").Columns[0].DataType = "bigint",
            item => item.Kind == SchemaDiffKind.AlterColumn
        );

        plan.Warnings.Should()
            .ContainSingle(w => w.Kind == SyncPlanWarningKind.ForeignKeyColumnTypeMismatch)
            .Which.Detail.Should()
            .Be("fk_md");
        result.Committed.Should().BeFalse();

        var live = await ImportAsync();
        live.Entities.Single(e => e.TableName == "md_parent")
            .Columns.Single(c => c.Name == "id")
            .DataType.Should()
            .Be("int");
        live.Relationships.Should().ContainSingle().Which.ConstraintName.Should().Be("fk_md");
    }

    /// <summary>
    /// 参照される複合主キーの列の並びだけを入れ替える同期が、その主キーを (a, b) の並びで参照する外部キーを外し、
    /// 新しい主キーの並び (b, a) で付け直して成功することを検証する（MySQL は参照する列の並びがキーの並びと一致することを
    /// 求める。以前は付け直せないので計画から外して何も変えなかった）。外部キーの外し直しは主キーの変更だけを選んでも
    /// 計画に入り、取り込み直すと差分は出ず、行も残る。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] MySQL 同期: 参照される複合主キーの並びだけを入れ替えると、外部キーを新しい並びで付け直して成功する"
    )]
    public async Task ReorderReferencedCompositePrimaryKey_RebuildsForeignKeyInNewOrder()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ExecuteAsync(
            "CREATE TABLE `md_pair` (`a` int NOT NULL, `b` int NOT NULL, PRIMARY KEY (`a`, `b`));",
            Ct
        );
        await fixture.ExecuteAsync(
            "CREATE TABLE `md_ref` (`id` int NOT NULL, `ra` int NOT NULL, `rb` int NOT NULL, PRIMARY KEY (`id`), "
                + "CONSTRAINT `fk_md_ref` FOREIGN KEY (`ra`, `rb`) REFERENCES `md_pair` (`a`, `b`));",
            Ct
        );
        await fixture.ExecuteAsync("INSERT INTO `md_pair` (`a`, `b`) VALUES (1, 2);", Ct);
        await fixture.ExecuteAsync("INSERT INTO `md_ref` (`id`, `ra`, `rb`) VALUES (1, 1, 2);", Ct);

        static void ReorderKey(List<Entity> entities, List<Relationship> _)
        {
            var pair = entities.Single(e => e.TableName == "md_pair");
            pair.PrimaryKeyColumnIds = [pair.Columns[1].Id, pair.Columns[0].Id];
        }

        var (result, script, plan) = await SyncAsync(
            ReorderKey,
            item => item.Kind == SchemaDiffKind.AlterPrimaryKey
        );

        plan.Warnings.Should().BeEmpty();
        plan.Sections.Single(s => s.Kind == SchemaDiffKind.DropForeignKey)
            .Items.Should()
            .ContainSingle()
            .Which.ForeignKeyName.Should()
            .Be("fk_md_ref");
        script.Should().Contain("FOREIGN KEY (`rb`, `ra`) REFERENCES `md_pair` (`b`, `a`)");
        result.Committed.Should().BeTrue($"{result.Error}{System.Environment.NewLine}{script}");

        var live = await ImportAsync();
        live.Entities.Single(e => e.TableName == "md_pair")
            .GetPrimaryKeyColumnsInOrder()
            .Select(c => c.Name)
            .Should()
            .Equal("b", "a");
        ForeignKeyPairNames(live, "fk_md_ref").Should().Equal(("b", "rb"), ("a", "ra"));
        (await CountRowsAsync("md_ref")).Should().Be(1);

        var (_, _, again) = await SyncAsync(ReorderKey, _ => true);
        again.IsEmpty.Should().BeTrue();
    }

    /// <summary>
    /// 同じ列の組で同じ複合主キーを参照する外部キーが 2 本あるとき、参照される主キーの並びだけを入れ替える同期が、
    /// 2 本とも新しい並びで付け直して成功することを検証する（MySQL は 2 本の外部キーに 1 つの索引を共有させ、名前は
    /// 片方の外部キーの名前になる。1 本目を外すときはもう 1 本が使っているので索引を残し、2 本目を外した後も残ると、
    /// 新しい並びの付け直しが同じ名前の索引を作ろうとしてエラー 1061 で失敗した）。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] MySQL 同期: 同じ列の外部キー 2 本が参照する複合主キーの並びを入れ替えても、2 本とも付け直して成功する"
    )]
    public async Task ReorderReferencedCompositePrimaryKey_TwoForeignKeysOnSameColumns_Succeeds()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ExecuteAsync(
            "CREATE TABLE `md_pair` (`a` int NOT NULL, `b` int NOT NULL, PRIMARY KEY (`a`, `b`));",
            Ct
        );
        await fixture.ExecuteAsync(
            "CREATE TABLE `md_ref` (`id` int NOT NULL, `ra` int NOT NULL, `rb` int NOT NULL, PRIMARY KEY (`id`), "
                + "UNIQUE KEY `uq_md_ref_rb` (`rb`));",
            Ct
        );
        // 先に足した外部キー（fk_md_second）の名前で索引ができ、後から足した外部キー（fk_md_first）はその索引を使う
        await fixture.ExecuteAsync(
            "ALTER TABLE `md_ref` ADD CONSTRAINT `fk_md_second` FOREIGN KEY (`ra`, `rb`) REFERENCES `md_pair` (`a`, `b`) "
                + "ON DELETE CASCADE;",
            Ct
        );
        await fixture.ExecuteAsync(
            "ALTER TABLE `md_ref` ADD CONSTRAINT `fk_md_first` FOREIGN KEY (`ra`, `rb`) REFERENCES `md_pair` (`a`, `b`);",
            Ct
        );

        static void ReorderKey(List<Entity> entities, List<Relationship> _)
        {
            var pair = entities.Single(e => e.TableName == "md_pair");
            pair.PrimaryKeyColumnIds = [pair.Columns[1].Id, pair.Columns[0].Id];
        }

        var (result, script, plan) = await SyncAsync(
            ReorderKey,
            item => item.Kind == SchemaDiffKind.AlterPrimaryKey
        );

        plan.Warnings.Should().BeEmpty();
        result.Committed.Should().BeTrue($"{result.Error}{System.Environment.NewLine}{script}");

        var live = await ImportAsync();
        ForeignKeyPairNames(live, "fk_md_first").Should().Equal(("b", "rb"), ("a", "ra"));
        ForeignKeyPairNames(live, "fk_md_second").Should().Equal(("b", "rb"), ("a", "ra"));

        var (_, _, again) = await SyncAsync(ReorderKey, _ => true);
        again.IsEmpty.Should().BeTrue();
    }

    /// <summary>
    /// 図の列ペアの並びが参照先の主キーと逆の外部キーを、参照アクションの変更で作り直す同期が、キーの並びで付け直して
    /// 成功することを検証する（差分は列ペアを並びを見ずに比べるので、作り直しは参照アクションの変更で起きる。以前は
    /// 付け直せないので計画から外して何も変えなかった）。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] MySQL 同期: 図の並びが参照先のキーと違う外部キーの作り直しは、キーの並びで付け直して成功する"
    )]
    public async Task AlterForeignKeyInOtherOrder_IsRebuiltInKeyOrder()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ExecuteAsync(
            "CREATE TABLE `md_pair` (`a` int NOT NULL, `b` int NOT NULL, PRIMARY KEY (`a`, `b`));",
            Ct
        );
        await fixture.ExecuteAsync(
            "CREATE TABLE `md_ref` (`id` int NOT NULL, `ra` int NOT NULL, `rb` int NOT NULL, PRIMARY KEY (`id`), "
                + "CONSTRAINT `fk_md_ref` FOREIGN KEY (`ra`, `rb`) REFERENCES `md_pair` (`a`, `b`));",
            Ct
        );

        static void ReverseAndCascade(List<Entity> _, List<Relationship> relationships)
        {
            var fk = relationships.Single();
            fk.ColumnPairs = [.. fk.ColumnPairs.AsEnumerable().Reverse()];
            fk.OnDelete = ForeignKeyReferentialAction.Cascade;
        }

        var (result, script, plan) = await SyncAsync(
            ReverseAndCascade,
            item => item.Kind == SchemaDiffKind.AlterForeignKey
        );

        plan.Warnings.Should().BeEmpty();
        plan.Sections.Select(s => s.Kind)
            .Should()
            .Equal(SchemaDiffKind.DropForeignKey, SchemaDiffKind.AddForeignKey);
        script.Should().Contain("FOREIGN KEY (`ra`, `rb`) REFERENCES `md_pair` (`a`, `b`)");
        result.Committed.Should().BeTrue($"{result.Error}{System.Environment.NewLine}{script}");

        var live = await ImportAsync();
        live.Relationships.Should()
            .ContainSingle()
            .Which.OnDelete.Should()
            .Be(ForeignKeyReferentialAction.Cascade);
        ForeignKeyPairNames(live, "fk_md_ref").Should().Equal(("a", "ra"), ("b", "rb"));

        var (_, _, again) = await SyncAsync(ReverseAndCascade, _ => true);
        again.IsEmpty.Should().BeTrue();
    }

    /// <summary>
    /// 主キーの先頭の列に乗る外部キーを持つ表の主キーを並べ替える同期が成功することを検証する
    /// （旧主キーが外部キーを支える唯一の索引だと、素の DROP PRIMARY KEY はエラー 1553 になる）。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] MySQL 同期: 主キーの先頭に乗る外部キーを持つ表の主キーを並べ替えられる"
    )]
    public async Task AlterPrimaryKey_WithForeignKeyOnLeadingColumn_Succeeds()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ExecuteAsync(
            "CREATE TABLE `md_p` (`id` int NOT NULL, PRIMARY KEY (`id`));",
            Ct
        );
        await fixture.ExecuteAsync(
            "CREATE TABLE `md_link` (`p_id` int NOT NULL, `x` int NOT NULL, "
                + "PRIMARY KEY (`p_id`, `x`), "
                + "CONSTRAINT `fk_link` FOREIGN KEY (`p_id`) REFERENCES `md_p` (`id`));",
            Ct
        );
        await fixture.ExecuteAsync("INSERT INTO `md_p` (`id`) VALUES (1);", Ct);
        await fixture.ExecuteAsync("INSERT INTO `md_link` (`p_id`, `x`) VALUES (1, 2);", Ct);

        var (result, script, _) = await SyncAsync(
            (entities, _) =>
            {
                var link = entities.Single(e => e.TableName == "md_link");
                link.PrimaryKeyColumnIds = [link.Columns[1].Id, link.Columns[0].Id];
            },
            item => item.Kind == SchemaDiffKind.AlterPrimaryKey
        );

        result.Committed.Should().BeTrue($"{result.Error}{System.Environment.NewLine}{script}");

        var live = await ImportAsync();
        live.Entities.Single(e => e.TableName == "md_link")
            .GetPrimaryKeyColumnsInOrder()
            .Select(c => c.Name)
            .Should()
            .Equal("x", "p_id");
        live.Relationships.Should().ContainSingle().Which.ConstraintName.Should().Be("fk_link");
    }

    /// <summary>
    /// 主キー列が AUTO_INCREMENT の表の主キーを付け替える同期が成功し、AUTO_INCREMENT が外れることを検証する
    /// （素の DROP PRIMARY KEY はエラー 1075 になる。AUTO_INCREMENT は図が持たない属性なので付け直さない）。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] MySQL 同期: AUTO_INCREMENT の主キーを付け替えられ、AUTO_INCREMENT は外れる"
    )]
    public async Task AlterPrimaryKey_OnAutoIncrementColumn_SucceedsAndRemovesAutoIncrement()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ExecuteAsync(
            "CREATE TABLE `md_ai` (`id` int NOT NULL AUTO_INCREMENT COMMENT 'surrogate', "
                + "`code` varchar(20) NOT NULL, PRIMARY KEY (`id`));",
            Ct
        );
        await fixture.ExecuteAsync("INSERT INTO `md_ai` (`code`) VALUES ('A');", Ct);

        var (result, script, _) = await SyncAsync(
            (entities, _) =>
            {
                var table = entities.Single(e => e.TableName == "md_ai");
                table.Columns.Single(c => c.Name == "id").IsPrimaryKey = false;
                table.Columns.Single(c => c.Name == "code").IsPrimaryKey = true;
                table.PrimaryKeyColumnIds = [];
            },
            item => item.Kind == SchemaDiffKind.AlterPrimaryKey
        );

        script.Should().Contain("-- Note: AUTO_INCREMENT on an old primary key column is removed");
        result.Committed.Should().BeTrue($"{result.Error}{System.Environment.NewLine}{script}");

        var live = await ImportAsync();
        live.Entities.Single(e => e.TableName == "md_ai")
            .GetPrimaryKeyColumnsInOrder()
            .Select(c => c.Name)
            .Should()
            .Equal("code");

        // AUTO_INCREMENT だけが外れ、説明と NOT NULL は残る
        await using var conn = new MySqlConnection(fixture.ConnectionString);
        await conn.OpenAsync(Ct);
        await using var cmd = new MySqlCommand(
            "SELECT EXTRA, IS_NULLABLE, COLUMN_COMMENT FROM information_schema.COLUMNS "
                + "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'md_ai' AND COLUMN_NAME = 'id';",
            conn
        );
        await using var reader = await cmd.ExecuteReaderAsync(Ct);
        (await reader.ReadAsync(Ct)).Should().BeTrue();
        reader.GetString(0).Should().NotContain("auto_increment");
        reader.GetString(1).Should().Be("NO");
        reader.GetString(2).Should().Be("surrogate");
    }

    /// <summary>
    /// 列の型だけを変える同期は、説明の変更を選んでいなければ DB の現在の説明を保つことを検証する
    /// （図の説明が空でも、MODIFY が説明を消さない）。
    /// </summary>
    [Fact(DisplayName = "[Integration] MySQL 同期: 列の型だけを変える同期は DB の説明を保つ")]
    public async Task AlterColumn_TypeOnly_KeepsLiveComment()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ExecuteAsync(
            "CREATE TABLE `md_cmt` (`id` int NOT NULL, `name` varchar(50) NOT NULL COMMENT 'customer name', "
                + "PRIMARY KEY (`id`));",
            Ct
        );

        var (result, script, _) = await SyncAsync(
            (entities, _) =>
            {
                var name = entities.Single(e => e.TableName == "md_cmt").Columns[1];
                name.DataType = "varchar(100)";
                name.Description = string.Empty;
            },
            item => item.Kind == SchemaDiffKind.AlterColumn
        );

        result.Committed.Should().BeTrue($"{result.Error}{System.Environment.NewLine}{script}");
        var name = (await ImportAsync())
            .Entities.Single(e => e.TableName == "md_cmt")
            .Columns.Single(c => c.Name == "name");
        name.DataType.Should().Be("varchar(100)");
        name.Description.Should().Be("customer name");
    }

    /// <summary>
    /// 参照される親の表だけ・外部キーの子列だけを消すとき、外部キーの削除（既定で未選択）を選ばなくても成功し、
    /// 残った子の表から外部キーが外れていることを検証する（MySQL は DDL を暗黙コミットするので、失敗すると
    /// 前の文が適用されたまま止まる）。
    /// </summary>
    [Theory(
        DisplayName = "[Integration] MySQL 同期: 親の表・外部キーの列だけを消しても、端の消える外部キーを外して成功する"
    )]
    [InlineData(SchemaDiffKind.DropTable)]
    [InlineData(SchemaDiffKind.DropColumn)]
    public async Task DropReferencedEnd_WithoutForeignKeyDrop_Succeeds(SchemaDiffKind dropKind)
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);
        await CreateParentChildAsync();

        var (result, script, plan) = await SyncAsync(
            (entities, relationships) =>
            {
                relationships.Clear();

                if (dropKind == SchemaDiffKind.DropTable)
                {
                    entities.RemoveAll(e => e.TableName == "md_parent");
                }
                else
                {
                    entities
                        .Single(e => e.TableName == "md_child")
                        .Columns.RemoveAll(c => c.Name == "parent_id");
                }
            },
            item => item.Kind == dropKind
        );

        plan.Warnings.Should().BeEmpty();
        result.Committed.Should().BeTrue($"{result.Error}{System.Environment.NewLine}{script}");
        var live = await ImportAsync();
        live.Relationships.Should().BeEmpty();
        live.Entities.Should().Contain(e => e.TableName == "md_child");
    }

    /// <summary>
    /// 名前付き一意制約の列を変えた図は、既定の選択のままでは何も変えず（以前は同じ名前の追加が既定で選ばれ、
    /// 重複名で失敗した）、作り直しの項目を選べば同じ名前のまま新しい列で作り直されることを検証する。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] MySQL 同期: 名前付き一意制約の列の変更は、既定では何もせず、選べば同じ名前で作り直す"
    )]
    public async Task UniqueConstraintColumnsChanged_AlterUniqueConstraint()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ExecuteAsync(
            "CREATE TABLE `md_item` (`id` int NOT NULL, `a` int NOT NULL, `b` int NOT NULL, "
                + "PRIMARY KEY (`id`), UNIQUE KEY `uq_md` (`a`));",
            Ct
        );

        static void Widen(List<Entity> entities, List<Relationship> _)
        {
            var item = entities.Single(e => e.TableName == "md_item");
            item.UniqueConstraints.Single().ColumnIds =
            [
                item.Columns.Single(c => c.Name == "a").Id,
                item.Columns.Single(c => c.Name == "b").Id,
            ];
        }

        var (defaultResult, defaultScript, defaultPlan) = await SyncAsync(
            Widen,
            item => item.IsSelected
        );

        defaultPlan.IsEmpty.Should().BeTrue();
        defaultResult
            .Committed.Should()
            .BeTrue($"{defaultResult.Error}{System.Environment.NewLine}{defaultScript}");

        var (result, script, _) = await SyncAsync(
            Widen,
            item => item.Kind == SchemaDiffKind.AlterUniqueConstraint
        );

        result.Committed.Should().BeTrue($"{result.Error}{System.Environment.NewLine}{script}");
        var item = (await ImportAsync()).Entities.Single(e => e.TableName == "md_item");
        var unique = item.UniqueConstraints.Should().ContainSingle().Which;
        unique.Name.Should().Be("uq_md");
        unique
            .ColumnIds.Select(id => item.Columns.Single(c => c.Id == id).Name)
            .Should()
            .Equal("a", "b");
    }

    /// <summary>
    /// 外部キーを支える唯一の索引が一意制約の索引のとき、その一意制約を消す同期が、外部キーを外して付け直して
    /// 成功することを検証する（素の <c>DROP INDEX</c> はエラー 1553 になり、前の文が適用されたまま止まっていた）。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] MySQL 同期: 外部キーを支える一意制約を消しても外部キーを保って成功する"
    )]
    public async Task DropUniqueConstraintSupportingForeignKey_Succeeds()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ExecuteAsync(
            "CREATE TABLE `md_parent` (`id` int NOT NULL, PRIMARY KEY (`id`));",
            Ct
        );
        await fixture.ExecuteAsync(
            "CREATE TABLE `md_child` (`id` int NOT NULL, `parent_id` int NOT NULL, PRIMARY KEY (`id`), "
                + "UNIQUE KEY `uq_md` (`parent_id`), "
                + "CONSTRAINT `fk_md` FOREIGN KEY (`parent_id`) REFERENCES `md_parent` (`id`));",
            Ct
        );

        var (result, script, _) = await SyncAsync(
            (entities, _) =>
                entities.Single(e => e.TableName == "md_child").UniqueConstraints.Clear(),
            item => item.Kind == SchemaDiffKind.DropUniqueConstraint
        );

        result.Committed.Should().BeTrue($"{result.Error}{System.Environment.NewLine}{script}");
        var live = await ImportAsync();
        live.Entities.Single(e => e.TableName == "md_child").UniqueConstraints.Should().BeEmpty();
        live.Relationships.Should().ContainSingle().Which.ConstraintName.Should().Be("fk_md");
    }

    /// <summary>
    /// 外部キーを外したとき、同じ名前の索引をほかの外部キーが支えに使っていれば、その索引は外さずに成功することを
    /// 検証する。同じ列に外部キーを 2 本張ると、MySQL は 1 つの自動索引を共有させ、その索引の名前を後に張った
    /// 外部キー（fk_second）の名前へ付け替える。fk_second を外した後にその索引まで外すと、fk_first が使っているので
    /// エラー 1553 になる。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] MySQL 同期: 外部キーの削除は、ほかの外部キーが使う同じ名前の索引を残して成功する"
    )]
    public async Task DropForeignKey_KeepsIndexUsedByAnotherForeignKey()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ExecuteAsync(
            "CREATE TABLE `md_parent` (`id` int NOT NULL, PRIMARY KEY (`id`));",
            Ct
        );
        await fixture.ExecuteAsync(
            "CREATE TABLE `md_other` (`id` int NOT NULL, PRIMARY KEY (`id`));",
            Ct
        );
        await fixture.ExecuteAsync(
            "CREATE TABLE `md_child` (`id` int NOT NULL, `parent_id` int NOT NULL, PRIMARY KEY (`id`), "
                + "CONSTRAINT `fk_first` FOREIGN KEY (`parent_id`) REFERENCES `md_parent` (`id`));",
            Ct
        );
        await fixture.ExecuteAsync(
            "ALTER TABLE `md_child` ADD CONSTRAINT `fk_second` FOREIGN KEY (`parent_id`) REFERENCES `md_other` (`id`);",
            Ct
        );

        var (result, script, _) = await SyncAsync(
            (_, relationships) => relationships.RemoveAll(r => r.ConstraintName == "fk_second"),
            item => item.Kind == SchemaDiffKind.DropForeignKey
        );

        result.Committed.Should().BeTrue($"{result.Error}{System.Environment.NewLine}{script}");
        (await ImportAsync())
            .Relationships.Should()
            .ContainSingle()
            .Which.ConstraintName.Should()
            .Be("fk_first");
    }

    /// <summary>
    /// 外部キーの子列だけを別の列へ変える作り直しが成功することを検証する。MySQL が外部キーのために作った
    /// 同じ名前の索引が外部キーを外した後も残り、同じ名前で付け直すとエラー 1061 になって外部キーが消えたまま残っていた。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] MySQL 同期: 外部キーの子列を別の列へ変える作り直しが成功する"
    )]
    public async Task AlterForeignKeyToOtherColumn_Succeeds()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ExecuteAsync(
            "CREATE TABLE `md_parent` (`id` int NOT NULL, PRIMARY KEY (`id`));",
            Ct
        );
        await fixture.ExecuteAsync(
            "CREATE TABLE `md_child` (`id` int NOT NULL, `a_id` int NOT NULL, `b_id` int NOT NULL, "
                + "PRIMARY KEY (`id`), "
                + "CONSTRAINT `fk_md` FOREIGN KEY (`a_id`) REFERENCES `md_parent` (`id`));",
            Ct
        );
        await fixture.ExecuteAsync("INSERT INTO `md_parent` (`id`) VALUES (1);", Ct);
        await fixture.ExecuteAsync(
            "INSERT INTO `md_child` (`id`, `a_id`, `b_id`) VALUES (1, 1, 1);",
            Ct
        );

        var (result, script, _) = await SyncAsync(
            (entities, relationships) =>
            {
                var child = entities.Single(e => e.TableName == "md_child");
                var pair = relationships.Single().ColumnPairs[0];
                relationships.Single().ColumnPairs =
                [
                    new(pair.SourceColumnId, child.Columns.Single(c => c.Name == "b_id").Id),
                ];
            },
            item => item.Kind == SchemaDiffKind.AlterForeignKey
        );

        result.Committed.Should().BeTrue($"{result.Error}{System.Environment.NewLine}{script}");
        var live = await ImportAsync();
        var fk = live.Relationships.Should().ContainSingle().Which;
        fk.ConstraintName.Should().Be("fk_md");
        var child = live.Entities.Single(e => e.TableName == "md_child");
        child
            .Columns.Single(c => c.Id == fk.ColumnPairs.Single().TargetColumnId)
            .Name.Should()
            .Be("b_id");
    }

    /// <summary>
    /// 同じ列に外部キーが 2 本あり、片方だけが同期後に型が食い違うとき、どの外部キーも外さず
    /// （列の型変更が必ず失敗するため）、実行しても何も変わらないことを検証する。
    /// 同じ列の外部キー（fk_parent）だけでなく、残した外部キーが参照する親の列を参照する別の表の外部キー（fk_item）も
    /// 外さない（親の列の変更も残した外部キーに阻まれて失敗し、外したものが付け直されないまま消えていた）。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] MySQL 同期: 同じ列の外部キー 2 本の片方だけ型が食い違うと、どの外部キーも外さず何も変えない"
    )]
    public async Task ForeignKeysOnSameColumn_OneMismatched_ChangesNothing()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ExecuteAsync(
            "CREATE TABLE `md_parent` (`id` int NOT NULL, PRIMARY KEY (`id`));",
            Ct
        );
        await fixture.ExecuteAsync(
            "CREATE TABLE `md_other` (`id` int NOT NULL, PRIMARY KEY (`id`));",
            Ct
        );
        await fixture.ExecuteAsync(
            "CREATE TABLE `md_child` (`id` int NOT NULL, `parent_id` int NOT NULL, PRIMARY KEY (`id`), "
                + "CONSTRAINT `fk_parent` FOREIGN KEY (`parent_id`) REFERENCES `md_parent` (`id`), "
                + "CONSTRAINT `fk_other` FOREIGN KEY (`parent_id`) REFERENCES `md_other` (`id`));",
            Ct
        );
        await fixture.ExecuteAsync(
            "CREATE TABLE `md_item` (`id` int NOT NULL, `parent_id` int NOT NULL, PRIMARY KEY (`id`), "
                + "CONSTRAINT `fk_item` FOREIGN KEY (`parent_id`) REFERENCES `md_parent` (`id`));",
            Ct
        );

        var (result, script, plan) = await SyncAsync(
            (entities, _) =>
            {
                entities.Single(e => e.TableName == "md_parent").Columns[0].DataType = "bigint";

                foreach (var table in new[] { "md_child", "md_item" })
                {
                    entities
                        .Single(e => e.TableName == table)
                        .Columns.Single(c => c.Name == "parent_id")
                        .DataType = "bigint";
                }
            },
            item => item.Kind == SchemaDiffKind.AlterColumn
        );

        plan.Warnings.Should()
            .ContainSingle(w => w.Kind == SyncPlanWarningKind.ForeignKeyColumnTypeMismatch)
            .Which.Detail.Should()
            .Be("fk_other");
        script.Should().NotContain("DROP FOREIGN KEY");
        result.Committed.Should().BeFalse();

        var live = await ImportAsync();
        live.Relationships.Select(r => r.ConstraintName)
            .Should()
            .BeEquivalentTo(["fk_parent", "fk_other", "fk_item"]);
        live.Entities.Single(e => e.TableName == "md_parent")
            .Columns[0]
            .DataType.Should()
            .Be("int");
    }

    // ---------------- ヘルパー ----------------

    /// <summary>親 md_parent(id int) と、外部キー fk_md で参照する子 md_child(id, parent_id int) を作る</summary>
    private async Task CreateParentChildAsync()
    {
        await fixture.ExecuteAsync(
            "CREATE TABLE `md_parent` (`id` int NOT NULL, PRIMARY KEY (`id`));",
            Ct
        );
        await fixture.ExecuteAsync(
            "CREATE TABLE `md_child` (`id` int NOT NULL, `parent_id` int NOT NULL, PRIMARY KEY (`id`), "
                + "CONSTRAINT `fk_md` FOREIGN KEY (`parent_id`) REFERENCES `md_parent` (`id`));",
            Ct
        );
        await fixture.ExecuteAsync("INSERT INTO `md_parent` (`id`) VALUES (1);", Ct);
        await fixture.ExecuteAsync("INSERT INTO `md_child` (`id`, `parent_id`) VALUES (1, 1);", Ct);
    }

    /// <summary>
    /// live を取り込み、GUI と同じく別 Guid 空間の図を作って <paramref name="editTarget"/> で変え、
    /// 選んだ差分だけで計画 → スクリプト → 実行まで通す。
    /// </summary>
    private async Task<(SchemaSyncResult Result, string Script, SyncPlan Plan)> SyncAsync(
        System.Action<List<Entity>, List<Relationship>> editTarget,
        System.Func<SchemaDiffItem, bool> select
    )
    {
        var live = await ImportAsync();

        var entityIdMap = new Dictionary<System.Guid, System.Guid>();
        var columnIdMap = new Dictionary<System.Guid, System.Guid>();
        var targetEntities = new List<Entity>();

        foreach (var entity in live.Entities)
        {
            var clone = entity.Clone(preserveId: false);
            entityIdMap[entity.Id] = clone.Id;

            for (var i = 0; i < entity.Columns.Count; i++)
            {
                columnIdMap[entity.Columns[i].Id] = clone.Columns[i].Id;
            }

            targetEntities.Add(clone);
        }

        var targetRelationships = live
            .Relationships.Select(r => new Relationship
            {
                SourceEntityId = entityIdMap[r.SourceEntityId],
                TargetEntityId = entityIdMap[r.TargetEntityId],
                Type = r.Type,
                ColumnPairs = [.. r.ColumnPairs.Select(p => p.Clone(columnIdMap))],
                ConstraintName = r.ConstraintName,
                OnDelete = r.OnDelete,
                OnUpdate = r.OnUpdate,
            })
            .ToList();

        editTarget(targetEntities, targetRelationships);

        var diff = new SchemaDiffService().Compute(
            live.Entities,
            live.Relationships,
            targetEntities,
            targetRelationships,
            _provider.SyncCapabilities
        );

        foreach (var item in diff.Items)
        {
            item.IsSelected = select(item);
        }

        var plan = new SyncPlanner().BuildPlan(
            diff.Items,
            _provider.SyncCapabilities,
            new SyncPlanContext
            {
                LiveEntities = live.Entities,
                LiveRelationships = live.Relationships,
            }
        );
        var script = _provider.SyncScriptBuilder.Build(plan);
        var result = await _provider.SyncExecutor.ExecuteAsync(
            fixture.ToDbConnectionSettings(),
            script,
            Ct
        );

        return (result, script, plan);
    }

    /// <summary>取り込んだ外部キーの列ペアを、取り込んだ並びの（親の列, 子の列）で返す</summary>
    private static IEnumerable<(string Parent, string Child)> ForeignKeyPairNames(
        SchemaImportResult live,
        string constraintName
    )
    {
        var fk = live.Relationships.Single(r => r.ConstraintName == constraintName);
        var parent = live.Entities.Single(e => e.Id == fk.SourceEntityId);
        var child = live.Entities.Single(e => e.Id == fk.TargetEntityId);

        return fk.ColumnPairs.Select(p =>
            (
                parent.Columns.Single(c => c.Id == p.SourceColumnId).Name,
                child.Columns.Single(c => c.Id == p.TargetColumnId).Name
            )
        );
    }

    /// <summary>表の行数を数える</summary>
    private async Task<long> CountRowsAsync(string table)
    {
        await using var connection = await fixture.OpenConnectionAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM `{table}`;";
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    /// <summary>
    /// 列ペアの並びが参照先の主キーの並びと違う複合外部キーの DDL が、キーの並びへ並べ替えて出るので適用でき
    /// （図の並びのままだと MySQL は外部キーを作れない）、取り込み直しても差分が出ないことを検証する。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] MySQL: 列ペアの並びが参照先のキーと違う複合外部キーの DDL は適用でき、取り込み直しても差分が出ない"
    )]
    public async Task CompositeForeignKeyInOtherOrder_DdlAppliesAndLeavesNoDiff()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        var parent = new Entity { TableName = "ko_parent" };
        parent.Columns.Add(
            new Column
            {
                Name = "a",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        parent.Columns.Add(
            new Column
            {
                Name = "b",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        var child = new Entity { TableName = "ko_child" };
        child.Columns.Add(
            new Column
            {
                Name = "id",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        child.Columns.Add(
            new Column
            {
                Name = "x",
                DataType = "int",
                IsNullable = false,
            }
        );
        child.Columns.Add(
            new Column
            {
                Name = "y",
                DataType = "int",
                IsNullable = false,
            }
        );
        // 宣言は (b → y, a → x)＝主キー (a, b) の並びと逆
        var diagram = new ErDiagram
        {
            Entities = [parent, child],
            Relationships =
            [
                new Relationship
                {
                    SourceEntityId = parent.Id,
                    TargetEntityId = child.Id,
                    ColumnPairs =
                    [
                        new(parent.Columns[1].Id, child.Columns[2].Id),
                        new(parent.Columns[0].Id, child.Columns[1].Id),
                    ],
                    ConstraintName = "FK_ko_child",
                },
            ],
        };

        await fixture.ExecuteAsync(new MySqlDdlGenerator().Build(diagram), Ct);

        var live = await ImportAsync();
        live.Relationships.Should().ContainSingle();
        new SchemaDiffService()
            .Compute(
                live.Entities,
                live.Relationships,
                diagram.Entities,
                diagram.Relationships,
                _provider.SyncCapabilities
            )
            .Items.Should()
            .BeEmpty();
    }

    /// <summary>コンテナの DB からスキーマを取り込む</summary>
    private Task<SchemaImportResult> ImportAsync() =>
        _provider.SchemaImporter.ImportAsync(fixture.ConnectionString, 120, Ct);
}
