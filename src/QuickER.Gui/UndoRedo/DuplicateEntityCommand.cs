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
    }

    /// <inheritdoc />
    public void Undo()
    {
        if (_duplicate is null)
        {
            return;
        }

        _main.Entities.Remove(_duplicate);
    }
}
