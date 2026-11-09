using System.Collections;
using System.Reflection;
using System.Text;
using QuickER.Documents;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;
using QuickER.UndoRedo;
using QuickER.ViewModels;
using Xunit.Sdk;

namespace QuickER.Tests.Gui.UndoRedo;

/// <summary>操作の入口（どこから履歴が積まれるか）</summary>
internal enum UndoEntry
{
    /// <summary>画面の操作（MainViewModel のコマンド・メソッド）</summary>
    Screen,

    /// <summary>プロパティの setter を DiagramChangeTracker が自動で記録する</summary>
    Tracker,

    /// <summary>内蔵チャットのツール（ErDiagramDynamicTools）</summary>
    Tool,

    /// <summary>ポインタ・ドラッグのビヘイビア（DragBehavior・DataGridRowReorderBehavior）</summary>
    Behavior,
}

/// <summary>行の追加の表明を当てる段</summary>
internal enum UndoStage
{
    /// <summary>操作の直後</summary>
    Operated,

    /// <summary>Undo を積んだ件数ぶん行った直後</summary>
    Undone,

    /// <summary>Redo を積んだ件数ぶん行った直後</summary>
    Redone,
}

/// <summary>操作の往復の行の入力</summary>
/// <param name="Build">図を持つ ViewModel を作る（元のテストの図のまま）</param>
/// <param name="Prepare">操作の前の準備（選択など。履歴に積むものも積まないものもある＝積んだ分は前の状態に含む）</param>
/// <param name="Entry">操作の入口</param>
/// <param name="OperationDescription">操作の説明（失敗の文に出す）</param>
/// <param name="Operation">操作</param>
/// <param name="Commands">操作が積む履歴の型（積む順）。空なら履歴を積まず、図も変えない</param>
/// <param name="Checks">段ごとの追加の表明（元のテストの表明だけ）</param>
internal sealed record UndoOperationScenario(
    Func<MainViewModel> Build,
    IReadOnlyList<(string Description, Action<MainViewModel> Apply)> Prepare,
    UndoEntry Entry,
    string OperationDescription,
    Action<MainViewModel> Operation,
    IReadOnlyList<Type> Commands,
    IReadOnlyList<UndoStageCheck> Checks
);

/// <summary>段ごとの追加の表明 1 つ</summary>
/// <param name="Stage">当てる段</param>
/// <param name="Description">何を見るか</param>
/// <param name="Verify">表明（違えば例外）</param>
internal sealed record UndoStageCheck(
    UndoStage Stage,
    string Description,
    Action<MainViewModel> Verify
);

/// <summary>操作の往復の行を組み立てる（<see cref="UndoOperationRows"/> の 1 行）</summary>
/// <param name="id">行の Id（「{主題}-{守っていること}-{形}」の英小文字とハイフン）</param>
/// <param name="description">何を確かめるか（元のテストの DisplayName）</param>
internal sealed class UndoRow(string id, string description)
{
    private readonly List<(string, Action<MainViewModel>)> _prepare = [];
    private readonly List<UndoStageCheck> _checks = [];
    private Func<MainViewModel>? _build;
    private (UndoEntry Entry, string Description, Action<MainViewModel> Apply)? _operation;
    private Type[]? _commands;

    /// <summary>図を作る</summary>
    public UndoRow Diagram(Func<MainViewModel> build)
    {
        _build = build;
        return this;
    }

    /// <summary>操作の前の準備</summary>
    public UndoRow Prepare(string what, Action<MainViewModel> apply)
    {
        _prepare.Add((what, apply));
        return this;
    }

    /// <summary>確かめる操作（行に 1 つ）</summary>
    public UndoRow Operate(UndoEntry entry, string what, Action<MainViewModel> apply)
    {
        if (_operation is not null)
        {
            throw new InvalidOperationException($"行 {id} の操作は 1 つだけ");
        }

        _operation = (entry, what, apply);
        return this;
    }

    /// <summary>操作が積む履歴の型（積む順）</summary>
    public UndoRow Commands(params Type[] commands)
    {
        if (commands.Length == 0)
        {
            throw new InvalidOperationException(
                $"行 {id}: 履歴を積まない操作は NoHistory() で書く"
            );
        }

        _commands = commands;
        return this;
    }

    /// <summary>操作は履歴を積まず、図も変えない</summary>
    public UndoRow NoHistory()
    {
        _commands = [];
        return this;
    }

