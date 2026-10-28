using System.Reflection;
using System.Text;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Tests.Integration.Dialects.SyncPlanFuzz;
using QuickER.Tests.Integration.GeneratedRuntime;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration;

/// <summary>
/// Integration にある乱数のエンジン（同期のモデル・同期計画の総当たり・実装先どうしの一致）の種の指紋を、
/// 記録（<c>engine-fingerprints.json</c>）と照合する
/// </summary>
/// <remarks>
/// <para>
/// 指紋の役割は「生成器の乱数の引き方の変化を検出する」ことで、実行の列そのものは守らない（実行の列は製品を直せば正当に変わるので、
/// 指紋に含めると製品の修正のたびに赤になる）。生成だけを呼び、DB・同期・Docker は使わない（windows・ubuntu のどちらでも流れる）。
/// 要約は生成器が作った入力を読むだけで、生成器の乱数を余分に引かない。
/// </para>
/// <list type="bullet">
/// <item>同期のモデル：生成器は 1 手ごとに実 DB から読んだ状態を見て次の手を選ぶので、実行の列は生成だけでは作れない。種の組
/// （Mode・Policy・Extras・Faults）・初期データの手・「初期データを当てた状態」に 3 者の状態を固定して 40 回選んだ手を要約にする
/// （1 手目は実際の実行と同じで、2 手目からは実際の列と違うが、決定的）。転送経路で生成は変わらないので、直結の種（0 から）だけを
/// 記録し、HTTP・SQL Server の種がその部分集合であることを確かめる。</item>
/// <item>同期計画の総当たり：生成器は DB から取り込んだ図に変更を当てる。取り込む代わりに生成した図をそのまま新しい Id へ写して
/// 変更・差分・選び方を作る（取込の正規化は通らない）。方言の部品で生成が変わるので、方言ごとにエンジン ID を分ける。</item>
/// <item>実装先どうしの一致：生成器は実行と分かれている。シナリオの名前・初期データ・操作の列を、record の中の一覧も展開して要約にする。</item>
/// </list>
/// <para>
/// 記録の書き直しは <c>QUICKER_REGEN_FIXTURES=1</c>（生成フィクスチャのドリフトと同じ 1 コマンド）。書き直しは承認であって検証ではない。
/// 1 つのクラスにまとめてあるので、xUnit は同じ記録の書き直しを並べて走らせない。
/// </para>
/// </remarks>
// 種を使うが夜間に流さない: 普段の CI が記録の全種を照合し、夜間に増える種は記録に無いので照合の対象が増えない
// （流すと、増えた種の入力の生成の時間だけが掛かる）
[Trait(NightlyEngineTrait.Name, NightlyEngineTrait.NotSelected)]
public sealed class EngineFingerprintDriftTests
{
    /// <summary>記録のファイル（リポジトリ直下からの相対パス）</summary>
    private const string RecordPath = "tests/QuickER.Tests.Integration/engine-fingerprints.json";

    /// <summary>同期計画の総当たりの方言と既定の種の数（Oracle は <c>QUICKER_SYNC_FUZZ_ORACLE</c> を見ずに既定の 4）</summary>
    public static TheoryData<string> SyncPlanDialects() =>
        new("sqlite", "mysql", "sqlserver", "postgresql", "oracle");

    [Fact(DisplayName = "[指紋] 同期のモデルの生成器は、記録と同じ入力を作る")]
    public void SyncModel_GeneratesRecordedInputs() =>
        Verify("SyncModel", SyncModelRuntimeTests.Seeds, SyncModelSummary);

    [Fact(DisplayName = "[指紋] 同期のモデルの転送経路の種は、直結の種の部分集合")]
    public void SyncModel_TransportSeedsAreRecorded()
    {
        var direct = Enumerable
            .Range(SyncModelRuntimeTests.Seeds.FirstSeed, SyncModelRuntimeTests.Seeds.RegularCount)
            .ToHashSet();
        var transport = SyncModelSignatures.Seeds;
        var http = Enumerable.Range(transport.FirstSeed, transport.RegularCount);
        var sqlServer = Enumerable
            .Range(transport.FirstSeed, transport.RegularCount * 4)
            .Where(seed => seed % 4 is 0 or 1)
            .Take(transport.RegularCount);

        Assert.Subset(direct, http.Concat(sqlServer).ToHashSet());
    }

