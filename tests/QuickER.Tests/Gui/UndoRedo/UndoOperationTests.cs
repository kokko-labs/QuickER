using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Gui.UndoRedo;

/// <summary>VM の操作の往復の行（<see cref="UndoOperationRows"/>）を流す</summary>
public sealed class UndoOperationTests
{
    /// <summary>行の Id</summary>
    public static TheoryData<string> Cases => new(UndoOperationRows.Table.Select(row => row.Id));

    [Theory(DisplayName = "VM の操作は宣言どおりの履歴を積み、Undo / Redo で図が並びまで戻る")]
    [MemberData(nameof(Cases))]
    public void Row_RoundTrips(string id) =>
        UndoKnownFailures.Run(id, () => UndoOperationRunner.Run(UndoOperationRows.Table[id]));

    [Fact(DisplayName = "操作の往復の行は、積む履歴の型 × 入口の組を宣言どおりに網羅する")]
    public void Rows_CoverUndoDimensions()
    {
        var counter = new CoverageCounter(UndoOperationDimensions.Table);
        UndoOperationDimensions.ObserveDeclarations(counter);

        var report = counter.Report();
        report.WriteSummary();
        report.WriteGapUsage(UndoOperationDimensions.Gaps, UndoOperationDimensions.Scope);
        report.AssertGaps(UndoOperationDimensions.Gaps, UndoOperationDimensions.Scope);
    }

    [Fact(
        DisplayName = "Undo の履歴を読む仕組みが、製品のスタックを見つけて積んだ型を古い順に返す"
    )]
    public void UndoHistory_ReadsPushedTypesInOrder()
    {
        var manager = new QuickER.UndoRedo.UndoRedoManager();
        manager.Push(new ProbeCommand());
        manager.Push(new OtherProbeCommand());

        var types = UndoHistory.Types(manager);

        Assert.Equal([typeof(ProbeCommand), typeof(OtherProbeCommand)], types);
    }

    private sealed class ProbeCommand : QuickER.UndoRedo.IUndoableCommand
    {
        public string Description => "ProbeCommand";

        public void Execute() { }

        public void Undo() { }
    }

    private sealed class OtherProbeCommand : QuickER.UndoRedo.IUndoableCommand
    {
        public string Description => "OtherProbeCommand";

        public void Execute() { }

        public void Undo() { }
    }
}
