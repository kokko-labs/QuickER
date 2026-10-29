using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>総当たりの生成器が必ず混ぜる図の形（種の集合全体で全部が出ることを表明する）</summary>
internal static class FuzzShape
{
    /// <summary>自己参照の外部キー</summary>
    public const string SelfReference = "self-reference";

    /// <summary>同じ列に外部キーが 2 本（別の親）</summary>
    public const string SameColumnTwoForeignKeys = "same-column-two-fks";

    /// <summary>同じ列ペアに外部キーが 2 本（同じ親・別の名前）</summary>
    public const string SamePairTwoForeignKeys = "same-pair-two-fks";

    /// <summary>計算列（生成列）</summary>
    public const string Computed = "computed-column";

    /// <summary>目標の図で大文字小文字だけ違う表・列の名前</summary>
    public const string CaseOnlyName = "case-only-name";

    /// <summary>名前付きの一意制約</summary>
    public const string NamedUnique = "named-unique";

    /// <summary>外部キーの子列に張った一意制約（MySQL では外部キーを支える索引になる）</summary>
    public const string UniqueOnForeignKey = "unique-on-fk";

    /// <summary>複合主キー</summary>
    public const string CompositePrimaryKey = "composite-pk";

    /// <summary>文字列の主キー</summary>
    public const string StringPrimaryKey = "string-pk";

    /// <summary>主キーの列の削除を、その表の主キーの変更を選ばずに選んだ</summary>
    public const string DropPrimaryKeyColumnAlone = "drop-pk-column-alone";

    /// <summary>一意制約の構成列の削除を、その一意制約の削除を選ばずに選んだ</summary>
    public const string DropUniqueMemberAlone = "drop-unique-member-alone";

    /// <summary>同じ列の並びに一意制約が 2 つ（別の名前）</summary>
    public const string SameSetTwoUniques = "same-set-two-uniques";

    /// <summary>同じ列の並びの一意制約 2 つの片方の削除と、その列の型の変更を一緒に選んだ</summary>
    public const string DropDuplicatedUniqueWithTypeChange =
        "drop-duplicated-unique-with-type-change";

    /// <summary>同じ親の表を、別の子の列から 2 本以上の外部キーが参照する（兄弟の外部キー）</summary>
    public const string SiblingForeignKeys = "sibling-fks";

    /// <summary>名前に識別子の引用の中で特別な扱いが要る文字（<see cref="SyncPlanFuzzDialect.SymbolCharacter"/>）を含む列</summary>
    public const string SymbolName = "symbol-name";

    /// <summary>既定と別のスキーマにある、同じ名前の表（<see cref="SyncPlanFuzzDialect.OtherSchema"/>）</summary>
    public const string OtherSchemaSameName = "other-schema-same-name";

    /// <summary>種の集合全体で必ず出ていなければならない形（方言が作れない形は除く＝<see cref="RequiredFor"/>）</summary>
    public static readonly IReadOnlyList<string> Required =
    [
        SelfReference,
        SameColumnTwoForeignKeys,
        SamePairTwoForeignKeys,
        Computed,
        CaseOnlyName,
        NamedUnique,
        UniqueOnForeignKey,
        CompositePrimaryKey,
        StringPrimaryKey,
        DropPrimaryKeyColumnAlone,
        DropUniqueMemberAlone,
        SameSetTwoUniques,
        DropDuplicatedUniqueWithTypeChange,
        SiblingForeignKeys,
        SymbolName,
        OtherSchemaSameName,
    ];

    /// <summary>
    /// 方言の必須の形（DB が作らせない形＝Oracle の同じ列ペアの外部キー 2 本・同じ列の並びの一意制約 2 つと、別のスキーマを
    /// 使わない方言の別のスキーマの表を除く）
    /// </summary>
    /// <param name="dialect">方言</param>
    /// <param name="realDatabase">
    /// 実 DB の総当たりか（偽なら能力の組の網）。実 DB では <see cref="DropDuplicatedUniqueWithTypeChange"/> を求めない：
    /// (1) SQLite は取込が一意制約の名前を持たない（sqlite_autoindex）ので、同じ列の並びの一意制約 2 つの片方だけを名前で
    /// 選ぶ形が原理的に出ない (2) SQL Server は普段の 100 種で出ない（形・変更・選び方の 3 つが重なる確率が低い）
    /// (3) 実 DB では形の行（D4・<see cref="SyncPlanShapeRows"/>）の Q6 の 3 つ組（一意制約 2 つ × 一意制約の削除 × 一意制約の列の
    /// 型の変更）が決定的に担う。網は 5 方言とも 5000 種で出るので網で表明する
    /// </param>
    public static IReadOnlyList<string> RequiredFor(
        SyncPlanFuzzDialect dialect,
        bool realDatabase
    ) =>
        Required
            .Where(shape =>
                shape switch
                {
                    SamePairTwoForeignKeys => dialect.AllowDuplicateForeignKeys,
                    SameSetTwoUniques => dialect.AllowDuplicateUniqueConstraints,
                    DropDuplicatedUniqueWithTypeChange => dialect.AllowDuplicateUniqueConstraints
                        && !realDatabase,
                    OtherSchemaSameName => dialect.OtherSchema is not null,
                    _ => true,
                }
            )
            .ToList();
}

/// <summary>差分の選び方</summary>
internal enum FuzzSelection
{
    /// <summary>全部を選ぶ</summary>
    All,

    /// <summary>差分の既定の選択のまま</summary>
    Default,

    /// <summary>項目ごとに半々の確率で選ぶ</summary>
    Random,

    /// <summary>1 項目だけ選ぶ</summary>
    Single,

    /// <summary>
    /// 差分の種類をランダムに選び、その種類の項目を全部選ぶ（「表の削除は選ぶが外部キーの削除は選ばない」
    /// 「主キーの変更は選ぶが列の追加は選ばない」のような、種類の境目で依存を切る選び方）
    /// </summary>
    ByKinds,
}

/// <summary>生成器が覚えておく 1 列の情報（型の種類・投入した値・変更してはいけない列か）</summary>
internal sealed class FuzzColumnInfo
{
    /// <summary>型の種類</summary>
    public FuzzTypeKind Kind { get; set; }

    /// <summary>文字列の長さ・小数の精度</summary>
    public int Length { get; set; }

    /// <summary>行ごとの値（SQL のリテラル。null は NULL）</summary>
    public List<string?> Values { get; } = [];

    /// <summary>計算列の式が参照する列（型の変更・削除をしない）</summary>
    public bool Pinned { get; set; }

    /// <summary>計算列そのもの</summary>
    public bool Computed { get; set; }

    /// <summary>NULL を含まないか</summary>
    public bool HasNoNull => Values.All(v => v is not null);

    /// <summary>値がすべて違うか（NULL を含まない前提）</summary>
    public bool IsDistinct => HasNoNull && Values.Distinct().Count() == Values.Count;
}

/// <summary>1 つの種で作った DB 側の図と、その DB へ流すセットアップの文</summary>
internal sealed class FuzzLiveSchema
{
    /// <summary>DDL 生成器へ渡す図（計算列は含まない＝後から足す）</summary>
    public ErDiagram Diagram { get; } = new();

