using QuickER.Model;
using QuickER.Provider;
using E = QuickER.Tests.Provider.DialectDdlElements;
using F = QuickER.Tests.Provider.DialectDdlFacets;
using P = QuickER.Tests.Provider.DialectDdlPaths;

namespace QuickER.Tests.Provider;

/// <summary>SqlNameText が名前を集める 1 か所（<c>names.Add</c> の 1 本）の宣言</summary>
/// <param name="Method"><c>names.Add</c> を持つメソッドの名前</param>
/// <param name="Expression"><c>names.Add</c> の第 1 引数の式のテキスト（ソースの綴りのまま）</param>
/// <param name="IsTable">表の名前として測るか（<c>names.Add</c> の第 2 引数）</param>
/// <param name="Location">例外文の置き場所の表記（<c>TABLE</c> / <c>COLUMN</c> / <c>UNIQUE</c> / <c>FOREIGN KEY</c>）</param>
/// <param name="Routes">この出どころへ名前を 1 つだけ差し込んだ入力を作る経路（1 本以上）</param>
internal sealed record DdlNameSource(
    string Method,
    string Expression,
    bool IsTable,
    string Location,
    IReadOnlyList<DdlNameRoute> Routes
)
{
    /// <summary>ソースを走査するガードが <c>names.Add</c> 1 本を表す表記（出どころの式・表の位置か・置き場所・所属のメソッド）</summary>
    public string What =>
        $"{Method}: names.Add({Expression}, {(IsTable ? "true" : "false")}, {Location})";
}

/// <summary>出どころへ名前を 1 つだけ差し込んだ入力を作る経路</summary>
/// <param name="Id">行の Id に使う経路の名前</param>
/// <param name="Path">DDL の表の経路（<see cref="DialectDdlPaths"/>）</param>
/// <param name="Description">出どころの説明（行の説明に使う）</param>
/// <param name="Build">
/// 名前を受けて、その名前を出どころ 1 か所だけに持ち、ほかの名前はすべて安全な入力を作る
/// （<see cref="Path"/> が DDL なら <see cref="ErDiagram"/>、それ以外は <see cref="SyncPlan"/>）
/// </param>
internal sealed record DdlNameRoute(
    string Id,
    string Path,
    string Description,
    Func<string, object> Build
);

/// <summary>
/// 名前の出どころの行（SqlNameText が名前を集める <c>names.Add</c> の 1 本ごとに、その出どころだけへ表せない名前を差し込み、入口が断るかを見る）
/// </summary>
/// <remarks>
/// <para>
/// 宣言の一覧（<see cref="NameSources"/>）が正本で、行はそこから作る。一覧と製品の <c>names.Add</c> の食い違いは、ソースの規則
/// <c>sql-name-text-name-sources</c> が 2 方向で照合する（足したのに一覧へ載せ忘れる・消したのに一覧に残る）。
/// </para>
/// <para>水準は 2 つ。</para>
/// <list type="bullet">
/// <item><description>
/// 改行入り：ほかの名前はすべて安全な入力に、その出どころだけ改行入りの名前を入れる。5 方言とも置き場所を名指しして断る。
/// 出どころの <c>names.Add</c> を 1 本消すと、その行だけが断らなくなる（同じ名前がほかの出どころへ流れ込まない入力にする）。
/// </description></item>
/// <item><description>
/// 表の位置の印：MySQL だけ。文字数は上限内で、ファイル名へ符号化すると上限を超える名前を入れる。表の位置の出どころは断り、
/// 列・制約の位置の出どころは断らない（印は MySQL のファイル名の長さでだけ効く）。
/// </description></item>
/// </list>
/// <para>
/// 計画は SyncPlanner を経ずに直接組み、同期スクリプトの入口へ渡す（どの出どころも 1 本ずつ入れられる）。再構築・列順の計画は SQLite・MySQL だけが
/// 作るが、名前の入口は 5 方言とも同じ計画の全体を読むので、5 方言へ同じ計画を渡す（経路はそのほかの項目として数える）。
/// </para>
/// </remarks>
internal static partial class DialectDdlRows
{
    /// <summary>出どころの宣言を持つ製品のファイル（リポジトリのルートからの相対パス）</summary>
    public const string NameSourceFile = "src/QuickER.Provider/SqlNameText.cs";

