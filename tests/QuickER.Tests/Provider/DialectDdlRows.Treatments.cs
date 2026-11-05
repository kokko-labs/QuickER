using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Tests.TestSupport;
using static QuickER.Tests.Provider.DdlModel;
using E = QuickER.Tests.Provider.DialectDdlElements;
using F = QuickER.Tests.Provider.DialectDdlFacets;
using Ft = QuickER.Dialects.UnsupportedFeature;
using K = QuickER.Dialects.UnsupportedDefinitionKind;
using O = QuickER.Tests.Provider.DialectDdlOutputs;
using P = QuickER.Tests.Provider.DialectDdlPaths;
using Tm = QuickER.Dialects.UnsupportedTreatment;

namespace QuickER.Tests.Provider;

/// <summary>
/// 扱い（図で表せない定義を外す・告げる）の行：計算列・採番列・外部キーの注記・説明・一意制約・スキーマ修飾名・多対多・
/// 型の表記の拒否（主題ごとに <c>DialectDdlRows.Treatments.*.cs</c> へ分ける）
/// </summary>
internal static partial class DialectDdlRows
{
    /// <summary>扱いの行すべて</summary>
    private static IEnumerable<DialectDdlRow> TreatmentRows() =>
        [
            .. ComputedColumnRows(),
            .. IdentityColumnRows(),
            .. ForeignKeyNoteRows(),
            .. DescriptionTreatmentRows(),
            .. UniqueOnPrimaryKeyRows(),
            .. MiscTreatmentRows(),
        ];

    /// <summary>扱いのマスを、扱いごとにまとめた方言の組で言う（方言ごとに手で書く。宣言と照合する）</summary>
    private static DialectDdlRow TreatsGroups(
        this DialectDdlRow row,
        K kind,
        Ft feature,
        params (Tm Treatment, string[] Dialects)[] groups
    ) =>
        row.Treats(
            kind,
            feature,
            [.. groups.SelectMany(g => g.Dialects.Select(d => (d, g.Treatment)))]
        );

    /// <summary>5 方言に同じ扱いを言う</summary>
    private static DialectDdlRow TreatsAll(
        this DialectDdlRow row,
        K kind,
        Ft feature,
        Tm treatment
    ) => row.TreatsGroups(kind, feature, (treatment, [Ss, Pg, My, Or, Sl]));

    /// <summary>注記の接頭辞だけを見る SQL の項目（中立の能力で計画を作る）</summary>
    private static DdlSyncItems TrNeutral(params SchemaDiffItem[] items) =>
        new(items, TestDialectCapabilities.Neutral);

    // ======================================================================
    // 計算列
    // ======================================================================

