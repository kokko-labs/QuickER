using System.IO;
using QuickER.Documents;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Document;

/// <summary>図ファイルの形式の表で、行が呼ぶ JsonStorageService の段</summary>
internal enum FormatStage
{
    /// <summary><see cref="JsonStorageService.Load"/>（読めなければ例外）</summary>
    Load,

    /// <summary>5 引数の <see cref="JsonStorageService.TryLoad(string, out DiagramDocument?, out DocumentLoadError, out Exception?, out byte[]?)"/></summary>
    TryLoad,

    /// <summary><see cref="JsonStorageService.SaveAtomic"/> で書いてから <see cref="JsonStorageService.Load"/> で読み戻す</summary>
    RoundTrip,

    /// <summary><see cref="JsonStorageService.SaveAtomic"/>（失敗は例外として控える）</summary>
    SaveAtomic,

    /// <summary><see cref="JsonStorageService.SaveNew"/>（失敗は例外として控える）</summary>
    SaveNew,

    /// <summary><see cref="JsonStorageService.IsNewerFormatFile"/></summary>
    VersionProbe,
}

/// <summary>行の結果の種類（行が宣言し、表の流し手が実際の結果と照らし合わせる。網羅の計数もこれを数える）</summary>
internal enum FormatResult
{
    /// <summary>図として読めた（Load・RoundTrip の成功・TryLoad の真）</summary>
    Loaded,

    /// <summary>TryLoad が IO の失敗で断った</summary>
    ReadFailed,

    /// <summary>TryLoad が JSON として読めないと断った</summary>
    InvalidJson,

    /// <summary>TryLoad が図でないと断った</summary>
    NotDiagramDocument,

    /// <summary>TryLoad が Id の重複で断った</summary>
    DuplicateId,

    /// <summary>TryLoad が必須の文字列の欠落で断った</summary>
    MissingRequiredText,

    /// <summary>TryLoad が図に無い表・列を指す参照で断った</summary>
    BrokenReference,

    /// <summary>書けた（SaveAtomic・SaveNew が例外なしで戻った）</summary>
    Written,

    /// <summary>例外を投げた（Load・SaveAtomic・SaveNew）</summary>
    Threw,

    /// <summary>IsNewerFormatFile が真</summary>
    Newer,

    /// <summary>IsNewerFormatFile が偽</summary>
    NotNewer,
}

/// <summary>図ファイルの形式の表の 1 行の入力と期待</summary>
/// <remarks>
/// 流し手（<see cref="DocumentFormatRunner"/>）は、一時フォルダの <c>doc.json</c> を対象に、
/// 前置き（<see cref="Directory"/>・<see cref="Prior"/>・<see cref="Json"/> の順）→ 段 → 結果の照合 → <see cref="Expect"/> の順に流す。
/// </remarks>
internal sealed record FormatCase
{
    /// <summary>呼ぶ段</summary>
    public required FormatStage Stage { get; init; }

    /// <summary>期待する結果の種類（実際と食い違えば赤）</summary>
    public required FormatResult Result { get; init; }

    /// <summary>true なら対象のパスにフォルダを置く（差し替えの失敗を起こす）</summary>
    public bool Directory { get; init; }

    /// <summary>段の前に SaveAtomic で書いておく図（既にあるファイル。無ければ null）</summary>
    public Func<DiagramDocument>? Prior { get; init; }

    /// <summary>段の前にそのまま書く文字列（無ければ null。<see cref="Prior"/> の後に書く）</summary>
    public string? Json { get; init; }

    /// <summary>段が書く図（RoundTrip・SaveAtomic・SaveNew）</summary>
    public Func<DiagramDocument>? Document { get; init; }

    /// <summary>元のテストの表明（結果の照合の後に当てる。無ければ null）</summary>
    public Action<FormatOutcome>? Expect { get; init; }
}

/// <summary>1 行を流した結果</summary>
/// <param name="Path">対象のパス（一時フォルダの doc.json）</param>
/// <param name="Folder">一時フォルダ</param>
/// <param name="Written">段が書いた図（RoundTrip・SaveAtomic・SaveNew で <see cref="FormatCase.Document"/> から作ったもの）</param>
/// <param name="Loaded">読めた図（Load・TryLoad・RoundTrip）</param>
/// <param name="Returned">SaveAtomic が返した JSON の文字列</param>
/// <param name="FileText">段の後のファイルの文字列（ファイルが無ければ null）</param>
/// <param name="BytesBefore">段の前のファイルのバイト列（無ければ null）</param>
/// <param name="BytesAfter">段の後のファイルのバイト列（無ければ null）</param>
/// <param name="RawContents">TryLoad が返したバイト列</param>
/// <param name="Error">TryLoad の失敗の種類</param>
/// <param name="Exception">TryLoad が持ち帰った例外</param>
/// <param name="Thrown">段が投げた例外</param>
/// <param name="IsNewer">IsNewerFormatFile の値</param>
/// <param name="Leftovers">段の後に残った一時ファイル（doc.json.*.tmp）</param>
internal sealed record FormatOutcome(
    string Path,
    string Folder,
    DiagramDocument? Written,
    DiagramDocument? Loaded,
    string? Returned,
    string? FileText,
    byte[]? BytesBefore,
    byte[]? BytesAfter,
    byte[]? RawContents,
    DocumentLoadError Error,
    Exception? Exception,
    Exception? Thrown,
    bool? IsNewer,
    IReadOnlyList<string> Leftovers
);

