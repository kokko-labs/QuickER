using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using QuickER.Provider.PostgreSql;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// PostgreSQL の型表記の閉包（図に書いた表記で同期 → 取り込み直す → 差分 0）を実 DB で確かめる統合テスト。
/// </summary>
/// <remarks>
/// 型の選択肢と <see cref="PostgreSqlTypeCatalog.TryFormat"/> の全表記について、
/// <see cref="PostgreSqlTypeNotation.Normalize"/> した表記が取込の表記と一致し、取り込み直した後の差分に
/// AlterColumn が残らないことを固定する（判定の本体は <see cref="TypeNotationClosure"/>）。
/// </remarks>
[Trait("Category", "Integration")]
[Collection(PostgreSqlContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class PostgreSqlTypeNotationIntegrationTests(PostgreSqlContainerFixture fixture)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>
    /// 選択肢と TryFormat の全表記で表を作って取り込み直すと、正規化した図の表記と取込の表記が一致し、
    /// 差分に AlterColumn が残らないことを検証する。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] PostgreSQL: 選択肢と TryFormat の全表記で同期→再取込→差分 0"
    )]
    public async Task TypeNotation_IsClosedUnderSyncAndImport()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        var provider = new PostgreSqlProvider();
        // 選択肢と TryFormat の表記に加え、指摘で実際に食い違った綴り（図に手で書かれ得る別表記）も確かめる
        var types = TypeNotationClosure
            .CandidateTypes(provider.TypeCatalog, TypeNotationClosure.DefaultSamples)
            .Concat(["timestamp", "timestamptz", "time", "numeric(18,0)", "timestamp(3)"])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        await TypeNotationClosure.AssertClosedAsync(
            provider,
            fixture.ToDbConnectionSettings(),
            types,
            "tn_",
            Ct
        );
    }
}
