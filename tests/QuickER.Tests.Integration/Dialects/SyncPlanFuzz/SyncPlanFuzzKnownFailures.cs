using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using QuickER.Dialects;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>
/// 同期計画の総当たり（<see cref="SyncPlanFuzzTestsBase"/>）の既知の失敗の一覧（厳密: 一覧に無い失敗も、一覧に載っているのに
/// 起きない失敗も赤）
/// </summary>
/// <remarks>
/// 項目は <see cref="Entry"/> で作り、種の番号でなく形で失敗を名指しする（生成器を変えて乱数の列がずれても、同じ形の失敗は
/// 同じ項目に当たる）。載っている方言の失敗だけを既知とし（範囲＝方言名）、ほかの方言で同じ形が起きたら新しい失敗として赤にする。
/// 修正の区切りは項目を消してから修正し、総当たりが緑になることで直ったことを確かめる。「起きなかった」の判定は、項目の
/// <see cref="KnownEntry.Reproduction"/> が言う既定の種の数で流したときだけ行う（種を絞ると、その形が出ないことがある。
/// 夜間に見つけた形は夜間の既定の種の数でだけ数える）。照合の規則は <see cref="KnownFailureSet"/> が持つ。
/// </remarks>
internal static partial class SyncPlanFuzzKnownFailures
{
    /// <summary>項目（<see cref="Entry"/> で作る）</summary>
    public static readonly IReadOnlyList<KnownEntry> Entries =
    [
        // candidate-key-re-add-collateral（旧名 nightly-mysql-1191・修正 A）：参照される表の主キーを変える変更を選ぶと、候補キーを
        // 証明できない付け直し（候補キーの警告は出る）が断られ（MySQL「Missing unique key for constraint」・Oracle ORA-02270）、
        // DDL が暗黙コミットされるため、同じ計画で先に外していた、警告を出していない制約が巻き添えで消えたまま残る（消えるのは
        // 警告を出した外部キー自身ではない）。方言を問わない 1 つの項目にする（修正 A で直すと MySQL・Oracle が同時に「起きない」になる）
        // - 実 DB の夜間の MySQL の種 1191：参照される表の主キーを外す（remove pk）→ 外し直した外部キーの付け直しが失敗 →
        //   選んでいない外部キーが消える
        // - 実 DB の夜間の MySQL の種 358（P2-2 で表明 1b を強めて見つけた・選んだ作り直しの版）：主キーの付け替え 2 つ（move pk・
        //   replace pk）と、自己参照の外部キー・別の外部キーの ON DELETE の作り直しを選ぶ → FK_T1_notes_t0_items の付け直しが失敗 →
        //   先に外した、選んだ自己参照の作り直し（fk_t2_groups_t2_groups）が消える
        // - 能力の組の網の Oracle の種 1551・1570・2660・3017・3279・3656・4094（P2-4）：同じ形の Oracle 版（実 DB の Oracle の
        //   夜間 120 種では出ていないので、実 DB は行 oracle-358-selected-rebuild で確かめる）
        .. Entry(
            "candidate-key-re-add-collateral",
            [DialectNames.MySql],
            "修正 A",
            IsCandidateKeyReAddCollateral,
            KnownReproduction.NightlySeeds
        ),
        .. Scenario(
            "candidate-key-re-add-collateral",
            DialectNames.MySql,
            "修正 A",
            ["mysql-1191-remove-pk", "mysql-358-selected-rebuild"],
            IsCandidateKeyReAddCollateral
        ),
        .. Scenario(
            "candidate-key-re-add-collateral",
            DialectNames.Oracle,
            "修正 A",
            ["oracle-358-selected-rebuild"],
            IsCandidateKeyReAddCollateral
        ),
        // 4m-P6（P2-2 で表明 1b を強めて既定の種で見つけた）。計画が列の変更の失敗を前もって分かっている
        // （ForeignKeyColumnTypeMismatch＝doomed）のに、選んだ作り直しの DROP が失敗より前に走り、付け直しは失敗の後ろにあって
        // 走らないので、DDL が暗黙コミットされる MySQL で制約が消えたまま残る。doomed の扱いは暗黙の外し直しだけを止め、選んだ
        // 作り直し・主キーの変更を止めない（同じ根）。修正 A の受け入れ条件は「計画が doomed を判定したとき、選んだ作り直しと主キーの
        // 変更（外部キー・一意制約・主キー）の DROP を、失敗より前に置かない」。規則 2（作り直し）は普段の種で、規則 3（主キーの変更）は
        // 夜間の種でだけ出る
        // - 実 DB の種 44（4m-P6 そのもの）：自己参照の外部キーの ON DELETE の作り直し（AlterForeignKey）・参照される表の主キーの解除・
        //   別の表の列の型の変更を選ぶ → 型の食い違いで外部キーを外し直さないまま主キーの解除が
        //   「Cannot drop index 'PRIMARY': needed in a foreign key constraint」で失敗 → 作り直した外部キーが消える
        // - 実 DB の種 19（一意制約の作り直し版）：一意制約の構成列の変更（AlterUniqueConstraint）・参照される主キー列と別の表の列の型の変更を
        //   選ぶ → 外部キーの両端の型が incompatible で列の変更が失敗 → 作り直した一意制約が消える
        // - 実 DB の種 107（一意制約の作り直し版）：主キーの付け替え・外部キーの片側の列の型の変更・一意制約の構成列の変更を選ぶ →
        //   外部キーの両端の型が incompatible で失敗 → 作り直した一意制約が消える
        // 規則 3（主キーの変更の版・夜間の種だけ）：
        // - 実 DB の種 447：複合主キーへ列を足す（id,k → id,k,n0）を選ぶ → 自己参照の外部キーの両端が incompatible で列の変更が失敗 →
        //   外した主キーが付かずに消える
        // - 実 DB の種 463：主キーへ列を足す（code → code,n1）と外部キーの削除を選ぶ → 自己参照の外部キーが incompatible で失敗 →
        //   主キーが消える
        // - 実 DB の種 797：複合主キーを縮める（id,k → id）と外部キーの削除を選ぶ → 別の外部キーが incompatible で失敗 → 主キーが消える
        // - 実 DB の種 1355：主キーへ列を足す（code → code,n1）・一意制約の構成列の変更・外部キーの削除を選ぶ → 外部キーが incompatible で
        //   失敗 → 主キーと作り直した一意制約の両方が消える
        // P2-4 で生成器を変えて種の指紋が変わり（型の種類・参照アクション・一意制約の形を足した）、上の種の番号は別の形になった。
        // 普段の 150 種にはこの形が出なくなったので、乱数の項目は夜間の種で数え、普段の CI は決定的な行（p6-*）が担う
        .. Entry(
            "4m-p6-doomed-rebuild",
            [DialectNames.MySql],
            "修正 A",
            IsDoomedRebuild,
            KnownReproduction.NightlySeeds
        ),
        .. Scenario(
            "4m-p6-doomed-rebuild",
            DialectNames.MySql,
            "修正 A",
            [
                "p6-alter-fk",
                "p6-alter-unique",
                "p6-alter-unique-self-reference",
                "p6-pk-extend",
                "p6-pk-shrink",
                "p6-drop-before-failure-rule4",
            ],
            IsDoomedRebuild
        ),
        // 4m-P1（Oracle・修正 A・P2-4 の行）：主キーの変更（候補キーを保つ）と兄弟の外部キーの片側だけの型の変更で、計画は型の
        // 食い違う外部キーだけを残し兄弟の外部キーを外す → 主キーの解除が ORA-02273 で失敗し、外した兄弟が消えたまま残る
        .. Scenario(
            "4m-p1",
            DialectNames.Oracle,
            "修正 A",
            // 形の行（D4・夜間の実行で流れる）：主キーの並べ替え × 参照 2 本 × 子だけの型の変更 × 一意制約の削除
            ["4m-p1-sibling-foreign-key", SyncPlanShapeRows.IdOf(DialectNames.Oracle, 51)],
            IsSiblingForeignKeyLost
        ),
        // 4m-Q6 の乱数の項目（P2-4 の生成器の「同じ列の並びの一意制約 2 つの片方の削除と型の変更」で、実 DB の夜間の SQL Server の
        // 種 152・316・448・854・909 で出る。普段の 100 種には出ないので夜間の種で数え、普段の CI は行が担う）
        .. Entry(
            "4m-q6",
            [DialectNames.SqlServer],
            "修正 A",
            IsUnwarnedAlterBlockedByUnique,
            KnownReproduction.NightlySeeds
        ),
        // 4m-Q6（SQL Server・修正 A・P2-4 の行）：同じ列集合の一意制約 2 つの片方の削除と列の型の変更で、暗黙の外し直しの照合が
        // 表＋列集合だけなので、もう片方を外さず Msg 5074（警告なし・トランザクションで巻き戻る）
        .. Scenario(
            "4m-q6",
            DialectNames.SqlServer,
            "修正 A",
            // 形の行（D4）：一意制約 2 つ × 一意制約の削除 × 一意制約の列の型の変更。23（主キーの並べ替え × 参照 2 本）は普段も、
            // 46（主キーの並べ替え × 参照 0 本）は夜間の実行でだけ流れる
            [
                "4m-q6-duplicate-unique",
                SyncPlanShapeRows.IdOf(DialectNames.SqlServer, 23),
                SyncPlanShapeRows.IdOf(DialectNames.SqlServer, 46),
            ],
            IsUnwarnedAlterBlockedByUnique
        ),
        // mysql-temporal-zero-precision-respelling（MySQL・修正 E・P2-4）：小数秒の精度 0 を明示した綴り（datetime(0)・time(0)・
        // timestamp(0)）を MySqlTypeNotation.Normalize が素の綴りへ寄せないので、取り込んだ綴り（datetime・time・timestamp）と
        // 食い違う AlterColumn が出て、同期しても消えない（CLAUDE.md の取込の節 (12) の型の欠陥。表明 2「選んだ項目が残る」）
        // - 実 DB の種 77：日時の列の型を datetime から datetime(0) へ書き換える（respell type）
        .. Entry(
            "mysql-temporal-zero-precision-respelling",
            [DialectNames.MySql],
            "修正 E",
            IsZeroPrecisionRespelling,
            KnownReproduction.RegularSeeds
        ),
        .. Scenario(
            "mysql-temporal-zero-precision-respelling",
            DialectNames.MySql,
            "修正 E",
            ["mysql-77-respell-datetime"],
            IsZeroPrecisionRespelling
        ),
        // sqlite-rebuild-replace-all-columns（SQLite・修正 B・P2-4）：表の元の列をすべて消して（主キーの解除と列の削除）新しい列を
        // 足すと、再構築に写す列が無く INSERT … SELECT が出ず、行がすべて消える（警告なし・コミット。逐次 DDL の方言では行は残り、
        // 新しい列は NULL になる）。表明 5（行数）の「row count changed」で出る。nightly-sqlite-empty-rebuild の隣の形
        // - 実 DB の種 795：主キーの解除・一意制約の削除・列 2 本の削除と列 2 本の追加を選ぶ → 行が 3 → 0
        .. Entry(
            "sqlite-rebuild-replace-all-columns",
            [DialectNames.Sqlite],
            "修正 B",
            IsRebuildReplacingAllColumns,
            KnownReproduction.RegularSeeds
        ),
        .. Scenario(
            "sqlite-rebuild-replace-all-columns",
            DialectNames.Sqlite,
            "修正 B",
            ["sqlite-795-replace-all-columns"],
            IsRebuildReplacingAllColumns
        ),
        // warned-removal-not-named（旧名 nightly-postgresql-512・修正 A）：計画が警告（ForeignKeyColumnTypeMismatch・
        // SetNullOnNotNullColumn）を出して選んだ外部キーの追加・作り直しを外すが、外した項目を SelectedItemRequiresUnselectedItem で
        // 名指ししないので、選んだ項目が同期の後も残る（直す場所は 1 つ＝計画が警告で外した選んだ項目を名指しする箇所）
        // - 実 DB の夜間の PostgreSQL の種 512：自己参照の外部キーの参照アクションの変更と、参照先の主キー列の型の変更を一緒に選ぶ
        // - 実 DB の夜間の SQL Server の種 753（P2-4）：親の列の型を変えずに、子に型の違う列と外部キーを足す（ForeignKeyColumnTypeMismatch）
        // - 実 DB の夜間の MySQL の種 997（P2-4）：NOT NULL の子の列の外部キーを SET NULL へ作り直し、NULL 許容化は選ばない
        //   （SetNullOnNotNullColumn）
        .. Entry(
            "warned-removal-not-named",
            [DialectNames.PostgreSql, DialectNames.SqlServer, DialectNames.MySql],
            "修正 A",
            IsWarnedRemovalNotNamed,
            KnownReproduction.NightlySeeds
        ),
        .. Scenario(
            "warned-removal-not-named",
            DialectNames.PostgreSql,
            "修正 A",
            ["postgresql-512-self-reference"],
            IsWarnedRemovalNotNamed
        ),
        .. Scenario(
            "warned-removal-not-named",
            DialectNames.SqlServer,
            "修正 A",
            ["sqlserver-753-warned-type-mismatch"],
            IsWarnedRemovalNotNamed
        ),
        .. Scenario(
            "warned-removal-not-named",
            DialectNames.MySql,
            "修正 A",
            ["mysql-997-warned-set-null"],
            IsWarnedRemovalNotNamed
        ),
        // synthesized-unique-name-collision（修正 E・P2-4）：名前付きの一意制約の列を変え（名前は保つ）、元の列に名前の無い一意制約を
        // 足すと、名前の無い一意制約の合成名（UniqueConstraint.SynthesizeName＝UQ_{表}_{列}）が既存の名前と同じになり、MySQL
        // 「Duplicate key name」・PostgreSQL 42P07 で失敗する（警告なし。外部キーの既定名は UniqueForeignKey で一意化するが、一意制約の
        // 合成名は一意化しない）
        // - 実 DB の夜間の MySQL・PostgreSQL の種 386
        .. Entry(
            "synthesized-unique-name-collision",
            [DialectNames.MySql, DialectNames.PostgreSql],
            "修正 E",
            IsSynthesizedUniqueNameCollision,
            KnownReproduction.NightlySeeds
        ),
        .. Scenario(
            "synthesized-unique-name-collision",
            DialectNames.MySql,
            "修正 E",
            ["mysql-386-synthesized-unique-name"],
            IsSynthesizedUniqueNameCollision
        ),
        .. Scenario(
            "synthesized-unique-name-collision",
            DialectNames.PostgreSql,
            "修正 E",
            ["postgresql-386-synthesized-unique-name"],
            IsSynthesizedUniqueNameCollision
        ),
        // 4m-f1-legacy-fk-name（4m-F1・修正 E・P2-6）：旧版（v0.1.0〜v0.2.0）の DDL で作った PostgreSQL の DB（名前の無い外部キーの
        // 縮める前の既定名を PostgreSQL が 63 バイトで切り詰めた名前）に、同じ子の表から名前の無い外部キーを図で 1 本足すと、差分の
        // 吸収が 2 本目の追加にも DB にある 1 本目と同じ切り詰めた名前を与え、既定で選ばれる追加が 42710 で断られる（警告なし）。
        // 行は来歴の行（SyncPlanHistoryRows）の「旧版 × 長い子の表名 × 2 本目を足す」
        .. Scenario(
            SyncPlanHistoryRows.F1Finding,
            DialectNames.PostgreSql,
            "修正 E",
            [SyncPlanHistoryRows.F1Id],
            IsLegacyForeignKeyNameCollision
        ),
        // 夜間の実行（Oracle の実 DB の種 100・111）で見つけた。DB にある一意制約と同じ列へ主キーを付け替える変更（AlterPrimaryKey）は
        // ORA-02261 で失敗するのに、計画が警告を出さない（CLAUDE.md の既知の残件を、警告なしの失敗として名指しする）。
        // 主キーの DROP が成功した後で ADD が失敗し、Oracle の DDL は暗黙コミットなので主キーが消えたまま残る。P2-2 で強めた
        // 表明 1b の規則 3（主キーの変更を選んだ表の主キーは旧か新の形で残る）がそれを名指しするので、その表の規則 3 の違反も
        // この項目に含める（両方がそろっていることを求める＝夜間の実行で規則 3 を戻すと「起きない」で赤になる。ほかの表の規則 3 が
        // 混ざったら当たらない）。P2-4 で生成器を変えて種の指紋が変わり、夜間の種で出なくなったので、乱数の項目を消して行が担う
        .. Scenario(
            "nightly-oracle-100-111",
            DialectNames.Oracle,
            "P2",
            ["oracle-100-111-move-pk-onto-unique"],
            IsPrimaryKeyOntoUnique
        ),
        // 夜間の実行（SQLite の実 DB の種 2301・2667・7088・7411・7898・15266・18203）で見つけた。表の列をすべて消す変更（主キーの解除と
        // 列の削除）を選ぶと、再構築の合成で列が 0 本の表になり、`CREATE TABLE "…_quicker_rebuild" ();` が構文エラーで失敗する。
        // 計画は警告も拒否もしない（利用者の図でも起き得る製品の欠け）
        .. Entry(
            "nightly-sqlite-empty-rebuild",
            [DialectNames.Sqlite],
            "P2",
            IsEmptyRebuild,
            KnownReproduction.NightlySeeds
        ),
        .. Scenario(
            "nightly-sqlite-empty-rebuild",
            DialectNames.Sqlite,
            "P2",
            ["sqlite-empty-rebuild"],
            IsEmptyRebuild
        ),
        // nightly-sqlite-pk-nonunique（受け入れ・P2-4 で切り分けた）：値が一意でない列への主キーの付け替えは、DB がデータで断る
        // （SQLite は再構築のデータの移送で UNIQUE constraint failed になり、トランザクションを巻き戻して何も変えない）。計画は列の値を
        // 知らないので警告できない（DB の事実）。乱数の種で出ていた形（旧い実 DB の種 12503・18316、P2-4 の夜間の SQLite の種 5631・
        // 8979・18819、MySQL の種 861）は、主キーを外した・縮めた・列を消した後で一意でない列を主キーへ足す生成器の穴だったので、
        // 生成器を直して乱数の項目を消した。行 sqlite-pk-nonunique は、巻き戻して何も変えない振る舞いを残すために受け入れで持つ
        .. Scenario(
            "nightly-sqlite-pk-nonunique",
            DialectNames.Sqlite,
            "P2",
            ["sqlite-pk-nonunique"],
            IsNonUniquePrimaryKey,
            acceptedReason: "値が一意でない列への主キーは DB がデータで断る（SQLite は再構築を巻き戻して何も変えない）。計画は列の値を知らないので警告できない（DB の事実）"
        ),
        // 夜間の実行（SQLite の実 DB の種 16938）で見つけた。外部キーの付け替え先が選んでいない新しい表（AddTable）のとき、計画は
        // SelectedItemRequiresUnselectedItem で名指しするのに、同期の後に作り直しの追加の側が「選んだのに残った」として数えられる。
        // ランナーの数え方（作り直しの展開後の対応づけ）の前提か、計画の名指しの欠けかは P2 で切り分ける。P2-4 で種の指紋が変わり
        // 夜間の種で出なくなったので、乱数の項目を消して行が担う
        .. Scenario(
            "nightly-sqlite-alterfk-unselected-target",
            DialectNames.Sqlite,
            "P2",
            ["sqlite-alterfk-unselected-target"],
            IsAlterForeignKeyOntoUnselectedTable
        ),
        // 夜間の実行（SQLite の実 DB の種 17592）で見つけた。同じ列ペアの外部キー 2 本の参照アクションをどちらも変え、片方の作り直し
        // （AlterForeignKey）だけを選ぶと、再構築が選ばなかったほうまで作り直す。P2-4 で種の指紋が変わり夜間の種で出なくなったので、
        // 乱数の項目を消して行が担う
        .. Scenario(
            "nightly-sqlite-twin-fk-rebuild",
            DialectNames.Sqlite,
            "P2",
            ["sqlite-twin-fk-rebuild"],
            IsTwinForeignKeyRebuild
        ),
    ];

