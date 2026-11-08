using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>
/// 同期計画の形の行（D4）：主キーの変更・参照する外部キーの数・型の変更・明示の削除・一意制約の数・参照先のキー・外部キーの両端の型・
/// 巻き添えの外部キーの変更・主キーと同じ列の一意制約の 9 因子の網羅の配列から、方言ごとに決定的な行を作る（網は全行、実 DB の総当たりは
/// 普段は 2 因子の網羅に要る行と必ず含む行だけ・夜間は全行）
/// </summary>
/// <remarks>
/// <para>
/// 行の図は親の表 <c>p</c>（複合主キー <c>(id, k)</c>・整数の列 <c>u</c> と <c>w</c>）と、親を参照する子の表
/// <c>c1</c>・<c>c2</c>（参照する外部キーの数の水準だけ）。子の外部キーは、参照先のキーの水準で親の主キー（子の列
/// <c>p_ref_0</c>・<c>p_ref_1</c>）か <c>p.u</c> の一意制約（子の列 <c>p_ref</c>）を参照する。<c>p.u</c> に名前付きの一意制約を
/// 一意制約の数の水準だけ張る。外部キーの両端の型が Wider の行は、外部キーを張った後に参照される親の列を bigint にする（子の列は int の
/// まま＝DB が受け入れる型の違う外部キー）。DecimalOtherKeyColumn の行は、親の主キーの 2 列目 <c>k</c> だけを小数にする（子の
/// <c>p_ref_1</c> は int のまま。型の変更の水準は <c>k</c> を変えないので、複合外部キーの「型の決まらない組」が DB の受け入れている違いを持つ）。
/// 巻き添えの外部キーの変更の行は、親の主キーを参照する表 <c>d</c> を足し、その外部キーの ON DELETE を変える（作り直しの外部キーが
/// 計画の前の方で外れる）。主キーと同じ列の一意制約の行は、<c>p</c> に一意制約 <c>uq_p_pk(id, k)</c> を張る（主キーを外しても外部キーを支えうる）。
/// 型の種類を変える水準（ChildKindChange）の行は、<c>c1</c> を行 0 で作る。
/// 差分は全部選ぶ。網羅の配列は 2 因子の組の全部と、P1（主キーの並べ替え × 参照 2 本 × 子だけの型の変更）・Q6（一意制約 2 つ ×
/// 一意制約の削除 × 一意制約の列の型の変更）を含む 3 因子の集まりの組を含む（<see cref="MissingTriples"/> が表明する）。
/// 網羅の配列の後ろに、指摘の形の完全な行（<see cref="RequiredRows"/>＝P5-2 の試運転の L1・L4・2 回目の V1・V3・V4・V6）を、
/// 配列に含まれていなければ足す。
/// </para>
/// <para>
/// Oracle は同じ列の並びに一意制約を 2 つ張れない（ORA-02261・DB の事実）ので、一意制約 2 つの行を作らない。
/// SQL Server（Msg 1778）・MySQL（エラー 3780）は両端の型の違う外部キーを作れない（DB の事実）ので、外部キーの両端の型が Same でない行を作らない。
/// 主キーと同じ列の一意制約は、持てない宣言の方言（<see cref="SyncDialectCapabilities.SupportsUniqueConstraintOnPrimaryKey"/> が偽＝Oracle・SQLite）で作らない。
/// Oracle は行のある列の型の種類を変えられない（ORA-01439・DB の事実）ので、型の種類を変える子の表は全方言で行 0 にそろえる。
/// </para>
/// </remarks>
internal static class SyncPlanShapeRows
{
    /// <summary>主キーの変更</summary>
    public const string PkChange = "PkChange";

    /// <summary>親を参照する外部キーの数</summary>
    public const string ReferencingFks = "ReferencingFks";

    /// <summary>型の変更（子の列だけ・親の列だけ・親と最初の子だけ・親と全部の子・一意制約の列）と、最初の子の NULL 許容だけの変更</summary>
    public const string TypeChange = "TypeChange";

    /// <summary>子の外部キーが参照する親のキー（主キー・p.u の一意制約）</summary>
    public const string ReferencedKey = "ReferencedKey";

