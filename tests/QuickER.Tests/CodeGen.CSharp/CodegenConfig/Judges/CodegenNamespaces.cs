using QuickER.CodeGen.CSharp;
using QuickER.Dialects;

namespace QuickER.Tests.CodeGen.CSharp.CodegenConfig.Judges;

/// <summary>判定が共有する、オプションから決まる事実（docs・CLAUDE.md の規則の写し。生成器の関数は呼ばない）</summary>
internal static class CodegenOptionFacts
{
    /// <summary>QuickER 版 Repository の実効方言</summary>
    /// <remarks>
    /// <c>CodeGenerationOptions.RepositoryDialects</c> の説明：null または空は既定 <c>sqlserver</c> の単一へフォールバックする。
    /// 次元表は対応する 2 方言だけを流すので、未対応方言の扱い（docs/cli.md）はここに書かない。
    /// </remarks>
    public static IReadOnlyList<string> Dialects(CodeGenerationOptions options) =>
        options.RepositoryDialects is { Count: > 0 } dialects
            ? dialects.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : [DialectNames.SqlServer];

    /// <summary>Repository 契約を生成するか</summary>
    /// <remarks>
    /// docs/cli.md の GenerateRemoteContracts：QuickER 版 Repository・EF Core 版 Repository・インメモリ Repository のどれかが契約を作る。
    /// </remarks>
    public static bool GeneratesRepositoryContract(CodeGenerationOptions options) =>
        options.GenerateRepositories
        || options.GenerateEfCoreRepositories
        || options.GenerateInMemoryRepositories;

    /// <summary>リモート（契約かサービス）を指定したか</summary>
    public static bool GeneratesRemote(CodeGenerationOptions options) =>
        options.GenerateRemoteContracts || options.GenerateRemoteServices;

    /// <summary>分割して出すか（層別は分割を含意する＝docs「Layered folder output」）</summary>
    public static bool Split(CodeGenerationOptions options) =>
        options.SplitFilesByCategory || options.LayeredOutput;

    /// <summary>QuickER 版 Repository を 2 方言以上で出すか（マルチターゲット）</summary>
    public static bool MultiTarget(CodeGenerationOptions options) =>
        options.GenerateRepositories && Dialects(options).Count >= 2;

    /// <summary>方言のファイル名・名前空間の接尾辞（docs：<c>Runtime.SqlServer.g.cs</c> / <c>Runtime.Sqlite.g.cs</c>）</summary>
    public static string DialectSuffix(string dialect) =>
        string.Equals(dialect, DialectNames.Sqlite, StringComparison.OrdinalIgnoreCase)
            ? "Sqlite"
            : "SqlServer";
}

/// <summary>
/// 名前空間の規則（期待の出どころ。<c>GeneratedFilePlanner</c> を期待に使わない）
/// </summary>
/// <remarks>
/// <para>出どころ：</para>
/// <list type="bullet">
///   <item>非分割は全部をルート名前空間へまとめる（docs/cli.md の SplitFilesByCategory。ルートの既定は <c>Generated</c>）。
///   マルチターゲットの方言実装だけは <c>{RepositoryNamespace}.SqlServer</c> / <c>.Sqlite</c>（src/QuickER.CodeGen.CSharp/CLAUDE.md
///   「DB アクセスの排他と単独出力」。RepositoryNamespace の既定は <c>{root}.Repositories</c>）。サーバー実装はルート（docs「Remote services」）。</item>
///   <item>通常分割はバケットごとに <c>{root}.{接尾辞}</c>（docs「Extending the generated base classes」の <c>{RootNamespace}.Entities</c>・
///   docs「How generated files map to the packages」の <c>{RootNamespace}.Runtime</c>）。方言実装・EF Core・インメモリ・同期は契約の名前空間の
///   サブ名前空間（CLAUDE.md「DB アクセスの排他と単独出力」）、HTTP クライアントは契約と同じ名前空間（CLAUDE.md「HTTP クライアントのバケット分離」）、
///   固定 infra は <c>{Runtime}.{接尾辞}</c>（docs の対応表）。サーバー実装は層別の表と同じ接尾辞 <c>RemoteServer</c>。</item>
///   <item>層別は層フォルダ由来のルートの下（docs「Layered folder output」の表）。インフラ層は固定 infra と per-entity が系統ごとに同じ名前空間
///   （<c>.SqlServer</c> / <c>.Sqlite</c> / <c>.EntityFrameworkCore</c> / <c>.InMemory</c> / <c>.Sync</c> / <c>.Http</c>）、サーバー層は
///   <c>.RemoteServer</c> / <c>.AspNetCore</c>。</item>
///   <item>明示の名前空間オプションは常に優先（CLAUDE.md「名前空間の既定」）。パッケージ参照モードの固定 infra は <c>QuickER.Runtime*</c>（docs）。</item>
/// </list>
/// </remarks>
internal sealed class CodegenNamespaces
{
    private readonly CodeGenerationOptions _options;
    private readonly bool _split;
    private readonly bool _layered;

    /// <summary>オプションから名前空間を決める</summary>
    public CodegenNamespaces(CodeGenerationOptions options)
    {
        _options = options;
        _split = CodegenOptionFacts.Split(options);
        _layered = options.LayeredOutput;
        Root = string.IsNullOrWhiteSpace(options.RootNamespace)
            ? "Generated"
            : options.RootNamespace.Trim();
    }

    /// <summary>ルート名前空間</summary>
    public string Root { get; }

