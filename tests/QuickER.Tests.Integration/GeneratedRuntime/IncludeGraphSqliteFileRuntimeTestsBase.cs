using System;
using System.Threading.Tasks;
using QuickER.Tests.GeneratedQueryFixture;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// グラフ取得糖衣のランタイムスイートを<b>実ファイル SQLite</b>（Docker 不要＝CI 常時実行）で流す派生の共通基底。
/// QuickER の <c>SqliteRepository</c> 版と EF Core Sqlite 版がリポジトリの解決だけを差し込む。
/// </summary>
public abstract class IncludeGraphSqliteFileRuntimeTestsBase
    : IncludeGraphQueryFixtureRuntimeTestsBase,
        IDisposable
{
    /// <summary>各テストが読み書きする一時ファイル DB</summary>
    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();

    /// <summary>書き込み可能な接続文字列（バックエンドはこの実ファイルへ読み書きする）</summary>
    protected string ConnectionString => _db.ReadWriteCreateConnectionString;

    protected override async Task ResetStorageAsync()
    {
        await _db.ResetSchemaAsync(Ct);
        await _db.ApplyDdlAsync(QueryFixtureDefinition.Build(), Ct);
    }

    /// <summary>一時 DB を破棄する</summary>
    public virtual void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }
}
