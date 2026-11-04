using System;
using System.Globalization;
using AwesomeAssertions;
using Xunit;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// 固定 infra の書式ヘルパー <see cref="EditModelInputFormat"/> を検証する（カルチャ×型×秒未満の有無の行列は
/// <see cref="EditModelScenarioTests.Format"/> の em-format の表）。
/// </summary>
/// <remarks>
/// <para>
/// 守る性質は 2 つ。(1) <b>秒未満がゼロの値の表示文字列は従来と 1 文字も変わらない</b>（＝その型自身の
/// <c>ToString()</c> と一致する）。既存画面の見え方を変えないための不変条件。(2) <b>秒未満を持つ値の表示文字列は、
/// バインド setter と同じパース（現在カルチャ・<c>IParsable</c> 経由＝<c>TryParse(text, null, out _)</c>）で
/// tick まで完全に往復する</b>。往復しなければ、同じ行の別の列を編集しただけで切り捨てた値が確定値へ戻る。
/// </para>
/// <para>
/// カルチャは書式の癖が割れる 4 つ（ja-JP＝24 時制・en-US＝AM/PM が秒の後ろに付く・de-DE＝小数点がカンマ・
/// fr-FR）とインバリアントを回す。小数部の区切りは不変カルチャの '.' 固定なので、de-DE のように小数点が
/// カンマのカルチャでもパーサーが受けることを em-format の行で確かめている。
/// </para>
/// </remarks>
public sealed class EditModelInputFormatTests
{
    /// <summary>秒未満がゼロの日時（em-format の行と、下のキャッシュのテストが使う）</summary>
    internal static DateTime WholeSecond() =>
        new(2026, 8, 10, 14, 30, 15, DateTimeKind.Unspecified);

    /// <summary>秒未満（7 桁 tick）を持つ日時</summary>
    internal static DateTime WithFraction() => WholeSecond().AddTicks(1_234_567);

    // カルチャ×型×秒未満の有無の行列は EditModelScenarioTests.Format（em-format）が確かめる

    // ===== パターンキャッシュのキー（カルチャ名でなく実パターン） =====

    [Fact(
        DisplayName = "LongTimePattern を差し替えたクローンカルチャが元カルチャのキャッシュに潰されない"
    )]
    public void 差し替えたクローンカルチャの書式が反映される()
    {
        var previous = CultureInfo.CurrentCulture;

        try
        {
            // 元カルチャのパターンを先にキャッシュへ載せる（en-US の長い時刻は 12 時間制 "h:mm:ss tt"）
            var baseline = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentCulture = baseline;
            _ = EditModelInputFormat.Format((DateTime?)WithFraction());

            // CultureInfo の等値は実質 Name 比較なので、クローンはキャッシュのキーとして元と衝突し得る。
            // キーが実パターンなら、差し替えた 24 時間制がそのまま効く
            var clone = (CultureInfo)baseline.Clone();
            clone.DateTimeFormat.LongTimePattern = "HH:mm:ss";
            CultureInfo.CurrentCulture = clone;

            var text = EditModelInputFormat.Format((DateTime?)WithFraction());

            text.Should()
                .Contain(
                    "14:30:15.1234567",
                    "クローンで差し替えた長い時刻パターンで書式化される（元カルチャのキャッシュ済みパターンに潰されない）"
                );
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
