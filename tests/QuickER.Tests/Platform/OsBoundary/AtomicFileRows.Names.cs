using System.IO;
using AwesomeAssertions;
using QuickER.Platform;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.Platform.OsBoundary.AtomicFailure;
using static QuickER.Tests.Platform.OsBoundary.AtomicOp;
using static QuickER.Tests.Platform.OsBoundary.AtomicOutcome;
using static QuickER.Tests.Platform.OsBoundary.AtomicTarget;

namespace QuickER.Tests.Platform.OsBoundary;

/// <summary>
/// 原子的な書き込みの表：一時ファイルの名前（QuickER.Platform の <c>AtomicFile.TemporaryPathFor</c>・<c>IsTemporaryFileName</c>・
/// 長い名前の保存先への <c>WriteAllText</c>）
/// </summary>
/// <remarks>
/// 一時ファイルの名前は保存先の名前に 37 文字を足すので、そのまま足すと 218 文字を超える名前の保存先では 255 文字の上限を超え、
/// 保存先そのものは作れるのに書き込みが必ず失敗する。
/// </remarks>
internal static partial class AtomicFileRows
{
    private static IEnumerable<ScenarioRow<AtomicCase>> NameRows()
    {
        // 一時ファイルの名前は、保存先の名前の長さに依らず 1 区間の上限に収まり、同じフォルダに置かれる
        foreach (var length in new[] { 1, 218, 219, 255 })
        {
            yield return Row(
                $"atomic-temporary-path-fits-segment-limit-{length}",
                "一時ファイルの名前は、保存先の名前の長さに依らず 1 区間の上限に収まり、同じフォルダに置かれる",
                TemporaryPathFor,
                LongName,
                None,
                Computed,
                _ => { },
                o => AtomicFile.TemporaryPathFor(Path.Combine(o.Folder, new string('a', length))),
                o =>
                {
                    var temporary = (string)o.Value!;

                    Path.GetFileName(temporary).Length.Should().BeLessThanOrEqualTo(255);
                    Path.GetDirectoryName(temporary).Should().Be(o.Folder);
                    AtomicFile.IsTemporaryFileName(Path.GetFileName(temporary)).Should().BeTrue();
                    Path.GetFileName(temporary)
                        .Should()
                        .StartWith(new string('a', Math.Min(length, 218)));
                }
            );
        }

        yield return Row(
            "atomic-temporary-path-does-not-split-surrogate-pair",
            "一時ファイルの名前を切るとき、サロゲートペアの途中で切らない",
            TemporaryPathFor,
            SurrogateAtCut,
            None,
            Computed,
            _ => { },
            o =>
            {
                // 218 文字目と 219 文字目がサロゲートペアになる名前
                var name = new string('a', 217) + "\U0001F600" + new string('b', 30);

                return Path.GetFileName(AtomicFile.TemporaryPathFor(Path.Combine(o.Folder, name)));
            },
            o =>
            {
                var temporaryName = (string)o.Value!;

                temporaryName.Should().StartWith(new string('a', 217) + ".");
                temporaryName.Length.Should().BeLessThanOrEqualTo(255);
            }
        );

        // 255 文字までの名前の保存先へ、書き込みが成功し一時ファイルを残さない
        foreach (var length in new[] { 219, 255 })
        {
            yield return Row(
                $"atomic-write-all-text-long-file-name-succeeds-{length}",
                "255 文字までの名前の保存先へ、書き込みが成功し一時ファイルを残さない",
                WriteAllText,
                LongName,
                None,
                Written,
                _ => { },
                o =>
                {
                    var path = Path.Combine(o.Folder, new string('x', length - 5) + ".json");

                    AtomicFile.WriteAllText(path, "first");
                    AtomicFile.WriteAllText(path, "second");
                    return path;
                },
                o =>
                {
                    File.ReadAllText((string)o.Value!).Should().Be("second");
                    Directory.GetFiles(o.Folder).Should().ContainSingle();
                }
            );
        }

        // 一時ファイルの形かどうかの判定
        var names = new (string Id, string Name, bool Expected, AtomicTarget Target)[]
        {
            (
                "guid-lower-json",
                "a.json.0123456789abcdef0123456789abcdef.tmp",
                true,
                TemporaryFormName
            ),
            (
                "guid-lower-upper-tmp",
                "x.0123456789abcdef0123456789abcdef.TMP",
                true,
                TemporaryFormName
            ),
            ("plain-tmp", "a.json.tmp", false, NonTemporaryName),
            (
                "guid-uppercase-hex",
                "a.json.0123456789ABCDEF0123456789ABCDEF.tmp",
                false,
                NonTemporaryName
            ),
            (
                "guid-non-hex-char",
                "a.json.0123456789abcdef0123456789abcdeg.tmp",
                false,
                NonTemporaryName
            ),
            (
                "guid-after-hyphen",
                "a.json-0123456789abcdef0123456789abcdef.tmp",
                false,
                NonTemporaryName
            ),
            (
                "guid-without-base-name",
                ".0123456789abcdef0123456789abcdef.tmp",
                false,
                NonTemporaryName
            ),
            ("index-html", "index.html", false, NonTemporaryName),
        };

        foreach (var (id, name, expected, target) in names)
        {
            yield return Row(
                $"atomic-is-temporary-file-name-{id}",
                "一時ファイルの形かどうかの判定",
                IsTemporaryFileName,
                target,
                None,
                Computed,
                _ => { },
                _ => AtomicFile.IsTemporaryFileName(name),
                o => ((bool)o.Value!).Should().Be(expected)
            );
        }
    }
}
