using System.Collections.Generic;
using System.Linq;
using ERDesigner.Models;
using ERDesigner.ViewModels;

namespace ERDesigner.UndoRedo;

/// <summary>
/// SQL Server からインポートしたスキーマでダイアグラムを置換する Undo 対応コマンド。
/// 実行前のエンティティ／リレーションをスナップショットし、Undo で復元します。
/// </summary>
public class ImportSchemaCommand : IUndoableCommand
{
    private readonly MainViewModel _main;
    private readonly IReadOnlyList<Entity> _newEntities;
    private readonly IReadOnlyList<Relationship> _newRelationships;

    private List<EntityViewModel> _previousEntities = new();
    private List<RelationshipViewModel> _previousRelationships = new();

    /// <summary>取り込んだ後に画面上に出ているエンティティ。テスト/レイアウト用に公開。</summary>
    public List<EntityViewModel> ImportedEntities { get; } = new();

    /// <summary>取り込んだ後のリレーション。</summary>
    public List<RelationshipViewModel> ImportedRelationships { get; } = new();

    /// <inheritdoc />
    public string Description => "DB からスキーマ取込";

    /// <summary>新しいインスタンスを生成します。</summary>
    public ImportSchemaCommand(MainViewModel main, IReadOnlyList<Entity> entities, IReadOnlyList<Relationship> relationships)
    {
        _main = main;
        _newEntities = entities;
        _newRelationships = relationships;
    }

    /// <inheritdoc />
    public void Execute()
    {
        // 既存をスナップショット
        _previousEntities = _main.Entities.ToList();
        _previousRelationships = _main.Relationships.ToList();

        foreach (var r in _previousRelationships)
        {
            r.Detach();
        }

        _main.Relationships.Clear();
        _main.Entities.Clear();

        // インポート分を構築 (初回のみ)
        if (ImportedEntities.Count == 0)
        {
            var byId = new Dictionary<System.Guid, EntityViewModel>();

            foreach (var e in _newEntities)
            {
                var vm = new EntityViewModel(e);
                ImportedEntities.Add(vm);
                byId[e.Id] = vm;
            }

            foreach (var r in _newRelationships)
            {
                if (byId.TryGetValue(r.SourceEntityId, out var s) && byId.TryGetValue(r.TargetEntityId, out var t))
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
    }

    /// <inheritdoc />
    public void Undo()
    {
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
    }
}