    /// <summary>改行入りの名前が例外文に載るときの形（制御文字は空白へ畳まれる）</summary>
    private const string NsInjectedShown = "a DROP TABLE users; --";

    /// <summary>
    /// MySQL で、文字数は上限内でファイル名へ符号化すると上限を超える名前（53 文字・UTF-8 で 53 バイト・ファイル名で
    /// 3 + 50 × 5 = 253 バイト &gt; 251。ほかの方言の上限〔SQL Server 128・PostgreSQL 63 バイト・Oracle 128 バイト〕にも収まる）
    /// </summary>
    private static string NsFileNameOverflow => "ts_" + new string('@', 50);

    /// <summary>出どころの宣言の一覧（SqlNameText の <c>names.Add</c> の 1 本に 1 つ）</summary>
    public static IReadOnlyList<DdlNameSource> NameSources =>
        [
            // ---- PlanNames：計画のセクションの項目 ----
            NsSource(
                "PlanNames",
                "table",
                true,
                "TABLE",
                NsRoute(
                    "item-table",
                    P.SyncOther,
                    "表の削除の項目のテーブル名",
                    name =>
                        NsItems(
                            new SchemaDiffItem { Kind = SchemaDiffKind.DropTable, TableName = name }
                        )
                )
            ),
            NsSource(
                "PlanNames",
                "item.ColumnName",
                false,
                "COLUMN",
                NsRoute(
                    "item-column-name",
                    P.SyncOther,
                    "列の削除の項目の列名",
                    name =>
                        NsItems(
                            new SchemaDiffItem
                            {
                                Kind = SchemaDiffKind.DropColumn,
                                TableName = "orders",
                                ColumnName = name,
                            }
                        )
                )
            ),
            NsSource(
                "PlanNames",
                "item.Column?.Name",
                false,
                "COLUMN",
                NsRoute(
                    "item-column",
                    P.SyncAddColumn,
                    "列の追加の項目の列の定義の名前",
                    name =>
                        NsItems(
                            new SchemaDiffItem
                            {
                                Kind = SchemaDiffKind.AddColumn,
                                TableName = "orders",
                                Column = DdlModel.Col(name, "int"),
                            }
                        )
                )
            ),
            NsSource(
                "PlanNames",
                "item.OldColumn?.Name",
                false,
                "COLUMN",
                NsRoute(
                    "item-old-column",
                    P.SyncOther,
                    "列の変更の項目の変更前の列の名前",
                    name =>
                        NsItems(
                            new SchemaDiffItem
                            {
                                Kind = SchemaDiffKind.AlterColumn,
                                TableName = "orders",
                                ColumnName = "memo",
                                Column = DdlModel.Col("memo", "int"),
                                OldColumn = DdlModel.Col(name, "int"),
                            }
                        )
                )
            ),
            NsSource(
                "PlanNames",
                "item.UniqueConstraintName",
                false,
                "UNIQUE",
                NsRoute(
                    "item-unique-constraint-name",
                    P.SyncOther,
                    "一意制約の追加の項目の制約名",
                    name => NsItems(NsAddUnique(name, "memo"))
                )
            ),
            NsSource(
                "PlanNames",
                "item.ForeignKeyName",
                false,
                "FOREIGN KEY",
                NsRoute(
                    "item-foreign-key-name",
                    P.SyncOther,
                    "外部キーの削除の項目の制約名",
                    name =>
                        NsItems(
                            new SchemaDiffItem
                            {
                                Kind = SchemaDiffKind.DropForeignKey,
                                TableName = "orders",
                                ForeignKeyName = name,
                            }
                        )
                )
            ),
            NsSource(
                "PlanNames",
                "item.Relationship?.ConstraintName",
                false,
                "FOREIGN KEY",
                NsRoute(
                    "item-relationship-constraint-name",
                    P.SyncOther,
                    "外部キーの追加の項目のリレーションの制約名",
                    name => NsItems(NsAddForeignKey(relationshipName: name, foreignKeyName: null))
                )
            ),
            NsSource(
                "PlanNames",
                "column",
                false,
                "COLUMN",
                NsRoute(
                    "item-unique-constraint-column",
                    P.SyncOther,
                    "一意制約の追加の項目の構成列",
                    name => NsItems(NsAddUnique("uq_orders_memo", name))
                )
            ),
            NsSource(
                "PlanNames",
                "pair.ParentColumn",
                false,
                "COLUMN",
                NsRoute(
                    "item-fk-pair-parent-column",
                    P.SyncOther,
                    "外部キーの追加の項目の列ペアの親の列",
                    name => NsItems(NsAddForeignKey(parentColumn: name))
                )
            ),
            NsSource(
                "PlanNames",
                "pair.ChildColumn",
                false,
                "COLUMN",
                NsRoute(
                    "item-fk-pair-child-column",
                    P.SyncOther,
                    "外部キーの追加の項目の列ペアの子の列",
                    name => NsItems(NsAddForeignKey(childColumn: name))
                )
            ),
            NsSource(
                "PlanNames",
                "item.ParentEntity?.TableName",
                true,
                "TABLE",
                NsRoute(
                    "item-parent-entity",
                    P.SyncOther,
                    "外部キーの追加の項目の親の表のテーブル名",
                    name => NsItems(NsAddForeignKey(parentTable: name))
                )
            ),
            NsSource(
                "PlanNames",
                "item.ChildEntity?.TableName",
                true,
                "TABLE",
                NsRoute(
                    "item-child-entity",
                    P.SyncOther,
                    "外部キーの追加の項目の子の表のテーブル名",
                    name => NsItems(NsAddForeignKey(childTable: name))
                )
            ),
            // ---- PlanNames：テーブルの再構築 ----
            NsSource(
                "PlanNames",
                "rebuild.TableName",
                true,
                "TABLE",
                NsRoute(
                    "rebuild-table",
                    P.SyncOther,
                    "再構築の計画のテーブル名",
                    name => NsRebuilds(NsRebuild(tableName: name))
                )
            ),
            NsSource(
                "PlanNames",
                "column",
                false,
                "COLUMN",
                NsRoute(
                    "rebuild-copy-column",
                    P.SyncOther,
                    "再構築の計画の移す列",
                    name => NsRebuilds(NsRebuild(copyColumns: ["id", name]))
                )
            ),
            NsSource(
                "PlanNames",
                "foreignKey.ConstraintName",
                false,
                "FOREIGN KEY",
                NsRoute(
                    "rebuild-fk-constraint-name",
                    P.SyncOther,
                    "再構築の計画の外部キーの制約名",
                    name =>
                        NsRebuilds(NsRebuild(foreignKey: NsRebuildForeignKey(constraintName: name)))
                )
            ),
            NsSource(
                "PlanNames",
                "foreignKey.ParentTable",
                true,
                "TABLE",
                NsRoute(
                    "rebuild-fk-parent-table",
                    P.SyncOther,
                    "再構築の計画の外部キーの親の表",
                    name =>
                        NsRebuilds(NsRebuild(foreignKey: NsRebuildForeignKey(parentTable: name)))
                )
            ),
            NsSource(
                "PlanNames",
                "column",
                false,
                "COLUMN",
                NsRoute(
                    "rebuild-fk-child-column",
                    P.SyncOther,
                    "再構築の計画の外部キーの子の列",
                    name =>
                        NsRebuilds(NsRebuild(foreignKey: NsRebuildForeignKey(childColumn: name)))
                ),
                NsRoute(
                    "rebuild-fk-parent-column",
                    P.SyncOther,
                    "再構築の計画の外部キーの親の列",
                    name =>
                        NsRebuilds(NsRebuild(foreignKey: NsRebuildForeignKey(parentColumn: name)))
                )
            ),
            // ---- PlanNames：ネイティブの列順の変更 ----
            NsSource(
                "PlanNames",
                "reorder.TableName",
                true,
                "TABLE",
                NsRoute(
                    "reorder-table",
                    P.SyncOther,
                    "列順の変更の計画のテーブル名",
                    name => NsReorders(NsReorder(tableName: name))
                )
            ),
            NsSource(
                "PlanNames",
                "move.Column.Name",
                false,
                "COLUMN",
                NsRoute(
                    "reorder-move-column",
                    P.SyncOther,
                    "列順の変更の計画の動かす列",
                    name => NsReorders(NsReorder(column: name))
                )
            ),
            NsSource(
                "PlanNames",
                "move.AfterColumn",
                false,
                "COLUMN",
                NsRoute(
                    "reorder-move-after-column",
                    P.SyncOther,
                    "列順の変更の計画の直後へ置く先の列",
                    name => NsReorders(NsReorder(afterColumn: name))
                )
            ),
            // ---- DiagramNames：図のリレーション ----
            NsSource(
                "DiagramNames",
                "relationship.ConstraintName",
                false,
                "FOREIGN KEY",
                NsRoute(
                    "relationship-constraint-name",
                    P.Ddl,
                    "図のリレーションの制約名",
                    name => NsFamilyDiagram(name)
                )
            ),
            // ---- AddEntityNames：図の表・表の追加の項目の表・再構築の合成後の定義（3 つの呼び出し元） ----
            NsSource(
                "AddEntityNames",
                "entity.TableName",
                true,
                "TABLE",
                NsRoute(
                    "entity-table",
                    P.Ddl,
                    "図の表のテーブル名",
                    name => DdlModel.Diagram(NsEntity(tableName: name))
                ),
                NsRoute(
                    "item-entity-table",
                    P.SyncAddTable,
                    "表の追加の項目の表の定義のテーブル名",
                    name => NsItems(NsAddTable(NsEntity(tableName: name)))
                ),
                NsRoute(
                    "rebuild-definition-table",
                    P.SyncOther,
                    "再構築の計画の合成後の定義のテーブル名",
                    name => NsRebuilds(NsRebuild(definition: NsEntity(tableName: name)))
                )
            ),
            NsSource(
                "AddEntityNames",
                "column.Name",
                false,
                "COLUMN",
                NsRoute(
                    "entity-column",
                    P.Ddl,
                    "図の表の列名",
                    name => DdlModel.Diagram(NsEntity(columnName: name))
                ),
                NsRoute(
                    "item-entity-column",
                    P.SyncAddTable,
                    "表の追加の項目の表の定義の列名",
                    name => NsItems(NsAddTable(NsEntity(columnName: name)))
                ),
                NsRoute(
                    "rebuild-definition-column",
                    P.SyncOther,
                    "再構築の計画の合成後の定義の列名",
                    name => NsRebuilds(NsRebuild(definition: NsEntity(columnName: name)))
                )
            ),
            NsSource(
                "AddEntityNames",
                "constraint.Name",
                false,
                "UNIQUE",
                NsRoute(
                    "entity-unique-constraint",
                    P.Ddl,
                    "図の表の一意制約名",
                    name => DdlModel.Diagram(NsEntity(uniqueName: name))
                ),
                NsRoute(
                    "item-entity-unique-constraint",
                    P.SyncAddTable,
                    "表の追加の項目の表の定義の一意制約名",
                    name => NsItems(NsAddTable(NsEntity(uniqueName: name)))
                ),
                NsRoute(
                    "rebuild-definition-unique-constraint",
                    P.SyncOther,
                    "再構築の計画の合成後の定義の一意制約名",
                    name => NsRebuilds(NsRebuild(definition: NsEntity(uniqueName: name)))
                )
            ),
        ];

