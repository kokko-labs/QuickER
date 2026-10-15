using System.Buffers;
using System.Globalization;
using QuickER.Platform.Resources;

namespace QuickER.Platform;

/// <summary>パスの 1 区間（ファイル名・フォルダ名）として断る理由</summary>
/// <remarks>
/// 判定の順は宣言の順（<see cref="PathSegments.CheckSegment"/>）。1 つの区間が複数の理由に当たるときは、先に当たったものを返す。
/// </remarks>
public enum PathSegmentViolation
{
    /// <summary>空の名前、または空の区間（<c>a//b</c>）</summary>
    Empty,

    /// <summary><c>.</c> または <c>..</c>（出力先の外を指しうる）</summary>
    DotSegment,

    /// <summary>1 区間の名前の中のパス区切り（<c>/</c> または <c>\</c>）</summary>
    Separator,

    /// <summary>絶対パスまたはドライブ指定（相対パスとして受け取る値の先頭）</summary>
    Rooted,

    /// <summary>ファイル名に使えない文字（制御文字と <c>" * : &lt; &gt; ? |</c>）</summary>
    InvalidCharacter,

    /// <summary>先頭の空白</summary>
    LeadingWhitespace,

    /// <summary>末尾の <c>.</c> または空白（Windows は黙って落として開くので、指定と実体が食い違う）</summary>
    TrailingDotOrWhitespace,

    /// <summary><c>~</c>（8.3 形式の短い名前になりうるので、別のフォルダの別名として解決されうる）</summary>
    Tilde,

    /// <summary>
    /// 予約されたデバイス名（<c>CON</c>・<c>PRN</c>・<c>AUX</c>・<c>NUL</c>・<c>CONIN$</c>・<c>CONOUT$</c>・<c>COM1</c>〜<c>COM9</c>・
    /// <c>LPT1</c>〜<c>LPT9</c>・上付き数字の <c>COM¹</c>〜<c>COM³</c>・<c>LPT¹</c>〜<c>LPT³</c>。拡張子つきも）
    /// </summary>
    ReservedDeviceName,

    /// <summary>1 区間が <see cref="PathSegments.MaxLength"/> 文字を超える</summary>
    TooLong,
}

/// <summary>パスの 1 区間と、出力先の中に収まる相対パスの妥当性の判定の正本</summary>
/// <remarks>
/// <para>
/// 利用者の設定・AI の出力から受け取った名前を出力先の下のファイル名・フォルダ名に使う入口（層フォルダ・出力サブフォルダ・
/// API リファレンスのファイル名・生成物のファイル名・モックのプロジェクト名・画面のファイル名・API キー方式の提出のパス）は、
/// すべてここで判定する。入口ごとに判定を持つと、断る形が入口ごとに食い違う（ある入口だけ <c>..</c> や 8.3 の別名を通す）。
/// </para>
/// <para>
/// 判定は OS に依らない（無効な文字は Windows の集合で固定する）。Windows 以外の環境で作った名前を Windows で開けない、
/// を防ぐため。値の前後の空白も取り除かずにそのまま判定する（先頭・末尾の空白は断る理由の 1 つ）。
/// </para>
/// </remarks>
public static class PathSegments
{
    /// <summary>1 区間の名前の長さの上限（UTF-16 の文字数）</summary>
    public const int MaxLength = 255;

    /// <summary>パス区切りとして受け付ける文字（Windows / Unix の両方の表記）</summary>
    private static readonly char[] Separators = ['/', '\\'];

    /// <summary>制御文字のほかにファイル名に使えない文字（パス区切りは <see cref="Separators"/> で別に判定する）</summary>
    private static readonly SearchValues<char> InvalidCharacters = SearchValues.Create("\"*:<>?|");

    /// <summary>上付き数字の 1・2・3（U+00B9・U+00B2・U+00B3）</summary>
    private static readonly string[] SuperscriptDigits = ["\u00B9", "\u00B2", "\u00B3"];

