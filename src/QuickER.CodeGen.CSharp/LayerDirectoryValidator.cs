namespace QuickER.CodeGen.CSharp;

/// <summary>
/// 層別出力（<see cref="CodeGenerationOptions.LayeredOutput"/>）の層フォルダパスの妥当性判定・正規化の唯一の正。
/// </summary>
/// <remarks>
/// 生成前の診断（<c>CSharpCodeGenerationService.Validate</c>）と書き出し時の防御（<see cref="GeneratedFileWriter"/>）が
/// 同じ規則を共有する。許可するのは「出力ディレクトリ内に収まる相対パス」だけ:
/// 絶対パス・ドライブ指定（<c>:</c>）・<c>..</c>／<c>.</c> セグメント・空セグメント・ファイル名に使えない文字を拒否する。
/// 区切りは <c>/</c>・<c>\</c> のどちらも受け付け、<see cref="Normalize"/> が OS の区切り文字へ揃える。
/// </remarks>
public static class LayerDirectoryValidator
{
    /// <summary>パス区切りとして受け付ける文字（Windows / Unix 両方の表記を許可する）</summary>
    private static readonly char[] Separators = ['/', '\\'];

    /// <summary>層フォルダパスとして妥当か（出力ディレクトリ内に収まる相対パスか）を判定する</summary>
    public static bool IsValid(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var trimmed = value.Trim();

        if (trimmed.Length == 0)
        {
            return false;
        }

        // ドライブ指定（"C:\..." だけでなくドライブ相対 "C:foo" も含む）と絶対パスを拒否する
        if (trimmed.Contains(':') || Path.IsPathRooted(trimmed))
        {
            return false;
        }

        foreach (var segment in Segments(trimmed))
        {
            // 空セグメント（連続区切り）・現在/親ディレクトリ参照を拒否する（出力先外への逸脱防止）
            if (segment.Length == 0 || segment == "." || segment == "..")
            {
                return false;
            }

            if (segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>層フォルダパスを OS のパス区切りへ正規化する（<see cref="IsValid"/> が真の値にだけ使う）</summary>
    public static string Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return string.Join(Path.DirectorySeparatorChar, Segments(value.Trim()));
    }

    /// <summary>末尾の区切りを許容しつつセグメントへ分割する（"MyApp.Domain/Generated/" → ["MyApp.Domain", "Generated"]）</summary>
    private static string[] Segments(string trimmed) =>
        trimmed.TrimEnd(Separators).Split(Separators);
}
