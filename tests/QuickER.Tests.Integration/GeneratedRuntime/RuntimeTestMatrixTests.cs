using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AwesomeAssertions;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 生成コードの実行時テストを「機能 × バックエンド」のマトリクスとして宣言し、実在するテストクラスと突合するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 「機能 × 実装先」の空欄はバグの保管庫である（実際、名前付きクエリ × インメモリの空欄を 1 つ埋めた初回実行で
/// NULL 許容列の <c>NullReferenceException</c> が出た）。従来この空欄は「気づく人がいれば見つかる」状態でしか
/// 管理されていなかった。ここは<b>宣言しない限り赤くなる</b>状態へ変えるための網で、次の 2 方向を同時に固定する:
/// </para>
/// <list type="number">
///   <item>
///     <b>順方向</b>: 宣言した機能 × バックエンドの各セルは「担当するテストクラスが実在する」か
///     「理由つきの既知のギャップ／非該当」でなければならない。カバーされているセルへギャップ宣言が残っていても落ちる
///     （＝空欄を埋めたら宣言を消すことが強制される）。
///   </item>
///   <item>
///     <b>逆方向</b>: 走査規約に合致する実行時テストクラスがマトリクスへ現れなければ落ちる
///     （＝新機能・新バックエンドのテストを足したらマトリクス宣言の更新が強制される）。
///   </item>
/// </list>
/// <para>
/// <b>走査規約</b>（この規約自体が仕様なので、変えるならここのコメントも変えること）:
/// </para>
/// <list type="bullet">
///   <item>
///     名前空間が <c>QuickER.Tests.Integration.GeneratedRuntime</c> のもの<b>すべて</b>。
///     このディレクトリは「生成コードの実行時挙動検証」の住所そのものなので、クラス名の綴りには依存しない
///     （<c>*ParityTests</c> / <c>*SpecificTests</c> / <c>*KeyedResolutionTests</c> のように
///     <c>RuntimeTests</c> で終わらないものも実在する）。
///   </item>
///   <item>
///     名前空間が <c>QuickER.Tests.Generated…</c>（フィクスチャの名前空間群）のうち、
///     クラス名が <c>RuntimeTests</c> で終わるもの。フィクスチャ配下は単体寄りのテスト（Mapper・EditModel・
///     翻訳器・ドリフト）と実行時テストが同居しており、この接尾辞がリポジトリ自身の目印になっている。
///   </item>
///   <item>
///     いずれも<b>具象クラスかつ [Fact]/[Theory] を宣言または継承しているもの</b>だけ
///     （＝xUnit が実際に実行する単位）。共有基底（<c>*RuntimeTestsBase</c>）・ヘルパー・フィクスチャ定義・
///     生成物の型は自然に外れる。
///   </item>
/// </list>
/// <para>
/// バックエンドは実行時に生成コードが載る 5 実装（<see cref="Backend"/>）。1 クラスが複数セルを埋めることもあり
/// （マルチターゲットの実 DB テストは 2 方言、共有基底の派生は 1 方言）、その場合は複数の登録を書く。
/// </para>
/// </remarks>
public class RuntimeTestMatrixTests
{
    /// <summary>生成コードが載る実装先（マトリクスの列）</summary>
    private enum Backend
    {
        /// <summary>QuickER 版 Repository（SQL Server 方言・実コンテナ）</summary>
        AdoSqlServer,

        /// <summary>QuickER 版 Repository（SQLite 方言・実ファイル DB）</summary>
        AdoSqlite,

        /// <summary>EF Core 版 Repository</summary>
        EfCore,

        /// <summary>インメモリ Repository</summary>
        InMemory,

        /// <summary>HTTP クライアント＋生成サーバー（Kestrel を in-process 起動）</summary>
        Remote,
    }

    /// <summary>セルの期待（担当テストが実在する／既知のギャップ／非該当）</summary>
    private enum CellKind
    {
        /// <summary>担当するテストクラスが実在するべきセル</summary>
        Covered,

