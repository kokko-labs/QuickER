using QuickER.Tests.TestSupport.Engines;
using QuickER.ViewModels;
using Xunit.Sdk;

namespace QuickER.Tests.Gui.UndoRedo;

/// <summary>図から外れた要素を指し得るもの（選択の不変条件の対象）</summary>
internal enum SelectionPointer
{
    /// <summary>選択中のエンティティ</summary>
    SelectedEntity,

    /// <summary>選択中の列</summary>
    SelectedColumn,

    /// <summary>選択中のリレーション</summary>
    SelectedRelationship,

    /// <summary>検索結果</summary>
    SearchResults,

    /// <summary>作成中のリレーションの始点</summary>
    PendingRelationshipSource,

    /// <summary>行の ViewModel の選択候補（列ペア・一意制約の構成列などのコンボボックス）</summary>
    Candidate,
}

/// <summary>要素が図から外れる（または戻る）経路</summary>
internal enum SelectionDetach
{
    /// <summary>Undo / Redo</summary>
    Undo,

    /// <summary>削除の操作</summary>
    Delete,

    /// <summary>図の置換（モジュール・新規作成）</summary>
    Replace,

    /// <summary>図の読み直し</summary>
    Reload,

    /// <summary>一覧の Reset・直接の書き換え（コマンドを通らない変更）</summary>
    Reset,

    /// <summary>パネルを畳む・モードを抜ける</summary>
    CollapsePanel,
}

/// <summary>選択の不変条件の行の入力</summary>
/// <param name="Build">図を持つ ViewModel を作る（元のテストの図のまま）</param>
/// <param name="Steps">手順（各手順の後に不変条件と、その手順に付けた表明を当てる）</param>
/// <param name="Covers">この行が流す（外れ得るもの × 外れ方）の組（網羅の計数が読む）</param>
internal sealed record SelectionScenario(
    Func<MainViewModel> Build,
    IReadOnlyList<SelectionStep> Steps,
    IReadOnlyList<(SelectionPointer Pointer, SelectionDetach Detach)> Covers
);

/// <summary>手順 1 つ</summary>
/// <param name="Label">何をするか（失敗の文に出す）</param>
/// <param name="Apply">手順</param>
/// <param name="Checks">手順の後の表明（元のテストの表明だけ。説明と表明の組）</param>
internal sealed record SelectionStep(
    string Label,
    Action<SelectionContext> Apply,
    IReadOnlyList<(string Description, Action<SelectionContext> Verify)> Checks
);

/// <summary>1 回の実行の文脈（ViewModel と、手順をまたいで覚えておく値）</summary>
internal sealed class SelectionContext(MainViewModel vm)
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

    /// <summary>図を持つ ViewModel</summary>
    public MainViewModel Vm { get; } = vm;

    /// <summary>値を覚える（後の手順・表明が <see cref="Recall{T}"/> で読む）</summary>
    public void Remember(string key, object? value) => _values[key] = value;

    /// <summary>覚えた値を読む（無ければ例外）</summary>
    public T Recall<T>(string key) =>
        _values.TryGetValue(key, out var value)
            ? (T)value!
            : throw new InvalidOperationException($"覚えていない値 {key}");
}

/// <summary>選択の不変条件の行を組み立てる（<see cref="SelectionInvariantRows"/> の 1 行）</summary>
/// <param name="id">行の Id（「{主題}-{守っていること}-{形}」の英小文字とハイフン）</param>
/// <param name="description">何を確かめるか（元のテストの DisplayName）</param>
internal sealed class SelectionRow(string id, string description)
{
    private readonly List<(SelectionPointer, SelectionDetach)> _covers = [];
    private readonly List<SelectionStep> _steps = [];
    private Func<MainViewModel>? _build;

    /// <summary>図を作る</summary>
    public SelectionRow Diagram(Func<MainViewModel> build)
    {
        _build = build;
        return this;
    }

    /// <summary>手順を足す（後に不変条件を当てる）</summary>
    public SelectionRow Step(string label, Action<MainViewModel> apply) =>
        Step(label, context => apply(context.Vm));

    /// <summary>手順を足す（手順をまたいで値を覚える形）</summary>
    public SelectionRow Step(string label, Action<SelectionContext> apply)
    {
        _steps.Add(new SelectionStep(label, apply, []));
        return this;
    }

    /// <summary>直前の手順の後に当てる表明を足す</summary>
    public SelectionRow Expect(string description, Action<MainViewModel> verify) =>
        Expect(description, context => verify(context.Vm));

    /// <summary>直前の手順の後に当てる表明を足す（覚えた値を読む形）</summary>
    public SelectionRow Expect(string description, Action<SelectionContext> verify)
    {
        if (_steps.Count == 0)
        {
            throw new InvalidOperationException($"行 {id}: Expect の前に Step が要る");
        }

        var last = _steps[^1];
        _steps[^1] = last with { Checks = [.. last.Checks, (description, verify)] };
        return this;
    }

    /// <summary>操作 → Undo → Redo → Undo の 4 手順（各段で不変条件を当てる）</summary>
    public SelectionRow RoundTrip(string label, Action<MainViewModel> operation) =>
        Step($"{label}（操作）", operation)
            .Step($"{label}（Undo）", (MainViewModel vm) => vm.UndoCommand.Execute(null))
            .Step($"{label}（Redo）", (MainViewModel vm) => vm.RedoCommand.Execute(null))
            .Step($"{label}（2 回目の Undo）", (MainViewModel vm) => vm.UndoCommand.Execute(null));

    /// <summary>この行が流す（外れ得るもの × 外れ方）の組</summary>
    public SelectionRow Covers(SelectionPointer pointer, params SelectionDetach[] detaches)
    {
        foreach (var detach in detaches)
        {
            _covers.Add((pointer, detach));
        }

        return this;
    }

    /// <summary>表の行にする</summary>
    public ScenarioRow<SelectionScenario> Build()
    {
        if (_build is null || _steps.Count == 0 || _covers.Count == 0)
        {
            throw new InvalidOperationException($"行 {id} に図・手順・Covers のどれかが無い");
        }

        return new ScenarioRow<SelectionScenario>(
            id,
            null,
            description,
            new SelectionScenario(_build, [.. _steps], [.. _covers])
        );
    }
}

/// <summary>選択の不変条件の行を流す</summary>
internal static class SelectionScenarioRunner
{
    /// <summary>手順を順に当て、各手順の後に不変条件（<see cref="SelectionInvariant.Assert"/>）と手順の表明を当てる</summary>
    public static void Run(ScenarioRow<SelectionScenario> row)
    {
        var vm = row.Input.Build();
        var context = new SelectionContext(vm);

        for (var i = 0; i < row.Input.Steps.Count; i++)
        {
            var step = row.Input.Steps[i];
            var where = $"行 {row.Id}（{row.Description}）・手順 {i + 1}「{step.Label}」";

            try
            {
                step.Apply(context);
                SelectionInvariant.Assert(vm, where);

                foreach (var (description, verify) in step.Checks)
                {
                    try
                    {
                        verify(context);
                    }
                    catch (Exception ex)
                        when (ex is not OutOfMemoryException and not XunitException)
                    {
                        throw new XunitException($"{where}: {description}: {ex.Message}", ex);
                    }
                    catch (XunitException ex)
                    {
                        throw new XunitException($"{where}: {description}: {ex.Message}", ex);
                    }
                }
            }
            catch (XunitException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                throw new XunitException($"{where}: 手順が例外を投げた: {ex.Message}", ex);
            }
        }
    }
}
