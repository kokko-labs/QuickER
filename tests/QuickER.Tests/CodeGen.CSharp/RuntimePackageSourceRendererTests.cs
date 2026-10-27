using System.IO;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using QuickER.CodeGen.CSharp;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// <see cref="RuntimePackageSourceRenderer"/> が書き出す 6 パッケージ（Core / SqlServer / Sqlite / EfCore / InMemory /
/// AspNetCore）のソースが、
/// 「案内どおりの最小依存だけを参照して」Roslyn でコンパイルできることと、依存が排他であることを検証する。
/// </summary>
/// <remarks>
/// <para>
/// Core は BCL のみ（Microsoft.Data.SqlClient / Microsoft.Data.Sqlite / EntityFrameworkCore を参照に含めない）で
/// コンパイル成立＝依存ゼロを証明する。方言／EF Core は Core ソース＋該当依存のみで成立し、他方言の ADO や EF Core を参照しない。
/// </para>
/// <para>
/// 参照集合は実行時の Trusted Platform Assemblies（BCL）をベースに、除外対象アセンブリ
/// （SqlClient / Sqlite / EF Core / DI）をファイル名で取り除いてから、各ケースが許す依存だけを明示的に戻して構築する。
/// </para>
/// </remarks>
public class RuntimePackageSourceRendererTests
{
    private readonly RuntimePackageSourceRenderer _renderer = new();

    /// <summary>Core は BCL のみ（ADO / EF Core / DI なし）でコンパイルでき、診断ゼロになる</summary>
    [Fact]
    public void RenderCore_CompilesWithFrameworkReferencesOnly()
    {
        var core = _renderer.RenderCore();

        var result = Compile([core], allowSqlClient: false, allowSqlite: false, allowEfCore: false);

        result
            .Success.Should()
            .BeTrue($"Core は BCL のみで成立するはず:{Environment.NewLine}{result.Describe()}");
    }

    /// <summary>SqlServer は Core＋SqlClient 参照でコンパイルでき、Sqlite / EF Core 参照なしで成立する</summary>
    [Fact]
    public void RenderSqlServer_CompilesWithCoreAndSqlClientOnly()
    {
        var core = _renderer.RenderCore();
        var sqlServer = _renderer.RenderSqlServer();

        var result = Compile(
            [core, sqlServer],
            allowSqlClient: true,
            allowSqlite: false,
            allowEfCore: false
        );

        result
            .Success.Should()
            .BeTrue(
                $"SqlServer は Core＋Microsoft.Data.SqlClient のみで成立するはず:{Environment.NewLine}{result.Describe()}"
            );
    }

    /// <summary>Sqlite は Core＋Microsoft.Data.Sqlite 参照でコンパイルでき、SqlClient / EF Core 参照なしで成立する</summary>
    [Fact]
    public void RenderSqlite_CompilesWithCoreAndSqliteOnly()
    {
        var core = _renderer.RenderCore();
        var sqlite = _renderer.RenderSqlite();

        var result = Compile(
            [core, sqlite],
            allowSqlClient: false,
            allowSqlite: true,
            allowEfCore: false
        );

        result
            .Success.Should()
            .BeTrue(
                $"Sqlite は Core＋Microsoft.Data.Sqlite のみで成立するはず:{Environment.NewLine}{result.Describe()}"
            );
    }

    /// <summary>EfCore は Core＋EF Core 参照でコンパイルでき、SqlClient / Sqlite 参照なしで成立する</summary>
    [Fact]
    public void RenderEfCore_CompilesWithCoreAndEfCoreOnly()
    {
        var core = _renderer.RenderCore();
        var efCore = _renderer.RenderEfCore();

        var result = Compile(
            [core, efCore],
            allowSqlClient: false,
            allowSqlite: false,
            allowEfCore: true
        );

        result
            .Success.Should()
            .BeTrue(
                $"EfCore は Core＋EF Core のみで成立するはず:{Environment.NewLine}{result.Describe()}"
            );
    }

