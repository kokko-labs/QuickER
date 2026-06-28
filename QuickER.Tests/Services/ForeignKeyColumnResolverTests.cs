using QuickER.Services;
using FluentAssertions;

namespace QuickER.Tests.Services;

/// <summary><see cref="ForeignKeyColumnResolver"/> の優先順位・除外・タイブレークの判定を検証するテストクラス</summary>
public class ForeignKeyColumnResolverTests
{
    /// <summary>候補列を簡潔に生成する</summary>
    private static ForeignKeyColumnResolver.CandidateColumn Col(
        string name,
        bool isPk = false,
        bool isFk = false,
        string? dataType = "int",
        bool isUsed = false
    ) => new(name, isPk, isFk, dataType, isUsed);

    /// <summary>既定引数（Customer テーブル、PK Id:int、非自己参照）で解決する</summary>
    private static int? Resolve(
        IReadOnlyList<ForeignKeyColumnResolver.CandidateColumn> columns,
        string sourceTable = "Customer",
        string? pkName = "Id",
        string? pkType = "int",
        bool selfRef = false
    ) =>
        ForeignKeyColumnResolver.ResolveTargetColumnIndex(
            sourceTable,
            pkName,
            pkType,
            columns,
            selfRef
        );

    /// <summary>①: 親テーブル名+Id（パスカルケース）と一致する列が選ばれることを検証する</summary>
    [Fact(DisplayName = "①: 親テーブル名+Id の列が最優先で選ばれる")]
    public void Rank1_TableNameIdConvention_IsPreferred()
    {
        var columns = new[]
        {
            Col("Id", isPk: true),
            Col("OrderDate", dataType: "datetime2"),
            Col("CustomerId"),
        };

        Resolve(columns).Should().Be(2);
    }

    /// <summary>①: スネークケース（親テーブル名_id）でも一致することを検証する</summary>
    [Fact(DisplayName = "①: スネークケースの customer_id も一致する")]
    public void Rank1_SnakeCaseConvention_Matches()
    {
        var columns = new[] { Col("id", isPk: true), Col("customer_id") };

        Resolve(columns, sourceTable: "customer", pkName: "id").Should().Be(1);
    }

    /// <summary>①: 複数形テーブル名（Customers）でも単数形化した CustomerId に一致することを検証する</summary>
    [Fact(DisplayName = "①: 複数形テーブル名 Customers でも CustomerId に一致する")]
    public void Rank1_PluralTableName_MatchesSingularizedFkName()
    {
        var columns = new[] { Col("Id", isPk: true), Col("CustomerId") };

        Resolve(columns, sourceTable: "Customers").Should().Be(1);
    }

    /// <summary>②: ①が無い場合に参照元キー列と同名の列が選ばれることを検証する</summary>
    [Fact(DisplayName = "②: 参照元キー列と同名の列が選ばれる")]
    public void Rank2_SameNameAsSourceKey_IsSelected()
    {
        var columns = new[] { Col("OrderId", isPk: true), Col("CustomerKey") };

        Resolve(columns, pkName: "CustomerKey").Should().Be(1);
    }

    /// <summary>③: 名前一致が無い場合に IsForeignKey=true の列が選ばれることを検証する</summary>
    [Fact(DisplayName = "③: 外部キーとしてマーク済みの列が選ばれる")]
    public void Rank3_ForeignKeyFlaggedColumn_IsSelected()
    {
        var columns = new[]
        {
            Col("Id", isPk: true),
            Col("Name", dataType: "nvarchar(50)"),
            Col("OwnerId", isFk: true),
        };

        Resolve(columns).Should().Be(2);
    }

    /// <summary>④: ロール付き FK 名（BillingCustomerId）が後方一致で選ばれることを検証する</summary>
    [Fact(DisplayName = "④: ロール付き FK 名が後方一致で選ばれる")]
    public void Rank4_RolePrefixedFkName_MatchesBySuffix()
    {
        var columns = new[]
        {
            Col("Id", isPk: true),
            Col("OrderDate", dataType: "datetime2"),
            Col("BillingCustomerId"),
        };

        Resolve(columns).Should().Be(2);
    }

