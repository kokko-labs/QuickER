namespace QuickER.Gui.Abstractions;

/// <summary>アプリケーションの再起動を抽象化するインターフェース</summary>
/// <remarks>
/// ViewModel から <c>Application.Current</c> を直接参照しないための抽象。
/// 実装は現在のプロセスを（自動保存などの終了処理を尊重して）終了させ、新しいインスタンスを起動する。
/// 単体テストではスタブへ差し替え、実際にプロセスを再起動せず呼び出しの有無を検証する。
/// </remarks>
public interface IApplicationRestartService
{
    /// <summary>現在のアプリを終了し、新しいインスタンスを起動する</summary>
    void Restart();
}