    /// <summary>出どころの行（改行入り・表の位置の印の 2 水準を、宣言の一覧の経路ごとに作る）</summary>
    private static IEnumerable<DialectDdlRow> NameSourceRows() =>
        NameSources.SelectMany(source =>
            source.Routes.SelectMany(route => NameSourceRows(source, route))
        );

    /// <summary>出どころ 1 つの経路 1 つの行（2 水準）</summary>
    private static IEnumerable<DialectDdlRow> NameSourceRows(
        DdlNameSource source,
        DdlNameRoute route
    )
    {
        // 行 Id の接頭辞（CLAUDE.md が名指す接頭辞をソースから読めるよう、組み立てずに丸ごと書く）
        var prefix = route.Path == P.Ddl ? "ddl-name-source-" : "sync-plan-name-source-";
        var entrance = route.Path == P.Ddl ? "DDL" : "同期スクリプト";
        var element = source.Location switch
        {
            "TABLE" => E.Table,
            "COLUMN" => E.Column,
            "UNIQUE" => E.Unique,
            "FOREIGN KEY" => E.ForeignKey,
            _ => throw new DdlRowDeclarationException(
                $"出どころ {route.Id} の置き場所 {source.Location} を知らない"
            ),
        };

        yield return NsRow(
                $"{prefix}{route.Id}-newline",
                $"{entrance}の入口は、{route.Description}だけにある改行入りの名前を {source.Location} として名指しで断る",
                route,
                element,
                CcInjected
            )
            .ExpectAll(e =>
                e.Throws(
                    $"InvalidOperationException: *line break or control character*{source.Location} '*{NsInjectedShown}'*"
                )
            );

        var overflow = NsFileNameOverflow;

        yield return source.IsTable
            ? NsRow(
                    $"{prefix}{route.Id}-table-file-name",
                    $"MySQL の{entrance}の入口は、{route.Description}を表の名前として測り、ファイル名へ符号化すると上限を超える名前を断る",
                    route,
                    element,
                    overflow
                )
                .Expect(
                    My,
                    e =>
                        e.Throws(
                            $"InvalidOperationException: *cannot be used in this database: TABLE '{overflow}' is longer than the limit of *"
                        )
                )
            : NsRow(
                    $"{prefix}{route.Id}-table-file-name",
                    $"MySQL の{entrance}の入口は、{route.Description}を表の名前として測らない（ファイル名へ符号化すると上限を超えても文字数が上限内なら断らない）",
                    route,
                    element,
                    overflow
                )
                .Expect(My, e => e.NoException());
    }