    /// <summary>外部キーの両端の型（同じ・参照される親の列だけが bigint）</summary>
    public const string ForeignKeyTypes = "ForeignKeyTypes";

    /// <summary>明示の削除（外部キー・一意制約）</summary>
    public const string ExplicitOp = "ExplicitOp";

    /// <summary>親の列 u に張る一意制約の数</summary>
    public const string UniqueMultiplicity = "UniqueMultiplicity";

    /// <summary>巻き添えの外部キーの変更（親の主キーを参照する表 d の外部キーの作り直し）</summary>
    public const string CollateralFk = "CollateralFk";

    /// <summary>主キーと同じ列の一意制約</summary>
    public const string PrimaryKeyTwin = "PrimaryKeyTwin";

    /// <summary>行の Id の頭</summary>
    public const string IdPrefix = "d4-";

    /// <summary>網羅の配列の種（決定的）</summary>
    private const int ArraySeed = 4;

    /// <summary>次元表</summary>
    public static DimensionTable Table { get; } =
        new(
            EngineIds.SyncPlan,
            [
                Levels(
                    PkChange,
                    "主キーの変更",
                    "None",
                    "Reorder",
                    "Extend",
                    "Shrink",
                    "Move",
                    "MoveToUniqueColumn",
                    "Remove"
                ),
                Levels(ReferencingFks, "親を参照する外部キーの数", "0", "1", "2"),
                Levels(
                    TypeChange,
                    "型の変更（子の列だけ・親の列だけ・親と最初の子だけ・親と全部の子・一意制約の列）と最初の子の NULL 許容だけの変更・最初の子の型の種類の変更",
                    "None",
                    "ChildOnly",
                    "ParentOnly",
                    "PartialBothEnds",
                    "BothEnds",
                    "UniqueColumn",
                    "ChildNullability",
                    "ChildKindChange"
                ),
                Levels(
                    ExplicitOp,
                    "明示の削除（外部キー・一意制約 1 つ・一意制約を全部）",
                    "None",
                    "DropForeignKey",
                    "DropUnique",
                    "DropAllUniques"
                ),
                Levels(UniqueMultiplicity, "親の列 u に張る一意制約の数", "0", "1", "2"),
                Levels(ReferencedKey, "子の外部キーが参照する親のキー", "PrimaryKey", "Unique"),
                Levels(
                    ForeignKeyTypes,
                    "外部キーの両端の型（同じ・参照される親の列だけが bigint・親の主キーの 2 列目だけが小数）",
                    "Same",
                    "Wider",
                    "DecimalOtherKeyColumn"
                ),
                Levels(
                    CollateralFk,
                    "巻き添えの外部キーの変更（親の主キーを参照する表 d の外部キーの作り直し）",
                    "None",
                    "AlterOtherForeignKey"
                ),
                Levels(PrimaryKeyTwin, "主キーと同じ列の一意制約", "0", "1"),
            ],
            [
                new FactorGroup(PkChange, ReferencingFks, TypeChange),
                new FactorGroup(UniqueMultiplicity, ExplicitOp, TypeChange),
                new FactorGroup(PkChange, ReferencedKey, TypeChange),
                new FactorGroup(PkChange, ForeignKeyTypes, TypeChange),
                new FactorGroup(PkChange, PrimaryKeyTwin, TypeChange),
                new FactorGroup(UniqueMultiplicity, ExplicitOp, CollateralFk),
                new FactorGroup(TypeChange, CollateralFk, ForeignKeyTypes),
            ],
            IsValid
        );

