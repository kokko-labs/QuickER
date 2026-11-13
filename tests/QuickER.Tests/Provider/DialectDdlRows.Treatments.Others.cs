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
/// 説明（空白だけ・BMP の外の文字）・主キーと同じ列集合の一意制約・スキーマ修飾名・多対多・型の表記の拒否
/// </summary>
internal static partial class DialectDdlRows
{
    /// <summary>空白だけの説明</summary>
    private const string TrBlank = " \t ";

    /// <summary>絵文字（サロゲート対）を 2 つ含む説明</summary>
    private const string TrEmoji = "a\U0001F600b\U0001F600c";

    /// <summary>BMP の外の文字を保存できない方言が説明の前に付ける注記の先頭（英語の文面）</summary>
    private const string TrBmpNote = "*Basic Multilingual Plane*";

    /// <summary>
    /// 説明つきの表 t(id PK, v int NULL)（表と列の説明を同値にする）
    /// </summary>
    private static Entity TrDescribed(
        string? description,
        string idType = "int",
        string? columnDescription = null
    )
    {
        var entity = new Entity { TableName = "t", Description = description! };
        entity.Columns.Add(
            new Column
            {
                Name = "id",
                DataType = idType,
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        entity.Columns.Add(
            new Column
            {
                Name = "v",
                DataType = idType,
                IsNullable = true,
                Description = (columnDescription ?? description)!,
            }
        );

        return entity;
    }

    /// <summary>方言を目標にした 1 表の図（<c>TargetDbms</c> に方言名を入れる）</summary>
    private static ErDiagram TrTargetDiagram(string dialect, Entity entity)
    {
        var diagram = new ErDiagram { TargetDbms = dialect };
        diagram.Entities.Add(entity);
        return diagram;
    }

    /// <summary>表と列の説明の項目 2 つ（新しい値を指定し、古い値は note）</summary>
    private static DdlSyncItems TrDescriptionItems(string newDescription) =>
        new([
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.SetTableDescription,
                TableName = "t",
                NewDescription = newDescription,
                OldDescription = "old",
                IsSelected = true,
            },
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.SetColumnDescription,
                TableName = "t",
                ColumnName = "v",
                NewDescription = newDescription,
                OldDescription = "old",
                IsSelected = true,
            },
        ]);

