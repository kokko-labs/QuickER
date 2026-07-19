using FluentAssertions;
using QuickER.CodeGen.UI;
using QuickER.Model;
using QuickER.Tests.TestDoubles;

namespace QuickER.Tests.CodeGen.UI;

/// <summary>
/// <see cref="QueryConditionRenameFollower"/> が、列リネーム通知を受けて対象エンティティのクエリ条件のみを
/// 書き換え、他エンティティを巻き込まず、該当クエリが無ければ差し替えを行わないことを検証するテストクラス。
/// </summary>
/// <remarks>
/// <c>MainViewModel.OnColumnRenamed</c> の旧・条件書き換えロジックを移植した機能の検証。
/// ホストは <see cref="StubErDiagramHost"/> を用い、<c>RaiseColumnRenamed</c> で通知を発火させる。
/// </remarks>
public class QueryConditionRenameFollowerTests
{
    /// <summary>リネームされた列を持つエンティティのクエリ条件だけが新名へ書き換わることを検証する</summary>
    [Fact(DisplayName = "対象エンティティのクエリ条件のみ書き換わり他エンティティは不変")]
    public void ColumnRenamed_RewritesOnlyMatchingEntityQueries()
    {
        var entityA = Guid.NewGuid();
        var entityB = Guid.NewGuid();
        var queryA = new QueryDefinition
        {
            EntityId = entityA,
            Name = "GetByCustomerA",
            Condition = "CustomerId = @customerId",
        };
        var queryB = new QueryDefinition
        {
            EntityId = entityB,
            Name = "GetByCustomerB",
            // 別エンティティに同名列があっても巻き込まない
            Condition = "CustomerId = @customerId",
        };
        var host = new StubErDiagramHost
        {
            DiagramToReturn = new ErDiagram { Queries = { queryA, queryB } },
        };
        var follower = new QueryConditionRenameFollower(host);
        follower.Attach();

        host.RaiseColumnRenamed(entityA, "CustomerId", "BuyerId");

        // 対象エンティティ A のクエリ条件だけが書き換わる
        queryA.Condition.Should().Be("BuyerId = @customerId");
        // 他エンティティ B のクエリ条件は不変
        queryB.Condition.Should().Be("CustomerId = @customerId");
        // 1 件以上書き換えたため、全クエリ一覧が書き戻される
        host.LastReplacedQueries.Should().NotBeNull();
        host.LastReplacedQueries.Should().Contain(queryA).And.Contain(queryB);
    }

    /// <summary>対象エンティティに条件式を持つクエリが無ければ差し替え（自動保存）を行わないことを検証する</summary>
    [Fact(DisplayName = "該当クエリが無ければ ReplaceQueries を呼ばない")]
    public void ColumnRenamed_NoMatchingQuery_DoesNotReplace()
    {
        var entityWithQuery = Guid.NewGuid();
        var renamedEntity = Guid.NewGuid();
        var host = new StubErDiagramHost
        {
            DiagramToReturn = new ErDiagram
            {
                Queries =
                {
                    new QueryDefinition
                    {
                        EntityId = entityWithQuery,
                        Name = "GetByCustomer",
                        Condition = "CustomerId = @customerId",
                    },
                },
            },
        };
        var follower = new QueryConditionRenameFollower(host);
        follower.Attach();

        // クエリを持たない別エンティティの列がリネームされても書き戻さない
        host.RaiseColumnRenamed(renamedEntity, "CustomerId", "BuyerId");

        host.LastReplacedQueries.Should().BeNull();
    }

    /// <summary>条件式が null のクエリしか無いエンティティのリネームでも差し替えを行わないことを検証する</summary>
    [Fact(DisplayName = "条件式 null のクエリのみなら ReplaceQueries を呼ばない")]
    public void ColumnRenamed_ConditionlessQueries_DoesNotReplace()
    {
        var entityId = Guid.NewGuid();
        var host = new StubErDiagramHost
        {
            DiagramToReturn = new ErDiagram
            {
                Queries =
                {
                    // 条件式を持たない（Condition = null）クエリは書き換え対象にならない
                    new QueryDefinition { EntityId = entityId, Name = "GetAll" },
                },
            },
        };
        var follower = new QueryConditionRenameFollower(host);
        follower.Attach();

        host.RaiseColumnRenamed(entityId, "CustomerId", "BuyerId");

        host.LastReplacedQueries.Should().BeNull();
    }
}