    /// <summary>段ごとの追加の表明（元のテストの表明だけ）</summary>
    public UndoRow Check(UndoStage stage, string what, Action<MainViewModel> verify)
    {
        _checks.Add(new UndoStageCheck(stage, what, verify));
        return this;
    }

    /// <summary>表の行にする</summary>
    public ScenarioRow<UndoOperationScenario> Build()
    {
        if (_build is null || _operation is not { } operation || _commands is null)
        {
            throw new InvalidOperationException(
                $"行 {id} に図・操作・積む履歴（Commands か NoHistory）のどれかが無い"
            );
        }

        return new ScenarioRow<UndoOperationScenario>(
            id,
            null,
            description,
            new UndoOperationScenario(
                _build,
                [.. _prepare],
                operation.Entry,
                operation.Description,
                operation.Apply,
                _commands,
                [.. _checks]
            )
        );
    }
}

/// <summary>Undo の履歴（<see cref="UndoRedoManager"/> の Undo のスタック）を読む</summary>
/// <remarks>
/// 製品は積んだコマンドを公開しないので、テストだけが反射でスタックを読む。フィールドが見つからなければ例外にする
/// （黙って空を返さない）。
/// </remarks>
internal static class UndoHistory
{
    private static readonly FieldInfo UndoField =
        typeof(UndoRedoManager).GetField("_undo", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(
            "UndoRedoManager の Undo のスタック（_undo）が見つからない。UndoHistory を製品の形に合わせて直すこと"
        );

    /// <summary>Undo のスタックに積まれたコマンドの型（古い順）</summary>
    public static IReadOnlyList<Type> Types(UndoRedoManager manager) =>
        ((IEnumerable)UndoField.GetValue(manager)!)
            .Cast<object>()
            .Select(command => command.GetType())
            .Reverse()
            .ToList();
}

/// <summary>操作の往復の行を流す</summary>
/// <remarks>
/// 順：準備 → 前の写し → 操作（積んだ履歴の型と件数・未保存・図が変わる）→ 後の写し → Undo を件数ぶん（前の写しと一致）→
/// Redo を件数ぶん（後の写しと一致）→ もう一度 Undo（前の写しと一致）。各段で選択の不変条件
/// （<see cref="SelectionInvariant.Assert"/>）と、操作の前の図の DDL の制約名がすべて違えばその段の図もすべて違うこと（<see cref="DdlConstraintNames"/>）と、行の追加の表明を当てる。写しは保存文書（<see cref="MainViewModel.ToDocument"/>）の
/// JSON（表・列・リレーション・一意制約・クエリの並びとレイアウトを保つ）。
/// </remarks>
internal static class UndoOperationRunner
{
    /// <summary>行を流す（合わなければ行の Id と段を添えて赤）</summary>
    public static void Run(ScenarioRow<UndoOperationScenario> row)
    {
        var scenario = row.Input;
        var vm = scenario.Build();

        foreach (var (_, apply) in scenario.Prepare)
        {
            apply(vm);
        }

        var before = Snapshot(vm);
        var distinctConstraintNames = HasDistinctDdlConstraintNames(vm);
        var historyBefore = UndoHistory.Types(vm.UndoRedo).Count;
        var dirtyBefore = vm.IsDirty;
        var generationBefore = vm.UndoRedo.ChangeGeneration;

        scenario.Operation(vm);

        var pushed = UndoHistory.Types(vm.UndoRedo).Skip(historyBefore).ToList();

        if (!pushed.SequenceEqual(scenario.Commands))
        {
            Fail(
                row,
                "操作の直後",
                $"積んだ履歴が [{Names(pushed)}]（期待 [{Names(scenario.Commands)}]）"
            );
        }

        if (scenario.Commands.Count == 0)
        {
            if (vm.UndoRedo.ChangeGeneration != generationBefore || vm.IsDirty != dirtyBefore)
            {
                Fail(row, "操作の直後", "履歴を積まない操作が履歴の世代か未保存の状態を変えた");
            }

            if (Snapshot(vm) != before)
            {
                Fail(row, "操作の直後", "履歴を積まない操作が図を変えた");
            }

            Stage(row, vm, distinctConstraintNames, UndoStage.Operated, "操作の直後");
            return;
        }

        if (!vm.IsDirty)
        {
            Fail(row, "操作の直後", "履歴を積んだのに未保存になっていない");
        }

        var after = Snapshot(vm);

        if (after == before)
        {
            Fail(row, "操作の直後", "履歴を積んだのに図（保存文書の写し）が変わっていない");
        }

        Stage(row, vm, distinctConstraintNames, UndoStage.Operated, "操作の直後");

        Repeat(scenario.Commands.Count, vm.UndoRedo.Undo);
        Expect(row, "Undo の直後", before, Snapshot(vm));
        Stage(row, vm, distinctConstraintNames, UndoStage.Undone, "Undo の直後");

        Repeat(scenario.Commands.Count, vm.UndoRedo.Redo);
        Expect(row, "Redo の直後", after, Snapshot(vm));
        Stage(row, vm, distinctConstraintNames, UndoStage.Redone, "Redo の直後");

        Repeat(scenario.Commands.Count, vm.UndoRedo.Undo);
        Expect(row, "2 回目の Undo の直後", before, Snapshot(vm));
        SelectionInvariant.Assert(vm, $"{row.Id}（2 回目の Undo の直後）");
    }