    /// <summary>
    /// 説明の削除（新しい値が空・空白だけ）の同期スクリプト。空の値のスクリプトと同じことを、
    /// 絶対値の期待で固定する
    /// </summary>
    private static DialectDdlRow TrRemovalExpect(this DialectDdlRow row) =>
        row.Expect(
                Ss,
                e =>
                    e.Exact(
                        O.Lines,
                        "note section | -- ===== SetTableDescription (1 items) =====",
                        "text DECLARE @default_schema sysname = SCHEMA_NAME();",
                        "text IF EXISTS (",
                        "text SELECT 1 FROM sys.extended_properties ep",
                        "text WHERE ep.name = N'MS_Description' AND ep.class = 1",
                        "text AND ep.major_id = OBJECT_ID(N'[t]')",
                        "text AND ep.minor_id = 0)",
                        "text EXEC sys.sp_dropextendedproperty @name=N'MS_Description', @level0type=N'SCHEMA', @level0name=@default_schema, @level1type=N'TABLE',  @level1name=N't';",
                        "text GO",
                        "note section | -- ===== SetColumnDescription (1 items) =====",
                        "text DECLARE @default_schema sysname = SCHEMA_NAME();",
                        "text IF EXISTS (",
                        "text SELECT 1 FROM sys.extended_properties ep",
                        "text WHERE ep.name = N'MS_Description' AND ep.class = 1",
                        "text AND ep.major_id = OBJECT_ID(N'[t]')",
                        "text AND ep.minor_id = COLUMNPROPERTY(OBJECT_ID(N'[t]'), N'v', 'ColumnId'))",
                        "text EXEC sys.sp_dropextendedproperty @name=N'MS_Description', @level0type=N'SCHEMA', @level0name=@default_schema, @level1type=N'TABLE',  @level1name=N't', @level2type=N'COLUMN', @level2name=N'v';",
                        "text GO"
                    )
            )
            .Expect(
                Pg,
                e =>
                    e.Exact(
                        O.Lines,
                        "note section | -- ===== SetTableDescription (1 items) =====",
                        "text COMMENT ON TABLE \"t\" IS NULL;",
                        "note section | -- ===== SetColumnDescription (1 items) =====",
                        "text COMMENT ON COLUMN \"t\".\"v\" IS NULL;"
                    )
            )
            .Expect(
                Or,
                e =>
                    e.Exact(
                        O.Lines,
                        "text COMMENT ON TABLE \"t\" IS '';",
                        "text COMMENT ON COLUMN \"t\".\"v\" IS '';"
                    )
            )
            .Expect(
                My,
                e =>
                    e.Exact(
                        O.Lines,
                        "note section | -- ===== SetTableDescription (1 items) =====",
                        "text ALTER TABLE `t` COMMENT = '';",
                        "note section | -- ===== SetColumnDescription (1 items) =====",
                        "text SET @gen = '';",
                        "text SET @dfx = '';",
                        "text SET @cmt = '';",
                        "text SET @exists = 0;",
                        "text SELECT 1, c.GENERATION_EXPRESSION, IF(INSTR(c.EXTRA, 'DEFAULT_GENERATED') > 0, IFNULL(c.COLUMN_DEFAULT, ''), ''), c.COLUMN_COMMENT",
                        "text INTO @exists, @gen, @dfx, @cmt",
                        "text FROM information_schema.COLUMNS c",
                        "text WHERE c.TABLE_SCHEMA = DATABASE() AND c.TABLE_NAME = 't' AND c.COLUMN_NAME = 'v' LIMIT 1;",
                        "text SET @sql = CONCAT('SELECT ''', IFNULL(@gen, ''), ''', ''', IFNULL(@dfx, ''), ''' INTO @gen, @dfx');",
                        "text PREPARE stmt FROM @sql;",
                        "text EXECUTE stmt;",
                        "text DEALLOCATE PREPARE stmt;",
                        "text SET @def = NULL;",
                        "text SELECT CONCAT(",
                        "text c.COLUMN_TYPE,",
                        "text IF(c.COLLATION_NAME IS NULL, '', CONCAT(' COLLATE ', c.COLLATION_NAME)),",
                        "text IF(@gen = '', '', CONCAT(' GENERATED ALWAYS AS (', @gen, ') ', IF(INSTR(c.EXTRA, 'STORED GENERATED') > 0, 'STORED', 'VIRTUAL'))),",
                        "text IF(c.IS_NULLABLE = 'YES', ' NULL', ' NOT NULL'),",
                        "text IF(c.SRS_ID IS NULL, '', CONCAT(' SRID ', c.SRS_ID)),",
                        "text IF(c.COLUMN_DEFAULT IS NULL, '',",
                        "text IF(c.DATA_TYPE IN ('timestamp', 'datetime') AND (UPPER(c.COLUMN_DEFAULT) = 'CURRENT_TIMESTAMP' OR UPPER(c.COLUMN_DEFAULT) LIKE 'CURRENT\\_TIMESTAMP(%)'), CONCAT(' DEFAULT ', c.COLUMN_DEFAULT),",
                        "text IF(INSTR(c.EXTRA, 'DEFAULT_GENERATED') > 0, CONCAT(' DEFAULT (', @dfx, ')'),",
                        "text IF(c.DATA_TYPE IN ('bit', 'binary', 'varbinary'), CONCAT(' DEFAULT ', c.COLUMN_DEFAULT),",
                        "text CONCAT(' DEFAULT ', QUOTE(c.COLUMN_DEFAULT)))))),",
                        "text IF(INSTR(c.EXTRA, 'INVISIBLE') > 0, ' INVISIBLE', ''),",
                        "text IF(INSTR(c.EXTRA, 'auto_increment') > 0, ' AUTO_INCREMENT', ''),",
                        "text IF(INSTR(c.EXTRA, 'on update ') > 0, CONCAT(' ON UPDATE ', SUBSTRING_INDEX(SUBSTRING(c.EXTRA, INSTR(c.EXTRA, 'on update ') + 10), ' ', 1)), '')",
                        "text ) INTO @def",
                        "text FROM information_schema.COLUMNS c",
                        "text WHERE c.TABLE_SCHEMA = DATABASE() AND c.TABLE_NAME = 't' AND c.COLUMN_NAME = 'v' LIMIT 1;",
                        "text SET @cmt = '';",
                        "text SET @sql = IF(@def IS NOT NULL, CONCAT('ALTER TABLE `t` MODIFY COLUMN `v` ', @def, ' COMMENT ', QUOTE(@cmt)), IF(@exists = 0, 'DO 0', 'QuickER sync: t.v definition could not be rebuilt'));",
                        "text PREPARE stmt FROM @sql;",
                        "text EXECUTE stmt;",
                        "text DEALLOCATE PREPARE stmt;"
                    )
            );