    /// <summary>層の既定フォルダ（docs：Domain / Presentation / Infrastructure / Server。空白は既定へ倒す）</summary>
    public string DomainDirectory => Directory(_options.DomainLayerDirectory, "Domain");

    /// <summary>プレゼンテーション層のフォルダ</summary>
    public string PresentationDirectory =>
        Directory(_options.PresentationLayerDirectory, "Presentation");

    /// <summary>インフラ層のフォルダ</summary>
    public string InfraDirectory =>
        Directory(_options.InfrastructureLayerDirectory, "Infrastructure");

    /// <summary>サーバー層のフォルダ</summary>
    public string ServerDirectory => Directory(_options.ServerLayerDirectory, "Server");

    /// <summary>インフラ層の出力フォルダ（層別でなければ null）</summary>
    public string? InfrastructureDirectory => _layered ? InfraDirectory : null;

    /// <summary>Entity</summary>
    public string Entity => Bucket(_options.EntityNamespace, DomainDirectory, "Entities");

    /// <summary>EditModel</summary>
    public string EditModel =>
        Bucket(_options.EditModelNamespace, PresentationDirectory, "EditModels");

    /// <summary>Mapper</summary>
    public string Mapper => Bucket(_options.MapperNamespace, PresentationDirectory, "Mappers");

    /// <summary>値オブジェクト</summary>
    public string ValueObject =>
        Bucket(_options.ValueObjectNamespace, DomainDirectory, "ValueObjects");

    /// <summary>Repository 契約</summary>
    public string Contract => Bucket(_options.RepositoryNamespace, DomainDirectory, "Repositories");

    /// <summary>固定 infra のコア（パッケージ参照モードでは出ない）</summary>
    public string Runtime => Bucket(_options.RuntimeNamespace, DomainDirectory, "Runtime");

    /// <summary>QuickER 版 Repository の方言実装</summary>
    /// <param name="dialect">方言名</param>
    public string DialectImplementation(string dialect)
    {
        var suffix = CodegenOptionFacts.DialectSuffix(dialect);

        if (!_split)
        {
            return CodegenOptionFacts.MultiTarget(_options)
                ? $"{NonSplitRepositoryNamespace}.{suffix}"
                : Root;
        }

        return _layered ? $"{LayerRoot(InfraDirectory)}.{suffix}" : $"{Contract}.{suffix}";
    }

    /// <summary>方言エンジンの固定 infra（インラインのとき）</summary>
    /// <param name="dialect">方言名</param>
    public string DialectEngine(string dialect)
    {
        var suffix = CodegenOptionFacts.DialectSuffix(dialect);

        if (!_split)
        {
            return DialectImplementation(dialect);
        }

        return _layered ? $"{LayerRoot(InfraDirectory)}.{suffix}" : $"{Runtime}.{suffix}";
    }

    /// <summary>EF Core 版 Repository・DbContext</summary>
    public string EfCore => SubImplementation("EntityFrameworkCore");

    /// <summary>インメモリ Repository</summary>
    public string InMemory => SubImplementation("InMemory");

    /// <summary>同期の記述子・デコレータ</summary>
    public string Sync => SubImplementation("Sync");

    /// <summary>HTTP クライアント</summary>
    public string Http =>
        !_split ? Root
        : _layered ? $"{LayerRoot(InfraDirectory)}.Http"
        : Contract;

    /// <summary>サーバー実装（per-entity のエンドポイント）</summary>
    public string RemoteServer =>
        !_split ? Root
        : _layered ? $"{LayerRoot(ServerDirectory)}.RemoteServer"
        : $"{Root}.RemoteServer";

    /// <summary>固定 infra の系統（EF Core・インメモリ・同期）</summary>
    /// <param name="suffix">系統の接尾辞</param>
    public string RuntimeFamily(string suffix) =>
        _layered ? $"{LayerRoot(InfraDirectory)}.{suffix}" : $"{Runtime}.{suffix}";

    /// <summary>サーバーの固定 infra</summary>
    public string RuntimeAspNetCore =>
        _layered ? $"{LayerRoot(ServerDirectory)}.AspNetCore" : $"{Runtime}.AspNetCore";

    /// <summary>非分割のマルチターゲットで方言実装がぶら下がる名前空間（RepositoryNamespace の既定は <c>{root}.Repositories</c>）</summary>
    private string NonSplitRepositoryNamespace =>
        string.IsNullOrWhiteSpace(_options.RepositoryNamespace)
            ? $"{Root}.Repositories"
            : _options.RepositoryNamespace.Trim();

    private string SubImplementation(string suffix) =>
        !_split ? Root
        : _layered ? $"{LayerRoot(InfraDirectory)}.{suffix}"
        : $"{Contract}.{suffix}";

    private string Bucket(string? explicitNamespace, string layerDirectory, string suffix)
    {
        if (!_split)
        {
            return Root;
        }

        if (!string.IsNullOrWhiteSpace(explicitNamespace))
        {
            return explicitNamespace.Trim();
        }

        return _layered ? $"{LayerRoot(layerDirectory)}.{suffix}" : $"{Root}.{suffix}";
    }

    /// <summary>層ルート＝フォルダパスの区切りを <c>.</c> にしたもの（docs：<c>MyApp.Domain/Generated</c> → <c>MyApp.Domain.Generated</c>）</summary>
    private static string LayerRoot(string directory) =>
        string.Join('.', directory.Split('/', '\\', StringSplitOptions.RemoveEmptyEntries));

    private static string Directory(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
