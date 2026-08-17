using QuickER.CodeGen.CSharp.Resources;

namespace QuickER.CodeGen.CSharp;

/// <summary>
/// C# コード生成の動作を制御するオプション
/// </summary>
/// <remarks>
/// 生成対象（Entity / EditModel / Mapper / Repository）の選択と、
/// 出力先・属性付与の有無を指定する。全プロパティは <c>init</c> 専用で、生成中に変化しない。
/// <c>record</c> なのは <c>with</c> 式で「1 項目だけ変えた設定」を作れるようにするため
/// （手書きの全プロパティ複製はプロパティ追加時に写し漏れて、その構成が黙って未検証になる）。
/// </remarks>
public sealed record CodeGenerationOptions
{
    /// <summary>生成コードを配置するルート名前空間。空白の場合はビルダー側で既定値 "Generated" にフォールバックする</summary>
    public string RootNamespace { get; init; } = "Generated";

    /// <summary>出力ファイル名。".g.cs" で終わらない場合はサービス側で補正される</summary>
    public string OutputFileName { get; init; } = "QuickEREntities.g.cs";

    /// <summary>WPF バインディング向けの EditModel クラスを生成するかどうか</summary>
    public bool GenerateEditModels { get; init; } = true;

    /// <summary>Entity と EditModel を相互変換する Mapper クラスを生成するかどうか</summary>
    public bool GenerateMappers { get; init; } = true;

    /// <summary>QuickER の SQL Server 実装（<c>Microsoft.Data.SqlClient</c> 依存）の Repository クラス群を生成するかどうか（既定 false）</summary>
    /// <remarks>
    /// SqlServerRepository 基底・各エンティティ実装・接続ファクトリ・SqlExecutor・SqlExpressionTranslator・
    /// エンジン別 DI 拡張 <c>AddGenerated{方言}Repositories</c> を生成する。共通契約（インターフェイス・SqlQuery・メタデータ等）は
    /// <see cref="GenerateEfCore"/> と共有し、どちらか一方が ON なら生成される。
    /// 既定では DB アクセスコードを生成しない（GUI の DB アクセス「なし」と同じ既定）
    /// </remarks>
    public bool GenerateRepositories { get; init; }

    /// <summary>
    /// QuickER 版 Repository を生成する方言の一覧（複数指定で 1 回の生成に複数方言実装を同梱する）。
    /// </summary>
    /// <remarks>
    /// <c>null</c> または空のときは既定 <c>"sqlserver"</c> の単一へフォールバックする。実効値の解決・正規化
    /// （重複排除・未対応方言の検証）は <see cref="EffectiveRepositoryDialects"/> に 1 箇所へ集約する。
    /// 対応方言は <see cref="SupportedRepositoryDialects"/> を参照（GUI / CLI 共通）。
    /// </remarks>
    public IReadOnlyList<string>? RepositoryDialects { get; init; }