    private static IEnumerable<DialectDdlRow> DescriptionTreatmentRows() =>
        [
            // ---- 空白だけの説明 ----
            DialectDdlRow
                .Ddl(
                    "ddl-description-blank-is-written-as-none",
                    "空白だけの説明: DDL は説明の無い図と同じ（説明を書かない）",
                    E.Description,
                    F.Omit,
                    d => TrTargetDiagram(d, TrDescribed(TrBlank))
                )
                // 説明の無い図の DDL と同じ出力（説明の文も COMMENT も無い）
                .ExpectAll(e =>
                    e.Exact(
                        O.Lines,
                        "header",
                        "generated-at",
                        "table t",
                        "column t.id int NOT NULL,",
                        "column t.v int NULL,",
                        "pk t PK_t (id)",
                        "end-table"
                    )
                ),
            DialectDdlRow
                .Ddl(
                    "ddl-description-text-is-written",
                    "空白だけの説明（対照）: 説明のある図は説明を書く",
                    E.Description,
                    F.StatementShape,
                    d => TrTargetDiagram(d, TrDescribed("note"))
                )
                // 説明を書く文は方言ごとに違う（SQL Server は拡張プロパティ・PostgreSQL と Oracle は COMMENT ON・MySQL は COMMENT 句・SQLite は SQL コメント）
                .Expect(
                    Ss,
                    e =>
                        e.Text(
                            "*sp_addextendedproperty*@value=N'note'*@level1name=N't';",
                            "*sp_addextendedproperty*@value=N'note'*@level2name=N'v';"
                        )
                )
                .Expect(
                    [Pg, Or],
                    e =>
                        e.Text(
                            "COMMENT ON TABLE \"t\" IS 'note';",
                            "COMMENT ON COLUMN \"t\".\"v\" IS 'note';"
                        )
                )
                .Expect(
                    My,
                    e => e.Lines("column t.v int NULL COMMENT 'note',", "end-table COMMENT='note'")
                )
                .Expect(Sl, e => e.Lines("comment -- t: note", "comment -- v: note")),
            DialectDdlRow
                .SyncItems(
                    "sync-other-description-blank-new-value-is-removal",
                    "空白だけの説明: 同期スクリプトは説明の削除と同じ文を書く",
                    P.SyncOther,
                    E.Description,
                    F.Omit,
                    _ => TrDescriptionItems(TrBlank)
                )
                .TrRemovalExpect(),
            DialectDdlRow
                .SyncItems(
                    "sync-other-description-empty-new-value-is-removal",
                    "空白だけの説明: 空の新しい値の説明の項目は説明の削除の文を書く",
                    P.SyncOther,
                    E.Description,
                    F.Omit,
                    _ => TrDescriptionItems(string.Empty)
                )
                .TrRemovalExpect(),
            DialectDdlRow
                .SyncItems(
                    "sync-other-description-text-is-written",
                    "空白だけの説明（対照）: 新しい値のある説明の項目は説明を書く",
                    P.SyncOther,
                    E.Description,
                    F.StatementShape,
                    _ => TrDescriptionItems("note")
                )
                // 新しい値の説明は削除ではなく、説明を書く文になる
                .Expect(
                    Ss,
                    e =>
                        e.Text(
                                "*sp_addextendedproperty*@value=N'note'*@level1name=N't';",
                                "*sp_addextendedproperty*@value=N'note'*@level2name=N'v';"
                            )
                            .NoText("*sp_dropextendedproperty*")
                )
                .Expect(
                    [Pg, Or],
                    e =>
                        e.Text(
                            "COMMENT ON TABLE \"t\" IS 'note';",
                            "COMMENT ON COLUMN \"t\".\"v\" IS 'note';"
                        )
                )
                .Expect(My, e => e.Text("ALTER TABLE `t` COMMENT = 'note';", "SET @cmt = 'note';")),
            DialectDdlRow
                .SyncItems(
                    "sync-add-column-mysql-does-not-write-comment",
                    "MySQL: 列の追加は説明（COMMENT）を書かない",
                    P.SyncAddColumn,
                    E.Description,
                    F.Omit,
                    _ =>
                        TrNeutral(
                            new SchemaDiffItem
                            {
                                Kind = SchemaDiffKind.AddColumn,
                                TableName = "t",
                                ColumnName = "w",
                                Column = new Column
                                {
                                    Name = "w",
                                    DataType = "int",
                                    IsNullable = true,
                                    Description = "note",
                                },
                                IsSelected = true,
                            }
                        )
                )
                .Expect(My, e => e.Text("*ADD COLUMN `w` int NULL;*").NoText("*COMMENT*")),
            // ---- BMP の外の文字の説明 ----
            DialectDdlRow
                .Ddl(
                    "ddl-description-supplementary-character-notes",
                    "説明の BMP の外の文字: DDL は保存できない方言でだけ説明の前に注記する（MySQL は表の文の前と列の定義行の直前）",
                    E.Description,
                    F.Note,
                    d => TrTargetDiagram(d, TrDescribed(TrEmoji))
                )
                .Expect(
                    My,
                    e =>
                        e.Count(O.Text, 2, TrBmpNote)
                            .Order(
                                O.Lines,
                                "note description-characters | *table t*",
                                "note description-characters | *column t.v*"
                            )
                            .Sequence(O.Lines, "note description-characters | *table t*", "table t")
                            .Sequence(
                                O.Lines,
                                "note description-characters | *column t.v*",
                                "column t.v int NULL*"
                            )
                )
                .Expect([Ss, Pg, Or, Sl], e => e.NoText(TrBmpNote)),
            DialectDdlRow
                .Ddl(
                    "ddl-description-bmp-only-no-note",
                    "説明の BMP の外の文字: BMP の中だけの説明は注記しない",
                    E.Description,
                    F.Note,
                    d => TrTargetDiagram(d, TrDescribed("note"))
                )
                .ExpectAll(e => e.NoText(TrBmpNote)),
            DialectDdlRow
                .Ddl(
                    "ddl-description-supplementary-character-notes-dialect-int-types",
                    "説明の BMP の外の文字: 方言ごとの整数型の表でも DDL の注記は保存できない方言の宣言どおり",
                    E.Description,
                    F.Note,
                    d => TrTargetDiagram(d, TrDescribed(TrEmoji, IntType[d]))
                )
                .Expect(My, e => e.Count(O.Text, 2, TrBmpNote))
                .Expect([Ss, Pg, Or, Sl], e => e.NoText(TrBmpNote)),
            DialectDdlRow
                .Ddl(
                    "ddl-description-bmp-only-no-note-dialect-int-types",
                    "説明の BMP の外の文字: BMP の中だけ（é・あ）の説明は方言ごとの整数型の表でも注記しない",
                    E.Description,
                    F.Note,
                    d => TrTargetDiagram(d, TrDescribed("aébあc", IntType[d]))
                )
                .ExpectAll(e => e.NoText(TrBmpNote)),
        ];

