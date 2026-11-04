using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider.Oracle;

namespace QuickER.Tests.Provider.Oracle;

/// <summary>
/// Oracle の同期ケーパビリティが参照アクションの畳み込みを配線していることを検証するテストクラス
/// </summary>
/// <remarks>
/// 型表記の正規化の規則は方言の型の表（<c>DialectTypeRows.Oracle.cs</c> の <c>normalize-oracle-*</c>）が固定する。
/// 実 DB で「選択肢と TryFormat の全表記が取込と一致する」ことは
/// <c>OracleTypeNotationIntegrationTests</c>（QuickER.Tests.Integration）が固定する。
/// </remarks>
public class OracleTypeNotationTests
{
    /// <summary>
    /// Oracle の同期ケーパビリティが、Oracle が持たない参照アクション（ON UPDATE・
    /// ON DELETE SET DEFAULT）の畳み込みを配線していることを検証する。
    /// </summary>
    [Fact(DisplayName = "Oracle の capabilities は参照アクションの畳み込みを配線している")]
    public void Provider_WiresNormalizationAndActionFolding()
    {
        var capabilities = new OracleProvider().SyncCapabilities;

        capabilities
            .EffectiveOnUpdate(ForeignKeyReferentialAction.Cascade)
            .Should()
            .Be(ForeignKeyReferentialAction.NoAction);
        capabilities
            .EffectiveOnDelete(ForeignKeyReferentialAction.SetDefault)
            .Should()
            .Be(ForeignKeyReferentialAction.NoAction);
        capabilities
            .EffectiveOnDelete(ForeignKeyReferentialAction.Cascade)
            .Should()
            .Be(ForeignKeyReferentialAction.Cascade);
    }
}
