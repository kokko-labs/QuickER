using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>文書の状態機械の決まった形のシナリオ（手の列）</summary>
/// <param name="Steps">手の列（最初の VM は空の図・無題・永続化先は空）</param>
internal sealed record DocScenario(IReadOnlyList<DocStep> Steps);

/// <summary>
/// 文書の状態機械の決まった形のシナリオの表（元の単発のテストの入力と期待をそのまま持つ）
/// </summary>
/// <remarks>
/// 行ごとに、手の列を VM と参照モデルで流して毎手で突き合わせ、不変条件を当て、元のテストの表明（<see cref="Doc.Expect"/>）を当てる。
/// 主題ごとの行は <c>DocumentFileScenarios.{主題}.cs</c> の private メソッドが返し、<see cref="AllRows"/> に足す。
/// 静的なデータは式形式のプロパティにする（partial の初期化順で null にならないよう）。
/// </remarks>
internal static partial class DocumentFileScenarios
{
    /// <summary>表</summary>
    public static ScenarioTable<DocScenario> Table => new(EngineIds.DocumentFile, AllRows());

    private static IEnumerable<ScenarioRow<DocScenario>> AllRows() =>
        [
            .. ExternalChangeRows(),
            .. OverwriteGuardRows(),
            .. NewerFormatRows(),
            .. HashRaceRows(),
            .. AutoSaveMetaRows(),
            .. OpenFailureRows(),
            .. DocumentRows(),
            .. RobustnessRows(),
            .. DiscardConfirmRows(),
            .. UntrackedChangeRows(),
            .. DragCancelRows(),
            .. QueriesRows(),
            .. AccessRows(),
            .. LayoutRows(),
        ];

    /// <summary>行を作る</summary>
    private static ScenarioRow<DocScenario> Row(
        string id,
        string description,
        params DocStep[] steps
    ) => new(id, null, description, new DocScenario(steps));
}