        /// <summary>まだ検証していないと分かっているセル（理由必須）</summary>
        Gap,

        /// <summary>その組合せが原理的に成立しないセル（理由必須）</summary>
        NotApplicable,
    }

    /// <summary>1 セルの宣言</summary>
    private sealed record Cell(CellKind Kind, string Reason = "");

    /// <summary>1 機能の行（説明＋バックエンドごとのセル）</summary>
    private sealed record FeatureRow(string Description, IReadOnlyDictionary<Backend, Cell> Cells);

    /// <summary>テストクラスがどのセルを埋めるかの登録（1 クラスが複数行に現れてよい）</summary>
    private sealed record Registration(string ClassName, string Feature, Backend Backend);

    /// <summary>担当テストが実在するべきセル</summary>
    private static Cell Covered() => new(CellKind.Covered);

    /// <summary>まだ検証していない（理由つき）セル</summary>
    private static Cell Gap(string reason) => new(CellKind.Gap, reason);

    /// <summary>原理的に成立しない（理由つき）セル</summary>
    private static Cell NotApplicable(string reason) => new(CellKind.NotApplicable, reason);

    /// <summary>5 バックエンドすべてに同じセルを敷く（行の既定値づくり用）</summary>
    private static Dictionary<Backend, Cell> Row(
        Cell adoSqlServer,
        Cell adoSqlite,
        Cell efCore,
        Cell inMemory,
        Cell remote
    ) =>
        new()
        {
            [Backend.AdoSqlServer] = adoSqlServer,
            [Backend.AdoSqlite] = adoSqlite,
            [Backend.EfCore] = efCore,
            [Backend.InMemory] = inMemory,
            [Backend.Remote] = remote,
        };

