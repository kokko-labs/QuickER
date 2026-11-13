using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>機能 × 実装先の行列の機能の名前（<see cref="RuntimeCellAttribute"/> の第 1 引数に書く）</summary>
public static class RuntimeFeatures
{
    public const string CrudParity = nameof(CrudParity);
    public const string Concurrency = nameof(Concurrency);
    public const string EditModelSave = nameof(EditModelSave);
    public const string ConcurrencyVo = nameof(ConcurrencyVo);
    public const string SaveHook = nameof(SaveHook);
    public const string BinaryColumn = nameof(BinaryColumn);
    public const string BinaryColumnEditModel = nameof(BinaryColumnEditModel);
    public const string UniquenessCheck = nameof(UniquenessCheck);
    public const string UniquenessEnforcement = nameof(UniquenessEnforcement);
    public const string IncludeGraph = nameof(IncludeGraph);
    public const string GraphSave = nameof(GraphSave);
    public const string BackendParity = nameof(BackendParity);
    public const string EfCoreExecutionStrategy = nameof(EfCoreExecutionStrategy);
    public const string IgnoredWrite = nameof(IgnoredWrite);
    public const string IncludeGraphSelfReference = nameof(IncludeGraphSelfReference);
    public const string NamedQuery = nameof(NamedQuery);
    public const string NamedQueryRawSql = nameof(NamedQueryRawSql);
    public const string TranslatorOperator = nameof(TranslatorOperator);
    public const string ValueObjectStringMatch = nameof(ValueObjectStringMatch);
    public const string TranslatorParity = nameof(TranslatorParity);
    public const string SchemaBootstrap = nameof(SchemaBootstrap);
    public const string MultiTarget = nameof(MultiTarget);
    public const string MultiTargetRowVersion = nameof(MultiTargetRowVersion);
    public const string ComputedColumn = nameof(ComputedColumn);
    public const string IdentityColumn = nameof(IdentityColumn);
    public const string SyncSupport = nameof(SyncSupport);
    public const string SyncRefresh = nameof(SyncRefresh);
    public const string DialectPortability = nameof(DialectPortability);
    public const string EditModelValidateUnique = nameof(EditModelValidateUnique);
    public const string RemoteContract = nameof(RemoteContract);
    public const string RemoteTransport = nameof(RemoteTransport);
    public const string SqlExecutorInjection = nameof(SqlExecutorInjection);
    public const string ForeignKeyDefault = nameof(ForeignKeyDefault);
    public const string BusyTimeoutDefault = nameof(BusyTimeoutDefault);
    public const string ValueConversion = nameof(ValueConversion);
}

/// <summary>担当でないセルの種類（担当は宣言せず、属性とエンジンの登録から導く）</summary>
public enum RuntimeCellKind
{
    /// <summary>空き：まだ検証していない（直す区切りの名前が必須＝<see cref="RuntimeCellDeclaration.FixedIn"/>）</summary>
    Gap,

    /// <summary>割り切り：意図して検証しない（根拠の docs か CLAUDE.md の見出しが必須＝<see cref="RuntimeCellDeclaration.Basis"/>）</summary>
    Accepted,

    /// <summary>非該当：その組み合わせが原理的に成り立たない</summary>
    NotApplicable,
}

/// <summary>担当でないセルの宣言</summary>
/// <param name="Kind">種類</param>
/// <param name="Reason">理由（具体的に書く）</param>
/// <param name="FixedIn">空きの直す区切り（決まっていなければ <see cref="RuntimeMatrixRows.Trigger"/>）。空き以外は null</param>
/// <param name="Basis">割り切りの根拠（<c>リポジトリの相対パス「見出し」</c>）。割り切り以外は null</param>
public sealed record RuntimeCellDeclaration(
    RuntimeCellKind Kind,
    string Reason,
    string? FixedIn = null,
    string? Basis = null
);

/// <summary>リモートの担当のうち、まだ流していないサーバーの宣言（担当のサーバーに現れたら消す）</summary>
/// <param name="Server">流していないサーバーの裏の実装先</param>
/// <param name="FixedIn">直す区切り</param>
/// <param name="Reason">理由</param>
public sealed record RuntimeMissingServer(RuntimeTarget Server, string FixedIn, string Reason);