    /// <summary>
    /// 網羅の配列に必ず含める完全な行（指摘の形。配列に同じ割り当ての行が無ければ後ろへ足し、実 DB の普段の実行にも入れる）
    /// </summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, string>> RequiredRows { get; } =
    [
        // L1（P5-2 の試運転・Oracle）：外部キー 2 本が参照する一意制約の列へ主キーを付け替え、片方の子の列の型を変える。型の
        // 食い違う外部キーは残り、主キーが置き換える一意制約の暗黙の削除がそれに阻まれる。兄弟の外部キーを外した後に断られると、
        // 暗黙コミットで兄弟の外部キーが消えたまま残った
        new Dictionary<string, string>
        {
            [PkChange] = "MoveToUniqueColumn",
            [ReferencingFks] = "2",
            [TypeChange] = "ChildOnly",
            [ExplicitOp] = "None",
            [UniqueMultiplicity] = "1",
            [ReferencedKey] = "Unique",
            [ForeignKeyTypes] = "Same",
            [CollateralFk] = "None",
            [PrimaryKeyTwin] = "0",
        },
        // L4（P5-2 の試運転・PostgreSQL）：DB が受け入れている型の違う外部キー（int → bigint）の親の主キーを並べ替え、子の列の
        // NULL 許容だけを変える。型を変えないのに型の食い違いと名指しして外部キーを外し直さず、主キーの解除が断られた
        new Dictionary<string, string>
        {
            [PkChange] = "Reorder",
            [ReferencingFks] = "1",
            [TypeChange] = "ChildNullability",
            [ExplicitOp] = "None",
            [UniqueMultiplicity] = "0",
            [ReferencedKey] = "PrimaryKey",
            [ForeignKeyTypes] = "Wider",
            [CollateralFk] = "None",
            [PrimaryKeyTwin] = "0",
        },
        // V1（P5-2 の試運転の 2 回目・PostgreSQL）：複合外部キーの組 (k, p_ref_1) が DB の受け入れている型の違いを持ち、別の組
        // (id, p_ref_0) の両端を bigint にして、親の主キーを並べ替える。外部キー単位で数えると型の決まらない組まで偽の食い違いにして
        // 外部キーを外し直さず、主キーの解除が断られた
        new Dictionary<string, string>
        {
            [PkChange] = "Reorder",
            [ReferencingFks] = "1",
            [TypeChange] = "BothEnds",
            [ExplicitOp] = "None",
            [UniqueMultiplicity] = "0",
            [ReferencedKey] = "PrimaryKey",
            [ForeignKeyTypes] = "DecimalOtherKeyColumn",
            [CollateralFk] = "None",
            [PrimaryKeyTwin] = "0",
        },
        // V3（同・MySQL）：子の外部キーが参照する列の一意制約の双子を両方外し、表 d の外部キーを作り直す。互いを「外部キーを支える
        // ほかのキー」と数えて先頭へ移さず、2 つ目の削除が断られたとき先に外した d の外部キーが消えたまま残った
        new Dictionary<string, string>
        {
            [PkChange] = "None",
            [ReferencingFks] = "1",
            [TypeChange] = "None",
            [ExplicitOp] = "DropAllUniques",
            [UniqueMultiplicity] = "2",
            [ReferencedKey] = "Unique",
            [ForeignKeyTypes] = "Same",
            [CollateralFk] = "AlterOtherForeignKey",
            [PrimaryKeyTwin] = "0",
        },
        // V4（同・Oracle・PostgreSQL）：残す外部キーの子の列を互換でない型（int → varchar）へ変え、表 d の外部キーを作り直す。
        // 列の変更で外部キーを外し直さない方言で名指しも先頭への移動もせず、Oracle は先に外した d の外部キーが消えたまま残った
        new Dictionary<string, string>
        {
            [PkChange] = "None",
            [ReferencingFks] = "1",
            [TypeChange] = "ChildKindChange",
            [ExplicitOp] = "None",
            [UniqueMultiplicity] = "0",
            [ReferencedKey] = "PrimaryKey",
            [ForeignKeyTypes] = "Same",
            [CollateralFk] = "AlterOtherForeignKey",
            [PrimaryKeyTwin] = "0",
        },
        // V6（同・MySQL）：主キーと同じ列の一意制約がある親の主キーを広げ、子の列を両端が食い違う型へ変える。主キーの解除を誤って
        // 「必ず断られる」と先頭へ移し、実際は一意制約が外部キーを支えて通り、続く列の変更の失敗で主キーが消えたまま残った
        new Dictionary<string, string>
        {
            [PkChange] = "Extend",
            [ReferencingFks] = "1",
            [TypeChange] = "ChildOnly",
            [ExplicitOp] = "None",
            [UniqueMultiplicity] = "0",
            [ReferencedKey] = "PrimaryKey",
            [ForeignKeyTypes] = "Same",
            [CollateralFk] = "None",
            [PrimaryKeyTwin] = "1",
        },
    ];

