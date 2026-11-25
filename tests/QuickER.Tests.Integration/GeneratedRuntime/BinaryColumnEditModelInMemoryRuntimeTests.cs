using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedBinaryVoFixture;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 「値オブジェクト × 無制限バイナリ列の除外 × EditModel / Mapper」の往復を、インメモリ Repository（BinaryVoFixture）で検証する派生。
/// </summary>
/// <remarks>
/// インメモリも除外の意味論（通常の取得は除外列を未取得で返し、未取得の除外列は更新でストアの値を保ち、値を残した更新は断る）を持つ
/// （<c>src/QuickER.Runtime/CLAUDE.md</c>「無制限バイナリ列の除外」）。基底の同じ表明で、EditModel / Mapper の素通しの往復が
/// インメモリでもストアの blob を温存することを確かめる。保存先の blob の長さは <c>WithUnboundedBinary</c> で読み戻す
/// （インメモリに生の SQL は無い）。
/// </remarks>
[RuntimeCell(RuntimeFeatures.BinaryColumnEditModel, RuntimeTarget.InMemory)]
public sealed class BinaryColumnEditModelInMemoryRuntimeTests
    : BinaryColumnEditModelRuntimeTestsBase,
        IDisposable
{
    /// <summary>インメモリ Repository を登録した DI コンテナ（テストごとに空のストア）</summary>
    private readonly ServiceProvider _provider = new ServiceCollection()
        .AddGeneratedInMemoryRepositories(seedSampleData: false)
        .BuildServiceProvider();

    protected override IVaultItemRepository Repository() =>
        _provider.GetRequiredService<IVaultItemRepository>();

    /// <summary>ストアはテストのインスタンスごとに空で作るので、何もしない</summary>
    protected override Task ResetAsync() => Task.CompletedTask;

    protected override async Task<long> StoredLengthAsync(string column, int itemId)
    {
        var stored = await Repository()
            .Query()
            .Where(item => item.ItemId == ItemIdValue.Create(itemId))
            .WithUnboundedBinary()
            .FirstOrDefaultAsync(Ct);

        return column switch
        {
            "seal" => stored?.Seal?.Value.Length ?? -1,
            "note_blob" => stored?.NoteBlob?.Value.Length ?? -1,
            _ => throw new ArgumentOutOfRangeException(nameof(column)),
        };
    }

    /// <summary>DI コンテナを破棄する</summary>
    public void Dispose() => _provider.Dispose();
}