    /// <summary>機能 × バックエンドの期待マトリクス（この宣言が仕様であり、盲点地図でもある）</summary>
    private static readonly IReadOnlyDictionary<string, FeatureRow> Matrix = new Dictionary<
        string,
        FeatureRow
    >(StringComparer.Ordinal)
    {
        ["CrudParity"] = new(
            "CRUD・グラフ保存・Query() の共通シナリオ（パリティ基底が同一シナリオを各実装へ掛ける）",
            Row(Covered(), Covered(), Covered(), Covered(), Covered())
        ),
        ["Concurrency"] = new(
            "rowversion による楽観排他（版ガード・SaveConflictException の分類）",
            Row(
                Covered(),
                NotApplicable(
                    "SQLite 単独の図に rowversion 列は生まれない（timestamp は日時の別名へ解決される）ため、"
                        + "版ガードの対象そのものが存在しない。マルチターゲットでの非対称は MultiTargetRowVersion 行が持つ"
                ),
                Covered(),
                Covered(),
                Covered()
            )
        ),
        ["ConcurrencyVo"] = new(
            "楽観排他 × 値オブジェクト（版プロパティが VO 型になる経路の Wrap/Unwrap）",
            Row(
                Covered(),
                NotApplicable("Concurrency 行と同じ理由（SQLite 単独図に rowversion 列が無い）"),
                Covered(),
                Covered(),
                Covered()
            )
        ),
        ["SaveHook"] = new(
            "ISaveHook（Before のスキップ・After のトランザクション参加）",
            Row(Covered(), Covered(), Covered(), Covered(), Covered())
        ),
        ["BinaryColumn"] = new(
            "無制限バイナリ列の除外・WithUnboundedBinary・Stream アクセサ",
            Row(Covered(), Covered(), Covered(), Covered(), Covered())
        ),
        ["UniquenessCheck"] = new(
            "UNIQUE 制約の事前チェック（重複検出・自分自身の除外）",
            Row(Covered(), Covered(), Covered(), Covered(), Covered())
        ),
        ["NamedQuery"] = new(
            "名前付きクエリ（簡易 DSL・射影・手動実装）",
            Row(
                Gap(
                    "名前付きクエリの実 DB 検証は SQLite / EF Core / インメモリ / リモートのみ。"
                        + "DSL の翻訳は方言に依らず SqlExpressionTranslator が担い、SQL Server 固有経路は "
                        + "SqlServerQueryPipeline / TranslatorOperator が押さえているため優先度を下げている"
                ),
                Covered(),
                Covered(),
                Covered(),
                Covered()
            )
        ),
        ["NamedQueryRawSql"] = new(
            "名前付きクエリの生 SQL 実装（戻り形・パラメータ展開）",
            Row(
                Gap("NamedQuery 行と同じ理由（SQL Server 版の名前付きクエリ実行時テストが未整備）"),
                Covered(),
                Covered(),
                NotApplicable(
                    "生 SQL の実装先を持たないインメモリでは契約宣言のみが生成され、実装は利用者の partial 実装になる"
                        + "（「SQL が与えられていない実装先は手動実装」の統一規則）"
                ),
                Covered()
            )
        ),
        ["TranslatorOperator"] = new(
            "式木 → SQL 翻訳の演算子分岐（NULL 意味論の補償・bool 列の短縮形）",
            Row(
                Covered(),
                Covered(),
                NotApplicable(
                    "EF Core は式木を Where へ直接渡し、翻訳は EF Core の LINQ プロバイダが行う"
                        + "（SqlExpressionTranslator を通らない）"
                ),
                NotApplicable(
                    "インメモリは式木をコンパイルして C# 意味論で評価する（SQL を組み立てない）"
                ),
                NotApplicable("Query() はリモート面に無く、式木は転送されない")
            )
        ),
        ["SchemaBootstrap"] = new(
            "生成 DDL の適用ヘルパー（ApplyDdlAsync）",
            Row(
                Covered(),
                Covered(),
                NotApplicable(
                    "スキーマ作成は DDL 生成の責務で、EF Core は既存スキーマへの接続専用"
                ),
                NotApplicable("インメモリにスキーマの概念が無い"),
                NotApplicable("スキーマ適用はサーバー側の運用作業でリモート面に無い")
            )
        ),
        ["MultiTarget"] = new(
            "マルチターゲット生成（中立契約 1 回＋方言別実装・keyed DI）",
            Row(
                Covered(),
                Covered(),
                NotApplicable("マルチターゲットと EF Core は生成時に排他（診断エラー）"),
                Gap(
                    "インメモリとの併用はコンパイル行列で押さえているが、keyed DI に方言実装とインメモリを"
                        + "同居させる実行時テストは無い"
                ),
                Gap("リモート面との併用は直交だが、実行時の組合せ検証は無い")
            )
        ),
        ["MultiTargetRowVersion"] = new(
            "マルチターゲット × rowversion（SQL Server は書き込み除外＋版ガード・SQLite は通常列）",
            Row(
                Covered(),
                Covered(),
                NotApplicable("マルチターゲットと EF Core は生成時に排他"),
                Gap(
                    "インメモリは擬似版を採番する SQL Server の代役として振る舞う（案 B のスコープ外）。"
                        + "ハイブリッド構成での併用は未検証"
                ),
                Gap(
                    "ミラー列をリモート面越しに往復させる専用テストは無い"
                        + "（HTTP 越しに実 rowversion を運ぶ経路自体は SyncSupport 行が押さえている）"
                )
            )
        ),
        ["SyncSupport"] = new(
            "双方向同期支援（差分ダウンロード・ジャーナル再生・削除伝搬・競合収集・ループ防止）",
            Row(
                Covered(),
                Covered(),
                NotApplicable("同期支援はマルチターゲット前提で、EF Core とは生成時に排他"),
                NotApplicable(
                    "同期はサーバー DB とローカル DB の 2 つの実 DB の間の話で、"
                        + "インメモリにはミラーすべきサーバー版を採番する主体が居ない"
                ),
                Covered()
            )
        ),
        ["SyncRefresh"] = new(
            "洗い替え（全消し＋サーバー全行の流し込み・未送信変更の拒否と force・FK 順・アンカーの継続）",
            Row(
                Covered(),
                Covered(),
                NotApplicable("同期支援はマルチターゲット前提で、EF Core とは生成時に排他"),
                NotApplicable(
                    "洗い替えはサーバー DB の全行でローカル DB を作り直す操作で、"
                        + "インメモリには作り直す先の実 DB が無い"
                ),
                Covered()
            )
        ),
        ["DialectPortability"] = new(
            "方言可搬フィクスチャの実 DB 実行（PostgreSQL / MySQL / Oracle / SQLite）",
            Row(
                NotApplicable(
                    "可搬フィクスチャは EF Core 単独出力で生成しており QuickER 版 Repository を持たない"
                ),
                NotApplicable("同上"),
                Covered(),
                NotApplicable("同上（インメモリは方言に依らないため可搬性の検証対象にならない）"),
                NotApplicable("同上")
            )
        ),
        ["EditModelValidateUnique"] = new(
            "EditModel.ValidateUniqueAsync（DB 照合による重複検出）",
            Row(
                Gap(
                    "DB 照合の実 DB 検証は SQLite のみ。照合 SQL は UniquenessCheck 側で 5 実装を通している"
                ),
                Covered(),
                Gap("同上"),
                Gap("同上"),
                Gap("同上")
            )
        ),
        ["RemoteContract"] = new(
            "リモート契約生成（I{Entity}RemoteRepository の面と、その裏に居る直結実装）",
            Row(
                Gap("契約の面はエンジンに依らないため SQLite / EF Core の 2 実装で代表させている"),
                Covered(),
                Covered(),
                Gap("同上"),
                NotApplicable("契約そのものは HTTP を経由しない（HTTP 側は RemoteTransport 行）")
            )
        ),
        ["RemoteTransport"] = new(
            "HTTP 転送層（health・エラー分類と詳細秘匿・応答本文・VO 本文・keyed 登録・認可の明示選択）",
            Row(
                NotApplicable(
                    "転送層はサーバー背後のエンジンに依らない（サーバー側実装は Ado/EfCore の 2 通りで実証）"
                ),
                NotApplicable("同上"),
                NotApplicable("同上"),
                NotApplicable("同上"),
                Covered()
            )
        ),
        ["SqlExecutorInjection"] = new(
            "DI 登録した ISqlExecutor が生 SQL 経路へ実際に届くこと",
            Row(
                Gap("注入経路はテンプレート上で方言に依らないため SQLite で代表させている"),
                Covered(),
                Gap("EF Core 版も同じ省略可能引数を持つが実行時検証は無い"),
                NotApplicable("インメモリに生 SQL 経路が無い"),
                NotApplicable("生 SQL はリモート面に無い")
            )
        ),
        ["ForeignKeyDefault"] = new(
            "生成 SqlConnectionFactory が FK 検査を既定で有効にすること",
            Row(
                NotApplicable("SQL Server は FK を常に検査するため既定の補完という概念が無い"),
                Covered(),
                NotApplicable("接続文字列の組み立ては QuickER 版 Repository の責務"),
                NotApplicable("インメモリに接続が無い"),
                NotApplicable("接続はサーバー側の構成")
            )
        ),
        ["ValueConversion"] = new(
            "SQLite の TEXT 値から IConvertible でない CLR 型（TimeSpan 等）への変換",
            Row(
                NotApplicable("SQL Server は対応する SQL 型を持ち、この変換経路を通らない"),
                Covered(),
                NotApplicable("値変換は EF Core の ValueConverter の責務"),
                NotApplicable("インメモリは CLR 値をそのまま保持する"),
                NotApplicable("転送は JSON で、SQLite の格納表現に依らない")
            )
        ),
    };