    /// <summary>網羅の配列（全行）。配列に含まれない必ず含む行は後ろへ足す</summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, string>> AllRows { get; } =
        WithRequiredRows(CoveringArray.Generate(Table, ArraySeed));

    /// <summary>必ず含む行の、全行の中の番号</summary>
    private static IReadOnlySet<int> RequiredRowIndexes { get; } =
        RequiredRows.Select(required => IndexOf(AllRows, required)).ToHashSet();

    /// <summary>必ず含む 3 つ組（P1・Q6）</summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, string>> RequiredShapes { get; } =
    [
        new Dictionary<string, string>
        {
            [PkChange] = "Reorder",
            [ReferencingFks] = "2",
            [TypeChange] = "ChildOnly",
        },
        new Dictionary<string, string>
        {
            [UniqueMultiplicity] = "2",
            [ExplicitOp] = "DropUnique",
            [TypeChange] = "UniqueColumn",
        },
    ];

    /// <summary>台帳の行（宣言は全行の後に置く＝静的な初期化の順。形の行の次元表。行は同期計画の総当たりと能力の組の網の決定的な行として流れる）</summary>
    public static EngineDescriptor Descriptor { get; } =
        new(
            EngineIds.SyncPlan,
            "ShapeRows",
            "同期計画の形の行（D4・網羅の配列から作る決定的な行）",
            Table,
            AllRows.Count,
            [
                "tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/SyncPlanShapeRows.cs",
                "tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/SyncPlanScenarios.cs",
            ],
            ["src/QuickER.Provider/SchemaDiffService.cs", "src/QuickER.Provider/SyncPlanner.cs"]
        );

    /// <summary>全方言の行</summary>
    public static IEnumerable<ScenarioRow<SyncPlanScenarioInput>> Rows =>
        DialectNames.All.SelectMany(RowsFor);

    /// <summary>全行に含まれない必須の 3 つ組（空なら全部含む）</summary>
    public static IEnumerable<string> MissingTriples() =>
        RequiredShapes
            .Where(shape => !AllRows.Any(row => shape.All(pair => row[pair.Key] == pair.Value)))
            .Select(shape => string.Join(" × ", shape.Select(p => $"{p.Key}={p.Value}")));

    /// <summary>行の Id（方言・全行の中の番号）</summary>
    public static string IdOf(string dialect, int index) =>
        $"{IdPrefix}{dialect}-{index.ToString("00", CultureInfo.InvariantCulture)}";

    /// <summary>
    /// 方言が普段の実 DB で流す、2 因子の組の全部を含む部分（その方言が作る行のうちで貪欲に選ぶ・並びは全行の順）
    /// </summary>
    /// <remarks>
    /// 全方言で 1 つの部分を選ぶと、方言が作らない行（Oracle の一意制約 2 つ・SQL Server と MySQL の型の違う外部キー・主キーと同じ列の
    /// 一意制約を持てない方言）の分だけ、その方言の普段の実行から組が抜ける。方言が作る行で作れる組だけを数える。
    /// </remarks>
    public static IReadOnlySet<int> PairwiseRowIndexesFor(string dialect) =>
        PairwiseByDialect.TryGetValue(dialect, out var indexes)
            ? indexes
            : throw new ArgumentOutOfRangeException(nameof(dialect), dialect, null);

    private static IReadOnlyDictionary<string, IReadOnlySet<int>> PairwiseByDialect { get; } =
        DialectNames.All.ToDictionary(dialect => dialect, SelectPairwise);