    /// <summary>経路の入力に名前を差し込んだ行</summary>
    private static DialectDdlRow NsRow(
        string id,
        string description,
        DdlNameRoute route,
        string element,
        string name
    ) =>
        route.Path == P.Ddl
            ? DialectDdlRow.Ddl(
                id,
                description,
                element,
                F.Reject,
                _ => (ErDiagram)route.Build(name)
            )
            : DialectDdlRow.SyncPlan(
                id,
                description,
                route.Path,
                element,
                F.Reject,
                _ => (SyncPlan)route.Build(name)
            );

    private static DdlNameSource NsSource(
        string method,
        string expression,
        bool isTable,
        string location,
        params DdlNameRoute[] routes
    ) => new(method, expression, isTable, location, routes);

    private static DdlNameRoute NsRoute(
        string id,
        string path,
        string description,
        Func<string, object> build
    ) => new(id, path, description, build);

    /// <summary>項目ごとに 1 セクションの計画</summary>
    private static SyncPlan NsItems(params SchemaDiffItem[] items) =>
        new()
        {
            Sections =
            [
                .. items.Select(item => new SyncPlanSection { Kind = item.Kind, Items = [item] }),
            ],
        };

    /// <summary>再構築だけの計画</summary>
    private static SyncPlan NsRebuilds(params TableRebuildPlan[] rebuilds) =>
        new() { Rebuilds = rebuilds };

