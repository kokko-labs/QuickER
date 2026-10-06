using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedQueryFixture;
using QuickER.Tests.Integration;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// EF Core 版リポジトリが、主キーで引く操作（<c>GetByIdAsync</c> / <c>DeleteAsync</c>）のキーを SQL のパラメータで送ることを検証する。
/// </summary>
/// <remarks>
/// キーを式木の定数として渡すと、EF Core はそれを SQL へリテラルで書き込み、キーの値ごとにクエリのコンパイルと
/// DB のプランが増える。値オブジェクトのキー（<c>CustomerIdValue</c>）を持つ QueryFixture を実ファイルの SQLite で動かし、
/// 送られたコマンドをインターセプターで捕まえて見る。Docker 不要（CI 常時実行）。
/// </remarks>
public sealed class EfCoreKeyParameterRuntimeTests : IDisposable
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private const int ProbeKey = 4242;

    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();

    private readonly List<ServiceProvider> _providers = [];

    public void Dispose()
    {
        foreach (var provider in _providers)
        {
            provider.Dispose();
        }

        _db.Dispose();
    }

    /// <summary>送られたコマンドの本文とパラメータの値を記録するインターセプター</summary>
    private sealed class CommandRecorder : DbCommandInterceptor
    {
        public ConcurrentQueue<(string Text, object?[] Values)> Commands { get; } = new();

        private void Record(DbCommand command) =>
            Commands.Enqueue(
                (
                    command.CommandText,
                    command.Parameters.Cast<DbParameter>().Select(p => p.Value).ToArray()
                )
            );

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            Record(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            Record(command);
            return ValueTask.FromResult(result);
        }
    }

    private async Task<(ICustomerRepository Customers, CommandRecorder Recorder)> BuildAsync()
    {
        await _db.ResetSchemaAsync(Ct);
        await _db.ApplyDdlAsync(QueryFixtureDefinition.Build(), Ct);

        var recorder = new CommandRecorder();
        var provider = new ServiceCollection()
            .AddGeneratedEfCoreRepositories(options =>
                options.UseSqlite(_db.ReadWriteCreateConnectionString).AddInterceptors(recorder)
            )
            .BuildServiceProvider();
        _providers.Add(provider);

        return (provider.GetRequiredService<ICustomerRepository>(), recorder);
    }

    /// <summary>customers を対象にしたコマンドが、キーをリテラルでなくパラメータで送ったことを確かめる</summary>
    private static void ShouldSendKeyAsParameter(CommandRecorder recorder)
    {
        var command = recorder.Commands.Should().ContainSingle().Which;

        command.Text.Should().Contain("customers");
        command.Text.Should().NotContain(ProbeKey.ToString(), "キーを SQL へリテラルで書かない");
        command
            .Values.Select(value => value is null ? (long?)null : Convert.ToInt64(value))
            .Should()
            .Contain(ProbeKey, "キーはパラメータの値として送る");
    }

    [Fact(DisplayName = "EF Core: GetByIdAsync は主キーをパラメータで送る")]
    public async Task GetById_SendsKeyAsParameter()
    {
        var (customers, recorder) = await BuildAsync();

        (await customers.GetByIdAsync(CustomerIdValue.Create(ProbeKey), Ct)).Should().BeNull();

        ShouldSendKeyAsParameter(recorder);
    }

    [Fact(DisplayName = "EF Core: DeleteAsync は主キーをパラメータで送る")]
    public async Task Delete_SendsKeyAsParameter()
    {
        var (customers, recorder) = await BuildAsync();

        (await customers.DeleteAsync(CustomerIdValue.Create(ProbeKey), Ct)).Should().BeFalse();

        ShouldSendKeyAsParameter(recorder);
    }
}
