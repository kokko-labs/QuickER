using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

    /// <summary>主キーでなく一意制約を参照する外部キー（後から足した水準）</summary>
    public const string ForeignKeyOnUnique = "fk-on-unique";

    /// <summary>主キーと同じ列の一意制約（後から足した水準）</summary>
    public const string PrimaryKeyTwinUnique = "pk-twin-unique";

    /// <summary>列の型の種類を変える変更（整数 → 文字列。後から足した水準）</summary>
    public const string KindChange = "kind-change";

    /// <summary>外部キーの両端の型の種類をそろえて変える変更（<see cref="KindChange"/> のうち両端のもの）</summary>
    public const string BothEndsKindChange = "both-ends-kind-change";

    /// <summary>型の違う live の列どうしに外部キーを足す変更（後から足した水準）</summary>
    public const string MismatchedForeignKeyAdd = "mismatched-fk-add";

    /// <summary>行のある子の、親の無い値を持つ列へ外部キーを足す変更（既存の行で断られる文の水準）</summary>
    public const string OrphanForeignKeyAdd = "orphan-fk-add";

    /// <summary>行のある列の精度（数値の精度・整数の幅・日時の秒の小数部）を下げる変更（既存の行で断られうる文の水準）</summary>
    public const string PrecisionDecrease = "precision-decrease";

    /// <summary>DB では普通の列を、目標の図だけで計算列にして型か NULL 許容を変える（後から足した水準）</summary>
    public const string DiagramOnlyComputed = "diagram-only-computed";

    /// <summary>識別子の上限で縮める既定名になる長い表名（大文字を混ぜる。後から足した水準・上限のある方言だけ）</summary>
    public const string LongDefaultName = "long-default-name";

    /// <summary>重複のある列への一意制約の追加・作り直しか主キーの付与（後から足した行の水準）</summary>
    public const string DuplicateData = "duplicate-data";

    /// <summary>NULL を含む列の NOT NULL 化か主キーへの追加（後から足した行の水準）</summary>
    public const string NullData = "null-data";

    /// <summary>
    /// NULL を含む列 1 つへの主キーの移動（後から足した行の水準 <see cref="NullData"/> の 3 つ目の選択肢。SQLite の整数の列では
    /// 新しい主キーが rowid の別名になる）
    /// </summary>
    public const string NullDataKeyMove = "null-data-key-move";

    /// <summary>データより短い長さへの文字列の列の縮小（後から足した行の水準）</summary>
    public const string ShortenBelowData = "shorten-below-data";

    /// <summary>行のある表への NOT NULL の列の追加（後から足した行の水準）</summary>
    public const string NotNullAddOnRows = "not-null-add-on-rows";

    /// <summary>計算列の式が参照する列の型の変更（後から足した行の水準）</summary>
    public const string ComputedSourceChange = "computed-source-change";

    /// <summary>SET NULL の外部キーの子の列の NOT NULL 化か主キーへの追加（後から足した行の水準。参照アクションを変える方言だけ）</summary>
    public const string SetNullChildNotNull = "set-null-child-not-null";

    /// <summary>子の列が NOT NULL の外部キーを SET DEFAULT へ変える（後から足した行の水準。参照アクションを変える方言だけ）</summary>
    public const string SetDefaultNotNullChild = "set-default-not-null-child";

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
        ForeignKeyOnUnique,
        PrimaryKeyTwinUnique,
        KindChange,
        BothEndsKindChange,
        MismatchedForeignKeyAdd,
        OrphanForeignKeyAdd,
        PrecisionDecrease,
        DiagramOnlyComputed,
        LongDefaultName,
        DuplicateData,
        NullData,
        NullDataKeyMove,
        ShortenBelowData,
        NotNullAddOnRows,
        ComputedSourceChange,
        SetNullChildNotNull,
        SetDefaultNotNullChild,
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
    /// 型の変更）が決定的に担う。網は 5 方言とも 5000 種で出るので網で表明する。
    /// 後から足した水準のうち目標の図の変更で出る形（<see cref="KindChange"/>・<see cref="BothEndsKindChange"/>・
    /// <see cref="MismatchedForeignKeyAdd"/>）も実 DB では求めない：網の 5000 種でそれぞれ 0.4〜3% しか出ず、普段の実 DB の種
    /// （SQL Server 100・MySQL 150）では出ない種の集合がありうる。実 DB では形の行（<see cref="SyncPlanShapeRows"/> の
    /// BothEndsKindChange・AddedForeignKey）が決定的に担う。既存の行で断られる文の水準（<see cref="OrphanForeignKeyAdd"/>・
    /// <see cref="PrecisionDecrease"/>・行の水準の <see cref="DuplicateData"/>・<see cref="NullData"/>・<see cref="ShortenBelowData"/>・
    /// <see cref="NotNullAddOnRows"/>・<see cref="ComputedSourceChange"/>）も同じく網でだけ求める（実 DB は形の行の OrphanChild と
    /// 期待の行）。DB 側の図で出る形（<see cref="ForeignKeyOnUnique"/>・
    /// <see cref="PrimaryKeyTwinUnique"/>）は実 DB でも求める
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
                    PrimaryKeyTwinUnique => dialect
                        .Provider
                        .SyncCapabilities
                        .SupportsUniqueConstraintOnPrimaryKey,
                    KindChange
                    or BothEndsKindChange
                    or MismatchedForeignKeyAdd
                    or OrphanForeignKeyAdd
                    or PrecisionDecrease
                    or DiagramOnlyComputed
                    or DuplicateData
                    or NullData
                    or NullDataKeyMove
                    or ShortenBelowData
                    or NotNullAddOnRows
                    or ComputedSourceChange => !realDatabase,
                    SetNullChildNotNull or SetDefaultNotNullChild => !realDatabase
                        && dialect.AllowCascade,
                    LongDefaultName => !realDatabase
                        && !dialect.Provider.SyncCapabilities.IdentifierLengthLimit.IsUnlimited,
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

/// <summary>既存の行で DB が断ると分かっている文の種類</summary>
internal enum FuzzDataFailureKind
{
    /// <summary>親の無い値を持つ列への外部キーの追加（名前は外部キーの名前）</summary>
    AddForeignKey,

    /// <summary>
    /// 既存の行で断られる列の変更（名前は列の名前）：列が空でないと通らない変更・NULL を含む列の NOT NULL 化（主キーへ足す変更が足す
    /// NOT NULL 化を含む）・データより短い長さへの縮小
    /// </summary>
    AlterColumn,

    /// <summary>重複のある列への一意制約の追加・作り直し（名前は一意制約の名前・列は重複のある列）</summary>
    AddUniqueConstraint,

    /// <summary>重複のある列への主キーの付与（名前は主キーの制約名 <c>PK_{表}</c>・列は重複のある列）</summary>
    AddPrimaryKey,

    /// <summary>行のある表への NOT NULL の列の追加（名前は列の名前。暗黙の既定値で埋める方言では断られない）</summary>
    AddColumn,