    /// <summary>計算列 1 本を持つ（または持たない）1 テーブル</summary>
    private static Entity TrComputedEntity(bool computed) =>
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
                    Name = "total",
                    DataType = "decimal(21,2)",
                    IsNullable = true,
                    IsComputed = computed,
                },
            },
        };

    /// <summary>計算列の列の追加の項目</summary>
    private static SchemaDiffItem TrComputedAddColumn(bool computed) =>
        new()
        {
            Kind = SchemaDiffKind.AddColumn,
            TableName = "items",
            Column = new Column
            {
                Name = "total",
                DataType = "decimal(21,2)",
                IsNullable = true,
                IsComputed = computed,
            },
            IsSelected = true,
        };

    /// <summary>計算列の注意（NULL 許容の計算列の 1 文。文面の正本は製品だが、期待は英語の文をここに書き下ろす）</summary>
    private const string TrComputedNullableNote =
        "-- Computed column 'total': the expression is not part of the diagram; add it to the schema by hand.";

    /// <summary>NOT NULL の計算列 period_start の注意（2 文＋INSERT の失敗の追記）</summary>
    private const string TrComputedNotNullNote =
        "-- Computed column 'period_start': the expression is not part of the diagram; add it to the schema by hand."
        + " Until the expression is added, generated INSERT statements leave this column out"
        + " and fail on its NOT NULL constraint.";

    private static IEnumerable<DialectDdlRow> ComputedColumnRows() =>
        [
            DialectDdlRow
                .Ddl(
                    "ddl-computed-column-note-precedes-column-line",
                    "DDL: 5 方言とも計算列の直前に注意コメントを出す（NULL 許容は 1 文だけ）",
                    E.Computed,
                    F.Note,
                    _ => Diagram(TrComputedEntity(computed: true))
                )
                .ExpectAll(e =>
                    e.Sequence(
                            O.Lines,
                            "note computed-column | " + TrComputedNullableNote,
                            "column items.total *"
                        )
                        .NoText("*-- Computed column 'item_id'*")
                        // NULL 許容の計算列には NOT NULL 向けの追記が混ざらない
                        .NoText("*fail on its NOT NULL constraint*")
                )
                .TreatsAll(K.ComputedColumn, Ft.Ddl, Tm.Annotation),
            DialectDdlRow
                .Ddl(
                    "ddl-computed-column-absent-no-note",
                    "DDL: 計算列が無ければ注意コメントは出ない",
                    E.Computed,
                    F.Note,
                    _ => Diagram(TrComputedEntity(computed: false))
                )
                .ExpectAll(e => e.NoText("*-- Computed column*")),
            DialectDdlRow
                .Ddl(
                    "ddl-computed-column-not-null-warns-insert-failure",
                    "DDL: NOT NULL の計算列にだけ INSERT 失敗の追記が出る（2 文の完全一致）",
                    E.Computed,
                    F.Note,
                    _ =>
                        Diagram(
                            Table(
                                "temporal_items",
                                Pk("id", "int"),
                                new Column
                                {
                                    Name = "period_start",
                                    DataType = "datetime2",
                                    IsNullable = false,
                                    IsComputed = true,
                                }
                            )
                        )
                )
                .ExpectAll(e =>
                    e.Lines("note computed-column | " + TrComputedNotNullNote)
                        .Text("*fail on its NOT NULL constraint*")
                ),
            DialectDdlRow
                .Ddl(
                    "ddl-computed-column-nullable-primary-key-warns-insert-failure",
                    "DDL: NULL 許容と記された主キーの計算列も INSERT が失敗することを告げる",
                    E.Computed,
                    F.Note,
                    _ =>
                        Diagram(
                            Table(
                                "items",
                                new Column
                                {
                                    Name = "period_start",
                                    DataType = "datetime2",
                                    IsPrimaryKey = true,
                                    IsNullable = true,
                                    IsComputed = true,
                                }
                            )
                        )
                )
                .ExpectAll(e => e.Text("*fail on its NOT NULL constraint*")),
            DialectDdlRow
                .SyncItems(
                    "sync-add-table-computed-column-note",
                    "同期 AddTable: 逐次 4 方言とも計算列の直前に注意コメントを出す",
                    P.SyncAddTable,
                    E.Computed,
                    F.Note,
                    _ =>
                        TrNeutral(
                            new SchemaDiffItem
                            {
                                Kind = SchemaDiffKind.AddTable,
                                TableName = "items",
                                Entity = TrComputedEntity(computed: true),
                                IsSelected = true,
                            }
                        )
                )
                .Expect(
                    [Ss, Pg, My, Or],
                    e => e.Lines("note computed-column | -- Computed column 'total'*")
                )
                .TreatsGroups(K.ComputedColumn, Ft.Sync, (Tm.Annotation, [Ss, Pg, My, Or])),
            DialectDdlRow
                .SyncItems(
                    "sync-add-column-computed-column-note-before-alter-table",
                    "同期 AddColumn: 5 方言とも計算列の注意コメントを ALTER TABLE の直前に出す",
                    P.SyncAddColumn,
                    E.Computed,
                    F.Note,
                    _ => TrNeutral(TrComputedAddColumn(computed: true))
                )
                .ExpectAll(e => e.Sequence(O.Text, "-- Computed column 'total'*", "ALTER TABLE*"))
                .TreatsAll(K.ComputedColumn, Ft.Sync, Tm.Annotation),
            DialectDdlRow
                .SyncItems(
                    "sync-add-column-non-computed-column-no-note",
                    "同期 AddColumn: 計算列でなければ注意コメントは出ない",
                    P.SyncAddColumn,
                    E.Computed,
                    F.Note,
                    _ => TrNeutral(TrComputedAddColumn(computed: false))
                )
                .ExpectAll(e => e.NoText("*-- Computed column*")),
            DialectDdlRow
                .SyncPlan(
                    "sync-rebuild-computed-column-note",
                    "同期 SQLite 再構築: 計算列の直前に注意コメントを出す",
                    E.Computed,
                    F.Note,
                    _ => new SyncPlan
                    {
                        Rebuilds =
                        [
                            new TableRebuildPlan
                            {
                                TableName = "items",
                                NewDefinition = TrComputedEntity(computed: true),
                                CreateOnly = true,
                            },
                        ],
                    }
                )
                .Expect(Sl, e => e.Lines("note computed-column | -- Computed column 'total'*"))
                .TreatsGroups(K.ComputedColumn, Ft.Sync, (Tm.Annotation, [Sl])),
            DialectDdlRow
                .SyncPlan(
                    "sync-rebuild-non-computed-column-no-note",
                    "同期 SQLite 再構築: 計算列が無ければ注意コメントは出ない",
                    E.Computed,
                    F.Note,
                    _ => new SyncPlan
                    {
                        Rebuilds =
                        [
                            new TableRebuildPlan
                            {
                                TableName = "items",
                                NewDefinition = TrComputedEntity(computed: false),
                                CreateOnly = true,
                            },
                        ],
                    }
                )
                .Expect(Sl, e => e.NoText("*-- Computed column*")),
        ];

    // ======================================================================
    // 採番列
    // ======================================================================

    /// <summary>採番列 1 本を持つ（または持たない）1 テーブル</summary>
    private static Entity TrIdentityEntity(
        bool identity,
        bool nullable = true,
        bool computed = false
    ) =>
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

    /// <summary>採番列の列の追加の項目</summary>
    private static SchemaDiffItem TrIdentityAddColumn(bool identity, bool computed = false) =>
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

    /// <summary>NULL 許容の採番列の注記（1 文）</summary>
    private const string TrIdentityNullableNote =
        "-- Identity column 'seq': the database assigns its values, but the diagram does not record"
        + " the identity definition; this creates a plain column. Add the identity property by hand.";

    private static IEnumerable<DialectDdlRow> IdentityColumnRows() =>
        [
            DialectDdlRow
                .Ddl(
                    "ddl-identity-column-note-precedes-column-line",
                    "DDL: SQL Server・PostgreSQL・Oracle は採番列の直前に注記を出し、MySQL・SQLite は出さない",
                    E.Identity,
                    F.Note,
                    _ => Diagram(TrIdentityEntity(identity: true))
                )
                .Expect(
                    [Ss, Pg, Or],
                    e =>
                        e.Sequence(
                                O.Lines,
                                "note identity-column | -- Identity column 'seq'*",
                                "column items.seq *"
                            )
                            .NoText("*-- Identity column 'item_id'*")
                            // NULL 許容の採番列には NOT NULL 向けの追記が混ざらない
                            .NoText("*fail on its NOT NULL constraint*")
                )
                // NULL 許容の採番列の注記は 1 文だけ（SQL Server の DDL で完全一致）
                .Expect(Ss, e => e.Lines("note identity-column | " + TrIdentityNullableNote))
                .Expect([My, Sl], e => e.NoText("*-- Identity column*"))
                .TreatsGroups(
                    K.IdentityRejectingValues,
                    Ft.Ddl,
                    (Tm.Annotation, [Ss, Pg, Or]),
                    (Tm.None, [My, Sl])
                ),
            DialectDdlRow
                .Ddl(
                    "ddl-identity-column-absent-no-note",
                    "DDL: 採番列が無ければ注記は出ない",
                    E.Identity,
                    F.Note,
                    _ => Diagram(TrIdentityEntity(identity: false))
                )
                .ExpectAll(e => e.NoText("*-- Identity column*")),
            DialectDdlRow
                .Ddl(
                    "ddl-identity-column-not-null-warns-insert-failure",
                    "DDL: 主キーでない NOT NULL の採番列は、生成コードの INSERT が失敗することも告げる",
                    E.Identity,
                    F.Note,
                    _ => Diagram(TrIdentityEntity(identity: true, nullable: false))
                )
                .Expect([Ss, Pg, Or], e => e.Text("*fail on its NOT NULL constraint*")),
            DialectDdlRow
                .Ddl(
                    "ddl-identity-column-primary-key-has-no-insert-failure-sentence",
                    "DDL: 主キーの採番列は生成コードが値を送るので、INSERT が失敗する文を足さない",
                    E.Identity,
                    F.Note,
                    _ =>
                        Diagram(
                            Table(
                                "items",
                                new Column
                                {
                                    Name = "id",
                                    DataType = "int",
                                    IsPrimaryKey = true,
                                    IsNullable = false,
                                    IsIdentity = true,
                                }
                            )
                        )
                )
                .Expect(
                    Pg,
                    e =>
                        e.Lines("note identity-column | -- Identity column 'id'*")
                            .NoText("*fail on its NOT NULL constraint*")
                ),
            DialectDdlRow
                .SyncItems(
                    "sync-add-table-identity-column-note",
                    "同期 AddTable: SQL Server・PostgreSQL・Oracle は採番列に注記を出し、MySQL は出さない",
                    P.SyncAddTable,
                    E.Identity,
                    F.Note,
                    _ =>
                        TrNeutral(
                            new SchemaDiffItem
                            {
                                Kind = SchemaDiffKind.AddTable,
                                TableName = "items",
                                Entity = TrIdentityEntity(identity: true),
                                IsSelected = true,
                            }
                        )
                )
                .Expect([Ss, Pg, Or], e => e.Text("*-- Identity column 'seq'*"))
                .Expect(My, e => e.NoText("*-- Identity column*"))
                .TreatsGroups(
                    K.IdentityRejectingValues,
                    Ft.Sync,
                    (Tm.Annotation, [Ss, Pg, Or]),
                    (Tm.None, [My])
                ),
            DialectDdlRow
                .SyncItems(
                    "sync-add-column-identity-column-note-before-alter-table",
                    "同期 AddColumn: SQL Server・PostgreSQL・Oracle は ALTER TABLE の直前に注記を出し、MySQL・SQLite は出さない",
                    P.SyncAddColumn,
                    E.Identity,
                    F.Note,
                    _ => TrNeutral(TrIdentityAddColumn(identity: true))
                )
                .Expect(
                    [Ss, Pg, Or],
                    e => e.Sequence(O.Text, "-- Identity column 'seq'*", "ALTER TABLE*")
                )
                .Expect([My, Sl], e => e.NoText("*-- Identity column*"))
                .TreatsGroups(
                    K.IdentityRejectingValues,
                    Ft.Sync,
                    (Tm.Annotation, [Ss, Pg, Or]),
                    (Tm.None, [My, Sl])
                ),
            DialectDdlRow
                .SyncItems(
                    "sync-add-column-non-identity-column-no-note",
                    "同期 AddColumn: 採番列でなければ注記は出ない",
                    P.SyncAddColumn,
                    E.Identity,
                    F.Note,
                    _ => TrNeutral(TrIdentityAddColumn(identity: false))
                )
                .ExpectAll(e => e.NoText("*-- Identity column*")),
            DialectDdlRow
                .SyncPlan(
                    "sync-rebuild-identity-column-no-note",
                    "同期 SQLite 再構築: 採番列に注記は出さない（扱いの表が「なし」）",
                    E.Identity,
                    F.Note,
                    _ => new SyncPlan
                    {
                        Rebuilds =
                        [
                            new TableRebuildPlan
                            {
                                TableName = "items",
                                NewDefinition = TrIdentityEntity(identity: true),
                                CreateOnly = true,
                            },
                        ],
                    }
                )
                .Expect(Sl, e => e.NoText("*-- Identity column*"))
                .TreatsGroups(K.IdentityRejectingValues, Ft.Sync, (Tm.None, [Sl])),
            DialectDdlRow
                .SyncItems(
                    "sync-add-column-computed-identity-column-both-notes",
                    "同期 AddColumn: 計算列でもある採番列は、計算列の注記の直後に採番列の注記を出し、その次が ALTER TABLE",
                    P.SyncAddColumn,
                    E.Identity,
                    F.Order,
                    _ => TrNeutral(TrIdentityAddColumn(identity: true, computed: true))
                )
                .Expect(
                    [Ss, Pg, Or],
                    e =>
                        e.Sequence(
                            O.Text,
                            "-- Computed column 'seq'*",
                            "-- Identity column 'seq'*",
                            "ALTER TABLE*"
                        )
                ),
        ];
}