    /// <summary>4m-p1：型の食い違う外部キーを残して主キーを外す文が ORA-02273 で失敗し、先に外した兄弟の外部キーが消える</summary>
    private static bool IsSiblingForeignKeyLost(FuzzSeedOutcome o) =>
        OnlyViolations(o, SyncPlanFuzzRunner.ConstraintLostPrefix)
        && o.Error?.Contains("ORA-02273", StringComparison.Ordinal) == true
        && o.Warnings.Any(w =>
            w.StartsWith("ForeignKeyColumnTypeMismatch", StringComparison.Ordinal)
        );

    /// <summary>4m-q6：同じ列集合の一意制約のもう片方が残ったまま列の型を変え、警告なしに Msg 5074（「The object … is dependent on column …」）で失敗する</summary>
    private static bool IsUnwarnedAlterBlockedByUnique(FuzzSeedOutcome o) =>
        OnlyViolations(o, "failed without a warning:")
        && o.Error?.Contains("is dependent on column", StringComparison.Ordinal) == true;

    /// <summary>
    /// candidate-key-re-add-collateral：候補キーの付け直しの失敗で、先に外した制約が消える（付け直しを断るエラー文は方言ごとに
    /// 結び付けて見る＝ほかの方言の文で当たらない）
    /// </summary>
    private static bool IsCandidateKeyReAddCollateral(FuzzSeedOutcome o) =>
        OnlyViolations(o, SyncPlanFuzzRunner.ConstraintLostPrefix)
        && CandidateKeyRefusal(o.Dialect) is { } refusal
        && o.Error?.Contains(refusal, StringComparison.Ordinal) == true
        && o.Warnings.Any(w =>
            w.StartsWith("ForeignKeyRebuildMayLoseCandidateKey", StringComparison.Ordinal)
            || w.StartsWith("ForeignKeyNotOnCandidateKey", StringComparison.Ordinal)
        );

