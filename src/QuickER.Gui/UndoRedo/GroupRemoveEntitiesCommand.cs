using System.Collections.Generic;
using System.Linq;
using QuickER.ViewModels;

namespace QuickER.UndoRedo;

/// <summary>複数エンティティ（と接続リレーション）の一括削除を単一の Undo 単位として扱う複合コマンド</summary>
/// <remarks>
/// 単一削除（<see cref="RemoveEntityCommand"/>）と同じく、削除対象を端点に持つリレーションを併せて退避・復元する。
/// 1 回の Undo で全エンティティと巻き添えリレーションが同時に復元される
/// （重複するリレーション＝複数の対象エンティティを結ぶ線を二重に退避しないよう集合で管理する）。
/// </remarks>
public sealed class GroupRemoveEntitiesCommand : IUndoableCommand
{
    /// <summary>削除を行うメイン ViewModel</summary>
    private readonly MainViewModel _main;

    /// <summary>削除対象のエンティティ群</summary>
    private readonly IReadOnlyList<EntityViewModel> _entities;

    /// <summary>巻き添えで削除したリレーション（Undo の復元元）</summary>
    private List<RelationshipViewModel> _removedRelationships = new();

    /// <summary><see cref="GroupRemoveEntitiesCommand"/> を生成する</summary>
    /// <param name="main">削除を行うメイン ViewModel</param>
    /// <param name="entities">削除対象のエンティティ群</param>
    public GroupRemoveEntitiesCommand(MainViewModel main, IReadOnlyList<EntityViewModel> entities)
    {
        _main = main;
        _entities = entities;
    }

    /// <inheritdoc />
    public string Description => $"エンティティ一括削除: {_entities.Count} 個";

    /// <inheritdoc />
    public void Execute()
    {
        var targets = _entities.ToHashSet();

        // 削除対象エンティティを端点に持つリレーションも併せて除去する（孤立した線を残さない）。
        // 対象同士を結ぶ線は両端が対象のため一度だけ退避すればよい（集合判定で重複を避ける）。
        _removedRelationships = _main
            .Relationships.Where(r => targets.Contains(r.Source) || targets.Contains(r.Target))
            .ToList();

        foreach (var r in _removedRelationships)
        {
            _main.Relationships.Remove(r);
        }

        foreach (var entity in _entities)
        {
            _main.Entities.Remove(entity);
        }
    }

    /// <inheritdoc />
    public void Undo()
    {
        foreach (var entity in _entities)
        {
            _main.Entities.Add(entity);
        }

        foreach (var r in _removedRelationships)
        {
            _main.Relationships.Add(r);
        }

        // 復元メンバーは IsSelected を保持したまま戻るため、主選択も復元して
        // パネル表示（一括操作カード・削除ボタンの実行可否）との整合を取る
        _main.SelectedEntity = _entities.FirstOrDefault();
    }
}
