using System.Collections.Generic;
using QuickER.Model;
using QuickER.Resources;
using QuickER.ViewModels;

namespace QuickER.UndoRedo;

/// <summary>既存エンティティを複製する Undo / Redo 対応コマンド</summary>
/// <remarks>
/// 名前の末尾に "_Copy" を付与し位置を右下にずらした新しい <see cref="EntityViewModel"/> を
/// <see cref="MainViewModel.Entities"/> に追加する Undo で追加した複製を削除する
/// </remarks>
public class DuplicateEntityCommand : IUndoableCommand
{
    /// <summary>追加先のメイン ViewModel</summary>
    private readonly MainViewModel _main;

    /// <summary>複製元エンティティ</summary>
    private readonly EntityViewModel _original;

    /// <summary>生成済みの複製エンティティ（Redo で再利用するため保持する）</summary>
    private EntityViewModel? _duplicate;

    /// <summary>Undo で一緒に外した、このエンティティの名前付きクエリと元の位置（Redo の復元元）</summary>
    /// <remarks>
    /// クエリの定義は履歴の外なので、追加の後に定義したクエリがあり得る。エンティティだけを外すと、
    /// どこからも触れない孤児が残る（<see cref="EntityQueryRemoval"/>）
    /// </remarks>
    private List<(int Index, QueryDefinition Item)> _removedQueries = new();

    /// <summary><see cref="DuplicateEntityCommand"/> を生成する</summary>
    /// <param name="main">追加先のメイン ViewModel</param>
    /// <param name="original">複製元エンティティ</param>
    public DuplicateEntityCommand(MainViewModel main, EntityViewModel original)
    {
        _main = main;
        _original = original;
    }

    /// <summary>複製で生成したエンティティ（Execute 後に有効）</summary>
    public EntityViewModel? Duplicated => _duplicate;

    /// <inheritdoc />
    public string Description => string.Format(Strings.Undo_DuplicateEntity, _original.TableName);

    /// <inheritdoc />
    public void Execute()
    {
        // 複製は初回 Execute 時のみ生成し、Redo では同一インスタンスを再利用する
        if (_duplicate is null)
        {
            _duplicate = _main.CreateEntityCopy(_original);
        }

        _main.Entities.Add(_duplicate);

        // Redo では、Undo で一緒に外したクエリを元の位置へ戻す
        EntityQueryRemoval.Restore(_main, _removedQueries);
        _removedQueries = new();
    }

    /// <inheritdoc />
    public void Undo()
    {
        if (_duplicate is null)
        {
            return;
        }

        _main.Entities.Remove(_duplicate);
        _removedQueries = EntityQueryRemoval.Remove(_main, new HashSet<Guid> { _duplicate.Id });
    }
}
