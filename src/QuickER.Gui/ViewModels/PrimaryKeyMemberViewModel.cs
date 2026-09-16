using CommunityToolkit.Mvvm.ComponentModel;

namespace QuickER.ViewModels;

/// <summary>主キー構成列 1 つを表す並び替え行（読み取り専用の列名＋上下移動ボタン 1 行分）</summary>
/// <remarks>
/// 正本はエンティティ側の膜（<see cref="ColumnViewModel.IsPrimaryKey"/>）と順序
/// （<see cref="EntityViewModel.PrimaryKeyColumnIds"/>）で、この行は導出表示にすぎない。
/// 列そのものを選び直す UI は持たない（主キーの入り切りはカラム一覧の PK チェックが担う）。
/// </remarks>
public partial class PrimaryKeyMemberViewModel : ObservableObject
{
    /// <summary>この行が属するエンティティ</summary>
    /// <remarks>
    /// 移動コマンドが行から所有エンティティを辿るために持つ
    /// （<c>SelectedEntity</c> への暗黙依存を作らず、行だけで適用先が決まるようにする）
    /// </remarks>
    public EntityViewModel Owner { get; }

    /// <summary>この行が指す主キー構成列</summary>
    [ObservableProperty]
    private ColumnViewModel _column;

    /// <summary>1 つ上へ移動できるかどうか（先頭行は不可）</summary>
    [ObservableProperty]
    private bool _canMoveUp;

    /// <summary>1 つ下へ移動できるかどうか（末尾行は不可）</summary>
    [ObservableProperty]
    private bool _canMoveDown;

    /// <summary><see cref="PrimaryKeyMemberViewModel"/> を生成する</summary>
    /// <param name="owner">この行が属するエンティティ</param>
    /// <param name="column">この行が指す主キー構成列</param>
    public PrimaryKeyMemberViewModel(EntityViewModel owner, ColumnViewModel column)
    {
        Owner = owner;
        _column = column;
    }

    /// <summary>正本の実効順をこの行へ反映する（行インスタンスの使い回しに用いる）</summary>
    internal void ApplyColumn(ColumnViewModel column) => Column = column;
}