/// <summary>図ファイルの形式の表の行を流す</summary>
internal static class DocumentFormatRunner
{
    /// <summary>1 行を流す（結果の種類が宣言と違えば例外。元の表明は行の Expect が当てる）</summary>
    public static void Run(ScenarioRow<FormatCase> row)
    {
        var input = row.Input;
        using var temp = new TestTempFolder();
        var path = System.IO.Path.Combine(temp.Path, "doc.json");

        if (input.Directory)
        {
            System.IO.Directory.CreateDirectory(path);
        }

        if (input.Prior is { } prior)
        {
            JsonStorageService.SaveAtomic(path, prior());
        }

        if (input.Json is { } json)
        {
            File.WriteAllText(path, json);
        }

        var before = File.Exists(path) ? File.ReadAllBytes(path) : null;
        var written = input.Document?.Invoke();
        DiagramDocument? loaded = null;
        string? returned = null;
        byte[]? raw = null;
        var error = DocumentLoadError.None;
        Exception? exception = null;
        Exception? thrown = null;
        bool? isNewer = null;
        bool? tryLoaded = null;

        try
        {
            switch (input.Stage)
            {
                case FormatStage.Load:
                    loaded = JsonStorageService.Load(path);
                    break;
                case FormatStage.TryLoad:
                    tryLoaded = JsonStorageService.TryLoad(
                        path,
                        out loaded,
                        out error,
                        out exception,
                        out raw
                    );
                    break;
                case FormatStage.RoundTrip:
                    returned = JsonStorageService.SaveAtomic(path, Require(written, row));
                    loaded = JsonStorageService.Load(path);
                    break;
                case FormatStage.SaveAtomic:
                    returned = JsonStorageService.SaveAtomic(path, Require(written, row));
                    break;
                case FormatStage.SaveNew:
                    JsonStorageService.SaveNew(path, Require(written, row));
                    break;
                case FormatStage.VersionProbe:
                    isNewer = JsonStorageService.IsNewerFormatFile(path);
                    break;
            }
        }
        catch (Exception ex)
        {
            thrown = ex;
        }

        var outcome = new FormatOutcome(
            path,
            temp.Path,
            written,
            loaded,
            returned,
            File.Exists(path) ? File.ReadAllText(path) : null,
            before,
            File.Exists(path) ? File.ReadAllBytes(path) : null,
            raw,
            error,
            exception,
            thrown,
            isNewer,
            System.IO.Directory.GetFiles(temp.Path, "doc.json.*.tmp")
        );
        var actual = ResultOf(input.Stage, outcome, tryLoaded);

        if (actual != input.Result)
        {
            throw new InvalidOperationException(
                $"{row.Id}: 結果 {actual} ≠ 宣言 {input.Result}（例外: {thrown?.GetType().Name} {thrown?.Message} / TryLoad の例外: {exception?.Message}）"
            );
        }

        input.Expect?.Invoke(outcome);
    }

    private static DiagramDocument Require(DiagramDocument? written, ScenarioRow<FormatCase> row) =>
        written
        ?? throw new InvalidOperationException(
            $"{row.Id}: 段 {row.Input.Stage} には Document が要る"
        );

    /// <summary>実際の結果の種類</summary>
    private static FormatResult ResultOf(FormatStage stage, FormatOutcome outcome, bool? tryLoaded)
    {
        if (outcome.Thrown is not null)
        {
            return FormatResult.Threw;
        }

        return stage switch
        {
            FormatStage.Load or FormatStage.RoundTrip => FormatResult.Loaded,
            FormatStage.TryLoad when tryLoaded == true => FormatResult.Loaded,
            FormatStage.TryLoad => outcome.Error switch
            {
                DocumentLoadError.ReadFailed => FormatResult.ReadFailed,
                DocumentLoadError.InvalidJson => FormatResult.InvalidJson,
                DocumentLoadError.NotDiagramDocument => FormatResult.NotDiagramDocument,
                DocumentLoadError.DuplicateId => FormatResult.DuplicateId,
                DocumentLoadError.MissingRequiredText => FormatResult.MissingRequiredText,
                DocumentLoadError.BrokenReference => FormatResult.BrokenReference,
                _ => throw new InvalidOperationException($"TryLoad が偽で種類が {outcome.Error}"),
            },
            FormatStage.SaveAtomic or FormatStage.SaveNew => FormatResult.Written,
            FormatStage.VersionProbe => outcome.IsNewer == true
                ? FormatResult.Newer
                : FormatResult.NotNewer,
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null),
        };
    }
}

/// <summary>
/// 図ファイルの形式の表（保存・読込の往復・null と欠落の吸収・旧形式・将来版・読込の検査・原子的な保存・版の判定）
/// </summary>
/// <remarks>
/// 行は元の単発のテストの入力（JSON の文字列・組んだ図）と期待（同じ強さの表明）をそのまま持つ。主題ごとの行は
/// <c>DocumentFormatRows.{主題}.cs</c> の private メソッドが返し、<see cref="AllRows"/> に足す。静的なデータは式形式のプロパティにする。
/// </remarks>
internal static partial class DocumentFormatRows
{
    /// <summary>表</summary>
    public static ScenarioTable<FormatCase> Table => new(EngineIds.DocumentFile, AllRows());

    private static IEnumerable<ScenarioRow<FormatCase>> AllRows() =>
        [
            .. TargetDbmsRows(),
            .. RoundTripRows(),
            .. NullAndOmittedRows(),
            .. VersionRows(),
            .. AtomicSaveRows(),
            .. IdIntegrityRows(),
            .. TryLoadClassificationRows(),
            .. RequiredTextRows(),
            .. ReferenceRows(),
            .. ColumnFlagRows(),
            .. KeyOrderAndUniqueRows(),
        ];

    /// <summary>行を作る</summary>
    private static ScenarioRow<FormatCase> Row(string id, string description, FormatCase input) =>
        new(id, null, description, input);
}
