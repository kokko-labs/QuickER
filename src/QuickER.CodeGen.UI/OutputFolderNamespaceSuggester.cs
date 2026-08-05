using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using QuickER.CodeGen.CSharp;

namespace QuickER.CodeGen.UI;

/// <summary>出力先フォルダのパスから、生成コードの名前空間の候補を導出する</summary>
/// <remarks>
/// Visual Studio の「フォルダ既定 namespace」と同等の規則で、フォルダを含むプロジェクト（*.csproj）の
/// ルート名前空間へ、プロジェクトディレクトリからの相対フォルダ階層を <c>.</c> で連結する。
/// csproj が見つからないときは選択フォルダ名 1 セグメントのみを候補にする。
/// 導出結果は必ず C# 識別子として妥当なセグメント列（<see cref="CSharpGenerationDialogViewModel"/> の
/// namespace 検証を通る形）になるようサニタイズする
/// </remarks>
internal static class OutputFolderNamespaceSuggester
{
    /// <summary>出力先フォルダのパスから名前空間の候補を導出する</summary>
    /// <param name="folderPath">出力先として選択されたフォルダのパス</param>
    /// <returns>導出した名前空間。導出できない場合（存在しないパス・全セグメントが空など）は null</returns>
    public static string? TryDerive(string folderPath)
    {
        // 空・存在しないパスは導出不能（フォルダピッカーは通常存在するフォルダを返すが、防御的に確認する）
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            return null;
        }

