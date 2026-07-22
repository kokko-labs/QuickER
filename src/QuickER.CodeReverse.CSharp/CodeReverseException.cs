namespace QuickER.CodeReverse.CSharp;

/// <summary>
/// リバース解析を続行できない致命的な問題（解析対象クラスが 0 件など）を表す例外。
/// </summary>
/// <remarks>
/// メッセージはローカライズ済み（<see cref="Resources.Strings"/> 由来）で、そのままユーザーへ提示できる。
/// CLI は標準エラーへ出力して終了コード 1、GUI はエラーダイアログで提示する。
/// </remarks>
public sealed class CodeReverseException : Exception
{
    /// <summary>ローカライズ済みメッセージを指定して生成する</summary>
    public CodeReverseException(string message)
        : base(message) { }
}
