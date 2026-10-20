using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using QuickER.Provider.SqlServer;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// SQL Server の型表記の閉包（図に書いた表記で同期 → 取り込み直す → 差分 0）を実 DB で確かめる統合テスト。
/// </summary>
/// <remarks>
/// 型の選択肢と <see cref="SqlServerTypeCatalog.TryFormat"/> の全表記について、
/// <see cref="SqlServerTypeNotation.Normalize"/> した表記が取込の表記と一致し、取り込み直した後の差分に
/// AlterColumn が残らないことを固定する（判定の本体は <see cref="TypeNotationClosure"/>）。
/// </remarks>
[Trait("Category", "Integration")]
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class SqlServerTypeNotationIntegrationTests(SqlServerContainerFixture fixture)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>
    /// 選択肢と TryFormat の全表記で表を作って取り込み直すと、正規化した図の表記と取込の表記が一致し、
    /// 差分に AlterColumn が残らないことを検証する。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] SQL Server: 選択肢と TryFormat の全表記で同期→再取込→差分 0"
    )]
    public async Task TypeNotation_IsClosedUnderSyncAndImport()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        var provider = new SqlServerProvider();
        // 選択肢と TryFormat の表記に加え、指摘で実際に食い違った綴り（図に手で書かれ得る別表記）も確かめる
        var types = TypeNotationClosure
            .CandidateTypes(provider.TypeCatalog, TypeNotationClosure.DefaultSamples)
            .Concat([
                "decimal",
                "numeric",
                "datetime2(7)",
                "time(7)",
                "datetimeoffset(7)",
                "decimal(10,0)",
                "datetime2(3)",
                "float(1)",
                "float(24)",
                "float(25)",
                "float(53)",
            ])
            // 方言切替が Oracle の INTEGER から書く型も閉じること
            .Append(TypeNotationClosure.ConvertedOracleInteger(provider))
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
