using System.IO;
using System.Text;
using AwesomeAssertions;
using QuickER.Platform;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.Platform.OsBoundary.AtomicFailure;
using static QuickER.Tests.Platform.OsBoundary.AtomicOp;
using static QuickER.Tests.Platform.OsBoundary.AtomicOutcome;
using static QuickER.Tests.Platform.OsBoundary.AtomicTarget;

namespace QuickER.Tests.Platform.OsBoundary;

/// <summary>
/// 原子的な書き込みの表：保存先の形ごとの行を、同じ形を流せる書く操作（<c>WriteAllBytes</c>・<c>Write</c>・<c>WriteAllText</c>・<c>CreateText</c>）へ
/// 展開する行
/// </summary>
/// <remarks>
/// <c>WriteAllBytes</c> と <c>WriteAllText</c> は <c>Write</c> への薄い委譲で、<c>CreateText</c> だけが置き換えず衝突で断る別の経路
/// （<c>ReplaceOnce</c> を通らない <c>File.Move(overwrite: false)</c>）を通る。期待は XmlDoc の「意味論は <c>WriteAllText</c> と同一」と
/// 「保存先フォルダの作成は行わない」「差し替えに失敗したら一時ファイルを掃除して例外を伝える（保存先は無傷）」から導く。
/// 同じ形を 4 つの操作で流すので、行は操作と形の表から作る（写しを並べない）。
/// </remarks>
internal static partial class AtomicFileRows
{
    /// <summary>成功する行が書く内容</summary>
    private const string FormText = "{}";

    /// <summary>固定名の一時ファイルの兄弟の内容</summary>
    private const string SiblingText = "他プロセスが使用中の一時ファイル";

    /// <summary>既存のファイル（ExistingLarger）の内容（<see cref="FormText"/> より長い）</summary>
    private static readonly string LargerText = new('x', 4096);

    /// <summary>形ごとの行を持つ操作と、その操作の行 Id の前置き・流す形</summary>
    private static readonly (AtomicOp Op, string Slug, AtomicTarget[] Targets)[] FormOps =
    [
        (
            WriteAllBytes,
            "write-all-bytes",
            [
                NoFile,
                ExistingLarger,
                DirectoryAtPath,
                FixedTmpSibling,
                LockedFile,
                LongName,
                SurrogateAtCut,
                MissingParentFolder,
            ]
        ),
        (
            Write,
            "write-stream",
            [
                NoFile,
                ExistingLarger,
                DirectoryAtPath,
                FixedTmpSibling,
                LockedFile,
                LongName,
                SurrogateAtCut,
                MissingParentFolder,
            ]
        ),
        (WriteAllText, "write-all-text", [SurrogateAtCut, MissingParentFolder]),
        (
            CreateText,
            "create-text",
            [
                ExistingLarger,
                DirectoryAtPath,
                FixedTmpSibling,
                LockedFile,
                LongName,
                SurrogateAtCut,
                MissingParentFolder,
            ]
        ),
    ];

    /// <summary>書き手のコールバックが投げる行を流す形（親フォルダが無いと書き手は呼ばれないので含まない）</summary>
    private static readonly AtomicTarget[] WriterThrowsTargets =
    [
        NoFile,
        ExistingLarger,
        FixedTmpSibling,
        LongName,
        SurrogateAtCut,
    ];

    private static IEnumerable<ScenarioRow<AtomicCase>> FormRows()
    {
        foreach (var (op, slug, targets) in FormOps)
        {
            foreach (var target in targets)
            {
                var failure = AtomicFileDimensions.NaturalFailure(op, target);

                yield return Row(
                    $"atomic-{slug}-{TargetSlug(target)}",
                    $"{op}: {TargetNote(target, failure)}",
                    op,
                    target,
                    failure,
                    failure == None ? Written : Failed,
                    o => ArrangeForm(o, target),
                    o => ActForm(op, target, o),
                    o => ExpectForm(target, failure, o)
                );
            }
        }

        // 書き手のコールバックの失敗は保存先の形に依らず、一時ファイルを掃除して例外をそのまま伝え、保存先を変えない
        foreach (var target in WriterThrowsTargets)
        {
            yield return Row(
                $"atomic-write-stream-writer-throws-{TargetSlug(target)}",
                $"Write: 書き出し処理が途中で失敗しても、{TargetNote(target, WriterThrows)}",
                Write,
                target,
                WriterThrows,
                Failed,
                o => ArrangeForm(o, target),
                o =>
                {
                    AtomicFile.Write(
                        FormPath(o, target),
                        stream =>
                        {
                            stream.Write([1, 2, 3]);

                            throw new InvalidOperationException("途中で失敗");
                        }
                    );
                    return null;
                },
                o => ExpectWriterThrows(target, o)
            );
        }
    }