    /// <summary>候補キーの無い外部キーの付け直しを DB が断るエラー文（DDL が暗黙コミットされる方言だけ・ほかは null）</summary>
    private static string? CandidateKeyRefusal(string dialect) =>
        dialect switch
        {
            DialectNames.MySql => "Missing unique key",
            DialectNames.Oracle => "ORA-02270",
            _ => null,
        };

    /// <summary>4m-p6-doomed-rebuild：失敗が決まった文より前に、選んだ作り直し・主キーの変更の DROP が走る</summary>
    private static bool IsDoomedRebuild(FuzzSeedOutcome o) =>
        OnlyViolations(o, SyncPlanFuzzRunner.ConstraintLostPrefix)
        && o.Violations.All(v =>
            (
                v.Contains("(rule 2;", StringComparison.Ordinal)
                || v.Contains("(rule 3;", StringComparison.Ordinal)
                // 規則 4（P2-4）：後ろの列の削除が理由の DROP が、失敗の文より前に走る
                || v.Contains("(rule 4;", StringComparison.Ordinal)
            ) && v.Contains("dropped-before-failure=yes", StringComparison.Ordinal)
        )
        && o.Warnings.Any(w =>
            w.StartsWith("ForeignKeyColumnTypeMismatch", StringComparison.Ordinal)
        );

