using System.IO;
using System.Runtime.ExceptionServices;
using AwesomeAssertions;
using AwesomeAssertions.Specialized;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Platform.OsBoundary;

/// <summary>原子的な書き込みの表（OS との境界のエンジンの AtomicFile の部分）</summary>
/// <remarks>SUT は QuickER.Platform の <c>AtomicFile</c>。</remarks>
internal enum AtomicOp
{
    /// <summary><c>AtomicFile.WriteAllText</c></summary>
    WriteAllText,

    /// <summary><c>AtomicFile.WriteAllBytes</c></summary>
    WriteAllBytes,

    /// <summary><c>AtomicFile.Write</c>（Stream へ書くコールバック）</summary>
    Write,

    /// <summary><c>AtomicFile.CreateText</c>（置き換えない書き込み）</summary>
    CreateText,

    /// <summary><c>AtomicFile.TryReadAllText</c></summary>
    TryReadAllText,

    /// <summary><c>AtomicFile.TemporaryPathFor</c></summary>
    TemporaryPathFor,

    /// <summary><c>AtomicFile.IsTemporaryFileName</c></summary>
    IsTemporaryFileName,
}

/// <summary>保存先・読む先・名前の形（次元表の因子 Target の水準）</summary>
internal enum AtomicTarget
{
    /// <summary>保存先が無い</summary>
    NoFile,

    /// <summary>既存のファイル</summary>
    ExistingFile,

    /// <summary>書く内容より長い既存のファイル（長い → 短い）</summary>
    ExistingLarger,

    /// <summary>保存先がディレクトリ</summary>
    DirectoryAtPath,

    /// <summary>固定名 <c>{path}.tmp</c> の一時ファイルが同居している</summary>
    FixedTmpSibling,

    /// <summary>他が排他で掴んでいる（<c>FileShare.None</c>）</summary>
    LockedFile,

    /// <summary>親フォルダが無い</summary>
    MissingParentFolder,

    /// <summary>長い名前（一時ファイルの名前が 1 区間の上限を超えうる長さ）</summary>
    LongName,

    /// <summary>上限で切る位置がサロゲートペアの途中になる名前</summary>
    SurrogateAtCut,

    /// <summary>一時ファイルの形の名前</summary>
    TemporaryFormName,

    /// <summary>一時ファイルの形でない名前</summary>
    NonTemporaryName,
}

/// <summary>起こす失敗（次元表の因子 Failure の水準）</summary>
internal enum AtomicFailure
{
    /// <summary>失敗を起こさない</summary>
    None,

    /// <summary>書き込み中に書き手のコールバックが投げる</summary>
    WriterThrows,

    /// <summary>差し替えに失敗する（保存先を掴まれている）</summary>
    ReplaceFails,

    /// <summary>保存先がディレクトリで書けない</summary>
    TargetIsDirectory,

    /// <summary>置き換えない書き込みが既存のファイルと衝突する</summary>
    CreateConflict,

    /// <summary>親フォルダが無く、一時ファイルを作れない（フォルダは作らない）</summary>
    ParentMissing,
}

/// <summary>結果の種類（行が宣言し、流す側が観測と食い違わないかを確かめる）</summary>
internal enum AtomicOutcome
{
    /// <summary>書けた（例外なし）</summary>
    Written,

    /// <summary>失敗を例外で伝えた</summary>
    Failed,

    /// <summary>読み取りの状態を返した</summary>
    Read,

    /// <summary>名前を計算・判定した</summary>
    Computed,
}

/// <summary>1 行を流した観測</summary>
internal sealed class AtomicObservation : IDisposable
{
    private readonly TestTempFolder _temp = new();

    /// <summary>入口の戻り値</summary>
    public object? Value { get; set; }

    /// <summary>入口が投げた例外</summary>
    public Exception? Thrown { get; set; }

    /// <summary>ロック中の保存先（<c>FileShare.None</c> で開いたままのストリーム）</summary>
    public FileStream? Lock { get; set; }

    /// <summary>テスト用の一時フォルダ</summary>
    public string Folder => _temp.Path;

    /// <summary>ロックを放す（行の表明の途中で放す行のため）</summary>
    public void ReleaseLock()
    {
        Lock?.Dispose();
        Lock = null;
    }

    /// <summary>
    /// 一時フォルダにある AtomicFile 自身の一時ファイル（<c>*.tmp</c> のうち Windows の ReplaceFile が残す <c>~RF</c> を含まないもの）を列挙する
    /// </summary>
    public string[] FindTemporaryLeftovers() =>
        Directory
            .GetFiles(Folder, "*.tmp")
            .Where(f => !Path.GetFileName(f).Contains("~RF", StringComparison.OrdinalIgnoreCase))
            .ToArray();