/// <summary>機能の行（説明・担当でないセル・リモートの足りないサーバー）</summary>
/// <param name="Feature">機能の名前</param>
/// <param name="Description">機能の説明</param>
/// <param name="Declared">担当でないセルの宣言</param>
/// <param name="MissingServers">リモートの担当のうち、まだ流していないサーバー</param>
public sealed record RuntimeFeatureRow(
    string Feature,
    string Description,
    IReadOnlyDictionary<RuntimeTarget, RuntimeCellDeclaration> Declared,
    IReadOnlyList<RuntimeMissingServer> MissingServers
);

/// <summary>
/// 機能 × 実装先の行列の行の表：機能ごとの説明と、担当でないセル（空き・割り切り・非該当）だけを宣言する
/// </summary>
/// <remarks>
/// 担当は書かない（テストのクラスの <see cref="RuntimeCellAttribute"/> とエンジンの登録 <see cref="RuntimeMatrixEngines"/> から導く）。
/// どの機能 × どの実装先も、担当か宣言のちょうど一方（<c>RuntimeTestMatrixTests</c>）。実装先を足すと全機能のその列が名指しで赤になり、
/// 空きを埋めたら宣言を消す。
/// </remarks>
public static class RuntimeMatrixRows
{
    /// <summary>直す区切りが決まっていない空きの区切りの名前</summary>
    public const string Trigger = "きっかけ待ち";

    private const string EfCoreUntested =
        "この機能を EF Core 版のこの方言の実 DB で流すテストは無い（EF Core の列を方言ごとに分けて見えた空き）";

    private const string RowVersionOnlyOnSqlServer =
        "rowversion 型は SQL Server だけにあり（src/QuickER.Runtime/CLAUDE.md「rowversion による楽観排他」の既知の制限）、"
        + "PostgreSQL・MySQL・Oracle の図に版の列が生まれないため、版ガードの対象そのものが存在しない";

    private const string QueryNotRemote = "Query() はリモート面に無く、式木は転送されない";

    private const string IncludeNotRemote =
        "Query() はリモート面に無く、Include ツリーは転送されない";

    private const string MultiTargetExcludesEfCore = "マルチターゲットと EF Core は生成時に排他";

    private static readonly RuntimeTarget[] EfCoreAll =
    [
        RuntimeTarget.EfCoreSqlServer,
        RuntimeTarget.EfCoreSqlite,
        RuntimeTarget.EfCorePostgreSql,
        RuntimeTarget.EfCoreMySql,
        RuntimeTarget.EfCoreOracle,
    ];

    private static readonly RuntimeTarget[] EfCoreOthers =
    [
        RuntimeTarget.EfCorePostgreSql,
        RuntimeTarget.EfCoreMySql,
        RuntimeTarget.EfCoreOracle,
    ];