    /// <summary>列の情報（キーは「表名\u0001列名」の大文字小文字非依存）</summary>
    public Dictionary<string, FuzzColumnInfo> Columns { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>表ごとの行数（キーは表名・大文字小文字非依存）</summary>
    public Dictionary<string, int> RowCounts { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>データを入れる文（親→子の順）</summary>
    public List<string> InsertStatements { get; } = [];

    /// <summary>計算列を足す文（データを入れた後に流す）</summary>
    public List<string> ComputedColumnStatements { get; } = [];

    /// <summary>この種に出た図の形（<see cref="FuzzShape"/>）</summary>
    public HashSet<string> Shapes { get; } = [];

    /// <summary>列情報のキー</summary>
    public static string Key(string table, string column) => table + "\u0001" + column;

    /// <summary>列情報を引く（無ければ null）</summary>
    public FuzzColumnInfo? Find(string table, string column) =>
        Columns.TryGetValue(Key(table, column), out var info) ? info : null;
}

/// <summary>
/// 同期計画の総当たりの 1 ケースの作り方（DB 側の図・目標の図への変更・差分の選び方）。乱数の生成器
/// （<see cref="SyncPlanFuzzGenerator"/>）と、手で書いた決定的な行が実装し、どちらも同じランナー
/// （<see cref="SyncPlanFuzzRunner.RunCaseAsync"/>）と同じ表明を通る。
/// </summary>
internal interface ISyncPlanFuzzCase
{
    /// <summary>このケースで目標の図へ加えた変更の記録（報告用）</summary>
    List<string> Mutations { get; }

    /// <summary>DB 側の図・データ・計算列の文を作る</summary>
    FuzzLiveSchema BuildLive();

    /// <summary>取り込んだ DB の図（新しい Guid 空間へ写したもの）に変更を加える</summary>
    /// <param name="entities">目標の図の表</param>
    /// <param name="relationships">目標の図のリレーション</param>
    /// <param name="live">DB 側の図（<see cref="BuildLive"/> の結果）</param>
    void Mutate(List<Entity> entities, List<Relationship> relationships, FuzzLiveSchema live);

    /// <summary>差分の選び方を決めて、項目の選択を書き換える</summary>
    /// <param name="items">差分の項目</param>
    FuzzSelection Select(IReadOnlyList<SchemaDiffItem> items);
}

/// <summary>
/// 同期計画の総当たりの生成器。DB 側の図（とデータ）・目標の図への変更・差分の選び方を 1 つの乱数から作る。
/// </summary>
/// <remarks>
/// <para>
/// データは「どの外部キーも満たす」ように作る（親の主キーは行番号 1..n・子の参照列は親の行を指す）。
/// 失敗するのが DB の都合だけの変更（データのある表へ NOT NULL の列を足す・NULL を含む列を NOT NULL にする・
/// データより短い長さへ縮める・計算列の式が参照する列を変える）は作らない。そうした失敗は同期計画の欠陥ではなく、
/// 総当たりの赤を信用できなくするだけなので、生成器の側で避ける。
/// </para>
/// </remarks>
internal sealed class SyncPlanFuzzGenerator(Random rng, SyncPlanFuzzDialect dialect)
    : ISyncPlanFuzzCase
{
    /// <summary>表の名前の部品</summary>
    private static readonly string[] TableWords =
    [
        "orders",
        "items",
        "users",
        "nodes",
        "codes",
        "tags",
        "notes",
        "groups",
    ];

    /// <summary>この種で起きた変更の記録（報告用）</summary>
    public List<string> Mutations { get; } = [];

    // ---------------- DB 側の図 ----------------

    /// <summary>DB 側の図・データ・計算列の文を作る</summary>
    public FuzzLiveSchema BuildLive()
    {
        var live = new FuzzLiveSchema();
        var tableCount = rng.Next(3, 6);
        var words = TableWords.OrderBy(_ => rng.Next()).Take(tableCount).ToList();

        for (var i = 0; i < tableCount; i++)
        {
            var name = $"t{i}_{words[i]}";

            // 大文字を含む名前も混ぜる（目標の図で大小を変える変更の相手になる）
            if (rng.Next(4) == 0)
            {
                name = char.ToUpperInvariant(name[0]) + name[1..];
            }

            BuildTable(live, name, i);
        }

        AddOtherSchemaTable(live);
        AddSameColumnTwoForeignKeys(live);
        AddSamePairTwoForeignKeys(live);
        AddUniqueConstraints(live);
        BuildInserts(live, dialect);
        AddComputedColumn(live);

        if (HasSiblingForeignKeys(live.Diagram.Relationships))
        {
            live.Shapes.Add(FuzzShape.SiblingForeignKeys);
        }

        return live;
    }

    /// <summary>
    /// 既定と別のスキーマに、既存の表と同じ名前の表を置く（行なし。名前の逆引きが表の所有者で絞られているかの相手）
    /// </summary>
    private void AddOtherSchemaTable(FuzzLiveSchema live)
    {
        if (dialect.OtherSchema is not { } schema || rng.Next(10) >= 3)
        {
            return;
        }

        var source = live.Diagram.Entities[rng.Next(live.Diagram.Entities.Count)];
        var name = $"{schema}.{source.TableName}";
        live.Shapes.Add(FuzzShape.OtherSchemaSameName);
        var entity = new Entity { TableName = name };
        live.RowCounts[name] = 0;
        AddColumn(live, entity, "id", FuzzTypeKind.Int, 0, false, r => Int(r), pk: true);
        AddColumn(live, entity, "n0", FuzzTypeKind.Int, 0, true, r => Int(r));
        live.Diagram.Entities.Add(entity);
    }

    /// <summary>同じ親の表を、別の子の列から 2 本以上の外部キーが参照するか（自己参照は数えない）</summary>
    internal static bool HasSiblingForeignKeys(IEnumerable<Relationship> relationships) =>
        relationships
            .Where(r => r.SourceEntityId != r.TargetEntityId)
            .GroupBy(r => r.SourceEntityId)
            .Any(g =>
                g.Select(r =>
                        (
                            r.TargetEntityId,
                            string.Join(",", r.ColumnPairs.Select(p => p.TargetColumnId))
                        )
                    )
                    .Distinct()
                    .Count() >= 2
            );

    /// <summary>主キーを変えた表を参照する外部キーの数の札（自己参照は数えない）</summary>
    private static string ReferencingTag(Entity entity, List<Relationship> relationships)
    {
        var count = relationships.Count(r =>
            r.SourceEntityId == entity.Id && r.TargetEntityId != entity.Id
        );
        return $" {{refs={(count >= 2 ? "2+" : Int(count))}}}";
    }

    /// <summary>1 表を作る（主キー・普通の列・前の表への外部キー・自己参照）</summary>
    private void BuildTable(FuzzLiveSchema live, string name, int index)
    {
        var entity = new Entity { TableName = name };
        var rows = rng.Next(10) < 3 ? 0 : 3;
        live.RowCounts[name] = rows;

        // 主キー: 整数 1 列（既定）・整数 2 列の複合・文字列
        var pkStyle = rng.Next(10);

        if (pkStyle < 6)
        {
            AddColumn(live, entity, "id", FuzzTypeKind.Int, 0, false, r => Int(r), pk: true);
        }
        else if (pkStyle < 8)
        {
            live.Shapes.Add(FuzzShape.CompositePrimaryKey);
            AddColumn(live, entity, "id", FuzzTypeKind.Int, 0, false, r => Int(r), pk: true);
            AddColumn(live, entity, "k", FuzzTypeKind.Int, 0, false, _ => "1", pk: true);
        }
        else
        {
            live.Shapes.Add(FuzzShape.StringPrimaryKey);
            var length = new[] { 20, 50, 63 }[rng.Next(3)];
            AddColumn(
                live,
                entity,
                "code",
                FuzzTypeKind.VarChar,
                length,
                false,
                r => dialect.StringLiteral($"c{r}"),
                pk: true
            );
        }

        // 普通の列（整数・文字列・小数・日時・固定長の文字列・バイト列）
        var extra = rng.Next(1, 4);

        for (var j = 0; j < extra; j++)
        {
            var nullable = rng.Next(2) == 0;

            switch (rng.Next(6))
            {
                case 0:
                    AddColumn(
                        live,
                        entity,
                        $"n{j}",
                        FuzzTypeKind.Int,
                        0,
                        nullable,
                        r => Int(r * 10 + j)
                    );
                    break;
                case 1:
                    // ときどき、識別子の引用の中で特別な扱いが要る文字を名前に含める
                    var symbol = rng.Next(4) == 0;

                    if (symbol)
                    {
                        live.Shapes.Add(FuzzShape.SymbolName);
                    }

                    AddColumn(
                        live,
                        entity,
                        symbol ? $"s{dialect.SymbolCharacter}{j}" : $"s{j}",
                        FuzzTypeKind.VarChar,
                        new[] { 20, 40 }[rng.Next(2)],
                        nullable,
                        r => dialect.StringLiteral($"s{r}{j}")
                    );
                    break;
                case 2:
                    AddColumn(
                        live,
                        entity,
                        $"d{j}",
                        FuzzTypeKind.Decimal,
                        10,
                        nullable,
                        r => $"{r}.5"
                    );
                    break;
                case 3:
                    AddColumn(
                        live,
                        entity,
                        $"t{j}",
                        FuzzTypeKind.DateTime,
                        rng.Next(2) == 0 ? 3 : dialect.DefaultDateTimePrecision,
                        nullable,
                        r => dialect.DateTimeLiteral(r)
                    );
                    break;
                case 4:
                    AddColumn(
                        live,
                        entity,
                        $"f{j}",
                        FuzzTypeKind.FixedChar,
                        new[] { 10, 20 }[rng.Next(2)],
                        nullable,
                        r => dialect.StringLiteral($"f{r}{j}")
                    );
                    break;
                default:
                    AddColumn(
                        live,
                        entity,
                        $"b{j}",
                        FuzzTypeKind.Binary,
                        new[] { 16, 32 }[rng.Next(2)],
                        nullable,
                        r =>
                            dialect.BinaryLiteral(
                                r.ToString("X2", CultureInfo.InvariantCulture)
                                    + j.ToString("X2", CultureInfo.InvariantCulture)
                            )
                    );
                    break;
            }
        }

        live.Diagram.Entities.Add(entity);

        // 前の表への外部キー（半々で、もう参照されている親を選ぶ＝兄弟の外部キーの形を作る）
        if (index > 0 && rng.Next(10) < 7)
        {
            var referenced = live
                .Diagram.Entities.Take(index)
                .Where(e =>
                    live.Diagram.Relationships.Any(r =>
                        r.SourceEntityId == e.Id && r.TargetEntityId != e.Id
                    )
                )
                .ToList();
            var parent =
                referenced.Count > 0 && rng.Next(2) == 0
                    ? referenced[rng.Next(referenced.Count)]
                    : live.Diagram.Entities[rng.Next(index)];
            AddForeignKeyColumns(live, entity, parent, $"{BaseName(parent)}_ref");
        }

        // 自己参照（整数 1 列の主キーの表だけ）
        if (rng.Next(10) < 4 && IsSingleIntPrimaryKey(entity))
        {
            live.Shapes.Add(FuzzShape.SelfReference);
            var idColumn = entity.Columns.Single(c => c.IsPrimaryKey);
            var parentColumn = AddColumn(
                live,
                entity,
                "parent_id",
                FuzzTypeKind.Int,
                0,
                true,
                r => r == 1 ? null : Int(r - 1)
            );
            parentColumn.IsForeignKey = true;
            live.Diagram.Relationships.Add(
                NewRelationship(entity, entity, [(idColumn, parentColumn)], name: null)
            );
        }
    }

    /// <summary>子の表に親の主キーを指す列を足し、外部キーを張る</summary>
    private void AddForeignKeyColumns(
        FuzzLiveSchema live,
        Entity child,
        Entity parent,
        string baseName
    )
    {
        var parentKey = parent.GetPrimaryKeyColumnsInOrder();
        var parentRows = live.RowCounts[parent.TableName];
        var childRows = live.RowCounts[child.TableName];

        // 親に行が無いのに子に行があるなら、参照列は NULL を許して NULL を入れる
        var mustBeNull = parentRows == 0 && childRows > 0;
        var nullable = mustBeNull || rng.Next(3) == 0;
        var pairs = new List<(Column Parent, Column Child)>();

        for (var p = 0; p < parentKey.Count; p++)
        {
            var parentColumn = parentKey[p];
            var parentInfo = live.Find(parent.TableName, parentColumn.Name)!;
            var name = parentKey.Count == 1 ? baseName : $"{baseName}_{p}";
            var childColumn = AddColumn(
                live,
                child,
                name,
                parentInfo.Kind,
                parentInfo.Length,
                nullable,
                r => mustBeNull ? null : parentInfo.Values[(r - 1) % parentRows]
            );
            childColumn.IsForeignKey = true;
            pairs.Add((parentColumn, childColumn));
        }

        var relationship = NewRelationship(
            parent,
            child,
            pairs,
            name: rng.Next(2) == 0 ? $"FK_{NamePart(child.TableName)}_{baseName}" : null
        );
        relationship.OnDelete = PickAction(nullable);
        relationship.OnUpdate =
            rng.Next(2) == 0 ? PickAction(nullable) : ForeignKeyReferentialAction.NoAction;
        live.Diagram.Relationships.Add(relationship);
    }

    /// <summary>同じ子列に、別の親への外部キーをもう 1 本張る（整数 1 列の主キーで行数が同じ親どうし）</summary>
    private void AddSameColumnTwoForeignKeys(FuzzLiveSchema live)
    {
        if (rng.Next(10) >= 4)
        {
            return;
        }

        foreach (var relationship in live.Diagram.Relationships.ToList())
        {
            var parent = EntityOf(live, relationship.SourceEntityId);
            var child = EntityOf(live, relationship.TargetEntityId);

            if (
                ReferenceEquals(parent, child)
                || relationship.ColumnPairs.Count != 1
                || !IsSingleIntPrimaryKey(parent)
            )
            {
                continue;
            }

            var childColumn = child.Columns.Single(c =>
                c.Id == relationship.ColumnPairs[0].TargetColumnId
            );
            var other = live.Diagram.Entities.FirstOrDefault(e =>
                !ReferenceEquals(e, parent)
                && !ReferenceEquals(e, child)
                && IsSingleIntPrimaryKey(e)
                && live.RowCounts[e.TableName] >= live.RowCounts[parent.TableName]
                && live.Diagram.Entities.IndexOf(e) < live.Diagram.Entities.IndexOf(child)
            );

            if (other is null)
            {
                continue;
            }

            live.Shapes.Add(FuzzShape.SameColumnTwoForeignKeys);
            var otherKey = other.Columns.Single(c => c.IsPrimaryKey);
            live.Diagram.Relationships.Add(
                NewRelationship(
                    other,
                    child,
                    [(otherKey, childColumn)],
                    name: $"FK_{NamePart(child.TableName)}_{BaseName(other)}_also"
                )
            );
            return;
        }
    }

    /// <summary>同じ列ペアに、同じ親への外部キーを別の名前でもう 1 本張る</summary>
    private void AddSamePairTwoForeignKeys(FuzzLiveSchema live)
    {
        if (!dialect.AllowDuplicateForeignKeys || rng.Next(10) >= 5)
        {
            return;
        }

        var source = live
            .Diagram.Relationships.Where(r => r.SourceEntityId != r.TargetEntityId)
            .OrderBy(_ => rng.Next())
            .FirstOrDefault();

        if (source is null)
        {
            return;
        }

        live.Shapes.Add(FuzzShape.SamePairTwoForeignKeys);
        var child = EntityOf(live, source.TargetEntityId);
        source.ConstraintName ??= $"FK_{NamePart(child.TableName)}_first";
        live.Diagram.Relationships.Add(
            new Relationship
            {
                SourceEntityId = source.SourceEntityId,
                TargetEntityId = source.TargetEntityId,
                Type = RelationshipType.OneToMany,
                ColumnPairs = [.. source.ColumnPairs.Select(p => p.Clone())],
                ConstraintName = $"FK_{NamePart(child.TableName)}_second",
                OnDelete = ForeignKeyReferentialAction.NoAction,
            }
        );
    }

    /// <summary>一意制約（名前付き・名前なし・外部キーの子列）を張る</summary>
    private void AddUniqueConstraints(FuzzLiveSchema live)
    {
        foreach (var entity in live.Diagram.Entities)
        {
            if (rng.Next(10) >= 5)
            {
                continue;
            }

            var candidates = entity
                .Columns.Where(c =>
                    !c.IsPrimaryKey
                    && live.Find(entity.TableName, c.Name) is { } info
                    && IsUniqueKind(info.Kind)
                    && (live.RowCounts[entity.TableName] == 0 || info.IsDistinct)
                )
                .ToList();

            if (candidates.Count == 0)
            {
                continue;
            }

            var column = candidates[rng.Next(candidates.Count)];
            var named = rng.Next(2) == 0;

            if (named)
            {
                live.Shapes.Add(FuzzShape.NamedUnique);
            }

            if (column.IsForeignKey)
            {
                live.Shapes.Add(FuzzShape.UniqueOnForeignKey);
            }

            entity.UniqueConstraints.Add(
                new UniqueConstraint
                {
                    Name = named ? $"UQ_{NamePart(entity.TableName)}_{column.Name}_named" : null,
                    ColumnIds = [column.Id],
                }
            );

            // ときどき、同じ列の並びに別の名前の一意制約をもう 1 つ張る（片方だけを外して型を変える形の相手）
            if (named && dialect.AllowDuplicateUniqueConstraints && rng.Next(3) == 0)
            {
                live.Shapes.Add(FuzzShape.SameSetTwoUniques);
                entity.UniqueConstraints.Add(
                    new UniqueConstraint
                    {
                        Name = $"UQ_{NamePart(entity.TableName)}_{column.Name}_twin",
                        ColumnIds = [column.Id],
                    }
                );
            }
        }
    }

    /// <summary>
    /// 一意制約を張る列の型の種類（日時・バイト列は張らない＝これらの型を混ぜる目的は列の型の変更と綴りで、一意制約の形として
    /// 足すものが無い）
    /// </summary>
    private static bool IsUniqueKind(FuzzTypeKind kind) =>
        kind is not (FuzzTypeKind.DateTime or FuzzTypeKind.Binary);

    /// <summary>計算列を 1 本足す（整数の普通の列を参照する。参照される列は以後変更しない）</summary>
    private void AddComputedColumn(FuzzLiveSchema live)
    {
        if (rng.Next(10) >= 4)
        {
            return;
        }

        foreach (var entity in live.Diagram.Entities.OrderBy(_ => rng.Next()))
        {
            var source = entity.Columns.FirstOrDefault(c =>
                !c.IsPrimaryKey
                && !c.IsForeignKey
                && live.Find(entity.TableName, c.Name)?.Kind == FuzzTypeKind.Int
            );

            if (source is null)
            {
                continue;
            }

            live.Shapes.Add(FuzzShape.Computed);
            live.Find(entity.TableName, source.Name)!.Pinned = true;
            live.Columns[FuzzLiveSchema.Key(entity.TableName, "calc")] = new FuzzColumnInfo
            {
                Kind = FuzzTypeKind.Int,
                Computed = true,
            };
            live.ComputedColumnStatements.Add(
                dialect.AddComputedColumnSql(entity.TableName, "calc", source.Name)
            );
            return;
        }
    }

    /// <summary>データを入れる文を親→子の順（＝表の作成順）で組み立てる（決定的な行も使う）</summary>
    internal static void BuildInserts(FuzzLiveSchema live, SyncPlanFuzzDialect dialect)
    {
        foreach (var entity in live.Diagram.Entities)
        {
            var rows = live.RowCounts[entity.TableName];
            var columns = entity.Columns;
            var columnList = string.Join(", ", columns.Select(c => dialect.Quote(c.Name)));

            for (var r = 0; r < rows; r++)
            {
                var values = string.Join(
                    ", ",
                    columns.Select(c => live.Find(entity.TableName, c.Name)!.Values[r] ?? "NULL")
                );
                live.InsertStatements.Add(
                    $"INSERT INTO {dialect.QuoteTable(entity.TableName)} ({columnList}) VALUES ({values});"
                );
            }
        }
    }

    // ---------------- 目標の図への変更 ----------------

    /// <summary>
    /// 取り込んだ DB の図（新しい Guid 空間へ写したもの）に、変更をランダムに 1〜4 個加える。
    /// </summary>
    public void Mutate(List<Entity> entities, List<Relationship> relationships, FuzzLiveSchema live)
    {
        // 変更の種類と重み（主キー・名前付きの一意制約・計算列・親子の表の削除は欠陥が出やすい形なので重くする）
        (int Weight, Func<bool> Apply)[] kinds =
        [
            (1, () => RenameCase(entities, live)),
            (1, () => AddTable(entities, relationships, live)),
            (1, () => DropTable(entities, relationships)),
            (1, () => DropParentAndChild(entities, relationships)),
            (1, () => AddPlainColumn(entities, live)),
            (1, () => DropColumn(entities, relationships, live)),
            (3, () => DropKeyMemberColumn(entities, relationships, live)),
            (2, () => AlterType(entities, relationships, live)),
            (1, () => ToggleNullable(entities, relationships, live)),
            (3, () => ChangePrimaryKey(entities, relationships, live)),
            (1, () => AddForeignKey(entities, relationships, live)),
            (1, () => DropForeignKey(entities, relationships)),
            (1, () => ChangeForeignKeyAction(entities, relationships)),
            (1, () => AddUnique(entities, live)),
            (1, () => DropUnique(entities)),
            (2, () => ChangeUniqueColumns(entities, live)),
            (1, () => ChangeComputed(entities, live)),
            (1, () => ReorderColumns(entities)),
            (1, () => ReorderReferencedCompositeKey(entities, relationships)),
            (1, () => RetargetForeignKey(entities, relationships, live)),
            (1, () => AlterDuplicatedForeignKeyColumn(entities, relationships, live)),
            (1, () => DropDuplicatedUniqueWithTypeChange(entities, live)),
            (1, () => RespellType(entities, live)),
        ];
        var total = kinds.Sum(k => k.Weight);
        var count = rng.Next(1, 6);
        var attempts = 0;

        while (Mutations.Count < count && attempts++ < 60)
        {
            var roll = rng.Next(total);

            foreach (var (weight, apply) in kinds)
            {
                if (roll < weight)
                {
                    apply();
                    break;
                }

                roll -= weight;
            }
        }
    }

    private bool RenameCase(List<Entity> entities, FuzzLiveSchema live)
    {
        var entity = Pick(entities);

        if (entity is null)
        {
            return false;
        }

        // 列を持たない表（列をすべて消した表）は表の名前を変える（乱数の引き方は変えない＝種の指紋を保つ）
        if (rng.Next(2) == 0 || entity.Columns.Count == 0)
        {
            var renamed = FlipCase(entity.TableName);
            Mutations.Add($"rename table case {entity.TableName} -> {renamed}");
            entity.TableName = renamed;
        }
        else
        {
            var column = entity.Columns[rng.Next(entity.Columns.Count)];
            var renamed = FlipCase(column.Name);
            Mutations.Add($"rename column case {entity.TableName}.{column.Name} -> {renamed}");
            column.Name = renamed;
        }

        live.Shapes.Add(FuzzShape.CaseOnlyName);
        return true;
    }

    private bool AddTable(
        List<Entity> entities,
        List<Relationship> relationships,
        FuzzLiveSchema live
    )
    {
        var name = $"new_{entities.Count}_{rng.Next(100)}";
        var entity = new Entity { TableName = name };
        var id = new Column
        {
            Name = "id",
            DataType = dialect.TypeName(FuzzTypeKind.Int, 0),
            IsPrimaryKey = true,
            IsNullable = false,
        };
        entity.Columns.Add(id);
        entity.Columns.Add(
            new Column { Name = "v", DataType = dialect.TypeName(FuzzTypeKind.VarChar, 20) }
        );
        var mutation = $"add table {name}";

        // 既存の表への外部キー（整数 1 列の主キーの親）も持たせる
        var parent = entities
            .Where(IsSingleIntPrimaryKey)
            .OrderBy(_ => rng.Next())
            .FirstOrDefault();

        if (parent is not null && rng.Next(2) == 0)
        {
            var reference = new Column
            {
                Name = "ref_id",
                DataType = dialect.TypeName(FuzzTypeKind.Int, 0),
                IsForeignKey = true,
            };
            entity.Columns.Add(reference);
            relationships.Add(
                NewRelationship(
                    parent,
                    entity,
                    [(parent.Columns.Single(c => c.IsPrimaryKey), reference)],
                    name: null
                )
            );
            mutation += $" (fk -> {parent.TableName})";
        }

        // 新しい表を参照する外部キーを既存の表に足す（新しい表を選ばない選び方の形）
        var child = Pick(entities);

        if (child is not null && rng.Next(3) == 0)
        {
            var column = new Column
            {
                Name = $"{name}_ref",
                DataType = dialect.TypeName(FuzzTypeKind.Int, 0),
                IsForeignKey = true,
            };
            child.Columns.Add(column);
            relationships.Add(NewRelationship(entity, child, [(id, column)], name: null));
            mutation += $" (referenced by {child.TableName}.{column.Name})";
        }

        entities.Add(entity);
        live.RowCounts[name] = 0;
        Mutations.Add(mutation);
        return true;
    }

    private bool DropTable(List<Entity> entities, List<Relationship> relationships)
    {
        var entity = Pick(entities);

        if (entity is null)
        {
            return false;
        }

        entities.Remove(entity);
        relationships.RemoveAll(r =>
            r.SourceEntityId == entity.Id || r.TargetEntityId == entity.Id
        );
        Mutations.Add($"drop table {entity.TableName}");
        return true;
    }

    /// <summary>外部キーでつながった親と子の表を両方消す（外部キーの削除は差分の既定では選ばれない）</summary>
    private bool DropParentAndChild(List<Entity> entities, List<Relationship> relationships)
    {
        var relationship = relationships
            .Where(r => r.SourceEntityId != r.TargetEntityId)
            .OrderBy(_ => rng.Next())
            .FirstOrDefault();

        if (relationship is null)
        {
            return false;
        }

        var parent = entities.Single(e => e.Id == relationship.SourceEntityId);
        var child = entities.Single(e => e.Id == relationship.TargetEntityId);

        foreach (var entity in new[] { parent, child })
        {
            entities.Remove(entity);
            relationships.RemoveAll(r =>
                r.SourceEntityId == entity.Id || r.TargetEntityId == entity.Id
            );
        }

        Mutations.Add($"drop tables {parent.TableName} and child {child.TableName}");
        return true;
    }

    private bool AddPlainColumn(List<Entity> entities, FuzzLiveSchema live)
    {
        var entity = Pick(entities);

        if (entity is null)
        {
            return false;
        }

        // データのある表へは NULL を許す列だけを足す（NOT NULL を足すと DB の都合で失敗する）
        var empty = RowCount(live, entity) == 0;
        var name = $"added_{rng.Next(1000)}";
        var kind = rng.Next(2) == 0 ? FuzzTypeKind.Int : FuzzTypeKind.VarChar;
        var nullable = !empty || rng.Next(2) == 0;
        entity.Columns.Add(
            new Column
            {
                Name = name,
                DataType = dialect.TypeName(kind, 30),
                IsNullable = nullable,
            }
        );
        Mutations.Add(
            $"add column {entity.TableName}.{name} {kind}{(nullable ? " null" : " not null")}"
        );
        return true;
    }

    private bool DropColumn(
        List<Entity> entities,
        List<Relationship> relationships,
        FuzzLiveSchema live
    )
    {
        // 主キーの列も、主キーを縮めてもデータが壊れないときは候補にする（その表の主キーの変更を選ばずに列の削除だけを
        // 選ぶ形を作るため）
        var entity = Pick(entities);
        var column = entity
            ?.Columns.Where(c =>
                !(Info(live, entity!, c)?.Pinned ?? false)
                && (!c.IsPrimaryKey || CanShrinkPrimaryKey(live, entity!, c))
            )
            .OrderBy(_ => rng.Next())
            .FirstOrDefault();

        if (entity is null || column is null)
        {
            return false;
        }

        RemoveColumn(entity, column, relationships);
        return true;
    }

    /// <summary>
    /// 主キーか一意制約の構成列を 1 つ消す（差分の列の削除だけを選ぶと、その主キー・一意制約が消える形を作る）。
    /// </summary>
    private bool DropKeyMemberColumn(
        List<Entity> entities,
        List<Relationship> relationships,
        FuzzLiveSchema live
    )
    {
        // 主キーの列と一意制約の構成列を半々で選ぶ（どちらの形も種の集合で必ず出るように）
        var primaryKey = rng.Next(2) == 0;
        var candidates = entities
            .SelectMany(e =>
                e.Columns.Where(c =>
                        !(Info(live, e, c)?.Pinned ?? false)
                        && (
                            primaryKey
                                ? c.IsPrimaryKey && CanShrinkPrimaryKey(live, e, c)
                                // 一意制約の構成列でも主キーの列なら、主キーが縮んでも DB の行が主キーを満たす列だけ
                                // （DropColumn と同じ条件。満たさないと DB の都合だけで失敗する）
                                : e.UniqueConstraints.Any(u => u.ColumnIds.Contains(c.Id))
                                    && (!c.IsPrimaryKey || CanShrinkPrimaryKey(live, e, c))
                        )
                    )
                    .Select(c => (Entity: e, Column: c))
            )
            .ToList();

        if (candidates.Count == 0)
        {
            return false;
        }

        var (entity, column) = candidates[rng.Next(candidates.Count)];
        RemoveColumn(entity, column, relationships);
        return true;
    }

    /// <summary>
    /// 主キーからこの列を外しても DB の行が主キーを満たすか（行が無い・残りの主キーが無い・残りの列に値が重ならない列がある）
    /// </summary>
    private static bool CanShrinkPrimaryKey(FuzzLiveSchema live, Entity entity, Column column)
    {
        var remaining = entity.GetPrimaryKeyColumnsInOrder().Where(c => c.Id != column.Id).ToList();

        return RowCount(live, entity) == 0
            || remaining.Count == 0
            || remaining.Any(c => Info(live, entity, c)?.IsDistinct == true);
    }

    /// <summary>目標の図から列を消し、その列を含む一意制約・外部キーも消す</summary>
    private void RemoveColumn(Entity entity, Column column, List<Relationship> relationships)
    {
        entity.Columns.Remove(column);
        entity.UniqueConstraints.RemoveAll(u => u.ColumnIds.Contains(column.Id));
        relationships.RemoveAll(r =>
            r.ColumnPairs.Any(p => p.SourceColumnId == column.Id || p.TargetColumnId == column.Id)
        );
        Mutations.Add($"drop column {entity.TableName}.{column.Name}");
    }

    private bool AlterType(
        List<Entity> entities,
        List<Relationship> relationships,
        FuzzLiveSchema live
    )
    {
        var entity = Pick(entities);
        var column = entity
            ?.Columns.Where(c => Info(live, entity!, c) is { Pinned: false, Computed: false })
            .OrderBy(_ => rng.Next())
            .FirstOrDefault();

        if (entity is null || column is null)
        {
            return false;
        }

        var info = Info(live, entity, column)!;
        var (kind, length) = NextType(info);

        // データのある日時の列の精度を下げる変更を DB が断る方言（Oracle の ORA-30082）では作らない
        if (
            kind == FuzzTypeKind.DateTime
            && info.Kind == FuzzTypeKind.DateTime
            && length < info.Length
            && RowCount(live, entity) > 0
            && !dialect.AllowsDateTimePrecisionDecreaseWithData
        )
        {
            return false;
        }

        // 外部キーの両端は半々の確率でそろえて変える（そろえない形は型の食い違いの警告の相手）
        var partners = new List<(Entity Entity, Column Column)> { (entity, column) };

        if (rng.Next(2) == 0)
        {
            CollectForeignKeyPartners(entities, relationships, column, partners);

            // 相手が 2 つ以上（同じ列に外部キーが 2 本）なら、ときどき 1 つだけそろえる（片方だけ型が食い違う形）
            if (partners.Count > 2 && rng.Next(2) == 0)
            {
                partners.RemoveRange(2, partners.Count - 2);
            }
        }

        foreach (var (owner, partner) in partners)
        {
            partner.DataType = dialect.TypeName(kind, length);
            Mutations.Add(
                $"alter type {owner.TableName}.{partner.Name} -> {partner.DataType}{TypeTag(kind, length)}"
            );
        }

        return true;
    }

    /// <summary>
    /// 型の変更の行き先の札（網の因子 AlteredType の水準＝型の種類か、方言の上限ちょうどの型なら <c>AtLimit</c>）
    /// </summary>
    private string TypeTag(FuzzTypeKind kind, int length) =>
        $" {{type={(dialect.LimitType == (kind, length) ? "AtLimit" : kind.ToString())}}}";

    /// <summary>
    /// 同じ列の並びの一意制約 2 つの片方を消し、その列の型を変える（残る一意制約が型の変更を阻む形＝SQL Server の Msg 5074）
    /// </summary>
    private bool DropDuplicatedUniqueWithTypeChange(List<Entity> entities, FuzzLiveSchema live)
    {
        foreach (var entity in entities.OrderBy(_ => rng.Next()))
        {
            var pair = entity
                .UniqueConstraints.GroupBy(u => string.Join(",", u.ColumnIds))
                .FirstOrDefault(g => g.Count() >= 2);

            if (pair is null || pair.First().ColumnIds.Count != 1)
            {
                continue;
            }

            var column = entity.Columns.FirstOrDefault(c => c.Id == pair.First().ColumnIds[0]);

            if (
                column is null
                || column.IsPrimaryKey
                || column.IsForeignKey
                || Info(live, entity, column) is not { Pinned: false, Computed: false } info
            )
            {
                continue;
            }

            var dropped = pair.ElementAt(rng.Next(2));
            entity.UniqueConstraints.Remove(dropped);
            var (kind, length) = NextType(info);
            column.DataType = dialect.TypeName(kind, length);
            Mutations.Add(
                $"alter type {entity.TableName}.{column.Name} -> {column.DataType}{TypeTag(kind, length)} (duplicated unique)"
            );
            Mutations.Add(
                $"drop one of duplicated uniques {entity.TableName} ({dropped.Name ?? "unnamed"})"
            );
            return true;
        }

        return false;
    }

    /// <summary>
    /// 日時の列の型を、同じ型の別の綴り（既定の精度を明示した綴りと書かない綴り）へ書き換える（差分を出してはいけない）
    /// </summary>
    private bool RespellType(List<Entity> entities, FuzzLiveSchema live)
    {
        foreach (var entity in entities.OrderBy(_ => rng.Next()))
        {
            foreach (var column in entity.Columns)
            {
                if (
                    Info(live, entity, column)
                        is not { Kind: FuzzTypeKind.DateTime, Computed: false } info
                    || info.Length != dialect.DefaultDateTimePrecision
                    || dialect.RespelledTypeName(info.Kind, info.Length) is not { } respelled
                )
                {
                    continue;
                }

                // 同じ種で型を変えた列（既定の精度でない綴り）は書き換えない（型の変更を取り消してしまう）
                var plain = dialect.TypeName(info.Kind, info.Length);

                if (
                    !DbTypeText.AreEquivalent(column.DataType, plain)
                    && !DbTypeText.AreEquivalent(column.DataType, respelled)
                )
                {
                    continue;
                }

                column.DataType = DbTypeText.AreEquivalent(column.DataType, plain)
                    ? respelled
                    : plain;
                Mutations.Add(
                    $"respell type {entity.TableName}.{column.Name} -> {column.DataType}"
                );
                return true;
            }
        }

        return false;
    }

    private bool ToggleNullable(
        List<Entity> entities,
        List<Relationship> relationships,
        FuzzLiveSchema live
    )
    {
        // SET NULL の外部キーの子列は NOT NULL にできない（DB の都合）ので対象にしない
        var setNullChildren = SetNullChildColumnIds(relationships);
        var entity = Pick(entities);
        // 計算列の式が参照する列も対象にしない（式への依存は意味モデルに無く、QuickER には見えない DB の都合）
        var column = entity
            ?.Columns.Where(c =>
                !c.IsPrimaryKey
                && !c.IsComputed
                && !setNullChildren.Contains(c.Id)
                && !(Info(live, entity!, c)?.Pinned ?? false)
            )
            .OrderBy(_ => rng.Next())
            .FirstOrDefault();

        if (entity is null || column is null)
        {
            return false;
        }

        // NULL を含む列・データのある表へ足したばかりの列（値は NULL になる）は NOT NULL にしない（DB の都合で失敗する）
        if (
            column.IsNullable
            && RowCount(live, entity) > 0
            && (Info(live, entity, column) is not { } info || !info.HasNoNull)
        )
        {
            return false;
        }

        column.IsNullable = !column.IsNullable;
        Mutations.Add(
            $"nullable {entity.TableName}.{column.Name} -> {(column.IsNullable ? "null" : "not null")}"
        );
        return true;
    }

    private bool ChangePrimaryKey(
        List<Entity> entities,
        List<Relationship> relationships,
        FuzzLiveSchema live
    )
    {
        var entity = Pick(entities);

        if (entity is null)
        {
            return false;
        }

        var key = entity.GetPrimaryKeyColumnsInOrder();
        var rows = RowCount(live, entity);

        // 主キーへ足せる列（NULL を含まず、計算列・小数でない列）。SET NULL の外部キーの子列は主キーにすると NOT NULL になり、
        // 外部キーを作れない（DB の都合＝ToggleNullable と同じ）ので対象にしない。移す先・作り直しで残す列もこの中から選ぶ
        var setNullChildren = SetNullChildColumnIds(relationships);
        var extendable = entity
            .Columns.Where(c =>
                !c.IsPrimaryKey
                && !c.IsComputed
                && !setNullChildren.Contains(c.Id)
                && !(Info(live, entity, c)?.Pinned ?? false)
                && (rows == 0 || Info(live, entity, c)?.HasNoNull == true)
                && Info(live, entity, c)?.Kind is FuzzTypeKind.Int or FuzzTypeKind.VarChar
            )
            .OrderBy(_ => rng.Next())
            .ToList();
        var options = new List<Func<bool>>();

        // 複合主キーの順序の入れ替え
        if (key.Count >= 2)
        {
            options.Add(() =>
            {
                SetPrimaryKey(entity, [key[1], key[0], .. key.Skip(2)]);
                Mutations.Add(
                    $"reorder pk {entity.TableName}{ReferencingTag(entity, relationships)}"
                );
                return true;
            });
        }

        // 列を主キーへ足す（主キーが空なら、足した列だけが主キーになるので、行のある表では一意な値の列だけ。一意でない値の列への
        // 主キーは DB がデータで断る＝計画は値を知らない）
        var extension =
            key.Count > 0 || rows == 0
                ? extendable.FirstOrDefault()
                : extendable.FirstOrDefault(c => Info(live, entity, c)?.IsDistinct == true);

        if (extension is not null)
        {
            options.Add(() =>
            {
                SetPrimaryKey(entity, [.. key, extension]);
                Mutations.Add(
                    $"extend pk {entity.TableName} + {extension.Name}{ReferencingTag(entity, relationships)}"
                );
                return true;
            });
        }

        // 複合主キーから列を外す（残りで一意になる場合だけ）
        if (key.Count >= 2 && Info(live, entity, key[0])?.IsDistinct != false)
        {
            options.Add(() =>
            {
                SetPrimaryKey(entity, [key[0]]);
                Mutations.Add(
                    $"shrink pk {entity.TableName} -> {key[0].Name}{ReferencingTag(entity, relationships)}"
                );
                return true;
            });
        }

        // 主キーを別の列（一意な値の列）へ移す
        var movable = extendable.FirstOrDefault(c =>
            rows == 0 || Info(live, entity, c)?.IsDistinct == true
        );

        if (movable is not null)
        {
            options.Add(() =>
            {
                SetPrimaryKey(entity, [movable]);
                Mutations.Add(
                    $"move pk {entity.TableName} -> {movable.Name}{ReferencingTag(entity, relationships)}"
                );
                return true;
            });
        }

        // 既存の列 1 つと新しい列で主キーを作り直す（行の無い表だけ＝NOT NULL の列を足せる。
        // 「新しい列を選ばず主キー変更だけ選ぶ」の相手）
        if (rows == 0)
        {
            options.Add(() =>
            {
                var column = new Column
                {
                    Name = $"pk_new_{rng.Next(1000)}",
                    DataType = dialect.TypeName(FuzzTypeKind.Int, 0),
                    IsNullable = false,
                };
                var keep = extendable.Take(1).ToList();
                entity.Columns.Add(column);
                SetPrimaryKey(entity, [.. keep, column]);
                Mutations.Add(
                    $"replace pk {entity.TableName} with new column {column.Name}{ReferencingTag(entity, relationships)}"
                );
                return true;
            });
        }

        // 主キーを外す
        options.Add(() =>
        {
            SetPrimaryKey(entity, []);
            Mutations.Add($"remove pk {entity.TableName}{ReferencingTag(entity, relationships)}");
            return true;
        });

        return options[rng.Next(options.Count)]();
    }

    /// <summary>ON DELETE / ON UPDATE が SET NULL の外部キーの子列</summary>
    private static HashSet<Guid> SetNullChildColumnIds(List<Relationship> relationships) =>
        relationships
            .Where(r =>
                r.OnDelete == ForeignKeyReferentialAction.SetNull
                || r.OnUpdate == ForeignKeyReferentialAction.SetNull
            )
            .SelectMany(r => r.ColumnPairs.Select(p => p.TargetColumnId))
            .ToHashSet();

    private bool AddForeignKey(
        List<Entity> entities,
        List<Relationship> relationships,
        FuzzLiveSchema live
    )
    {
        var parent = entities
            .Where(IsSingleIntPrimaryKey)
            .OrderBy(_ => rng.Next())
            .FirstOrDefault();
        var child = Pick(entities);

        if (parent is null || child is null)
        {
            return false;
        }

        var parentKey = parent.Columns.Single(c => c.IsPrimaryKey);
        var parentIds = Info(live, parent, parentKey)?.Values ?? [];

        // 既存の整数の列が親の主キーの値だけを持つなら、その列へ張る。無ければ NULL を許す列を足して張る
        var existing = child
            .Columns.Where(c =>
                !c.IsComputed
                && c != parentKey
                && string.Equals(c.DataType, parentKey.DataType, StringComparison.OrdinalIgnoreCase)
                && Info(live, child, c) is { Kind: FuzzTypeKind.Int } info
                && info.Values.All(v => v is null || parentIds.Contains(v))
                && (RowCount(live, child) == 0 || RowCount(live, parent) > 0)
                && CanAddForeignKey(
                    relationships,
                    parent,
                    c,
                    ForeignKeyReferentialAction.NoAction,
                    ForeignKeyReferentialAction.NoAction
                )
            )
            .OrderBy(_ => rng.Next())
            .FirstOrDefault();
        var column = existing;

        if (column is null)
        {
            column = new Column { Name = $"fk_{rng.Next(1000)}", DataType = parentKey.DataType };
            child.Columns.Add(column);
        }

        relationships.Add(
            NewRelationship(
                parent,
                child,
                [(parentKey, column)],
                name: rng.Next(2) == 0
                    ? UnusedConstraintName($"FK_added_{rng.Next(1000)}", entities, relationships)
                    : null
            )
        );
        Mutations.Add($"add fk {child.TableName}.{column.Name} -> {parent.TableName}");
        return true;
    }

    private bool DropForeignKey(List<Entity> entities, List<Relationship> relationships)
    {
        // 同じ子列にもう 1 本ある外部キーを半々の確率で優先する（2 本のうち 1 本だけ消す形）
        var duplicated = relationships
            .Where(r =>
                relationships.Any(o =>
                    o != r
                    && o.TargetEntityId == r.TargetEntityId
                    && o.ColumnPairs.Select(p => p.TargetColumnId)
                        .SequenceEqual(r.ColumnPairs.Select(p => p.TargetColumnId))
                )
            )
            .ToList();
        var relationship = (duplicated.Count > 0 && rng.Next(2) == 0 ? duplicated : relationships)
            .OrderBy(_ => rng.Next())
            .FirstOrDefault();

        if (relationship is null)
        {
            return false;
        }

        relationships.Remove(relationship);
        Mutations.Add(
            $"drop fk {Name(entities, relationship.TargetEntityId)} -> {Name(entities, relationship.SourceEntityId)} ({relationship.ConstraintName ?? "unnamed"})"
        );
        return true;
    }

    private bool ChangeForeignKeyAction(List<Entity> entities, List<Relationship> relationships)
    {
        var relationship = relationships.OrderBy(_ => rng.Next()).FirstOrDefault();

        if (relationship is null || !dialect.AllowCascade)
        {
            return false;
        }

        // ON UPDATE も変える（Oracle は ON UPDATE を持たず、MySQL は SET DEFAULT を効かせない＝差分は方言の実効値へ畳む）。
        // SET NULL・SET DEFAULT は子の列がすべて NULL を許すときだけ
        var onUpdate = rng.Next(3) == 0;
        var current = onUpdate ? relationship.OnUpdate : relationship.OnDelete;
        var nullable = relationship.ColumnPairs.All(p =>
            entities.SelectMany(e => e.Columns).FirstOrDefault(c => c.Id == p.TargetColumnId)
                is { IsNullable: true }
        );
        ForeignKeyReferentialAction[] choices =
        [
            ForeignKeyReferentialAction.NoAction,
            ForeignKeyReferentialAction.Cascade,
            .. nullable
                ? new[]
                {
                    ForeignKeyReferentialAction.SetNull,
                    ForeignKeyReferentialAction.SetDefault,
                }
                : [],
        ];
        var others = choices.Where(a => a != current).ToList();
        var action = others[rng.Next(others.Count)];

        if (onUpdate)
        {
            relationship.OnUpdate = action;
        }
        else
        {
            relationship.OnDelete = action;
        }

        Mutations.Add(
            $"fk action {Name(entities, relationship.TargetEntityId)} -> {Name(entities, relationship.SourceEntityId)} {(onUpdate ? "ON UPDATE" : "ON DELETE")} {action}"
        );
        return true;
    }

    private bool AddUnique(List<Entity> entities, FuzzLiveSchema live)
    {
        var entity = Pick(entities);
        var column = entity
            ?.Columns.Where(c =>
                !c.IsPrimaryKey
                && !c.IsComputed
                && Info(live, entity!, c) is { } info
                && info.Kind != FuzzTypeKind.Decimal
                && IsUniqueKind(info.Kind)
                && (RowCount(live, entity!) == 0 || info.IsDistinct)
                && (
                    dialect.AllowDuplicateUniqueConstraints
                        && dialect.DistinguishesSameSetConstraints
                    || !entity!.UniqueConstraints.Any(u => u.ColumnIds.SequenceEqual([c.Id]))
                )
            )
            .OrderBy(_ => rng.Next())
            .FirstOrDefault();

        if (entity is null || column is null)
        {
            return false;
        }

        // 同じ列の並びにもう一意制約があるなら名前を付ける（名前なしの 2 つは同じ合成名になる＝製品の入口は作らない図）
        var duplicate = entity.UniqueConstraints.Any(u => u.ColumnIds.SequenceEqual([column.Id]));
        var named = rng.Next(2) == 0 || duplicate;
        entity.UniqueConstraints.Add(
            new UniqueConstraint
            {
                Name = named
                    ? UnusedConstraintName($"UQ_added_{rng.Next(1000)}", entities, [])
                    : null,
                ColumnIds = [column.Id],
            }
        );
        Mutations.Add($"add unique {entity.TableName}({column.Name}){(named ? " named" : "")}");
        return true;
    }

    private bool DropUnique(List<Entity> entities)
    {
        var entity = entities
            .Where(e => e.UniqueConstraints.Count > 0)
            .OrderBy(_ => rng.Next())
            .FirstOrDefault();

        if (entity is null)
        {
            return false;
        }

        var unique = entity.UniqueConstraints[rng.Next(entity.UniqueConstraints.Count)];
        entity.UniqueConstraints.Remove(unique);
        Mutations.Add($"drop unique {entity.TableName} {unique.Name ?? "(unnamed)"}");
        return true;
    }

    private bool ChangeUniqueColumns(List<Entity> entities, FuzzLiveSchema live)
    {
        var entity = entities
            .Where(e => e.UniqueConstraints.Any(u => u.Name is not null))
            .OrderBy(_ => rng.Next())
            .FirstOrDefault();

        if (entity is null)
        {
            return false;
        }

        var unique = entity.UniqueConstraints.First(u => u.Name is not null);
        var column = entity
            .Columns.Where(c =>
                !c.IsComputed
                && !unique.ColumnIds.Contains(c.Id)
                && Info(live, entity, c) is { } info
                && info.Kind != FuzzTypeKind.Decimal
                && IsUniqueKind(info.Kind)
                && (RowCount(live, entity) == 0 || info.IsDistinct)
            )
            .OrderBy(_ => rng.Next())
            .FirstOrDefault();

        if (column is null)
        {
            return false;
        }

        unique.ColumnIds = [column.Id];
        Mutations.Add($"change unique {entity.TableName} {unique.Name} -> ({column.Name})");
        return true;
    }

    private bool ChangeComputed(List<Entity> entities, FuzzLiveSchema live)
    {
        var entity = entities.FirstOrDefault(e => e.Columns.Any(c => c.IsComputed));

        if (entity is null)
        {
            return false;
        }

        var column = entity.Columns.First(c => c.IsComputed);

        if (rng.Next(2) == 0)
        {
            column.DataType = dialect.TypeName(FuzzTypeKind.BigInt, 0);
            Mutations.Add($"computed type {entity.TableName}.{column.Name} -> {column.DataType}");
        }
        else
        {
            column.IsNullable = !column.IsNullable;
            Mutations.Add(
                $"computed nullable {entity.TableName}.{column.Name} -> {column.IsNullable}"
            );
        }

        live.Shapes.Add(FuzzShape.Computed);
        return true;
    }

    private bool ReorderColumns(List<Entity> entities)
    {
        var entity = entities
            .Where(e => e.Columns.Count >= 3)
            .OrderBy(_ => rng.Next())
            .FirstOrDefault();

        if (entity is null)
        {
            return false;
        }

        var reordered = entity.Columns.OrderBy(_ => rng.Next()).ToList();
        entity.Columns.Clear();
        entity.Columns.AddRange(reordered);
        Mutations.Add($"reorder columns {entity.TableName}");
        return true;
    }

    /// <summary>
    /// 名前付きの外部キーの子列を、同じ名前のまま別の列へ付け替える（名前で作り直しの組になる方言では
    /// AlterForeignKey になる。MySQL では外部キーが作った同じ名前の索引が残る形）
    /// </summary>
    private bool RetargetForeignKey(
        List<Entity> entities,
        List<Relationship> relationships,
        FuzzLiveSchema live
    )
    {
        foreach (var relationship in relationships.OrderBy(_ => rng.Next()))
        {
            var parent = entities.FirstOrDefault(e => e.Id == relationship.SourceEntityId);
            var child = entities.FirstOrDefault(e => e.Id == relationship.TargetEntityId);

            if (
                parent is null
                || child is null
                || relationship.ColumnPairs.Count != 1
                || !IsSingleIntPrimaryKey(parent)
            )
            {
                continue;
            }

            var parentKey = parent.Columns.Single(c => c.IsPrimaryKey);
            var parentIds = Info(live, parent, parentKey)?.Values ?? [];
            var current = relationship.ColumnPairs[0].TargetColumnId;

            // 付け替え前も後も、主キー・一意制約の索引に乗らない列にする（MySQL は外部キーと同じ名前の索引を作る）
            bool IsIndexed(Column c) =>
                c.IsPrimaryKey || child.UniqueConstraints.Any(u => u.ColumnIds.Contains(c.Id));

            if (
                child.Columns.FirstOrDefault(c => c.Id == current) is not { } currentColumn
                || IsIndexed(currentColumn)
            )
            {
                continue;
            }

            var column = child.Columns.FirstOrDefault(c =>
                c.Id != current
                && !IsIndexed(c)
                && !c.IsComputed
                && string.Equals(c.DataType, parentKey.DataType, StringComparison.OrdinalIgnoreCase)
                && Info(live, child, c) is { } info
                && info.Values.All(v => v is null || parentIds.Contains(v))
                && CanAddForeignKey(
                    relationships,
                    parent,
                    c,
                    relationship.OnDelete,
                    relationship.OnUpdate
                )
            );

            if (column is null)
            {
                continue;
            }

            relationship.ConstraintName ??= UnusedConstraintName(
                $"FK_{NamePart(child.TableName)}_retarget",
                entities,
                relationships
            );
            relationship.ColumnPairs = [new RelationshipColumnPair(parentKey.Id, column.Id)];
            Mutations.Add(
                $"retarget fk {relationship.ConstraintName} {child.TableName} -> {column.Name}"
            );
            return true;
        }

        return false;
    }

    /// <summary>
    /// 図の制約名（外部キー・一意制約）と重ならない名前（重なれば番号を足す。乱数を引かない＝ほかの種の入力を変えない）
    /// </summary>
    private static string UnusedConstraintName(
        string name,
        List<Entity> entities,
        List<Relationship> relationships
    )
    {
        var taken = relationships
            .Select(r => r.ConstraintName)
            .Concat(entities.SelectMany(e => e.UniqueConstraints).Select(u => u.Name))
            .OfType<string>()
            .ToHashSet(SchemaNames.Comparer);
        var candidate = name;

        for (var n = 2; taken.Contains(candidate); n++)
        {
            candidate = $"{name}{n}";
        }

        return candidate;
    }

    /// <summary>
    /// 同じ列ペアに 2 本ある外部キーの列の型を両端そろえて変え、2 本のうち 1 本を消す（明示の削除と、型変更に伴う
    /// 暗黙の外し直しが同じ列ペアで重なる形）
    /// </summary>
    private bool AlterDuplicatedForeignKeyColumn(
        List<Entity> entities,
        List<Relationship> relationships,
        FuzzLiveSchema live
    )
    {
        foreach (var relationship in relationships.OrderBy(_ => rng.Next()))
        {
            var sibling = relationships.FirstOrDefault(o =>
                o != relationship
                && o.TargetEntityId == relationship.TargetEntityId
                && o.SourceEntityId == relationship.SourceEntityId
                && o.ColumnPairs.Select(p => p.TargetColumnId)
                    .SequenceEqual(relationship.ColumnPairs.Select(p => p.TargetColumnId))
            );
            var child = entities.FirstOrDefault(e => e.Id == relationship.TargetEntityId);
            var childColumn = child?.Columns.FirstOrDefault(c =>
                c.Id == relationship.ColumnPairs[0].TargetColumnId
            );

            if (
                sibling is null
                || child is null
                || childColumn is null
                || Info(live, child, childColumn) is not { Pinned: false } info
            )
            {
                continue;
            }

            var (kind, length) = NextType(info);
            var partners = new List<(Entity Entity, Column Column)> { (child, childColumn) };
            CollectForeignKeyPartners(entities, relationships, childColumn, partners);

            foreach (var (owner, partner) in partners)
            {
                partner.DataType = dialect.TypeName(kind, length);
                Mutations.Add(
                    $"alter type {owner.TableName}.{partner.Name} -> {partner.DataType}{TypeTag(kind, length)} (duplicated fk)"
                );
            }

            relationships.Remove(sibling);
            Mutations.Add(
                $"drop one of duplicated fks {child.TableName} ({sibling.ConstraintName ?? "unnamed"})"
            );
            return true;
        }

        return false;
    }

    /// <summary>外部キーに参照されている複合主キーの列順を入れ替える（参照する外部キーの付け直しが要る形）</summary>
    private bool ReorderReferencedCompositeKey(
        List<Entity> entities,
        List<Relationship> relationships
    )
    {
        var entity = entities
            .Where(e =>
                e.GetPrimaryKeyColumnsInOrder().Count >= 2
                && relationships.Any(r => r.SourceEntityId == e.Id && r.TargetEntityId != e.Id)
            )
            .OrderBy(_ => rng.Next())
            .FirstOrDefault();

        if (entity is null)
        {
            return false;
        }

        var key = entity.GetPrimaryKeyColumnsInOrder();
        SetPrimaryKey(entity, [key[1], key[0], .. key.Skip(2)]);
        Mutations.Add(
            $"reorder referenced pk {entity.TableName}{ReferencingTag(entity, relationships)}"
        );
        return true;
    }

    // ---------------- 差分の選び方 ----------------

    /// <summary>差分の選び方を決めて、項目の選択を書き換える</summary>
    public FuzzSelection Select(IReadOnlyList<SchemaDiffItem> items)
    {
        var selection = (FuzzSelection)rng.Next(5);
        var selectable = items.Where(i => i.IsSelectable).ToList();
        var single = selectable.Count == 0 ? null : selectable[rng.Next(selectable.Count)];
        var kinds = selectable
            .Select(i => i.Kind)
            .Distinct()
            .Where(_ => rng.Next(2) == 0)
            .ToHashSet();

        foreach (var item in selectable)
        {
            item.IsSelected = selection switch
            {
                FuzzSelection.All => true,
                FuzzSelection.Default => item.IsSelected,
                FuzzSelection.Random => rng.Next(2) == 0,
                FuzzSelection.ByKinds => kinds.Contains(item.Kind),
                _ => ReferenceEquals(item, single),
            };
        }

        return selection;
    }

    // ---------------- 部品 ----------------

    private Column AddColumn(
        FuzzLiveSchema live,
        Entity entity,
        string name,
        FuzzTypeKind kind,
        int length,
        bool nullable,
        Func<int, string?> value,
        bool pk = false
    )
    {
        var column = new Column
        {
            Name = name,
            DataType = dialect.TypeName(kind, length),
            IsNullable = nullable && !pk,
            IsPrimaryKey = pk,
        };
        entity.Columns.Add(column);

        if (pk)
        {
            entity.PrimaryKeyColumnIds.Add(column.Id);
        }

        var info = new FuzzColumnInfo { Kind = kind, Length = length };

        for (var r = 1; r <= live.RowCounts[entity.TableName]; r++)
        {
            info.Values.Add(value(r));
        }

        live.Columns[FuzzLiveSchema.Key(entity.TableName, name)] = info;
        return column;
    }

    /// <summary>
    /// 型の変更の行き先（長さを変える型は、ときどき方言の上限ちょうど＝<see cref="SyncPlanFuzzDialect.LimitType"/> にする。
    /// 縮める長さは投入した値より短くしない）
    /// </summary>
    private (FuzzTypeKind Kind, int Length) NextType(FuzzColumnInfo info)
    {
        if (
            info.Kind is FuzzTypeKind.VarChar or FuzzTypeKind.FixedChar
            && dialect.LimitType is { } limit
            && limit.Kind == info.Kind
            && rng.Next(4) == 0
        )
        {
            return limit;
        }

        return info.Kind switch
        {
            FuzzTypeKind.Int => (FuzzTypeKind.BigInt, 0),
            FuzzTypeKind.BigInt => (FuzzTypeKind.BigInt, 0),
            FuzzTypeKind.VarChar or FuzzTypeKind.FixedChar => (
                info.Kind,
                rng.Next(2) == 0 ? info.Length * 2 : Math.Max(info.Length / 2, 10)
            ),
            FuzzTypeKind.DateTime => (
                FuzzTypeKind.DateTime,
                info.Length == 3 ? dialect.DefaultDateTimePrecision : 3
            ),
            FuzzTypeKind.Binary => (
                FuzzTypeKind.Binary,
                rng.Next(2) == 0 ? info.Length * 2 : Math.Max(info.Length / 2, 8)
            ),
            _ => (FuzzTypeKind.Decimal, info.Length + 4),
        };
    }

    private static void CollectForeignKeyPartners(
        List<Entity> entities,
        List<Relationship> relationships,
        Column column,
        List<(Entity, Column)> partners
    )
    {
        foreach (var relationship in relationships)
        {
            foreach (var pair in relationship.ColumnPairs)
            {
                var other =
                    pair.SourceColumnId == column.Id
                        ? (relationship.TargetEntityId, pair.TargetColumnId)
                    : pair.TargetColumnId == column.Id
                        ? (relationship.SourceEntityId, pair.SourceColumnId)
                    : default;

                if (other == default)
                {
                    continue;
                }

                var owner = entities.FirstOrDefault(e => e.Id == other.Item1);
                var partner = owner?.Columns.FirstOrDefault(c => c.Id == other.Item2);

                if (
                    owner is not null
                    && partner is not null
                    && partners.All(p => p.Item2 != partner)
                )
                {
                    partners.Add((owner, partner));
                }
            }
        }
    }

    /// <summary>その子列から親への外部キーが既にあるか（同じ列から同じ親へ 2 本張れない方言のため）</summary>
    /// <summary>
    /// 子の列から親へ外部キーを（もう 1 本）張ってよいか。同じ列ペアに 2 本を許さない方言では張らない。同じ列の並びの制約を名前で
    /// 見分けない方言（SQLite）では、参照アクションまで同じ 2 本目を張らない（名前の無い制約は同じ列集合の DB の制約で満たされる
    /// という製品の仕様では、2 本は 1 本と区別できない）
    /// </summary>
    private bool CanAddForeignKey(
        List<Relationship> relationships,
        Entity parent,
        Column column,
        ForeignKeyReferentialAction onDelete,
        ForeignKeyReferentialAction onUpdate
    ) =>
        !HasForeignKey(relationships, parent, column)
        || dialect.AllowDuplicateForeignKeys
            && (
                dialect.DistinguishesSameSetConstraints
                || !relationships.Any(r =>
                    r.SourceEntityId == parent.Id
                    && r.ColumnPairs.Count == 1
                    && r.ColumnPairs[0].TargetColumnId == column.Id
                    && r.OnDelete == onDelete
                    && r.OnUpdate == onUpdate
                )
            );

    private static bool HasForeignKey(
        List<Relationship> relationships,
        Entity parent,
        Column column
    ) =>
        relationships.Any(r =>
            r.SourceEntityId == parent.Id
            && r.ColumnPairs.Count == 1
            && r.ColumnPairs[0].TargetColumnId == column.Id
        );

    /// <summary>主キーを列の並びで置き換える（主キーの列は NOT NULL にする。決定的な行も使う）</summary>
    internal static void SetPrimaryKey(Entity entity, List<Column> key)
    {
        foreach (var column in entity.Columns)
        {
            column.IsPrimaryKey = key.Contains(column);

            if (column.IsPrimaryKey)
            {
                column.IsNullable = false;
            }
        }

        entity.PrimaryKeyColumnIds = [.. key.Select(c => c.Id)];
    }

    private static Relationship NewRelationship(
        Entity parent,
        Entity child,
        IReadOnlyList<(Column Parent, Column Child)> pairs,
        string? name
    ) =>
        new()
        {
            SourceEntityId = parent.Id,
            TargetEntityId = child.Id,
            Type = RelationshipType.OneToMany,
            ColumnPairs =
            [
                .. pairs.Select(p => new RelationshipColumnPair(p.Parent.Id, p.Child.Id)),
            ],
            ConstraintName = name,
        };

    private ForeignKeyReferentialAction PickAction(bool nullable)
    {
        if (!dialect.AllowCascade)
        {
            return ForeignKeyReferentialAction.NoAction;
        }

        // SET NULL・SET DEFAULT は NULL を許す子の列だけ（SET DEFAULT の既定値は NULL＝NOT NULL の列では親の行を消すと DB が断る）
        return rng.Next(4) switch
        {
            0 => ForeignKeyReferentialAction.Cascade,
            1 when nullable => ForeignKeyReferentialAction.SetNull,
            2 when nullable => ForeignKeyReferentialAction.SetDefault,
            _ => ForeignKeyReferentialAction.NoAction,
        };
    }

    private Entity? Pick(List<Entity> entities) =>
        entities.Count == 0 ? null : entities[rng.Next(entities.Count)];

    private static bool IsSingleIntPrimaryKey(Entity entity)
    {
        var key = entity.GetPrimaryKeyColumnsInOrder();
        return key.Count == 1
            && key[0].Name.Equals("id", StringComparison.OrdinalIgnoreCase)
            && !key[0].IsComputed;
    }

    private static Entity EntityOf(FuzzLiveSchema live, Guid id) =>
        live.Diagram.Entities.Single(e => e.Id == id);

    private static string Name(List<Entity> entities, Guid id) =>
        entities.FirstOrDefault(e => e.Id == id)?.TableName ?? "?";

    private static string BaseName(Entity entity) => entity.TableName.ToLowerInvariant();

    private static int RowCount(FuzzLiveSchema live, Entity entity) =>
        live.RowCounts.TryGetValue(entity.TableName, out var rows) ? rows : 0;

    private static FuzzColumnInfo? Info(FuzzLiveSchema live, Entity entity, Column column) =>
        live.Find(entity.TableName, column.Name);

    private string FlipCase(string name)
    {
        var chars = name.ToCharArray();
        var letters = Enumerable
            .Range(0, chars.Length)
            .Where(i => char.IsLetter(chars[i]))
            .ToList();
        var index = letters[rng.Next(letters.Count)];
        chars[index] = char.IsUpper(chars[index])
            ? char.ToLowerInvariant(chars[index])
            : char.ToUpperInvariant(chars[index]);
        return new string(chars);
    }

    /// <summary>
    /// 制約名に使う表の名前（別のスキーマの表の <c>.</c> を <c>_</c> にする＝製品の既定名と同じく、制約名をスキーマで修飾した
    /// 名前に見せない）
    /// </summary>
    private static string NamePart(string table) => table.Replace('.', '_');

    private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);
}