    /// <summary>
    /// 実効的なQuickER 版 Repository 生成方言の一覧を解決する（唯一の正）。
    /// </summary>
    /// <remarks>
    /// 解決規則:
    /// <list type="number">
    ///   <item><see cref="RepositoryDialects"/> が非空ならそれを、空/未指定なら既定 <c>"sqlserver"</c> の単一を採る</item>
    ///   <item>各要素を Trim し、空要素は除去する</item>
    ///   <item>大文字小文字を無視して重複を除去する（初出の表記を保持し、指定順を維持する）</item>
    ///   <item>未対応方言（<see cref="SupportedRepositoryDialects"/> 外）が含まれる場合は <see cref="ArgumentException"/> を投げる</item>
    ///   <item>結果が空になった場合は既定 <c>"sqlserver"</c> の単一を返す（従来挙動の保険）</item>
    /// </list>
    /// </remarks>
    public IReadOnlyList<string> EffectiveRepositoryDialects
    {
        get
        {
            var source = RepositoryDialects is { Count: > 0 } ? RepositoryDialects : ["sqlserver"];

            var resolved = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var raw in source)
            {
                var value = raw?.Trim();

                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                if (!SupportedRepositoryDialects.Contains(value, StringComparer.OrdinalIgnoreCase))
                {
                    throw new ArgumentException(
                        string.Format(
                            Strings.CodeGen_Error_UnsupportedRepositoryDialect,
                            value,
                            string.Join(", ", SupportedRepositoryDialects)
                        )
                    );
                }

                if (seen.Add(value))
                {
                    resolved.Add(value);
                }
            }

            return resolved.Count > 0 ? resolved : ["sqlserver"];
        }
    }

    /// <summary>
    /// QuickER 版 Repository が対応する生成方言の一覧（プロバイダ名と同一の識別子。例: <c>"sqlserver"</c>, <c>"sqlite"</c>）。
    /// </summary>
    /// <remarks>
    /// GUI（生成ダイアログの選択可否判定）と CLI（未対応方言の早期エラー）が単一ソースとして参照する。
    /// PostgreSQL / MySQL / Oracle は将来対応予定のためここには含めない。<c>QuickER.CodeGen.CSharp</c> は DB 非依存を保つため、
    /// ここに置くのは文字列識別子の一覧のみで、各プロバイダの実装や型情報は一切参照しない。
    /// </remarks>
    public static IReadOnlyList<string> SupportedRepositoryDialects { get; } =
    ["sqlserver", "sqlite"];

    /// <summary>
    /// リモート操作用の Repository インターフェイス（<c>I{Entity}RemoteRepository</c>）を追加生成するかどうか（既定 false）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>true</c> のとき、ネットワーク境界を越えられる操作（CRUD・保存・名前付きクエリ）だけを持つ
    /// <c>I{Entity}RemoteRepository</c>（<see cref="IRemoteRepository{TEntity, TKey}"/> 相当の基底を継承）を追加生成し、
    /// 既存の <c>I{Entity}Repository</c> はそれを継承する全機能面（従来どおり <c>Query()</c>・生 SQL・一括追加も持つ）になる。
    /// 純粋に追加的な変更のため、ON にしても既存の利用コードは一切壊れない。
    /// </para>
    /// <para>
    /// アプリ本体がリモート面だけに依存すれば、将来その実体を Web サービス経由の実装（3 階層化）へ差し替えても
    /// コンパイル時に安全が保証される。DI はリモート面を同一実装インスタンスへの転送として追加登録する
    /// （keyed 版も同様）。実装クラス・ランタイムエンジンに分岐はなく、間仕切りは純粋にインターフェイス水準。
    /// </para>
    /// </remarks>
    public bool GenerateRemoteContracts { get; init; }

    /// <summary>
    /// リモート面を HTTP + JSON で提供するクライアント／サーバー実装を生成するかどうか（既定 false）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>true</c> のとき、(1) リモート面の HTTP クライアント実装（<c>Http{Entity}RemoteRepository</c>＝BCL の
    /// <c>HttpClient</c> のみ使用・<c>AddGeneratedHttpRemoteRepositories</c> で DI 登録）を本体生成物へ同梱し、
    /// (2) ASP.NET Core Minimal API のサーバー実装（<c>MapGeneratedRemoteEndpoints</c>）を別ファイル
    /// <c>{ベース名}.RemoteServer.g.cs</c> へ追加出力する。サーバーファイルは ASP.NET Core の
    /// FrameworkReference（<c>Microsoft.AspNetCore.App</c>）を持つプロジェクトに置くこと。
    /// </para>
    /// <para>
    /// リモート面（<see cref="GenerateRemoteContracts"/>）が前提のため、本オプション ON はリモート面の生成を自動的に含意する。
    /// 直列化はランタイム既存の JSON 設定（VO コンバータ・RowState 込み）を使い、
    /// <c>SaveConflictException</c> は HTTP 409 を介して型ごと復元される（直結⇔リモートで catch が変わらない）。
    /// </para>
    /// </remarks>
    public bool GenerateRemoteServices { get; init; }

    /// <summary>
    /// EF Core 用コード（DbContext・Fluent API 構成・EF Core 版 Repository 実装）を生成するかどうか。
    /// </summary>
    /// <remarks>
    /// 生成される DbContext は既存 Entity をそのまま既存スキーマへ接続する用途（方言非依存・1 本）で、
    /// スキーマ作成（Migrations / EnsureCreated）は範囲外とする。<see cref="GenerateRepositories"/> とは独立に選べ、
    /// EF Core 単独出力時はQuickER の SQL Server 実装（<c>Microsoft.Data.SqlClient</c> 依存）を一切含まない。
    /// 共通契約（インターフェイス・SqlQuery・メタデータ等）は <see cref="GenerateRepositories"/> と共有する
    /// </remarks>
    public bool GenerateEfCore { get; init; }

    /// <summary>
    /// DB 非依存のインメモリ Repository 群（<c>InMemory{Entity}Repository</c>・<c>InMemoryDataStore</c>・
    /// <c>InMemorySampleData</c>・<c>AddGeneratedInMemoryRepositories</c>）を生成するかどうか（既定 false）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 実 DB を使わず、共通契約（<c>I{Entity}Repository</c> / <c>IRepository</c> / <c>SqlQuery</c> 等）と同じ API を
    /// メモリ上の辞書で満たす。プロトタイピング・UI 検証・単体テスト向けで、生 SQL 系メソッドは
    /// <see cref="NotSupportedException"/> を投げる（実 DB の Repository へ切り替える案内）。共通契約は
    /// <see cref="GenerateRepositories"/> / <see cref="GenerateEfCore"/> と共有する（どれか一つでも ON なら契約を生成）。
    /// </para>
    /// <para>
    /// 方言に依存しないため、QuickER 版 Repository のマルチターゲット・<see cref="GenerateEfCore"/>・
    /// <see cref="UseRuntimePackages"/> のいずれとも併用できる（パッケージ参照モードではインメモリ基盤の固定 infra を
    /// <c>QuickER.Runtime.InMemory</c> パッケージが担い、per-entity 実装・シーダー・DI 登録だけが生成側に残る）。
    /// </para>
    /// </remarks>
    public bool GenerateInMemoryRepositories { get; init; }

    /// <summary>
    /// サーバー（SQL Server）とローカル（SQLite）のハイブリッド構成で双方向の差分同期を行う支援コード
    /// （同期エンジン・同期記述子・ジャーナル記録デコレータ・差分ソース・DI 登録）を生成するかどうか（既定 false）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>true</c> のとき、<see cref="RepositoryDialects"/> がちょうど <c>"sqlserver"</c> と <c>"sqlite"</c> の 2 方言で、
    /// かつ <c>rowversion</c>（<c>timestamp</c>）列を持つテーブルが 1 つ以上あることを要求する（満たさない指定は生成時の診断エラー）。
    /// 同期対象はその <c>rowversion</c> 列を持つテーブルだけで、「列の有無がそのままポリシー」という楽観排他と同じ流儀に従う。
    /// </para>
    /// <para>
    /// サーバー側には追加スキーマを一切作らない。ローカル（SQLite）にだけ共有ジャーナル 1 テーブル
    /// （<c>quicker_sync_journal</c>）を実行時に <c>CREATE TABLE IF NOT EXISTS</c> で用意し、オフライン編集を記録する。
    /// 差分の再開点（アンカー）は保存せず、ローカルのミラー版列の <c>MAX</c> から導出する。
    /// </para>
    /// <para>
    /// <see cref="GenerateRepositories"/> が前提（QuickER 版 Repository の実装が同期の読み書き経路になる）。
    /// <see cref="GenerateEfCore"/> とはマルチターゲットの排他規則により自動的に併用できない。
    /// </para>
    /// </remarks>
    public bool GenerateSyncSupport { get; init; }

    /// <summary>Repository 契約（共通契約バケット）の生成が必要か（QuickER 版 / EF Core / インメモリのいずれかが有効）</summary>
    public bool GeneratesRepositoryContract =>
        GenerateRepositories || GenerateEfCore || GenerateInMemoryRepositories;

    /// <summary>[Table] [Key] [Column] [Required] [MaxLength] などのデータアノテーション属性を付与するかどうか</summary>
    public bool IncludeDataAnnotations { get; init; } = true;

    /// <summary>親参照ナビゲーションへ [JsonIgnore] を付与するかどうか（JSON シリアライズ時の循環参照対策）</summary>
    public bool IncludeJsonIgnoreOnParentNavigation { get; init; } = true;

    /// <summary>全カラムを値オブジェクト（Value Object）として生成するかどうか。ON で Entity/EditModel/Mapper/Repository のプロパティ型が VO になる</summary>
    public bool GenerateValueObjects { get; init; }

    /// <summary>string 型の主キーを GuidKey 値オブジェクト（GUID を文字列保持・無引数生成で自動採番）にするかどうか。<see cref="GenerateValueObjects"/> が ON かつ PK が string のときのみ適用</summary>
    public bool UseGuidKeyForStringPrimaryKey { get; init; }

    /// <summary>
    /// 出力をカテゴリ（Entity / EditModel / Mapper / Repository / ValueObject / Runtime）ごとに別ファイル・別名前空間へ分割するかどうか
    /// </summary>
    /// <remarks>
    /// false（既定）: 全クラスを <see cref="RootNamespace"/> の単一ファイル（<see cref="OutputFileName"/>）へ出力する（従来動作）。
    /// true: 生成対象カテゴリと共有基盤（Runtime）をそれぞれ 1 カテゴリ 1 ファイルへ出力し、各ファイルに個別の名前空間を与える
    /// </remarks>
    public bool SplitFilesByCategory { get; init; }

    /// <summary>分割時の共有基盤（基底クラス・属性・VO 基底・JSON コンバータ）の名前空間。空なら <c>{RootNamespace}.Runtime</c> へフォールバックする</summary>
    public string? RuntimeNamespace { get; init; }

    /// <summary>分割時の Entity クラスの名前空間。空なら <see cref="RootNamespace"/> へフォールバックする</summary>
    public string? EntityNamespace { get; init; }

    /// <summary>分割時の EditModel クラスの名前空間。空なら <see cref="RootNamespace"/> へフォールバックする</summary>
    public string? EditModelNamespace { get; init; }

    /// <summary>分割時の Mapper クラスの名前空間。空なら <see cref="RootNamespace"/> へフォールバックする</summary>
    public string? MapperNamespace { get; init; }

    /// <summary>分割時の Repository クラス群の名前空間。空なら <see cref="RootNamespace"/> へフォールバックする</summary>
    public string? RepositoryNamespace { get; init; }

    /// <summary>分割時の値オブジェクトクラスの名前空間。空なら <see cref="RootNamespace"/> へフォールバックする</summary>
    public string? ValueObjectNamespace { get; init; }

    /// <summary>
    /// スキーマ非依存の固定コード（ランタイム）を生成コードへ同梱せず、NuGet パッケージ <c>QuickER.Runtime.*</c> への参照で賄うかどうか（既定 false）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>true</c> のとき、固定 infra（<c>EntityBase</c>・属性・VO 基底・<c>IRepository</c>・<c>SqlQuery</c>・<c>ISqlExecutor</c>・
    /// 方言 Repository 基底・式木翻訳・実行器・接続ファクトリ等）を出力せず、生成コードは
    /// <see cref="RuntimePackages.Core"/> / <see cref="RuntimePackages.SqlServer"/> / <see cref="RuntimePackages.Sqlite"/> /
    /// <see cref="RuntimePackages.EntityFrameworkCore"/> の型を <c>using</c> で参照する。スキーマ依存物
    /// （Entity / EditModel / Mapper / VO 具象 / I{Entity}Repository / エンティティ別実装 / DI 登録）は従来どおり出力する。
    /// </para>
    /// <para>
    /// 分割時（<see cref="SplitFilesByCategory"/>）の共有基盤名前空間 <see cref="RuntimeNamespace"/> は本モードでは無視される
    /// （固定 infra を出力しないため）。必要なパッケージ参照は <see cref="RuntimePackageReferenceGuidance"/> が案内する。
    /// </para>
    /// <para>
    /// 本モードは <see cref="GenerateEfCore"/> とは併用できない（EF Core の <c>QuickErDbContext</c> がスキーマ依存で、
    /// EF Core 固定 infra が同一アセンブリの具象 DbContext を参照するためパッケージ境界を跨げない）。併用指定は生成時に診断エラーになる。
    /// </para>
    /// </remarks>
    public bool UseRuntimePackages { get; init; }

    /// <summary>
    /// 生成コード（.g.cs）と一緒に、その図のスキーマに即した API リファレンス Markdown（<c>.g.md</c>・英語）を出力するかどうか（既定 false）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>true</c> のとき、<see cref="OutputFileName"/> のベース名を <c>.g.md</c> に置換した Markdown ファイルを 1 つ追加出力する
    /// （例: <c>EcOrder.g.cs</c> → <c>EcOrder.g.md</c>）。正本は英語。日本語版の併産は <see cref="IncludeJapaneseApiDocs"/> を参照。
    /// 内容は「スキーマ依存部（Entity 一覧・各エンティティのプロパティ／ナビゲーション・Repository 契約）＋その図のエンティティ名で
    /// 具体化した使い方例」で、固定ランタイム API の詳細は <c>docs/code-generation.md</c> へのリンクで済ませる（本文へ複製しない）。
    /// </para>
    /// <para>
    /// <see cref="SplitFilesByCategory"/>（カテゴリ別分割）でも Markdown は 1 ファイルのみで、名前は <c>Entities.g.cs</c> 等の
    /// カテゴリ別固定名と同じ流儀の固定名 <c>ApiDocs.g.md</c> になる（分割時は <see cref="OutputFileName"/> が
    /// .cs / .md とも出力名に関与しない）。生成日時など非決定的な要素は一切含めないため、同一入力に対して常にバイト一致する。
    /// 検証エラーで生成ファイルが空になる場合は Markdown も出さない。
    /// </para>
    /// </remarks>
    public bool GenerateApiDocs { get; init; }

    /// <summary>
    /// <see cref="GenerateApiDocs"/> が ON のとき、英語の <c>.g.md</c> に加えて日本語版 <c>{ベース名}.ja.g.md</c> を併産するかどうか（既定 false）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>true</c> のとき、英語版（例: <c>EcOrder.g.md</c>）に加えて日本語版（例: <c>EcOrder.ja.g.md</c>・
    /// 分割時は固定名 <c>ApiDocs.ja.g.md</c>）を追加出力する。
    /// 内容・構成は英語版と同一で、見出し・本文・C# 側で組み立てる文言（ナビゲーション種別・DI 登録説明）だけが日本語になる。
    /// </para>
    /// <para>
    /// <see cref="GenerateApiDocs"/> が <c>false</c> のときは無効＝日本語版も含め Markdown を一切出さない。
    /// </para>
    /// </remarks>
    public bool IncludeJapaneseApiDocs { get; init; }

    /// <summary>
    /// 無制限バイナリ列（<c>varbinary(max)</c> / <c>image</c> / 長さ宣言なし BLOB / <c>bytea</c> 等）を、生成 Entity のプロパティに
    /// マーカー属性 <c>[UnboundedBinaryColumn]</c> で印付けし、QuickER 版 Repository の SELECT / UPDATE 対象から除外するかどうか（既定 false）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>true</c> のとき、無制限バイナリ列の Entity プロパティへ <c>[UnboundedBinaryColumn]</c> を付与する。ランタイム
    /// （<c>EntitySaveMetadata</c>）はこの属性をリフレクションで読み、QuickER 版 Repository の SELECT（列読み出し）・UPDATE
    /// （更新列）から当該列を外す。INSERT / BulkInsert は全列のまま（初回書き込みは通常どおり値を渡せる）。
    /// </para>
    /// <para>
    /// 大きな BLOB を一覧・更新のたびに往復させないための最適化で、除外列の値の更新は生 SQL（<c>ExecuteSqlAsync</c>）で行う。
    /// 除外列に値を設定したまま UPDATE を試みると実行時例外になる（黙ってデータを取りこぼさない）。
    /// EF Core モードの <c>DbSet</c> 経由クエリ / <c>SaveChanges</c> には適用されない（EF Core の列選択は EF Core の責務）。
    /// </para>
    /// </remarks>
    public bool ExcludeUnboundedBinaryColumns { get; init; }
}
