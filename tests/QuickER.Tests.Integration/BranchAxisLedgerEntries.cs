using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration;

/// <summary>
/// 分岐の軸の台帳と領域の標準の軸の一覧（宣言だけ。照合は <see cref="BranchAxisLedgerChecks"/> と両アセンブリの台帳のテスト）
/// </summary>
/// <remarks>
/// <para>
/// 直し残しの指摘 1 つを 1 項目にし、受け入れを「行 1 本」でなく「生成の表の入力の水準＋領域の標準の軸への答え」で持つ
/// （tests/QuickER.Tests/TestSupport/CLAUDE.md「分岐の軸の台帳と新しい行の札」）。
/// </para>
/// <para>
/// 純粋なデータ（Engines の型と BCL だけを参照する）。QuickER.Tests へ逆向きに Compile のリンクで入り、結び済みの判定役の実在を
/// CLAUDE.md の照合と同じ関数で見る（作らない形の宣言と同じ形）。このファイルは CLAUDE.md の照合の行 Id の本文から名前で外す。
/// </para>
/// </remarks>
internal static class BranchAxisLedgerEntries
{
    /// <summary>領域の標準の軸の一覧の Id：値の束縛</summary>
    public const string ValueBinding = "value-binding";

    /// <summary>領域の標準の軸の一覧の Id：同期の計画</summary>
    public const string SyncPlan = "sync-plan";

    /// <summary>領域の標準の軸の一覧</summary>
    public static IReadOnlyList<StandardAxisArea> Areas { get; } =
    [
        new(
            ValueBinding,
            "値の束縛（式の翻訳・パラメータの型・インターセプター）",
            [
                new(
                    "column-type",
                    "列の型",
                    "直した分岐が見る列の型の隣の型（date と datetime・整数の幅・decimal と money・文字列の Unicode）は何か"
                ),
                new(
                    "value-pool",
                    "値の池",
                    "値の端の水準（DateTimeKind・端数・刻みの外・範囲の端・ASCII の外）のどれで答えが変わるか"
                ),
                new(
                    "value-count",
                    "値の数",
                    "単一の値とリスト（IN・NOT IN・配列のパラメータ）で同じ分岐を通るか"
                ),
                new(
                    "value-shape",
                    "値の形",
                    "比べる値の式の形（定数・捕捉した変数・メソッド呼び出し・ラムダを含む式・型変換）で同じ分岐を通るか"
                ),
                new(
                    "column-shape",
                    "列の側の形",
                    "列の側の式の形（素の列・日付部品・型変換・値オブジェクト・ナビゲーション）で同じ分岐を通るか"
                ),
                new(
                    "comparison",
                    "比べ方",
                    "等値・順序・IN・否定・文字列一致のどれで答えが変わるか"
                ),
                new(
                    "query-path",
                    "クエリの経路",
                    "LinqQuery・ExecuteDelete・ExecuteUpdate・SaveChanges のどの経路で同じ値が束縛されるか"
                ),
                new(
                    "backend",
                    "実装先",
                    "QuickER 版・EF Core 版の 5 方言・インメモリ・リモートのどれが同じ分岐を持つか"
                ),
            ]
        ),
        new(
            SyncPlan,
            "同期の計画（差分・選択・組み立て・評価）",
            [
                new(
                    "dialect-declaration",
                    "方言の宣言",
                    "直した分岐が読む方言の宣言ごとに、真の方言と偽の方言で答えが変わる水準はあるか",
                    RequiresDeclarationCheck: true
                ),
                new(
                    "step-origin",
                    "文の由来",
                    "選んだ項目・暗黙の外し直しと付け直し・再構築のどの由来の文で同じ判定が要るか"
                ),
                new("data", "データ", "行あり・NULL・重複・親の無い値のどれで文が断られうるか"),
                new(
                    "referential-action",
                    "参照アクション",
                    "外部キーの ON DELETE / ON UPDATE（NO ACTION・CASCADE・SET NULL・SET DEFAULT）で判定が変わるか"
                ),
                new(
                    "key-kind",
                    "キーの種類",
                    "参照先・変えるキーが主キーか一意制約かで判定が変わるか"
                ),
                new(
                    "column-change",
                    "列の変更の種類",
                    "型の種類の変更・文字列の長さだけ・整数の幅・精度とスケール・NULL 許容だけのどれで判定が変わるか"
                ),
            ]
        ),
    ];