    /// <summary>テストクラス → 埋めるセルの登録一覧</summary>
    private static readonly IReadOnlyList<Registration> Registrations =
    [
        // --- CRUD パリティ ---
        new("GeneratedRuntimeAdoParityTests", "CrudParity", Backend.AdoSqlServer),
        new("SqlServerQueryPipelineRuntimeTests", "CrudParity", Backend.AdoSqlServer),
        new("GeneratedSqliteAdoRuntimeTests", "CrudParity", Backend.AdoSqlite),
        new("GeneratedRuntimeEfCoreParityTests", "CrudParity", Backend.EfCore),
        new("GeneratedRuntimeEfCoreSpecificTests", "CrudParity", Backend.EfCore),
        new("GeneratedSqliteEfCoreParityRuntimeTests", "CrudParity", Backend.EfCore),
        new("InMemoryFixtureRuntimeTests", "CrudParity", Backend.InMemory),
        new("RemoteServiceAdoRuntimeTests", "CrudParity", Backend.Remote),
        new("RemoteServiceEfCoreRuntimeTests", "CrudParity", Backend.Remote),
        // --- 楽観排他 ---
        new("SqlServerConcurrencyRuntimeTests", "Concurrency", Backend.AdoSqlServer),
        new("EfCoreConcurrencyRuntimeTests", "Concurrency", Backend.EfCore),
        new("InMemoryConcurrencyRuntimeTests", "Concurrency", Backend.InMemory),
        new("SaveConflictDetailsRuntimeTests", "Concurrency", Backend.InMemory),
        new("RemoteConcurrencyRuntimeTests", "Concurrency", Backend.Remote),
        new("ConcurrencyVoSqlServerRuntimeTests", "ConcurrencyVo", Backend.AdoSqlServer),
        new("ConcurrencyVoEfCoreRuntimeTests", "ConcurrencyVo", Backend.EfCore),
        new("ConcurrencyVoInMemoryRuntimeTests", "ConcurrencyVo", Backend.InMemory),
        new("ConcurrencyVoRemoteRuntimeTests", "ConcurrencyVo", Backend.Remote),
        // --- Save フック ---
        new("SaveHookSqlServerRuntimeTests", "SaveHook", Backend.AdoSqlServer),
        new("SaveHookAdoRuntimeTests", "SaveHook", Backend.AdoSqlite),
        new("SaveHookEfCoreRuntimeTests", "SaveHook", Backend.EfCore),
        new("SaveHookInMemoryRuntimeTests", "SaveHook", Backend.InMemory),
        new("SaveHookRegistrationRuntimeTests", "SaveHook", Backend.InMemory),
        new("SaveHookRemoteRuntimeTests", "SaveHook", Backend.Remote),
        // --- 無制限バイナリ列 ---
        new("SqlServerBinaryColumnRuntimeTests", "BinaryColumn", Backend.AdoSqlServer),
        new("BinaryColumnAdoRuntimeTests", "BinaryColumn", Backend.AdoSqlite),
        new("BinaryColumnEfCoreRuntimeTests", "BinaryColumn", Backend.EfCore),
        new("BinaryInMemoryFixtureRuntimeTests", "BinaryColumn", Backend.InMemory),
        new("BinaryColumnRemoteRuntimeTests", "BinaryColumn", Backend.Remote),
        // --- 一意性の事前チェック ---
        new("UniquenessCheckSqlServerRuntimeTests", "UniquenessCheck", Backend.AdoSqlServer),
        new("UniquenessCheckAdoRuntimeTests", "UniquenessCheck", Backend.AdoSqlite),
        new("UniquenessCheckEfCoreRuntimeTests", "UniquenessCheck", Backend.EfCore),
        new("UniquenessCheckInMemoryRuntimeTests", "UniquenessCheck", Backend.InMemory),
        new("UniquenessCheckRemoteRuntimeTests", "UniquenessCheck", Backend.Remote),
        // --- 名前付きクエリ ---
        new("NamedQueryAdoRuntimeTests", "NamedQuery", Backend.AdoSqlite),
        new("NamedQueryAdoRuntimeTests", "NamedQueryRawSql", Backend.AdoSqlite),
        new("SqlitePortableNamedQueryRuntimeTests", "NamedQuery", Backend.AdoSqlite),
        new("NamedQueryEfCoreRuntimeTests", "NamedQuery", Backend.EfCore),
        new("NamedQueryEfCoreRuntimeTests", "NamedQueryRawSql", Backend.EfCore),
        new("NamedQueryInMemoryRuntimeTests", "NamedQuery", Backend.InMemory),
        new("RemoteServiceAdoRuntimeTests", "NamedQuery", Backend.Remote),
        new("RemoteServiceAdoRuntimeTests", "NamedQueryRawSql", Backend.Remote),
        // --- 式木翻訳 ---
        new("SqlServerTranslatorOperatorRuntimeTests", "TranslatorOperator", Backend.AdoSqlServer),
        new(
            "SqlServerBoolColumnTranslatorRuntimeTests",
            "TranslatorOperator",
            Backend.AdoSqlServer
        ),
        new("SqliteTranslatorOperatorRuntimeTests", "TranslatorOperator", Backend.AdoSqlite),
        new("SqliteBoolColumnTranslatorRuntimeTests", "TranslatorOperator", Backend.AdoSqlite),
        // --- スキーマ適用 ---
        new("SqlServerSchemaBootstrapRuntimeTests", "SchemaBootstrap", Backend.AdoSqlServer),
        new("SqliteSchemaBootstrapRuntimeTests", "SchemaBootstrap", Backend.AdoSqlite),
        // --- マルチターゲット ---
        new("MultiTargetRepositoryRuntimeTests", "MultiTarget", Backend.AdoSqlServer),
        new("MultiTargetRepositoryRuntimeTests", "MultiTarget", Backend.AdoSqlite),
        new("MultiTargetRepositorySqliteKeyedResolutionTests", "MultiTarget", Backend.AdoSqlite),
        new("MultiTargetRowVersionRuntimeTests", "MultiTargetRowVersion", Backend.AdoSqlServer),
        new("MultiTargetRowVersionRuntimeTests", "MultiTargetRowVersion", Backend.AdoSqlite),
        new("MultiTargetRowVersionSqliteRuntimeTests", "MultiTargetRowVersion", Backend.AdoSqlite),
        new("SyncSqliteRuntimeTests", "SyncSupport", Backend.AdoSqlite),
        new("SyncSqlServerRuntimeTests", "SyncSupport", Backend.AdoSqlServer),
        new("SyncHttpRuntimeTests", "SyncSupport", Backend.Remote),
        new("SyncSqlServerHttpRuntimeTests", "SyncSupport", Backend.Remote),
        // 同期エンドポイントのマップ時 fail-fast＝ソース未登録の起動時検出
        new("SyncEndpointRegistrationRuntimeTests", "SyncSupport", Backend.Remote),
        // 構築時除外（excludeFromSync）＝ローカル専用宣言の 2 点セットと登録時 fail-fast
        new("SyncConstructionExclusionTests", "SyncSupport", Backend.AdoSqlite),
        new("SyncSqliteRuntimeTests", "SyncRefresh", Backend.AdoSqlite),
        new("SyncRefreshBenchmarkRuntimeTests", "SyncRefresh", Backend.AdoSqlite),
        new("SyncSqlServerRuntimeTests", "SyncRefresh", Backend.AdoSqlServer),
        new("SyncHttpRuntimeTests", "SyncRefresh", Backend.Remote),
        // --- 方言可搬性 ---
        new("GeneratedEfCoreSqliteRuntimeTests", "DialectPortability", Backend.EfCore),
        new("GeneratedEfCorePostgreSqlRuntimeTests", "DialectPortability", Backend.EfCore),
        new("GeneratedEfCoreMySqlRuntimeTests", "DialectPortability", Backend.EfCore),
        new("GeneratedEfCoreOracleRuntimeTests", "DialectPortability", Backend.EfCore),
        // --- EditModel の DB 照合 ---
        new("EditModelValidateUniqueRuntimeTests", "EditModelValidateUnique", Backend.AdoSqlite),
        // --- リモート ---
        new("RemoteContractRuntimeTests", "RemoteContract", Backend.AdoSqlite),
        new("RemoteContractRuntimeTests", "RemoteContract", Backend.EfCore),
        new("RemoteHealthRuntimeTests", "RemoteTransport", Backend.Remote),
        new("RemoteErrorDetailRuntimeTests", "RemoteTransport", Backend.Remote),
        new("RemoteResponseBodyRuntimeTests", "RemoteTransport", Backend.Remote),
        new("RemoteValueObjectBodyRuntimeTests", "RemoteTransport", Backend.Remote),
        new("RemoteKeyedRegistrationRuntimeTests", "RemoteTransport", Backend.Remote),
        new("RemoteAccessRuntimeTests", "RemoteTransport", Backend.Remote),
        new("SaveConflictDetailsRuntimeTests", "Concurrency", Backend.Remote),
        // --- その他の単発機能 ---
        new("SqlExecutorInjectionRuntimeTests", "SqlExecutorInjection", Backend.AdoSqlite),
        new("SqliteForeignKeyDefaultRuntimeTests", "ForeignKeyDefault", Backend.AdoSqlite),
        new("SqliteValueConversionRuntimeTests", "ValueConversion", Backend.AdoSqlite),
    ];

