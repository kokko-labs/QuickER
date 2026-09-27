using QuickER.Gui.Abstractions;

namespace QuickER.Tests.TestDoubles;

/// <summary>
/// アプリ終了の連鎖で受け止めた失敗を記録するだけの検証用 <see cref="IShutdownFailureReporter"/>。
/// </summary>
/// <remarks>
/// どの段（<c>step</c>）が失敗したかを順に控えるだけで、実ログは書かない。
/// 契約どおり <see cref="Report"/> は例外を投げない。
/// </remarks>
internal sealed class RecordingShutdownFailureReporter : IShutdownFailureReporter
{
    /// <summary>記録された失敗（段の名前と例外）を受け取った順に保持する</summary>
    public List<(string Step, Exception Exception)> Reports { get; } = [];

    /// <summary>記録された段の名前だけを順に返す</summary>
    public IReadOnlyList<string> Steps => Reports.Select(report => report.Step).ToList();

    /// <inheritdoc />
    public void Report(string step, Exception exception) => Reports.Add((step, exception));
}
