using System.IO;
using System.Text;
using QuickER.Tests.GeneratedFixture;
using QuickER.Tests.Resources;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// QuickER.Tests にある乱数のエンジン（簡易 DSL・名前の総当たり）の種の指紋を、記録（<c>engine-fingerprints.json</c>）と照合する
/// </summary>
/// <remarks>
/// <para>
/// 指紋の役割は「生成器の乱数の引き方の変化を検出する」ことで、実行の結果は守らない（実行の結果は製品を直せば正当に変わる）。
/// 要約は生成器が作った入力（簡易 DSL は単位ごとの列・行・クエリの条件とパラメータの値、名前は名前の組）だけを読み、
/// 生成器の乱数を余分に引かない。生成だけを呼ぶので DB もコンパイルも使わない（windows・ubuntu のどちらでも数秒で流れる）。
/// </para>
/// <para>
/// 記録の書き直しは <c>QUICKER_REGEN_FIXTURES=1</c>（生成フィクスチャのドリフトと同じ 1 コマンド）。書き直しは承認であって検証では
/// ない（差分を見て、乱数の引き方を意図して変えたことを確かめてから書き直す）。1 つのクラスにまとめてあるので、xUnit は同じ記録の
/// 書き直しを並べて走らせない。
/// </para>
/// </remarks>
// 種を使うが夜間に流さない: 普段の CI が記録の全種を照合し、夜間に増える種は記録に無いので照合の対象が増えない
// （流すと、増えた種の入力の生成の時間だけが掛かる）
[Trait(NightlyEngineTrait.Name, NightlyEngineTrait.NotSelected)]
public sealed class EngineFingerprintDriftTests
{
    /// <summary>記録のファイル（リポジトリ直下からの相対パス）</summary>
    private const string RecordPath = "tests/QuickER.Tests/TestSupport/engine-fingerprints.json";

    [Fact(DisplayName = "[指紋] 簡易 DSL の総当たりの生成器は、記録と同じ入力を作る")]
    public void Dsl_GeneratesRecordedInputs() => Verify("Dsl", NameFuzz.SeedSettings, DslSummary);

    [Fact(DisplayName = "[指紋] 名前の総当たりの生成器は、記録と同じ名前の組を作る")]
    public void Name_GeneratesRecordedInputs() =>
        Verify("Name", NameFuzz.SeedSettings, NameSummary);

    /// <summary>種ごとの要約の指紋を記録と照合する（書き直しの指定があれば記録を書き直す）</summary>
    private static void Verify(string engineId, SeedSettings settings, Func<int, string> summary)
    {
        var plan = SeedSource.Resolve(settings);
        var actual = plan.Seeds.ToDictionary(
            seed => seed,
            seed => SeedFingerprint.Of(summary(seed))
        );
        FingerprintFile.Verify(
            Path.Combine(NeutralResxFiles.FindRepositoryRoot(), RecordPath),
            engineId,
            actual,
            plan,
            FixtureDriftHarness.IsRegenerationRequested()
        );
    }

    /// <summary>簡易 DSL：種から作る全単位（SQLite・MySQL・SQL Server）の列・行・クエリ</summary>
    internal static string DslSummary(int seed)
    {
        var text = new StringBuilder();
        var units = DslFuzzUnits
            .Sqlite(seed)
            .Concat(DslFuzzUnits.MySql(seed))
            .Concat(DslFuzzUnits.SqlServer(seed));

        foreach (var unit in units)
        {
            text.Append("unit ")
                .Append(unit.Name)
                .Append(" table=")
                .Append(unit.TableName)
                .Append(" vo=")
                .Append(unit.ValueObjects)
                .Append(" seed=")
                .Append(unit.Seed)
                .Append('\n');

            foreach (var column in unit.Columns)
            {
                text.Append("  column ").Append(DescribeColumn(column)).Append('\n');
            }

            foreach (var row in unit.Rows)
            {
                text.Append("  row");

                foreach (var column in unit.Columns)
                {
                    text.Append(' ')
                        .Append(column.Name)
                        .Append('=')
                        .Append(SeedSummary.Object(row.GetValueOrDefault(column.Name)));
                }

                text.Append(" keys=").Append(string.Join(",", row.Keys)).Append('\n');
            }

            foreach (var query in unit.Queries)
            {
                text.Append("  query ")
                    .Append(query.Name)
                    .Append(" returns=")
                    .Append(query.Returns)
                    .Append(" atom=")
                    .Append(query.IsAtomQuery)
                    .Append(" desc=")
                    .Append(query.OrderDescending)
                    .Append(" projected=")
                    .Append(query.ProjectedColumn?.Name ?? "-")
                    .Append(" nonnull=")
                    .Append(query.ProjectedNonNull)
                    .Append(" type=")
                    .Append(query.ResultTypeName ?? "-")
                    .Append(" collision=")
                    .Append(query.TypeNameCollision ?? "-")
                    .Append('\n')
                    .Append("    condition ")
                    .Append(SeedSummary.Quote(DslFuzz.Render(query.Condition)))
                    .Append('\n');

                foreach (var atom in query.Atoms)
                {
                    text.Append("    atom ")
                        .Append(atom.GetType().Name)
                        .Append(' ')
                        .Append(atom.Kind)
                        .Append(" column=")
                        .Append(atom.Column.Name)
                        .Append(" literal=")
                        .Append(LiteralOf(atom))
                        .Append('\n');
                }

                foreach (var parameter in query.Parameters)
                {
                    text.Append("    param ")
                        .Append(parameter.Name)
                        .Append(" list=")
                        .Append(parameter.IsList)
                        .Append(" bycolumn=")
                        .Append(parameter.ByColumn)
                        .Append(" column=")
                        .Append(parameter.Column.Name)
                        .Append(" values=")
                        .Append(SeedSummary.Object(parameter.Values))
                        .Append('\n');
                }
            }
        }

        return text.ToString();
    }

    /// <summary>名前の総当たり：種から作る名前の組（固定の組を含む）</summary>
    internal static string NameSummary(int seed) =>
        string.Join(
            "\n",
            NameFuzz
                .Pairs(seed, NameFuzzRunner.RandomPairsPerSeed)
                .Select(pair =>
                    $"{SeedSummary.Quote(pair.First)} | {SeedSummary.Quote(pair.Second)} | {pair.Difference}"
                )
        );

    private static string DescribeColumn(DslColumn column) =>
        $"{column.Name} {column.DataType} {column.Clr} nullable={column.Nullable} pk={column.PrimaryKey}";

    private static string LiteralOf(DslAtom atom) =>
        atom switch
        {
            DslCompare { Operand: DslLiteral literal } => SeedSummary.Object(literal.Value),
            DslLike { Operand: DslLiteral literal } => SeedSummary.Object(literal.Value),
            DslMatch { Operand: DslLiteral literal } => SeedSummary.Object(literal.Value),
            _ => "-",
        };
}