    [Theory(DisplayName = "[指紋] 同期計画の総当たりの生成器は、方言ごとに記録と同じ入力を作る")]
    [MemberData(nameof(SyncPlanDialects))]
    public void SyncPlan_GeneratesRecordedInputs(string dialectName)
    {
        var dialect = CreateDialect(dialectName);

        try
        {
            Verify(
                $"SyncPlan.{dialectName}",
                SyncPlanSeeds(dialectName),
                seed => SyncPlanSummary(dialect, seed)
            );
        }
        finally
        {
            (dialect as IDisposable)?.Dispose();
        }
    }

    [Fact(DisplayName = "[指紋] 実装先どうしの一致の生成器は、記録と同じ操作の列を作る")]
    public void BackendParity_GeneratesRecordedInputs() =>
        Verify("BackendParity", BackendParityRuntimeTests.Seeds, ParitySummary);

    /// <summary>種ごとの要約の指紋を記録と照合する（書き直しの指定があれば記録を書き直す）</summary>
    private static void Verify(string engineId, SeedSettings settings, Func<int, string> summary)
    {
        var plan = SeedSource.Resolve(settings);
        var actual = SeedFingerprint.Compute(plan.Seeds, summary);
        FingerprintFile.Verify(
            Path.Combine(RepositoryRoot(), RecordPath),
            engineId,
            actual,
            plan,
            IsRegenerationRequested()
        );
    }

    // ---------------- 同期のモデル ----------------

    /// <summary>同期のモデル：種の組・初期データ・初期データの状態に固定して選んだ手</summary>
    internal static string SyncModelSummary(int seed)
    {
        var generator = new SyncModelGenerator(seed);
        var text = new StringBuilder();
        text.Append("mode=")
            .Append(generator.Mode)
            .Append(" policy=")
            .Append(generator.Policy)
            .Append(" extras=")
            .Append(generator.Extras)
            .Append(" faults=")
            .Append(generator.Faults)
            .Append('\n');

        var setup = generator.Setup();

        foreach (var op in setup)
        {
            text.Append("setup ").Append(SeedSummary.Object(op)).Append('\n');
        }

        var states = Enum.GetValues<ModelActor>()
            .ToDictionary(actor => actor, _ => StateAfter(setup));

        for (var step = 0; step < SyncModelRuntimeTests.StepsPerSeed; step++)
        {
            foreach (var op in generator.Next(states))
            {
                text.Append(step).Append(' ').Append(SeedSummary.Object(op)).Append('\n');
            }
        }

        return text.ToString();
    }

    /// <summary>初期データの手（追加だけ）を当てた状態（同期で 3 者がこの内容にそろった後の状態）</summary>
    private static ModelDbState StateAfter(IReadOnlyList<ModelWriteOp> setup)
    {
        var state = new ModelDbState();

        foreach (var op in setup)
        {
            switch (op)
            {
                case AddFolderOp folder:
                    state.Folders[folder.Id] = (folder.ParentId, folder.Name);
                    break;
                case AddLabelOp label:
                    state.Labels[label.Code] = (label.FolderId, label.Caption);
                    break;
                case AddItemOp item:
                    state.Items[item.Id] = (
                        item.FolderId,
                        item.LabelCode,
                        item.Code,
                        item.Title,
                        item.PayloadHex
                    );
                    break;
                case AddEventOp @event:
                    state.Events[@event.KeyText] = (@event.At, @event.ItemId, @event.Memo);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"初期データに追加でない手がある: {op.Describe()}"
                    );
            }
        }