    /// <summary>InMemory は Core 参照のみでコンパイルでき、ADO / EF Core / DI 参照なしで成立する</summary>
    [Fact]
    public void RenderInMemory_CompilesWithCoreOnly()
    {
        var core = _renderer.RenderCore();
        var inMemory = _renderer.RenderInMemory();

        var result = Compile(
            [core, inMemory],
            allowSqlClient: false,
            allowSqlite: false,
            allowEfCore: false
        );

        result
            .Success.Should()
            .BeTrue(
                $"InMemory は Core＋BCL のみで成立するはず:{Environment.NewLine}{result.Describe()}"
            );
    }

    /// <summary>AspNetCore は Core＋ASP.NET Core（DI 込み）でコンパイルでき、ADO / EF Core 参照なしで成立する</summary>
    /// <remarks>
    /// ASP.NET Core のアセンブリは TPA（テストホストが ASP.NET Core 共有フレームワークを持つ）に含まれるため、
    /// 追加で戻すのは DI（<c>Microsoft.Extensions.DependencyInjection.Abstractions</c>）のみ。固定エンジンが
    /// リポジトリと <c>ILoggerFactory</c> を DI から解決するために必要で、実プロジェクトでは
    /// <c>Microsoft.AspNetCore.App</c> の FrameworkReference が推移的に提供する。
    /// </remarks>
    [Fact]
    public void RenderAspNetCore_CompilesWithCoreAndAspNetCoreOnly()
    {
        var core = _renderer.RenderCore();
        var aspNetCore = _renderer.RenderAspNetCore();

        var result = Compile(
            [core, aspNetCore],
            allowSqlClient: false,
            allowSqlite: false,
            allowEfCore: false,
            allowDependencyInjection: true
        );

        result
            .Success.Should()
            .BeTrue(
                $"AspNetCore は Core＋ASP.NET Core（共有フレームワーク）のみで成立するはず:{Environment.NewLine}{result.Describe()}"
            );
    }

    /// <summary>AspNetCore ソースには方言 ADO / EF Core の名前空間文字列が現れない（依存排他の文字列ガード）</summary>
    /// <remarks>
    /// DI（<c>Microsoft.Extensions.DependencyInjection</c>）は EF Core パッケージと同じ例外で、固定エンジン自身が
    /// リポジトリ・<c>ILoggerFactory</c> の解決に使うため許容する（ASP.NET Core が推移的に持ち込むので依存は増えない）。
    /// </remarks>
    [Fact]
    public void RenderAspNetCore_DoesNotReferenceDialectOrEfNamespaces()
    {
        var aspNetCore = _renderer.RenderAspNetCore();

        aspNetCore.Should().NotContain("Microsoft.Data.SqlClient");
        aspNetCore.Should().NotContain("Microsoft.Data.Sqlite");
        aspNetCore.Should().NotContain("EntityFrameworkCore");
    }

    /// <summary>InMemory ソースには方言 ADO / EF Core / DI の名前空間文字列が現れない（依存排他の文字列ガード）</summary>
    [Fact]
    public void RenderInMemory_DoesNotReferenceDialectEfOrDiNamespaces()
    {
        var inMemory = _renderer.RenderInMemory();

        inMemory.Should().NotContain("Microsoft.Data.SqlClient");
        inMemory.Should().NotContain("Microsoft.Data.Sqlite");
        inMemory.Should().NotContain("EntityFrameworkCore");
        // DI 登録拡張（AddGeneratedInMemoryRepositories）はスキーマ依存物として生成側にだけ出る
        inMemory.Should().NotContain("Microsoft.Extensions.DependencyInjection");
    }

    /// <summary>Core ソースには方言 ADO / EF Core の名前空間文字列が現れない（依存排他の文字列ガード）</summary>
    [Fact]
    public void RenderCore_DoesNotReferenceDialectOrEfNamespaces()
    {
        var core = _renderer.RenderCore();

        core.Should().NotContain("Microsoft.Data.SqlClient");
        core.Should().NotContain("Microsoft.Data.Sqlite");
        core.Should().NotContain("EntityFrameworkCore");
    }

