using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Gui.UndoRedo;

/// <summary>
/// Undo の網羅テスト（<see cref="TrackedPropertyUndoExhaustiveTests"/>・<see cref="SelectionInvariantTests"/>・
/// <see cref="RemovalOrderRoundTripTests"/>）が共有する、既知の失敗の台帳。
/// </summary>
/// <remarks>
/// 突き合わせの規則は <see cref="KnownCaseLedger"/> が持つ。区切りの最後に空にした状態を保つ。
/// </remarks>
internal static class UndoKnownFailures
{
    /// <summary>既知の失敗（<see cref="KnownCaseLedger.Case"/> で作る項目＝ケース ID・期待する失敗の理由の断片）</summary>
    private static readonly KnownCaseLedger Ledger = new("Undo", []);

    /// <summary>一覧に載っているケース ID</summary>
    public static IReadOnlyCollection<string> Ids => Ledger.Ids;

    /// <inheritdoc cref="KnownCaseLedger.Run"/>
    public static void Run(string caseId, Action body, List<string>? problems = null) =>
        Ledger.Run(caseId, body, problems);

    /// <inheritdoc cref="KnownCaseLedger.ThrowIfAny"/>
    public static void ThrowIfAny(List<string> problems) => KnownCaseLedger.ThrowIfAny(problems);
}
