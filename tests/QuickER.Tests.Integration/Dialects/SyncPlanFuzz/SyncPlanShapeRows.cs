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
/// 巻き添えの外部キーの変更・主キーと同じ列の一意制約・足す外部キーの 10 因子の網羅の配列から、方言ごとに決定的な行を作る（網は全行、実 DB の総当たりは
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
/// 型の種類を変える水準（ChildKindChange）の行は、<c>c1</c> を行 0 で作る。参照される親の列と全部の子の型の種類を変える水準
/// （BothEndsKindChange）の行は、親・子・<c>d</c> を行 0 で作る。足す外部キーの行は、外部キーの無い表 <c>e</c>（行 0）を置き、
/// 目標の図で <c>e(p_ref_0, p_ref_1)</c> から <c>p(id, k)</c> への外部キーを足す（最初の列の型が水準。2 列目は <c>p.k</c> と同じ型）。
/// 差分は全部選ぶ。網羅の配列は 2 因子の組の全部と、P1（主キーの並べ替え × 参照 2 本 × 子だけの型の変更）・Q6（一意制約 2 つ ×
/// 一意制約の削除 × 一意制約の列の型の変更）を含む 3 因子の集まりの組を含む（<see cref="MissingTriples"/> が表明する）。
/// 網羅の配列の後ろに、指摘の形の完全な行（<see cref="RequiredRows"/>＝P5-2 の試運転の L1・L4・2 回目の V1・V3・V4・V6、修正 AC の実 DB の確かめの
/// V8・型の違う外部キーの追加 2 つ）を、
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

    /// <summary>目標の図で足す外部キー（live の表 e の列から親の主キーへ。両端の型が同じ・子が bigint・子が文字列）</summary>
    public const string AddedForeignKey = "AddedForeignKey";

    /// <summary>親の列 u のデータ（値がどれも違う・NULL 許容で 2 行が NULL）</summary>
    public const string UniqueColumnData = "UniqueColumnData";

    /// <summary>最初の子の外部キーの ON DELETE（DB 側の図）</summary>
    public const string ChildOnDelete = "ChildOnDelete";

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
                new Factor(
                    TypeChange,
                    [
                        .. new[]
                        {
                            "None",
                            "ChildOnly",
                            "ParentOnly",
                            "PartialBothEnds",
                            "BothEnds",
                            "UniqueColumn",
                            "ChildNullability",
                            "ChildKindChange",
                            "BothEndsKindChange",
                            "DivergentEnds",
                        }.Select(level => new Level(level)),
                        new Level("DivergentEndsStringLength") { Tag = RowTag.Ledger("pe1") },
                        new Level("DivergentEndsStringLengthAcrossPrefix")
                        {
                            Tag = RowTag.Ledger("pe1"),
                        },
                        new Level("DivergentEndsStringLengthInBand") { Tag = RowTag.Ledger("pe1") },
                        new Level("DivergentEndsStringLengthLatin1") { Tag = RowTag.Ledger("pe1") },
                        new Level("ChildNotNull") { Tag = RowTag.Ledger("pe3") },
                    ],
                    "型の変更（子の列だけ・親の列だけ・親と最初の子だけ・親と全部の子・一意制約の列）と最初の子の NULL 許容だけの変更・最初の子の型の種類の変更・参照される親の列と全部の子の型の種類の変更・両端を別々の型へ（最初の子は互換の範囲で広げ、参照される親の列は互換でない型へ。子の広げ方は整数の幅・可変長の文字列の長さ〔MySQL が外部キーを張ったまま受ける範囲〕・可変長の文字列の長さ〔長さの頭のバイト数が変わる＝MySQL が断る〕）・最初の子の NOT NULL 化（DB 側では NULL 許容）"
                ),
                new Factor(
                    ExplicitOp,
                    [
                        .. new[]
                        {
                            "None",
                            "DropForeignKey",
                            "DropUnique",
                            "DropAllUniques",
                            "RenameKeepAndAddUnnamedOnOld",
                            "MoveOntoAddedUnique",
                            "ChangeFirstDropOthers",
                            "ChangeAllUniques",
                        }.Select(level => new Level(level)),
                        new Level("WidenUnique") { Tag = RowTag.Ledger("pa1") },
                    ],
                    "明示の削除（外部キー・一意制約 1 つ・一意制約を全部）と、名前を保って一意制約の列を変え元の列へ名前の無い一意制約を足す変更・名前を保って一意制約の列を変え移した先の列へ名前の無い一意制約を足す変更・最初の一意制約の列を変えほかを外す変更・u と定数の列 x の一意制約を足す変更（u の一意制約の上位集合）"
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
                Levels(
                    AddedForeignKey,
                    "目標の図で足す外部キー（live の表 e の列から親の主キーへ。両端の型が同じ・子の最初の列が bigint・文字列・両端の型が同じで e の行が親に無い値を持つ）",
                    "None",
                    "IntChild",
                    "BigIntChild",
                    "VarCharChild",
                    "OrphanChild"
                ),
                new Factor(
                    UniqueColumnData,
                    [
                        new Level("Distinct") { Tag = RowTag.Ledger("pa1") },
                        new Level("NullRepeats") { Tag = RowTag.Ledger("pa1") },
                        new Level("CompositeNullRepeats") { Tag = RowTag.Ledger("pa1") },
                        new Level("CompositePartialNull") { Tag = RowTag.Ledger("pa1") },
                    ],
                    "親の列 u のデータ（値がどれも違う・u を NULL 許容で作り、最初の 2 行を NULL にする＝定数の列 x と組むと一部が NULL の重複）"
                ),
                new Factor(
                    ChildOnDelete,
                    [
                        new Level("NoAction") { Tag = RowTag.Ledger("pe3") },
                        new Level("SetDefault") { Tag = RowTag.Ledger("pe3") },
                        new Level("SetNull") { Tag = RowTag.Ledger("pe3") },
                        new Level("Cascade") { Tag = RowTag.Ledger("pe3") },
                    ],
                    "最初の子 c1 の外部キーの ON DELETE（DB 側の図。SET DEFAULT・SET NULL の行は c1 の子の列を NULL 許容で作る）"
                ),
            ],
            [
                new FactorGroup(PkChange, ReferencingFks, TypeChange),
                new FactorGroup(UniqueMultiplicity, ExplicitOp, TypeChange),
                new FactorGroup(PkChange, ReferencedKey, TypeChange),
                new FactorGroup(PkChange, ForeignKeyTypes, TypeChange),
                new FactorGroup(PkChange, PrimaryKeyTwin, TypeChange),
                new FactorGroup(UniqueMultiplicity, ExplicitOp, CollateralFk),
                new FactorGroup(TypeChange, CollateralFk, ForeignKeyTypes),
                new FactorGroup(TypeChange, AddedForeignKey, ForeignKeyTypes),
                new FactorGroup(PkChange, AddedForeignKey, TypeChange),
            ],
            IsValid
        );

    /// <summary>
    /// 網羅の配列に必ず含める完全な行（指摘の形。配列に同じ割り当ての行が無ければ後ろへ足し、実 DB の普段の実行にも入れる）
    /// </summary>
    public static IReadOnlyList<RequiredRow> RequiredRows { get; } =
    [
        // L1（P5-2 の試運転・Oracle）：外部キー 2 本が参照する一意制約の列へ主キーを付け替え、片方の子の列の型を変える。型の
        // 食い違う外部キーは残り、主キーが置き換える一意制約の暗黙の削除がそれに阻まれる。兄弟の外部キーを外した後に断られると、
        // 暗黙コミットで兄弟の外部キーが消えたまま残った
        new RequiredRow(
            "l1-move-to-unique-child-type",
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
                [AddedForeignKey] = "None",
                [UniqueColumnData] = "Distinct",
                [ChildOnDelete] = "NoAction",
            }
        ),
        // L4（P5-2 の試運転・PostgreSQL）：DB が受け入れている型の違う外部キー（int → bigint）の親の主キーを並べ替え、子の列の
        // NULL 許容だけを変える。型を変えないのに型の食い違いと名指しして外部キーを外し直さず、主キーの解除が断られた
        new RequiredRow(
            "l4-reorder-wider-child-nullability",
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
                [AddedForeignKey] = "None",
                [UniqueColumnData] = "Distinct",
                [ChildOnDelete] = "NoAction",
            }
        ),
        // V1（P5-2 の試運転の 2 回目・PostgreSQL）：複合外部キーの組 (k, p_ref_1) が DB の受け入れている型の違いを持ち、別の組
        // (id, p_ref_0) の両端を bigint にして、親の主キーを並べ替える。外部キー単位で数えると型の決まらない組まで偽の食い違いにして
        // 外部キーを外し直さず、主キーの解除が断られた
        new RequiredRow(
            "v1-reorder-decimal-other-key-column",
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
                [AddedForeignKey] = "None",
                [UniqueColumnData] = "Distinct",
                [ChildOnDelete] = "NoAction",
            }
        ),
        // V3（同・MySQL）：子の外部キーが参照する列の一意制約の双子を両方外し、表 d の外部キーを作り直す。互いを「外部キーを支える
        // ほかのキー」と数えて先頭へ移さず、2 つ目の削除が断られたとき先に外した d の外部キーが消えたまま残った
        new RequiredRow(
            "v3-drop-unique-twins-with-collateral-fk",
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
                [AddedForeignKey] = "None",
                [UniqueColumnData] = "Distinct",
                [ChildOnDelete] = "NoAction",
            }
        ),
        // V4（同・Oracle・PostgreSQL）：残す外部キーの子の列を互換でない型（int → varchar）へ変え、表 d の外部キーを作り直す。
        // 列の変更で外部キーを外し直さない方言で名指しも先頭への移動もせず、Oracle は先に外した d の外部キーが消えたまま残った
        new RequiredRow(
            "v4-child-kind-change-with-collateral-fk",
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
                [AddedForeignKey] = "None",
                [UniqueColumnData] = "Distinct",
                [ChildOnDelete] = "NoAction",
            }
        ),
        // V6（同・MySQL）：主キーと同じ列の一意制約がある親の主キーを広げ、子の列を両端が食い違う型へ変える。主キーの解除を誤って
        // 「必ず断られる」と先頭へ移し、実際は一意制約が外部キーを支えて通り、続く列の変更の失敗で主キーが消えたまま残った
        new RequiredRow(
            "v6-extend-pk-with-twin-unique",
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
                [AddedForeignKey] = "None",
                [UniqueColumnData] = "Distinct",
                [ChildOnDelete] = "NoAction",
            }
        ),
        // V8（修正 AC の実 DB の確かめ・PostgreSQL・Oracle）：外部キーの両端を同じ別の種類（int → varchar）へ変え、表 d の外部キーを
        // 作り直す。同期後の型は合うので名指しも外し直しもせず、外部キーを張ったまま片側だけ変えた途中の状態で断られた（42804・
        // ORA-02267。Oracle は先に外した d の外部キーが消えたまま残る形）
        new RequiredRow(
            "v8-both-ends-kind-change-with-collateral-fk",
            new Dictionary<string, string>
            {
                [PkChange] = "None",
                [ReferencingFks] = "1",
                [TypeChange] = "BothEndsKindChange",
                [ExplicitOp] = "None",
                [UniqueMultiplicity] = "0",
                [ReferencedKey] = "PrimaryKey",
                [ForeignKeyTypes] = "Same",
                [CollateralFk] = "AlterOtherForeignKey",
                [PrimaryKeyTwin] = "0",
                [AddedForeignKey] = "None",
                [UniqueColumnData] = "Distinct",
                [ChildOnDelete] = "NoAction",
            }
        ),
        // 型の違う外部キーの追加（同・MySQL・SQL Server）：live の列どうし（親 int・子 bigint）に外部キーを足す。どちらの端の型も
        // この同期で決まらないので「DB が受け入れている」とみなして数えず、警告なしに断られた（エラー 3780・Msg 1778）
        new RequiredRow(
            "ac-added-bigint-child-fk",
            new Dictionary<string, string>
            {
                [PkChange] = "None",
                [ReferencingFks] = "0",
                [TypeChange] = "None",
                [ExplicitOp] = "None",
                [UniqueMultiplicity] = "0",
                [ReferencedKey] = "PrimaryKey",
                [ForeignKeyTypes] = "Same",
                [CollateralFk] = "None",
                [PrimaryKeyTwin] = "0",
                [AddedForeignKey] = "BigIntChild",
                [UniqueColumnData] = "Distinct",
                [ChildOnDelete] = "NoAction",
            }
        ),
        // 前寄せの型の判定（P5-2 の本番 PA1・Oracle）：外部キーの両端を別々の型へ変え（子は互換の範囲で広げ、親は互換でない型へ）、
        // 表 d の外部キーを作り直す。同期後の両端で「必ず断られる」と判定して、先頭の位置では通る子の変更を前寄せし、断られる親の
        // 変更は元の位置に残って、先に外した d の外部キーが消えたまま残った
        new RequiredRow(
            "ah-move-first-divergent-ends",
            new Dictionary<string, string>
            {
                [PkChange] = "None",
                [ReferencingFks] = "1",
                [TypeChange] = "DivergentEnds",
                [ExplicitOp] = "None",
                [UniqueMultiplicity] = "0",
                [ReferencedKey] = "PrimaryKey",
                [ForeignKeyTypes] = "Same",
                [CollateralFk] = "AlterOtherForeignKey",
                [PrimaryKeyTwin] = "0",
                [AddedForeignKey] = "None",
                [UniqueColumnData] = "Distinct",
                [ChildOnDelete] = "NoAction",
            }
        ),
        // 付け直しの並び（P5-2 の本番 PB1・MySQL・Oracle）：親の主キーを並べ替え（子の外部キーを外して付け直す）、親の無い値を持つ
        // 列へ外部キーを足す。付け直しを利用者の追加の後ろに置いたので、追加が既存の行で断られると付け直しが走らず、外した外部キーが
        // 消えたまま残った
        new RequiredRow(
            "ah-readd-order-orphan-child",
            new Dictionary<string, string>
            {
                [PkChange] = "Reorder",
                [ReferencingFks] = "1",
                [TypeChange] = "None",
                [ExplicitOp] = "None",
                [UniqueMultiplicity] = "0",
                [ReferencedKey] = "PrimaryKey",
                [ForeignKeyTypes] = "Same",
                [CollateralFk] = "None",
                [PrimaryKeyTwin] = "0",
                [AddedForeignKey] = "OrphanChild",
                [UniqueColumnData] = "Distinct",
                [ChildOnDelete] = "NoAction",
            }
        ),
        // 型の違う外部キーの追加（修正 AC2・4 方言）：live の列どうし（親 int・子 varchar）に外部キーを足す
        new RequiredRow(
            "ac2-added-varchar-child-fk",
            new Dictionary<string, string>
            {
                [PkChange] = "None",
                [ReferencingFks] = "0",
                [TypeChange] = "None",
                [ExplicitOp] = "None",
                [UniqueMultiplicity] = "0",
                [ReferencedKey] = "PrimaryKey",
                [ForeignKeyTypes] = "Same",
                [CollateralFk] = "None",
                [PrimaryKeyTwin] = "0",
                [AddedForeignKey] = "VarCharChild",
                [UniqueColumnData] = "Distinct",
                [ChildOnDelete] = "NoAction",
            }
        ),
        // 前寄せの組の並び（夜間 CI の網の MySQL の種 8169）：外部キーが参照する列の一意制約の双子のうち、片方の列を変え（作り直し）、
        // もう片方を外す。支えうるキーが全部外れるので削除の組を前寄せするが、組の並びが作り直しの削除 → ただの削除だったので、
        // 作り直しの削除が通った後にただの削除が断られ（1553）、作り直す一意制約が消えたまま残った
        new RequiredRow(
            "ah2-move-first-pair-order",
            new Dictionary<string, string>
            {
                [PkChange] = "None",
                [ReferencingFks] = "1",
                [TypeChange] = "None",
                [ExplicitOp] = "ChangeFirstDropOthers",
                [UniqueMultiplicity] = "2",
                [ReferencedKey] = "Unique",
                [ForeignKeyTypes] = "Same",
                [CollateralFk] = "None",
                [PrimaryKeyTwin] = "0",
                [AddedForeignKey] = "None",
                [UniqueColumnData] = "Distinct",
                [ChildOnDelete] = "NoAction",
            }
        ),
        // 前寄せの組に付け直すキーが 2 つ（修正 AH2 の残り）：外部キーが参照する列の一意制約の双子を両方作り直す。組をどう並べても
        // 前の作り直しの削除が通った後に次の削除が断られ（1553）、前の一意制約が消えたまま残る。作り直し 1 つを計画から外して名指しし、
        // 残した一意制約が外部キーを支える
        new RequiredRow(
            "ah2-two-readded-uniques",
            new Dictionary<string, string>
            {
                [PkChange] = "None",
                [ReferencingFks] = "1",
                [TypeChange] = "None",
                [ExplicitOp] = "ChangeAllUniques",
                [UniqueMultiplicity] = "2",
                [ReferencedKey] = "Unique",
                [ForeignKeyTypes] = "Same",
                [CollateralFk] = "None",
                [PrimaryKeyTwin] = "0",
                [AddedForeignKey] = "None",
                [UniqueColumnData] = "Distinct",
                [ChildOnDelete] = "NoAction",
            }
        ),
        // 一部が NULL の重複（3 回目の効果測定の PA1・Oracle）：NULL 許容の列 u の一意制約を持つ表へ、u と定数の列 x の一意制約を
        // 足し、表 d の外部キーを作り直す。u が NULL で x が同じ 2 行を Oracle は (u, x) の重複とみなすのに、計画は u の一意制約を
        // 根拠に「既存の行で断られない」と証明し、追加が断られたとき先に外した d の外部キーが名指しされずに消えたまま残った
        new RequiredRow(
            "pa1-widen-unique-over-null-rows",
            new Dictionary<string, string>
            {
                [PkChange] = "None",
                [ReferencingFks] = "0",
                [TypeChange] = "None",
                [ExplicitOp] = "WidenUnique",
                [UniqueMultiplicity] = "1",
                [ReferencedKey] = "PrimaryKey",
                [ForeignKeyTypes] = "Same",
                [CollateralFk] = "AlterOtherForeignKey",
                [PrimaryKeyTwin] = "0",
                [AddedForeignKey] = "None",
                [UniqueColumnData] = "NullRepeats",
                [ChildOnDelete] = "NoAction",
            }
        )
        {
            Tag = RowTag.Ledger("pa1"),
        },
        // 同じ形の文の由来の隣（PA1）：巻き添えの作り直しの代わりに、親の主キーの並べ替えで子の外部キーと主キーを外して付け直す
        new RequiredRow(
            "pa1-widen-unique-over-null-rows-pk-reorder",
            new Dictionary<string, string>
            {
                [PkChange] = "Reorder",
                [ReferencingFks] = "1",
                [TypeChange] = "None",
                [ExplicitOp] = "WidenUnique",
                [UniqueMultiplicity] = "1",
                [ReferencedKey] = "PrimaryKey",
                [ForeignKeyTypes] = "Same",
                [CollateralFk] = "None",
                [PrimaryKeyTwin] = "0",
                [AddedForeignKey] = "None",
                [UniqueColumnData] = "NullRepeats",
                [ChildOnDelete] = "NoAction",
            }
        )
        {
            Tag = RowTag.Ledger("pa1"),
        },
        // 同じ形の複合のキーの隣（PA1）：live の一意制約が複合 (u, y) で、構成列が全部 NULL 許容なら証拠にならず（Oracle は
        // (NULL, NULL) の 2 行を持てて、(u, y, x) を断る）、一部だけ NULL 許容なら証拠になる（(10, NULL) の 2 行目は Oracle も
        // INSERT で断るので、重複が入らない）
        new RequiredRow(
            "pa1-widen-unique-over-null-composite-key",
            new Dictionary<string, string>
            {
                [PkChange] = "None",
                [ReferencingFks] = "0",
                [TypeChange] = "None",
                [ExplicitOp] = "WidenUnique",
                [UniqueMultiplicity] = "1",
                [ReferencedKey] = "PrimaryKey",
                [ForeignKeyTypes] = "Same",
                [CollateralFk] = "AlterOtherForeignKey",
                [PrimaryKeyTwin] = "0",
                [AddedForeignKey] = "None",
                [UniqueColumnData] = "CompositeNullRepeats",
                [ChildOnDelete] = "NoAction",
            }
        )
        {
            Tag = RowTag.Ledger("pa1"),
        },
        new RequiredRow(
            "pa1-widen-unique-over-partly-nullable-composite-key",
            new Dictionary<string, string>
            {
                [PkChange] = "None",
                [ReferencingFks] = "0",
                [TypeChange] = "None",
                [ExplicitOp] = "WidenUnique",
                [UniqueMultiplicity] = "1",
                [ReferencedKey] = "PrimaryKey",
                [ForeignKeyTypes] = "Same",
                [CollateralFk] = "AlterOtherForeignKey",
                [PrimaryKeyTwin] = "0",
                [AddedForeignKey] = "None",
                [UniqueColumnData] = "CompositePartialNull",
                [ChildOnDelete] = "NoAction",
            }
        )
        {
            Tag = RowTag.Ledger("pa1"),
        },
        // 外し直しの方言の前寄せ（3 回目の効果測定の PE1・MySQL）：外部キーの両端を別々の型へ変え（子は可変長の文字列の長さだけを
        // 伸ばし、親は互換でない型へ）、表 d の外部キーを作り直す。外部キー単位の同期後の両端で「必ず断られる」と判定して、MySQL が
        // 外部キーを張ったまま受ける子の変更を前寄せし、断られる親の変更が元の位置に残って、先に外した d の外部キーが消えたまま残った
        new RequiredRow(
            "pe1-divergent-ends-string-length",
            new Dictionary<string, string>
            {
                [PkChange] = "None",
                [ReferencingFks] = "1",
                [TypeChange] = "DivergentEndsStringLength",
                [ExplicitOp] = "None",
                [UniqueMultiplicity] = "0",
                [ReferencedKey] = "PrimaryKey",
                [ForeignKeyTypes] = "Same",
                [CollateralFk] = "AlterOtherForeignKey",
                [PrimaryKeyTwin] = "0",
                [AddedForeignKey] = "None",
                [UniqueColumnData] = "Distinct",
                [ChildOnDelete] = "NoAction",
            }
        )
        {
            Tag = RowTag.Ledger("pe1"),
        },
        // 暗黙の付け直しの SET DEFAULT（3 回目の効果測定の PE3・SQL Server）：DB の SET DEFAULT の外部キーの子の列（NULL 許容・
        // 既定値なし）を NOT NULL にする。計画は外部キーを外して列を変えて付け直すが、付け直しが Msg 1760 で断られるのに警告が無かった
        new RequiredRow(
            "pe3-live-set-default-child-not-null",
            new Dictionary<string, string>
            {
                [PkChange] = "None",
                [ReferencingFks] = "1",
                [TypeChange] = "ChildNotNull",
                [ExplicitOp] = "None",
                [UniqueMultiplicity] = "0",
                [ReferencedKey] = "PrimaryKey",
                [ForeignKeyTypes] = "Same",
                [CollateralFk] = "None",
                [PrimaryKeyTwin] = "0",
                [AddedForeignKey] = "None",
                [UniqueColumnData] = "Distinct",
                [ChildOnDelete] = "SetDefault",
            }
        )
        {
            Tag = RowTag.Ledger("pe3"),
        },
    ];

    /// <summary>網羅の配列（全行）。配列に含まれない必ず含む行は後ろへ足す</summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, string>> AllRows { get; } =
        WithRequiredRows(CoveringArray.Generate(Table, ArraySeed));

    /// <summary>必ず含む行の、全行の中の番号</summary>
    private static IReadOnlySet<int> RequiredRowIndexes { get; } =
        RequiredRows.Select(required => IndexOf(AllRows, required.Assignment)).ToHashSet();

    /// <summary>
    /// 必ず含む行の方言の行 Id（分岐の軸の台帳の指摘のセルを引く。方言がその行を作らなければ null）
    /// </summary>
    /// <param name="dialect">方言名</param>
    /// <param name="requiredRowId">必ず含む行の Id（<see cref="RequiredRow.Id"/>）</param>
    /// <exception cref="ArgumentException">必ず含む行に無い Id</exception>
    public static string? RequiredRowIdFor(string dialect, string requiredRowId)
    {
        var required =
            RequiredRows.FirstOrDefault(row =>
                string.Equals(row.Id, requiredRowId, StringComparison.Ordinal)
            )
            ?? throw new ArgumentException(
                $"形の行の必ず含む行に {requiredRowId} が無い",
                nameof(requiredRowId)
            );
        var index = IndexOf(AllRows, required.Assignment);

        return CreatesRow(dialect, AllRows[index]) ? IdOf(dialect, index) : null;
    }

    /// <summary>
    /// 方言が作る行のうち、水準の組（因子 → 水準）をすべて持つ行があるか（分岐の軸の台帳の「項目の水準が出る方言」）
    /// </summary>
    /// <param name="dialect">方言名</param>
    /// <param name="levels">水準の組</param>
    public static bool CreatesLevels(string dialect, IReadOnlyDictionary<string, string> levels) =>
        AllRows.Any(row =>
            CreatesRow(dialect, row)
            && levels.All(pair => row.TryGetValue(pair.Key, out var level) && level == pair.Value)
        );

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
            [
                "src/QuickER.Provider/SchemaDiffService.cs",
                "src/QuickER.Provider/SyncPlanner.cs",
                "src/QuickER.Provider/SyncPlanContext.cs",
                "src/QuickER.Provider/Sync",
            ]
        );

    /// <summary>全方言の行</summary>
    public static IEnumerable<ScenarioRow<SyncPlanScenarioInput>> Rows =>
        DialectNames.All.SelectMany(RowsFor);

    /// <summary>全行に含まれない必須の 3 つ組（空なら全部含む）</summary>
    public static IEnumerable<string> MissingTriples() =>
        RequiredShapes
            .Where(shape => !AllRows.Any(row => shape.All(pair => row[pair.Key] == pair.Value)))
            .Select(shape => string.Join(" × ", shape.Select(p => $"{p.Key}={p.Value}")));

    /// <summary>形の行の Id の水準の割り当て（形の行の Id でなければ null）</summary>
    /// <param name="scenarioId">決定的な行の Id</param>
    public static IReadOnlyDictionary<string, string>? AssignmentOf(string? scenarioId)
    {
        if (scenarioId is null || !scenarioId.StartsWith(IdPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var separator = scenarioId.LastIndexOf('-');

        return
            int.TryParse(
                scenarioId.AsSpan(separator + 1),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var index
            )
            && index < AllRows.Count
            ? AllRows[index]
            : null;
    }

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

        // 移した先の列へもう 1 つ一意制約を足す変更は、目標の図に同じ列集合の一意制約を 2 つ持つ。Oracle は持てず（ORA-02261）、
        // SQLite は同じ列の並びの制約を名前で見分けない（生成器の宣言 DistinguishesSameSetConstraints と同じ）
        if (
            row[ExplicitOp] == "MoveOntoAddedUnique"
            && dialect is DialectNames.Oracle or DialectNames.Sqlite
        )
        {
            return false;
        }

        // 全部の一意制約の列を変える変更は、同じ列の一意制約 2 つを名前で見分けて別々に作り直す。SQLite は同じ列の並びの UNIQUE を
        // 1 つの索引にまとめる（取り込むと 1 つ）ので、実 DB には作り直す 2 つ目が無い
        if (row[ExplicitOp] == "ChangeAllUniques" && dialect == DialectNames.Sqlite)
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

        // SQL Server は一意制約の列の NULL どうしを重なりとみなす（2 行目の NULL を INSERT で断る＝DB の事実）ので、u の一意制約の
        // 下に NULL を 2 行持てない
        if (
            row[UniqueColumnData] is "NullRepeats" or "CompositeNullRepeats"
            && row[UniqueMultiplicity] != "0"
            && dialect == DialectNames.SqlServer
        )
        {
            return false;
        }

        // 列ごとの文字集合（CHARACTER SET latin1）は MySQL の型の表記だけが書ける（ほかの方言は文字集合を列の型に書かない）
        if (row[TypeChange] == "DivergentEndsStringLengthLatin1" && dialect != DialectNames.MySql)
        {
            return false;
        }

        // SET DEFAULT は、DB が作らない方言（SupportsSetDefaultAction が偽＝MySQL・Oracle）で作らない
        if (
            row[ChildOnDelete] == "SetDefault"
            && !SyncPlanExpectationCapabilities.Get(dialect).SupportsSetDefaultAction
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
        // 参照される親の列と全部の子の型の種類を変える行は、親・子・d を行 0 で作る（Oracle は行のある列の型の種類を変えられない＝
        // ORA-01439）
        var kindChangesBothEnds =
            row[TypeChange] is "BothEndsKindChange" or "DivergentEnds"
            || StringEndsOf(row) is not null;
        var p = kindChangesBothEnds ? live.Table("p", 0) : live.Table("p");
        var referencesUnique = row[ReferencedKey] == "Unique";

        // 可変長の文字列の長さを変える水準は、参照される親の列を水準の長さと文字集合の varchar で作る（子の列は親の列の型を写す）
        var stringEnds = StringEndsOf(row);
        var id =
            stringEnds is { } idEnds && !referencesUnique
                ? live.VarChar(p, "id", idEnds.Length, pk: true, characterSet: idEnds.CharacterSet)
                : live.Int(p, "id", pk: true);
        var k = live.Int(p, "k", pk: true);

        // NULL の重複の水準は、u を NULL 許容で作って最初の 2 行を NULL にする（x と組むと一部が NULL の重複）。複合のキーの水準は
        // u と y の一意制約を張り、全部が NULL 許容（CompositeNullRepeats＝最初の 2 行の u と y が NULL）か、y だけが NULL 許容
        // （CompositePartialNull＝u は NOT NULL で値がどれも違い、最初の 2 行の y が NULL）にする
        var composite = IsComposite(row);
        var nullRepeats = row[UniqueColumnData] is "NullRepeats" or "CompositeNullRepeats";
        Func<int, string?> uValue = r =>
            nullRepeats && r <= 2 ? null : (r * 10).ToString(CultureInfo.InvariantCulture);
        var u =
            stringEnds is { } uEnds && referencesUnique
                ? live.VarChar(
                    p,
                    "u",
                    uEnds.Length,
                    nullable: nullRepeats,
                    characterSet: uEnds.CharacterSet
                )
                : live.Int(p, "u", nullable: nullRepeats, value: uValue);
        live.Int(p, "w", value: r => (r * 100).ToString(CultureInfo.InvariantCulture));

        // 一意制約を広げる操作の相手の定数の列（u の値がどれも違えば (u, x) も一意）
        live.Int(p, "x", value: _ => "7");

        // 複合のキーの水準の 2 列目（NULL 許容）
        var y = live.Int(
            p,
            "y",
            nullable: true,
            value: r =>
                composite && r <= 2 ? null : (r * 1000).ToString(CultureInfo.InvariantCulture)
        );

        foreach (var name in UniqueNames(row))
        {
            if (composite)
            {
                live.Unique(p, name, u, y);
            }
            else
            {
                live.Unique(p, name, u);
            }
        }

        // 主キーと同じ列の一意制約（主キーを外しても外部キーを支えうる）
        if (row[PrimaryKeyTwin] == "1")
        {
            live.Unique(p, "uq_p_pk", id, k);
        }

        for (var i = 1; i <= Count(row[ReferencingFks]); i++)
        {
            // 型の種類を変える子の表は行 0（Oracle は行のある列の型の種類を変えられない＝ORA-01439）
            var child =
                (i == 1 && row[TypeChange] == "ChildKindChange") || kindChangesBothEnds
                    ? live.Table($"c{i}", 0)
                    : live.Table($"c{i}");
            live.Int(child, "id", pk: true);

            // 最初の子の外部キーの ON DELETE。SET DEFAULT・SET NULL と NOT NULL 化の水準は子の列を NULL 許容で作る（SQL Server は
            // NOT NULL で既定値の無い列に SET DEFAULT を作れず＝Msg 1760、SET NULL は NULL 許容の列が要る）
            var onDelete =
                i == 1
                    ? row[ChildOnDelete] switch
                    {
                        "SetDefault" => ForeignKeyReferentialAction.SetDefault,
                        "SetNull" => ForeignKeyReferentialAction.SetNull,
                        "Cascade" => ForeignKeyReferentialAction.Cascade,
                        _ => ForeignKeyReferentialAction.NoAction,
                    }
                    : ForeignKeyReferentialAction.NoAction;
            var nullableChild =
                i == 1
                && (
                    row[ChildOnDelete] is "SetDefault" or "SetNull"
                    || row[TypeChange] == "ChildNotNull"
                );
            live.Ref(
                child,
                p,
                "p_ref",
                $"fk_c{i}_p",
                onDelete: onDelete,
                nullable: nullableChild,
                referenced: referencesUnique ? [u] : null
            );
        }

        // u と x の一意制約を足す操作は、一部が NULL の重複を重なりとみなす DB（Oracle は全部が NULL の行だけを検査の外に置き、
        // SQL Server は NULL どうしを等しいとみなす）で既存の行に断られる（測った事実＝SyncPlanFuzzDialect の宣言）
        if (
            nullRepeats
            && row[ExplicitOp] == "WidenUnique"
            && !kindChangesBothEnds
            && (
                live.Dialect.UniqueComparesPartiallyNullKeys
                || !live.Dialect.UniqueAllowsRepeatedNull
            )
        )
        {
            live.ExpectDataFailure(
                FuzzDataFailureKind.AddUniqueConstraint,
                "p",
                composite ? "uq_p_uyx" : "uq_p_ux",
                "u"
            );
        }

        // 巻き添えで作り直す外部キー（親の主キーを参照する表 d）
        if (row[CollateralFk] == "AlterOtherForeignKey")
        {
            var d = kindChangesBothEnds ? live.Table("d", 0) : live.Table("d");
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

        // 目標の図で外部キーを足す表 e（行 0・外部キーなし）。最初の列は水準の型（int・bigint・文字列）で、2 列目は p.k と同じ型
        // （外部キーの両端の型の水準が作る k の型の違いを、足す外部キーへ持ち込まない）
        if (row[AddedForeignKey] != "None")
        {
            // 親の無い値を持つ行（OrphanChild）：足す外部キーは既存の行で断られる（計画は列の値を見ないので警告しない。宣言して
            // 実 DB の表明 1 だけを外し、外した付け直しが消えないこと＝表明 1b は掛ける）
            var orphanRows = row[AddedForeignKey] == "OrphanChild";
            var e = orphanRows ? live.Table("e") : live.Table("e", 0);
            Func<int, string?>? orphan = orphanRows
                ? r => (900 + r).ToString(CultureInfo.InvariantCulture)
                : null;
            live.Int(e, "id", pk: true);

            if (orphanRows)
            {
                live.ExpectDataFailure(FuzzDataFailureKind.AddForeignKey, "e", "fk_e_p");
            }

            switch (row[AddedForeignKey])
            {
                case "BigIntChild":
                    live.SetKind(e, live.Int(e, "p_ref_0", nullable: true), FuzzTypeKind.BigInt);
                    break;
                case "VarCharChild":
                    live.VarChar(e, "p_ref_0", 10, nullable: true);
                    break;
                default:
                    live.Int(e, "p_ref_0", nullable: true, value: orphan);
                    break;
            }

            var secondColumn = live.Int(e, "p_ref_1", nullable: true, value: orphan);

            if (row[ForeignKeyTypes] == "DecimalOtherKeyColumn")
            {
                live.SetKind(e, secondColumn, FuzzTypeKind.Decimal, 12);
            }
            else if (row[ForeignKeyTypes] == "Wider" && !referencesUnique)
            {
                live.SetKind(e, secondColumn, FuzzTypeKind.BigInt);
            }
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
            case "BothEndsKindChange":
                // 参照される親の列と、それを参照する子の列すべて（c1..cN と、主キーを参照するなら d）を同じ別の種類へ。子を 1 つでも
                // 残すとその外部キーが同期後に食い違って名指しされ、予告の警告が途中の状態の失敗を隠す
                target.SetType(
                    "p",
                    row[ReferencedKey] == "Unique" ? "u" : "id",
                    FuzzTypeKind.VarChar,
                    10
                );

                for (var i = 1; i <= refs; i++)
                {
                    target.SetType($"c{i}", childColumn, FuzzTypeKind.VarChar, 10);
                }

                if (row[CollateralFk] == "AlterOtherForeignKey" && row[ReferencedKey] != "Unique")
                {
                    target.SetType("d", "p_ref_0", FuzzTypeKind.VarChar, 10);
                }

                break;
            case "DivergentEnds":
                // 外部キーの両端を別々の型へ：最初の子は互換の範囲で広げ（int → bigint）、参照される親の列は互換でない型へ（int →
                // varchar）。子の変更は先頭へ移しても通り、断られるのは親の変更。子の表を先頭へ移して子の変更を計画の順で先に並べる
                // （実 DB は取り込んだ表の名前の順で c1 が p より先。網の図は p が先なので、移さないと子が先の形が出ない）
                target.MoveTableFirst("c1");
                target.SetType("c1", childColumn, FuzzTypeKind.BigInt);
                target.SetType(
                    "p",
                    row[ReferencedKey] == "Unique" ? "u" : "id",
                    FuzzTypeKind.VarChar,
                    10
                );
                break;
            case "DivergentEndsStringLength":
            case "DivergentEndsStringLengthAcrossPrefix":
            case "DivergentEndsStringLengthInBand":
            case "DivergentEndsStringLengthLatin1":
                // 外部キーの両端を別々の型へ：最初の子は可変長の文字列の長さだけを伸ばし（水準の長さ。DB の今の型の表記の括弧の後ろ＝
                // 文字集合の指定はそのまま保つ。網の DB 側の図は書き、実 DB の取込は書かない。同期は列の照合順序を読み直して保つ）、参照される
                // 親の列は互換でない型へ（int）。子の表を先頭へ移す（DivergentEnds と同じ）
                target.MoveTableFirst("c1");
                target.SetStringLength("c1", childColumn, StringEndsOf(row)!.Value.NewLength);
                target.SetType("p", row[ReferencedKey] == "Unique" ? "u" : "id", FuzzTypeKind.Int);
                break;
            case "ChildNotNull":
                target.SetNullable("c1", childColumn, false);
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
                target.DropUniqueOn("p", UniqueNames(row)[0], ["u"]);
                break;
            case "DropAllUniques":
                // 最初の 1 つは必ず外す。2 つ目以降は、同じ列集合の一意制約を 1 つの索引にまとめる DB（SQLite）では外す物が残っていない
                foreach (var (name, index) in UniqueNames(row).Select((n, i) => (n, i)))
                {
                    target.DropUniqueOn("p", name, ["u"], absentAllowed: index > 0);
                }

                break;
            case "RenameKeepAndAddUnnamedOnOld":
                // 名前を保って最初の一意制約の列を u から w へ変え（作り直し）、元の列 u へ名前の無い一意制約を足す（取込が一意制約の
                // 名前を持たない方言＝SQLite では、u の一意制約を列で引いて変える）
                target.ChangeUniqueOn("p", UniqueNames(row)[0], ["u"], "w");
                target.AddUnique("p", null, "u");
                break;
            case "MoveOntoAddedUnique":
                // 名前を保って一意制約の列を u から w へ変え（作り直し）、移した先の列 w へ名前の無い一意制約を足す（目標の図は w に一意制約を
                // 2 つ持つ。持てる方言では両方を選ぶとどちらも作る）
                target.ChangeUniqueOn("p", UniqueNames(row)[0], ["u"], "w");
                target.AddUnique("p", null, "w");
                break;
            case "ChangeFirstDropOthers":
                // 最初の一意制約の列を u から w へ変え（作り直し＝後ろで付け直す削除）、ほかの u の一意制約を外す（ただの削除）。u を参照する
                // 外部キーがあれば、支えうるキーが全部外れて削除の組が前寄せされる。SQLite は u の一意制約を 1 つにまとめるので、変えた後に
                // 外す物は残っていない
                target.ChangeUniqueOn("p", UniqueNames(row)[0], ["u"], "w");

                foreach (var name in UniqueNames(row).Skip(1))
                {
                    target.DropUniqueOn("p", name, ["u"], absentAllowed: true);
                }

                break;
            case "ChangeAllUniques":
                // u の一意制約を両方とも別の列集合へ作り直す（最初を (w)・2 つ目を (u, w)）。u を参照する外部キーがあれば、支えうるキーが
                // 全部外れ、組の中に後ろで付け直す削除が 2 つ並ぶ（どう並べても前の 1 つが通った後に次が断られる形）
                target.ChangeUniqueOn("p", UniqueNames(row)[0], ["u"], "w");
                target.ChangeUnique("p", UniqueNames(row)[1], "u", "w");
                break;
            case "WidenUnique":
                // u と定数の列 x の一意制約を足す（u の一意制約の上位集合。u の値がどれも違えば通り、u が NULL の 2 行は一部が NULL の
                // 重複になる）
                if (IsComposite(row))
                {
                    target.AddUnique("p", "uq_p_uyx", "u", "y", "x");
                }
                else
                {
                    target.AddUnique("p", "uq_p_ux", "u", "x");
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

        // live の表 e の列から親の (id, k) へ外部キーを足す（主キーの変更の水準で (id, k) がキーでなくなる組も残す）
        if (row[AddedForeignKey] != "None")
        {
            target.AddForeignKey("e", ["p_ref_0", "p_ref_1"], "p", ["id", "k"], "fk_e_p");
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
                    "ChildKindChange",
                    "BothEndsKindChange",
                    "DivergentEnds",
                    "DivergentEndsStringLength",
                    "DivergentEndsStringLengthAcrossPrefix",
                    "DivergentEndsStringLengthInBand",
                    "DivergentEndsStringLengthLatin1",
                    "ChildNotNull"
                )
                || Has(ExplicitOp, "DropForeignKey")
                || Has(ForeignKeyTypes, "Wider", "DecimalOtherKeyColumn")
                || Has(ChildOnDelete, "SetDefault", "SetNull", "Cascade")
            )
        )
        {
            return false;
        }

        // SET DEFAULT・SET NULL の子の列は DB 側で NULL 許容なので、NULL 許容だけの変更の相手が無い
        if (Has(ChildOnDelete, "SetDefault", "SetNull") && Has(TypeChange, "ChildNullability"))
        {
            return false;
        }

        // u が NULL の行は、u の一意制約を参照する子（外部キーの列は NOT NULL）が指せず、u へ移す主キーは NOT NULL 化がデータで
        // 断られる（どちらもこの水準と別の形を持ち込む）
        if (
            Has(UniqueColumnData, "NullRepeats", "CompositeNullRepeats")
            && (Has(ReferencedKey, "Unique") || Has(PkChange, "MoveToUniqueColumn"))
        )
        {
            return false;
        }

        // 複合のキーの水準は u の一意制約を (u, y) に張るので、u だけの一意制約を参照する子と、u の一意制約を名前か列集合 (u) で
        // 引いて外す・変える操作の相手が無い
        if (
            Has(UniqueColumnData, "CompositeNullRepeats", "CompositePartialNull")
            && (
                Has(ReferencedKey, "Unique")
                || Has(
                    ExplicitOp,
                    "DropUnique",
                    "DropAllUniques",
                    "RenameKeepAndAddUnnamedOnOld",
                    "MoveOntoAddedUnique",
                    "ChangeFirstDropOthers",
                    "ChangeAllUniques"
                )
            )
        )
        {
            return false;
        }

        // 可変長の文字列の長さを変える水準は参照される親の列を varchar で作るので、親の列だけを bigint にする両端の型と、p.id を
        // 整数とみなす足す外部キーの子の型の水準（int・bigint・文字列）が成り立たない
        if (
            Has(
                TypeChange,
                "DivergentEndsStringLength",
                "DivergentEndsStringLengthAcrossPrefix",
                "DivergentEndsStringLengthInBand",
                "DivergentEndsStringLengthLatin1"
            )
            && (
                Has(ForeignKeyTypes, "Wider")
                || Has(AddedForeignKey, "IntChild", "BigIntChild", "VarCharChild", "OrphanChild")
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

        // 名前を保って列を変え元の列へ名前の無い一意制約を足すのは一意制約 1 つの行だけ（2 つなら元の列のもう 1 つが名前の無い
        // 制約を満たして、追加が出ない）
        if (
            Has(ExplicitOp, "RenameKeepAndAddUnnamedOnOld", "MoveOntoAddedUnique")
            && !Has(UniqueMultiplicity, "1")
        )
        {
            return false;
        }

        // 最初の一意制約の列を変えてほかを外す・全部の一意制約の列を変えるのは一意制約 2 つの行だけ（1 つならほかが無く、
        // 列を変える変更 1 つは名前の段の作り直しの水準で足りる）
        if (
            Has(ExplicitOp, "ChangeFirstDropOthers", "ChangeAllUniques")
            && !Has(UniqueMultiplicity, "2")
        )
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

        foreach (var requiredRow in RequiredRows)
        {
            var required = requiredRow.Assignment;

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

    /// <summary>
    /// 可変長の文字列の長さを変える型の変更の水準の、参照される親の列と子の列の長さ・最初の子の変えた後の長さ・文字集合（水準でなければ null）
    /// </summary>
    /// <remarks>
    /// 長さの頭のバイト数（255 バイト以下は 1 バイト・256 バイト以上は 2 バイト）を変えずに伸ばす変更だけを、MySQL は外部キーを張ったまま受ける。
    /// utf8mb4 は 1 文字 4 バイト、latin1 は 1 バイト：
    /// <list type="bullet">
    ///   <item>DivergentEndsStringLength：20 → 30 文字（80 → 120 バイト・受ける）</item>
    ///   <item>DivergentEndsStringLengthAcrossPrefix：20 → 64 文字（80 → 256 バイト・断る）</item>
    ///   <item>DivergentEndsStringLengthInBand：64 → 100 文字（256 → 400 バイト・受ける。文字数だけでは 64〜255 の帯で判定できない）</item>
    ///   <item>DivergentEndsStringLengthLatin1：latin1 の 40 → 70 文字（40 → 70 バイト・受ける。utf8mb4 なら 160 → 280 で断る）</item>
    /// </list>
    /// </remarks>
    private static (int Length, int NewLength, string? CharacterSet)? StringEndsOf(
        IReadOnlyDictionary<string, string> row
    ) =>
        row[TypeChange] switch
        {
            "DivergentEndsStringLength" => (20, 30, null),
            "DivergentEndsStringLengthAcrossPrefix" => (20, 64, null),
            "DivergentEndsStringLengthInBand" => (64, 100, null),
            "DivergentEndsStringLengthLatin1" => (40, 70, "latin1"),
            _ => null,
        };

    /// <summary>親の列 u のデータが複合のキー（u と y の一意制約）の水準か</summary>
    private static bool IsComposite(IReadOnlyDictionary<string, string> row) =>
        row[UniqueColumnData] is "CompositeNullRepeats" or "CompositePartialNull";

    private static Factor Levels(string name, string description, params string[] levels) =>
        new(name, [.. levels.Select(level => new Level(level))], description);
}