        try
        {
            // 生の（未サニタイズの）セグメント列を組み立てる
            var rawSegments = BuildRawSegments(folderPath);

            // 各セグメントを C# 識別子として妥当な形へ整え、空になったものは捨てる
            var segments = rawSegments
                .Select(Sanitize)
                .Where(segment => segment.Length > 0)
                .ToList();

            if (segments.Count == 0)
            {
                return null;
            }

            return string.Join('.', segments);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // csproj 読み取り（XDocument.Load）等での IO 失敗に対する保険。
            // ディレクトリ走査の 1 階層のアクセス拒否は FindProjectDirectory 内で吸収されるため、
            // ここまで来るのは走査後の読み取りが失敗したケースに限られる（導出不能として扱う）
            return null;
        }
    }

    /// <summary>
    /// 出力先フォルダを含む最初のプロジェクト（*.csproj）を親方向に探し、
    /// 「ルート名前空間 ＋ プロジェクトディレクトリからの相対フォルダ階層」の生セグメント列を組み立てる。
    /// csproj が見つからなければ選択フォルダ名 1 セグメントのみを返す
    /// </summary>
    private static List<string> BuildRawSegments(string folderPath)
    {
        var folder = new DirectoryInfo(folderPath);
        var projectDirectory = FindProjectDirectory(folder, out var csprojPath);

        if (projectDirectory is null || csprojPath is null)
        {
            // csproj が見つからないときは選択フォルダ名 1 セグメントのみを候補にする
            return new List<string> { folder.Name };
        }

        var segments = new List<string>();

        // ベース = csproj の <RootNamespace>。読めない・無ければ csproj ファイル名（拡張子除去）
        // ルート名前空間自体がドットを含む（例 Acme.App）ため、ドットで分割して各セグメントを別々にサニタイズする
        var baseNamespace = ReadRootNamespace(csprojPath);
        segments.AddRange(baseNamespace.Split('.', StringSplitOptions.RemoveEmptyEntries));

        // プロジェクトディレクトリから選択フォルダまでの相対パスの各セグメントを連結する
        var relative = Path.GetRelativePath(projectDirectory.FullName, folder.FullName);

        var relativeSegments = relative.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries
        );

        foreach (var segment in relativeSegments)
        {
            // 同一ディレクトリ（相対パスが "."）は連結対象にしない
            if (segment != ".")
            {
                segments.Add(segment);
            }
        }

        return segments;
    }

    /// <summary>フォルダから親方向へ走査し、最初に *.csproj を含むディレクトリを返す（ドライブルートまで）</summary>
    /// <param name="csprojPath">見つかった csproj のフルパス（複数あれば名前順で最初の 1 つ）。無ければ null</param>
    /// <remarks>
    /// 読めない階層（アクセス拒否・IO エラー）は「csproj 無し」とみなして走査を続ける（契約）。
    /// 全階層が読めなければ csproj 未検出として返し、呼び出し側の
    /// 「選択フォルダ名 1 セグメント」フォールバックへ委ねる。
    /// </remarks>
    private static DirectoryInfo? FindProjectDirectory(DirectoryInfo start, out string? csprojPath)
    {
        for (var current = start; current is not null; current = current.Parent)
        {
            FileInfo? csproj;

            try
            {
                // 名前順で安定させ、同一ディレクトリに複数 csproj があっても常に同じ 1 つを選ぶ。
                // EnumerateFiles は遅延列挙のため、実際の列挙（FirstOrDefault）でこの階層の IO 例外が出る
                csproj = current
                    .EnumerateFiles("*.csproj", SearchOption.TopDirectoryOnly)
                    .OrderBy(file => file.Name, StringComparer.Ordinal)
                    .FirstOrDefault();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 読めない階層は「csproj 無し」とみなして親方向の走査を続ける。
                // break（走査打ち切り）にしないのは、中間の 1 階層だけ読めないケースで
                // その上位にある本来の csproj を取りこぼさないため
                continue;
            }

            if (csproj is not null)
            {
                csprojPath = csproj.FullName;
                return current;
            }
        }

        csprojPath = null;
        return null;
    }

    /// <summary>csproj の &lt;RootNamespace&gt; を読む。読めない・空なら csproj ファイル名（拡張子除去）へフォールバックする</summary>
    private static string ReadRootNamespace(string csprojPath)
    {
        var fallback = Path.GetFileNameWithoutExtension(csprojPath);

        try
        {
            var document = XDocument.Load(csprojPath);

            // SDK スタイル・旧スタイルの双方に対応するため、名前空間を無視して要素名だけで探す
            var rootNamespace = document
                .Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "RootNamespace")
                ?.Value;

            return string.IsNullOrWhiteSpace(rootNamespace) ? fallback : rootNamespace.Trim();
        }
        catch (System.Xml.XmlException)
        {
            // 壊れた csproj はファイル名ベースへフォールバックする
            return fallback;
        }
    }

    /// <summary>
    /// 1 セグメントを C# 識別子として妥当な形へ整える（無効文字は '_'・先頭数字は '_' 前置・予約語は '_' 前置）
    /// </summary>
    private static string Sanitize(string segment)
    {
        if (string.IsNullOrEmpty(segment))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(segment.Length);

        foreach (var ch in segment)
        {
            // 識別子として有効な文字（Unicode 文字・10 進数字・アンダースコア）はそのまま、それ以外は '_' へ置換
            builder.Append(IsIdentifierChar(ch) ? ch : '_');
        }

        // 先頭が数字なら（識別子は数字始まり不可のため）アンダースコアを前置する
        if (builder.Length > 0 && char.IsDigit(builder[0]))
        {
            builder.Insert(0, '_');
        }

        var candidate = builder.ToString();

        // 予約語（フォルダ名が "class" 等）はそのままだとコンパイル不能な名前空間になるためアンダースコアを前置する。
        // 判定表は namespace 検証と同じ CSharpNamespaceValidator を使い、「導出結果は必ず検証を通る」不変条件を保つ
        return CSharpNamespaceValidator.IsReservedKeyword(candidate) ? "_" + candidate : candidate;
    }

    /// <summary>C# 識別子を構成できる文字か（Unicode 文字 \p{L}・10 進数字 \p{Nd}・アンダースコア）</summary>
    private static bool IsIdentifierChar(char ch) =>
        char.IsLetter(ch) || char.IsDigit(ch) || ch == '_';
}
