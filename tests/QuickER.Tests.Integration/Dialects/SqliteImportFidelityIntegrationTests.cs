using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Provider;
using QuickER.Provider.Sqlite;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// SQLite の取込忠実性（仮想テーブル・付属表（シャドウテーブル）の除外）の統合テスト。
/// </summary>
/// <remarks>
/// <see cref="SqliteDdlRoundTripIntegrationTests"/> が「QuickER が作った DDL が往復するか」を見るのに対し、
/// こちらは「QuickER が作ったのではない実 DB」（FTS5 / R*Tree の仮想テーブルを含む）を取り込んだときに
/// 何が落ちるかを固定する。SQLite はインプロセス（Microsoft.Data.Sqlite）のため Docker を使わず、
/// CI（windows-latest・Docker なし）でも常時実行される。
/// </remarks>
[Trait("Category", "Integration")]
public sealed class SqliteImportFidelityIntegrationTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>
    /// FTS5 仮想テーブルは本体・付属表（シャドウテーブル）ともに取り込まれず、
    /// 本体 1 件だけの <see cref="SchemaImportWarningKind.VirtualTableExcluded"/> 警告が出る。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] FTS5 仮想テーブルは本体・付属表ともに取り込まれず警告 1 件が出る"
    )]
    public async Task Import_Fts5VirtualTable_ExcludedWithSingleWarning()
    {
        using var db = SqliteTempDatabase.Create();

        await db.ApplyDdlAsync(
            @"
CREATE TABLE customers (
    id INTEGER PRIMARY KEY,
    name TEXT NOT NULL
);
CREATE VIRTUAL TABLE docs USING fts5(title, body);
",
            Ct
        );

        await using var conn = await db.OpenReadOnlyConnectionAsync(Ct);
        var result = await new SqliteSchemaImporter().ImportAsync(conn, Ct);

        // 通常テーブルだけが載る＝仮想テーブル本体（docs）・付属表（docs_data 等）は 1 つも載らない
        result.Entities.Select(e => e.TableName).Should().BeEquivalentTo(["customers"]);

        // 付属表の分の警告は出ず、本体 1 件だけが代表する
        result
            .Warnings.Should()
            .ContainSingle(w => w.Kind == SchemaImportWarningKind.VirtualTableExcluded)
            .Which.TableName.Should()
            .Be("docs");
    }

    /// <summary>
    /// R*Tree 仮想テーブルも FTS5 と同じ規則で除外される。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] R*Tree 仮想テーブルは本体・付属表ともに取り込まれず警告 1 件が出る"
    )]
    public async Task Import_RTreeVirtualTable_ExcludedWithSingleWarning()
    {
        using var db = SqliteTempDatabase.Create();

        await db.ApplyDdlAsync(
            @"
CREATE TABLE customers (
    id INTEGER PRIMARY KEY,
    name TEXT NOT NULL
);
CREATE VIRTUAL TABLE geo USING rtree(id, minx, maxx, miny, maxy);
",
            Ct
        );

        await using var conn = await db.OpenReadOnlyConnectionAsync(Ct);
        var result = await new SqliteSchemaImporter().ImportAsync(conn, Ct);

        result.Entities.Select(e => e.TableName).Should().BeEquivalentTo(["customers"]);

        result
            .Warnings.Should()
            .ContainSingle(w => w.Kind == SchemaImportWarningKind.VirtualTableExcluded)
            .Which.TableName.Should()
            .Be("geo");
    }

    /// <summary>
    /// FTS5 / R*Tree が同じ DB に混在しても、それぞれ独立に本体だけが名指しされる
    /// （通常テーブル・外部キーを持つ図の取込フローが仮想テーブルの混在で崩れないことも兼ねて確認する）。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] 仮想テーブルが複数・通常テーブルの FK と混在しても各本体だけが警告される"
    )]
    public async Task Import_MultipleVirtualTablesWithOrdinaryForeignKey_ExcludesEachBodyOnly()
    {
        using var db = SqliteTempDatabase.Create();

        await db.ApplyDdlAsync(
            @"
CREATE TABLE customers (
    id INTEGER PRIMARY KEY,
    name TEXT NOT NULL
);
CREATE TABLE notes (
    id INTEGER PRIMARY KEY,
    customer_id INTEGER NOT NULL REFERENCES customers(id),
    body TEXT
);
CREATE VIRTUAL TABLE docs USING fts5(title, body);
CREATE VIRTUAL TABLE geo USING rtree(id, minx, maxx, miny, maxy);
CREATE INDEX idx_customers_name ON customers(name);
",
            Ct
        );

        await using var conn = await db.OpenReadOnlyConnectionAsync(Ct);
        var result = await new SqliteSchemaImporter().ImportAsync(conn, Ct);

        // 仮想テーブル本体・付属表は 1 つも通常テーブルに混ざらない
        result.Entities.Select(e => e.TableName).Should().BeEquivalentTo(["customers", "notes"]);

        // 通常テーブルの FK・インデックスは仮想テーブルの混在に影響されず取り込まれる
        result.Relationships.Should().ContainSingle();
        result
            .AuxiliaryObjects.Should()
            .Contain(a => a.Name == "idx_customers_name" && a.TableName == "customers");

        // 警告は仮想テーブル本体 2 件のみ（付属表 8 個分は 1 つも積まれない）
        result.Warnings.Should().HaveCount(2);
        result
            .Warnings.Where(w => w.Kind == SchemaImportWarningKind.VirtualTableExcluded)
            .Select(w => w.TableName)
            .Should()
            .BeEquivalentTo(["docs", "geo"]);
    }

    /// <summary>通常テーブルだけの DB では警告 0 件・従来と同一の取込結果になる（回帰なし）</summary>
    [Fact(DisplayName = "[Integration] 仮想テーブルが無い DB では警告が出ない（回帰なし）")]
    public async Task Import_NoVirtualTables_NoWarnings()
    {
        using var db = SqliteTempDatabase.Create();

        await db.ApplyDdlAsync(
            @"
CREATE TABLE customers (
    id INTEGER PRIMARY KEY,
    name TEXT NOT NULL
);
",
            Ct
        );

        await using var conn = await db.OpenReadOnlyConnectionAsync(Ct);
        var result = await new SqliteSchemaImporter().ImportAsync(conn, Ct);

        result.Entities.Select(e => e.TableName).Should().BeEquivalentTo(["customers"]);
        result.Warnings.Should().BeEmpty();
    }

    /// <summary>
    /// 生成列（<c>GENERATED ALWAYS AS</c>）が取り込まれ、
    /// <see cref="QuickER.Model.Column.IsComputed"/> つきで報告されることを検証する。
    /// </summary>
    /// <remarks>
    /// <c>PRAGMA table_info</c> は生成列を 1 行も返さない（実測）。列挙を <c>table_xinfo</c> へ替える前は
    /// 生成列が<b>列ごと図から消えて</b>いた。式は PRAGMA から取れないため警告の <c>Detail</c> は空になる。
    /// </remarks>
    [Fact(
        DisplayName = "[Integration] SQLite: 生成列は IsComputed つきで取り込み、式の喪失を報告する"
    )]
    public async Task Import_GeneratedColumn_IsMarkedAndWarns()
    {
        using var db = SqliteTempDatabase.Create();

        await db.ApplyDdlAsync(
            @"
CREATE TABLE invoices (
    invoice_id INTEGER PRIMARY KEY,
    qty INTEGER NOT NULL,
    price REAL NOT NULL,
    total_stored REAL GENERATED ALWAYS AS (qty * price) STORED,
    total_virtual REAL GENERATED ALWAYS AS (qty * price) VIRTUAL
);
",
            Ct
        );

        await using var conn = await db.OpenReadOnlyConnectionAsync(Ct);
        var result = await new SqliteSchemaImporter().ImportAsync(conn, Ct);

        var entity = result.Entities.Should().ContainSingle().Subject;

        // 生成列も列として取り込まれる（table_info では 1 行も返らず、丸ごと消えていた）
        entity
            .Columns.Select(c => c.Name)
            .Should()
            .Equal("invoice_id", "qty", "price", "total_stored", "total_virtual");
        entity
            .Columns.Where(c => c.IsComputed)
            .Select(c => c.Name)
            .Should()
            .Equal("total_stored", "total_virtual");
        entity.Columns.Single(c => c.Name == "qty").IsComputed.Should().BeFalse();

        var warnings = result
            .Warnings.Where(w => w.Kind == SchemaImportWarningKind.ComputedColumnExpressionLost)
            .ToList();
        warnings.Select(w => w.Subject).Should().Equal("total_stored", "total_virtual");
        warnings.Should().OnlyContain(w => w.TableName == "invoices");
        // SQLite は PRAGMA から式を取れない＝Detail は空（表示側が式なしの文面へ振り分ける）
        warnings.Should().OnlyContain(w => w.Detail.Length == 0);
    }

    /// <summary>生成列を持たない DB では計算列の警告が出ないことを検証する（回帰なし）</summary>
    [Fact(DisplayName = "[Integration] SQLite: 生成列が無ければ計算列の警告は出ない")]
    public async Task Import_NoGeneratedColumn_NoComputedWarning()
    {
        using var db = SqliteTempDatabase.Create();

        await db.ApplyDdlAsync(
            "CREATE TABLE plain (id INTEGER PRIMARY KEY, qty INTEGER NOT NULL);",
            Ct
        );

        await using var conn = await db.OpenReadOnlyConnectionAsync(Ct);
        var result = await new SqliteSchemaImporter().ImportAsync(conn, Ct);

        result.Entities.Single().Columns.Should().OnlyContain(c => !c.IsComputed);
        result.Warnings.Should().BeEmpty();
    }

    /// <summary>
    /// テーブル名にドットを含む DB でも取込が例外にならない（SL1）。
    /// </summary>
    /// <remarks>
    /// PRAGMA の引数へ <see cref="SqliteIdentifier.Quote"/>（ドット分割クォート）を渡すと
    /// <c>PRAGMA table_xinfo("a"."b")</c> のように誤って 2 分割クォートされ、取込全体が例外で失敗していた。
    /// PRAGMA の引数は「ドットも含めた 1 つの識別子」のため <see cref="SqliteIdentifier.QuoteSimple"/> を使う。
    /// </remarks>
    [Fact(
        DisplayName = "[Integration] SQLite: テーブル名にドットを含む DB でも取込が例外にならない"
    )]
    public async Task Import_TableNameContainingDot_DoesNotThrow()
    {
        using var db = SqliteTempDatabase.Create();

        await db.ApplyDdlAsync(
            @"
CREATE TABLE ""a.b"" (
    id INTEGER PRIMARY KEY,
    name TEXT NOT NULL
);
CREATE TABLE customers (
    id INTEGER PRIMARY KEY,
    name TEXT NOT NULL
);
",
            Ct
        );

        await using var conn = await db.OpenReadOnlyConnectionAsync(Ct);
        var result = await new SqliteSchemaImporter().ImportAsync(conn, Ct);

        result.Entities.Select(e => e.TableName).Should().BeEquivalentTo(["a.b", "customers"]);

        var dotted = result.Entities.Single(e => e.TableName == "a.b");
        dotted.Columns.Select(c => c.Name).Should().Equal("id", "name");
        dotted.GetPrimaryKeyColumnsInOrder().Select(c => c.Name).Should().Equal("id");
        result.Warnings.Should().BeEmpty();
    }

    /// <summary>
    /// 参照先テーブルが実在しない FK は、リレーションを作らず <see cref="SchemaImportWarningKind.ForeignKeyOutsideScope"/>
    /// で告げる（SL4）。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] SQLite: 参照先が実在しない FK は警告を出しリレーションを作らない"
    )]
    public async Task Import_ForeignKeyToMissingTable_WarnsAndExcludesRelationship()
    {
        using var db = SqliteTempDatabase.Create();

        await db.ApplyDdlAsync(
            @"
CREATE TABLE child (
    id INTEGER PRIMARY KEY,
    ghost_id INTEGER REFERENCES ghost(id)
);
",
            Ct
        );

        await using var conn = await db.OpenReadOnlyConnectionAsync(Ct);
        var result = await new SqliteSchemaImporter().ImportAsync(conn, Ct);

        result.Entities.Select(e => e.TableName).Should().BeEquivalentTo(["child"]);
        result.Relationships.Should().BeEmpty();

        var warning = result
            .Warnings.Should()
            .ContainSingle(w => w.Kind == SchemaImportWarningKind.ForeignKeyOutsideScope)
            .Subject;
        warning.TableName.Should().Be("child");
        warning.Detail.Should().Be("ghost");
    }

    /// <summary>
    /// 複合 FK で参照先テーブルが実在しない場合も、構成列ごとにではなく FK 単位で警告 1 件にまとまる（SL4）。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] SQLite: 複合 FK で参照先が実在しない場合も警告は 1 件にまとまる"
    )]
    public async Task Import_CompositeForeignKeyToMissingTable_WarnsOnce()
    {
        using var db = SqliteTempDatabase.Create();

        await db.ApplyDdlAsync(
            @"
CREATE TABLE child (
    a INTEGER NOT NULL,
    b INTEGER NOT NULL,
    PRIMARY KEY (a, b),
    FOREIGN KEY (a, b) REFERENCES ghost(x, y)
);
",
            Ct
        );

        await using var conn = await db.OpenReadOnlyConnectionAsync(Ct);
        var result = await new SqliteSchemaImporter().ImportAsync(conn, Ct);

        result.Relationships.Should().BeEmpty();
        result
            .Warnings.Should()
            .ContainSingle(w => w.Kind == SchemaImportWarningKind.ForeignKeyOutsideScope)
            .Which.TableName.Should()
            .Be("child");
    }

    /// <summary>参照先が実在する FK では警告が出ない（回帰なし）</summary>
    [Fact(
        DisplayName = "[Integration] SQLite: 参照先が実在する FK では ForeignKeyOutsideScope 警告が出ない（回帰なし）"
    )]
    public async Task Import_ForeignKeyToExistingTable_NoOutsideScopeWarning()
    {
        using var db = SqliteTempDatabase.Create();

        await db.ApplyDdlAsync(
            @"
CREATE TABLE customers (
    id INTEGER PRIMARY KEY,
    name TEXT NOT NULL
);
CREATE TABLE orders (
    id INTEGER PRIMARY KEY,
    customer_id INTEGER NOT NULL REFERENCES customers(id)
);
",
            Ct
        );

        await using var conn = await db.OpenReadOnlyConnectionAsync(Ct);
        var result = await new SqliteSchemaImporter().ImportAsync(conn, Ct);

        result.Relationships.Should().ContainSingle();
        result.Warnings.Should().BeEmpty();
    }

    /// <summary>
    /// rowid 表の非 INTEGER 単一列主キーは NOT NULL へ補正され（従来どおり維持）、
    /// 補正したことが <see cref="SchemaImportWarningKind.PrimaryKeyNullabilityAdjusted"/> で報告される（SL3）。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] SQLite: rowid 表の TEXT 主キーは NOT NULL へ補正され警告される"
    )]
    public async Task Import_RowidTableWithTextPrimaryKey_WarnsAndAdjustsToNotNull()
    {
        using var db = SqliteTempDatabase.Create();

        await db.ApplyDdlAsync(
            @"
CREATE TABLE items (
    code TEXT PRIMARY KEY,
    name TEXT
);
",
            Ct
        );

        await using var conn = await db.OpenReadOnlyConnectionAsync(Ct);
        var result = await new SqliteSchemaImporter().ImportAsync(conn, Ct);

        var entity = result.Entities.Should().ContainSingle().Subject;
        var code = entity.Columns.Single(c => c.Name == "code");
        code.IsPrimaryKey.Should().BeTrue();
        // 補正自体は維持する（SQLite 自身は NULL を許すが、意味モデル・GUI は主キーを常に NOT NULL とする）
        code.IsNullable.Should().BeFalse();

        var warning = result
            .Warnings.Should()
            .ContainSingle(w => w.Kind == SchemaImportWarningKind.PrimaryKeyNullabilityAdjusted)
            .Subject;
        warning.TableName.Should().Be("items");
        warning.Subject.Should().Be("code");
    }

    /// <summary>
    /// <c>WITHOUT ROWID</c> 表は SQLite 自身が主キー列への NULL を拒否するため、
    /// 補正したところで実質的な補正にならず、警告が出ない（SL3）。
    /// </summary>
    [Fact(DisplayName = "[Integration] SQLite: WITHOUT ROWID 表の主キーは警告されない")]
    public async Task Import_WithoutRowidTable_DoesNotWarn()
    {
        using var db = SqliteTempDatabase.Create();

        await db.ApplyDdlAsync(
            @"
CREATE TABLE items_wr (
    code TEXT PRIMARY KEY,
    name TEXT
) WITHOUT ROWID;
",
            Ct
        );

        await using var conn = await db.OpenReadOnlyConnectionAsync(Ct);
        var result = await new SqliteSchemaImporter().ImportAsync(conn, Ct);

        var entity = result.Entities.Should().ContainSingle().Subject;
        entity.Columns.Single(c => c.Name == "code").IsNullable.Should().BeFalse();

        result
            .Warnings.Should()
            .NotContain(w => w.Kind == SchemaImportWarningKind.PrimaryKeyNullabilityAdjusted);
    }

    /// <summary>
    /// 単一列 <c>INTEGER PRIMARY KEY</c>（rowid 別名）は NULL を取り得ないため警告されない（SL3・回帰なし）。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] SQLite: 単一列 INTEGER PRIMARY KEY（rowid 別名）は警告されない"
    )]
    public async Task Import_SingleColumnIntegerPrimaryKey_DoesNotWarn()
    {
        using var db = SqliteTempDatabase.Create();

        await db.ApplyDdlAsync(
            @"
CREATE TABLE widgets_alias (
    id INTEGER PRIMARY KEY,
    name TEXT
);
",
            Ct
        );

        await using var conn = await db.OpenReadOnlyConnectionAsync(Ct);
        var result = await new SqliteSchemaImporter().ImportAsync(conn, Ct);

        result
            .Warnings.Should()
            .NotContain(w => w.Kind == SchemaImportWarningKind.PrimaryKeyNullabilityAdjusted);
    }

    /// <summary>
    /// 宣言型 <c>INT</c> は <c>INTEGER</c> と綴りが違うため rowid 別名にならず、
    /// NULL を許す普通の主キーとして警告される（SL3・境界ケース）。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] SQLite: 単一列 INT PRIMARY KEY は rowid 別名にならず警告される"
    )]
    public async Task Import_SingleColumnIntPrimaryKey_Warns()
    {
        using var db = SqliteTempDatabase.Create();

        await db.ApplyDdlAsync(
            @"
CREATE TABLE widgets (
    id INT PRIMARY KEY,
    name TEXT
);
",
            Ct
        );

        await using var conn = await db.OpenReadOnlyConnectionAsync(Ct);
        var result = await new SqliteSchemaImporter().ImportAsync(conn, Ct);

        var warning = result
            .Warnings.Should()
            .ContainSingle(w => w.Kind == SchemaImportWarningKind.PrimaryKeyNullabilityAdjusted)
            .Subject;
        warning.TableName.Should().Be("widgets");
        warning.Subject.Should().Be("id");
    }

    /// <summary>
    /// 複合主キーは rowid 別名になり得ないため、構成列が NULL 許容のままなら列ごとに警告される（SL3）。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] SQLite: NULL 許容の複合主キーは rowid 別名になり得ず列ごとに警告される"
    )]
    public async Task Import_CompositePrimaryKeyWithoutNotNull_WarnsPerColumn()
    {
        using var db = SqliteTempDatabase.Create();

        await db.ApplyDdlAsync(
            @"
CREATE TABLE composite_pk (
    a TEXT,
    b TEXT,
    PRIMARY KEY (a, b)
);
",
            Ct
        );

        await using var conn = await db.OpenReadOnlyConnectionAsync(Ct);
        var result = await new SqliteSchemaImporter().ImportAsync(conn, Ct);

        var warnings = result
            .Warnings.Where(w => w.Kind == SchemaImportWarningKind.PrimaryKeyNullabilityAdjusted)
            .ToList();
        warnings.Should().OnlyContain(w => w.TableName == "composite_pk");
        warnings.Select(w => w.Subject).Should().BeEquivalentTo(["a", "b"]);
    }

    /// <summary>
    /// 構成列が最初から <c>NOT NULL</c> の複合主キーは補正していないため警告されない（SL3・RI1）。
    /// </summary>
    /// <remarks>
    /// 他の DB から移したスキーマでごく普通に現れる形。ここで鳴ると
    /// 「補正した列を名指しする」という警告の意味が失われる。
    /// </remarks>
    [Fact(
        DisplayName = "[Integration] SQLite: 明示 NOT NULL の複合主キーは補正していないため警告されない"
    )]
    public async Task Import_CompositePrimaryKeyDeclaredNotNull_DoesNotWarn()
    {
        using var db = SqliteTempDatabase.Create();

        await db.ApplyDdlAsync(
            @"
CREATE TABLE composite_pk (
    a TEXT NOT NULL,
    b TEXT NOT NULL,
    PRIMARY KEY (a, b)
);
",
            Ct
        );

        await using var conn = await db.OpenReadOnlyConnectionAsync(Ct);
        var result = await new SqliteSchemaImporter().ImportAsync(conn, Ct);

        // 補正自体は従来どおり（意味モデル・GUI は主キーを常に NOT NULL とする）
        var entity = result.Entities.Should().ContainSingle().Subject;
        entity.Columns.Should().OnlyContain(c => !c.IsNullable);

        result
            .Warnings.Should()
            .NotContain(w => w.Kind == SchemaImportWarningKind.PrimaryKeyNullabilityAdjusted);
    }

    /// <summary>
    /// 構成列ごとに宣言が違う複合主キーでは、実際に補正した列だけが警告される（SL3・RI1）。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] SQLite: 複合主キーの警告は NULL 許容だった列だけを名指しする"
    )]
    public async Task Import_CompositePrimaryKeyMixedNullability_WarnsOnlyAdjustedColumn()
    {
        using var db = SqliteTempDatabase.Create();

        await db.ApplyDdlAsync(
            @"
CREATE TABLE mixed_pk (
    a TEXT NOT NULL,
    b TEXT,
    PRIMARY KEY (a, b)
);
",
            Ct
        );

        await using var conn = await db.OpenReadOnlyConnectionAsync(Ct);
        var result = await new SqliteSchemaImporter().ImportAsync(conn, Ct);

        var warnings = result
            .Warnings.Where(w => w.Kind == SchemaImportWarningKind.PrimaryKeyNullabilityAdjusted)
            .ToList();
        warnings.Should().ContainSingle();
        warnings[0].TableName.Should().Be("mixed_pk");
        warnings[0].Subject.Should().Be("b");
    }
}