    /// <summary>各パッケージソースは固定名前空間で出力され、方言／EF Core はコア契約を using する</summary>
    [Fact]
    public void RenderedSources_UseFixedNamespacesAndCoreUsing()
    {
        _renderer.RenderCore().Should().Contain($"namespace {RuntimePackages.Core};");

        var sqlServer = _renderer.RenderSqlServer();
        sqlServer.Should().Contain($"namespace {RuntimePackages.SqlServer};");
        sqlServer.Should().Contain($"using {RuntimePackages.Core};");

        var sqlite = _renderer.RenderSqlite();
        sqlite.Should().Contain($"namespace {RuntimePackages.Sqlite};");
        sqlite.Should().Contain($"using {RuntimePackages.Core};");

        var efCore = _renderer.RenderEfCore();
        efCore.Should().Contain($"namespace {RuntimePackages.EntityFrameworkCore};");
        efCore.Should().Contain($"using {RuntimePackages.Core};");

        var inMemory = _renderer.RenderInMemory();
        inMemory.Should().Contain($"namespace {RuntimePackages.InMemory};");
        inMemory.Should().Contain($"using {RuntimePackages.Core};");

        var aspNetCore = _renderer.RenderAspNetCore();
        aspNetCore.Should().Contain($"namespace {RuntimePackages.AspNetCore};");
        aspNetCore.Should().Contain($"using {RuntimePackages.Core};");
    }

    /// <summary>
    /// 方言パッケージの行マテリアライザに型特殊化（<c>_typedReaders</c>）と
    /// 値オブジェクトの <c>Create</c> 直呼びが含まれる。
    /// </summary>
    /// <remarks>
    /// パッケージソースは常に値オブジェクト有効でレンダリングされる（<see cref="RuntimePackageSourceRenderer"/>）ため、
    /// 型特殊化が値オブジェクト構成で無効化されていると、<c>UseRuntimePackages</c> の利用者は図の設定に依らず
    /// 常に <c>GetValue</c> ボックス化＋<c>PropertyInfo.SetValue</c> のフォールバックへ落ちてしまう。
    /// EF Core のパッケージは行の組み立て器を持たない（<see cref="EfCoreEntitySaveMetadataTests"/> が固定する）。
    /// </remarks>
    [Fact]
    public void RenderedSources_KeepTypeSpecializedMaterializer()
    {
        string[] sources = [_renderer.RenderSqlServer(), _renderer.RenderSqlite()];

        foreach (var source in sources)
        {
            // 型特殊化アクセサ表（素の列の boxing 回避）
            source
                .Should()
                .Contain(
                    "private static readonly IReadOnlyDictionary<Type, MethodInfo> _typedReaders ="
                );
            source.Should().Contain("[typeof(int)] = Getter(nameof(DbDataReader.GetInt32)),");

            // 値オブジェクト列は内包値を型特殊化アクセサで読み、静的 Create を式木から直呼びする
            source.Should().Contain("private static (MethodInfo Getter, MethodInfo Create)?");
            source
                .Should()
                .Contain(
                    "if (ResolveValueObjectReader(propertyType) is (var voGetter, var voCreate))"
                );
            source.Should().Contain("Expression.Call(readerParam, voGetter, ordinal)");
        }
    }

    /// <summary>
    /// SqlServer パッケージのパラメータ束縛が <c>[SqlColumnType]</c> による明示型付け版であり、
    /// Sqlite パッケージには入らない。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 明示型付けの出力条件は「図に SqlDbType の判明した列がある」ことなので、空図でレンダリングする
    /// パッケージソースでは素通りし、<c>AddWithValue</c> のフォールバック版だけが入っていた。属性型
    /// <c>SqlColumnTypeAttribute</c> 自体は出ているため型集合の対称性テストでは検出できず、
    /// <c>UseRuntimePackages</c> の利用者だけが Size ガード（宣言長超過時の値長採用）と精度指定を失っていた。
    /// </para>
    /// <para>
    /// <c>[SqlColumnType]</c> は <c>System.Data.SqlDbType</c> 前提の SQL Server 専用機構なので、Sqlite 側へ
    /// 混入しないことも同時に固定する。
    /// </para>
    /// </remarks>
    [Fact]
    public void RenderSqlServer_KeepsExplicitlyTypedParameterBinding()
    {
        var sqlServer = _renderer.RenderSqlServer();

        sqlServer
            .Should()
            .Contain(
                "private static readonly ConcurrentDictionary<PropertyInfo, SqlColumnTypeAttribute?> _columnTypeCache =",
                "属性解決のキャッシュは明示型付け版の分岐にしかない"
            );
        sqlServer.Should().Contain("var parameter = new SqlParameter(name, dbType);");
        sqlServer
            .Should()
            .Contain(
                "parameter.Size = valueLength > attribute.Size ? valueLength : attribute.Size;",
                "文字列／バイナリの Size ガード（宣言長超過なら値長）はサイレントなデータ破損を防ぐ"
            );

        _renderer
            .RenderSqlite()
            .Should()
            .NotContain(
                "SqlColumnTypeAttribute",
                "[SqlColumnType] は SqlDbType 前提の SQL Server 専用機構"
            );
    }