    /// <summary>機能の行（行列の行の並び）</summary>
    public static IReadOnlyList<RuntimeFeatureRow> All { get; } =
    [
        Row(
            RuntimeFeatures.CrudParity,
            "CRUD・グラフ保存・Query() の共通シナリオ（パリティ基底が同一シナリオを各実装へ掛ける）",
            On(Gap(Trigger, EfCoreUntested), EfCoreOthers)
        ),
        Row(
            RuntimeFeatures.Concurrency,
            "rowversion による楽観排他（版ガード・SaveConflictException の分類）",
            [
                On(
                    NotApplicable(
                        "SQLite 単独の図に rowversion 列は生まれない（timestamp は日時の別名へ解決される）ため、"
                            + "版ガードの対象そのものが存在しない。マルチターゲットでの非対称は MultiTargetRowVersion 行が持つ"
                    ),
                    RuntimeTarget.AdoSqlite,
                    RuntimeTarget.EfCoreSqlite
                ),
                On(NotApplicable(RowVersionOnlyOnSqlServer), EfCoreOthers),
            ],
            [
                new(
                    RuntimeTarget.AdoSqlServer,
                    Trigger,
                    "リモートの楽観排他のサーバーはインメモリだけで、実 rowversion を HTTP で往復させる QuickER 版 SQL Server のサーバーのテストは無い"
                ),
            ]
        ),
        Row(
            RuntimeFeatures.EditModelSave,
            "EditModel の保存の受け入れ（AcceptSaved＝版・DB とフックが書いた値の読み戻し・フックが飛ばした行・足して消した子）",
            On(
                NotApplicable(
                    "シナリオは rowversion 列の版を読み戻す形で、SQLite 単独の図に rowversion 列は生まれない。"
                        + "読み戻しの処理は実装先に依らない（マッパーの共通部）"
                ),
                RuntimeTarget.AdoSqlite,
                RuntimeTarget.EfCoreSqlite
            ),
            On(NotApplicable(RowVersionOnlyOnSqlServer), EfCoreOthers)
        ),
        Row(
            RuntimeFeatures.ConcurrencyVo,
            "楽観排他 × 値オブジェクト（版プロパティが VO 型になる経路の Wrap/Unwrap）",
            On(
                NotApplicable("Concurrency 行と同じ理由（SQLite 単独図に rowversion 列が無い）"),
                RuntimeTarget.AdoSqlite,
                RuntimeTarget.EfCoreSqlite
            ),
            On(NotApplicable(RowVersionOnlyOnSqlServer), EfCoreOthers)
        ),
        Row(
            RuntimeFeatures.SaveHook,
            "ISaveHook（Before のスキップ・After のトランザクション参加）",
            On(Gap(Trigger, EfCoreUntested), EfCoreOthers)
        ),
        Row(
            RuntimeFeatures.BinaryColumn,
            "無制限バイナリ列の除外・WithUnboundedBinary・Stream アクセサ",
            On(Gap(Trigger, EfCoreUntested), [RuntimeTarget.EfCoreSqlServer, .. EfCoreOthers])
        ),
        Row(
            RuntimeFeatures.BinaryColumnEditModel,
            "無制限バイナリ除外 × 値オブジェクト × EditModel / Mapper の往復（未取得の除外列を素通しする保存）",
            On(
                Gap(
                    Trigger,
                    "EditModel / Mapper は方言に依らない生成物で、除外の意味論は SQLite で代表させている"
                        + "（SQL Server 側の除外の実 DB 検証は BinaryColumn 行が持つ）"
                ),
                RuntimeTarget.AdoSqlServer
            ),
            On(
                NotApplicable(
                    "EF Core は列選択を自身が行うため除外が非適用＝除外列が未取得になる状況が発生しない"
                ),
                EfCoreAll
            ),
            On(
                Gap(
                    Trigger,
                    "インメモリも除外の意味論を持つが、往復の主題は「実 DB の UPDATE が除外列に触れない」ことなので"
                        + "実 DB 側を先に押さえている"
                ),
                RuntimeTarget.InMemory
            ),
            On(
                NotApplicable("EditModel / Mapper はクライアント側の型でリモート面を経由しない"),
                RuntimeTarget.Remote
            )
        ),
        Row(
            RuntimeFeatures.UniquenessCheck,
            "UNIQUE 制約の事前チェック（重複検出・自分自身の除外）",
            On(Gap(Trigger, EfCoreUntested), [RuntimeTarget.EfCoreSqlServer, .. EfCoreOthers])
        ),
        Row(
            RuntimeFeatures.UniquenessEnforcement,
            "書き込み時の UNIQUE 制約強制（インメモリが実 DB の制約の代役を務める。実 DB の実装先は、DB の拒否が呼び出し側へ同じ形で届き何も書かないことを実装先の一致が比べる）",
            On(
                NotApplicable(
                    "実 DB では制約が DDL の実体として存在し、拒否するのは DB 自身＝生成コードの挙動ではない"
                        + "（SQLite の実装先の拒否の届き方は実装先の一致が比べ、SQL Server 方言の実装先の一致は版の系統だけを持つ）"
                ),
                RuntimeTarget.AdoSqlServer
            ),
            On(Gap(Trigger, EfCoreUntested), [RuntimeTarget.EfCoreSqlServer, .. EfCoreOthers])
        ),
        Row(
            RuntimeFeatures.IncludeGraph,
            "グラフ取得糖衣（IncludeGraph＝カスケード閉包の Include ツリー）",
            On(Gap(Trigger, EfCoreUntested), [RuntimeTarget.EfCoreSqlServer, .. EfCoreOthers]),
            On(NotApplicable(IncludeNotRemote), RuntimeTarget.Remote)
        ),
        Row(
            RuntimeFeatures.GraphSave,
            "グラフ保存がカスケードの閉包だけを保存する（親への逆参照を辿らない・削除の根の子孫）",
            On(Gap(Trigger, EfCoreUntested), EfCoreOthers)
        ),
        Row(
            RuntimeFeatures.BackendParity,
            "実装先どうしの一致（同じ操作の列を流し、戻り値・保存先・呼び出し側のグラフ・Save フックを QuickER 版と比べる）",
            On(Gap(Trigger, EfCoreUntested), EfCoreOthers)
        ),
        Row(
            RuntimeFeatures.EfCoreExecutionStrategy,
            "再試行する実行戦略のもとで、自前のトランザクションを開く経路（フックありの保存・カスケードの一括削除）を試行ごとに組み直す",
            On(
                NotApplicable("実行戦略は EF Core の機構"),
                RuntimeTarget.AdoSqlServer,
                RuntimeTarget.AdoSqlite,
                RuntimeTarget.InMemory,
                RuntimeTarget.Remote
            ),
            On(Gap(Trigger, EfCoreUntested), [RuntimeTarget.EfCoreSqlServer, .. EfCoreOthers])
        ),
        Row(
            RuntimeFeatures.IgnoredWrite,
            "版の無い表で競合以外の理由により更新が当たらないときの扱い（行なしとして報告・やり直さない）",
            On(
                Gap(
                    Trigger,
                    "QuickER 版は SQL Server・SQLite とも版の無い表で 0 行を実在確認しない同じ規則で、SQLite で代表させている"
                ),
                RuntimeTarget.AdoSqlServer
            ),
            On(Gap(Trigger, EfCoreUntested), [RuntimeTarget.EfCoreSqlServer, .. EfCoreOthers]),
            On(
                NotApplicable(
                    "インメモリにはトリガー・行レベルセキュリティが無く、文が当たらない状況が生まれない"
                ),
                RuntimeTarget.InMemory
            )
        ),
        Row(
            RuntimeFeatures.IncludeGraphSelfReference,
            "IncludeGraph の edge-skip（自己参照ナビを展開しない）",
            On(Gap(Trigger, EfCoreUntested), [RuntimeTarget.EfCoreSqlServer, .. EfCoreOthers]),
            On(NotApplicable(IncludeNotRemote), RuntimeTarget.Remote)
        ),
        Row(RuntimeFeatures.NamedQuery, "名前付きクエリ（簡易 DSL・射影・手動実装）"),
        Row(
            RuntimeFeatures.NamedQueryRawSql,
            "名前付きクエリの生 SQL 実装（戻り形・パラメータ展開）",
            On(
                Gap(
                    Trigger,
                    "SQL Server の名前付きクエリの実行時テストは簡易 DSL の総当たりだけで、生 SQL の実装は未整備"
                ),
                RuntimeTarget.AdoSqlServer
            ),
            On(Gap(Trigger, EfCoreUntested), [RuntimeTarget.EfCoreSqlServer, .. EfCoreOthers]),
            On(
                NotApplicable(
                    "生 SQL の実装先を持たないインメモリでは契約宣言のみが生成され、実装は利用者の partial 実装になる"
                        + "（「SQL が与えられていない実装先は手動実装」の統一規則）"
                ),
                RuntimeTarget.InMemory
            )
        ),
        Row(
            RuntimeFeatures.TranslatorOperator,
            "式木 → SQL 翻訳の演算子分岐（NULL 意味論の補償・bool 列の短縮形）",
            On(
                NotApplicable(
                    "EF Core は式木を Where へ直接渡し、翻訳は EF Core の LINQ プロバイダが行う"
                        + "（SqlExpressionTranslator を通らない）"
                ),
                EfCoreAll
            ),
            On(
                NotApplicable(
                    "インメモリは式木をコンパイルして C# 意味論で評価する（SQL を組み立てない）"
                ),
                RuntimeTarget.InMemory
            ),
            On(NotApplicable(QueryNotRemote), RuntimeTarget.Remote)
        ),
        Row(
            RuntimeFeatures.ValueObjectStringMatch,
            "値オブジェクトの文字列一致（Contains / StartsWith / EndsWith）の判定を、具象の値オブジェクト型から引いた MethodInfo でも同じにする",
            On(Gap(Trigger, EfCoreUntested), RuntimeTarget.EfCoreSqlServer),
            On(NotApplicable(QueryNotRemote), RuntimeTarget.Remote)
        ),
        Row(
            RuntimeFeatures.TranslatorParity,
            "式木の翻訳・識別子の引用・Include の照合・UPDATE 文の組み立てを C# の意味論と突き合わせる共通シナリオ",
            On(NotApplicable(QueryNotRemote), RuntimeTarget.Remote)
        ),
        Row(
            RuntimeFeatures.SchemaBootstrap,
            "生成 DDL の適用ヘルパー（ApplyDdlAsync）",
            On(
                NotApplicable(
                    "スキーマ作成は DDL 生成の責務で、EF Core は既存スキーマへの接続専用"
                ),
                EfCoreAll
            ),
            On(NotApplicable("インメモリにスキーマの概念が無い"), RuntimeTarget.InMemory),
            On(
                NotApplicable("スキーマ適用はサーバー側の運用作業でリモート面に無い"),
                RuntimeTarget.Remote
            )
        ),
        Row(
            RuntimeFeatures.MultiTarget,
            "マルチターゲット生成（中立契約 1 回＋方言別実装・keyed DI）",
            On(NotApplicable("マルチターゲットと EF Core は生成時に排他（診断エラー）"), EfCoreAll),
            On(
                Gap(
                    Trigger,
                    "インメモリとの併用はコンパイル行列で押さえているが、keyed DI に方言実装とインメモリを"
                        + "同居させる実行時テストは無い"
                ),
                RuntimeTarget.InMemory
            ),
            On(
                Gap(Trigger, "リモート面との併用は直交だが、実行時の組合せ検証は無い"),
                RuntimeTarget.Remote
            )
        ),
        Row(
            RuntimeFeatures.MultiTargetRowVersion,
            "マルチターゲット × rowversion（SQL Server は書き込み除外＋版ガード・SQLite は通常列）",
            On(NotApplicable(MultiTargetExcludesEfCore), EfCoreAll),
            On(
                Gap(
                    Trigger,
                    "インメモリは擬似版を採番する SQL Server の代役として振る舞う（案 B のスコープ外）。"
                        + "ハイブリッド構成での併用は未検証"
                ),
                RuntimeTarget.InMemory
            ),
            On(
                Gap(
                    Trigger,
                    "ミラー列をリモート面越しに往復させる専用テストは無い"
                        + "（HTTP 越しに実 rowversion を運ぶ経路自体は SyncSupport 行が押さえている）"
                ),
                RuntimeTarget.Remote
            )
        ),
        Row(
            RuntimeFeatures.ComputedColumn,
            "計算列・生成列（[ComputedColumn] による INSERT / UPDATE 除外・SELECT では読む）",
            On(
                NotApplicable(
                    "計算列フィクスチャはマルチターゲットで、EF Core とは生成時に排他。"
                        + "Fluent の ValueGeneratedOnAddOrUpdate は生成テキストの両アームテストが固定する"
                ),
                EfCoreAll
            ),
            On(
                NotApplicable(
                    "インメモリは式を評価しない＝計算列も普通のプロパティとして値を保つ（意図的な割り切り）"
                ),
                RuntimeTarget.InMemory
            ),
            On(
                Gap(
                    Trigger,
                    "リモート面越しの専用テストは無い（除外は転送経路でなく EntitySaveMetadata の側で効く）"
                ),
                RuntimeTarget.Remote
            )
        ),
        Row(
            RuntimeFeatures.IdentityColumn,
            "非主キーの自動採番列（[IdentityColumn] による INSERT / UPDATE 除外・EF Core の ValueGeneratedOnAddOrUpdate・SELECT では読む）",
            On(
                NotApplicable(
                    "自動採番列を持つ図は SQL Server 方言の図で、SQLite 単独の図に IDENTITY の概念が無い"
                        + "（除外は列自身の事実で方言に依らないため、SQL Server の実 DB で代表させている）"
                ),
                RuntimeTarget.AdoSqlite,
                RuntimeTarget.EfCoreSqlite
            ),
            On(Gap(Trigger, EfCoreUntested), RuntimeTarget.EfCoreMySql, RuntimeTarget.EfCoreOracle),
            On(
                NotApplicable(
                    "インメモリは採番を行わない＝自動採番列も普通のプロパティとして値を保つ（意図的な割り切り）"
                ),
                RuntimeTarget.InMemory
            )
        ),
        Row(
            RuntimeFeatures.SyncSupport,
            "双方向同期支援（差分ダウンロード・ジャーナル再生・削除伝搬・競合収集・ループ防止）",
            On(
                NotApplicable("同期支援はマルチターゲット前提で、EF Core とは生成時に排他"),
                EfCoreAll
            ),
            On(
                NotApplicable(
                    "同期はサーバー DB とローカル DB の 2 つの実 DB の間の話で、"
                        + "インメモリにはミラーすべきサーバー版を採番する主体が居ない"
                ),
                RuntimeTarget.InMemory
            )
        ),
        Row(
            RuntimeFeatures.SyncRefresh,
            "洗い替え（全消し＋サーバー全行の流し込み・未送信変更の拒否と force・FK 順・アンカーの継続）",
            On(
                NotApplicable("同期支援はマルチターゲット前提で、EF Core とは生成時に排他"),
                EfCoreAll
            ),
            On(
                NotApplicable(
                    "洗い替えはサーバー DB の全行でローカル DB を作り直す操作で、"
                        + "インメモリには作り直す先の実 DB が無い"
                ),
                RuntimeTarget.InMemory
            )
        ),
        Row(
            RuntimeFeatures.DialectPortability,
            "方言可搬フィクスチャの実 DB 実行（PostgreSQL / MySQL / Oracle / SQLite）",
            On(
                NotApplicable(
                    "可搬フィクスチャは EF Core 単独出力で生成しており QuickER 版 Repository を持たない"
                ),
                RuntimeTarget.AdoSqlServer,
                RuntimeTarget.AdoSqlite,
                RuntimeTarget.Remote
            ),
            On(
                Gap(
                    Trigger,
                    "可搬フィクスチャを SQL Server の実 DB で流すテストは無い"
                        + "（SQL Server の EF Core 版は SQL Server 方言のフィクスチャで CrudParity 行が持つ）"
                ),
                RuntimeTarget.EfCoreSqlServer
            ),
            On(
                NotApplicable(
                    "可搬フィクスチャは EF Core 単独出力で生成しており QuickER 版 Repository を持たない"
                        + "（インメモリは方言に依らないため可搬性の検証対象にならない）"
                ),
                RuntimeTarget.InMemory
            )
        ),
        Row(
            RuntimeFeatures.EditModelValidateUnique,
            "EditModel.ValidateUniqueAsync（DB 照合による重複検出）",
            On(
                Gap(
                    Trigger,
                    "DB 照合の実 DB 検証は QuickER 版（SQLite・SQL Server）だけ。照合 SQL は UniquenessCheck 側で 5 実装を通している"
                ),
                [.. EfCoreAll, RuntimeTarget.InMemory, RuntimeTarget.Remote]
            )
        ),
        Row(
            RuntimeFeatures.RemoteContract,
            "リモート契約生成（I{Entity}RemoteRepository の面と、その裏に居る直結実装）",
            On(
                Accepted(
                    "契約の面はエンジンに依らないため SQLite / EF Core の 2 実装で代表させている",
                    "src/QuickER.CodeGen.CSharp/CLAUDE.md「リモート契約生成」"
                ),
                [
                    RuntimeTarget.AdoSqlServer,
                    RuntimeTarget.EfCoreSqlServer,
                    .. EfCoreOthers,
                    RuntimeTarget.InMemory,
                ]
            ),
            On(
                NotApplicable("契約そのものは HTTP を経由しない（HTTP 側は RemoteTransport 行）"),
                RuntimeTarget.Remote
            )
        ),
        Row(
            RuntimeFeatures.RemoteTransport,
            "HTTP 転送層（health・エラー分類と詳細秘匿・応答本文・VO 本文・keyed 登録・認可の明示選択）",
            On(
                NotApplicable(
                    "転送層はサーバー背後のエンジンに依らない（サーバーの背後が QuickER 版と EF Core の 2 通りでも同じことは、CrudParity 行の RemoteService 系と実装先の一致のリモート 2 つの実装先が見る）"
                ),
                [
                    RuntimeTarget.AdoSqlServer,
                    RuntimeTarget.AdoSqlite,
                    .. EfCoreAll,
                    RuntimeTarget.InMemory,
                ]
            )
        ),
        Row(
            RuntimeFeatures.SqlExecutorInjection,
            "DI 登録した ISqlExecutor が生 SQL 経路へ実際に届くこと",
            On(
                Accepted(
                    "注入経路はテンプレート上で方言に依らないため SQLite で代表させている",
                    "src/QuickER.CodeGen.CSharp/CLAUDE.md「DB アクセスの排他と単独出力」"
                ),
                RuntimeTarget.AdoSqlServer
            ),
            On(Gap(Trigger, "EF Core 版も同じ省略可能引数を持つが実行時検証は無い"), EfCoreAll),
            On(NotApplicable("インメモリに生 SQL 経路が無い"), RuntimeTarget.InMemory),
            On(NotApplicable("生 SQL はリモート面に無い"), RuntimeTarget.Remote)
        ),
        Row(
            RuntimeFeatures.ForeignKeyDefault,
            "生成 SqlConnectionFactory が FK 検査を既定で有効にすること",
            On(
                NotApplicable("SQL Server は FK を常に検査するため既定の補完という概念が無い"),
                RuntimeTarget.AdoSqlServer
            ),
            On(NotApplicable("接続文字列の組み立ては QuickER 版 Repository の責務"), EfCoreAll),
            On(NotApplicable("インメモリに接続が無い"), RuntimeTarget.InMemory),
            On(NotApplicable("接続はサーバー側の構成"), RuntimeTarget.Remote)
        ),
        Row(
            RuntimeFeatures.BusyTimeoutDefault,
            "SQLite の接続を開くたびに busy timeout を待ち時間（Default Timeout・EF Core はコマンドタイムアウト）へ合わせること",
            On(
                NotApplicable(
                    "SQL Server のロック待ちは CommandTimeout が決め、接続に当てる設定が無い"
                ),
                RuntimeTarget.AdoSqlServer,
                RuntimeTarget.EfCoreSqlServer
            ),
            On(
                NotApplicable(
                    "busy timeout は SQLite の接続の設定で、PostgreSQL・MySQL・Oracle に当てる設定が無い"
                ),
                EfCoreOthers
            ),
            On(NotApplicable("インメモリに接続が無い"), RuntimeTarget.InMemory),
            On(NotApplicable("接続はサーバー側の構成"), RuntimeTarget.Remote)
        ),
        Row(
            RuntimeFeatures.ValueConversion,
            "SQLite の TEXT 値から IConvertible でない CLR 型（TimeSpan 等）への変換",
            On(
                NotApplicable("SQL Server は対応する SQL 型を持ち、この変換経路を通らない"),
                RuntimeTarget.AdoSqlServer
            ),
            On(NotApplicable("値変換は EF Core の ValueConverter の責務"), EfCoreAll),
            On(NotApplicable("インメモリは CLR 値をそのまま保持する"), RuntimeTarget.InMemory),
            On(NotApplicable("転送は JSON で、SQLite の格納表現に依らない"), RuntimeTarget.Remote)
        ),
    ];

