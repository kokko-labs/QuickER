using System.IO;
using AwesomeAssertions;
using QuickER.Platform;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.Platform.OsBoundary.AtomicFailure;
using static QuickER.Tests.Platform.OsBoundary.AtomicOp;
using static QuickER.Tests.Platform.OsBoundary.AtomicOutcome;
using static QuickER.Tests.Platform.OsBoundary.AtomicTarget;

namespace QuickER.Tests.Platform.OsBoundary;

/// <summary>原子的な書き込みの表：書く操作（QuickER.Platform の <c>AtomicFile.WriteAllText</c>・<c>WriteAllBytes</c>・<c>Write</c>・<c>CreateText</c>）</summary>
internal static partial class AtomicFileRows
{
    private const string JsonText = "{ \"名前\": \"顧客\" }";

    /// <summary>I/O 系の例外か（差し替え手段と OS によって <see cref="IOException"/> か <see cref="UnauthorizedAccessException"/> になる）</summary>
    private static void ShouldBeIoFailure(Exception thrown) =>
        (thrown is IOException or UnauthorizedAccessException)
            .Should()
            .BeTrue($"IO 系の例外であること（実際: {thrown.GetType().Name}）");

    private static IEnumerable<ScenarioRow<AtomicCase>> WriteRows()
    {
        yield return Row(
            "atomic-write-all-text-creates-new-file-without-leftover",
            "WriteAllText: 保存先が無ければ新規作成し .tmp を残さない",
            WriteAllText,
            NoFile,
            None,
            Written,
            _ => { },
            o =>
            {
                // 非 ASCII を含めて、既定の文字コード（BOM なし UTF-8）で往復できることも併せて確認する
                AtomicFile.WriteAllText(Path.Combine(o.Folder, "new.json"), JsonText);
                return null;
            },
            o =>
            {
                var path = Path.Combine(o.Folder, "new.json");

                File.Exists(path).Should().BeTrue();
                File.ReadAllText(path).Should().Be(JsonText);
                o.FindTemporaryLeftovers().Should().BeEmpty("一時ファイルは差し替え後に残らない");
            }
        );

        yield return Row(
            "atomic-write-all-text-replaces-existing-file-without-leftover",
            "WriteAllText: 既存ファイルを置換し .tmp を残さない",
            WriteAllText,
            ExistingFile,
            None,
            Written,
            o => File.WriteAllText(Path.Combine(o.Folder, "existing.json"), "old"),
            o =>
            {
                AtomicFile.WriteAllText(Path.Combine(o.Folder, "existing.json"), "new");
                return null;
            },
            o =>
            {
                File.ReadAllText(Path.Combine(o.Folder, "existing.json")).Should().Be("new");
                o.FindTemporaryLeftovers().Should().BeEmpty("一時ファイルは差し替え後に残らない");
            }
        );

        // 「一時ファイルへ書いて差し替える」実装では自明だが、保存先へ直接追記・部分上書きする実装へ退行すると、
        // 旧内容の尻尾が残って壊れた JSON になる。その退行を検知する
        yield return Row(
            "atomic-write-all-text-shorter-content-leaves-no-remnant",
            "WriteAllText: 短い内容で上書きしても旧内容が残らない",
            WriteAllText,
            ExistingLarger,
            None,
            Written,
            o => File.WriteAllText(Path.Combine(o.Folder, "shrink.json"), new string('x', 4096)),
            o =>
            {
                AtomicFile.WriteAllText(Path.Combine(o.Folder, "shrink.json"), "{}");
                return null;
            },
            o => File.ReadAllText(Path.Combine(o.Folder, "shrink.json")).Should().Be("{}")
        );

        // OS の仕掛けを使う理由：差し込み点が無く、置換（File.Replace）とそのフォールバック（File.Move）の双方を確実に
        // 失敗させるには、保存先を FileShare.None で開いたままにする実ファイルのロックが要る
        yield return Row(
            "atomic-write-all-text-replace-failure-keeps-original-and-propagates",
            "WriteAllText: 差し替え失敗時は元ファイル無傷・.tmp なし・例外を伝播",
            WriteAllText,
            LockedFile,
            ReplaceFails,
            Failed,
            o =>
            {
                var path = Path.Combine(o.Folder, "locked.json");
                File.WriteAllText(path, "original");
                o.Lock = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            },
            o =>
            {
                AtomicFile.WriteAllText(Path.Combine(o.Folder, "locked.json"), "replacement");
                return null;
            },
            o =>
            {
                // File.Replace は IOException・フォールバックの File.Move は UnauthorizedAccessException を
                // 投げる（OS/ランタイム依存）ため、どちらの型でも「伝播していること」を検証する
                var thrown = o.ShouldHaveThrown<Exception>("差し替えの失敗を握り潰さない").Which;
                ShouldBeIoFailure(thrown);
                o.FindTemporaryLeftovers().Should().BeEmpty("失敗時に一時ファイルを残さない");

                o.ReleaseLock();

                File.ReadAllText(Path.Combine(o.Folder, "locked.json"))
                    .Should()
                    .Be("original", "失敗しても保存先は書き込み前のまま");
            }
        );

        yield return Row(
            "atomic-write-all-bytes-replaces-existing-file-without-leftover",
            "WriteAllBytes: 既存ファイルを置換し .tmp を残さない",
            WriteAllBytes,
            ExistingFile,
            None,
            Written,
            o => File.WriteAllBytes(Path.Combine(o.Folder, "secret.dat"), [1, 2, 3, 4, 5]),
            o =>
            {
                AtomicFile.WriteAllBytes(Path.Combine(o.Folder, "secret.dat"), [9, 8]);
                return null;
            },
            o =>
            {
                File.ReadAllBytes(Path.Combine(o.Folder, "secret.dat")).Should().Equal(9, 8);
                o.FindTemporaryLeftovers().Should().BeEmpty("一時ファイルは差し替え後に残らない");
            }
        );

        yield return Row(
            "atomic-write-stream-replaces-file-without-leftover",
            "Write: ストリームへ書いた内容で保存先を置換し .tmp を残さない",
            Write,
            ExistingFile,
            None,
            Written,
            o => File.WriteAllText(Path.Combine(o.Folder, "image.png"), "old"),
            o =>
            {
                AtomicFile.Write(
                    Path.Combine(o.Folder, "image.png"),
                    stream => stream.Write([0x89, 0x50, 0x4E, 0x47])
                );
                return null;
            },
            o =>
            {
                File.ReadAllBytes(Path.Combine(o.Folder, "image.png"))
                    .Should()
                    .Equal(0x89, 0x50, 0x4E, 0x47);
                o.FindTemporaryLeftovers().Should().BeEmpty("一時ファイルは差し替え後に残らない");
            }
        );

        // 画像のエンコーダやブックの保存が途中で失敗する形。保存先へ直接書く実装なら、保存先は書きかけで残る
        yield return Row(
            "atomic-write-stream-writer-throws-keeps-original-and-propagates",
            "Write: 書き出し処理の途中の失敗で元ファイル無傷・.tmp なし・例外を伝播",
            Write,
            ExistingFile,
            WriterThrows,
            Failed,
            o => File.WriteAllText(Path.Combine(o.Folder, "book.xlsx"), "original"),
            o =>
            {
                AtomicFile.Write(
                    Path.Combine(o.Folder, "book.xlsx"),
                    stream =>
                    {
                        stream.Write([1, 2, 3]);

                        throw new InvalidOperationException("途中で失敗");
                    }
                );
                return null;
            },
            o =>
            {
                o.ShouldHaveThrown<InvalidOperationException>().WithMessage("途中で失敗");
                File.ReadAllText(Path.Combine(o.Folder, "book.xlsx"))
                    .Should()
                    .Be("original", "失敗しても保存先は書き込み前のまま");
                o.FindTemporaryLeftovers().Should().BeEmpty("失敗時に一時ファイルを残さない");
            }
        );

        // OS の仕掛けを使う理由：差し込み点が無く、保存先が書き込めない状況（差し替え手段と OS 依存で IOException か
        // UnauthorizedAccessException）を作るには、実際に保存先の名前でディレクトリを置くしかない。
        // 押さえたいのは「握り潰さず伝播し、一時ファイルを残さない」ことなので型は両方許容する
        yield return Row(
            "atomic-write-all-text-path-is-directory-throws-without-leftover",
            "WriteAllText: 保存先がディレクトリなら .tmp を残さず例外を投げる",
            WriteAllText,
            DirectoryAtPath,
            TargetIsDirectory,
            Failed,
            o => Directory.CreateDirectory(Path.Combine(o.Folder, "as-directory")),
            o =>
            {
                AtomicFile.WriteAllText(Path.Combine(o.Folder, "as-directory"), "{}");
                return null;
            },
            o =>
            {
                var thrown = o.ShouldHaveThrown<Exception>("書き込みの失敗を握り潰さない").Which;
                ShouldBeIoFailure(thrown);
                o.FindTemporaryLeftovers().Should().BeEmpty("失敗時に一時ファイルを残さない");
            }
        );

        // 固定名へ退行すると、同じファイルを同時保存した 2 プロセスが同じ一時ファイルを奪い合い破損へ昇格する。
        // 固定名の一時ファイルをあらかじめ置いて「他プロセスの一時ファイルを踏まない」ことを直接確かめる。
        // この行は、置いた固定名のファイルを残りとして数えない（準備の後を基準にする）
        yield return Row(
            "atomic-write-all-text-does-not-use-fixed-temporary-name",
            "WriteAllText: 固定名 {path}.tmp を使わない",
            WriteAllText,
            FixedTmpSibling,
            None,
            Written,
            o =>
                File.WriteAllText(
                    Path.Combine(o.Folder, "guid.json") + ".tmp",
                    "他プロセスが使用中の一時ファイル"
                ),
            o =>
            {
                AtomicFile.WriteAllText(Path.Combine(o.Folder, "guid.json"), "{}");
                return null;
            },
            o =>
            {
                var path = Path.Combine(o.Folder, "guid.json");

                File.ReadAllText(path).Should().Be("{}");
                File.ReadAllText(path + ".tmp")
                    .Should()
                    .Be("他プロセスが使用中の一時ファイル", "一時ファイル名には GUID が挟まる");
            }
        );

        yield return Row(
            "atomic-create-text-creates-new-file-without-leftover",
            "CreateText: 保存先が無ければ新規作成し .tmp を残さない",
            CreateText,
            NoFile,
            None,
            Written,
            _ => { },
            o =>
            {
                AtomicFile.CreateText(Path.Combine(o.Folder, "created.json"), JsonText);
                return null;
            },
            o =>
            {
                File.ReadAllText(Path.Combine(o.Folder, "created.json")).Should().Be(JsonText);
                o.FindTemporaryLeftovers().Should().BeEmpty("一時ファイルは置いた後に残らない");
            }
        );

        // 存在を確かめてから書く呼び出し側（モックフォルダの作成・MCP の create_diagram）は、その隙間に別のプロセスが
        // 作ったファイルをこれで上書きしない
        yield return Row(
            "atomic-create-text-existing-file-throws-and-keeps-content",
            "CreateText: 保存先が既にあれば置き換えずに IOException を投げ .tmp を残さない",
            CreateText,
            ExistingFile,
            CreateConflict,
            Failed,
            o => File.WriteAllText(Path.Combine(o.Folder, "existing.json"), "original"),
            o =>
            {
                AtomicFile.CreateText(Path.Combine(o.Folder, "existing.json"), "replacement");
                return null;
            },
            o =>
            {
                o.ShouldHaveThrown<IOException>("置き換えない書き込みは既存のファイルで失敗する");
                File.ReadAllText(Path.Combine(o.Folder, "existing.json")).Should().Be("original");
                o.FindTemporaryLeftovers().Should().BeEmpty("失敗時に一時ファイルを残さない");
            }
        );
    }
}
