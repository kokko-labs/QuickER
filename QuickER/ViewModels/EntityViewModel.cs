using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using QuickER.Models;
using QuickER.Services;

namespace QuickER.ViewModels;

/// <summary>エンティティの表示・ドラッグ・選択をビューへ仲介する ViewModel</summary>
public partial class EntityViewModel : ObservableObject
{
    /// <summary>モデルと同一の識別子</summary>
    public Guid Id { get; }

    /// <summary>テーブル名</summary>
    [ObservableProperty]
    private string _tableName;

    /// <summary>キャンバス上の X 座標 (px)（ドラッグで更新する）</summary>
    [ObservableProperty]
    private double _x;

    /// <summary>キャンバス上の Y 座標 (px)（ドラッグで更新する）</summary>
    [ObservableProperty]
    private double _y;

    /// <summary>カードの横幅 (px)</summary>
    [ObservableProperty]
    private double _width;

    /// <summary>ER 図上で説明表示を行うかどうかの実体フィールド</summary>
    private bool _showDescriptionsInDiagram;

    /// <summary>ER 図上で NULL 許容表示を行うかどうかの実体フィールド</summary>
    private bool _showNullabilityInDiagram;

    /// <summary>メモ（プロパティパネルで編集する）</summary>
    [ObservableProperty]
    private string _memo;

    /// <summary>テーブルの説明（SQL Server の拡張プロパティ <c>MS_Description</c> と同期する）</summary>
    [ObservableProperty]
    private string _description;

    /// <summary>タイトル帯背景色の実体フィールド</summary>
    private string _titleBackgroundColor = Entity.DefaultTitleBackgroundColor;

    /// <summary>選択中かどうか（枠線スタイルの切り替えに用いる）</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>このエンティティに含まれるカラム一覧</summary>
    public ObservableCollection<ColumnViewModel> Columns { get; }

    /// <summary>ダイアグラム上の見出し帯に表示する背景色（設定時に正規化する）</summary>
    public string TitleBackgroundColor
    {
        get => _titleBackgroundColor;
        set => SetProperty(ref _titleBackgroundColor, EntityTitleColorPalette.Normalize(value));
    }

    /// <summary>ER 図上で説明表示を行うかどうか（変更時は表示高さキャッシュを無効化する）</summary>
    public bool ShowDescriptionsInDiagram
    {
        get => _showDescriptionsInDiagram;
        set
        {
            if (SetProperty(ref _showDescriptionsInDiagram, value))
            {
                InvalidateDisplayHeight();
            }
        }
    }

    /// <summary>ER 図上で NULL 許容表示を行うかどうか（変更時は表示高さキャッシュを無効化する）</summary>
    public bool ShowNullabilityInDiagram
    {
        get => _showNullabilityInDiagram;
        set
        {
            if (SetProperty(ref _showNullabilityInDiagram, value))
            {
                InvalidateDisplayHeight();
            }
        }
    }

    /// <summary>表示高さ見積もりのキャッシュ（NaN は未計算を表す）</summary>
    private double _displayHeightCache = double.NaN;

    /// <summary>現在の表示状態に応じたエンティティの表示高さ</summary>
    /// <remarks>
    /// 計測（<see cref="System.Windows.Media.FormattedText"/> 生成）が高コストのため結果をキャッシュし、
    /// 高さに影響するプロパティの変更時のみ <see cref="InvalidateDisplayHeight"/> で再計算する
    /// ドラッグ・整列時の大量再評価による性能劣化を防ぐ
    /// </remarks>
    public double DisplayHeight
    {
        get
        {
            if (double.IsNaN(_displayHeightCache))
            {
                _displayHeightCache = DiagramMetricsService.EstimateEntityHeight(
                    this,
                    ShowDescriptionsInDiagram
                );
            }

            return _displayHeightCache;
        }
    }

    /// <summary>表示高さキャッシュを破棄し、<see cref="DisplayHeight"/> の変更を通知する</summary>
    private void InvalidateDisplayHeight()
    {
        _displayHeightCache = double.NaN;
        OnPropertyChanged(nameof(DisplayHeight));
    }

    /// <summary>モデルから ViewModel を生成し、カラムの変更購読を設定する</summary>
    public EntityViewModel(Entity model)
    {
        Id = model.Id;
        _tableName = model.TableName;
        _x = model.X;
        _y = model.Y;
        _width = model.Width <= 0 ? 200 : model.Width;
        _memo = model.Memo;
        _description = model.Description ?? string.Empty;
        _titleBackgroundColor = EntityTitleColorPalette.Normalize(model.TitleBackgroundColor);
        Columns = new ObservableCollection<ColumnViewModel>(
            model.Columns.Select(c => new ColumnViewModel(c))
        );

        Columns.CollectionChanged += OnColumnsChanged;

        foreach (var column in Columns)
        {
            column.PropertyChanged += OnColumnPropertyChanged;
        }
    }

    /// <summary>内容に合わせてエンティティ幅を自動調整する</summary>
    public void AutoFitWidth()
    {
        Width = DiagramMetricsService.CalculateAutoWidth(this);
    }

    /// <summary>幅変更時に表示高さキャッシュを無効化する（折り返しで高さが変わるため）</summary>
    partial void OnWidthChanged(double value) => InvalidateDisplayHeight();

    /// <summary>説明変更時に表示高さキャッシュを無効化する</summary>
    partial void OnDescriptionChanged(string value) => InvalidateDisplayHeight();

    /// <summary>カラムの増減に追従し、購読の着脱と表示高さキャッシュの無効化を行う</summary>
    private void OnColumnsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (ColumnViewModel column in e.OldItems)
            {
                column.PropertyChanged -= OnColumnPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (ColumnViewModel column in e.NewItems)
            {
                column.PropertyChanged += OnColumnPropertyChanged;
            }
        }

        InvalidateDisplayHeight();
    }

    /// <summary>カラムの説明変更時に表示高さキャッシュを無効化する</summary>
    private void OnColumnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ColumnViewModel.Description))
        {
            InvalidateDisplayHeight();
        }
    }

    /// <summary>現在の状態をモデルへコピーして返す</summary>
    public Entity ToModel() =>
        new()
        {
            Id = Id,
            TableName = TableName,
            X = X,
            Y = Y,
            Width = Width,
            Memo = Memo,
            Description = Description ?? string.Empty,
            TitleBackgroundColor = TitleBackgroundColor,
            Columns = Columns.Select(c => c.ToModel()).ToList(),
        };
}