    /// <summary>計画が選んだ項目を外すときに出す警告のうち、この族の 2 種</summary>
    private static readonly string[] RemovalWarningKinds =
    [
        "ForeignKeyColumnTypeMismatch",
        "SetNullOnNotNullColumn",
    ];

    /// <summary>
    /// warned-removal-not-named：警告（2 種）で外した選んだ外部キーが名指しされずに残る。残った項目の子の表ごとに、その表を名指しした
    /// 2 種のどれかの警告がある（無関係な表の警告では当たらない）
    /// </summary>
    private static bool IsWarnedRemovalNotNamed(FuzzSeedOutcome o) =>
        o.Committed
        && OnlyViolations(o, "selected item remains: FK")
        && o.Violations.All(v =>
        {
            // 「selected item remains: FK+|子の表|親の表|…」の子の表
            var table = v.Split('|')[1];
            return o.Warnings.Any(w =>
                RemovalWarningKinds.Any(kind =>
                    string.Equals(w, kind + "(" + table + ")", StringComparison.OrdinalIgnoreCase)
                )
            );
        });

    /// <summary>synthesized-unique-name-collision：名前の無い一意制約の合成名が既存の一意制約の名前と衝突して失敗する</summary>
    private static bool IsSynthesizedUniqueNameCollision(FuzzSeedOutcome o) =>
        !o.Committed
        && o.Violations.Count == 1
        && o.Violations[0].StartsWith("failed without a warning:", StringComparison.Ordinal)
        && (
            o.Violations[0].Contains("Duplicate key name", StringComparison.Ordinal)
            || o.Violations[0].Contains("42P07", StringComparison.Ordinal)
        )
        && o.Mutations.Any(m => m.StartsWith("change unique ", StringComparison.Ordinal))
        && o.Mutations.Any(m => m.StartsWith("add unique ", StringComparison.Ordinal));