        return state;
    }

    // ---------------- 同期計画の総当たり ----------------

    internal static SeedSettings SyncPlanSeeds(string dialect) =>
        dialect switch
        {
            "sqlite" => SqliteSyncPlanFuzzTests.Seeds,
            "mysql" => MySqlSyncPlanFuzzTests.Seeds,
            "sqlserver" => SqlServerSyncPlanFuzzTests.Seeds,
            "postgresql" => PostgreSqlSyncPlanFuzzTests.Seeds,
            "oracle" => OracleSyncPlanFuzzTests.SeedsFor(extended: false),
            _ => throw new ArgumentOutOfRangeException(nameof(dialect), dialect, null),
        };

    /// <summary>
    /// 方言の部品を作る（Docker の方言は fixture を渡さない＝生成が使うメンバーは fixture に触れず、接続を開くメンバーは呼ばない）
    /// </summary>
    internal static SyncPlanFuzzDialect CreateDialect(string name) =>
        name switch
        {
            "sqlite" => new SqliteSyncPlanFuzzDialect(),
            "mysql" => new MySqlSyncPlanFuzzDialect(null!),
            "sqlserver" => new SqlServerSyncPlanFuzzDialect(null!),
            "postgresql" => new PostgreSqlSyncPlanFuzzDialect(null!),
            "oracle" => new OracleSyncPlanFuzzDialect(null!),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
        };

    /// <summary>
    /// 同期計画の総当たり：DB 側の図とデータ・目標の図への変更・差分の選び方（取り込む代わりに生成した図を使う）
    /// </summary>
    internal static string SyncPlanSummary(SyncPlanFuzzDialect dialect, int seed)
    {
        var generator = new SyncPlanFuzzGenerator(new Random(seed), dialect);
        var live = generator.BuildLive();
        live.Diagram.TargetDbms = dialect.Provider.Name;

        var text = new StringBuilder();
        text.Append("live\n")
            .Append(DescribeSchema(live.Diagram.Entities, live.Diagram.Relationships));

        foreach (var (key, info) in live.Columns.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            text.Append("column-info ")
                .Append(SeedSummary.Quote(key))
                .Append(' ')
                .Append(SeedSummary.Object(info))
                .Append('\n');
        }

        foreach (var (table, count) in live.RowCounts.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            text.Append("rows ")
                .Append(SeedSummary.Quote(table))
                .Append('=')
                .Append(count)
                .Append('\n');
        }

        foreach (var statement in live.InsertStatements.Concat(live.ComputedColumnStatements))
        {
            text.Append("sql ").Append(SeedSummary.Quote(statement)).Append('\n');
        }

        text.Append("shapes ")
            .Append(string.Join(",", live.Shapes.Order(StringComparer.Ordinal)))
            .Append('\n');

        var before = new SchemaImportResult
        {
            Entities = [.. live.Diagram.Entities],
            Relationships = [.. live.Diagram.Relationships],
        };
        var (targetEntities, targetRelationships) = SyncPlanFuzzRunner.CloneWithFreshIds(before);
        generator.Mutate(targetEntities, targetRelationships, live);

        foreach (var mutation in generator.Mutations)
        {
            text.Append("mutation ").Append(SeedSummary.Quote(mutation)).Append('\n');
        }

        text.Append("target\n").Append(DescribeSchema(targetEntities, targetRelationships));

        var diff = new SchemaDiffService().Compute(
            before.Entities,
            before.Relationships,
            targetEntities,
            targetRelationships,
            dialect.Provider.SyncCapabilities
        );
        text.Append("selection ").Append(generator.Select(diff.Items)).Append('\n');

        foreach (var item in diff.Items)
        {
            text.Append("diff ")
                .Append(item.Kind)
                .Append(" selectable=")
                .Append(item.IsSelectable)
                .Append(" selected=")
                .Append(item.IsSelected)
                .Append(' ')
                .Append(SeedSummary.Quote(item.Description))
                .Append('\n');
        }

        return text.ToString();
    }

    /// <summary>図を名前で書く（Id は乱数でなく Guid なので書かない）</summary>
    private static string DescribeSchema(
        IEnumerable<Entity> entities,
        IEnumerable<Relationship> relationships
    )
    {
        var text = new StringBuilder();
        var entityList = entities.ToList();
        var columnNames = entityList
            .SelectMany(e => e.Columns.Select(c => (c.Id, Name: $"{e.TableName}.{c.Name}")))
            .ToDictionary(p => p.Id, p => p.Name);
        var tableNames = entityList.ToDictionary(e => e.Id, e => e.TableName);

        string ColumnName(Guid id) => columnNames.TryGetValue(id, out var name) ? name : "?";

        foreach (var entity in entityList)
        {
            text.Append("  table ").Append(SeedSummary.Quote(entity.TableName)).Append('\n');

            foreach (var column in entity.Columns)
            {
                text.Append("    column ")
                    .Append(SeedSummary.Quote(column.Name))
                    .Append(' ')
                    .Append(SeedSummary.Quote(column.DataType))
                    .Append(" pk=")
                    .Append(column.IsPrimaryKey)
                    .Append(" fk=")
                    .Append(column.IsForeignKey)
                    .Append(" null=")
                    .Append(column.IsNullable)
                    .Append(" computed=")
                    .Append(column.IsComputed)
                    .Append(" identity=")
                    .Append(column.IsIdentity)
                    .Append('\n');
            }

            text.Append("    pk-order ")
                .Append(
                    string.Join(
                        ",",
                        entity.GetPrimaryKeyColumnsInOrder().Select(c => SeedSummary.Quote(c.Name))
                    )
                )
                .Append(" stored=")
                .Append(string.Join(",", entity.PrimaryKeyColumnIds.Select(ColumnName)))
                .Append('\n');

            foreach (var unique in entity.UniqueConstraints)
            {
                text.Append("    unique ")
                    .Append(SeedSummary.Quote(unique.Name))
                    .Append(' ')
                    .Append(string.Join(",", unique.ColumnIds.Select(ColumnName)))
                    .Append('\n');
            }
        }

        foreach (var relationship in relationships)
        {
            text.Append("  relationship ")
                .Append(tableNames.GetValueOrDefault(relationship.SourceEntityId, "?"))
                .Append(" -> ")
                .Append(tableNames.GetValueOrDefault(relationship.TargetEntityId, "?"))
                .Append(' ')
                .Append(relationship.Type)
                .Append(" name=")
                .Append(SeedSummary.Quote(relationship.ConstraintName))
                .Append(" delete=")
                .Append(relationship.OnDelete)
                .Append(" update=")
                .Append(relationship.OnUpdate)
                .Append(" pairs=")
                .Append(
                    string.Join(
                        ",",
                        relationship.ColumnPairs.Select(p =>
                            $"{ColumnName(p.SourceColumnId)}:{ColumnName(p.TargetColumnId)}"
                        )
                    )
                )
                .Append('\n');
        }

        return text.ToString();
    }

    // ---------------- 実装先どうしの一致 ----------------

    /// <summary>実装先どうしの一致：シナリオの名前・条件・初期データ・操作の列（record の中の一覧も展開する）</summary>
    internal static string ParitySummary(int seed)
    {
        var scenario = BackendParityGenerator.Create(seed);
        var text = new StringBuilder();
        text.Append("name ")
            .Append(scenario.Name)
            .Append(" options=")
            .Append(SeedSummary.Object(scenario.Options))
            .Append(" baseline=")
            .Append(scenario.CompareWithBaseline)
            .Append('\n');

        foreach (var op in scenario.Seeds)
        {
            text.Append("seed ").Append(SeedSummary.Object(op)).Append('\n');
        }

        foreach (var op in scenario.Ops)
        {
            text.Append("op ").Append(SeedSummary.Object(op)).Append('\n');
        }

        return text.ToString();
    }

    // ---------------- 補助 ----------------

    private static bool IsRegenerationRequested()
    {
        var value = Environment.GetEnvironmentVariable("QUICKER_REGEN_FIXTURES");

        return string.Equals(value, "1", StringComparison.Ordinal)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!
        );

        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "QuickER.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "リポジトリ直下（QuickER.slnx）が見つかりませんでした。"
        );
    }
}
