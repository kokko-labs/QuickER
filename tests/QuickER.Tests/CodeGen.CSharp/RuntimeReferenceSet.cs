using System.IO;
using Microsoft.CodeAnalysis;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 生成コード・ランタイムパッケージのソースを Roslyn でコンパイルするときの参照集合
/// </summary>
/// <remarks>
/// <para>
/// TPA（Trusted Platform Assemblies＝BCL 全体）をベースに、排他の対象のアセンブリ（SqlClient / Sqlite / EF Core / DI と、
/// 指定があれば配布ランタイム 7 パッケージ）をファイル名で除外し、構成が許すものだけを明示的に戻す。
/// 許す依存を絞った集合でコンパイルが通ることが、そのソースの依存が最小であることの証明になる
/// （<c>RuntimePackageModeCompilationTests</c>・<c>RuntimePackageSourceRendererTests</c>）。
/// <see cref="Full"/> は何も除外せず全部を許す集合で、<see cref="GeneratedCodeCompiler"/> が使う。
/// </para>
/// <para>
/// 実在しないパスは参照に加えない（TPA に実在しないパスが載る環境への備え）。集合はインスタンスごとに 1 回だけ作り、
/// 以後は同じ一覧を返す（<see cref="MetadataReference"/> は不変）。
/// </para>
/// </remarks>
internal sealed class RuntimeReferenceSet
{
    private readonly bool _sqlClient;
    private readonly bool _sqlite;
    private readonly bool _efCore;
    private readonly bool _di;
    private readonly bool _excludeRuntimePackages;
    private readonly bool _excludeNothing;
    private readonly Lazy<IReadOnlyList<MetadataReference>> _references;

    private RuntimeReferenceSet(
        bool sqlClient,
        bool sqlite,
        bool efCore,
        bool di,
        bool excludeRuntimePackages,
        bool excludeNothing = false
    )
    {
        _sqlClient = sqlClient;
        _sqlite = sqlite;
        _efCore = efCore;
        _di = di;
        _excludeRuntimePackages = excludeRuntimePackages;
        _excludeNothing = excludeNothing;
        _references = new(BuildCore);
    }

    /// <summary>
    /// 許す依存を指定して参照集合を作る
    /// </summary>
    /// <param name="sqlClient">Microsoft.Data.SqlClient を許す</param>
    /// <param name="sqlite">Microsoft.Data.Sqlite を許す</param>
    /// <param name="efCore">EF Core（本体・Relational・Abstractions）を許す</param>
    /// <param name="di">Microsoft.Extensions.DependencyInjection（本体・Abstractions）を許す</param>
    /// <param name="excludeRuntimePackages">
    /// 配布ランタイム 7 パッケージ（<c>QuickER.Runtime*</c>）を TPA から外す。テストプロジェクトは公開 API 面の
    /// スナップショット検証のためにこれらを参照しており、その実アセンブリが TPA に載る。同じ型をソースからその場で
    /// コンパイルし、そのアセンブリを別のコンパイルの参照に足す構成では、混ざると同名型が 2 アセンブリに存在して
    /// CS0433（型があいまい）になる
    /// </param>
    public static RuntimeReferenceSet Create(
        bool sqlClient,
        bool sqlite,
        bool efCore,
        bool di,
        bool excludeRuntimePackages
    ) => new(sqlClient, sqlite, efCore, di, excludeRuntimePackages);

    /// <summary>何も除外せず、生成コードが直接依存し得るアセンブリ（ADO 2 種・EF Core・DI）を全部足した集合</summary>
    /// <remarks>
    /// テストホストでは遅延ロードのため TPA に列挙されないことがあるので、ADO・EF Core・DI は明示的に足す。
    /// </remarks>
    public static RuntimeReferenceSet Full { get; } =
        new(true, true, true, true, excludeRuntimePackages: false, excludeNothing: true);

    /// <summary>Core パッケージ: BCL のみ（ADO / EF Core / DI なし）</summary>
    public static RuntimeReferenceSet CoreOnly { get; } = Create(false, false, false, false, true);

    /// <summary>SqlServer パッケージ: BCL＋SqlClient＋DI</summary>
    public static RuntimeReferenceSet SqlServer { get; } = Create(true, false, false, true, true);

    /// <summary>Sqlite パッケージ: BCL＋Microsoft.Data.Sqlite＋DI</summary>
    public static RuntimeReferenceSet Sqlite { get; } = Create(false, true, false, true, true);

    /// <summary>EfCore パッケージ: BCL＋EF Core＋DI</summary>
    public static RuntimeReferenceSet EfCore { get; } = Create(false, false, true, true, true);

    /// <summary>InMemory パッケージ: BCL のみ（ADO / EF Core / DI なし＝DB 非依存を参照集合で証明する）</summary>
    public static RuntimeReferenceSet InMemory { get; } = Create(false, false, false, false, true);

