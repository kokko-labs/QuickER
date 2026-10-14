using System.Collections.Generic;
using System.Linq;
using QuickER.Model;
using QuickER.ViewModels;

namespace QuickER.UndoRedo;

/// <summary>
/// エンティティ（1 個以上）と、それを端点に持つリレーション・そのエンティティの名前付きクエリをまとめて削除し、
/// Undo で元の位置へ戻すコマンドの共通部分
/// </summary>
/// <remarks>
/// <para>
/// 単独の削除（<see cref="RemoveEntityCommand"/>）と複数の削除（<see cref="GroupRemoveEntitiesCommand"/>）は
/// 説明文だけが違う。退避・復元・主選択の戻し方をここに 1 つだけ置く（2 つに写すと、片方だけ直した差が
/// 生まれる）。
/// </para>
/// <para>
/// Undo で戻したエンティティは <see cref="EntityViewModel.IsSelected"/> を保ったまま戻るため、削除の直前の
/// 主選択（<see cref="MainViewModel.SelectedEntity"/>）が削除したエンティティの 1 つなら、それを主選択へ戻し、
/// パネルの表示と削除ボタンの実行可否を選択の表示とそろえる。主選択が削除の対象の外（AI ツールが選択外の
/// テーブルを消したときなど）なら主選択を変えない。
/// </para>
/// </remarks>
public abstract class EntityRemovalCommandBase : IUndoableCommand
{
    /// <summary>削除を行うメイン ViewModel</summary>
    private readonly MainViewModel _main;

    /// <summary>削除対象のエンティティ群</summary>
    private readonly IReadOnlyList<EntityViewModel> _entities;

    /// <summary>削除したエンティティと元の位置（Undo の復元元）</summary>
    private List<(int Index, EntityViewModel Item)> _removedEntities = new();

    /// <summary>巻き添えで削除したリレーションと元の位置（Undo の復元元）</summary>
    private List<(int Index, RelationshipViewModel Item)> _removedRelationships = new();

    /// <summary>巻き添えで削除した名前付きクエリと元の位置（Undo の復元元）</summary>
    private List<(int Index, QueryDefinition Item)> _removedQueries = new();

    /// <summary>削除の直前の主選択が削除の対象だったとき、その主選択（Undo で戻す）</summary>
    private EntityViewModel? _removedPrimarySelection;

    /// <summary>削除対象を受け取って生成する</summary>
    /// <param name="main">削除を行うメイン ViewModel</param>
    /// <param name="entities">削除対象のエンティティ群</param>
    protected EntityRemovalCommandBase(MainViewModel main, IReadOnlyList<EntityViewModel> entities)
    {
        _main = main;
        _entities = entities;
    }

    /// <inheritdoc />
    public abstract string Description { get; }

    /// <summary>エンティティ・接続リレーション・名前付きクエリを、元の位置を記録しながら取り除く</summary>
    public void Execute()
    {
        var targets = _entities.ToHashSet(ReferenceEqualityComparer.Instance);
        _removedPrimarySelection =
            _main.SelectedEntity is { } primary && targets.Contains(primary) ? primary : null;

        // 削除対象を端点に持つリレーションも併せて除去する（孤立した線を残さない）。
        // 対象同士を結ぶ線は両端が対象のため一度だけ退避すればよい（集合判定で重複を避ける）。
        // どちらも元の位置を記録し、Undo で同じ位置へ戻す（末尾へ戻すと図の順序が変わる）
        _removedRelationships = IndexedRemoval.RemoveWhere(
            _main.Relationships,
            r => targets.Contains(r.Source) || targets.Contains(r.Target)
        );
        _removedEntities = IndexedRemoval.RemoveWhere(_main.Entities, targets.Contains);

        // 削除対象の名前付きクエリも取り除く（残すとどこからも触れない孤児になる）
        _removedQueries = EntityQueryRemoval.Remove(
            _main,
            _entities.Select(entity => entity.Id).ToHashSet()
        );
    }

    /// <summary>取り除いたものを元の位置へ戻し、主選択を選択の表示とそろえる</summary>
    public void Undo()
    {
        // リレーションは端点のエンティティが戻ってから戻す
        IndexedRemoval.Restore(_main.Entities, _removedEntities);
        IndexedRemoval.Restore(_main.Relationships, _removedRelationships);
        EntityQueryRemoval.Restore(_main, _removedQueries);

        if (_removedPrimarySelection is not null)
        {
            _main.SelectedEntity = _removedPrimarySelection;
        }
    }
}
