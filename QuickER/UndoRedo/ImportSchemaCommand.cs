using System.Collections.Generic;
using System.Linq;
using QuickER.Models;
using QuickER.ViewModels;

namespace QuickER.UndoRedo;

/// <summary>取り込んだスキーマでダイアグラム全体を置換する Undo 対応コマンド</summary>
/// <remarks>実行前のエンティティ・リレーションをスナップショットし Undo で復元する</remarks>
public class ImportSchemaCommand : IUndoableCommand
{
    /// <summary>置換対象のメイン ViewModel</summary>
    private readonly MainViewModel _main;

    /// <summary>取り込むエンティティのモデル一覧</summary>
    private readonly IReadOnlyList<Entity> _newEntities;

    /// <summary>取り込むリレーションのモデル一覧</summary>
    private readonly IReadOnlyList<Relationship> _newRelationships;

    /// <summary>履歴表示用の説明</summary>
    private readonly string _description;

    /// <summary>Execute 直前に退避した既存エンティティ（Undo の復元元）</summary>
    private List<EntityViewModel> _previousEntities = new();

    /// <summary>Execute 直前に退避した既存リレーション（Undo の復元元）</summary>
    private List<RelationshipViewModel> _previousRelationships = new();

    /// <summary>取り込み後に表示するエンティティ（テスト・レイアウト用に公開する）</summary>
    public List<EntityViewModel> ImportedEntities { get; } = new();

    /// <summary>取り込み後に表示するリレーション</summary>
    public List<RelationshipViewModel> ImportedRelationships { get; } = new();

    /// <inheritdoc />
    public string Description => _description;

    /// <summary><see cref="ImportSchemaCommand"/> を生成する</summary>
    public ImportSchemaCommand(
        MainViewModel main,
        IReadOnlyList<Entity> entities,
        IReadOnlyList<Relationship> relationships,
        string description = "DB からスキーマ取込"
    )
    {
        _main = main;
        _newEntities = entities;
        _newRelationships = relationships;
        _description = description;
    }

    /// <inheritdoc />
    public void Execute()
    {
        // Undo に備えて現在の表示内容を退避する
        _previousEntities = _main.Entities.ToList();
        _previousRelationships = _main.Relationships.ToList();

        // 退避済みリレーションのイベント購読を解除し参照を切り離す
        foreach (var r in _previousRelationships)
        {
            r.Detach();
        }

        _main.Relationships.Clear();
        _main.Entities.Clear();

        // 取り込み用 ViewModel は初回 Execute 時のみ構築し、Redo では再利用する
        if (ImportedEntities.Count == 0)
        {
            // リレーション接続のためエンティティ ID から ViewModel を引けるよう索引化する
            var byId = new Dictionary<System.Guid, EntityViewModel>();

            foreach (var e in _newEntities)
            {
                var vm = new EntityViewModel(e);
                ImportedEntities.Add(vm);
                byId[e.Id] = vm;
            }

            // 両端のエンティティが揃うリレーションのみ復元する
            foreach (var r in _newRelationships)
            {
                if (
                    byId.TryGetValue(r.SourceEntityId, out var s)
                    && byId.TryGetValue(r.TargetEntityId, out var t)
                )
                {
                    ImportedRelationships.Add(new RelationshipViewModel(r, s, t));
                }
            }
        }

        foreach (var e in ImportedEntities)
        {
            _main.Entities.Add(e);
        }

        foreach (var r in ImportedRelationships)
        {
            _main.Relationships.Add(r);
        }

        _main.SelectedEntity = null;
        _main.SelectedRelationship = null;
        _main.SelectedColumn = null;
    }

    /// <inheritdoc />
    public void Undo()
    {
        // 取り込んだリレーションのイベント購読を解除してから差し替える
        foreach (var r in ImportedRelationships)
        {
            r.Detach();
        }

        _main.Relationships.Clear();
        _main.Entities.Clear();

        foreach (var e in _previousEntities)
        {
            _main.Entities.Add(e);
        }

        foreach (var r in _previousRelationships)
        {
            _main.Relationships.Add(r);
        }

        _main.SelectedEntity = null;
        _main.SelectedRelationship = null;
        _main.SelectedColumn = null;
    }
}