    /// <summary>
    /// 4m-f1-legacy-fk-name：旧版の名前の吸収が 2 本目の追加に DB の外部キーと同じ名前を与え、追加が 42710 で失敗する（静的な規則 R4 も
    /// 予言する）
    /// </summary>
    private static bool IsLegacyForeignKeyNameCollision(FuzzSeedOutcome o) =>
        !o.Committed
        && o.Violations.Count == 1
        && o.Violations[0].StartsWith("failed without a warning:", StringComparison.Ordinal)
        && o.Violations[0].Contains("42710", StringComparison.Ordinal)
        && o.Notes.Contains(SyncPlanFuzzRunner.StaticRulePredictedNote)
        && o.Mutations.Any(m => m.StartsWith("add fk ", StringComparison.Ordinal));

    /// <summary>nightly-oracle-100-111：一意制約のある列への主キーの付け替えが ORA-02261 で失敗し主キーが消える</summary>
    private static bool IsPrimaryKeyOntoUnique(FuzzSeedOutcome o) =>
        o.Violations.Any(v =>
            v.StartsWith("failed without a warning: ORA-02261", StringComparison.Ordinal)
        )
        && o.Violations.Any(v => IsPrimaryKeyLossOfAlteredTable(o, v))
        && o.Violations.All(v =>
            v.StartsWith("failed without a warning: ORA-02261", StringComparison.Ordinal)
            || IsPrimaryKeyLossOfAlteredTable(o, v)
        )
        && o.Selected.Any(s => s.StartsWith("AlterPrimaryKey", StringComparison.Ordinal));

