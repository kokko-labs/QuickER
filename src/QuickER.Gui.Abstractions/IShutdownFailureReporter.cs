namespace QuickER.Gui.Abstractions;

/// <summary>アプリ終了の連鎖で受け止めた例外を記録するための口</summary>
/// <remarks>
/// <para>
/// 終了の連鎖（フィーチャーモジュールの後始末・モードレスウィンドウの強制クローズ）は、
/// 段ごとに独立して失敗しうる。とくに終了時の設定保存はディスクフル・権限・保存先フォルダの
/// パスに同名のファイルがある等で落ちるが、その失敗で後続の段（常駐プロセスを止めるエンジンの破棄・
/// 後続モジュールの後始末）へ届かなくなると、子プロセスが孤児として残る。
/// 段ごとの受け止めをこの口へ集約し、失敗を黙って捨てずに証跡だけ残して連鎖を続ける。
/// </para>
/// <para>
/// <b>実装は決して例外を投げてはならない。</b>ここで投げると、受け止めの目的である
/// 「1 つの失敗が他を止めない」がそのまま破れる（記録の失敗は記録できないだけで、
/// 終了を妨げる理由にはならない）。
/// </para>
/// </remarks>
public interface IShutdownFailureReporter
{
    /// <summary>終了の連鎖の 1 段で起きた失敗を記録する</summary>
    /// <param name="step">
    /// どの段で失敗したかを表す短い識別子（英語・例 <c>AiChat.SaveSettings</c>）。
    /// 機械向けの診断情報のため UI 言語に追従させない。
    /// </param>
    /// <param name="exception">その段が投げた例外</param>
    void Report(string step, Exception exception);
}