    /// <summary>走査規約に合致する実行時テストクラス（xUnit が実際に実行する単位）を列挙する</summary>
    private static IReadOnlyList<Type> RuntimeTestClasses() =>
        typeof(RuntimeTestMatrixTests)
            .Assembly.GetTypes()
            .Where(IsRuntimeTestClass)
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToList();

    /// <summary>走査規約（クラス XmlDoc の「走査規約」節と 1:1）</summary>
    private static bool IsRuntimeTestClass(Type type)
    {
        if (type is not { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false })
        {
            return false;
        }

        if (!HasTestMethods(type))
        {
            return false;
        }

        var ns = type.Namespace ?? string.Empty;

        // (1) Integration/GeneratedRuntime 配下は綴りに依らず全部
        if (string.Equals(ns, typeof(RuntimeTestMatrixTests).Namespace, StringComparison.Ordinal))
        {
            // このマトリクス自身は検証対象ではない（生成コードを動かしていない）
            return type != typeof(RuntimeTestMatrixTests);
        }

        // (2) フィクスチャの名前空間群からは *RuntimeTests だけ
        return ns.StartsWith("QuickER.Tests.Generated", StringComparison.Ordinal)
            && type.Name.EndsWith("RuntimeTests", StringComparison.Ordinal);
    }

