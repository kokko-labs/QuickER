using QuickER.Platform;

namespace QuickER.CodeGen.CSharp;

/// <summary>
/// 層フォルダ・出力サブフォルダ（出力ディレクトリからの相対パス）の妥当性判定と正規化
/// </summary>
/// <remarks>
/// 生成前の診断（<c>CSharpCodeGenerationService.Validate</c>）と書き出し時の防御（<see cref="GeneratedFileWriter"/>）が
/// 同じ規則を共有する。規則の正本は <see cref="PathSegments.CheckRelativePath"/>（出力ディレクトリ内に収まる相対パスだけを許す）で、
/// ここは生成の入口の名前を与えるだけ。区切りは <c>/</c>・<c>\</c> のどちらも受け付け、<see cref="Normalize"/> が OS の区切り文字へ揃える。
/// 値全体の前後の空白は判定の前に取り除く（決まった形への正規化で、意図と違う場所へは書かない）。区間の内側の前後の空白
/// （<c>a /b</c>・<c>a/ b</c>）は Windows が黙って落とすので断る。
/// </remarks>
public static class LayerDirectoryValidator
{
    /// <summary>層フォルダパスとして断る理由を返す（断らないなら <c>null</c>）</summary>
    public static PathSegmentViolation? Check(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return PathSegments.CheckRelativePath(value.Trim());
    }

    /// <summary>層フォルダパスとして妥当か（出力ディレクトリ内に収まる相対パスか）を判定する</summary>
    public static bool IsValid(string value) => Check(value) is null;

    /// <summary>層フォルダパスを OS のパス区切りへ正規化する（<see cref="IsValid"/> が真の値にだけ使う）</summary>
    public static string Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return string.Join(
            Path.DirectorySeparatorChar,
            PathSegments.SplitRelativePath(value.Trim())
        );
    }
}
