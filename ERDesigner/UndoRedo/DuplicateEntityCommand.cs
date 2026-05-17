using ERDesigner.Models;
using ERDesigner.ViewModels;

namespace ERDesigner.UndoRedo;

/// <summary>
/// 既存エンティティを複製するコマンドです（Undo / Redo 可能）。
/// </summary>
/// <remarks>
/// 名前の末尾に "_Copy" を付け、位置を少し右下にずらした新しい <see cref="EntityViewModel"/>
/// を <see cref="MainViewModel.Entities"/> に追加します。Undo すると追加された複製が削除されます。
/// </remarks>
public class DuplicateEntityCommand : IUndoableCommand
{
    private readonly MainViewModel _main;
    private readonly EntityViewModel _original;
    private EntityViewModel? _duplicate;

    /// <summary>新しい <see cref="DuplicateEntityCommand"/> を生成します。</summary>
    /// <param name="main">追加先の <see cref="MainViewModel"/>。</param>
    /// <param name="original">複製元のエンティティ。</param>
    public DuplicateEntityCommand(MainViewModel main, EntityViewModel original)
    {
        _main = main;
        _original = original;
    }

    /// <summary>複製後にできた新しいエンティティ（Execute 後に有効）。</summary>
    public EntityViewModel? Duplicated => _duplicate;

    /// <inheritdoc />
    public string Description => $"複製: {_original.TableName}";

    /// <inheritdoc />
    public void Execute()
    {
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