    /// <summary>mysql-temporal-zero-precision-respelling：精度 0 を明示した日時の綴りの AlterColumn が同期しても残る</summary>
    private static bool IsZeroPrecisionRespelling(FuzzSeedOutcome o) =>
        o.Committed
        && OnlyViolations(o, "selected item remains: AlterColumn")
        // 選んだ項目の説明は UI の言語で変わるので、変更の記録（言語に依らない）で見る
        && o.Mutations.Any(m =>
            m.StartsWith("respell type ", StringComparison.Ordinal)
            && m.EndsWith("(0)", StringComparison.Ordinal)
        );

    /// <summary>sqlite-rebuild-replace-all-columns：警告なしのコミットで行数が減る（再構築が行を写さない）</summary>
    private static bool IsRebuildReplacingAllColumns(FuzzSeedOutcome o) =>
        o.Committed
        && o.Warnings.Count == 0
        && o.Violations.Count > 0
        && o.Violations.All(v =>
            v.StartsWith("row count changed:", StringComparison.Ordinal)
            && v.EndsWith("-> 0", StringComparison.Ordinal)
        );

    /// <summary>nightly-sqlite-empty-rebuild：列が 0 本の再構築の表が構文エラー</summary>
    private static bool IsEmptyRebuild(FuzzSeedOutcome o) =>
        OnlyViolations(o, "failed without a warning: SQLite Error 1: 'near \")\": syntax error'")
        && EmptyRebuildTable().IsMatch(o.Script);

