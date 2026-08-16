using System.Reflection;
using QuickER.Tests.GeneratedFixture;

namespace QuickER.Tests.PublicApi;

/// <summary>
/// 配布ランタイム 6 パッケージ（<c>QuickER.Runtime</c> / <c>.SqlServer</c> / <c>.Sqlite</c> /
/// <c>.EntityFrameworkCore</c> / <c>.InMemory</c> / <c>.AspNetCore</c>）の公開 API 面が、
/// チェックイン済みの承認ファイル（<c>PublicApi/*.approved.txt</c>）と一致することを検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// パッケージはロックステップ版で公開され、同一メジャー内では生成コードとランタイムが互換であると
/// SemVer で宣言している。その約束を守れているかを機械的に確かめる仕掛けがこれまで無く、
/// 依存集合（<c>RuntimePackageProjectDependencyGuardTests</c>）と型集合（<c>SplitRuntimeSymmetryTests</c>）は
/// 見ていたが、メンバーの追加・削除・シグネチャ変更は誰も見ていなかった。
/// </para>
/// <para>
/// 正規化の仕様（何を含め何を含めないか）は <see cref="PublicApiSnapshot"/> の docstring を正本とする。
/// 承認ファイルの更新は既存フィクスチャと同一経路（<c>QUICKER_REGEN_FIXTURES=1</c>）で行う。
/// <b>差分が出たときは、まず「利用者のコードが壊れる変更か」を判断してから再生成すること</b>
/// ——再生成は承認であって検証ではない。
/// </para>
/// <para>
/// クラス名の <c>Drift</c> は既存ドリフトテスト群と同じ命名で、再生成コマンドの
/// <c>--filter "FullyQualifiedName~Drift"</c> に乗せるための約束でもある（外すと再生成されなくなる）。
/// </para>
/// </remarks>
public sealed class RuntimePackagePublicApiDriftTests
{
    /// <summary>コア（<c>QuickER.Runtime</c>）の公開 API 面が承認ファイルと一致する</summary>
    [Fact(DisplayName = "コアパッケージの公開 API 面が承認ファイルと一致する（スナップショット）")]
    public void CoreAssembly_MatchesApprovedApi() =>
        Verify(typeof(global::QuickER.Runtime.NavigationReferenceAttribute).Assembly);

    /// <summary>SQL Server エンジン（<c>QuickER.Runtime.SqlServer</c>）の公開 API 面が承認ファイルと一致する</summary>
    [Fact(
        DisplayName = "SqlServer パッケージの公開 API 面が承認ファイルと一致する（スナップショット）"
    )]
    public void SqlServerAssembly_MatchesApprovedApi() =>
        Verify(typeof(global::QuickER.Runtime.SqlServer.SqlConnectionFactory).Assembly);

    /// <summary>SQLite エンジン（<c>QuickER.Runtime.Sqlite</c>）の公開 API 面が承認ファイルと一致する</summary>
    [Fact(
        DisplayName = "Sqlite パッケージの公開 API 面が承認ファイルと一致する（スナップショット）"
    )]
    public void SqliteAssembly_MatchesApprovedApi() =>
        Verify(typeof(global::QuickER.Runtime.Sqlite.SqlConnectionFactory).Assembly);

    /// <summary>EF Core 共通部品（<c>QuickER.Runtime.EntityFrameworkCore</c>）の公開 API 面が承認ファイルと一致する</summary>
    [Fact(
        DisplayName = "EntityFrameworkCore パッケージの公開 API 面が承認ファイルと一致する（スナップショット）"
    )]
    public void EntityFrameworkCoreAssembly_MatchesApprovedApi() =>
        Verify(typeof(global::QuickER.Runtime.EntityFrameworkCore.EntitySaveMetadata).Assembly);

    /// <summary>インメモリ基盤（<c>QuickER.Runtime.InMemory</c>）の公開 API 面が承認ファイルと一致する</summary>
    [Fact(
        DisplayName = "InMemory パッケージの公開 API 面が承認ファイルと一致する（スナップショット）"
    )]
    public void InMemoryAssembly_MatchesApprovedApi() =>
        Verify(typeof(global::QuickER.Runtime.InMemory.EntitySaveMetadata).Assembly);

    /// <summary>リモートサーバー基盤（<c>QuickER.Runtime.AspNetCore</c>）の公開 API 面が承認ファイルと一致する</summary>
    [Fact(
        DisplayName = "AspNetCore パッケージの公開 API 面が承認ファイルと一致する（スナップショット）"
    )]
    public void AspNetCoreAssembly_MatchesApprovedApi() =>
        Verify(typeof(global::QuickER.Runtime.AspNetCore.RemoteServerEngine).Assembly);

    /// <summary>アセンブリ名から承認ファイルのパスを導き、公開 API 面を照合（または再生成）する。</summary>
    private static void Verify(Assembly assembly)
    {
        var name = assembly.GetName().Name!;

        FixtureDriftHarness.VerifyOrRegenerateRepoFile(
            PublicApiSnapshot.Render(assembly),
            $"tests/QuickER.Tests/PublicApi/{name}.approved.txt",
            $"{name} の公開 API 面が承認ファイルと乖離しています。"
                + "利用者のコンパイル済みコードを壊す変更（メンバーの削除・シグネチャや既定値の変更）でないことを"
                + "確認してから承認ファイルを再生成してください。"
        );
    }
}