    /// <summary>①が④より優先されることを検証する（完全一致 > 後方一致）</summary>
    [Fact(DisplayName = "完全一致（①）は後方一致（④）より優先される")]
    public void ExactMatch_BeatsSuffixMatch()
    {
        var columns = new[] { Col("BillingCustomerId"), Col("CustomerId") };

        Resolve(columns).Should().Be(1);
    }

    /// <summary>主キー列は名前が一致しても候補から除外されることを検証する</summary>
    [Fact(DisplayName = "主キー列は候補から除外される")]
    public void PrimaryKeyColumns_AreExcluded()
    {
        var columns = new[]
        {
            Col("CustomerId", isPk: true),
            Col("Note", dataType: "nvarchar(100)"),
        };

        Resolve(columns).Should().BeNull();
    }

    /// <summary>他リレーションで使用済みの列が除外され、次点の列が選ばれることを検証する</summary>
    [Fact(DisplayName = "使用済みの列は除外され次点の候補が選ばれる")]
    public void UsedColumns_AreExcluded()
    {
        var columns = new[]
        {
            Col("Id", isPk: true),
            Col("CustomerId", isUsed: true),
            Col("OwnerCustomerId"),
        };

        Resolve(columns).Should().Be(2);
    }

    /// <summary>どの規則にも該当しない場合は null（未割当）となることを検証する</summary>
    [Fact(DisplayName = "該当列が無ければ未割当（null）となる")]
    public void NoMatch_ReturnsNull()
    {
        var columns = new[]
        {
            Col("Id", isPk: true),
            Col("Quantity"),
            Col("Note", dataType: "nvarchar(100)"),
        };

        Resolve(columns).Should().BeNull();
    }

    /// <summary>同率の場合に参照元キー列とデータ型が一致する列が優先されることを検証する</summary>
    [Fact(DisplayName = "同率時はデータ型が一致する列が優先される")]
    public void Tie_IsBrokenByDataTypeMatch()
    {
        // 両方とも③（FK マーク済み）で同率だが、型一致する 2 番目が優先される
        var columns = new[]
        {
            Col("Id", isPk: true),
            Col("RegionCode", isFk: true, dataType: "nvarchar(10)"),
            Col("OwnerId", isFk: true, dataType: "bigint"),
        };

        Resolve(columns, pkType: "bigint").Should().Be(2);
    }

    /// <summary>同率かつ型も同じ場合は宣言順で先の列が選ばれることを検証する</summary>
    [Fact(DisplayName = "同率かつ同型なら宣言順で先の列が選ばれる")]
    public void Tie_FallsBackToDeclarationOrder()
    {
        var columns = new[]
        {
            Col("Id", isPk: true),
            Col("OwnerId", isFk: true),
            Col("ManagerId", isFk: true),
        };

        Resolve(columns).Should().Be(1);
    }

    /// <summary>自己参照では ParentId が①の候補として選ばれることを検証する</summary>
    [Fact(DisplayName = "自己参照では ParentId が選ばれる")]
    public void SelfReference_ParentId_IsSelected()
    {
        var columns = new[]
        {
            Col("Id", isPk: true),
            Col("Name", dataType: "nvarchar(50)"),
            Col("ParentId"),
        };

        Resolve(columns, sourceTable: "Employee", selfRef: true).Should().Be(2);
    }

    /// <summary>自己参照では Parent+キー列名（ParentEmployeeId）も一致することを検証する</summary>
    [Fact(DisplayName = "自己参照では Parent+キー列名も一致する")]
    public void SelfReference_ParentPlusKeyName_Matches()
    {
        var columns = new[] { Col("EmployeeId", isPk: true), Col("ParentEmployeeId") };

        Resolve(columns, sourceTable: "Employee", pkName: "EmployeeId", selfRef: true)
            .Should()
            .Be(1);
    }

    /// <summary>期待 FK 名の一覧にパスカル・スネーク・単数形化の変体が含まれることを検証する</summary>
    [Fact(DisplayName = "期待 FK 名にパスカル・スネーク・単数形化の変体が含まれる")]
    public void BuildExpectedForeignKeyNames_ContainsAllVariants()
    {
        var names = ForeignKeyColumnResolver.BuildExpectedForeignKeyNames("customer_orders");

        names.Should().Contain("CustomerOrdersId");
        names.Should().Contain("customer_orders_id");
        names.Should().Contain("CustomerOrderId");
        names.Should().Contain("customer_order_id");
    }
}