    /// <summary>保存文書の JSON（並びを保つ図の写し）</summary>
    public static string Snapshot(MainViewModel vm) => JsonStorageService.ToJson(vm.ToDocument());

    /// <summary>図の SQL Server の DDL の制約名がすべて違うか（DDL を作れない図は偽＝段の表明の対象外）</summary>
    private static bool HasDistinctDdlConstraintNames(MainViewModel vm) =>
        DdlConstraintNames.TryFindDuplicates(vm.ToDiagramModel(), out var duplicates)
        && duplicates.Count == 0;

    /// <summary>段の表明（選択の不変条件・DDL の制約名・行の追加の表明）を当てる</summary>
    /// <param name="distinctConstraintNames">
    /// 操作の前の図の DDL の制約名がすべて違ったか（真なら、この段の図の DDL の制約名もすべて違うことを表明する）
    /// </param>
    private static void Stage(
        ScenarioRow<UndoOperationScenario> row,
        MainViewModel vm,
        bool distinctConstraintNames,
        UndoStage stage,
        string label
    )
    {
        SelectionInvariant.Assert(vm, $"{row.Id}（{label}）");

        // 操作・Undo・Redo は、制約名がすべて違う図から同じ名前の制約を 2 つ持つ図を作らない
        // （複製・貼り付けが一意制約の名前を写す・名前の欄が重複を受け付けると、DDL が同じ制約名を 2 回出して断られる）
        // （段の図が DDL を作れないとき＝列の無い表を足した直後などは、制約名を数えられないので見ない）
        if (
            distinctConstraintNames
            && DdlConstraintNames.TryFindDuplicates(vm.ToDiagramModel(), out var duplicates)
            && duplicates.Count > 0
        )
        {
            Fail(row, label, $"DDL の制約名が重複している: {string.Join(", ", duplicates)}");
        }

        foreach (var check in row.Input.Checks.Where(check => check.Stage == stage))
        {
            try
            {
                check.Verify(vm);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Fail(row, label, $"{check.Description}: {ex.Message}");
            }
        }
    }

    private static void Expect(
        ScenarioRow<UndoOperationScenario> row,
        string label,
        string expected,
        string actual
    )
    {
        if (expected == actual)
        {
            return;
        }

        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');
        var index = 0;

        while (
            index < expectedLines.Length
            && index < actualLines.Length
            && expectedLines[index] == actualLines[index]
        )
        {
            index++;
        }

        Fail(
            row,
            label,
            $"図の写しが違う（{index + 1} 行目から）"
                + Environment.NewLine
                + $"  期待: {Line(expectedLines, index)}"
                + Environment.NewLine
                + $"  実際: {Line(actualLines, index)}"
        );
    }

    private static string Line(string[] lines, int index) =>
        index < lines.Length ? lines[index].Trim() : "（行なし）";

    private static void Repeat(int count, Action action)
    {
        for (var i = 0; i < count; i++)
        {
            action();
        }
    }

    private static string Names(IEnumerable<Type> types) =>
        string.Join(", ", types.Select(type => type.Name));

    private static void Fail(ScenarioRow<UndoOperationScenario> row, string label, string problem)
    {
        var message = new StringBuilder();
        message.AppendLine(
            $"行 {row.Id}（{row.Description}）・{row.Input.Entry}・{row.Input.OperationDescription}・{label}"
        );
        message.Append("  ").Append(problem);
        throw new XunitException(message.ToString());
    }
}
