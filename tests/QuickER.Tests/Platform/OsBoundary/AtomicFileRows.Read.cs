using System.IO;
using AwesomeAssertions;
using QuickER.Platform;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.Platform.OsBoundary.AtomicFailure;
using static QuickER.Tests.Platform.OsBoundary.AtomicOp;
using static QuickER.Tests.Platform.OsBoundary.AtomicOutcome;
using static QuickER.Tests.Platform.OsBoundary.AtomicTarget;

namespace QuickER.Tests.Platform.OsBoundary;

/// <summary>原子的な書き込みの表：読み取り（QuickER.Platform の <c>AtomicFile.TryReadAllText</c>）</summary>
internal static partial class AtomicFileRows
{
    /// <summary>読み取りの戻り値（状態と内容）</summary>
    private static (FileReadStatus Status, string? Contents) ReadResult(AtomicObservation o) =>
        ((FileReadStatus, string?))o.Value!;

    /// <summary>読み取りの行の入口（状態と内容を戻り値に入れる）</summary>
    private static object? Read(string path)
    {
        var status = AtomicFile.TryReadAllText(path, out var contents);
        return (status, contents);
    }

    private static IEnumerable<ScenarioRow<AtomicCase>> ReadRows()
    {
        yield return Row(
            "atomic-try-read-existing-file-returns-loaded",
            "TryReadAllText: 読めたら Loaded と内容を返す",
            TryReadAllText,
            ExistingFile,
            None,
            AtomicOutcome.Read,
            o => AtomicFile.WriteAllText(Path.Combine(o.Folder, "readable.json"), JsonText),
            o => Read(Path.Combine(o.Folder, "readable.json")),
            o =>
            {
                ReadResult(o).Status.Should().Be(FileReadStatus.Loaded);
                ReadResult(o).Contents.Should().Be(JsonText);
            }
        );

        // 「保存されたものが無い」は既定値・空データへ落としてよい唯一の分類。ここが Unavailable へ化けると、
        // 初回起動で保存が一切できなくなる
        yield return Row(
            "atomic-try-read-missing-file-returns-missing",
            "TryReadAllText: ファイルが無ければ Missing を返す",
            TryReadAllText,
            NoFile,
            None,
            AtomicOutcome.Read,
            _ => { },
            o => Read(Path.Combine(o.Folder, "absent.json")),
            o =>
            {
                ReadResult(o).Status.Should().Be(FileReadStatus.Missing);
                ReadResult(o).Contents.Should().BeNull();
            }
        );

        // DirectoryNotFoundException も IOException の派生なので、分類の順序を誤ると「フォルダごと未作成の初回起動」が
        // 読み取り失敗として扱われ、保存が止まる
        yield return Row(
            "atomic-try-read-missing-directory-returns-missing",
            "TryReadAllText: 親フォルダが無くても Missing を返す",
            TryReadAllText,
            MissingParentFolder,
            None,
            AtomicOutcome.Read,
            _ => { },
            o => Read(Path.Combine(o.Folder, "not-created", "absent.json")),
            o => ReadResult(o).Status.Should().Be(FileReadStatus.Missing)
        );

        // OS の仕掛けを使う理由：差し込み点が無く、「中身はあるのに読めない」状況を決定的に作るには、
        // 実ファイルを FileShare.None で開いたままにするしかない。
        // Missing（空）と取り違えると、読み込み → 変更 → 書き込みの保存でその空が確定して登録済みの内容が全消えする
        yield return Row(
            "atomic-try-read-locked-file-returns-unavailable",
            "TryReadAllText: 排他で掴まれたファイルは Unavailable を返す",
            TryReadAllText,
            LockedFile,
            None,
            AtomicOutcome.Read,
            o =>
            {
                var path = Path.Combine(o.Folder, "locked-read.json");
                File.WriteAllText(path, "{ \"kept\": true }");
                o.Lock = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            },
            o => Read(Path.Combine(o.Folder, "locked-read.json")),
            o =>
            {
                ReadResult(o)
                    .Status.Should()
                    .Be(FileReadStatus.Unavailable, "中身は無事なので「空」ではない");
                ReadResult(o).Contents.Should().BeNull();

                o.ReleaseLock();

                File.ReadAllText(Path.Combine(o.Folder, "locked-read.json"))
                    .Should()
                    .Be("{ \"kept\": true }", "読み取りは中身を変えない");
            }
        );

        // 一時ファイルの名前の上限に収める細工が読み取りには無いので、長い名前でも読めることだけを見る
        yield return Row(
            "atomic-try-read-long-file-name-returns-loaded",
            "TryReadAllText: 255 文字の名前のファイルも読めたら Loaded と内容を返す",
            TryReadAllText,
            LongName,
            None,
            AtomicOutcome.Read,
            o =>
                AtomicFile.WriteAllText(
                    Path.Combine(o.Folder, new string('x', 250) + ".json"),
                    JsonText
                ),
            o => Read(Path.Combine(o.Folder, new string('x', 250) + ".json")),
            o =>
            {
                ReadResult(o).Status.Should().Be(FileReadStatus.Loaded);
                ReadResult(o).Contents.Should().Be(JsonText);
            }
        );

        // 特性の固定（文書に定めが無い今の振る舞い）：読む先がディレクトリのときの分類は FileReadStatus の XmlDoc が決めていない
        // （「あるのに読めない」は Unavailable・「無い」は Missing とだけある）。File.ReadAllText はディレクトリに
        // UnauthorizedAccessException を投げ、ReadWithRetry が粘った後、TryReadAllText の
        // UnauthorizedAccessException の枝が Unavailable を返す（src/QuickER.Platform/AtomicFile.cs の TryReadAllText と ReadWithRetry）。
        // 文書に定めができたらその期待へ書き直す
        yield return Row(
            "atomic-try-read-path-is-directory-returns-unavailable",
            "特性の固定（文書に定めが無い今の振る舞い）：TryReadAllText: 読む先がディレクトリなら Unavailable を返す",
            TryReadAllText,
            DirectoryAtPath,
            None,
            AtomicOutcome.Read,
            o => Directory.CreateDirectory(Path.Combine(o.Folder, "as-directory")),
            o => Read(Path.Combine(o.Folder, "as-directory")),
            o =>
            {
                ReadResult(o).Status.Should().Be(FileReadStatus.Unavailable);
                ReadResult(o).Contents.Should().BeNull();
                Directory.Exists(Path.Combine(o.Folder, "as-directory")).Should().BeTrue();
            }
        );
    }
}
