using AwesomeAssertions;
using QuickER.Tests.CodeGen.CSharp;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// QuickER.Tests の行列の軸が、軸の正本と一対一に対応することを確かめる（軸は作る時点で正本の値ごとのアダプタか
/// 理由つきの除外を確かめるので、作れれば対応している）
/// </summary>
public sealed class MatrixAxisGuardTests
{
    [Fact(
        DisplayName = "簡易 DSL の総当たりの SQLite の構成の実装先の軸は作れ、3 実装先を正本の並びで流す"
    )]
    public void DslSqliteBackendAxis_IsComplete()
    {
        var axis = DslConditionFuzzTests.SqliteBackendAxis();

        axis.Adapters.Select(backend => backend.Name).Should().Equal(DslFuzzBackends.Names);
        axis.Excluded.Should().BeEmpty();
    }

    [Fact(
        DisplayName = "簡易 DSL の総当たりの MySQL の構成の実装先の軸は作れ、インメモリだけを流し、ほかは理由つきで除く"
    )]
    public void DslMySqlBackendAxis_IsComplete()
    {
        var axis = DslConditionFuzzTests.MySqlBackendAxis();

        axis.Adapters.Select(backend => backend.Name).Should().Equal(DslFuzzBackends.InMemory);
        axis.Excluded.Keys.Should().BeEquivalentTo(DslFuzzBackends.Ado, DslFuzzBackends.EfCore);
    }
}
