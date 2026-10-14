using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Gui.UndoRedo;

/// <summary>
/// Undo の網羅テスト（<see cref="TrackedPropertyUndoExhaustiveTests"/>・<see cref="SelectionInvariantTests"/>・
/// <see cref="RemovalOrderRoundTripTests"/>）が共有する、既知の失敗の台帳。
/// </summary>
/// <remarks>
/// 突き合わせの規則は <see cref="KnownFailureLedger"/> が持つ。区切りの最後に空にした状態を保つ。
/// </remarks>
internal static class UndoKnownFailures
{
    /// <summary>既知の失敗（ケース ID → 期待する失敗の理由の断片）</summary>
    private static readonly KnownFailureLedger Ledger = new(new Dictionary<string, string>());

    /// <summary>既知の失敗（ケース ID → 期待する失敗の理由の断片）</summary>
    public static IReadOnlyDictionary<string, string> Ids => Ledger.Known;

    /// <inheritdoc cref="KnownFailureLedger.Run"/>
    public static void Run(string caseId, Action body, List<string>? problems = null) =>
        Ledger.Run(caseId, body, problems);

    /// <inheritdoc cref="KnownFailureLedger.ThrowIfAny"/>
    public static void ThrowIfAny(List<string> problems) => KnownFailureLedger.ThrowIfAny(problems);
}
