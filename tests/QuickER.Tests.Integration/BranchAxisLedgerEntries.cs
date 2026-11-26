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
        BoundFixed(
            "pa1",
            "Oracle の HoldsInLiveRows が、NULL 許容の列の live の一意制約を含む複合の一意制約を「既存の行で断られない」と証明し、追加が ORA-02299 で断られたとき前後で外した外部キーが名指しされずに消える（修正 G1a の直し残し・再現は v4-plan-ab-repro.patch）",
            new BranchBinding(
                "SyncPlan/ShapeRows",
                [
                    new("ExplicitOp", "WidenUnique"),
                    new("UniqueColumnData", "NullRepeats"),
                    new("UniqueMultiplicity", "1"),
                    new("CollateralFk", "AlterOtherForeignKey"),
                ],
                "pa1-widen-unique-over-null-rows",
                ["oracle"],
                "row-dependent-named-widened-unique",
                [
                    new(
                        "sqlserver",
                        "SQL Server は一意制約の列の NULL どうしを重なりとみなし、u の一意制約の下に NULL を 2 行持てない（2 行目の INSERT で断る）",
                        "SyncPlanFuzzDialect.UniqueAllowsRepeatedNull"
                    ),
                ]
            ),
            "`SyncPlan/ShapeRows`（静的な規則 R10・D9）",
            AxisAnswer.Cross(
                "dialect-declaration",
                "一部が NULL の重複を重なりとみなすか（UniqueColumnData の NullRepeats × 方言）",
                "SyncDialectCapabilities.UniqueConstraintComparesPartiallyNullKeys（一意制約が、構成列の全部が NULL の行だけを検査の外に置き、一部が NULL の行は残りの列で比べるか。既定値なし）：2026-11-26 の実測で Oracle が真（ORA-02299）、PostgreSQL・MySQL・SQLite は偽（受ける）、SQL Server は偽（NULL どうしを等しいとみなすので u の一意制約の下に重複を持てない）",
                "SyncDialectCapabilities.TransactionalDdl：偽の方言（MySQL・Oracle）だけで、断られた追加の前に外した制約が消えたまま残る"
            ),
            AxisAnswer.Cross(
                "step-origin",
                "選んだ一意制約の追加 × 巻き添えの外部キーの作り直し（必ず含む行 pa1-widen-unique-over-null-rows）・主キーの並べ替えの暗黙の外し直し（必ず含む行 pa1-widen-unique-over-null-rows-pk-reorder）"
            ),
            AxisAnswer.Cross(
                "data",
                "UniqueColumnData（Distinct＝重複なし・NullRepeats＝一部が NULL の重複・CompositeNullRepeats＝全部 NULL 許容の複合のキーの下の NULL の重複・CompositePartialNull＝一部だけ NULL 許容の複合のキー）× 行 0 の表（両端の型を変える水準）"
            ),
            AxisAnswer.Irrelevant(
                "referential-action",
                "参照アクションは巻き添えの外部キーを作り直させる手段で、断られうる文の証明は参照アクションを読まない"
            ),
            AxisAnswer.Cross(
                "key-kind",
                "含む側の live のキー：一意制約（UniqueMultiplicity 1・2）・キーなし（UniqueMultiplicity 0＝今も名指しする対照）・主キー（PkChange Extend の主キーの付与の証明）"
            ),
            AxisAnswer.Cross(
                "column-change",
                "含まれる live のキーの構成列の NULL 許容（単一列の NullRepeats・全部 NULL 許容の複合 CompositeNullRepeats・一部だけ NULL 許容の複合 CompositePartialNull・NOT NULL の Distinct）と、同じ計画の子の列の変更（TypeChange）"
            )
        ),
        BoundFixed(
            "pe1",
            "MySQL の前寄せ（AlterColumnRefusal）が外部キー単位の同期後の両端で「必ず断られる」と判定し、MySQL が受ける子の端の文字列の長さだけの変更を前寄せする。断られる親の変更が後ろに残り、その間に外した別の外部キーが名指しされずに消える（修正 AH の直し残し・再現は v5-plan-cde-repro.patch）",
            new BranchBinding(
                "SyncPlan/ShapeRows",
                [
                    new("TypeChange", "DivergentEndsStringLength"),
                    new("ReferencingFks", "1"),
                    new("CollateralFk", "AlterOtherForeignKey"),
                ],
                "pe1-divergent-ends-string-length",
                ["mysql"],
                "moved-first-parent-change",
                []
            ),
            "`SyncPlan/ShapeRows`（静的な規則 M1・D9・R2）",
            AxisAnswer.Cross(
                "dialect-declaration",
                "外し直しの方言が、外部キーを張ったまま可変長の型を伸ばす変更を受けるか（DivergentEndsStringLength＝受ける範囲・DivergentEndsStringLengthAcrossPrefix＝長さの頭のバイト数が変わり MySQL が断る範囲）",
                "SyncDialectCapabilities.ForeignKeyColumnWidening（既定値なしの列挙。外部キーを張ったままの列で可変長の型を伸ばす変更を受けるか）：2026-11-26 の実測で MySQL は長さの頭のバイト数が変わらないときだけ受け（WithinLengthPrefix。判定は取込が読む列のバイト数の事実で行い、事実の無い列と文字集合を変える変更は必ず断られる文に数える）、SQL Server は断り（Refused・Msg 5074）、PostgreSQL・Oracle は受ける（Accepted）",
                "SyncDialectCapabilities.AlterColumnRequiresForeignKeyRebuild：真の方言（SQL Server・MySQL）は外部キー単位の同期後の両端で判定し、偽の方言（Oracle・PostgreSQL・SQLite）は組の相手の端の今の型で判定する（修正 AH は偽の側だけを直した）",
                "SyncDialectCapabilities.TransactionalDdl：偽の方言（MySQL・Oracle）だけが前寄せする"
            ),
            AxisAnswer.Cross(
                "step-origin",
                "選んだ列の変更 × 暗黙の外部キーの外し直し（列の変更）と巻き添えの作り直し（CollateralFk）"
            ),
            AxisAnswer.Irrelevant(
                "data",
                "前寄せの型の判定は列の型だけを読み行の値を読まない（実測でも行の有無で結果は変わらなかった。既存の行で断られる文は項目 pa1 の領域）"
            ),
            AxisAnswer.Irrelevant(
                "referential-action",
                "参照アクションは巻き添えの外部キーを作り直させる手段で、前寄せの型の判定は参照アクションを読まない"
            ),
            AxisAnswer.Cross("key-kind", "参照先のキー（ReferencedKey の主キー・一意制約）"),
            AxisAnswer.Cross(
                "column-change",
                "両端を別々の型へ：子の端は整数の幅（DivergentEnds）・可変長の文字列の長さの受ける範囲（DivergentEndsStringLength）・受けない範囲（DivergentEndsStringLengthAcrossPrefix）・文字数では判定できない 64〜255 文字の帯（DivergentEndsStringLengthInBand）・1 文字あたりのバイト数で答えが変わる latin1（DivergentEndsStringLengthLatin1）"
            )
        ),
        BoundFixed(
            "pe3",
            "SQL Server で、live の SET DEFAULT の外部キーの子の列（NULL 許容・DEFAULT なし）を NOT NULL にすると、暗黙の付け直しが Msg 1760 で断られるのに警告が出ない（区切り EL の直し残し＝規則は選んだ外部キーの追加だけを見る・再現は v5-plan-cde-repro.patch）",
            new BranchBinding(
                "SyncPlan/ShapeRows",
                [new("TypeChange", "ChildNotNull"), new("ChildOnDelete", "SetDefault")],
                "pe3-live-set-default-child-not-null",
                ["sqlserver"],
                "set-default-readd-warned",
                [
                    new(
                        "mysql",
                        "MySQL は SET DEFAULT の外部キーを作らない（製品は実効値へ畳む）",
                        "SyncDialectCapabilities.SupportsSetDefaultAction"
                    ),
                    new(
                        "oracle",
                        "Oracle は SET DEFAULT の外部キーを作らない（製品は実効値へ畳む）",
                        "SyncDialectCapabilities.SupportsSetDefaultAction"
                    ),
                ]
            ),
            "`SyncPlan/ShapeRows`（静的な規則 R13）",
            AxisAnswer.Cross(
                "dialect-declaration",
                "SET DEFAULT に列の既定値が要る方言かどうか（ChildOnDelete の SetDefault × 方言）",
                "SyncDialectCapabilities.SetDefaultRequiresColumnDefault：真の SQL Server だけが付け直しを Msg 1760 で断り（2026-11-26 の実測。列に DEFAULT 制約があれば受ける）、偽の PostgreSQL・SQLite は作る",
                "SyncDialectCapabilities.SupportsSetDefaultAction：偽の方言（MySQL・Oracle）は SET DEFAULT を作らないので、この形が出ない"
            ),
            AxisAnswer.Cross(
                "step-origin",
                "選んだ外部キーの追加（区切り EL で直した経路・網羅の行）× 暗黙の付け直し：列の変更（ChildNotNull）・主キーの変更（PkChange と ChildOnDelete の組）"
            ),
            AxisAnswer.Irrelevant(
                "data",
                "Msg 1760 は外部キーを作る時点の宣言の検査で、行の値に依らずに断られる（実測でも行の有無で結果は変わらなかった）"
            ),
            AxisAnswer.Cross(
                "referential-action",
                "ChildOnDelete（NoAction・SetDefault・SetNull・Cascade）。ON UPDATE の SET DEFAULT は網羅の行の手で書いた行が受け持つ"
            ),
            AxisAnswer.Cross(
                "key-kind",
                "参照先のキー（ReferencedKey）。子の列を子の主キーへ入れる経路は形の行に無く、網羅の行で受ける"
            ),
            AxisAnswer.Cross(
                "column-change",
                "子の列の NOT NULL 化（ChildNotNull）・NULL 許容化（ChildNullability）・型の変更（ChildOnly ほか）× 列の既定値の有無（網羅の行）"
            )
        ),
        BoundFixed(
            "ha1",
            "PostgreSQL の EF Core 版で、date の列と Kind=Utc の時刻つき DateTime を比べると、インターセプターが timestamp で送り直し、Npgsql が ArgumentException で断る（区切り D3 の直し残し・再現は v7-efcore-repro.patch）",
            ValueBinding,
            new BranchBinding(
                BindingTable,
                [new("ColumnType", "date"), new("DateTimeKind", "utc")],
                "ha1-date-less-utc-time-of-day",
                ["EfCorePostgreSql"],
                "utc-date-parameter-sent-without-kind",
                []
            ),
            EfCoreBindingJudge,
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
        BoundFixed(
            "ha2",
            "PostgreSQL の EF Core 版で、date の列への DateTime の IN のリストは配列のパラメータなので送り直されず、時刻が落ちて C# と違う行を返す（区切り D3 の直し残し＝time の枝はリストを扱うが date の枝は扱わない・再現は v7-efcore-repro.patch）",
            ValueBinding,
            new BranchBinding(
                BindingTable,
                [new("ColumnType", "date"), new("ValueCount", "list")],
                "ha2-date-in-list-time-of-day",
                ["EfCorePostgreSql"],
                "date-list-sent-as-timestamp-array",
                []
            ),
            EfCoreBindingJudge,
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
        BoundFixed(
            "ha4",
            "SQLite の EF Core 版で、比べる値の式がラムダを含むと IsRowFree が行に依ると数えて書き換えを飛ばし、整数の列と decimal の値の IN が C# の 3 行に対して 0 行を返す（区切り CT の直し残し・再現は v7-efcore-repro.patch）",
            ValueBinding,
            new BranchBinding(
                BindingTable,
                [new("ValueShape", "lambda"), new("ValuePool", "integral-decimal")],
                "ha4-int-in-decimal-lambda-list",
                ["EfCoreSqlite"],
                "row-free-lambda-rewritten",
                []
            ),
            EfCoreBindingJudge,
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
        BoundFixed(
            "xb1",
            "SQL Server の QuickER 版で、日付部品（.Date）との比較は列名が取れないので値が AddWithValue に落ち、DateTime が datetime で束縛されて 1/300 秒へ丸められる（1753 年より前は例外）（修正 G2 の直し残し・再現は v10-xbind-xtranslator-repro.patch）",
            ValueBinding,
            new BranchBinding(
                BindingTable,
                [new("ColumnShape", "date-part")],
                "xb1-date-part-equal-tick-before-midnight",
                ["AdoSqlServer"],
                "date-part-value-bound-as-datetime2",
                []
            ),
            QuickErBindingJudge,
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
        BoundFixed(
            "xt4",
            "式の翻訳器が値を変える明示の型変換を黙って外す（値の側の (int)ratio は小数のまま束縛し、列の側の (byte)x.Id は変換なしの列と比べる）。SQLite・SQL Server の QuickER 版が C# と違う行を返す（前からある新規・再現は v10-xbind-xtranslator-repro.patch）",
            ValueBinding,
            new BranchBinding(
                BindingTable,
                [new("ValueShape", "narrowing-cast"), new("ValuePool", "fraction-cut-by-cast")],
                "xt4-value-narrowing-cast-equal",
                ["AdoSqlite", "AdoSqlServer"],
                "narrowing-cast-honoured",
                []
            ),
            QuickErBindingJudge,
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
                "QuickER 版の翻訳器（LinqQuery・ExecuteDelete。QuickER 版に ExecuteUpdate の経路は無い）と、EF Core 版の LinqQuery・ExecuteDelete・ExecuteUpdate（対照）"
            ),
            AxisAnswer.Cross(
                "backend",
                "QuickER 版の SQLite・SQL Server（指摘）× インメモリ（一致の対照）・EF Core 版"
            )
        ),
    ];

    /// <summary>値の束縛の生成の表の台帳の部分の名前</summary>
    private const string BindingTable = "GeneratedRuntime/TranslatorParityBindings";

    /// <summary>QuickER 版・インメモリの指摘のセルの判定役（値の束縛の生成の表と、指摘のセルの照合）</summary>
    private const string QuickErBindingJudge =
        "`GeneratedRuntime/TranslatorParityBindings`（`TranslatorParityRuntimeTestsBase.LedgerCells_FollowStatus`・`TranslatorParityRuntimeTestsBase.Binding_MatchesCSharp`）";

    /// <summary>EF Core 版の指摘のセルの判定役（値の束縛の生成の表と、指摘のセルの照合）</summary>
    private const string EfCoreBindingJudge =
        "`GeneratedRuntime/TranslatorParityBindings`（`TranslatorParityEfCoreRuntimeTestsBase.LedgerCells_FollowStatus`・`TranslatorParityEfCoreRuntimeTestsBase.Binding_MatchesCSharp`）";

    /// <summary>結び済み・修正済みの項目（同期の計画の領域・区切り c で直した）</summary>
    private static BranchAxisItem BoundFixed(
        string id,
        string finding,
        BranchBinding binding,
        string judge,
        params AxisAnswer[] answers
    ) => BoundFixed(id, finding, SyncPlan, binding, judge, answers);

    /// <summary>結び済み・修正済みの項目（領域を名指す。値の束縛の領域は区切り a で直した）</summary>
    private static BranchAxisItem BoundFixed(
        string id,
        string finding,
        string area,
        BranchBinding binding,
        string judge,
        params AxisAnswer[] answers
    ) => new(id, finding, area, BranchAxisStatus.BoundFixed, null, binding, judge, answers, null);
}