    /// <summary>nightly-sqlite-pk-nonunique：一意でない列への主キーの付け替えが UNIQUE 違反</summary>
    private static bool IsNonUniquePrimaryKey(FuzzSeedOutcome o) =>
        OnlyViolations(o, "failed without a warning: SQLite Error 19: 'UNIQUE constraint failed:")
        && o.Selected.Any(s => s.StartsWith("AlterPrimaryKey", StringComparison.Ordinal));

    /// <summary>nightly-sqlite-alterfk-unselected-target：選んでいない新しい表への付け替えを「選んだのに残った」と数える</summary>
    private static bool IsAlterForeignKeyOntoUnselectedTable(FuzzSeedOutcome o) =>
        o.Committed
        && OnlyViolations(o, "selected item remains: FK+")
        && o.Warnings.Any(w =>
            w.StartsWith("SelectedItemRequiresUnselectedItem", StringComparison.Ordinal)
        )
        && o.Unselected.Any(s => s.StartsWith("AddTable", StringComparison.Ordinal));

    /// <summary>nightly-sqlite-twin-fk-rebuild：同じ列ペアの外部キー 2 本の片方の作り直しで、もう片方も作り直す</summary>
    private static bool IsTwinForeignKeyRebuild(FuzzSeedOutcome o) =>
        o.Committed
        && OnlyViolations(o, "unselected item was applied: FK")
        && o.Selected.Any(s => s.StartsWith("AlterForeignKey", StringComparison.Ordinal))
        && o.Unselected.Any(s => s.StartsWith("AlterForeignKey", StringComparison.Ordinal));

    /// <summary>列が 0 本の再構築の表の作成文</summary>
    [GeneratedRegex(@"_quicker_rebuild"" \(\s*\);")]
    private static partial Regex EmptyRebuildTable();

    /// <summary>選んだ AlterPrimaryKey の説明から表名を取り出す（例「AlterPrimaryKey: …: [t3_notes] (code) → …」）</summary>
    [GeneratedRegex(@"^AlterPrimaryKey: [^\[]*\[([^\]]+)\]", RegexOptions.CultureInvariant)]
    private static partial Regex AlteredPrimaryKeyTable();

    /// <summary>違反が、AlterPrimaryKey を選んだ表の主キーの表明 1b・規則 3 の消失か</summary>
    private static bool IsPrimaryKeyLossOfAlteredTable(FuzzSeedOutcome outcome, string violation)
    {
        if (
            !violation.StartsWith(SyncPlanFuzzRunner.ConstraintLostPrefix, StringComparison.Ordinal)
            || !violation.Contains("(rule 3;", StringComparison.Ordinal)
        )
        {
            return false;
        }

        var key = violation[SyncPlanFuzzRunner.ConstraintLostPrefix.Length..].TrimStart();
        var parts = key.Split('|');

        return parts.Length >= 2
            && parts[0] == "PK"
            && outcome
                .Selected.Select(s => AlteredPrimaryKeyTable().Match(s))
                .Any(m =>
                    m.Success
                    && string.Equals(
                        m.Groups[1].Value.Trim(),
                        parts[1],
                        StringComparison.OrdinalIgnoreCase
                    )
                );
    }