    /// <summary>方言がこの行を作るか（DB・方言の宣言が作れない形の行は作らない）</summary>
    private static bool CreatesRow(string dialect, IReadOnlyDictionary<string, string> row)
    {
        // Oracle は同じ列の並びに一意制約を 2 つ張れない（ORA-02261・DB の事実）
        if (dialect == DialectNames.Oracle && row[UniqueMultiplicity] == "2")
        {
            return false;
        }

        // SQL Server（Msg 1778）・MySQL（エラー 3780）は両端の型の違う外部キーを作れない（DB の事実）
        if (
            row[ForeignKeyTypes] != "Same"
            && dialect is DialectNames.SqlServer or DialectNames.MySql
        )
        {
            return false;
        }

        // 主キーと同じ列の一意制約は、持てない宣言の方言（Oracle＝ORA-02261・SQLite）で作らない
        return row[PrimaryKeyTwin] != "1"
            || SyncPlanExpectationCapabilities.Get(dialect).SupportsUniqueConstraintOnPrimaryKey;
    }

    /// <summary>方言の行</summary>
    private static IEnumerable<ScenarioRow<SyncPlanScenarioInput>> RowsFor(string dialect)
    {
        var pairwise = PairwiseRowIndexesFor(dialect);

        for (var index = 0; index < AllRows.Count; index++)
        {
            var row = AllRows[index];

            if (!CreatesRow(dialect, row))
            {
                continue;
            }

            yield return new ScenarioRow<SyncPlanScenarioInput>(
                IdOf(dialect, index),
                null,
                $"形の行：{string.Join(" × ", row.Select(p => $"{p.Key}={p.Value}"))}",
                new SyncPlanScenarioInput(
                    dialect,
                    fuzzDialect => new SyncPlanScenarioCase(
                        fuzzDialect,
                        live => Build(live, row),
                        target => Mutate(target, row),
                        null
                    ),
                    pairwise.Contains(index) || RequiredRowIndexes.Contains(index)
                )
            );
        }
    }

    /// <summary>DB 側の図</summary>
    private static void Build(ScenarioLive live, IReadOnlyDictionary<string, string> row)
    {
        var p = live.Table("p");
        var id = live.Int(p, "id", pk: true);
        var k = live.Int(p, "k", pk: true);
        var u = live.Int(p, "u", value: r => (r * 10).ToString(CultureInfo.InvariantCulture));
        live.Int(p, "w", value: r => (r * 100).ToString(CultureInfo.InvariantCulture));

        foreach (var name in UniqueNames(row))
        {
            live.Unique(p, name, u);
        }

        // 主キーと同じ列の一意制約（主キーを外しても外部キーを支えうる）
        if (row[PrimaryKeyTwin] == "1")
        {
            live.Unique(p, "uq_p_pk", id, k);
        }

        var referencesUnique = row[ReferencedKey] == "Unique";

        for (var i = 1; i <= Count(row[ReferencingFks]); i++)
        {
            // 型の種類を変える子の表は行 0（Oracle は行のある列の型の種類を変えられない＝ORA-01439）
            var child =
                i == 1 && row[TypeChange] == "ChildKindChange"
                    ? live.Table($"c{i}", 0)
                    : live.Table($"c{i}");
            live.Int(child, "id", pk: true);
            live.Ref(child, p, "p_ref", $"fk_c{i}_p", referenced: referencesUnique ? [u] : null);
        }

        // 巻き添えで作り直す外部キー（親の主キーを参照する表 d）
        if (row[CollateralFk] == "AlterOtherForeignKey")
        {
            var d = live.Table("d");
            live.Int(d, "id", pk: true);
            live.Ref(d, p, "p_ref", "fk_d_p");
        }

        // 外部キーを張った後に参照される親の列だけを bigint にする（子の列は int のまま＝DB が受け入れる型の違う外部キー）
        if (row[ForeignKeyTypes] == "Wider")
        {
            foreach (var column in referencesUnique ? new[] { u } : [id, k])
            {
                live.SetKind(p, column, FuzzTypeKind.BigInt);
            }
        }

        // 親の主キーの 2 列目だけを小数にする（子の p_ref_1 は int のまま＝DB が受け入れる型の違い。型の変更の水準は k を変えない）
        if (row[ForeignKeyTypes] == "DecimalOtherKeyColumn")
        {
            live.SetKind(p, k, FuzzTypeKind.Decimal, 12);
        }
    }