    /// <summary>空きのセル</summary>
    private static RuntimeCellDeclaration Gap(string fixedIn, string reason) =>
        new(RuntimeCellKind.Gap, reason, FixedIn: fixedIn);

    /// <summary>割り切りのセル（根拠は <c>リポジトリの相対パス「見出し」</c>）</summary>
    private static RuntimeCellDeclaration Accepted(string reason, string basis) =>
        new(RuntimeCellKind.Accepted, reason, Basis: basis);

    /// <summary>非該当のセル</summary>
    private static RuntimeCellDeclaration NotApplicable(string reason) =>
        new(RuntimeCellKind.NotApplicable, reason);

    /// <summary>同じ宣言を複数の実装先へ敷く</summary>
    private static (RuntimeCellDeclaration Declaration, RuntimeTarget[] Targets) On(
        RuntimeCellDeclaration declaration,
        params RuntimeTarget[] targets
    ) => (declaration, targets);

    /// <summary>行を作る（同じ実装先を 2 度宣言したら断る）</summary>
    private static RuntimeFeatureRow Row(
        string feature,
        string description,
        params (RuntimeCellDeclaration Declaration, RuntimeTarget[] Targets)[] cells
    ) => Row(feature, description, cells, []);

    private static RuntimeFeatureRow Row(
        string feature,
        string description,
        (RuntimeCellDeclaration Declaration, RuntimeTarget[] Targets)[] cells,
        RuntimeMissingServer[] missingServers
    )
    {
        var declared = new Dictionary<RuntimeTarget, RuntimeCellDeclaration>();

        foreach (var (declaration, targets) in cells)
        {
            foreach (var target in targets)
            {
                if (!declared.TryAdd(target, declaration))
                {
                    throw new InvalidOperationException(
                        $"機能 {feature} の実装先 {target} を 2 度宣言している"
                    );
                }
            }
        }

        return new(feature, description, declared, missingServers);
    }
}