    /// <summary>台帳の項目</summary>
    public static IReadOnlyList<BranchAxisItem> Items { get; } =
    [
        // 修正 AH（f6fa8c66）の前寄せ：外部キーの両端を別々の型へ変え（子は互換の範囲で広げ、親は互換でない型へ）、表 d の外部キーを
        // 作り直す。同期後の両端で「必ず断られる」と判定して、先頭の位置では通る子の変更を前寄せしていた（P5-2 の本番 PA1・Oracle）。
        // 配線の確かめのための結び済み・修正済みの項目（MySQL の隣の枝は PE1）
        new(
            "ah-move-first",
            "前寄せが先頭の位置で通る子の変更を移し、断られる親の変更を元の位置に残して、先に外した外部キーが消えたまま残った（修正 AH・Oracle）",
            SyncPlan,
            BranchAxisStatus.BoundFixed,
            Plan: null,
            new BranchBinding(
                "SyncPlan/ShapeRows",
                [
                    new("TypeChange", "DivergentEnds"),
                    new("ReferencingFks", "1"),
                    new("CollateralFk", "AlterOtherForeignKey"),
                ],
                "ah-move-first-divergent-ends",
                ["oracle"],
                "moved-first-plan",
                []
            ),
            "`SyncPlan/ShapeRows`（静的な規則 M1）",
            [
                AxisAnswer.Cross(
                    "dialect-declaration",
                    "暗黙コミットの方言（Oracle・MySQL）だけが前寄せし、外し直さない Oracle は両端の型で判定する",
                    "SyncDialectCapabilities.TransactionalDdl：偽の方言（MySQL・Oracle）だけが前寄せを持ち、真の方言の計画に前寄せは無い",
                    "SyncDialectCapabilities.AlterColumnRequiresForeignKeyRebuild：偽の Oracle は外部キーを外さずに列を変えるので両端の型で判定し、真の MySQL は外し直すので判定が変わる（MySQL の文字列の長さだけの変更は項目 pe1）"
                ),
                AxisAnswer.Cross(
                    "step-origin",
                    "選んだ列の変更 × 暗黙の外部キーの作り直し（表 d の外部キー）"
                ),
                AxisAnswer.Irrelevant(
                    "data",
                    "前寄せの型の判定は列の型だけを読み行の値を読まない（既存の行で断られる文は項目 pa1 の領域）"
                ),
                AxisAnswer.Irrelevant(
                    "referential-action",
                    "参照アクションは表 d の外部キーを作り直させる手段で、前寄せの型の判定は参照アクションを読まない"
                ),
                AxisAnswer.Cross(
                    "key-kind",
                    "参照先のキー（主キー・一意制約）は形の行の因子 ReferencedKey が型の変更と 3 因子で交差する"
                ),
                AxisAnswer.Cross(
                    "column-change",
                    "両端を別々の型へ（DivergentEnds）。文字列の長さだけの変更は項目 pe1"
                ),
            ],
            FixedIn: null
        ),
        // ---- 3 回目の効果測定（2026-11-26・quicker-notes の reviews/review-measure3-2026-11-26.md）の直し残し。再現パッチは
        // reviews/review-measure3-repro/ ----
        Planned(
            "pa1",
            "Oracle の HoldsInLiveRows が、NULL 許容の列の live の一意制約を含む複合の一意制約を「既存の行で断られない」と証明し、追加が ORA-02299 で断られたとき前後で外した外部キーが名指しされずに消える（修正 G1a の直し残し・再現は v4-plan-ab-repro.patch）",
            SyncPlan,
            new PlannedBinding(
                "形の行（区切り c で入力の因子を足す）",
                "足す一意制約が live のキーを含むか × 構成列の NULL 許容 × NULL の重複のデータ × 巻き添えの外部キーの変更（CollateralFk）",
                "Oracle（実 DB）：NULL 許容の列の live の一意制約を含む複合の一意制約の追加 × その列が NULL で残りが同じ 2 行 × 表 d の外部キーの作り直し",
                "計画が追加の前後で外して付け直す制約を持つ"
            ),
            "汎用の表明 1b（巻き添えの制約の消失）",
            "区切り c（未起動）",
            AxisAnswer.Cross(
                "dialect-declaration",
                "live の一意制約を含む上位集合の一意制約が、一部が NULL の行で重複になりうるか",
                "区切り c で足す宣言の候補（実 DB で測って決める）：live の一意制約を含む上位集合の一意制約が、一部が NULL の行で重複になりうるか。今の読みは Oracle が真、SQL Server・PostgreSQL・MySQL・SQLite が偽（まだ測っていない）",
                "SyncDialectCapabilities.TransactionalDdl：偽の方言（MySQL・Oracle）だけで、断られた追加の前に外した制約が消えたまま残る"
            ),
            AxisAnswer.Cross(
                "step-origin",
                "選んだ一意制約の追加 × 暗黙の外部キーの作り直し（巻き添え）"
            ),
            AxisAnswer.Cross("data", "一部が NULL で残りが同じ 2 行・重複なし・行 0"),
            AxisAnswer.Irrelevant(
                "referential-action",
                "参照アクションは巻き添えの外部キーを作り直させる手段で、断られうる文の証明は参照アクションを読まない"
            ),
            AxisAnswer.Cross(
                "key-kind",
                "含む側の live のキーが主キー（NULL を持たないので証明が成り立つ対照）か一意制約か"
            ),
            AxisAnswer.Cross(
                "column-change",
                "構成列の NULL 許容（NULL 許容・NOT NULL）と、同じ計画の NOT NULL 化との組"
            )
        ),
        Planned(
            "pe1",
            "MySQL の前寄せ（AlterColumnRefusal）が外部キー単位の同期後の両端で「必ず断られる」と判定し、MySQL が受ける子の端の文字列の長さだけの変更を前寄せする。断られる親の変更が後ろに残り、その間に外した別の外部キーが名指しされずに消える（修正 AH の直し残し・再現は v5-plan-cde-repro.patch）",
            SyncPlan,
            new PlannedBinding(
                "形の行（区切り c で TypeChange の水準を足す）",
                "型の変更＝子の端は文字列の長さだけ・親の端は互換でない型（DivergentEndsStringLength）× 巻き添えの外部キーの変更（CollateralFk）",
                "MySQL（実 DB）：子の varchar の長さを広げ、親を int へ変え、別の外部キーを作り直す",
                "残す外部キーがあり、列の変更が 2 つ以上"
            ),
            "区切り c で足す実 DB の表明（型の食い違いの警告だけで失敗したら選んだ列は live のまま）",
            "区切り c（未起動）",
            AxisAnswer.Cross(
                "dialect-declaration",
                "外し直す方言と外し直さない方言 × 文字列の長さの違いを許すか",
                "SyncDialectCapabilities.ForeignKeyAllowsStringLengthDifference：真の MySQL は子の端の長さだけの変更を受け、偽の方言は断る（前寄せの判定が変わる）",
                "SyncDialectCapabilities.AlterColumnRequiresForeignKeyRebuild：真の方言は外部キー単位の同期後の両端で判定し、偽の方言（Oracle・PostgreSQL）は組単位で判定する（修正 AH は偽の側だけを直した）",
                "SyncDialectCapabilities.TransactionalDdl：偽の方言（MySQL・Oracle）だけが前寄せする"
            ),
            AxisAnswer.Cross(
                "step-origin",
                "選んだ列の変更 × 暗黙の外部キーの外し直し（列の変更）と作り直し（巻き添え）"
            ),
            AxisAnswer.Irrelevant(
                "data",
                "前寄せの型の判定は列の型だけを読み行の値を読まない（既存の行で断られる文は項目 pa1 の領域）"
            ),
            AxisAnswer.Irrelevant(
                "referential-action",
                "参照アクションは巻き添えの外部キーを作り直させる手段で、前寄せの型の判定は参照アクションを読まない"
            ),
            AxisAnswer.Cross("key-kind", "参照先のキー（主キー・一意制約）"),
            AxisAnswer.Cross(
                "column-change",
                "両端を別々の型へ：子の端は文字列の長さだけ・整数の幅だけ・型の種類の変更（DivergentEnds の兄弟の水準）"
            )
        ),
        Planned(
            "pe3",
            "SQL Server で、live の SET DEFAULT の外部キーの子の列（NULL 許容・DEFAULT なし）を NOT NULL にすると、暗黙の付け直しが Msg 1760 で断られるのに警告が出ない（区切り EL の直し残し＝規則は選んだ外部キーの追加だけを見る・再現は v5-plan-cde-repro.patch）",
            SyncPlan,
            new PlannedBinding(
                "形の行（区切り c で操作と参照アクションの因子を足す）",
                "操作＝外部キーを選んで足す／live の外部キーの子の列を変える × live の参照アクション × 子の列の NULL 許容の変更 × DEFAULT の有無",
                "SQL Server（実 DB）：live の SET DEFAULT の外部キーの子の列（NULL 許容・DEFAULT なし）の NOT NULL 化だけを選ぶ",
                "計画がその外部キーの暗黙の付け直しを持つ"
            ),
            "DEFAULT なしは汎用の表明 1、DEFAULT ありは区切り c で決める行の期待（警告を出すか）",
            "区切り c（未起動）",
            AxisAnswer.Cross(
                "dialect-declaration",
                "SET DEFAULT に列の DEFAULT 制約が要る方言かどうか",
                "SyncDialectCapabilities.SetDefaultRequiresColumnDefault：真の SQL Server だけが付け直しを Msg 1760 で断り、偽の方言は通す",
                "SyncDialectCapabilities.SupportsSetDefaultAction：偽の方言は SET DEFAULT を実効値へ畳むので、この形が出ない"
            ),
            AxisAnswer.Cross(
                "step-origin",
                "選んだ外部キーの追加（区切り EL で直した経路）× 暗黙の付け直し（主キーの変更・列の変更・途中の状態）"
            ),
            AxisAnswer.Irrelevant(
                "data",
                "Msg 1760 は外部キーを作る時点の宣言の検査で、行の値に依らずに断られる"
            ),
            AxisAnswer.Cross(
                "referential-action",
                "live の参照アクション（SET DEFAULT・SET NULL・CASCADE・NO ACTION）× ON DELETE と ON UPDATE"
            ),
            AxisAnswer.Cross(
                "key-kind",
                "子の列が主キーへ入る経路（主キーの変更）と入らない経路（列の変更）"
            ),
            AxisAnswer.Cross(
                "column-change",
                "子の列の NULL 許容だけの変更（NOT NULL 化）と主キーへ入れる変更 × 列の DEFAULT の有無"
            )
        ),
        Planned(
            "ha1",
            "PostgreSQL の EF Core 版で、date の列と Kind=Utc の時刻つき DateTime を比べると、インターセプターが timestamp で送り直し、Npgsql が ArgumentException で断る（区切り D3 の直し残し・再現は v7-efcore-repro.patch）",
            ValueBinding,
            new PlannedBinding(
                "述語の生成の表（区切り a）",
                "列の型 × 値が時刻つきか × DateTimeKind",
                "EF Core 版の PostgreSQL：date の列と Kind=Utc の時刻つき DateTime の比較",
                "値を列の型へ落とした述語と C# の答えが違う（Kind は組になる値の水準の条件で数える）"
            ),
            "C# の評価との一致（例外も不一致）",
            "区切り a（未起動）",
            AxisAnswer.Cross(
                "column-type",
                "date・timestamp・timestamptz の列（PostgreSQL）と、同じ判定を持つほかの方言の date の列"
            ),
            AxisAnswer.Cross(
                "value-pool",
                "DateTimeKind（Unspecified・Utc・Local）× 時刻つきか（0 時ちょうど・時刻つき）"
            ),
            AxisAnswer.Cross("value-count", "単一の値とリスト（項目 ha2 と同じ表で交差）"),
            AxisAnswer.Cross(
                "value-shape",
                "捕捉した変数・定数・メソッド呼び出し（DateTime.UtcNow）"
            ),
            AxisAnswer.Irrelevant(
                "column-shape",
                "直す分岐はパラメータの値と型だけを読み、列の側の式の形を読まない（列の側の形は項目 xb1）"
            ),
            AxisAnswer.Cross("comparison", "等値・順序・IN（Kind の例外は比べ方に依らず出る）"),
            AxisAnswer.Cross(
                "query-path",
                "LinqQuery・ExecuteDelete と、SET の値を変えない ExecuteUpdate（対照）"
            ),
            AxisAnswer.Cross(
                "backend",
                "EF Core 版の PostgreSQL（指摘）× ほかの EF Core のプロバイダと QuickER 版・インメモリ（C# の評価との一致で同じ行を流す）"
            )
        ),
        Planned(
            "ha2",
            "PostgreSQL の EF Core 版で、date の列への DateTime の IN のリストは配列のパラメータなので送り直されず、時刻が落ちて C# と違う行を返す（区切り D3 の直し残し＝time の枝はリストを扱うが date の枝は扱わない・再現は v7-efcore-repro.patch）",
            ValueBinding,
            new PlannedBinding(
                "述語の生成の表（区切り a）",
                "列の型 × 値の数（リスト）× 値が時刻つきか",
                "EF Core 版の PostgreSQL：date の列への時刻つき DateTime の IN・NOT IN",
                "時刻を落とした述語と C# の答えが違う"
            ),
            "C# の評価との一致",
            "区切り a（未起動）",
            AxisAnswer.Cross(
                "column-type",
                "date・time の列（PostgreSQL）と、同じリストの判定を持つほかの方言の列"
            ),
            AxisAnswer.Cross("value-pool", "時刻つきか（0 時ちょうど・時刻つき）× DateTimeKind"),
            AxisAnswer.Cross("value-count", "単一の値・リスト（IN・NOT IN）・配列のパラメータ"),
            AxisAnswer.Cross("value-shape", "捕捉したリスト・配列・定数の配列"),
            AxisAnswer.Irrelevant(
                "column-shape",
                "直す分岐はパラメータの値と型だけを読み、列の側の式の形を読まない（列の側の形は項目 xb1）"
            ),
            AxisAnswer.Cross("comparison", "IN・NOT IN と単一の値の等値（対照）"),
            AxisAnswer.Cross(
                "query-path",
                "LinqQuery（time の枝と同じ）・ExecuteDelete・ExecuteUpdate"
            ),
            AxisAnswer.Cross(
                "backend",
                "EF Core 版の PostgreSQL（指摘）× ほかの EF Core のプロバイダと QuickER 版・インメモリ"
            )
        ),
        Planned(
            "ha4",
            "SQLite の EF Core 版で、比べる値の式がラムダを含むと IsRowFree が行に依ると数えて書き換えを飛ばし、整数の列と decimal の値の IN が C# の 3 行に対して 0 行を返す（区切り CT の直し残し・再現は v7-efcore-repro.patch）",
            ValueBinding,
            new PlannedBinding(
                "述語の生成の表（区切り a）",
                "整数の列 × decimal の値 × 値の形（ラムダを含む式）",
                "EF Core 版の SQLite：整数の列と、ラムダを含む式で作った decimal のリストの IN",
                "整数へ切った述語と C# の答えが違う"
            ),
            "C# の評価との一致",
            "区切り a（未起動）",
            AxisAnswer.Cross(
                "column-type",
                "整数の列（int・bigint）と decimal の列（書き換えない対照）"
            ),
            AxisAnswer.Cross("value-pool", "整数の decimal（10m）と小数の decimal"),
            AxisAnswer.Cross("value-count", "単一の値とリスト（IN）"),
            AxisAnswer.Cross(
                "value-shape",
                "定数・捕捉した変数・メソッド呼び出し・ラムダを含む式（Select でリストを作る形）"
            ),
            AxisAnswer.Irrelevant(
                "column-shape",
                "書き換えは列の側を素の列としてだけ読み、列の側の式の形を読まない（列の側の形は項目 xt4）"
            ),
            AxisAnswer.Cross("comparison", "等値・IN と、書き換えない順序比較（対照）"),
            AxisAnswer.Cross(
                "query-path",
                "LinqQuery・ExecuteDelete・ExecuteUpdate（書き換えは Query の条件の 1 か所）"
            ),
            AxisAnswer.Cross(
                "backend",
                "EF Core 版の SQLite（指摘）× 同じ IsRowFree を使う PostgreSQL の値オブジェクトの IN の書き換え・QuickER 版・インメモリ"
            )
        ),
        Planned(
            "xb1",
            "SQL Server の QuickER 版で、日付部品（.Date）との比較は列名が取れないので値が AddWithValue に落ち、DateTime が datetime で束縛されて 1/300 秒へ丸められる（1753 年より前は例外）（修正 G2 の直し残し・再現は v10-xbind-xtranslator-repro.patch）",
            ValueBinding,
            new PlannedBinding(
                "述語の生成の表（区切り a）",
                "日付部品 × 値が列の型に入らない（刻みの外・1753 年より前）",
                "QuickER 版の SQL Server：日付部品との比較の値が datetime で束縛される",
                "1/300 秒へ丸めた述語と C# の答えが違う"
            ),
            "C# の評価との一致",
            "区切り a（未起動）",
            AxisAnswer.Cross("column-type", "datetime・datetime2・date・smalldatetime の列"),
            AxisAnswer.Cross(
                "value-pool",
                "刻みの外（1/300 秒の間・その日の 1 tick 前）・1753 年より前・範囲の最大"
            ),
            AxisAnswer.Cross("value-count", "単一の値とリスト（IN）"),
            AxisAnswer.Cross("value-shape", "捕捉した変数・定数"),
            AxisAnswer.Cross("column-shape", "素の列（修正 G2 が直した経路）・日付部品・型変換"),
            AxisAnswer.Cross("comparison", "等値・順序"),
            AxisAnswer.Cross(
                "query-path",
                "QuickER 版の翻訳器（LinqQuery・ExecuteDelete）と EF Core 版のインターセプター"
            ),
            AxisAnswer.Cross(
                "backend",
                "QuickER 版の SQL Server（指摘）× SQLite・インメモリ（一致の対照）・EF Core 版の SQL Server"
            )
        ),
        Planned(
            "xt4",
            "式の翻訳器が値を変える明示の型変換を黙って外す（値の側の (int)ratio は小数のまま束縛し、列の側の (byte)x.Id は変換なしの列と比べる）。SQLite・SQL Server の QuickER 版が C# と違う行を返す（前からある新規・再現は v10-xbind-xtranslator-repro.patch）",
            ValueBinding,
            new PlannedBinding(
                "述語の生成の表（区切り a）",
                "型変換で値が実際に変わるか × 値の側・列の側",
                "QuickER 版の SQLite・SQL Server：値を変える明示の型変換を外した述語",
                "変換を外した述語と C# の答えが違う"
            ),
            "C# の評価との一致",
            "区切り a（未起動）",
            AxisAnswer.Cross("column-type", "整数の列（byte・int・long）と小数の値・範囲の外の値"),
            AxisAnswer.Cross(
                "value-pool",
                "型変換で値が変わる値（小数部・範囲の外）と変わらない値（対照）"
            ),
            AxisAnswer.Cross("value-count", "単一の値とリスト（IN）"),
            AxisAnswer.Cross("value-shape", "値の側の明示の型変換"),
            AxisAnswer.Cross("column-shape", "列の側の明示の型変換"),
            AxisAnswer.Cross("comparison", "等値・順序・IN"),
            AxisAnswer.Cross(
                "query-path",
                "QuickER 版の翻訳器（LinqQuery・ExecuteDelete・ExecuteUpdate）"
            ),
            AxisAnswer.Cross(
                "backend",
                "QuickER 版の SQLite・SQL Server（指摘）× インメモリ（一致の対照）・EF Core 版"
            )
        ),
    ];

    /// <summary>予定の項目（因子の名前を文で持つ・表に未結び）</summary>
    private static BranchAxisItem Planned(
        string id,
        string finding,
        string area,
        PlannedBinding plan,
        string judge,
        string fixedIn,
        params AxisAnswer[] answers
    ) => new(id, finding, area, BranchAxisStatus.Planned, plan, null, judge, answers, fixedIn);
}