    /// <summary>目標の図への変更</summary>
    private static void Mutate(ScenarioTarget target, IReadOnlyDictionary<string, string> row)
    {
        var refs = Count(row[ReferencingFks]);

        // 子の外部キーの最初の列（主キーを参照すれば p_ref_0・一意制約を参照すれば p_ref）
        var childColumn = row[ReferencedKey] == "Unique" ? "p_ref" : "p_ref_0";

        switch (row[TypeChange])
        {
            case "ChildOnly":
                target.SetType("c1", childColumn, FuzzTypeKind.BigInt);
                break;
            case "ParentOnly":
                target.SetType("p", "id", FuzzTypeKind.BigInt);
                break;
            case "PartialBothEnds":
                target.SetType("p", "id", FuzzTypeKind.BigInt);
                target.SetType("c1", childColumn, FuzzTypeKind.BigInt);
                break;
            case "BothEnds":
                target.SetType("p", "id", FuzzTypeKind.BigInt);

                for (var i = 1; i <= refs; i++)
                {
                    target.SetType($"c{i}", childColumn, FuzzTypeKind.BigInt);
                }

                break;
            case "UniqueColumn":
                target.SetType("p", "u", FuzzTypeKind.BigInt);
                break;
            case "ChildNullability":
                target.SetNullable("c1", childColumn, true);
                break;
            case "ChildKindChange":
                target.SetType("c1", childColumn, FuzzTypeKind.VarChar, 10);
                break;
        }

        switch (row[PkChange])
        {
            case "Reorder":
                target.SetPrimaryKey("p", "k", "id");
                break;
            case "Extend":
                target.SetPrimaryKey("p", "id", "k", "w");
                break;
            case "Shrink":
                target.SetPrimaryKey("p", "id");
                break;
            case "Move":
                target.SetPrimaryKey("p", "w");
                break;
            case "MoveToUniqueColumn":
                target.SetPrimaryKey("p", "u");
                break;
            case "Remove":
                target.RemovePrimaryKey("p");
                break;
        }

        switch (row[ExplicitOp])
        {
            case "DropForeignKey":
                target.DropForeignKey(target.ForeignKey("c1", childColumn));
                break;
            case "DropUnique":
                target.DropUnique("p", UniqueNames(row)[0]);
                break;
            case "DropAllUniques":
                foreach (var name in UniqueNames(row))
                {
                    target.DropUnique("p", name);
                }

                break;
        }

        if (row[CollateralFk] == "AlterOtherForeignKey")
        {
            target.SetOnDelete(
                target.ForeignKey("d", "p_ref_0"),
                ForeignKeyReferentialAction.Cascade
            );
        }
    }

    /// <summary>原理的に起こり得ない組（型の変更・明示の削除・参照の相手が無い）</summary>
    private static bool IsValid(IReadOnlyDictionary<string, string> assignment)
    {
        bool Has(string factor, params string[] levels) =>
            assignment.TryGetValue(factor, out var level) && levels.Contains(level);

        // 子の列の変更・外部キーの削除・外部キーの両端の型の違いは、親を参照する外部キーが要る
        if (
            Has(ReferencingFks, "0")
            && (
                Has(
                    TypeChange,
                    "ChildOnly",
                    "PartialBothEnds",
                    "BothEnds",
                    "ChildNullability",
                    "ChildKindChange"
                )
                || Has(ExplicitOp, "DropForeignKey")
                || Has(ForeignKeyTypes, "Wider", "DecimalOtherKeyColumn")
            )
        )
        {
            return false;
        }

        // 一意制約を全部外すのは一意制約 2 つの行だけ（1 つなら一意制約 1 つの削除と同じ形）
        if (Has(ExplicitOp, "DropAllUniques") && !Has(UniqueMultiplicity, "2"))
        {
            return false;
        }

        // 親の主キーの 2 列目は、主キーを参照する外部キーにしか無い（一意制約を参照する外部キーは 1 列）
        if (Has(ForeignKeyTypes, "DecimalOtherKeyColumn") && Has(ReferencedKey, "Unique"))
        {
            return false;
        }

        // 親と最初の子だけの型の変更は、変えない兄弟の外部キーが要る（参照 1 本なら両端の変更と同じ）
        if (Has(ReferencingFks, "0", "1") && Has(TypeChange, "PartialBothEnds"))
        {
            return false;
        }

        // 一意制約の列の型の変更・一意制約の削除・一意制約を参照する外部キーは、一意制約が要る
        if (
            Has(UniqueMultiplicity, "0")
            && (
                Has(TypeChange, "UniqueColumn")
                || Has(ExplicitOp, "DropUnique")
                || Has(ReferencedKey, "Unique")
            )
        )
        {
            return false;
        }

        // 型の変更の水準は「int の列を bigint にする」。Wider は参照される親の列を bigint で作るので、その列を bigint にする変更が
        // 無い（子の列が無い表で子の列を変える組と同じく、変更の相手が無い）。主キーを参照するなら p.id を変える水準
        // （ParentOnly・PartialBothEnds・BothEnds）、一意制約を参照するなら p.u を変える水準（UniqueColumn）がそれに当たる
        return !(
            Has(ForeignKeyTypes, "Wider")
            && (
                (
                    Has(ReferencedKey, "PrimaryKey")
                    && Has(TypeChange, "ParentOnly", "PartialBothEnds", "BothEnds")
                ) || (Has(ReferencedKey, "Unique") && Has(TypeChange, "UniqueColumn"))
            )
        );
    }