    /// <summary>
    /// 計算列の式が参照する列の型の変更（名前は列の名前。データでなくモデルの外の依存で断られる。断らない方言もある）
    /// </summary>
    AlterComputedSource,
}

/// <summary>既存の行で DB が断ると分かっている文（<see cref="FuzzLiveSchema.DataFailures"/>）</summary>
/// <param name="Kind">文の種類</param>
/// <param name="Table">表（外部キーは子の表）</param>
/// <param name="Name">外部キー・一意制約・主キーの名前か列の名前</param>
/// <param name="Column">一意制約・主キーの重複のある列（SQLite の再構築のエラー文で見分ける。ほかの種類は null）</param>
internal sealed record FuzzDataFailure(
    FuzzDataFailureKind Kind,
    string Table,
    string Name,
    string? Column = null
);

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

    /// <summary>
    /// 管理者として DDL の前に流す文（別のユーザー・データベースの作成。<see cref="PrepareStatements"/> より前。生成器は使わない＝期待の行の
    /// 実 DB の段が使う）
    /// </summary>
    public List<string> AdminStatements { get; } = [];

    /// <summary>DDL の前に流す文（スキーマの作成など。生成器は使わない＝期待の行の実 DB の段が使う）</summary>
    public List<string> PrepareStatements { get; } = [];

    /// <summary>データを入れる文（親→子の順）</summary>
    public List<string> InsertStatements { get; } = [];

    /// <summary>計算列を足す文（データを入れた後に流す）</summary>
    public List<string> ComputedColumnStatements { get; } = [];

    /// <summary>
    /// 同期の実行器を通さずに DB へ直接流す文（データ・計算列の文の後。実行器が増加を断る前提＝最初から壊れたビュー・既存の外部キー違反を
    /// 作るため。生成器は使わない＝期待の行の実 DB の段が使う）
    /// </summary>
    public List<string> RawStatements { get; } = [];

    /// <summary>この種に出た図の形（<see cref="FuzzShape"/>）</summary>
    public HashSet<string> Shapes { get; } = [];

    /// <summary>
    /// 既存の行で DB が断ると分かっている文（親の無い値を持つ列への外部キーの追加・列が空でないと通らない列の変更）。計画は列の値を
    /// 見ないので警告しない。実 DB の総当たりは、断られた文がこのどれかのときだけ表明 1（失敗は警告が予告する）を外す（表明 1b は掛ける）
    /// </summary>
    public List<FuzzDataFailure> DataFailures { get; } = [];

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

    /// <summary>
    /// 同期の前に DB を取り込んだ直後に呼ばれる（<see cref="Mutate"/> より前）。既定は何もしない（生成器と決定的な行は使わない。
    /// 期待の行の実 DB の段が、取り込んだ図とカタログの探針を同期の前に読むのに使う）
    /// </summary>
    /// <param name="dialect">方言</param>
    /// <param name="before">同期の前に取り込んだ図</param>
    /// <param name="ct">取り消し</param>
    Task OnImportedAsync(
        SyncPlanFuzzDialect dialect,
        SchemaImportResult before,
        CancellationToken ct
    ) => Task.CompletedTask;
}