    /// <summary>[Fact] / [Theory] を宣言または継承しているか（xUnit が実行する単位かどうか）</summary>
    private static bool HasTestMethods(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
            .Any(method =>
                method.GetCustomAttributes(typeof(FactAttribute), inherit: true).Length > 0
            );

    /// <summary>
    /// 走査規約に合致する実行時テストクラスがすべてマトリクスへ登録されていることを検証する（逆方向）。
    /// </summary>
    [Fact(
        DisplayName = "実行時テストクラスがすべて機能 × バックエンドのマトリクスへ登録されている"
    )]
    public void EveryRuntimeTestClass_ShouldAppearInMatrix()
    {
        var registered = Registrations
            .Select(registration => registration.ClassName)
            .ToHashSet(StringComparer.Ordinal);
        var actual = RuntimeTestClasses().Select(type => type.Name).ToList();

        var unregistered = actual.Where(name => !registered.Contains(name)).ToList();
        var stale = registered.Where(name => !actual.Contains(name)).ToList();

        unregistered
            .Should()
            .BeEmpty(
                "マトリクスへ未登録の実行時テストクラスがある"
                    + "（RuntimeTestMatrixTests.Registrations へ「機能 × バックエンド」を宣言すること）: "
                    + string.Join(", ", unregistered)
            );
        stale
            .Should()
            .BeEmpty(
                "実在しないテストクラスの登録が残っている（改名・削除に追随していない）: "
                    + string.Join(", ", stale.OrderBy(name => name, StringComparer.Ordinal))
            );

        // 走査規約が壊れて 0 件になると、上の 2 表明はどちらも空振りで緑になる（登録側も同時に空でない限り）
        actual
            .Should()
            .HaveCountGreaterThan(
                40,
                "走査が実行時テストクラスを見つけられていない（走査規約が名前空間・属性の変更で空振りしている）"
            );
    }

    /// <summary>
    /// 宣言した機能 × バックエンドの各セルが「担当テストが実在する」か「理由つきのギャップ／非該当」であることを検証する（順方向）。
    /// </summary>
    [Fact(
        DisplayName = "機能 × バックエンドの各セルが担当テストか理由つきギャップのどちらかで埋まっている"
    )]
    public void EveryMatrixCell_ShouldBeCoveredOrDeclaredAsGap()
    {
        var existing = RuntimeTestClasses()
            .Select(type => type.Name)
            .ToHashSet(StringComparer.Ordinal);

        var coveredCells = Registrations
            .Where(registration => existing.Contains(registration.ClassName))
            .Select(registration => (registration.Feature, registration.Backend))
            .ToHashSet();

        var problems = new List<string>();

        foreach (var (feature, row) in Matrix.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            foreach (var backend in Enum.GetValues<Backend>())
            {
                if (!row.Cells.TryGetValue(backend, out var cell))
                {
                    problems.Add(
                        $"[{feature} × {backend}] セルが未宣言（Covered / Gap / NotApplicable のいずれかを書くこと）"
                    );

                    continue;
                }

                var covered = coveredCells.Contains((feature, backend));

                if (cell.Kind == CellKind.Covered && !covered)
                {
                    problems.Add(
                        $"[{feature} × {backend}] は担当テストが実在するべき（Covered）だが、"
                            + "そのセルを埋めるテストクラスが 1 つも登録されていない"
                    );
                }
                else if (cell.Kind != CellKind.Covered && covered)
                {
                    problems.Add(
                        $"[{feature} × {backend}] は {cell.Kind}（理由: {cell.Reason}）と宣言されているが、"
                            + "実際には担当テストが登録されている。空欄が埋まったので宣言を Covered へ変えること"
                    );
                }

                if (cell.Kind != CellKind.Covered && string.IsNullOrWhiteSpace(cell.Reason))
                {
                    problems.Add(
                        $"[{feature} × {backend}] のギャップ／非該当に理由が書かれていない"
                    );
                }
            }
        }

        // 登録が指す機能名がマトリクスに存在すること（綴り違いで黙って新しい機能が生えるのを防ぐ）
        foreach (
            var feature in Registrations
                .Select(registration => registration.Feature)
                .Distinct(StringComparer.Ordinal)
                .Where(feature => !Matrix.ContainsKey(feature))
        )
        {
            problems.Add($"登録が参照する機能 '{feature}' がマトリクスに宣言されていない");
        }

        problems
            .Should()
            .BeEmpty(
                "機能 × バックエンドのマトリクスに未解決のセルがある:"
                    + Environment.NewLine
                    + string.Join(Environment.NewLine, problems)
            );
    }

    /// <summary>
    /// マトリクスが「空欄の一覧」として意味を保っていること（全セルがギャップ宣言で埋まる退化を防ぐ）を検証する。
    /// </summary>
    /// <remarks>
    /// ギャップ宣言は空欄を可視化するためのもので、面倒になったら全部ギャップにする、という逃げ道を塞ぐ。
    /// カバー済みセルが過半を大きく割り込んだら、それは網ではなく言い訳の一覧になっている。
    /// </remarks>
    [Fact(DisplayName = "マトリクスのカバー済みセルが過半を占める")]
    public void Matrix_ShouldBeMostlyCovered()
    {
        var cells = Matrix.Values.SelectMany(row => row.Cells.Values).ToList();
        var covered = cells.Count(cell => cell.Kind == CellKind.Covered);

        cells.Should().HaveCount(Matrix.Count * Enum.GetValues<Backend>().Length);
        covered
            .Should()
            .BeGreaterThan(
                cells.Count / 2,
                "カバー済みセルが半数以下なら、マトリクスは網ではなくギャップ宣言の一覧に退化している"
            );
    }
}
