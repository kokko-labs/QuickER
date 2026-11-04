using AwesomeAssertions;
using QuickER.Dialects;
using QuickER.Provider;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Provider;

/// <summary>方言の型の表（<see cref="DialectTypeRows"/>）を流すテスト</summary>
public sealed class DialectTypeTableTests(ITestOutputHelper output)
{
    /// <summary>(行の Id, 方言) の組</summary>
    public static TheoryData<string, string> Cases => DialectTypeRows.Cases();

    /// <summary>方言（行列の軸の正本の並び）</summary>
    public static TheoryData<string> Dialects => new(DialectNames.All);

    [Theory(DisplayName = "[方言の型] 行の期待どおりに解析・書き出し・正規化・写像・変換される")]
    [MemberData(nameof(Cases))]
    public void Row_MatchesExpectation(string id, string dialect)
    {
        var row = DialectTypeRows.Table[id];
        var problems = DialectTypeRunner.Check(row.Input, dialect);

        problems
            .Should()
            .BeEmpty(
                $"行 {id}（{row.Description}）の {dialect} で 入力 {row.Input.DescribeInput()}：\n  "
                    + string.Join("\n  ", problems)
            );
    }

    [Fact(
        DisplayName = "[方言の型] 網羅しない組は一覧のとおり（種類 × 方言 × 観点・種類 × 方言 × 境界）"
    )]
    public void Coverage_MatchesDeclaredGaps()
    {
        var counter = new CoverageCounter(DialectTypeDimensions.Table);

        foreach (var row in DialectTypeRows.Table)
        {
            foreach (var dialect in row.Input.Expected.Keys)
            {
                DialectTypeDimensions.Observe(counter, row.Input, dialect);
            }
        }

        var report = counter.Report();
        report.WriteSummary(output);
        report.AssertGaps(DialectTypeDimensions.Gaps, DialectTypeDimensions.Scope, output: output);
    }

    [Theory(
        DisplayName = "[方言の型] プロバイダは方言の名前・表示名・既定のポート・型の選択肢・部品を結線している"
    )]
    [MemberData(nameof(Dialects))]
    public void Provider_IsWired(string dialect)
    {
        var adapter = DialectTypeAxis.Get(dialect);
        var provider = adapter.Provider;

        provider.Name.Should().Be(dialect);
        provider.DisplayName.Should().Be(adapter.ExpectedDisplayName);
        provider.DefaultPort.Should().Be(adapter.ExpectedDefaultPort);
        provider.TypeCatalog.DataTypes.Should().BeSameAs(adapter.ExpectedDataTypes);
        provider.TypeCatalog.DataTypes.Should().NotBeEmpty();
        adapter
            .Components.Select(component => component.GetType())
            .Should()
            .Equal(adapter.ExpectedComponentTypes);

        var registry = new DatabaseProviderRegistry(
            DialectTypeAxis.Axis.Adapters.Select(other => other.Provider)
        );
        registry.Get(dialect).Should().BeSameAs(provider);
        registry.All.Should().HaveCount(DialectNames.All.Count);
    }

    /// <summary>
    /// アダプタの「書き出しに上限がある種類」の宣言が型カタログと一致する（宣言は網羅の規則が上限の境界の組を外すのに使う）
    /// </summary>
    /// <remarks>
    /// 長さ・精度・小数秒を持つ種類で、小さい値を書き出せる種類について、上限は「とても大きな値を書き出さない」ことと同じ。
    /// 小さい値も書き出せない種類（Oracle の Time）は上限の宣言の対象に入らない。
    /// </remarks>
    [Theory(DisplayName = "[方言の型] 書き出しに上限がある種類の宣言は型カタログと一致する")]
    [MemberData(nameof(Dialects))]
    public void WriteLimitedKinds_MatchCatalog(string dialect)
    {
        var adapter = DialectTypeAxis.Get(dialect);
        var actual = new HashSet<CanonicalTypeKind>();

        foreach (var kind in Enum.GetValues<CanonicalTypeKind>())
        {
            var (small, huge) = Probes(kind);

            if (small is null || huge is null || !adapter.Catalog.TryFormat(small, out _))
            {
                continue;
            }

            if (!adapter.Catalog.TryFormat(huge, out _))
            {
                actual.Add(kind);
            }
        }

        actual.Should().BeEquivalentTo(adapter.WriteLimitedKinds);
    }

    /// <summary>
    /// アダプタの「解析が返し得る種類」の宣言が、型の選択肢・全種類の代表値の書き出し・表の行の表記の入力を解析した種類と一致する
    /// （宣言は網羅の規則が表記を入力にする観点の組を外すのに使う。宣言を狭めると欠けが黙って消えるので、根拠の集合と照合する）
    /// </summary>
    [Theory(DisplayName = "[方言の型] 解析が返し得る種類の宣言は型カタログの解析と一致する")]
    [MemberData(nameof(Dialects))]
    public void ParsedKinds_MatchCatalog(string dialect)
    {
        var adapter = DialectTypeAxis.Get(dialect);
        var texts = new List<string>(adapter.Catalog.DataTypes);

        foreach (var kind in Enum.GetValues<CanonicalTypeKind>())
        {
            if (
                adapter.Catalog.TryFormat(
                    DialectTypeRows.RepresentativeSample(kind),
                    out var native
                )
            )
            {
                texts.Add(native);
            }
        }

        foreach (var row in DialectTypeRows.Table)
        {
            if (row.Input.Expected.ContainsKey(dialect) && row.Input.Input is string text)
            {
                texts.Add(text);
            }
        }

        var actual = texts
            .Select(text =>
                adapter.Catalog.TryParse(text, out var canonical)
                    ? canonical.Kind
                    : (CanonicalTypeKind?)null
            )
            .OfType<CanonicalTypeKind>()
            .ToHashSet();

        actual
            .Order()
            .Should()
            .Equal(
                adapter.ParsedKinds.Order(),
                $"{dialect} の解析が返した種類：{string.Join(", ", actual.Order())}"
            );
    }

    private static (CanonicalType? Small, CanonicalType? Huge) Probes(CanonicalTypeKind kind)
    {
        if (CanonicalTypeToken.HasLengthArgument(kind))
        {
            return (
                new CanonicalType(kind, Length: 10),
                new CanonicalType(kind, Length: 100_000_000)
            );
        }

        return kind switch
        {
            CanonicalTypeKind.Decimal => (
                new CanonicalType(kind, Precision: 10, Scale: 0),
                new CanonicalType(kind, Precision: 100_000, Scale: 0)
            ),
            CanonicalTypeKind.Time
            or CanonicalTypeKind.DateTime
            or CanonicalTypeKind.DateTimeOffset => (
                new CanonicalType(kind, Precision: 3),
                new CanonicalType(kind, Precision: 100)
            ),
            _ => (null, null),
        };
    }
}
