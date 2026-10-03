using AwesomeAssertions;
using QuickER.Model;
using QuickER.ViewModels;

namespace QuickER.Tests.Model;

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

    /// <summary>親の複合主キー（列宣言順 TenantId, RegionCode）と、同名の外部キー列を持つ子を組み立てる</summary>
    private static (Entity Parent, Entity Child) BuildCompositeKeyPair()
    {
        var tenantId = new Column
        {
            Name = "TenantId",
            DataType = "int",
            IsPrimaryKey = true,
        };
        var regionCode = new Column
        {
            Name = "RegionCode",
            DataType = "nvarchar(10)",
            IsPrimaryKey = true,
        };
        var parent = new Entity { TableName = "TenantRegion", Columns = { tenantId, regionCode } };
        var child = new Entity
        {
            TableName = "TenantUser",
            Columns =
            {
                new Column
                {
                    Name = "TenantUserId",
                    DataType = "int",
                    IsPrimaryKey = true,
                },
                new Column { Name = "TenantId", DataType = "int" },
                new Column { Name = "RegionCode", DataType = "nvarchar(10)" },
            },
        };

        // 主キーの実効順を列宣言順の逆（RegionCode, TenantId）へ上書きする
        parent.PrimaryKeyColumnIds = [regionCode.Id, tenantId.Id];

        return (parent, child);
    }

    /// <summary>
    /// ViewModel を写した意味モデル（GUI の呼び方）でも、自動ペア化が親主キーの実効順に従うことを検証する
    /// </summary>
    /// <remarks>
    /// GUI は ViewModel を <c>ToModel()</c> で写して渡すので、写しが主キーの順序を運ぶことまでを確かめる
    /// </remarks>
    [Fact(DisplayName = "自動ペア化（ViewModel 版）は親主キーの実効順で列ペアを作る")]
    public void ResolveColumnPairs_ViewModel_FollowsPrimaryKeyOrder()
    {
        var (parentModel, childModel) = BuildCompositeKeyPair();
        var parent = new EntityViewModel(parentModel);
        var child = new EntityViewModel(childModel);

        var pairs = ForeignKeyColumnResolver.ResolveColumnPairs(
            parent.ToModel(),
            child.ToModel(),
            Array.Empty<Relationship>()
        );

        pairs
            .Select(pair => parent.Columns.Single(c => c.Id == pair.SourceColumnId).Name)
            .Should()
            .Equal("RegionCode", "TenantId");
    }

    /// <summary>自動ペア化（意味モデル版）が親主キーの実効順に従うことを検証する</summary>
    [Fact(DisplayName = "自動ペア化（意味モデル版）は親主キーの実効順で列ペアを作る")]
    public void ResolveColumnPairs_Model_FollowsPrimaryKeyOrder()
    {
        var (parent, child) = BuildCompositeKeyPair();

        var pairs = ForeignKeyColumnResolver.ResolveColumnPairs(
            parent,
            child,
            Array.Empty<Relationship>()
        );

        pairs
            .Select(pair => parent.Columns.Single(c => c.Id == pair.SourceColumnId).Name)
            .Should()
            .Equal("RegionCode", "TenantId");
    }

    /// <summary>テスト用の列を作る</summary>
    private static Column NewColumn(
        string name,
        bool isPk = false,
        bool isFk = false,
        string dataType = "int"
    ) =>
        new()
        {
            Name = name,
            DataType = dataType,
            IsPrimaryKey = isPk,
            IsForeignKey = isFk,
        };

    /// <summary>親と子の 2 テーブルで自動ペア化し、列ペアを「親列→子列」の名前で返す</summary>
    private static List<string> PairNames(Entity parent, Entity child) =>
        ForeignKeyColumnResolver
            .ResolveColumnPairs(parent, child, Array.Empty<Relationship>())
            .Select(pair =>
                $"{parent.Columns.Single(c => c.Id == pair.SourceColumnId).Name}→{child.Columns.Single(c => c.Id == pair.TargetColumnId).Name}"
            )
            .ToList();

    /// <summary>
    /// 複合主キーの 2 列目が、親テーブル名由来の名前（①）で 1 列目と同じ子列を引かないことを検証する
    /// </summary>
    [Fact(
        DisplayName = "複合主キーの各列は同名の子列へ付く（2 列目が親テーブル名由来の列を引かない）"
    )]
    public void ResolveColumnPairs_CompositeKey_SecondColumnDoesNotReuseTableDerivedColumn()
    {
        var parent = new Entity
        {
            TableName = "Orders",
            Columns = { NewColumn("OrderId", isPk: true), NewColumn("Revision", isPk: true) },
        };
        var child = new Entity
        {
            TableName = "OrderNotes",
            Columns =
            {
                NewColumn("NoteId", isPk: true),
                NewColumn("OrderId"),
                NewColumn("Revision"),
            },
        };

        PairNames(parent, child).Should().Equal("OrderId→OrderId", "Revision→Revision");
    }

    /// <summary>
    /// 複合主キーの 1 列目が、親テーブル名由来の名前（①）で 2 列目の同名の子列を取り違えないことを検証する
    /// </summary>
    [Fact(DisplayName = "複合主キーの 1 列目が後ろの列の同名の子列を取り違えない")]
    public void ResolveColumnPairs_CompositeKey_FirstColumnDoesNotTakeLaterColumnsSameName()
    {
        var parent = new Entity
        {
            TableName = "Projects",
            Columns = { NewColumn("TenantId", isPk: true), NewColumn("ProjectId", isPk: true) },
        };
        var child = new Entity
        {
            TableName = "Tasks",
            Columns =
            {
                NewColumn("TaskId", isPk: true),
                NewColumn("TenantId"),
                NewColumn("ProjectId"),
            },
        };

        PairNames(parent, child).Should().Equal("TenantId→TenantId", "ProjectId→ProjectId");
    }

    /// <summary>
    /// 複合主キーでは、前の列が下位の順位（③の外部キー印）で後ろの列の同名の子列を奪わないことを検証する
    /// </summary>
    [Fact(DisplayName = "複合主キーでは同名の子列への割り当てを全列について先に済ませる")]
    public void ResolveColumnPairs_CompositeKey_SameNameIsAssignedBeforeLowerRanks()
    {
        // Code は同名の子列を持たず、③（外部キー印）で解く。印は TenantId と AccountCode の両方にあり、
        // 型も同じなので宣言順なら TenantId が先に来る＝同名の割り当てを先に済ませないと TenantId を奪う
        var parent = new Entity
        {
            TableName = "Accounts",
            Columns = { NewColumn("Code", isPk: true), NewColumn("TenantId", isPk: true) },
        };
        var child = new Entity
        {
            TableName = "Ledgers",
            Columns =
            {
                NewColumn("LedgerId", isPk: true),
                NewColumn("TenantId", isFk: true),
                NewColumn("AccountCode", isFk: true),
            },
        };

        PairNames(parent, child).Should().Equal("Code→AccountCode", "TenantId→TenantId");
    }

    /// <summary>このペア化で既に選んだ子列が、後ろの列の候補から外れることを検証する</summary>
    [Fact(DisplayName = "このペア化で既に選んだ子列は後ろの列の候補から外れる")]
    public void ResolveColumnPairs_CompositeKey_ChosenColumnIsExcludedFromLaterColumns()
    {
        // Id は①で OrderId を取る。Revision は同名の子列が無く、①で再び OrderId を引けると
        // 使用済みとして捨てられる＝候補から外れていれば③（外部キー印）で OrderRevision に付く
        var parent = new Entity
        {
            TableName = "Orders",
            Columns = { NewColumn("Id", isPk: true), NewColumn("Revision", isPk: true) },
        };
        var child = new Entity
        {
            TableName = "OrderNotes",
            Columns =
            {
                NewColumn("NoteId", isPk: true),
                NewColumn("OrderId", isFk: true),
                NewColumn("OrderRevision", isFk: true),
            },
        };

        PairNames(parent, child).Should().Equal("Id→OrderId", "Revision→OrderRevision");
    }

    /// <summary>主キーが 1 列のときは、同名の列より親テーブル名由来の名前（①）を先に見ることを検証する</summary>
    [Fact(
        DisplayName = "主キーが 1 列なら同名の列より親テーブル名由来の列を先に見る（従来どおり）"
    )]
    public void ResolveColumnPairs_SingleKey_PrefersTableDerivedNameOverSameName()
    {
        var parent = new Entity
        {
            TableName = "Customer",
            Columns = { NewColumn("CustomerKey", isPk: true) },
        };
        var child = new Entity
        {
            TableName = "Orders",
            Columns =
            {
                NewColumn("OrderId", isPk: true),
                NewColumn("CustomerKey"),
                NewColumn("CustomerId"),
            },
        };

        PairNames(parent, child).Should().Equal("CustomerKey→CustomerId");
    }

    /// <summary>既存リレーションが使う子列は、複合主キーの同名の割り当てでも候補から外れることを検証する</summary>
    [Fact(DisplayName = "既存リレーションが使う子列は同名の割り当てでも使わない")]
    public void ResolveColumnPairs_CompositeKey_ColumnsUsedByExistingRelationshipAreExcluded()
    {
        var parent = new Entity
        {
            TableName = "Projects",
            Columns = { NewColumn("TenantId", isPk: true), NewColumn("ProjectId", isPk: true) },
        };
        var child = new Entity
        {
            TableName = "Tasks",
            Columns =
            {
                NewColumn("TaskId", isPk: true),
                NewColumn("TenantId"),
                NewColumn("ProjectId"),
            },
        };
        var existing = new Relationship
        {
            SourceEntityId = Guid.NewGuid(),
            TargetEntityId = child.Id,
            ColumnPairs = { new RelationshipColumnPair(Guid.NewGuid(), child.Columns[1].Id) },
        };

        ForeignKeyColumnResolver
            .ResolveColumnPairs(parent, child, [existing])
            .Select(pair => child.Columns.Single(c => c.Id == pair.TargetColumnId).Name)
            .Should()
            .Equal("ProjectId");
    }

    /// <summary>
    /// 単数形が語尾から 1 つに決まらないテーブル名でも、親テーブル名_id の列が名前一致で選ばれることを検証する
    /// （外部キー印の付いた別の列が先にあっても、順位 3 へ落ちて取り違えない）
    /// </summary>
    [Theory(DisplayName = "①: courses / warehouses / statuses なども単数形の列名に一致する")]
    [InlineData("courses", "course_id")]
    [InlineData("purchases", "purchase_id")]
    [InlineData("responses", "response_id")]
    [InlineData("warehouses", "warehouse_id")]
    [InlineData("statuses", "status_id")]
    [InlineData("caches", "cache_id")]
    [InlineData("Courses", "CourseId")]
    public void Rank1_AmbiguousPluralTableName_MatchesSingularColumn(
        string sourceTable,
        string expectedColumn
    )
    {
        var columns = new[]
        {
            Col("id", isPk: true),
            Col("other_ref", isFk: true),
            Col(expectedColumn),
        };

        Resolve(columns, sourceTable: sourceTable, pkName: "id").Should().Be(2);
    }
}
