using System;
using System.Linq;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.SqlServer;
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.Provider;

/// <summary><see cref="SyncPlanner"/> が差分項目から組み立てる実行計画（選択フィルタ・固定順序・グループ化）を検証する</summary>
public class SyncPlannerTests
{
    // ---------------- テーブル再構築（rebuild）方言の集約 ----------------

    /// <summary>ALTER COLUMN も FK の後付けもできない再構築方言のケーパビリティ</summary>
    private static readonly SyncDialectCapabilities RebuildCaps =
        TestDialectCapabilities.Neutral with
        {
            SupportsAlterColumn = false,
            SupportsForeignKeyAlter = false,
            SupportsDescriptions = false,
            PersistsForeignKeyConstraintNames = false,
            ColumnReorder = ColumnReorderMode.Rebuild,
        };

    /// <summary>単純な非 NULL 主キー列 id を生成する</summary>
    private static Column PkId() =>
        new()
        {
            Name = "id",
            DataType = "INT",
            IsPrimaryKey = true,
            IsNullable = false,
        };

    /// <summary>NULL 許容の通常列を生成する</summary>
    private static Column Col(string name, string type) =>
        new()
        {
            Name = name,
            DataType = type,
            IsNullable = true,
        };

    /// <summary>AlterPrimaryKey 差分項目を生成する（target＝新しい主キー構成の源）</summary>
    private static SchemaDiffItem AlterPk(string table, Entity target, bool selected = true) =>
        new()
        {
            Kind = SchemaDiffKind.AlterPrimaryKey,
            TableName = table,
            Entity = target,
            IsSelected = selected,
        };

    // ---------------- 主キー変更の 3 フェーズ出力（レンダラー経由で順序を固定する） ----------------

    /// <summary>
    /// 主キー変更と旧主キー列の NULL 許容化（AlterColumn）を同時に選択したとき、生成スクリプト上で
    /// 「主キー解除 → 列定義変更 → 主キー付与」の順になることを SQL Server レンダラー経由で検証する。
    /// </summary>
    /// <remarks>
    /// DROP と ADD を 1 セクションから連続出力する単一フェーズでは、主キー制約が残ったまま旧主キー列を
    /// NULL 許容へ変更することになり SQL Server では Msg 5074 → 4922 で失敗する（PostgreSQL / MySQL /
    /// Oracle も同種のエラーで、MySQL / Oracle は部分適用のまま止まる）。順序が唯一の解のためテストで固定する。
    /// </remarks>
    [Fact(
        DisplayName = "主キー変更＋旧 PK 列の NULL 許容化は PK DROP → ALTER COLUMN → PK ADD の順"
    )]
    public void AlterPrimaryKey_WithAlterColumn_RendersDropThenAlterThenAdd()
    {
        // 旧主キー列 old_id を NULL 許容へ変更しつつ、主キーを code へ移す
        var target = new Entity
        {
            TableName = "T",
            Columns =
            {
                Col("old_id", "int"),
                new Column
                {
                    Name = "code",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            },
        };
        var alterColumn = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AlterColumn,
            TableName = "T",
            ColumnName = "old_id",
            Column = Col("old_id", "int"),
            IsSelected = true,
        };

        var plan = new SyncPlanner().BuildPlan(
            [AlterPk("T", target), alterColumn],
            TestDialectCapabilities.Neutral
        );
        var sql = new SqlServerSyncScriptBuilder().Build(plan);

        var dropPrimaryKey = sql.IndexOf(
            "DROP CONSTRAINT ' + QUOTENAME(@pk);",
            StringComparison.Ordinal
        );
        var alterColumnAt = sql.IndexOf("ALTER COLUMN [old_id]", StringComparison.Ordinal);
        var addPrimaryKey = sql.IndexOf("ADD CONSTRAINT [PK_T]", StringComparison.Ordinal);

        dropPrimaryKey.Should().BeGreaterThan(-1);
        alterColumnAt.Should().BeGreaterThan(dropPrimaryKey);
        addPrimaryKey.Should().BeGreaterThan(alterColumnAt);
    }