    /// <summary>列順の変更だけの計画</summary>
    private static SyncPlan NsReorders(params TableReorderPlan[] reorders) =>
        new() { Reorders = reorders };

    /// <summary>orders（主キー id・memo）に一意制約 1 つを持つ表（名前はどれも差し替えられる）</summary>
    private static Entity NsEntity(
        string tableName = "orders",
        string columnName = "memo",
        string uniqueName = "uq_orders_memo"
    )
    {
        var entity = DdlModel.Table(
            tableName,
            DdlModel.Pk("id", "int"),
            DdlModel.Col(columnName, "int")
        );
        entity.UniqueConstraints.Add(
            new UniqueConstraint { Name = uniqueName, ColumnIds = [entity.Columns[1].Id] }
        );

        return entity;
    }

    /// <summary>表の追加の項目（項目のテーブル名は orders のまま＝表の定義のテーブル名とは別の出どころ）</summary>
    private static SchemaDiffItem NsAddTable(Entity entity) =>
        new()
        {
            Kind = SchemaDiffKind.AddTable,
            TableName = "orders",
            Entity = entity,
        };

    /// <summary>orders の一意制約の追加の項目</summary>
    private static SchemaDiffItem NsAddUnique(string constraintName, string columnName) =>
        new()
        {
            Kind = SchemaDiffKind.AddUniqueConstraint,
            TableName = "orders",
            UniqueConstraintName = constraintName,
            UniqueConstraintColumns = [columnName],
        };

