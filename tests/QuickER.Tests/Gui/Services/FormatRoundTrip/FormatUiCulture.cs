using System.Globalization;

namespace QuickER.Tests.Gui.Services.FormatRoundTrip;

/// <summary>エンジンを流す UI のカルチャ（製品の中立＝英語の文）を一時的に置いて戻す</summary>
/// <remarks>
/// <para>
/// 製品の取込・生成のメッセージは <see cref="CultureInfo.CurrentUICulture"/> で文が変わる。エンジンの照合（既知の失敗の
/// 理由の断片・規則の行の断り方と警告の期待）が実行する機械の UI のカルチャに依らないよう、ケースの本体と、resx から期待の文を
/// 組む表の構築をこの中で流す。
/// </para>
/// <para>
/// 置くのは UI のカルチャだけで、書式のカルチャ（<see cref="CultureInfo.CurrentCulture"/>）には触らない
/// （カルチャの代表の形は書式のカルチャを動かして確かめる）。
/// </para>
/// </remarks>
internal static class FormatUiCulture
{
    /// <summary>エンジンを流す UI のカルチャ（インバリアント＝resx の中立の英語）</summary>
    public static CultureInfo Culture => CultureInfo.InvariantCulture;

    /// <summary>UI のカルチャを置いて処理を流し、元へ戻す</summary>
    public static T Run<T>(Func<T> body)
    {
        var previous = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = Culture;
            return body();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    /// <summary>UI のカルチャを置いて処理を流し、元へ戻す</summary>
    public static void Run(Action body) =>
        Run(() =>
        {
            body();
            return 0;
        });
}