    /// <summary>
    /// 指定した許可依存だけを参照へ含めて、パッケージソース群をコンパイルする。
    /// </summary>
    /// <remarks>
    /// BCL（TPA）から SqlClient / Sqlite / EF Core / DI のアセンブリをファイル名で除外してから、
    /// <paramref name="allowSqlClient"/> 等が true のものだけを明示的に戻す。DI（<c>Microsoft.Extensions.DependencyInjection</c>）は
    /// EF Core 部品（AddGeneratedEfCoreRepositories）とサーバー固定エンジン（<paramref name="allowDependencyInjection"/>）
    /// だけが必要とする。方言パッケージの DI 登録拡張はスキーマ依存物として
    /// 生成側に出力される（パッケージ書き出しでは抑止）ため、方言許可では DI を戻さない＝DI 非依存をコンパイルで証明する。
    /// </remarks>
    private static CompileResult Compile(
        IReadOnlyList<string> sources,
        bool allowSqlClient,
        bool allowSqlite,
        bool allowEfCore,
        bool allowDependencyInjection = false
    )
    {
        var emit = GeneratedCodeCompiler.Emit(
            sources.Select((source, index) => ($"Package{index}.g.cs", source)),
            $"QuickER.RuntimePackage.Tests.{Guid.NewGuid():N}",
            References(allowSqlClient, allowSqlite, allowEfCore, allowDependencyInjection)
        );

        return new CompileResult { Success = emit.Success, Errors = emit.Errors };
    }

    /// <summary>許可依存だけを含む参照集合</summary>
    /// <remarks>
    /// DI（登録拡張）を必要とするのは EF Core 部品（AddGeneratedEfCoreRepositories）と、
    /// サーバー固定エンジン（リポジトリ・ILoggerFactory を DI から解決する）だけ。
    /// 方言パッケージは DI 非依存（DI 登録拡張はスキーマ依存物として生成側に出力される）のため戻さない。
    /// パッケージのソースは Core と方言を 1 つのコンパイルに束ね、そのアセンブリをほかのコンパイルの参照に足さないので、
    /// 配布ランタイムのアセンブリ（<c>QuickER.Runtime*</c>）は TPA から外さない（従来の参照集合のまま）。
    /// </remarks>
    private static IReadOnlyList<MetadataReference> References(
        bool allowSqlClient,
        bool allowSqlite,
        bool allowEfCore,
        bool allowDependencyInjection
    ) =>
        RuntimeReferenceSet
            .Create(
                sqlClient: allowSqlClient,
                sqlite: allowSqlite,
                efCore: allowEfCore,
                di: allowEfCore || allowDependencyInjection,
                excludeRuntimePackages: false
            )
            .Build();

    /// <summary>コンパイル結果（成功可否とエラー診断）</summary>
    private sealed class CompileResult
    {
        public required bool Success { get; init; }

        public required IReadOnlyList<Diagnostic> Errors { get; init; }

        /// <summary>エラー診断を読みやすい文字列にまとめる（テスト失敗メッセージ用）</summary>
        public string Describe() =>
            string.Join(Environment.NewLine, Errors.Select(GeneratedCodeDiagnostics.Describe));
    }
}