    /// <summary>一覧</summary>
    public static KnownFailureSet Set { get; } = new("SyncPlan", Entries);

    /// <summary>項目を作る（方言ごとに 1 項目）</summary>
    /// <param name="finding">指摘の名前</param>
    /// <param name="dialects">起きる方言（<see cref="SyncPlanFuzzDialect.Name"/>）</param>
    /// <param name="fixedIn">一覧から外す区切り</param>
    /// <param name="matches">失敗した種がこの項目の形か（種の番号でなく、変更と失敗の中身で判定する）</param>
    /// <param name="reproduction">どの既定の種の数で流せば必ず起きるか（「起きない」を数えるのはそのときだけ）</param>
    public static IEnumerable<KnownEntry> Entry(
        string finding,
        IReadOnlyList<string> dialects,
        string fixedIn,
        Func<FuzzSeedOutcome, bool> matches,
        KnownReproduction reproduction
    ) =>
        dialects.Select(dialect => new KnownEntry(
            finding,
            KnownKind.Pending,
            fixedIn,
            null,
            dialect,
            KnownMatch.Predicate,
            Predicate: failure => failure.Source is FuzzSeedOutcome outcome && matches(outcome),
            RandomSeeds: true,
            Reproduction: reproduction
        ));

    /// <summary>
    /// 決定的な行（<see cref="SyncPlanScenarios"/>）の項目を作る（行ごとに 1 項目・範囲は方言名＋
    /// <see cref="SyncPlanScenarios.ScopeSuffix"/>・どの種の数で流しても「起きない」を数える）
    /// </summary>
    /// <param name="finding">指摘の名前（乱数の項目と同じ）</param>
    /// <param name="dialect">行の方言</param>
    /// <param name="fixedIn">一覧から外す区切り</param>
    /// <param name="scenarioIds">行の Id（表に無い Id は断る）</param>
    /// <param name="matches">失敗した行がこの項目の形か（乱数の項目と同じ述語）</param>
    public static IEnumerable<KnownEntry> Scenario(
        string finding,
        string dialect,
        string fixedIn,
        IReadOnlyList<string> scenarioIds,
        Func<FuzzSeedOutcome, bool> matches,
        string? acceptedReason = null
    ) =>
        scenarioIds.Select(id =>
        {
            var row = SyncPlanScenarios.Table[id];

            if (!string.Equals(row.Input.Dialect, dialect, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"行 {id} の方言は {row.Input.Dialect}（項目は {dialect}）"
                );
            }

            return new KnownEntry(
                finding,
                acceptedReason is null ? KnownKind.Pending : KnownKind.Accepted,
                acceptedReason is null ? fixedIn : null,
                acceptedReason,
                SyncPlanScenarios.ScopeOf(dialect, id),
                KnownMatch.Predicate,
                Predicate: failure =>
                    failure.Source is FuzzSeedOutcome outcome
                    && string.Equals(outcome.ScenarioId, id, StringComparison.Ordinal)
                    && matches(outcome)
            );
        });

    /// <summary>違反が 1 つ以上あり、どれも指定の文で始まるか</summary>
    private static bool OnlyViolations(FuzzSeedOutcome outcome, string prefix) =>
        outcome.Violations.Count > 0
        && outcome.Violations.All(v => v.StartsWith(prefix, StringComparison.Ordinal));

    /// <summary>
    /// 違反のあった種か行を、照合に渡す失敗にする（範囲＝方言、行なら方言＋<see cref="SyncPlanScenarios.ScopeSuffix"/>・
    /// 行は種を持たない・元の結果を持つ）
    /// </summary>
    /// <param name="outcome">違反のあった種か行の結果</param>
    public static EngineFailure ToFailure(FuzzSeedOutcome outcome) =>
        new(
            outcome.ScenarioId is null
                ? outcome.Dialect
                : SyncPlanScenarios.ScopeOf(outcome.Dialect, outcome.ScenarioId),
            outcome.Violations.FirstOrDefault() ?? "",
            outcome.Describe(),
            outcome.ScenarioId is null ? outcome.Seed : null,
            outcome
        );
}