    // ======================================================================
    // 主キーと同じ列集合の一意制約
    // ======================================================================

    /// <summary>主キー id と、id の一意制約（主キーと同じ列集合）・code の一意制約を持つ表</summary>
    private static Entity TrItem()
    {
        var entity = new Entity { TableName = "item" };
        entity.Columns.Add(
            new Column
            {
                Name = "id",
                DataType = "INTEGER",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        entity.Columns.Add(
            new Column
            {
                Name = "code",
                DataType = "INTEGER",
                IsNullable = false,
            }
        );
        entity.UniqueConstraints.Add(
            new UniqueConstraint { Name = "UQ_item_id", ColumnIds = [entity.Columns[0].Id] }
        );
        entity.UniqueConstraints.Add(
            new UniqueConstraint { Name = "UQ_item_code", ColumnIds = [entity.Columns[1].Id] }
        );

        return entity;
    }

    /// <summary>
    /// 主キーと同じ列集合の一意制約の省略（持てない方言は省いて注記し、持てる方言は出す。表を作る文がその一意制約を黙って捨てる方言
    /// ＝PostgreSQL は、表を作る文を閉じた直後の ALTER TABLE で足す）
    /// </summary>
    private static DialectDdlRow TrOmissionExpect(this DialectDdlRow row) =>
        row.ExpectAll(e => e.Lines("uq item UQ_item_code (code)*"))
            .Expect(
                [Ss, Pg, My],
                e => e.NoText("*cannot have a unique constraint on the primary key columns*")
            )
            .Expect([Ss, My], e => e.Lines("uq item UQ_item_id (id)*").NoLines("* alter"))
            .Expect(
                [Pg],
                e =>
                    e.Sequence(
                            DialectDdlOutputs.Lines,
                            "uq item UQ_item_code (code)",
                            "end-table",
                            "uq item UQ_item_id (id) alter"
                        )
                        .NoLines("uq item UQ_item_id (id)", "uq item UQ_item_id (id),")
            )
            .Expect(
                [Or, Sl],
                e =>
                    e.Lines(
                            "note unique-on-primary-key | -- Note: this database cannot have a unique constraint on the primary key columns, so UQ_item_id (id) is omitted;*"
                        )
                        .NoLines("uq item UQ_item_id *")
                        .NoText("*UQ_item_id* UNIQUE*")
            );

    private static IEnumerable<DialectDdlRow> UniqueOnPrimaryKeyRows() =>
        [
            DialectDdlRow
                .Ddl(
                    "ddl-unique-on-primary-key-columns-omitted-where-unsupported",
                    "DDL: 主キーと同じ列集合の一意制約は Oracle・SQLite だけが省いて注記する",
                    E.Unique,
                    F.Omit,
                    _ => Diagram(TrItem())
                )
                .TrOmissionExpect()
                .TreatsGroups(
                    K.UniqueConstraintOnPrimaryKey,
                    Ft.Ddl,
                    (Tm.None, [Ss, Pg, My]),
                    (Tm.OmitWithAnnotation, [Or, Sl])
                ),
            DialectDdlRow
                .SyncItems(
                    "sync-add-table-unique-on-primary-key-columns-omitted-where-unsupported",
                    "同期: 表の追加は主キーと同じ列集合の一意制約を Oracle・SQLite だけが省いて注記する",
                    P.SyncAddTable,
                    E.Unique,
                    F.Omit,
                    _ =>
                    {
                        var entity = TrItem();

                        return new DdlSyncItems(
                            [
                                new SchemaDiffItem
                                {
                                    Kind = SchemaDiffKind.AddTable,
                                    TableName = entity.TableName,
                                    Entity = entity,
                                    IsSelected = true,
                                },
                            ],
                            null,
                            new SyncPlanContext()
                        );
                    }
                )
                .TrOmissionExpect()
                .TreatsGroups(
                    K.UniqueConstraintOnPrimaryKey,
                    Ft.Sync,
                    (Tm.None, [Ss, Pg, My]),
                    (Tm.OmitWithAnnotation, [Or, Sl])
                ),
            DialectDdlRow
                .SyncItems(
                    "sync-rebuild-unique-that-becomes-primary-key-omitted",
                    "同期: SQLite の再構築は、主キーと同じ列集合になる live の一意制約を省いて注記する",
                    P.SyncRebuild,
                    E.Unique,
                    F.Omit,
                    _ =>
                    {
                        var live = new Entity { TableName = "item" };
                        live.Columns.Add(
                            new Column
                            {
                                Name = "id",
                                DataType = "INTEGER",
                                IsPrimaryKey = true,
                                IsNullable = false,
                            }
                        );
                        live.Columns.Add(
                            new Column
                            {
                                Name = "code",
                                DataType = "TEXT",
                                IsNullable = false,
                            }
                        );
                        live.UniqueConstraints.Add(
                            new UniqueConstraint
                            {
                                Name = "UQ_item_code",
                                ColumnIds = [live.Columns[1].Id],
                            }
                        );
                        var target = live.Clone(preserveId: true);
                        target.Columns[0].IsPrimaryKey = false;
                        target.Columns[1].IsPrimaryKey = true;

                        return new DdlSyncItems(
                            [
                                new SchemaDiffItem
                                {
                                    Kind = SchemaDiffKind.AlterPrimaryKey,
                                    TableName = "item",
                                    Entity = target,
                                    IsSelected = true,
                                },
                            ],
                            null,
                            new SyncPlanContext { LiveEntities = [live] }
                        );
                    }
                )
                .Expect(
                    Sl,
                    e =>
                        e.Lines(
                                "note unique-on-primary-key | -- Note: this database cannot have a unique constraint on the primary key columns, so UQ_item_code (code) is omitted;*"
                            )
                            .NoText("*UNIQUE (\"code\")*")
                )
                .TreatsGroups(
                    K.UniqueConstraintOnPrimaryKey,
                    Ft.Sync,
                    (Tm.OmitWithAnnotation, [Sl])
                ),
            DialectDdlRow
                .Ddl(
                    "ddl-unique-on-same-columns-as-earlier-unique-added-after-create-table",
                    "DDL: 先に書いた一意制約と同じ列の並びの一意制約は、表を作る文が黙って捨てる方言（PostgreSQL）だけ表の後ろの ALTER TABLE で足す",
                    E.Unique,
                    F.StatementShape,
                    _ => Diagram(TrDuplicateUniques())
                )
                .TrDuplicateUniquesExpect(),
            DialectDdlRow
                .SyncItems(
                    "sync-add-table-unique-on-same-columns-as-earlier-unique-added-after-create-table",
                    "同期: 表の追加は、先に書いた一意制約と同じ列の並びの一意制約を、表を作る文が黙って捨てる方言（PostgreSQL）だけ表の後ろの ALTER TABLE で足す",
                    P.SyncAddTable,
                    E.Unique,
                    F.StatementShape,
                    _ =>
                    {
                        var entity = TrDuplicateUniques();

                        return new DdlSyncItems(
                            [
                                new SchemaDiffItem
                                {
                                    Kind = SchemaDiffKind.AddTable,
                                    TableName = entity.TableName,
                                    Entity = entity,
                                    IsSelected = true,
                                },
                            ],
                            null,
                            new SyncPlanContext()
                        );
                    }
                )
                .TrDuplicateUniquesExpect(),
        ];

    /// <summary>
    /// 表 dup（id〔主キー〕・code・kind）と、一意制約 UQ_dup_a(code, kind)・同じ並びの UQ_dup_b(code, kind)・並びの違う UQ_dup_c(kind, code)
    /// </summary>
    private static Entity TrDuplicateUniques()
    {
        var entity = new Entity { TableName = "dup" };

        foreach (var name in new[] { "id", "code", "kind" })
        {
            entity.Columns.Add(
                new Column
                {
                    Name = name,
                    DataType = "INTEGER",
                    IsPrimaryKey = name == "id",
                    IsNullable = false,
                }
            );
        }

        var code = entity.Columns[1].Id;
        var kind = entity.Columns[2].Id;
        entity.UniqueConstraints.Add(
            new UniqueConstraint { Name = "UQ_dup_a", ColumnIds = [code, kind] }
        );
        entity.UniqueConstraints.Add(
            new UniqueConstraint { Name = "UQ_dup_b", ColumnIds = [code, kind] }
        );
        entity.UniqueConstraints.Add(
            new UniqueConstraint { Name = "UQ_dup_c", ColumnIds = [kind, code] }
        );

        return entity;
    }

    /// <summary>
    /// 同じ列の並びの 2 つ目の一意制約（表を作る文が黙って捨てる方言＝PostgreSQL だけ、表を作る文を閉じた直後の ALTER TABLE で足す。
    /// 並びの違う一意制約は DB も別の索引として持つので表の中に書く。ほかの方言の出力は表の中に 3 つとも書く）
    /// </summary>
    private static DialectDdlRow TrDuplicateUniquesExpect(this DialectDdlRow row) =>
        row.ExpectAll(e =>
                e.Lines("uq dup UQ_dup_a (code, kind)*", "uq dup UQ_dup_c (kind, code)*")
                    .NoLines("uq dup UQ_dup_a * alter", "uq dup UQ_dup_c * alter")
            )
            .Expect(
                [Ss, My, Or, Sl],
                e => e.Lines("uq dup UQ_dup_b (code, kind)*").NoLines("* alter")
            )
            .Expect(
                [Pg],
                e =>
                    e.Sequence(
                            O.Lines,
                            "uq dup UQ_dup_c (kind, code)",
                            "end-table",
                            "uq dup UQ_dup_b (code, kind) alter"
                        )
                        .NoLines("uq dup UQ_dup_b (code, kind)", "uq dup UQ_dup_b (code, kind),")
            );

    // ======================================================================
    // スキーマ修飾名・多対多・型の表記の拒否
    // ======================================================================

    /// <summary>主キー id（INTEGER）だけの表</summary>
    private static Entity TrIntegerKeyTable(string name)
    {
        var entity = new Entity { TableName = name };
        entity.Columns.Add(
            new Column
            {
                Name = "id",
                DataType = "INTEGER",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        return entity;
    }

    /// <summary>
    /// 親（主キー id）と、親を外部キー region_id（名前なし＝既定の名前）で参照する子（主キー id）を並べた図
    /// </summary>
    /// <param name="parentName">親の表名</param>
    /// <param name="childNames">子の表名（子ごとに親への外部キーを 1 本持つ）</param>
    private static ErDiagram TrForeignKeyDiagram(string parentName, params string[] childNames)
    {
        var parent = TrIntegerKeyTable(parentName);
        var diagram = new ErDiagram { Entities = [parent] };

        foreach (var childName in childNames)
        {
            var child = TrIntegerKeyTable(childName);
            var regionId = new Column
            {
                Name = "region_id",
                DataType = "INTEGER",
                IsNullable = true,
            };
            child.Columns.Add(regionId);
            diagram.Entities.Add(child);
            diagram.Relationships.Add(
                new Relationship
                {
                    SourceEntityId = parent.Id,
                    TargetEntityId = child.Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs = [new(parent.Columns[0].Id, regionId.Id)],
                }
            );
        }

        return diagram;
    }

    /// <summary>orders テーブル（int 主キー＋指定型の memo 列）の最小の図</summary>
    private static ErDiagram TrOrdersDiagram(string memoDataType) =>
        new()
        {
            Entities =
            [
                new Entity
                {
                    TableName = "orders",
                    Columns =
                    [
                        new Column
                        {
                            Name = "id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Name = "memo",
                            DataType = memoDataType,
                            IsNullable = true,
                        },
                    ],
                },
            ],
        };

    /// <summary>安全でない型の表記</summary>
    private const string TrUnsafeType = "int'; DROP TABLE users; --";

    private static IEnumerable<DialectDdlRow> MiscTreatmentRows() =>
        [
            DialectDdlRow
                .Ddl(
                    "ddl-schema-qualified-dotted-table-name-note",
                    "DDL: ドットを含む表名は SQLite だけが、その表の CREATE TABLE の前に注記する",
                    E.SchemaQualified,
                    F.Note,
                    _ => new ErDiagram
                    {
                        Entities = [TrIntegerKeyTable("sales.orders"), TrIntegerKeyTable("plain")],
                    }
                )
                .Expect(
                    Sl,
                    e =>
                        e.Lines(
                                "note dotted-table-name | -- Note: this database reads sales.orders as table orders in the attached database sales;*"
                            )
                            .Count(O.Text, 1, "*in the attached database*")
                )
                .Expect([Ss, Pg, My, Or], e => e.NoText("*in the attached database*"))
                .TreatsGroups(
                    K.SchemaQualifiedTableName,
                    Ft.Ddl,
                    (Tm.Annotation, [Sl]),
                    (Tm.None, [Ss, Pg, My, Or])
                ),
            // SQLite の外部キーの句は表名にスキーマを書けない（親は子と同じ DB で解決する）。同じ DB なら表の部分だけを書く
            DialectDdlRow
                .Ddl(
                    "ddl-fk-to-schema-qualified-parent-in-same-schema-references-table-part",
                    "DDL: 子と親が同じスキーマ（sales）の外部キーは、SQLite だけ REFERENCES に表の部分だけを書く（src/QuickER.Provider.Sqlite/CLAUDE.md「スキーマをまたぐ外部キー」）",
                    E.SchemaQualified,
                    F.StatementShape,
                    _ => TrForeignKeyDiagram("sales.regions", "sales.region_notes")
                )
                .Expect(
                    Sl,
                    e =>
                        e.Lines("fk * * (region_id) -> regions (id) inline")
                            .NoText("*REFERENCES \"sales\"*", "*cannot reference*")
                )
                .Expect(
                    [Ss, Pg, My, Or],
                    e => e.Lines("fk * * (region_id) -> sales.regions (id)*")
                ),
            DialectDdlRow
                .Ddl(
                    "ddl-fk-to-main-qualified-parent-from-unqualified-child-references-table-part",
                    "DDL: スキーマの無い子は main の表とみなし、main で修飾した親への外部キーは同じ DB として SQLite も表の部分を書く（src/QuickER.Provider.Sqlite/CLAUDE.md「スキーマをまたぐ外部キー」）",
                    E.SchemaQualified,
                    F.StatementShape,
                    _ => TrForeignKeyDiagram("MAIN.regions", "notes")
                )
                .Expect(
                    Sl,
                    e =>
                        e.Lines("fk notes * (region_id) -> regions (id) inline")
                            .NoText("*cannot reference*")
                )
                .Expect(
                    [Ss, Pg, My, Or],
                    e => e.Lines("fk notes * (region_id) -> MAIN.regions (id)*")
                ),
            DialectDdlRow
                .Ddl(
                    "ddl-fk-across-schemas-omitted-with-note-sqlite",
                    "DDL: 子と親が違うスキーマの外部キーは、SQLite だけ作らずに CREATE TABLE の前に注記する（ほかの方言は作る）",
                    E.SchemaQualified,
                    F.Note,
                    _ => TrForeignKeyDiagram("sales.regions", "notes", "archive.notes")
                )
                .Expect(
                    Sl,
                    e =>
                        e.Lines(
                                "note cross-schema-fk | -- Note: this database cannot reference sales.regions from notes across attached databases; the foreign key FK_notes_sales_regions is not created",
                                "note cross-schema-fk | -- Note: this database cannot reference sales.regions from archive.notes across attached databases; the foreign key FK_archive_notes_sales_regions is not created"
                            )
                            .NoLines("fk * * * -> * *")
                            .NoText("*REFERENCES*")
                )
                .Expect(
                    [Ss, Pg, My, Or],
                    e =>
                        e.Lines(
                                "fk notes * (region_id) -> sales.regions (id)*",
                                "fk archive.notes * (region_id) -> sales.regions (id)*"
                            )
                            .NoText("*cannot reference*")
                )
                .TreatsGroups(
                    K.CrossSchemaForeignKey,
                    Ft.Ddl,
                    (Tm.OmitWithAnnotation, [Sl]),
                    (Tm.None, [Ss, Pg, My, Or])
                ),
            DialectDdlRow
                .SyncDiff(
                    "sync-add-table-fk-to-schema-qualified-parent-in-same-schema-references-table-part",
                    "同期: 表の追加で、子と親が同じスキーマの外部キーは SQLite も表の部分だけを書く。計画は両端のドットの表を名指しする",
                    P.SyncAddTable,
                    E.SchemaQualified,
                    F.StatementShape,
                    _ =>
                    {
                        var diagram = TrForeignKeyDiagram("sales.regions", "sales.region_notes");
                        return CvDiff([], [], diagram.Entities, diagram.Relationships);
                    }
                )
                .Expect(
                    Sl,
                    e =>
                        e.Lines("fk * * (region_id) -> regions (id) inline")
                            .NoText("*REFERENCES \"sales\"*", "*cannot reference*")
                            .Contains(
                                O.Warnings,
                                "SchemaQualifiedTableName sales.regions",
                                "SchemaQualifiedTableName sales.region_notes"
                            )
                )
                .Expect(
                    [Ss, Pg, My, Or],
                    e => e.Lines("fk * * (region_id) -> sales.regions (id)*")
                ),
            DialectDdlRow
                .SyncDiff(
                    "sync-add-table-fk-across-schemas-omitted-with-note-sqlite",
                    "同期: 表の追加で、子と親が違うスキーマの外部キーは SQLite だけ作らずに注記し、計画は両端のドットの表を名指しする",
                    P.SyncAddTable,
                    E.SchemaQualified,
                    F.Note,
                    _ =>
                    {
                        var diagram = TrForeignKeyDiagram("sales.regions", "archive.notes");
                        return CvDiff([], [], diagram.Entities, diagram.Relationships);
                    }
                )
                .Expect(
                    Sl,
                    e =>
                        e.Lines(
                                "note cross-schema-fk | -- Note: this database cannot reference sales.regions from archive.notes across attached databases;*"
                            )
                            .NoText("*REFERENCES*")
                            .Contains(
                                O.Warnings,
                                "SchemaQualifiedTableName sales.regions",
                                "SchemaQualifiedTableName archive.notes"
                            )
                )
                .Expect(
                    [Ss, Pg, My, Or],
                    e =>
                        e.Text("*REFERENCES*")
                            .NoText("*cannot reference*")
                            .Absent(O.Warnings, "SchemaQualifiedTableName*")
                )
                .TreatsGroups(
                    K.CrossSchemaForeignKey,
                    Ft.Sync,
                    (Tm.OmitWithAnnotation, [Sl]),
                    (Tm.None, [Ss, Pg, My, Or])
                ),
            DialectDdlRow
                .SyncDiff(
                    "sync-rebuild-fk-across-schemas-omitted-with-note-sqlite",
                    "同期: SQLite の再構築で付ける外部キーも、子と親が違うスキーマなら作らずに注記する（DDL と同じ 1 か所の判定）",
                    P.SyncRebuild,
                    E.SchemaQualified,
                    F.Note,
                    _ =>
                    {
                        var live = TrForeignKeyDiagram("sales.regions", "notes");
                        var target = TrForeignKeyDiagram("sales.regions", "notes");
                        return CvDiff(live.Entities, [], target.Entities, target.Relationships);
                    }
                )
                .Expect(
                    Sl,
                    e =>
                        e.Lines(
                                "note cross-schema-fk | -- Note: this database cannot reference sales.regions from notes across attached databases;*"
                            )
                            .Text("*CREATE TABLE \"notes_quicker_rebuild\"*")
                            .NoText("*REFERENCES*")
                ),
            DialectDdlRow
                .Ddl(
                    "ddl-many-to-many-junction-note-no-foreign-key",
                    "DDL: 多対多は 5 方言とも外部キーを作らず、中間テーブルを別に定義する旨を注記する",
                    E.ManyToMany,
                    F.Note,
                    _ =>
                    {
                        var a = new Entity
                        {
                            TableName = "a",
                            Columns =
                            {
                                new Column
                                {
                                    Name = "id",
                                    DataType = "int",
                                    IsPrimaryKey = true,
                                },
                            },
                        };
                        var b = new Entity
                        {
                            TableName = "b",
                            Columns =
                            {
                                new Column
                                {
                                    Name = "id",
                                    DataType = "int",
                                    IsPrimaryKey = true,
                                },
                            },
                        };

                        return new ErDiagram
                        {
                            Entities = { a, b },
                            Relationships =
                            {
                                new Relationship
                                {
                                    SourceEntityId = a.Id,
                                    TargetEntityId = b.Id,
                                    Type = RelationshipType.ManyToMany,
                                },
                            },
                        };
                    }
                )
                .ExpectAll(e =>
                    e.Lines("note many-to-many | -- Many-to-many (a <-> b)*")
                        .NoText("*FOREIGN KEY*")
                )
                .TreatsAll(K.ManyToManyRelationship, Ft.Ddl, Tm.Annotation),
            DialectDdlRow
                .Ddl(
                    "ddl-type-unsafe-data-type-is-rejected",
                    "DDL: 5 方言とも安全でない型をテーブル.列つきで拒否する",
                    E.Type,
                    F.Reject,
                    _ => TrOrdersDiagram(TrUnsafeType)
                )
                .ExpectAll(e =>
                    e.Contains(
                        O.Exception,
                        "InvalidOperationException: *",
                        "*orders.memo*",
                        "*DROP TABLE users*"
                    )
                )
                .TreatsAll(K.UnemittableTypeNotation, Ft.Ddl, Tm.Reject),
            DialectDdlRow
                .Ddl(
                    "ddl-type-safe-data-type-is-generated",
                    "DDL: 5 方言とも安全な型の図をそのまま生成する",
                    E.Type,
                    F.StatementShape,
                    _ => TrOrdersDiagram("nvarchar(max)")
                )
                .ExpectAll(e => e.Lines("table orders").NoException()),
            DialectDdlRow
                .SyncItems(
                    "sync-add-table-unsafe-data-type-is-rejected",
                    "同期: 5 方言とも安全でない型の表の追加をテーブル.列つきで拒否する",
                    P.SyncAddTable,
                    E.Type,
                    F.Reject,
                    _ =>
                    {
                        var entity = TrOrdersDiagram(TrUnsafeType).Entities[0];

                        return TrNeutral(
                            new SchemaDiffItem
                            {
                                Kind = SchemaDiffKind.AddTable,
                                TableName = entity.TableName,
                                Entity = entity,
                                IsSelected = true,
                            }
                        );
                    }
                )
                .ExpectAll(e =>
                    e.Contains(O.Exception, "InvalidOperationException: *", "*orders.memo*")
                )
                .TreatsAll(K.UnemittableTypeNotation, Ft.Sync, Tm.Reject),
            DialectDdlRow
                .SyncItems(
                    "sync-other-alter-column-unsafe-data-type-is-rejected",
                    "同期: AlterColumn 単体の安全でない型も拒否する（能力は列の変更だけを足した中立値）",
                    P.SyncOther,
                    E.Type,
                    F.Reject,
                    _ => new DdlSyncItems(
                        [
                            new SchemaDiffItem
                            {
                                Kind = SchemaDiffKind.AlterColumn,
                                TableName = "orders",
                                ColumnName = "memo",
                                Column = new Column
                                {
                                    Name = "memo",
                                    DataType = "nvarchar(50)'; DROP TABLE users; --",
                                    IsNullable = true,
                                },
                                IsSelected = true,
                            },
                        ],
                        TestDialectCapabilities.Neutral with
                        {
                            SupportsAlterColumn = true,
                        }
                    )
                )
                .Expect(
                    Ss,
                    e => e.Contains(O.Exception, "InvalidOperationException: *", "*orders.memo*")
                ),
            DialectDdlRow
                .SyncItems(
                    "sync-other-drop-table-with-unsafe-data-type-is-allowed",
                    "同期: 型を書き出さない表の削除は、安全でない型で止めない（同じ表の追加は拒否する＝対照は sync-add-table-unsafe-data-type-is-rejected）",
                    P.SyncOther,
                    E.Type,
                    F.Omit,
                    _ =>
                    {
                        var entity = TrOrdersDiagram(TrUnsafeType).Entities[0];

                        return TrNeutral(
                            new SchemaDiffItem
                            {
                                Kind = SchemaDiffKind.DropTable,
                                TableName = entity.TableName,
                                Entity = entity,
                                IsSelected = true,
                            }
                        );
                    }
                )
                .Expect(Ss, e => e.Text("*DROP TABLE*")),
        ];
}
