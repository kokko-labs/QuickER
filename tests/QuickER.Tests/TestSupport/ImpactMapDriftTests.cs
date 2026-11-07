using System.IO;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// 影響の地図（<c>impact-map.md</c>）が、宣言（エンジン・単体の表の台帳・規則の範囲・Integration の写し・守りの無い領域）から
/// 作り直した内容と食い違わないことを固定する
/// </summary>
/// <remarks>
/// 宣言を変えたら書き直す（生成フィクスチャと同じ環境変数。Integration の台帳を変えたときは、先に Integration の写しを書き直す）。
/// 書き直しは承認であって検証ではない（地図の差分を見て、守りの範囲の変化が意図どおりかを確かめる）。ほかの区切りのマージで
/// 台帳・規則が変わると地図も変わるので、マージの後に書き直してそろえる。
/// </remarks>
public sealed class ImpactMapDriftTests
{
    [Fact(DisplayName = "[地図] 影響の地図は、宣言から作り直した内容と同じ")]
    public void Map_MatchesDeclarations()
    {
        var root = EngineRepository.Root();
        var map = ImpactMapSources.Build(root);

        GeneratedTextFile.Verify(
            Path.Combine(root, ImpactMapSources.MapPath),
            map.Render(
                ImpactMapSources.RegenerateSteps,
                ImpactMapSources.HandwrittenTests(map, root)
            ),
            "影響の地図",
            ImpactMapSources.RegenerateSteps
        );
    }
}