    /// <summary>
    /// 親の表（主キー 1 列）から子の表（主キー id と外部キーの列）への外部キーの追加の項目
    /// （項目のテーブル名は orders のまま＝子の表のテーブル名とは別の出どころ。表の定義の列は出どころに入らない）
    /// </summary>
    private static SchemaDiffItem NsAddForeignKey(
        string parentTable = "customers",
        string parentColumn = "id",
        string childTable = "orders",
        string childColumn = "customer_id",
        string? relationshipName = null,
        string? foreignKeyName = "fk_orders_customers"
    )
    {
        var parent = DdlModel.Table(parentTable, DdlModel.Pk(parentColumn, "int"));
        var child = DdlModel.Table(
            childTable,
            DdlModel.Pk("id", "int"),
            DdlModel.Col(childColumn, "int")
        );

        return new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AddForeignKey,
            TableName = "orders",
            ForeignKeyName = foreignKeyName,
            Relationship = new Relationship
            {
                SourceEntityId = parent.Id,
                TargetEntityId = child.Id,
                Type = RelationshipType.OneToMany,
                ColumnPairs = [new(parent.Columns[0].Id, child.Columns[1].Id)],
                ConstraintName = relationshipName,
            },
            ParentEntity = parent,
            ChildEntity = child,
            ForeignKeyColumnPairs = [new ForeignKeyColumnNamePair(parentColumn, childColumn)],
        };
    }

    /// <summary>
    /// orders の再構築（計画のテーブル名と合成後の定義のテーブル名は別の出どころ）
    /// </summary>
    private static TableRebuildPlan NsRebuild(
        string tableName = "orders",
        Entity? definition = null,
        IReadOnlyList<string>? copyColumns = null,
        TableRebuildForeignKey? foreignKey = null
    ) =>
        new()
        {
            TableName = tableName,
            NewDefinition = definition ?? NsEntity(),
            CopyColumns = copyColumns ?? ["id", "memo"],
            ForeignKeys = foreignKey is null ? [] : [foreignKey],
        };

    /// <summary>再構築の計画に張る外部キー（orders.customer_id → customers.id）</summary>
    private static TableRebuildForeignKey NsRebuildForeignKey(
        string constraintName = "fk_orders_customers",
        string childColumn = "customer_id",
        string parentTable = "customers",
        string parentColumn = "id"
    ) =>
        new(
            constraintName,
            [childColumn],
            parentTable,
            [parentColumn],
            ForeignKeyReferentialAction.NoAction,
            ForeignKeyReferentialAction.NoAction
        );

    /// <summary>orders の列順の変更（customer_id を id の直後へ動かす）</summary>
    private static TableReorderPlan NsReorder(
        string tableName = "orders",
        string column = "customer_id",
        string afterColumn = "id"
    ) =>
        new()
        {
            TableName = tableName,
            Moves = [new ColumnMove(DdlModel.Col(column, "int"), afterColumn)],
        };

    /// <summary>customers.id → orders.customer_id の外部キー 1 本の図（制約名は受けた名前）</summary>
    private static ErDiagram NsFamilyDiagram(string constraintName)
    {
        var parent = DdlModel.Table("customers", DdlModel.Pk("id", "int"));
        var child = DdlModel.Table(
            "orders",
            DdlModel.Pk("id", "int"),
            DdlModel.Col("customer_id", "int")
        );
        var relationship = new Relationship
        {
            SourceEntityId = parent.Id,
            TargetEntityId = child.Id,
            Type = RelationshipType.OneToMany,
            ColumnPairs = [new(parent.Columns[0].Id, child.Columns[1].Id)],
            ConstraintName = constraintName,
        };

        return new ErDiagram { Entities = { parent, child }, Relationships = { relationship } };
    }
}
