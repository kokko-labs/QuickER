using System.Collections.Generic;
using QuickER.ViewModels;

namespace QuickER.UndoRedo;

/// <summary>複数エンティティのタイトル背景色の一括変更を単一の Undo 単位として扱う複合コマンド</summary>
/// <remarks>
/// <see cref="EntityViewModel.TitleBackgroundColor"/> は <see cref="DiagramChangeTracker"/> が
/// PropertyChanged から個別に自動記録するため、適用は <see cref="DiagramChangeTracker.RunWithoutTracking"/>
/// の内側で行い、履歴には本複合コマンドのみを Push する（そうしないと履歴が N 件へ分裂し二重登録になる）。
/// 1 回の Undo で全メンバーが元の色へ同時に戻る。
/// </remarks>
public sealed class GroupChangeTitleColorCommand : IUndoableCommand
{
    /// <summary>色変更を追跡なしで適用するためのラッパー（MainViewModel の RunWithoutTracking フック）</summary>
    private readonly System.Action<System.Action> _runWithoutTracking;

    /// <summary>変更対象メンバーと新旧の色（適用・取消しに用いる）</summary>
    private readonly IReadOnlyList<(
        EntityViewModel Entity,
        string OldColor,
        string NewColor
    )> _changes;

    /// <summary><see cref="GroupChangeTitleColorCommand"/> を生成する</summary>
    /// <param name="changes">各メンバーの新旧の色</param>
    /// <param name="runWithoutTracking">追跡抑止下で処理を実行するラッパー</param>
    public GroupChangeTitleColorCommand(
        IReadOnlyList<(EntityViewModel Entity, string OldColor, string NewColor)> changes,
        System.Action<System.Action> runWithoutTracking
    )
    {
        _changes = changes;
        _runWithoutTracking = runWithoutTracking;
    }

    /// <inheritdoc />
    public string Description => $"タイトル色一括変更: {_changes.Count} 個";

    /// <inheritdoc />
    public void Execute()
    {
        // 色プロパティは自動追跡対象のため、追跡を止めて適用し履歴の二重登録を防ぐ
        _runWithoutTracking(() =>
        {
            foreach (var change in _changes)
            {
                change.Entity.TitleBackgroundColor = change.NewColor;
            }
        });
    }

    /// <inheritdoc />
    public void Undo()
    {
        _runWithoutTracking(() =>
        {
            foreach (var change in _changes)
            {
                change.Entity.TitleBackgroundColor = change.OldColor;
            }
        });
    }
}