    /// <summary>
    /// 例外の表明を元のテストの書き方（<c>act.Should().Throw&lt;T&gt;()</c>）のまま行う
    /// </summary>
    /// <typeparam name="T">期待する例外の型（派生の型も通る＝元の表明と同じ）</typeparam>
    /// <param name="because">失敗時の理由</param>
    public ExceptionAssertions<T> ShouldHaveThrown<T>(string because = "")
        where T : Exception
    {
        var thrown = Thrown;
        Action act = () =>
        {
            if (thrown is not null)
            {
                ExceptionDispatchInfo.Throw(thrown);
            }
        };

        return act.Should().Throw<T>(because);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        ReleaseLock();
        _temp.Dispose();
    }
}

/// <summary>原子的な書き込みの表の 1 行の入力</summary>
/// <param name="Op">操作</param>
/// <param name="Target">保存先・読む先・名前の形</param>
/// <param name="Failure">起こす失敗</param>
/// <param name="Outcome">結果の種類（観測と照合する）</param>
/// <param name="Arrange">準備（ファイルを置く・ロックする）。一時ファイルの残りは準備の後を基準に数える</param>
/// <param name="Act">入口を呼ぶ（戻り値は <see cref="AtomicObservation.Value"/> に入る）</param>
/// <param name="Expect">元のテストの表明</param>
internal sealed record AtomicCase(
    AtomicOp Op,
    AtomicTarget Target,
    AtomicFailure Failure,
    AtomicOutcome Outcome,
    Action<AtomicObservation> Arrange,
    Func<AtomicObservation, object?> Act,
    Action<AtomicObservation> Expect
)
{
    /// <summary>書く操作か（行の後に一時ファイルの残りを確かめる）</summary>
    public bool Writes =>
        Op
            is AtomicOp.WriteAllText
                or AtomicOp.WriteAllBytes
                or AtomicOp.Write
                or AtomicOp.CreateText;
}

/// <summary>原子的な書き込みの表（操作 × 保存先の形 × 失敗 → 結果）</summary>
/// <remarks>
/// 行は元の単発のテストと同じ入力・同じ表明を持つ（主題ごとのファイル：<c>AtomicFileRows.Write.cs</c>・<c>AtomicFileRows.Forms.cs</c>・
/// <c>AtomicFileRows.Read.cs</c>・<c>AtomicFileRows.Names.cs</c>）。
/// 流す側（<see cref="AtomicFileRunner"/>）は、宣言した結果の種類が観測と食い違わないこと、
/// 書く操作の行は行の表明の後に一時ファイルの残りが無いことを全行に掛ける。
/// </remarks>
internal static partial class AtomicFileRows
{
    /// <summary>表</summary>
    public static ScenarioTable<AtomicCase> Table =>
        new(EngineIds.OsBoundary, [.. WriteRows(), .. FormRows(), .. ReadRows(), .. NameRows()]);

    /// <summary>行を作る</summary>
    private static ScenarioRow<AtomicCase> Row(
        string id,
        string description,
        AtomicOp op,
        AtomicTarget target,
        AtomicFailure failure,
        AtomicOutcome outcome,
        Action<AtomicObservation> arrange,
        Func<AtomicObservation, object?> act,
        Action<AtomicObservation> expect
    ) =>
        new(
            id,
            null,
            description,
            new AtomicCase(op, target, failure, outcome, arrange, act, expect)
        );
}

/// <summary>原子的な書き込みの表の行を流す</summary>
internal static class AtomicFileRunner
{
    /// <summary>1 行を流す（準備 → 入口 → 結果の種類の照合 → 行の表明 → 一時ファイルの残りの照合）</summary>
    public static void Run(AtomicCase row)
    {
        using var observation = new AtomicObservation();
        row.Arrange(observation);
        var before = observation.FindTemporaryLeftovers();

        try
        {
            observation.Value = row.Act(observation);
        }
        catch (Exception ex)
        {
            observation.Thrown = ex;
        }

        CheckOutcome(row.Outcome, observation);
        row.Expect(observation);

        if (row.Writes)
        {
            observation
                .FindTemporaryLeftovers()
                .Except(before)
                .Should()
                .BeEmpty("書く操作は成功しても失敗しても一時ファイルを残さない");
        }
    }

    /// <summary>宣言した結果の種類が観測と食い違わないことを確かめる（全行に共通）</summary>
    private static void CheckOutcome(AtomicOutcome outcome, AtomicObservation o)
    {
        if (outcome == AtomicOutcome.Failed)
        {
            o.Thrown.Should().NotBeNull("失敗の行は例外で伝える");
            return;
        }

        o.Thrown.Should().BeNull($"結果が {outcome} の行は例外を出さない");

        if (outcome == AtomicOutcome.Read)
        {
            o.Value.Should().BeOfType<ValueTuple<QuickER.Platform.FileReadStatus, string?>>();
        }
    }
}
