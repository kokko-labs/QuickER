using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Provider;

/// <summary>
/// DDL 生成（<see cref="IDdlGenerator.Build"/>）と差分同期の入口（<see cref="SchemaDiffService.Compute"/>）へ名前の総当たり
/// （<see cref="NameFuzz"/>）を流し、図の中の重複の判定が <see cref="NameIdentityRule"/> と同じ答えになることを確かめるテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 図は手で編集した JSON のように、名前の重複を断る入口（GUI・ツール・形式の取込）を通らずに名前の組を持ち得る
/// （図ファイルの読込検査は名前の重複を断らない）。規則が「同じ名前」と言う組を持つ図は、DDL も差分も名前を挙げて図全体を
/// 断り、「別の名前」と言う組は（1 つずつなら通る名前なら）2 つでも通ることを、5 方言の DDL と差分で表明する。
/// 差分の DB の側は空（図の表はすべて追加になる）で、DB の側の同じ名前の扱い（<c>NameLookup</c>）は見ない。
/// </para>
/// <para>結果は既知の失敗の一覧（<see cref="NameFuzzKnownFailures"/>）と厳密に照合する。</para>
/// </remarks>
public sealed class NameIdentityDdlFuzzTests
{
    private static readonly IDatabaseProvider[] Providers =
    [
        new SqlServerProvider(),
        new PostgreSqlProvider(),
        new MySqlProvider(),
        new OracleProvider(),
        new SqliteProvider(),
    ];

    [Fact(DisplayName = "DDL 生成と差分の図の重複の判定は名前の規則と同じ答えになる")]
    [Trait(NightlyEngineTrait.Name, NightlyEngineTrait.Selected)]
    public void DdlAndDiff_AgreeWithNameRule()
    {
        var ledger = new FuzzFailureLog(NameFuzzKnownFailures.Set);

        foreach (var provider in Providers)
        {
            NameFuzzRunner.Run(
                ledger,
                $"ddl:{provider.Name}:table",
                NameKind.Table,
                name => Ddl(provider, Tables(provider, name)).Accepted,
                pair => Ddl(provider, Tables(provider, pair.First, pair.Second)).Outcome
            );
            NameFuzzRunner.Run(
                ledger,
                $"ddl:{provider.Name}:column",
                NameKind.Column,
                name => Ddl(provider, Columns(provider, name)).Accepted,
                pair => Ddl(provider, Columns(provider, pair.First, pair.Second)).Outcome
            );
            NameFuzzRunner.Run(
                ledger,
                $"diff:{provider.Name}:table",
                NameKind.Table,
                name => Diff(provider, Tables(provider, name)).Accepted,
                pair => Diff(provider, Tables(provider, pair.First, pair.Second)).Outcome
            );
            NameFuzzRunner.Run(
                ledger,
                $"diff:{provider.Name}:column",
                NameKind.Column,
                name => Diff(provider, Columns(provider, name)).Accepted,
                pair => Diff(provider, Columns(provider, pair.First, pair.Second)).Outcome
            );
        }

        ledger.AssertMatchesKnown("ddl:");
        ledger.AssertMatchesKnown("diff:");
    }

    /// <summary>DDL を作れたか（断られたら例外の文を詳細にする）</summary>
    private static (bool Accepted, PairOutcome Outcome) Ddl(
        IDatabaseProvider provider,
        ErDiagram diagram
    ) => Attempt(() => provider.DdlGenerator.Build(diagram));

    /// <summary>空の DB に対する差分を組めたか（断られたら例外の文を詳細にする）</summary>
    private static (bool Accepted, PairOutcome Outcome) Diff(
        IDatabaseProvider provider,
        ErDiagram diagram
    ) =>
        Attempt(() =>
            new SchemaDiffService().Compute(
                [],
                [],
                diagram.Entities,
                diagram.Relationships,
                provider.SyncCapabilities
            )
        );

    private static (bool Accepted, PairOutcome Outcome) Attempt(Action action)
    {
        try
        {
            action();

            return (true, PairOutcome.Of(true, string.Empty));
        }
        catch (InvalidOperationException ex)
        {
            return (false, PairOutcome.Of(false, ex.Message));
        }
    }

    /// <summary>名前ごとのテーブル（主キー Pk だけ）を持つ図</summary>
    private static ErDiagram Tables(IDatabaseProvider provider, params string[] names) =>
        new()
        {
            TargetDbms = provider.Name,
            Entities = names.Select(name => Table(name, [])).ToList(),
        };

    /// <summary>テーブル T に名前ごとの列を持つ図</summary>
    private static ErDiagram Columns(IDatabaseProvider provider, params string[] names) =>
        new()
        {
            TargetDbms = provider.Name,
            Entities =
            [
                Table("T", names.Select(name => new Column { Name = name, DataType = "int" })),
            ],
        };

    private static Entity Table(string name, IEnumerable<Column> columns) =>
        new()
        {
            TableName = name,
            Columns =
            [
                new Column
                {
                    Name = "Pk",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                .. columns,
            ],
        };
}
