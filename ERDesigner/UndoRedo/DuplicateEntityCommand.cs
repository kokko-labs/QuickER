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
            // モデル経由でディープコピー（ID は新しく振り直す）
            var srcModel = _original.ToModel();
            var newModel = new Entity
            {
                TableName = srcModel.TableName + "_Copy",
                X = srcModel.X + 30,
                Y = srcModel.Y + 30,
                Width = srcModel.Width,
                Memo = srcModel.Memo,
                Description = srcModel.Description
            };
            foreach (var c in srcModel.Columns)
            {
                newModel.Columns.Add(new Column
                {
                    Name = c.Name,
                    DataType = c.DataType,
                    IsPrimaryKey = c.IsPrimaryKey,
                    IsForeignKey = c.IsForeignKey,
                    Description = c.Description
                });
            }
            _duplicate = new EntityViewModel(newModel);
        }
        _main.Entities.Add(_duplicate);
    }

    /// <inheritdoc />
    public void Undo()
    {
        if (_duplicate is null) return;
        _main.Entities.Remove(_duplicate);
    }
}