    /// <summary>行 Id に使う、保存先の形の短い名前</summary>
    private static string TargetSlug(AtomicTarget target) =>
        target switch
        {
            NoFile => "creates-new-file",
            ExistingLarger => "shorter-content-leaves-no-remnant",
            DirectoryAtPath => "path-is-directory",
            FixedTmpSibling => "does-not-use-fixed-temporary-name",
            LockedFile => "locked-file",
            LongName => "long-file-name",
            SurrogateAtCut => "surrogate-at-cut",
            MissingParentFolder => "missing-parent-folder",
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
        };

    /// <summary>行の説明に使う、形の言い回し</summary>
    private static string TargetNote(AtomicTarget target, AtomicFailure failure) =>
        target switch
        {
            NoFile => "保存先を作らず .tmp を残さない",
            ExistingLarger when failure == CreateConflict =>
                "長い既存のファイルは置き換えずに IOException で断り、内容を変えない",
            ExistingLarger when failure == WriterThrows => "長い既存のファイルは書き込み前のまま",
            ExistingLarger => "短い内容で上書きしても旧内容が残らない",
            DirectoryAtPath => "保存先がディレクトリなら .tmp を残さず例外を投げる",
            FixedTmpSibling when failure == WriterThrows => "固定名 {path}.tmp の兄弟を消さない",
            FixedTmpSibling => "固定名 {path}.tmp を使わない",
            LockedFile when failure == CreateConflict =>
                "掴まれた保存先は置き換えずに断り、元ファイルは無傷・.tmp なし",
            LockedFile => "差し替え失敗時は元ファイル無傷・.tmp なし・例外を伝播",
            LongName => "255 文字の名前の保存先を扱える",
            SurrogateAtCut => "一時ファイルの名前を切る位置がサロゲートペアの途中でも扱える",
            MissingParentFolder =>
                "親フォルダが無ければ作らずに IOException で断り、.tmp を残さない",
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
        };

    /// <summary>形ごとの保存先のパス</summary>
    private static string FormPath(AtomicObservation o, AtomicTarget target) =>
        target switch
        {
            LongName => Path.Combine(o.Folder, new string('x', 250) + ".json"),
            // 218 文字目と 219 文字目がサロゲートペアになる名前（一時ファイルの名前を切る位置）
            SurrogateAtCut => Path.Combine(
                o.Folder,
                new string('a', 217) + "\U0001F600" + new string('b', 30)
            ),
            MissingParentFolder => Path.Combine(o.Folder, "not-created", "form.json"),
            _ => Path.Combine(o.Folder, "form.json"),
        };

    /// <summary>形ごとの準備</summary>
    private static void ArrangeForm(AtomicObservation o, AtomicTarget target)
    {
        var path = FormPath(o, target);

        switch (target)
        {
            case ExistingLarger:
                File.WriteAllText(path, LargerText);
                break;
            case DirectoryAtPath:
                Directory.CreateDirectory(path);
                break;
            case FixedTmpSibling:
                File.WriteAllText(path + ".tmp", SiblingText);
                break;
            case LockedFile:
                File.WriteAllText(path, "original");
                o.Lock = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
                break;
        }
    }

