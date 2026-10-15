using System.Reflection;
using AwesomeAssertions;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// コード生成の出力が書式のカルチャに依らないことを、ドリフトテストを各カルチャ（<see cref="CultureScope.Cultures"/>）のもとで
/// 流し直して確かめる
/// </summary>
/// <remarks>
/// <para>
/// ドリフトテスト（<c>*DriftTests</c>）は、固定フィクスチャ・ランタイムのパッケージ用ソース・サンプルの生成物を作り直して
/// コミット済みのものと照合する。コミット済みのものを基準にすれば、全フィクスチャの全オプションの生成物（C#・API リファレンス・
/// サンプルの DDL）について、カルチャを変えて作ったものが基準と一致するかを 1 度に確かめられる。
/// </para>
/// <para>
/// 生成物でないもの（公開 API の承認ファイル・型名の名簿）は流さない。再生成のための環境変数が立っているときは何もしない
/// （ほかのカルチャで作ったものでコミット済みの生成物を上書きしないため）。この名前に Drift を含めないのも、再生成の手順
/// （<c>--filter "FullyQualifiedName~Drift"</c>）に拾われないため。
/// </para>
/// </remarks>
public sealed class CultureInvariantGenerationTests
{
    /// <summary>流さないドリフトテスト（生成物の照合ではないもの）</summary>
    private static readonly HashSet<string> Excluded =
    [
        "RuntimePackagePublicApiDriftTests",
        "GeneratedTypeNameDriftTests",
        "GeneratedFixedMemberDriftTests",
    ];

    /// <summary>確かめるカルチャ</summary>
    public static TheoryData<string> Cultures => [.. CultureScope.Cultures];

    [Theory(DisplayName = "コード生成の出力は、書式のカルチャに依らずコミット済みの生成物と同じ")]
    [MemberData(nameof(Cultures))]
    public async Task DriftTests_PassUnderCulture(string culture)
    {
        if (FixtureDriftHarness.IsRegenerationRequested())
        {
            return;
        }

        var driftTests = typeof(CultureInvariantGenerationTests)
            .Assembly.GetTypes()
            .Where(type =>
                type.Name.EndsWith("DriftTests", StringComparison.Ordinal)
                && !Excluded.Contains(type.Name)
            )
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToList();

        driftTests.Should().HaveCountGreaterThan(20, "ドリフトテストを見つけられること");

        var failures = new List<string>();

        using var scope = new CultureScope(culture);

        foreach (var type in driftTests)
        {
            foreach (
                var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .Where(method => method.GetCustomAttribute<FactAttribute>() is not null)
            )
            {
                var instance = Activator.CreateInstance(type);

                try
                {
                    if (method.Invoke(instance, null) is Task task)
                    {
                        await task;
                    }
                }
                catch (Exception ex)
                {
                    var inner = ex is TargetInvocationException { InnerException: { } e } ? e : ex;
                    failures.Add($"{type.Name}.{method.Name}: {FirstLine(inner.Message)}");
                }
                finally
                {
                    (instance as IDisposable)?.Dispose();
                }
            }
        }

        failures.Should().BeEmpty();
    }

    private static string FirstLine(string message) => message.Split('\n', 2)[0].TrimEnd('\r');
}
