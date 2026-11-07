using System;
using System.IO;
using System.Linq;
using System.Text;
using Xunit.Sdk;

namespace QuickER.Tests.TestSupport.Engines;

/// <summary>
/// テストが宣言から作ってチェックインする生成物のテキスト（改行は LF）を、記録のファイルと照合する。書き直しの指定があれば書き直す
/// </summary>
/// <remarks>
/// 書き直しの指定は生成フィクスチャと同じ環境変数（<see cref="RegenerationRequest"/>）。書き直しは承認であって検証ではない
/// （差分を見て、宣言の変更が意図どおりかを確かめてから書き直す）。記録は <c>.gitattributes</c> で eol=lf に固定する。
/// 読むときは CRLF を LF にそろえる（属性の無いチェックアウトでも、改行だけの違いを差分にしない）。
/// </remarks>
public static class GeneratedTextFile
{
    /// <summary>記録と照合する（書き直しの指定があれば、内容が違うときだけ書き直す）</summary>
    /// <param name="path">記録のファイルの絶対パス</param>
    /// <param name="expected">宣言から作ったテキスト（改行は LF）</param>
    /// <param name="what">何の記録か（失敗の文に添える）</param>
    /// <param name="regenerateSteps">書き直しの手順（失敗の文に添える）</param>
    /// <param name="regenerate">書き直すか（既定は環境変数）</param>
    public static void Verify(
        string path,
        string expected,
        string what,
        string regenerateSteps,
        bool? regenerate = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(expected);

        if (expected.Contains('\r', StringComparison.Ordinal))
        {
            throw new ArgumentException("生成物のテキストの改行は LF だけにする", nameof(expected));
        }

        var actual = File.Exists(path)
            ? File.ReadAllText(path, Encoding.UTF8).Replace("\r\n", "\n", StringComparison.Ordinal)
            : null;

        if (regenerate ?? RegenerationRequest.IsRequested())
        {
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                File.WriteAllText(path, expected, new UTF8Encoding(false));
            }

            return;
        }

        if (actual is null)
        {
            throw new XunitException(
                $"{what}の記録 {path} が無い。書き直しの手順：\n{regenerateSteps}"
            );
        }

        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new XunitException(
                $"{what}の記録が宣言と食い違う（{FirstDifference(actual, expected)}）。宣言の変更が意図どおりなら書き直す：\n{regenerateSteps}"
            );
        }
    }

    /// <summary>最初に食い違う行の番号と、両方のその行</summary>
    private static string FirstDifference(string actual, string expected)
    {
        var actualLines = actual.Split('\n');
        var expectedLines = expected.Split('\n');
        var count = Math.Max(actualLines.Length, expectedLines.Length);

        for (var i = 0; i < count; i++)
        {
            var recorded = i < actualLines.Length ? actualLines[i] : "（無い）";
            var declared = i < expectedLines.Length ? expectedLines[i] : "（無い）";

            if (!string.Equals(recorded, declared, StringComparison.Ordinal))
            {
                return $"{i + 1} 行目　記録：{recorded}　宣言：{declared}";
            }
        }

        return "違いが見つからない";
    }
}

/// <summary>
/// 生成物の書き直しの指定（環境変数 <c>QUICKER_REGEN_FIXTURES</c>）の正本：生成フィクスチャ・サンプル・公開 API の承認ファイル・
/// 種の指紋・守りの台帳の写し・影響の地図のドリフトのテストが、変数の名前・読み方・案内のコマンドをここから組む
/// </summary>
public static class RegenerationRequest
{
    /// <summary>環境変数の名前</summary>
    public const string VariableName = "QUICKER_REGEN_FIXTURES";

    /// <summary>書き直しが指定されているか（<c>1</c> か <c>true</c>〔大文字小文字を問わない〕）</summary>
    public static bool IsRequested()
    {
        var value = Environment.GetEnvironmentVariable(VariableName);

        return string.Equals(value, "1", StringComparison.Ordinal)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>書き直しのコマンド（リポジトリ直下・PowerShell。変数を立て、テストを順に流し、変数を戻す）</summary>
    /// <param name="runs">流すテスト（プロジェクトのパスと <c>FullyQualifiedName~</c> の後に続く絞り込み）を流す順に</param>
    public static string Command(params (string Project, string Filter)[] runs)
    {
        ArgumentNullException.ThrowIfNull(runs);

        if (runs.Length == 0)
        {
            throw new ArgumentException("流すテストを 1 つ以上渡す", nameof(runs));
        }

        return $"$env:{VariableName}=1; "
            + string.Concat(
                runs.Select(run =>
                    $"dotnet test {run.Project} --filter \"FullyQualifiedName~{run.Filter}\"; "
                )
            )
            + $"$env:{VariableName}=$null";
    }
}