    /// <summary>Sync パッケージ: BCL のみ（ADO / EF Core / DI なし＝依存ゼロを参照集合で証明する）</summary>
    public static RuntimeReferenceSet Sync { get; } = Create(false, false, false, false, true);

    /// <summary>
    /// AspNetCore パッケージ: BCL（ASP.NET Core 共有フレームワークを含む）＋DI。ADO / EF Core は参照しない
    /// </summary>
    /// <remarks>
    /// ASP.NET Core のアセンブリはテストホストの共有フレームワーク経由で TPA に含まれるため除外対象に入れていない。
    /// DI はサーバー固定エンジンがリポジトリ・<c>ILoggerFactory</c> を解決するために必要（実プロジェクトでは
    /// <c>Microsoft.AspNetCore.App</c> の FrameworkReference が推移的に提供する）。
    /// </remarks>
    public static RuntimeReferenceSet AspNetCore { get; } = Create(false, false, false, true, true);

    /// <summary>生成コード: BCL＋DI（登録拡張）のみ。方言 ADO / EF Core はパッケージ側だけが参照する</summary>
    public static RuntimeReferenceSet GeneratedCode { get; } =
        Create(false, false, false, true, true);

    /// <summary>生成コード（EF Core 生成時）: BCL＋DI＋EF Core。生成側の QuickErDbContext / DI 拡張が EF Core 型を直接参照する</summary>
    public static RuntimeReferenceSet GeneratedCodeWithEfCore { get; } =
        Create(false, false, true, true, true);

    /// <summary>許可されない限り参照へ含めないアセンブリのファイル名（拡張子なし・大文字小文字を区別しない）</summary>
    private static readonly IReadOnlyList<string> ExclusiveAssemblyFileNames =
    [
        "Microsoft.Data.SqlClient",
        "Microsoft.Data.Sqlite",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.EntityFrameworkCore.Relational",
        "Microsoft.EntityFrameworkCore.Abstractions",
        "Microsoft.Extensions.DependencyInjection",
        "Microsoft.Extensions.DependencyInjection.Abstractions",
    ];

    /// <summary>配布ランタイム 7 パッケージのアセンブリのファイル名</summary>
    private static readonly IReadOnlyList<string> RuntimePackageAssemblyFileNames =
    [
        "QuickER.Runtime",
        "QuickER.Runtime.SqlServer",
        "QuickER.Runtime.Sqlite",
        "QuickER.Runtime.EntityFrameworkCore",
        "QuickER.Runtime.InMemory",
        "QuickER.Runtime.AspNetCore",
        "QuickER.Runtime.Sync",
    ];

    /// <summary>参照集合（初回だけ組み立てる）</summary>
    public IReadOnlyList<MetadataReference> Build() => _references.Value;

    private IReadOnlyList<MetadataReference> BuildCore()
    {
        var referencesByPath = new Dictionary<string, MetadataReference>(
            StringComparer.OrdinalIgnoreCase
        );

        void AddPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            referencesByPath.TryAdd(path, MetadataReference.CreateFromFile(path));
        }

        var trustedAssembliesPaths = (
            AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
        )?.Split(Path.PathSeparator);

        if (trustedAssembliesPaths is not null)
        {
            foreach (var path in trustedAssembliesPaths)
            {
                if (IsExcluded(Path.GetFileNameWithoutExtension(path)))
                {
                    continue;
                }

                AddPath(path);
            }
        }

        if (_di)
        {
            AddPath(
                typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection)
                    .Assembly
                    .Location
            );
            AddPath(
                typeof(Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions)
                    .Assembly
                    .Location
            );
        }

        if (_sqlClient)
        {
            AddPath(typeof(Microsoft.Data.SqlClient.SqlConnection).Assembly.Location);
        }

        if (_sqlite)
        {
            AddPath(typeof(Microsoft.Data.Sqlite.SqliteConnection).Assembly.Location);
        }

        if (_efCore)
        {
            AddPath(typeof(Microsoft.EntityFrameworkCore.DbContext).Assembly.Location);
            AddPath(
                typeof(Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions)
                    .Assembly
                    .Location
            );
            AddPath(typeof(Microsoft.EntityFrameworkCore.DeleteBehavior).Assembly.Location);
        }

        return referencesByPath.Values.ToArray();
    }

    /// <summary>TPA のこのアセンブリを除外するか</summary>
    private bool IsExcluded(string fileName)
    {
        if (_excludeNothing)
        {
            return false;
        }

        return ExclusiveAssemblyFileNames.Contains(fileName, StringComparer.OrdinalIgnoreCase)
            || (
                _excludeRuntimePackages
                && RuntimePackageAssemblyFileNames.Contains(
                    fileName,
                    StringComparer.OrdinalIgnoreCase
                )
            );
    }
}