    /// <summary>予約されたデバイス名（大文字小文字を区別しない）</summary>
    private static readonly HashSet<string> ReservedDeviceNames = new(
        [
            "CON",
            "PRN",
            "AUX",
            "NUL",
            "CONIN$",
            "CONOUT$",
            .. Enumerable.Range(1, 9).Select(n => "COM" + n.ToString(CultureInfo.InvariantCulture)),
            .. Enumerable.Range(1, 9).Select(n => "LPT" + n.ToString(CultureInfo.InvariantCulture)),
            // Windows は上付き数字の 1〜3 も COM・LPT の番号として扱う
            .. SuperscriptDigits.Select(digit => "COM" + digit),
            .. SuperscriptDigits.Select(digit => "LPT" + digit),
        ],
        StringComparer.OrdinalIgnoreCase
    );

    /// <summary>1 区間の名前（ファイル名・フォルダ名）として断る理由を返す（断らないなら <c>null</c>）</summary>
    /// <param name="segment">判定する名前（前後の空白も含めてそのまま）</param>
    public static PathSegmentViolation? CheckSegment(string segment)
    {
        ArgumentNullException.ThrowIfNull(segment);

        if (segment.Length == 0)
        {
            return PathSegmentViolation.Empty;
        }

        if (segment is "." or "..")
        {
            return PathSegmentViolation.DotSegment;
        }

        if (segment.AsSpan().IndexOfAny(Separators) >= 0)
        {
            return PathSegmentViolation.Separator;
        }

        if (segment.Any(char.IsControl) || segment.AsSpan().IndexOfAny(InvalidCharacters) >= 0)
        {
            return PathSegmentViolation.InvalidCharacter;
        }

        if (char.IsWhiteSpace(segment[0]))
        {
            return PathSegmentViolation.LeadingWhitespace;
        }

        if (segment[^1] == '.' || char.IsWhiteSpace(segment[^1]))
        {
            return PathSegmentViolation.TrailingDotOrWhitespace;
        }

        if (segment.Contains('~'))
        {
            return PathSegmentViolation.Tilde;
        }

        // Windows は拡張子を付けても予約名として扱う（"NUL.txt" もデバイスを指す）
        var dot = segment.IndexOf('.');
        var baseName = (dot < 0 ? segment : segment[..dot]).TrimEnd();

        if (ReservedDeviceNames.Contains(baseName))
        {
            return PathSegmentViolation.ReservedDeviceName;
        }

        return segment.Length > MaxLength ? PathSegmentViolation.TooLong : null;
    }

    /// <summary>出力先の中に収まる相対パスとして断る理由を返す（断らないなら <c>null</c>）</summary>
    /// <remarks>
    /// 区切りは <c>/</c>・<c>\</c> のどちらも受け付け、末尾の区切りは許す（<c>MyApp.Domain/Generated/</c>）。
    /// 先頭の区切り・ドライブ指定・絶対パスは <see cref="PathSegmentViolation.Rooted"/>、各区間は <see cref="CheckSegment"/> で判定する。
    /// </remarks>
    /// <param name="path">判定する相対パス（前後の空白も含めてそのまま）</param>
    public static PathSegmentViolation? CheckRelativePath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (path.Length == 0)
        {
            return PathSegmentViolation.Empty;
        }

        if (
            path[0] is '/' or '\\'
            || Path.IsPathRooted(path)
            || (path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':')
        )
        {
            return PathSegmentViolation.Rooted;
        }

        foreach (var segment in SplitRelativePath(path))
        {
            if (CheckSegment(segment) is { } violation)
            {
                return violation;
            }
        }

        return null;
    }

    /// <summary>相対パスを区間へ分ける（末尾の区切りは無視する。<see cref="CheckRelativePath"/> が断らない値にだけ使う）</summary>
    public static string[] SplitRelativePath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return path.TrimEnd(Separators).Split(Separators);
    }

    /// <summary>
    /// 断る理由の説明を、指定した言語で返す（画面の文言には CultureInfo.CurrentUICulture、機械向けの英語固定の文言には
    /// CultureInfo.InvariantCulture を渡す）
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">未定義の理由</exception>
    public static string Describe(PathSegmentViolation violation, CultureInfo culture)
    {
        if (!Enum.IsDefined(violation))
        {
            throw new ArgumentOutOfRangeException(nameof(violation), violation, null);
        }

        return Strings.ResourceManager.GetString("PathSegment_" + violation, culture)
            ?? throw new InvalidOperationException($"No text for {violation}.");
    }
}