    /// <summary>
    /// 主キー変更を含まない計画の出力にフェーズ表記が混ざらないことを固定する
    /// （フェーズを持たないセクションの見出しは種別名のみ。計画のセクションにフェーズが付かないことは同期計画の期待の行
    /// <c>plan-without-pk-change-has-no-phase</c> が見る）。
    /// </summary>
    [Fact(DisplayName = "主キー変更を含まない計画にフェーズ表記は出ない")]
    public void PlanWithoutPrimaryKeyChange_RendersUnchangedBytes()
    {
        var addColumn = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AddColumn,
            TableName = "T",
            ColumnName = "Email",
            Column = new Column
            {
                Name = "Email",
                DataType = "nvarchar(200)",
                IsNullable = false,
            },
            IsSelected = true,
        };
        var alterColumn = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AlterColumn,
            TableName = "T",
            ColumnName = "Memo",
            // 文字列型でない列の NULL 許容だけの変更＝照合順序も DEFAULT 制約も読み直さない素の ALTER COLUMN になる
            Column = Col("Memo", "int"),
            OldColumn = new Column
            {
                Name = "Memo",
                DataType = "int",
                IsNullable = false,
            },
            IsSelected = true,
        };

        var plan = new SyncPlanner().BuildPlan(
            [alterColumn, addColumn],
            TestDialectCapabilities.Neutral
        );
        var sql = new SqlServerSyncScriptBuilder().Build(plan);

        var expected = string.Join(
            Environment.NewLine,
            "-- ===== AddColumn (1 items) =====",
            "ALTER TABLE [T] ADD [Email] nvarchar(200) NOT NULL;",
            "GO",
            "",
            "-- ===== AlterColumn (1 items) =====",
            "ALTER TABLE [T] ALTER COLUMN [Memo] int NULL;",
            "GO",
            "",
            ""
        );

        sql.Should().Be(expected);
    }

    // ---------------- 一意制約（UNIQUE）の同期 ----------------

    /// <summary>エンティティへ一意制約を足す（構成列は列名で引き当てる）</summary>
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

    // ---------------- 外部キーの両端の型・参照先のキーの並び（純粋な関数） ----------------

    /// <summary>外部キーの両端に並べられる型の判定（長さだけの違いを許すのは同じ文字列型で、括弧の後ろの指定も同じとき）</summary>
    [Theory(DisplayName = "外部キーの両端の型: 長さの違いを許すのは同じ文字列型どうしだけ")]
    [InlineData("varchar(50)", "VARCHAR(100)", true)]
    [InlineData("char(2)", "char(10)", true)]
    [InlineData("varbinary(16)", "varbinary(32)", true)]
    [InlineData("varchar(50)", "char(50)", false)]
    [InlineData("varchar(50) CHARACTER SET latin1", "varchar(100)", false)]
    [InlineData("int", "bigint", false)]
    [InlineData("decimal(10,2)", "decimal(12,2)", false)]
    [InlineData("nchar(2)", "NCHAR(4)", true)]
    [InlineData("nvarchar(10)", "nvarchar(20)", true)]
    [InlineData("VARCHAR2(50 CHAR)", "varchar2(100 char)", true)]
    [InlineData("NVARCHAR2(10)", "NVARCHAR2(20)", true)]
    [InlineData("RAW(16)", "RAW(32)", true)]
    [InlineData("VARCHAR2(50 CHAR)", "VARCHAR2(100 BYTE)", false)]
    [InlineData("VARCHAR2(50)", "VARCHAR2(100 BYTE)", false)]
    [InlineData("VARCHAR2(10)", "NVARCHAR2(10)", false)]
    public void AreForeignKeyCompatibleTypes_AllowsStringLengthOnly(
        string left,
        string right,
        bool expected
    )
    {
        var lenient = TestDialectCapabilities.Neutral with
        {
            ForeignKeyAllowsStringLengthDifference = true,
        };

        lenient.AreForeignKeyCompatibleTypes(left, right).Should().Be(expected);
        TestDialectCapabilities
            .Neutral.AreForeignKeyCompatibleTypes(left, right)
            .Should()
            .Be(left.Equals(right, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 外部キーを張ったままの列の型の変更を DB が受けるかの判定（宣言 <see cref="SyncDialectCapabilities.ForeignKeyColumnWidening"/> の 3 つの値と、
    /// 列の最大バイト数の事実）
    /// </summary>
    /// <remarks>
    /// 製品の計画が判定を通すのは、列の変更で外部キーを外し直す暗黙コミットの方言（MySQL＝<c>WithinLengthPrefix</c>）の前寄せだけで、Refused・
    /// Accepted と事実の無い列の腕は実方言の計画からは届かない（SQL Server はトランザクションの方言なので前寄せしない）。腕ごとの答えをここで
    /// 固定する。MySQL の腕の計画の側は、形の行の水準（<c>DivergentEndsStringLength*</c>・台帳の項目 pe1）が網と実 DB で見る。
    /// </remarks>
    [Theory(
        DisplayName = "外部キーを張ったまま列を伸ばす変更: 宣言の 3 つの値と列の最大バイト数で判定する"
    )]
    // Refused：どの変更も受けない
    [InlineData(ForeignKeyColumnWidening.Refused, "varchar(20)", "varchar(30)", 80L, false)]
    // Accepted：同じ可変長の型で長さだけを伸ばす変更を、事実が無くても受ける
    [InlineData(ForeignKeyColumnWidening.Accepted, "varchar(20)", "varchar(30)", null, true)]
    [InlineData(
        ForeignKeyColumnWidening.Accepted,
        "VARCHAR2(20 CHAR)",
        "varchar2(300 char)",
        null,
        true
    )]
    [InlineData(ForeignKeyColumnWidening.Accepted, "varbinary(16)", "varbinary(32)", null, true)]
    [InlineData(ForeignKeyColumnWidening.Accepted, "varchar(30)", "varchar(20)", null, false)]
    [InlineData(ForeignKeyColumnWidening.Accepted, "varchar(20)", "varchar(20)", null, false)]
    [InlineData(ForeignKeyColumnWidening.Accepted, "char(20)", "char(30)", null, false)]
    [InlineData(ForeignKeyColumnWidening.Accepted, "varchar(20)", "nvarchar(30)", null, false)]
    [InlineData(ForeignKeyColumnWidening.Accepted, "varchar(20)", "char(30)", null, false)]
    [InlineData(
        ForeignKeyColumnWidening.Accepted,
        "VARCHAR2(20 CHAR)",
        "VARCHAR2(30 BYTE)",
        null,
        false
    )]
    [InlineData(ForeignKeyColumnWidening.Accepted, "int", "bigint", null, false)]
    [InlineData(ForeignKeyColumnWidening.Accepted, "varchar(max)", "varchar(max)", null, false)]
    // WithinLengthPrefix：1 単位のバイト数は「今の最大バイト数 ÷ 今の長さ」。長さの頭（255 バイト以下は 1 バイト・256 以上は 2 バイト）が同じなら受ける
    [InlineData(
        ForeignKeyColumnWidening.WithinLengthPrefix,
        "varchar(20)",
        "varchar(30)",
        80L,
        true
    )]
    [InlineData(
        ForeignKeyColumnWidening.WithinLengthPrefix,
        "varchar(20)",
        "varchar(63)",
        80L,
        true
    )]
    [InlineData(
        ForeignKeyColumnWidening.WithinLengthPrefix,
        "varchar(20)",
        "varchar(64)",
        80L,
        false
    )]
    [InlineData(
        ForeignKeyColumnWidening.WithinLengthPrefix,
        "varchar(64)",
        "varchar(100)",
        256L,
        true
    )]
    [InlineData(
        ForeignKeyColumnWidening.WithinLengthPrefix,
        "varchar(40)",
        "varchar(70)",
        40L,
        true
    )]
    [InlineData(
        ForeignKeyColumnWidening.WithinLengthPrefix,
        "varchar(40)",
        "varchar(70)",
        160L,
        false
    )]
    [InlineData(
        ForeignKeyColumnWidening.WithinLengthPrefix,
        "varchar(200)",
        "varchar(255)",
        200L,
        true
    )]
    [InlineData(
        ForeignKeyColumnWidening.WithinLengthPrefix,
        "varchar(200)",
        "varchar(256)",
        200L,
        false
    )]
    [InlineData(
        ForeignKeyColumnWidening.WithinLengthPrefix,
        "varbinary(100)",
        "varbinary(200)",
        100L,
        true
    )]
    // WithinLengthPrefix の保守側：事実の無い列・割り切れない事実・文字集合を変える変更・縮める変更は受けない
    [InlineData(
        ForeignKeyColumnWidening.WithinLengthPrefix,
        "varchar(20)",
        "varchar(30)",
        null,
        false
    )]
    [InlineData(
        ForeignKeyColumnWidening.WithinLengthPrefix,
        "varchar(20)",
        "varchar(30)",
        81L,
        false
    )]
    [InlineData(
        ForeignKeyColumnWidening.WithinLengthPrefix,
        "varchar(20)",
        "varchar(30) CHARACTER SET latin1",
        80L,
        false
    )]
    [InlineData(
        ForeignKeyColumnWidening.WithinLengthPrefix,
        "varchar(40) CHARACTER SET latin1",
        "varchar(70) CHARACTER SET latin1",
        40L,
        true
    )]
    [InlineData(
        ForeignKeyColumnWidening.WithinLengthPrefix,
        "varchar(30)",
        "varchar(20)",
        120L,
        false
    )]
    public void WidensForeignKeyColumnInPlace_FollowsDeclarationAndByteLength(
        ForeignKeyColumnWidening widening,
        string oldType,
        string newType,
        long? oldOctetLength,
        bool expected
    )
    {
        var capabilities = TestDialectCapabilities.Neutral with
        {
            ForeignKeyColumnWidening = widening,
        };

        capabilities
            .WidensForeignKeyColumnInPlace(oldType, newType, oldOctetLength)
            .Should()
            .Be(expected);
    }

    /// <summary>外部キーの両端に並べられる型の判定（幅だけの違いを許すのは整数型どうしで、Oracle は小数部の無い NUMBER）</summary>
    [Theory(DisplayName = "外部キーの両端の型: 幅の違いを許すのは整数型どうしだけ")]
    [InlineData("int", "bigint", true)]
    [InlineData("SMALLINT", "integer", true)]
    [InlineData("int4", "int8", true)]
    [InlineData("NUMBER(10)", "NUMBER(19)", true)]
    [InlineData("NUMBER(10,0)", "NUMBER(19)", true)]
    [InlineData("NUMBER(*,0)", "NUMBER(10)", true)]
    [InlineData("NUMBER(10)", "NUMBER(12,2)", false)]
    [InlineData("NUMBER", "NUMBER(10)", false)]
    [InlineData("NUMBER(*)", "NUMBER(10)", false)]
    [InlineData("int", "decimal(10,0)", false)]
    [InlineData("real", "double precision", false)]
    [InlineData("int", "varchar(10)", false)]
    [InlineData("bigint unsigned", "bigint", false)]
    public void AreForeignKeyCompatibleTypes_AllowsIntegerWidthOnly(
        string left,
        string right,
        bool expected
    )
    {
        var lenient = TestDialectCapabilities.Neutral with
        {
            ForeignKeyAllowsIntegerWidthDifference = true,
        };

        lenient.AreForeignKeyCompatibleTypes(left, right).Should().Be(expected);
        TestDialectCapabilities
            .Neutral.AreForeignKeyCompatibleTypes(left, right)
            .Should()
            .Be(left.Equals(right, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 参照先に主キーと同じ列の集合で並びの違う一意制約があるとき、DDL の <see cref="ForeignKeyColumnOrder.ArrangeToReferencedKey"/>
    /// が外部キーの列ペアを主キーの並びへ並べ替える（主キーを先に試す）ことを検証する
    /// （同期の計画が同じ並びで出すことは同期計画の期待の行 <c>plan-arrange-add-fk-pk-and-unique-on-same-columns-follows-pk</c> が見る。
    /// 一意制約を先に試すと、同期と DDL で同じ外部キーの並びが食い違う）。
    /// </summary>
    [Fact(
        DisplayName = "並べ替え: 主キーと同じ列の集合の一意制約があっても、外部キーの追加は主キーの並びで出す"
    )]
    public void AddForeignKey_PrimaryKeyAndUniqueOnSameColumns_FollowsPrimaryKey()
    {
        // pair(a, b) の複合主キーと、同じ列の集合で並びの違う一意制約 (b, a)
        var pair = new Entity
        {
            TableName = "pair",
            Columns =
            {
                new Column
                {
                    Name = "a",
                    DataType = "INT",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "b",
                    DataType = "INT",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            },
        };
        WithUnique(pair, "UQ_pair_b_a", "b", "a");
        ForeignKeyColumnNamePair[] pairs = [new("b", "xb"), new("a", "xa")];

        ForeignKeyColumnOrder
            .ArrangeToReferencedKey(pairs, pair)
            .Select(p => (p.ParentColumn, p.ChildColumn))
            .Should()
            .Equal(("a", "xa"), ("b", "xb"));
    }

    // ---------------- 大文字小文字だけ違う live テーブルの引き当て ----------------

    /// <summary>
    /// live に大文字小文字だけ違うテーブルが共存し、名前が曖昧で再構築しなかった項目を、レンダラーがスキップとして明示することを
    /// 検証する（引き当て＝序数一致の 1 件を優先し、曖昧なら再構築せず項目をセクションへ残すことは同期計画の期待の行
    /// <c>plan-rebuild-case-only-different-live-tables-*</c> が見る）。
    /// </summary>
    [Fact(
        DisplayName = "再構築: 大文字小文字だけ違う live テーブルは序数一致で引き、曖昧なら再構築せずスキップを明示する"
    )]
    public void Rebuild_CaseOnlyDifferentLiveTables_ExactOrSkipped()
    {
        var upper = new Entity { TableName = "Dup", Columns = { PkId(), Col("note", "TEXT") } };
        var lower = new Entity { TableName = "dup", Columns = { PkId(), Col("note", "TEXT") } };
        var context = new SyncPlanContext { LiveEntities = [upper, lower] };

        // 曖昧: 再構築せず、項目はセクションへ残ってスキップのコメントになる
        var ambiguous = new SyncPlanner().BuildPlan(
            [
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AlterColumn,
                    TableName = "DUP",
                    ColumnName = "note",
                    Column = Col("note", "INTEGER"),
                    IsSelected = true,
                },
            ],
            RebuildCaps,
            context
        );

        var script = new QuickER.Provider.Sqlite.SqliteSyncScriptBuilder().Build(ambiguous);
        script.Should().Contain("-- Skipped 'AlterColumn' on DUP");
    }
}
