using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using AwesomeAssertions;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Provider;

/// <summary>
/// トルコ語の環境で起動しても、SQLite の再構築で失われる属性の検出（<c>TableRebuildAttributeDetector</c>）が
/// インバリアントのときと同じ語を見つけることを、実際に型をトルコ語のカルチャのもとで初期化して確かめる
/// </summary>
/// <remarks>
/// <para>
/// 検出の正規表現は静的なフィールドで、型を初めて使った時点の書式のカルチャで作られる。このプロセスでは型が既に
/// 初期化されているので、<c>QuickER.Provider.dll</c> を新しい読み込みコンテキストへ読み込み直し、カルチャを
/// tr-TR にしてから初めて呼ぶ。<c>RegexOptions.CultureInvariant</c> を外すと、<c>AUTOINCREMENT</c>・
/// <c>DEFERRABLE</c>・<c>WITHOUT ROWID</c>・<c>STRICT</c> を見落とす。
/// </para>
/// <para>
/// 規則そのもの（src の全正規表現）は <see cref="QuickER.Tests.Docs.RegexCultureGuardTests"/> の走査が守る。
/// 読み込み直したアセンブリはほかのテストから見えてしまうので、並列に走らせず、回収を確かめてから戻る
/// （<see cref="IsolatedAssemblyLoadTestCollection"/>）。WPF を含むアセンブリは読み込み直さない。
/// </para>
/// </remarks>
[Collection(IsolatedAssemblyLoadTestCollection.Name)]
public sealed class TableRebuildAttributeDetectorCultureTests
{
    private const string Sql =
        "create table t (a integer primary key autoincrement, b text collate nocase, c int, "
        + "foreign key (c) references p(id) deferrable initially deferred) without rowid, strict";

    [Fact(
        DisplayName = "トルコ語のカルチャで型が初めて使われても、再構築で失われる属性を同じように検出する"
    )]
    public void Detect_UnderTurkishCultureFromFirstUse_FindsTheSameTokens()
    {
        Detect("").Should().Be("AUTOINCREMENT,COLLATE,DEFERRABLE,WITHOUT ROWID,STRICT");
        Detect("tr-TR").Should().Be(Detect(""));
    }

    /// <summary>新しい読み込みコンテキストで、指定のカルチャのもとで初めて検出を呼び、回収を確かめてから結果を返す</summary>
    private static string Detect(string culture)
    {
        var (result, context) = DetectInFreshContext(culture);

        for (var i = 0; i < 20 && context.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        context
            .IsAlive.Should()
            .BeFalse("読み込み直したアセンブリを回収してからほかのテストへ戻ること");

        return result;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (string Result, WeakReference Context) DetectInFreshContext(string culture)
    {
        var directory = Path.GetDirectoryName(
            typeof(TableRebuildAttributeDetectorCultureTests).Assembly.Location
        )!;
        var context = new AssemblyLoadContext("regex-culture-" + culture, isCollectible: true);
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(culture);

        try
        {
            var detect = context
                .LoadFromAssemblyPath(Path.Combine(directory, "QuickER.Provider.dll"))
                .GetType("QuickER.Provider.TableRebuildAttributeDetector", throwOnError: true)!
                .GetMethod("Detect")!;

            return (
                string.Join(",", (IReadOnlyList<string>)detect.Invoke(null, [Sql])!),
                new WeakReference(context)
            );
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            context.Unload();
        }
    }
}