    /// <summary>網羅の配列に、含まれていない必ず含む行を後ろへ足す（足す行が起こり得ない割り当てなら宣言の誤り）</summary>
    private static IReadOnlyList<IReadOnlyDictionary<string, string>> WithRequiredRows(
        IReadOnlyList<IReadOnlyDictionary<string, string>> generated
    )
    {
        var rows = generated.ToList();

        foreach (var required in RequiredRows)
        {
            if (
                required.Count != Table.Factors.Count
                || Table.Factors.Any(f => !required.ContainsKey(f.Name))
                || !Table.IsValid(required)
            )
            {
                throw new InvalidOperationException(
                    $"必ず含む行は全因子の起こり得る割り当て: {string.Join(", ", required.Select(p => $"{p.Key}={p.Value}"))}"
                );
            }

            if (IndexOf(rows, required) < 0)
            {
                rows.Add(Table.Factors.ToDictionary(f => f.Name, f => required[f.Name]));
            }
        }

        return rows;
    }

    /// <summary>割り当てがちょうど一致する行の番号（無ければ -1）</summary>
    private static int IndexOf(
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows,
        IReadOnlyDictionary<string, string> assignment
    )
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (assignment.All(pair => rows[i][pair.Key] == pair.Value))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>方言が作る行で作れる 2 因子の組の全部を含むように、その方言が作る行から貪欲に選ぶ</summary>
    private static IReadOnlySet<int> SelectPairwise(string dialect)
    {
        var candidates = Enumerable
            .Range(0, AllRows.Count)
            .Where(i => CreatesRow(dialect, AllRows[i]))
            .ToList();

        // 方言が作る行のどれかに含まれる組だけを数える（作らない形の組は、その方言では起こり得ない）
        var uncovered = Table
            .RequiredPairs()
            .Where(t => candidates.Any(i => t.IsContainedIn(AllRows[i])))
            .ToHashSet();
        var chosen = new SortedSet<int>();

        while (uncovered.Count > 0)
        {
            var best = candidates
                .Where(i => !chosen.Contains(i))
                .Select(i => (Index: i, Count: uncovered.Count(t => t.IsContainedIn(AllRows[i]))))
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.Index)
                .First();

            if (best.Count == 0)
            {
                throw new InvalidOperationException("網羅の配列が 2 因子の組を含まない");
            }

            chosen.Add(best.Index);
            uncovered.RemoveWhere(t => t.IsContainedIn(AllRows[best.Index]));
        }

        return chosen;
    }

    private static string[] UniqueNames(IReadOnlyDictionary<string, string> row) =>
        Count(row[UniqueMultiplicity]) switch
        {
            0 => [],
            1 => ["uq_p_u_a"],
            _ => ["uq_p_u_a", "uq_p_u_b"],
        };

    private static int Count(string level) => int.Parse(level, CultureInfo.InvariantCulture);

    private static Factor Levels(string name, string description, params string[] levels) =>
        new(name, [.. levels.Select(level => new Level(level))], description);
}
