using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedRemoteServiceFixture;
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// リモートの保存で値を行と突き合わせる位置の並びを、サーバーが保存の前に受け取ったグラフで決めることを、
/// RemoteServiceFixture の自己参照の表 nodes で検証する（サーバーの実体は QuickER 版と EF Core 版・どちらも SQLite）。
/// </summary>
/// <remarks>
/// 親と子を別々の根として SaveMany すると、EF Core のサーバーでは保存の fix-up が子を親のコレクションへ入れるので、
/// 保存の後にグラフをたどると子が 2 回数えられ、クライアントの並びと数が合わずに値が 1 行も戻らなくなる。
/// </remarks>
[Trait("Category", "Integration")]
public sealed class RemoteSaveRowOrderRuntimeTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static NodeEntity NewNode(int id, int? parentId, string label)
    {
        var node = new NodeEntity
        {
            NodeId = NodeIdValue.Create(id),
            ParentNodeId = parentId is { } parent ? NodeIdValue.Create(parent) : null,
            Label = LabelValue.Create(label),
        };
        node.MarkAdded();
        return node;
    }

    [Theory(
        DisplayName = "[Remote/書き戻し] 自己参照の親と子を別々の根で保存しても、値はそれぞれの行へ戻る"
    )]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelfReferencingRootsSavedTogether_EachGetsItsValues(bool efCoreServer)
    {
        using var db = SqliteTempDatabase.Create();
        await db.ApplyDdlAsync(RemoteServiceFixtureDefinition.Build(), Ct);
        await using var server = await InProcessRemoteServer.StartAsync(
            services =>
            {
                if (efCoreServer)
                {
                    services.AddGeneratedEfCoreRepositories(options =>
                        Microsoft
                            .EntityFrameworkCore.SqliteDbContextOptionsBuilderExtensions.UseSqlite(
                                options,
                                db.ReadWriteCreateConnectionString
                            )
                            .IgnoreServiceProviderCacheLimit()
                    );
                }
                else
                {
                    services.AddGeneratedSqliteRepositories(db.ReadWriteCreateConnectionString);
                }

                services.AddSingleton<ISaveHook<NodeEntity>>(new BangOnInsert());
            },
            app => app.MapGeneratedRemoteEndpoints(RemoteAccess.AllowAnonymous),
            Ct
        );
        using var client = new ServiceCollection()
            .AddGeneratedHttpRemoteRepositories(server.BaseAddress(RemotePaths.DefaultPrefix))
            .BuildServiceProvider();
        var repository = client.GetRequiredService<INodeRemoteRepository>();

        // 子は親のコレクションへ入れず、別の根として送る
        var parent = NewNode(1, null, "p");
        var child = NewNode(2, 1, "c");

        await repository.SaveAsync([parent, child], cancellationToken: Ct);

        parent.Label.Value.Should().Be("p!");
        child.Label.Value.Should().Be("c!");
        parent.RowState.Should().Be(RowState.Unchanged);
        child.RowState.Should().Be(RowState.Unchanged);
    }

    /// <summary>挿入のときに Label の末尾へ "!" を足す</summary>
    private sealed class BangOnInsert : ISaveHook<NodeEntity>
    {
        public Task<bool> BeforeSaveAsync(
            NodeEntity entity,
            SaveOperation operation,
            CancellationToken cancellationToken = default
        )
        {
            if (operation == SaveOperation.Insert)
            {
                entity.Label = LabelValue.Create(entity.Label.Value + "!");
            }

            return Task.FromResult(true);
        }
    }
}