/// <summary>
/// 同期計画の総当たりの生成器。DB 側の図（とデータ）・目標の図への変更・差分の選び方を 1 つの乱数から作る。
/// </summary>
/// <remarks>
/// <para>
/// データは「どの外部キーも満たす」ように作る（親の主キーは行番号 1..n・子の参照列は親の行を指す）。
/// 作らない形（表・列の説明・既定値のある列など）は <see cref="SyncPlanFuzzNotGenerated"/> に宣言する。
/// </para>
/// <para>
/// 後から足した水準（外部キーが一意制約を参照・主キーと同じ列の一意制約・整数の列の型の種類を文字列へ変える・型の違う live の文字列の列へ
/// 外部キーを足す）は、本流と別の乱数の列（<see cref="ForSeed"/>）で引く。本流の乱数の引き方・回数は変えないので、水準が出なかった種は
/// 足す前と同じケースのまま残る。型の種類を変える変更と型の違う外部キーの追加は、値の都合で断られないよう、値が全部 NULL か行 0 の列だけに作る。
/// </para>
/// <para>
/// 既存の行で断られる文は、さらに別の乱数の列で引く水準だけが作る（親の無い値・精度の縮小は <see cref="DataLevelSeed"/> の列、
/// 重複・NULL・データより短い長さ・行のある表への NOT NULL の列・計算列の参照する列の変更は <see cref="RowLevelSeed"/> の列）。
/// どれも <see cref="FuzzLiveSchema.DataFailures"/> に宣言する。
/// </para>
/// </remarks>
internal sealed class SyncPlanFuzzGenerator(
    Random rng,
    Random? levelRng,
    Random? dataLevelRng,
    Random? rowLevelRng,
    Random? nullKeyMoveRng,
    SyncPlanFuzzDialect dialect
) : ISyncPlanFuzzCase
{
    /// <summary>
    /// 種から生成器を作る（乱数の種を使う所はすべてここを通す）。本流の乱数は <c>new Random(seed)</c> のまま、後から足した水準
    /// （外部キーが一意制約を参照・型の種類を変える・主キーと同じ列の一意制約・型の違う live の列への外部キーの追加）は種から
    /// 決定的に作る別の乱数の列で引く。
    /// </summary>
    /// <param name="seed">種</param>
    /// <param name="dialect">方言</param>
    /// <param name="levels">後から足した水準を引くか（偽なら水準を足す前と同じケース）</param>
    /// <remarks>
    /// 別の列にしたのは、本流の乱数の引き方・回数を変えないため。水準が出なかった種は足す前と同じケースのまま残り、種の指紋は
    /// 水準が出た種だけ変わる。別の列の種は <see cref="HashCode.Combine{T1, T2}(T1, T2)"/> で作らない（プロセスごとに値が変わり、
    /// 種の指紋も網の種も再現しなくなる）。
    /// </remarks>
    public static SyncPlanFuzzGenerator ForSeed(
        int seed,
        SyncPlanFuzzDialect dialect,
        bool levels = true
    ) =>
        new(
            new Random(seed),
            levels ? new Random(LevelSeed(seed)) : null,
            levels ? new Random(DataLevelSeed(seed)) : null,
            levels ? new Random(RowLevelSeed(seed)) : null,
            levels ? new Random(NullKeyMoveSeed(seed)) : null,
            dialect
        );

    /// <summary>後から足した水準の乱数の列の種（種から決定的に作る）</summary>
    private static int LevelSeed(int seed) =>
        (int)(unchecked((uint)seed * 2654435761u) ^ 0x5EEDAC2u) & int.MaxValue;

    /// <summary>
    /// 既存の行で断られる文を作る水準（親の無い値を持つ列への外部キーの追加・精度の縮小）の乱数の列の種（種から決定的に作る。
    /// <see cref="LevelSeed"/> の列と分けて、先に足した水準の引き方を変えない）
    /// </summary>
    private static int DataLevelSeed(int seed) =>
        (int)(unchecked((uint)seed * 2246822519u) ^ 0xDA7A1EEu) & int.MaxValue;

    /// <summary>
    /// 行の水準（重複・NULL・データより短い長さ・行のある表への NOT NULL の列・計算列の参照する列の変更）の乱数の列の種
    /// （<see cref="DataLevelSeed"/> から決定的に作る）
    /// </summary>
    /// <remarks>
    /// 行の水準はデータの値を DB 側の図を作る段（<see cref="BuildLive"/>）で変えるので、<see cref="DataLevelSeed"/> の列で引くと、
    /// その列が後の <see cref="Mutate"/> で引く既存の水準（親の無い値・精度の縮小・図でだけ計算列）の引き方が全部の種で変わる。
    /// 別の列にして、既存の 3 本の列の引き方を変えない（種の指紋は行の水準が出た種だけ変わる）。
    /// </remarks>
    private static int RowLevelSeed(int seed) =>
        (int)(unchecked((uint)DataLevelSeed(seed) * 3266489917u) ^ 0x0A0B1EEu) & int.MaxValue;

    /// <summary>
    /// NULL の行の水準で、主キーをその列 1 つへ移すかを引く乱数の列の種（<see cref="RowLevelSeed"/> から決定的に作る）
    /// </summary>
    /// <remarks>
    /// NULL の行の水準を引いた種でだけ 1 回引く。別の列にして、<see cref="RowLevelSeed"/> の列の引き方を変えない（移さない種は
    /// 足す前と同じケースのまま残り、種の指紋は移した種だけ変わる）。
    /// </remarks>
    private static int NullKeyMoveSeed(int seed) =>
        (int)(unchecked((uint)RowLevelSeed(seed) * 2654435769u) ^ 0x4E11C3Eu) & int.MaxValue;

    /// <summary>行の水準（<see cref="RowLevelSeed"/> の列で 1 回だけ引く）</summary>
    private enum RowLevel
    {
        None,
        DuplicateData,
        NullData,
        ShortenBelowData,
        NotNullAddOnRows,
        ComputedSourceChange,
        SetNullChildNotNull,
        SetDefaultNotNullChild,
    }

    /// <summary>この種で引いた行の水準</summary>
    private RowLevel _rowLevel;

    /// <summary>行の水準でデータを変えた表と列（データを変えない水準は null）</summary>
    private (string Table, string Column)? _rowLevelTarget;

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

        // 後から足した水準（既存の行で断られる文の乱数の列。本流の乱数は引かない）：表名を識別子の上限の半分を超える長さへ伸ばし、
        // 大文字を混ぜる。名前の無い外部キーの既定名（FK_{子}_{親}）が上限を超えて縮められ（末尾に縮める前の名前のハッシュ）、大文字
        // 小文字を畳む DB の綴り（関係 N2）で既定名を求め直すとハッシュが変わる形を作る。上限の無い方言（SQLite）では伸ばさない
        var limit = dialect.Provider.SyncCapabilities.IdentifierLengthLimit;
        var longNames = dataLevelRng is not null && !limit.IsUnlimited && dataLevelRng.Next(6) == 0;

        if (longNames)
        {
            live.Shapes.Add(FuzzShape.LongDefaultName);
        }

        for (var i = 0; i < tableCount; i++)
        {
            var name = $"t{i}_{words[i]}";

            // 大文字を含む名前も混ぜる（目標の図で大小を変える変更の相手になる）
            if (rng.Next(4) == 0)
            {
                name = char.ToUpperInvariant(name[0]) + name[1..];
            }

            if (longNames)
            {
                name = LongName(name, limit.Max / 2 + 2);
            }

            BuildTable(live, name, i);
        }

        AddOtherSchemaTable(live);
        AddSameColumnTwoForeignKeys(live);
        AddSamePairTwoForeignKeys(live);
        AddUniqueConstraints(live);
        RetargetForeignKeyToUnique(live);
        AddPrimaryKeyTwinUnique(live);
        PickRowLevel(live);
        BuildInserts(live, dialect);
        AddComputedColumn(live);

        if (HasSiblingForeignKeys(live.Diagram.Relationships))
        {
            live.Shapes.Add(FuzzShape.SiblingForeignKeys);
        }

        return live;
    }

    /// <summary>名前を、大文字を混ぜた語（<c>_Long</c>）で <paramref name="length"/> 文字まで伸ばす</summary>
    private static string LongName(string name, int length)
    {
        var builder = new System.Text.StringBuilder(name);

        while (builder.Length < length)
        {
            builder.Append("_Long");
        }

        return builder.ToString(0, length);
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
            name: rng.Next(2) == 0 ? $"FK_{NamePart(child.TableName)}_{NamePart(baseName)}" : null
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
                    name: $"FK_{NamePart(child.TableName)}_{NamePart(BaseName(other))}_also"
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
                    Name = named
                        ? $"UQ_{NamePart(entity.TableName)}_{NamePart(column.Name)}_named"
                        : null,
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
                        Name = $"UQ_{NamePart(entity.TableName)}_{NamePart(column.Name)}_twin",
                        ColumnIds = [column.Id],
                    }
                );
            }
        }
    }

    /// <summary>
    /// 後から足した水準（別の乱数の列）：外部キーを、親の主キーから親の一意制約の列へ付け替え、子の列の値を親の一意な列の値へ
    /// 写し直す（主キーでなく一意制約を参照する外部キーの形）
    /// </summary>
    /// <remarks>
    /// 対象は 1 列の外部キーで、自己参照でなく、子の列をほかの外部キーが使わない（同じ列の外部キー 2 本・同じ列ペアの 2 本の値を
    /// 壊さない）もの。付け替え先は親の 1 列の一意制約のうち、主キーでなく、子の列と同じ型で、値が NULL を含まず重ならない（親が行 0 なら
    /// 問わない）列。
    /// </remarks>
    private void RetargetForeignKeyToUnique(FuzzLiveSchema live)
    {
        if (levelRng is null)
        {
            return;
        }

        var candidates = new List<(Relationship Relationship, Entity Parent, Column Target)>();

        foreach (var relationship in live.Diagram.Relationships)
        {
            if (
                relationship.SourceEntityId == relationship.TargetEntityId
                || relationship.ColumnPairs.Count != 1
            )
            {
                continue;
            }

            var parent = EntityOf(live, relationship.SourceEntityId);
            var child = EntityOf(live, relationship.TargetEntityId);
            var childColumnId = relationship.ColumnPairs[0].TargetColumnId;

            if (
                live.Diagram.Relationships.Any(other =>
                    !ReferenceEquals(other, relationship)
                    && other.ColumnPairs.Any(p => p.TargetColumnId == childColumnId)
                )
            )
            {
                continue;
            }

            var childColumn = child.Columns.Single(c => c.Id == childColumnId);
            var childInfo = live.Find(child.TableName, childColumn.Name)!;

            foreach (var unique in parent.UniqueConstraints.Where(u => u.ColumnIds.Count == 1))
            {
                var target = parent.Columns.Single(c => c.Id == unique.ColumnIds[0]);

                if (
                    !target.IsPrimaryKey
                    && live.Find(parent.TableName, target.Name) is { } info
                    && info.Kind == childInfo.Kind
                    && info.Length == childInfo.Length
                    && (live.RowCounts[parent.TableName] == 0 || info.IsDistinct)
                )
                {
                    candidates.Add((relationship, parent, target));
                }
            }
        }

        if (candidates.Count == 0 || levelRng.Next(2) != 0)
        {
            return;
        }

        var (chosen, chosenParent, targetColumn) = candidates[levelRng.Next(candidates.Count)];
        var keyColumn = chosenParent.Columns.Single(c =>
            c.Id == chosen.ColumnPairs[0].SourceColumnId
        );
        var keyValues = live.Find(chosenParent.TableName, keyColumn.Name)!.Values;
        var uniqueValues = live.Find(chosenParent.TableName, targetColumn.Name)!.Values;
        var chosenChild = EntityOf(live, chosen.TargetEntityId);
        var childValues = live.Find(
            chosenChild.TableName,
            chosenChild.Columns.Single(c => c.Id == chosen.ColumnPairs[0].TargetColumnId).Name
        )!.Values;

        // 子の値は親の主キーの値を指している。同じ行の一意な列の値へ写し直す（NULL は NULL のまま）
        for (var i = 0; i < childValues.Count; i++)
        {
            if (childValues[i] is { } value)
            {
                childValues[i] = uniqueValues[keyValues.IndexOf(value)];
            }
        }

        chosen.ColumnPairs =
        [
            new RelationshipColumnPair(targetColumn.Id, chosen.ColumnPairs[0].TargetColumnId),
        ];
        live.Shapes.Add(FuzzShape.ForeignKeyOnUnique);
    }

    /// <summary>
    /// 後から足した水準（別の乱数の列）：主キーと同じ列の一意制約を張る（持てる宣言の方言だけ＝
    /// <see cref="SyncDialectCapabilities.SupportsUniqueConstraintOnPrimaryKey"/>）。参照されている親の表を優先する
    /// （主キーを外しても外部キーを支えうる形）
    /// </summary>
    private void AddPrimaryKeyTwinUnique(FuzzLiveSchema live)
    {
        if (
            levelRng is null
            || !dialect.Provider.SyncCapabilities.SupportsUniqueConstraintOnPrimaryKey
        )
        {
            return;
        }

        var tables = live
            .Diagram.Entities.Where(e =>
                e.PrimaryKeyColumnIds.Count > 0
                && !e.TableName.Contains('.', StringComparison.Ordinal)
                && !e.UniqueConstraints.Any(u =>
                    u.ColumnIds.Count == e.PrimaryKeyColumnIds.Count
                    && u.ColumnIds.All(e.PrimaryKeyColumnIds.Contains)
                )
            )
            .ToList();

        if (tables.Count == 0 || levelRng.Next(4) != 0)
        {
            return;
        }

        var referenced = tables
            .Where(e =>
                live.Diagram.Relationships.Any(r =>
                    r.SourceEntityId == e.Id && r.TargetEntityId != e.Id
                )
            )
            .ToList();
        var pool = referenced.Count > 0 && levelRng.Next(3) != 0 ? referenced : tables;
        var table = pool[levelRng.Next(pool.Count)];
        table.UniqueConstraints.Add(
            new UniqueConstraint
            {
                Name = levelRng.Next(2) == 0 ? $"UQ_{NamePart(table.TableName)}_pk_twin" : null,
                ColumnIds = [.. table.GetPrimaryKeyColumnsInOrder().Select(c => c.Id)],
            }
        );
        live.Shapes.Add(FuzzShape.PrimaryKeyTwinUnique);
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

        // 後から足した水準（既存の行で断られる文の乱数の列。本流の乱数は引かない）：DB では普通の列を、目標の図だけで計算列にして
        // 型か NULL 許容を変える（DDL は計算列を普通の列として作るので、DB で普通の列の図の計算列は、普通の列として変えられる）
        if (dataLevelRng is not null && dataLevelRng.Next(8) == 0)
        {
            MarkComputedInDiagramOnly(entities, relationships, live);
        }

        ApplyRowLevel(entities, relationships, live);
        CheckDeclaredTargetShapes(entities);
    }

    /// <summary>
    /// 行の水準を引き、データを変える水準なら DB 側の図の値を変える（行のある表の普通の列。構造は変えない＝本流の乱数の引き方は
    /// 変えないが、値を変えた列は以後の本流の変更の候補（一意な値の列・NULL を含まない列）から外れうる）
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>重複：外部キー・一意制約・主キーに入らない整数か文字列の列の 2 行目を 1 行目と同じ値にする（目標の図で一意制約・主キーを
    ///   張る相手）。</item>
    ///   <item>NULL：主キーでも参照される列でもない、NULL を許す整数か文字列の列の 1 行目を NULL にする（外部キーの子の列も選ぶ＝
    ///   NULL の子は外部キーを満たす。SET NULL の外部キーの子の列は選ばない）。</item>
    ///   <item>データより短い長さ：外部キー・一意制約・主キーに入らない長さ 20 以上の文字列の列の 1 行目を 12 文字にする（目標の図で
    ///   長さ 5 へ縮める）。</item>
    /// </list>
    /// 行のある表への NOT NULL の列の追加と、計算列の参照する列の変更は値を変えない（<see cref="ApplyRowLevel"/> で目標の図だけを変える）。
    /// </remarks>
    private void PickRowLevel(FuzzLiveSchema live)
    {
        if (rowLevelRng is null)
        {
            return;
        }

        _rowLevel = rowLevelRng.Next(20) switch
        {
            0 => RowLevel.DuplicateData,
            1 => RowLevel.NullData,
            2 => RowLevel.ShortenBelowData,
            3 => RowLevel.NotNullAddOnRows,
            4 => RowLevel.ComputedSourceChange,
            5 => RowLevel.SetNullChildNotNull,
            6 => RowLevel.SetDefaultNotNullChild,
            _ => RowLevel.None,
        };

        if (
            _rowLevel
            is not (RowLevel.DuplicateData or RowLevel.NullData or RowLevel.ShortenBelowData)
        )
        {
            return;
        }

        var pairColumns = live
            .Diagram.Relationships.SelectMany(r =>
                r.ColumnPairs.SelectMany(p => new[] { p.SourceColumnId, p.TargetColumnId })
            )
            .ToHashSet();
        var referencedColumns = live
            .Diagram.Relationships.SelectMany(r => r.ColumnPairs.Select(p => p.SourceColumnId))
            .ToHashSet();
        var setNullChildren = SetNullChildColumnIds(live.Diagram.Relationships);
        var candidates = live
            .Diagram.Entities.Where(e => RowCount(live, e) >= 2)
            .SelectMany(e =>
                e.Columns.Where(c => !c.IsPrimaryKey && Info(live, e, c) is { Computed: false })
                    .Where(c =>
                        _rowLevel switch
                        {
                            RowLevel.DuplicateData => !pairColumns.Contains(c.Id)
                                && !e.UniqueConstraints.Any(u => u.ColumnIds.Contains(c.Id))
                                && Info(live, e, c)
                                    is {
                                        Kind: FuzzTypeKind.Int or FuzzTypeKind.VarChar,
                                        IsDistinct: true,
                                    },
                            RowLevel.NullData => c.IsNullable
                                && !referencedColumns.Contains(c.Id)
                                && !setNullChildren.Contains(c.Id)
                                && Info(live, e, c)
                                    is { Kind: FuzzTypeKind.Int or FuzzTypeKind.VarChar },
                            _ => !pairColumns.Contains(c.Id)
                                && !e.UniqueConstraints.Any(u => u.ColumnIds.Contains(c.Id))
                                && Info(live, e, c)
                                    is { Kind: FuzzTypeKind.VarChar, Length: >= 20 },
                        }
                    )
                    .Select(c => (Entity: e, Column: c))
            )
            .ToList();

        if (candidates.Count == 0)
        {
            _rowLevel = RowLevel.None;
            return;
        }

        var (entity, column) = candidates[rowLevelRng.Next(candidates.Count)];
        var values = Info(live, entity, column)!.Values;

        switch (_rowLevel)
        {
            case RowLevel.DuplicateData:
                values[1] = values[0];
                break;
            case RowLevel.NullData:
                values[0] = null;
                break;
            default:
                values[0] = dialect.StringLiteral("xxxxxxxxxxxx");
                break;
        }

        _rowLevelTarget = (entity.TableName, column.Name);
    }

    /// <summary>
    /// 行の水準の変更を目標の図へ加え、既存の行（とモデルの外の依存）で DB が断ると決まっている文を宣言する。本流の変更が表・列を
    /// 消したか、変えてはいけない列（計算列の式が参照する列・計算列）に変えたときは何もしない
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>重複：その列へ名前つきの一意制約を足す／主キーをその列へ移す（表名が 16 文字以下＝主キーの制約名 <c>PK_{表}</c> を縮めない）
    ///   ／名前つきの一意制約をその列へ移す（作り直し）。</item>
    ///   <item>NULL：その列を NOT NULL にする／主キーへ足す（主キーの変更が NOT NULL 化を足す）。</item>
    ///   <item>データより短い長さ：長さ 5 へ縮める。</item>
    ///   <item>行のある表への NOT NULL の列の追加：整数の NOT NULL の列を足す（暗黙の既定値で埋める方言では断られない）。</item>
    ///   <item>計算列の参照する列の変更：整数を 64 ビット整数へ広げる（データに依らず、式の依存で断る方言がある）。</item>
    /// </list>
    /// 変更の文は本流と同じ頭で書く（網羅の計数の変更の種類を増やさない）。
    /// </remarks>
    private void ApplyRowLevel(
        List<Entity> entities,
        List<Relationship> relationships,
        FuzzLiveSchema live
    )
    {
        if (rowLevelRng is null || _rowLevel == RowLevel.None)
        {
            return;
        }

        switch (_rowLevel)
        {
            case RowLevel.NotNullAddOnRows:
                AddNotNullColumnOnRows(entities, live);
                return;
            case RowLevel.ComputedSourceChange:
                ChangeComputedSource(entities, live);
                return;
            case RowLevel.SetNullChildNotNull:
                TightenSetNullChild(entities, relationships, live);
                return;
            case RowLevel.SetDefaultNotNullChild:
                SetDefaultOnNotNullChild(entities, relationships, live);
                return;
        }

        var (table, columnName) = _rowLevelTarget!.Value;
        var entity = entities.FirstOrDefault(e => e.TableName == table);
        var column = entity?.Columns.FirstOrDefault(c => c.Name == columnName);

        if (
            entity is null
            || column is null
            || column.IsComputed
            || Info(live, entity, column) is not { Pinned: false }
        )
        {
            return;
        }

        switch (_rowLevel)
        {
            case RowLevel.DuplicateData:
                ApplyDuplicateData(entities, entity, column, relationships, live);
                break;
            case RowLevel.NullData:
                ApplyNullData(entity, column, relationships, live);
                break;
            default:
                column.DataType = dialect.TypeName(FuzzTypeKind.VarChar, 5);
                live.DataFailures.Add(
                    new FuzzDataFailure(FuzzDataFailureKind.AlterColumn, table, columnName)
                );
                live.Shapes.Add(FuzzShape.ShortenBelowData);
                Mutations.Add(
                    $"alter type {table}.{columnName} -> {column.DataType}{TypeTag(FuzzTypeKind.VarChar, 5)}"
                );
                break;
        }
    }

    /// <summary>重複のある列へ一意制約を足す・主キーを移す・名前つきの一意制約を移す（<see cref="ApplyRowLevel"/>）</summary>
    private void ApplyDuplicateData(
        List<Entity> entities,
        Entity entity,
        Column column,
        List<Relationship> relationships,
        FuzzLiveSchema live
    )
    {
        var table = entity.TableName;

        // 本流の変更が同じ列に一意制約・主キーを張っていたら何もしない（重複の値の列には張らないはずの本流と重ねない）
        if (
            column.IsPrimaryKey
            || entity.UniqueConstraints.Any(u => u.ColumnIds.Contains(column.Id))
        )
        {
            return;
        }

        var named = entity.UniqueConstraints.Where(u => u.Name is not null).ToList();
        var options = new List<Action>
        {
            () =>
            {
                var name = UnusedConstraintName(
                    $"UQ_{NamePart(table)}_dup",
                    entities,
                    relationships
                );
                entity.UniqueConstraints.Add(
                    new UniqueConstraint { Name = name, ColumnIds = [column.Id] }
                );
                live.DataFailures.Add(
                    new FuzzDataFailure(
                        FuzzDataFailureKind.AddUniqueConstraint,
                        table,
                        name,
                        column.Name
                    )
                );
                Mutations.Add($"add unique {table}({column.Name}) named");
            },
        };

        if (table.Length <= 16)
        {
            options.Add(() =>
            {
                SetPrimaryKey(entity, [column]);
                live.DataFailures.Add(
                    new FuzzDataFailure(
                        FuzzDataFailureKind.AddPrimaryKey,
                        table,
                        $"PK_{table}",
                        column.Name
                    )
                );
                Mutations.Add(
                    $"move pk {table} -> {column.Name}{ReferencingTag(entity, relationships)}"
                );
            });
        }

        if (named.Count > 0)
        {
            options.Add(() =>
            {
                var unique = named[rowLevelRng!.Next(named.Count)];
                unique.ColumnIds = [column.Id];
                live.DataFailures.Add(
                    new FuzzDataFailure(
                        FuzzDataFailureKind.AddUniqueConstraint,
                        table,
                        unique.Name!,
                        column.Name
                    )
                );
                Mutations.Add($"change unique {table} {unique.Name} -> ({column.Name})");
            });
        }

        options[rowLevelRng!.Next(options.Count)]();
        live.Shapes.Add(FuzzShape.DuplicateData);
    }

    /// <summary>NULL を含む列を NOT NULL にする・主キーへ足す・主キーをその列へ移す（<see cref="ApplyRowLevel"/>）</summary>
    private void ApplyNullData(
        Entity entity,
        Column column,
        List<Relationship> relationships,
        FuzzLiveSchema live
    )
    {
        // 本流の変更が NOT NULL にしていた・SET NULL の外部キーの子にしていたら何もしない
        if (
            !column.IsNullable
            || column.IsPrimaryKey
            || SetNullChildColumnIds(relationships).Contains(column.Id)
        )
        {
            return;
        }

        var table = entity.TableName;
        var key = entity.GetPrimaryKeyColumnsInOrder();

        var extend = key.Count > 0 && rowLevelRng!.Next(2) == 0;

        // 3 つ目の選択肢は別の乱数の列で引く（NullKeyMoveSeed）：主キーをその列 1 つへ移す。SQLite で整数の列なら新しい主キーが
        // rowid の別名（宣言の型 INTEGER の単一列）になる形で、ほかの方言・型では主キーへ足す形と同じく NOT NULL 化が断られる
        if (nullKeyMoveRng is not null && nullKeyMoveRng.Next(3) == 0)
        {
            SetPrimaryKey(entity, [column]);
            Mutations.Add(
                $"move pk {table} -> {column.Name}{ReferencingTag(entity, relationships)}"
            );
            live.Shapes.Add(FuzzShape.NullDataKeyMove);
        }
        else if (extend)
        {
            SetPrimaryKey(entity, [.. key, column]);
            Mutations.Add(
                $"extend pk {table} + {column.Name}{ReferencingTag(entity, relationships)}"
            );
        }
        else
        {
            column.IsNullable = false;
            Mutations.Add($"nullable {table}.{column.Name} -> not null");
        }

        live.DataFailures.Add(
            new FuzzDataFailure(FuzzDataFailureKind.AlterColumn, table, column.Name)
        );
        live.Shapes.Add(FuzzShape.NullData);
    }

    /// <summary>行のある表へ整数の NOT NULL の列を足す（<see cref="ApplyRowLevel"/>）</summary>
    private void AddNotNullColumnOnRows(List<Entity> entities, FuzzLiveSchema live)
    {
        var tables = entities.Where(e => RowCount(live, e) > 0).ToList();

        if (tables.Count == 0)
        {
            return;
        }

        var entity = tables[rowLevelRng!.Next(tables.Count)];
        var name = $"added_nn_{rowLevelRng.Next(1000)}";
        entity.Columns.Add(
            new Column
            {
                Name = name,
                DataType = dialect.TypeName(FuzzTypeKind.Int, 0),
                IsNullable = false,
            }
        );
        live.DataFailures.Add(
            new FuzzDataFailure(FuzzDataFailureKind.AddColumn, entity.TableName, name)
        );
        live.Shapes.Add(FuzzShape.NotNullAddOnRows);
        Mutations.Add($"add column {entity.TableName}.{name} Int not null");
    }

    /// <summary>計算列の式が参照する整数の列を 64 ビット整数へ広げる（<see cref="ApplyRowLevel"/>）</summary>
    private void ChangeComputedSource(List<Entity> entities, FuzzLiveSchema live)
    {
        var candidates = entities
            .SelectMany(e =>
                e.Columns.Where(c =>
                        !c.IsComputed
                        && Info(live, e, c) is { Pinned: true, Kind: FuzzTypeKind.Int }
                        && c.DataType == dialect.TypeName(FuzzTypeKind.Int, 0)
                    )
                    .Select(c => (Entity: e, Column: c))
            )
            .ToList();

        if (candidates.Count == 0)
        {
            return;
        }

        var (entity, column) = candidates[rowLevelRng!.Next(candidates.Count)];
        column.DataType = dialect.TypeName(FuzzTypeKind.BigInt, 0);
        live.DataFailures.Add(
            new FuzzDataFailure(
                FuzzDataFailureKind.AlterComputedSource,
                entity.TableName,
                column.Name
            )
        );
        live.Shapes.Add(FuzzShape.ComputedSourceChange);
        Mutations.Add(
            $"alter type {entity.TableName}.{column.Name} -> {column.DataType}{TypeTag(FuzzTypeKind.BigInt, 0)}"
        );
    }

    /// <summary>
    /// 目標の図が方言の宣言を守っているか（宣言の誤りは例外＝その種の失敗）。同じ列の並びに一意制約を 2 つ張れない方言
    /// （<see cref="SyncPlanFuzzDialect.AllowDuplicateUniqueConstraints"/> が偽＝Oracle の ORA-02261）では、同じ列集合の一意制約を
    /// 2 つ以上持つ表を作らない（その図は DDL も同期も DB が断る。計画の誤りでなく生成器の誤りとして名指しする）
    /// </summary>
    private void CheckDeclaredTargetShapes(List<Entity> entities)
    {
        if (dialect.AllowDuplicateUniqueConstraints)
        {
            return;
        }

        foreach (var entity in entities)
        {
            var duplicated = entity
                .UniqueConstraints.GroupBy(u =>
                    string.Join(",", u.ColumnIds.Order().Select(id => id.ToString()))
                )
                .FirstOrDefault(g => g.Count() > 1);

            if (duplicated is not null)
            {
                throw new InvalidOperationException(
                    $"generator declaration: {entity.TableName} has {duplicated.Count()} unique constraints on the same columns ({string.Join(", ", duplicated.Select(u => u.Name ?? "(unnamed)"))}), which {dialect.Name} cannot hold"
                );
            }
        }
    }

    /// <summary>
    /// SET NULL の外部キーの子の列を NOT NULL にするか主キーへ足す（<see cref="ApplyRowLevel"/>）。値は NULL を含まない列か行 0 の表だけ
    /// （NOT NULL 化をデータで断らせない＝SET NULL の宣言の分岐だけを見る）
    /// </summary>
    /// <remarks>
    /// SET NULL の子の列の NOT NULL 化を断る方言（<see cref="SyncDialectCapabilities.SetNullRequiresNullableChildColumn"/>＝MySQL）では計画が
    /// その変更を先頭へ移して警告し、ほかの方言は通して警告する（親の行の削除・更新が後で断られる）。
    /// </remarks>
    private void TightenSetNullChild(
        List<Entity> entities,
        List<Relationship> relationships,
        FuzzLiveSchema live
    )
    {
        var candidates = relationships
            .Where(r =>
                r.OnDelete == ForeignKeyReferentialAction.SetNull
                || r.OnUpdate == ForeignKeyReferentialAction.SetNull
            )
            .Select(r =>
            {
                var child = entities.FirstOrDefault(e => e.Id == r.TargetEntityId);
                var column = child?.Columns.FirstOrDefault(c =>
                    c.Id == r.ColumnPairs.FirstOrDefault()?.TargetColumnId
                );
                return (Child: child, Column: column);
            })
            .Where(x =>
                x.Child is not null
                && x.Column is { IsNullable: true, IsPrimaryKey: false, IsComputed: false }
                && Info(live, x.Child, x.Column) is { Pinned: false } info
                && (info.HasNoNull || RowCount(live, x.Child) == 0)
            )
            .ToList();

        if (candidates.Count == 0)
        {
            return;
        }

        var (entity, column) = candidates[rowLevelRng!.Next(candidates.Count)];
        var key = entity!.GetPrimaryKeyColumnsInOrder();

        if (key.Count > 0 && rowLevelRng.Next(2) == 0)
        {
            SetPrimaryKey(entity, [.. key, column!]);
            Mutations.Add(
                $"extend pk {entity.TableName} + {column!.Name}{ReferencingTag(entity, relationships)}"
            );
        }
        else
        {
            column!.IsNullable = false;
            Mutations.Add($"nullable {entity.TableName}.{column.Name} -> not null");
        }

        live.Shapes.Add(FuzzShape.SetNullChildNotNull);
    }

    /// <summary>
    /// 子の列がどれも NOT NULL の外部キーの ON DELETE か ON UPDATE を SET DEFAULT へ変える（<see cref="ApplyRowLevel"/>。参照アクションを
    /// 変える方言だけ）
    /// </summary>
    /// <remarks>
    /// 図は既定値を持たないので、SET DEFAULT の子の列は既定値が無い。計画は列の DEFAULT 制約を要る方言
    /// （<see cref="SyncDialectCapabilities.SetDefaultRequiresColumnDefault"/>＝SQL Server）で警告し、SET DEFAULT を持たない方言は
    /// 実効値（NO ACTION）へ畳む。SQL Server は参照アクションを変えない方言なので、網羅の行（手で書いた行）が受け持つ。
    /// </remarks>
    private void SetDefaultOnNotNullChild(
        List<Entity> entities,
        List<Relationship> relationships,
        FuzzLiveSchema live
    )
    {
        if (!dialect.AllowCascade)
        {
            return;
        }

        var candidates = relationships
            .Where(r =>
                r.ColumnPairs.Count > 0
                && r.ColumnPairs.All(p =>
                    entities
                        .SelectMany(e => e.Columns)
                        .FirstOrDefault(c => c.Id == p.TargetColumnId)
                        is { IsNullable: false }
                )
            )
            .ToList();

        if (candidates.Count == 0)
        {
            return;
        }

        var relationship = candidates[rowLevelRng!.Next(candidates.Count)];
        var onUpdate = rowLevelRng.Next(3) == 0;

        if (
            (onUpdate ? relationship.OnUpdate : relationship.OnDelete)
            == ForeignKeyReferentialAction.SetDefault
        )
        {
            return;
        }

        if (onUpdate)
        {
            relationship.OnUpdate = ForeignKeyReferentialAction.SetDefault;
        }
        else
        {
            relationship.OnDelete = ForeignKeyReferentialAction.SetDefault;
        }

        Mutations.Add(
            $"fk action {Name(entities, relationship.TargetEntityId)} -> {Name(entities, relationship.SourceEntityId)} {(onUpdate ? "ON UPDATE" : "ON DELETE")} {ForeignKeyReferentialAction.SetDefault}"
        );
        live.Shapes.Add(FuzzShape.SetDefaultNotNullChild);
    }

    /// <summary>DB の普通の整数の列を、目標の図だけで計算列にして型か NULL 許容を変える</summary>
    private void MarkComputedInDiagramOnly(
        List<Entity> entities,
        List<Relationship> relationships,
        FuzzLiveSchema live
    )
    {
        var keyColumns = relationships
            .SelectMany(r =>
                r.ColumnPairs.SelectMany(p => new[] { p.SourceColumnId, p.TargetColumnId })
            )
            .ToHashSet();
        var candidates = entities
            .SelectMany(e =>
                e.Columns.Where(c =>
                        !c.IsComputed
                        && !c.IsPrimaryKey
                        && !keyColumns.Contains(c.Id)
                        && !e.UniqueConstraints.Any(u => u.ColumnIds.Contains(c.Id))
                        && Info(live, e, c)
                            is { Kind: FuzzTypeKind.Int, Pinned: false, Computed: false }
                    )
                    .Select(c => (Entity: e, Column: c))
            )
            .ToList();

        if (candidates.Count == 0)
        {
            return;
        }

        var (entity, column) = candidates[dataLevelRng!.Next(candidates.Count)];
        column.IsComputed = true;

        if (!column.IsNullable && dataLevelRng.Next(2) == 0)
        {
            column.IsNullable = true;
            Mutations.Add(
                $"computed nullable {entity.TableName}.{column.Name} -> True (diagram only)"
            );
        }
        else
        {
            column.DataType = dialect.TypeName(FuzzTypeKind.BigInt, 0);
            Mutations.Add(
                $"computed type {entity.TableName}.{column.Name} -> {column.DataType} (diagram only)"
            );
        }

        live.Shapes.Add(FuzzShape.DiagramOnlyComputed);
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

        // データのある日時の列の精度を下げる変更を DB が断る方言（Oracle の ORA-30082＝方言の宣言
        // PrecisionDecreaseRequiresEmptyColumn）では、既存の行で断られる文の水準のときだけ作る（断られる文として宣言する＝下）。
        // ほかは作らない
        var capabilities = dialect.Provider.SyncCapabilities;

        if (
            kind == FuzzTypeKind.DateTime
            && info.Kind == FuzzTypeKind.DateTime
            && length < info.Length
            && RowCount(live, entity) > 0
            && capabilities.PrecisionDecreaseRequiresEmptyColumn
            && (dataLevelRng is null || dataLevelRng.Next(2) != 0)
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

        // 後から足した水準（別の乱数の列）：整数の列を bigint でなく文字列へ（型の種類を変える）。変える列と揃える相手の値が全部 NULL か
        // 行 0 のときだけ（Oracle は行のある列の型の種類を変えられない＝ORA-01439。親が行 0 なら子の参照列は NULL だけなので、外部キーの
        // 両端をそろえて変える形はここで出る）
        if (
            levelRng is not null
            && info.Kind == FuzzTypeKind.Int
            && kind == FuzzTypeKind.BigInt
            && partners.All(p =>
                Info(live, p.Entity, p.Column) is { } pi && pi.Values.All(v => v is null)
            )
            && levelRng.Next(2) == 0
        )
        {
            (kind, length) = (FuzzTypeKind.VarChar, 10);
            live.Shapes.Add(FuzzShape.KindChange);

            if (partners.Count >= 2)
            {
                live.Shapes.Add(FuzzShape.BothEndsKindChange);
            }
        }
        // 後から足した水準（既存の行で断られる文の乱数の列）：数値の精度・整数の幅を下げる（小数は精度を 2 つ・bigint は int へ）。
        // 値は新しい型に収まるので、値が収まれば通す方言では通り、列が空でないと下げられない方言（Oracle の ORA-01440）では行のある
        // 列で断られる（断られる文として宣言する＝下）
        else if (
            dataLevelRng is not null
            && info.Kind is FuzzTypeKind.Decimal or FuzzTypeKind.BigInt
            && dataLevelRng.Next(3) == 0
        )
        {
            (kind, length) =
                info.Kind == FuzzTypeKind.Decimal
                    ? (FuzzTypeKind.Decimal, info.Length - 2)
                    : (FuzzTypeKind.Int, 0);
            live.Shapes.Add(FuzzShape.PrecisionDecrease);
        }

        if (
            kind == FuzzTypeKind.DateTime
            && info.Kind == FuzzTypeKind.DateTime
            && length < info.Length
        )
        {
            live.Shapes.Add(FuzzShape.PrecisionDecrease);
        }

        foreach (var (owner, partner) in partners)
        {
            var oldType = partner.DataType;
            partner.DataType = dialect.TypeName(kind, length);

            // 列が空でないと通らない変更を行のある列へ当てるなら、既存の行で断られる文として宣言する（計画は列の値を見ない）
            if (
                Info(live, owner, partner) is { } values
                && values.Values.Any(v => v is not null)
                && capabilities.RequiresEmptyColumn(oldType, partner.DataType)
            )
            {
                live.DataFailures.Add(
                    new FuzzDataFailure(
                        FuzzDataFailureKind.AlterColumn,
                        owner.TableName,
                        partner.Name
                    )
                );
            }

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

        // 後から足した水準（別の乱数の列）：行 0 の子の、型の違う live の文字列の列へ張る（どちらの端の型もこの同期で決まらない
        // 外部キーの追加。整数の主キーと文字列の列は 4 方言とも外部キーを作れない＝MySQL 3780・SQL Server 1778・PostgreSQL 42804・
        // Oracle ORA-02267。行 0 にするのは値の都合で断られないため。本流の乱数の引き方は変えない＝上の選び方の後で差し替える）
        if (levelRng is not null && RowCount(live, child) == 0)
        {
            var mismatched = child
                .Columns.Where(c =>
                    !c.IsComputed
                    && !c.IsPrimaryKey
                    && Info(live, child, c)
                        is { Kind: FuzzTypeKind.VarChar or FuzzTypeKind.FixedChar } info
                    && !info.Computed
                    && !string.Equals(
                        c.DataType,
                        parentKey.DataType,
                        StringComparison.OrdinalIgnoreCase
                    )
                    && CanAddForeignKey(
                        relationships,
                        parent,
                        c,
                        ForeignKeyReferentialAction.NoAction,
                        ForeignKeyReferentialAction.NoAction
                    )
                )
                .ToList();

            if (mismatched.Count > 0 && levelRng.Next(3) == 0)
            {
                column = mismatched[levelRng.Next(mismatched.Count)];
                live.Shapes.Add(FuzzShape.MismatchedForeignKeyAdd);
            }
        }

        // 後から足した水準（既存の行で断られる文の乱数の列）：行のある子の、親の無い値を持つ live の整数の列へ張る（計画は列の値を
        // 見ないので警告せず、DB は既存の行で断る＝MySQL 1452・SQL Server 547・PostgreSQL 23503・Oracle ORA-02298・SQLite の外部キー
        // 違反。宣言して実 DB の表明 1 だけを外し、同じ計画で外した付け直しが消えないこと＝表明 1b を見る）
        var orphan = false;

        if (dataLevelRng is not null && RowCount(live, child) > 0 && dataLevelRng.Next(3) == 0)
        {
            var orphans = child
                .Columns.Where(c =>
                    !c.IsComputed
                    && c != parentKey
                    && string.Equals(
                        c.DataType,
                        parentKey.DataType,
                        StringComparison.OrdinalIgnoreCase
                    )
                    && Info(live, child, c) is { Kind: FuzzTypeKind.Int, Computed: false } info
                    && info.Values.Any(v => v is not null && !parentIds.Contains(v))
                    && CanAddForeignKey(
                        relationships,
                        parent,
                        c,
                        ForeignKeyReferentialAction.NoAction,
                        ForeignKeyReferentialAction.NoAction
                    )
                )
                .ToList();

            if (orphans.Count > 0)
            {
                column = orphans[dataLevelRng.Next(orphans.Count)];
                orphan = true;
                live.Shapes.Add(FuzzShape.OrphanForeignKeyAdd);
            }
        }

        var name =
            rng.Next(2) == 0
                ? UnusedConstraintName($"FK_added_{rng.Next(1000)}", entities, relationships)
                : null;

        // 断られる文を名前で見分けるので、名前の無い追加には名前を付ける（本流の乱数は引かない）
        if (orphan)
        {
            name ??= UnusedConstraintName("FK_orphan", entities, relationships);
            live.DataFailures.Add(
                new FuzzDataFailure(FuzzDataFailureKind.AddForeignKey, child.TableName, name)
            );
        }

        relationships.Add(NewRelationship(parent, child, [(parentKey, column)], name: name));
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

        // 同じ列の並びに一意制約を 2 つ張れない方言（Oracle＝ORA-02261）では、ほかの一意制約が乗る列へ移さない（目標の図を DB が
        // 持てない形にしない。AddUnique と同じ宣言。目標の図の自己検査 CheckDeclaredTargetShapes が守る）
        var column = entity
            .Columns.Where(c =>
                !c.IsComputed
                && !unique.ColumnIds.Contains(c.Id)
                && Info(live, entity, c) is { } info
                && info.Kind != FuzzTypeKind.Decimal
                && IsUniqueKind(info.Kind)
                && (RowCount(live, entity) == 0 || info.IsDistinct)
                && (
                    dialect.AllowDuplicateUniqueConstraints
                    || !entity.UniqueConstraints.Any(u =>
                        u != unique && u.ColumnIds.SequenceEqual([c.Id])
                    )
                )
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
        ApplySelection(rng, selection, items);
        return selection;
    }

    /// <summary>
    /// 選び方 <paramref name="selection"/> で項目の選択を書き換える（1 項目・種類の集まり・項目ごとの半々を
    /// <paramref name="rng"/> から引く。引く順と回数は選び方に依らず同じ＝生成器の乱数の列を変えない）
    /// </summary>
    /// <param name="rng">乱数</param>
    /// <param name="selection">選び方</param>
    /// <param name="items">差分の項目</param>
    internal static void ApplySelection(
        Random rng,
        FuzzSelection selection,
        IReadOnlyList<SchemaDiffItem> items
    )
    {
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
    /// 生成器が明示の制約名に埋める表名の部分（別のスキーマの表の <c>.</c> は <c>_</c>＝製品の既定名と同じく、制約名をスキーマで修飾した
    /// 名前に見せない。長い名前の水準の表名は先頭の 16 文字だけ＝明示の名前は利用者が付けた名前なので製品は縮めずに上限で断る。既定名
    /// だけを上限で縮める形にする）
    /// </summary>
    private static string NamePart(string table)
    {
        var part = table.Replace('.', '_');
        return part.Length > 16 ? part[..16] : part;
    }

    private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);
}
