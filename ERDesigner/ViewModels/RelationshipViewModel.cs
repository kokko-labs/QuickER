using System;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ERDesigner.Models;

namespace ERDesigner.ViewModels;

/// <summary>
/// 2 つのエンティティを接続するリレーションの ViewModel です。
/// エンティティの位置変更を購読し、線の端点を自動追従させます。
/// </summary>
public partial class RelationshipViewModel : ObservableObject
{
    /// <summary>モデルと同じ ID。</summary>
    public Guid Id { get; }

    /// <summary>関連の種類。</summary>
    [ObservableProperty] private RelationshipType _type;
    /// <summary>選択中かどうか。</summary>
    [ObservableProperty] private bool _isSelected;

    /// <summary>関連の起点となるエンティティ。</summary>
    public EntityViewModel Source { get; }
    /// <summary>関連の終点となるエンティティ。</summary>
    public EntityViewModel Target { get; }

    /// <summary>モデルと両端のエンティティから ViewModel を生成します。</summary>
    public RelationshipViewModel(Relationship model, EntityViewModel source, EntityViewModel target)
    {
        Id = model.Id;
        _type = model.Type;
        Source = source;
        Target = target;

        // 両端のエンティティが動いたら X1/Y1/X2/Y2 を再計算させるため購読。
        Source.PropertyChanged += OnEndpointChanged;
        Target.PropertyChanged += OnEndpointChanged;
    }

    /// <summary>両端の位置・幅が変わったら端点プロパティを再通知します。</summary>
    private void OnEndpointChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EntityViewModel.X) or nameof(EntityViewModel.Y) or nameof(EntityViewModel.Width))
        {
            OnPropertyChanged(nameof(X1));
            OnPropertyChanged(nameof(Y1));
            OnPropertyChanged(nameof(X2));
            OnPropertyChanged(nameof(Y2));
        }
    }

    /// <summary>端点計算用のエンティティ高さ（近似値）。</summary>
    private const double EntityHeightApprox = 60;

    /// <summary>起点エンティティの中央 X 座標。</summary>
    public double X1 => Source.X + Source.Width / 2;
    /// <summary>起点エンティティのヘッダ中心 Y 座標。</summary>
    public double Y1 => Source.Y + EntityHeightApprox / 2;
    /// <summary>終点エンティティの中央 X 座標。</summary>
    public double X2 => Target.X + Target.Width / 2;
    /// <summary>終点エンティティのヘッダ中心 Y 座標。</summary>
    public double Y2 => Target.Y + EntityHeightApprox / 2;

    /// <summary>線上に表示するラベルテキスト。</summary>
    public string Label => Type switch
    {
        RelationshipType.OneToOne => "1―1",
        RelationshipType.OneToMany => "1―N",
        RelationshipType.ManyToMany => "N―N",
        _ => string.Empty
    };

    /// <summary>種別が変わったら <see cref="Label"/> も変更通知します。</summary>
    partial void OnTypeChanged(RelationshipType value) => OnPropertyChanged(nameof(Label));

    /// <summary>現在の状態をモデルにコピーして返します。</summary>
    public Relationship ToModel() => new()
    {
        Id = Id,
        SourceEntityId = Source.Id,
        TargetEntityId = Target.Id,
        Type = Type
    };

    /// <summary>両端の PropertyChanged 購読を解除します（画面リセット時など）。</summary>
    public void Detach()
    {
        Source.PropertyChanged -= OnEndpointChanged;
        Target.PropertyChanged -= OnEndpointChanged;
    }
}
