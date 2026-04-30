using System;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ERDesigner.Models;

namespace ERDesigner.ViewModels;

/// <summary>
/// 2 つのエンティティを接続するリレーションの ViewModel です。
/// エンティティの位置変更を購読し、線の端点を自動追従させます。
/// </summary>
/// <remarks>
/// 線本体（X1,Y1）-(X2,Y2) と、両端のマーカーを描くための補助プロパティ
/// (<see cref="SourceMarker"/> / <see cref="TargetMarker"/>) を提供します。
/// マーカーの位置は <see cref="MarkerOffset"/> 分だけ端点から内側にずらした座標です。
/// </remarks>
public partial class RelationshipViewModel : ObservableObject
{
    /// <summary>マーカーが端点から内側に置かれる距離 (px)。</summary>
    private const double MarkerOffset = 14;

    /// <summary>端点計算用のエンティティ高さ（近似値）。</summary>
    private const double EntityHeightApprox = 60;

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

        Source.PropertyChanged += OnEndpointChanged;
        Target.PropertyChanged += OnEndpointChanged;
    }

    /// <summary>両端の位置・幅が変わったら端点プロパティを再通知します。</summary>
    private void OnEndpointChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EntityViewModel.X) or nameof(EntityViewModel.Y) or nameof(EntityViewModel.Width))
        {
            NotifyGeometryChanged();
        }
    }

    /// <summary>幾何プロパティをまとめて再通知します。</summary>
    private void NotifyGeometryChanged()
    {
        OnPropertyChanged(nameof(X1));
        OnPropertyChanged(nameof(Y1));
        OnPropertyChanged(nameof(X2));
        OnPropertyChanged(nameof(Y2));
        OnPropertyChanged(nameof(SourceMarkerX));
        OnPropertyChanged(nameof(SourceMarkerY));
        OnPropertyChanged(nameof(TargetMarkerX));
        OnPropertyChanged(nameof(TargetMarkerY));
        OnPropertyChanged(nameof(SourceMarkerAngle));
        OnPropertyChanged(nameof(TargetMarkerAngle));
        OnPropertyChanged(nameof(LabelX));
        OnPropertyChanged(nameof(LabelY));
    }

    /// <summary>種別が変わったらラベルとマーカー種別を再通知します。</summary>
    partial void OnTypeChanged(RelationshipType value)
    {
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(SourceMarker));
        OnPropertyChanged(nameof(TargetMarker));
    }

    // ===== 端点座標 =====

    /// <summary>起点エンティティの中央 X 座標。</summary>
    public double X1 => Source.X + Source.Width / 2;
    /// <summary>起点エンティティのヘッダ中心 Y 座標。</summary>
    public double Y1 => Source.Y + EntityHeightApprox / 2;
    /// <summary>終点エンティティの中央 X 座標。</summary>
    public double X2 => Target.X + Target.Width / 2;
    /// <summary>終点エンティティのヘッダ中心 Y 座標。</summary>
    public double Y2 => Target.Y + EntityHeightApprox / 2;

    // ===== マーカー位置（端点から内側に MarkerOffset 移動した点） =====

    private (double dx, double dy, double len) Direction()
    {
        var dx = X2 - X1;
        var dy = Y2 - Y1;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 0.0001) len = 1;
        return (dx / len, dy / len, len);
    }

    /// <summary>起点側マーカーの中心 X。</summary>
    public double SourceMarkerX
    {
        get { var (ux, _, _) = Direction(); return X1 + ux * MarkerOffset; }
    }
    /// <summary>起点側マーカーの中心 Y。</summary>
    public double SourceMarkerY
    {
        get { var (_, uy, _) = Direction(); return Y1 + uy * MarkerOffset; }
    }
    /// <summary>終点側マーカーの中心 X。</summary>
    public double TargetMarkerX
    {
        get { var (ux, _, _) = Direction(); return X2 - ux * MarkerOffset; }
    }
    /// <summary>終点側マーカーの中心 Y。</summary>
    public double TargetMarkerY
    {
        get { var (_, uy, _) = Direction(); return Y2 - uy * MarkerOffset; }
    }

    /// <summary>起点マーカーを線方向に合わせて回転させる角度（度）。</summary>
    public double SourceMarkerAngle
    {
        get { var (ux, uy, _) = Direction(); return Math.Atan2(uy, ux) * 180.0 / Math.PI; }
    }
    /// <summary>終点マーカーを線方向に合わせて回転させる角度（度・反転）。</summary>
    public double TargetMarkerAngle => SourceMarkerAngle + 180;

    /// <summary>線の中点 X（ラベル表示用）。</summary>
    public double LabelX => (X1 + X2) / 2;
    /// <summary>線の中点 Y（ラベル表示用）。</summary>
    public double LabelY => (Y1 + Y2) / 2;

    // ===== 種別ごとのマーカー種類 =====

    /// <summary>
    /// 端点マーカーの種類。
    /// </summary>
    public enum MarkerKind
    {
        /// <summary>「1」を表す（短い縦棒）。</summary>
        One,
        /// <summary>「多」を表す（鳥の足: crow's foot）。</summary>
        Many
    }

    /// <summary>起点側マーカーの種類。</summary>
    public MarkerKind SourceMarker => Type switch
    {
        RelationshipType.OneToOne => MarkerKind.One,
        RelationshipType.OneToMany => MarkerKind.One,
        RelationshipType.ManyToMany => MarkerKind.Many,
        _ => MarkerKind.One
    };

    /// <summary>終点側マーカーの種類。</summary>
    public MarkerKind TargetMarker => Type switch
    {
        RelationshipType.OneToOne => MarkerKind.One,
        RelationshipType.OneToMany => MarkerKind.Many,
        RelationshipType.ManyToMany => MarkerKind.Many,
        _ => MarkerKind.One
    };

    /// <summary>線上に表示するラベルテキスト。</summary>
    public string Label => Type switch
    {
        RelationshipType.OneToOne => "1―1",
        RelationshipType.OneToMany => "1―N",
        RelationshipType.ManyToMany => "N―N",
        _ => string.Empty
    };

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