    /// <summary>操作を呼ぶ（4 つの操作を同じ形で流すための入口）</summary>
    private static void WriteVia(AtomicOp op, string path, string text)
    {
        switch (op)
        {
            case WriteAllText:
                AtomicFile.WriteAllText(path, text);
                break;
            case WriteAllBytes:
                AtomicFile.WriteAllBytes(path, Encoding.UTF8.GetBytes(text));
                break;
            case Write:
                var bytes = Encoding.UTF8.GetBytes(text);

                AtomicFile.Write(path, stream => stream.Write(bytes));
                break;
            case CreateText:
                AtomicFile.CreateText(path, text);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(op), op, null);
        }
    }

    /// <summary>形ごとの入口の呼び方（長い名前は上書きできる操作では 2 回書く）</summary>
    private static object? ActForm(AtomicOp op, AtomicTarget target, AtomicObservation o)
    {
        var path = FormPath(o, target);

        if (target is LongName or SurrogateAtCut)
        {
            // 置き換えない書き込みは 2 回目が衝突するので 1 回だけ
            if (op != CreateText)
            {
                WriteVia(op, path, "first");
            }

            WriteVia(op, path, "second");
            return null;
        }

        WriteVia(op, path, FormText);
        return null;
    }

    /// <summary>形ごとの表明（一時ファイルの残りの照合は流す側が全行に掛ける）</summary>
    /// <remarks>
    /// SurrogateAtCut の行は「その名前でも書けて残骸が残らない」ことだけを見る。NTFS は孤立サロゲートの名前も受け付けうるので、
    /// 一時ファイルの名前でペアを割る退行（<c>TemporaryPathFor</c> の <c>IsHighSurrogate</c> の補正を外す）があっても、
    /// この行は緑のまま残りうる。退行を見ているのは <c>atomic-temporary-path-does-not-split-surrogate-pair</c>（TemporaryPathFor の行）。
    /// </remarks>
    private static void ExpectForm(AtomicTarget target, AtomicFailure failure, AtomicObservation o)
    {
        var path = FormPath(o, target);

        switch (target)
        {
            case NoFile:
                File.ReadAllText(path).Should().Be(FormText);
                Directory.GetFiles(o.Folder).Should().ContainSingle();
                break;
            case ExistingLarger when failure == CreateConflict:
                o.ShouldHaveThrown<IOException>("置き換えない書き込みは既存のファイルで失敗する");
                File.ReadAllText(path)
                    .Should()
                    .Be(LargerText, "断られても保存先は書き込み前のまま");
                break;
            case ExistingLarger:
                File.ReadAllText(path)
                    .Should()
                    .Be(FormText, "短い内容で上書きしても旧内容の尻尾が残らない");
                break;
            case DirectoryAtPath:
                ShouldBeIoFailure(
                    o.ShouldHaveThrown<Exception>("書き込みの失敗を握り潰さない").Which
                );
                Directory.Exists(path).Should().BeTrue("保存先のディレクトリはそのまま");
                break;
            case FixedTmpSibling:
                File.ReadAllText(path).Should().Be(FormText);
                File.ReadAllText(path + ".tmp")
                    .Should()
                    .Be(SiblingText, "一時ファイル名には GUID が挟まる");
                break;
            case LockedFile:
                // File.Replace は IOException・フォールバックの File.Move は UnauthorizedAccessException を投げる
                // （OS/ランタイム依存）ため、型は IO 系の両方を許容する
                ShouldBeIoFailure(
                    o.ShouldHaveThrown<Exception>("差し替えの失敗を握り潰さない").Which
                );

                o.ReleaseLock();

                File.ReadAllText(path)
                    .Should()
                    .Be("original", "失敗しても保存先は書き込み前のまま");
                break;
            case LongName:
            case SurrogateAtCut:
                File.ReadAllText(path).Should().Be("second");
                Directory.GetFiles(o.Folder).Should().ContainSingle();
                break;
            case MissingParentFolder:
                // 型は XmlDoc が決めていない（実際は DirectoryNotFoundException）ので IOException の基底で受ける
                o.ShouldHaveThrown<IOException>("保存先フォルダは作らず、書き込みは失敗する");
                Directory
                    .Exists(Path.GetDirectoryName(path))
                    .Should()
                    .BeFalse("保存先フォルダの作成は呼び出し側の責務で、書き込みは作らない");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(target), target, null);
        }
    }

    /// <summary>書き手のコールバックが投げた行の表明</summary>
    private static void ExpectWriterThrows(AtomicTarget target, AtomicObservation o)
    {
        var path = FormPath(o, target);

        o.ShouldHaveThrown<InvalidOperationException>().WithMessage("途中で失敗");

        switch (target)
        {
            case ExistingLarger:
                File.ReadAllText(path)
                    .Should()
                    .Be(LargerText, "失敗しても保存先は書き込み前のまま");
                break;
            case FixedTmpSibling:
                File.Exists(path).Should().BeFalse("失敗した書き込みは保存先を作らない");
                File.ReadAllText(path + ".tmp")
                    .Should()
                    .Be(SiblingText, "掃除は自分の GUID 名の一時ファイルだけ");
                break;
            default:
                File.Exists(path).Should().BeFalse("失敗した書き込みは保存先を作らない");
                Directory.GetFiles(o.Folder).Should().BeEmpty("失敗時に一時ファイルを残さない");
                break;
        }
    }
}
