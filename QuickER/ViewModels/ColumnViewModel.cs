using CommunityToolkit.Mvvm.ComponentModel;
using QuickER.Models;

namespace QuickER.ViewModels;

/// <summary>1 カラムをビューへバインドするための ViewModel</summary>
/// <remarks><see cref="INotifyPropertyChanged"/> は CommunityToolkit.Mvvm のソースジェネレーターで自動生成する</remarks>
public partial class ColumnViewModel : ObservableObject
{
    /// <summary>モデルと同一の識別子（保存・読込時のマッチングに用いる）</summary>
    public Guid Id { get; }

    /// <summary>カラム名</summary>
    [ObservableProperty]
    private string _name;

    /// <summary>データ型（例: int, varchar(100)）</summary>
    [ObservableProperty]
    private string _dataType;

    /// <summary>主キーかどうか</summary>
    [ObservableProperty]
    private bool _isPrimaryKey;

    /// <summary>外部キーかどうか</summary>
    [ObservableProperty]
    private bool _isForeignKey;

    /// <summary>NULL を許容するかどうかの実体フィールド</summary>
    private bool _isNullable;

    /// <summary>主キーチェックを編集可能にするかどうか</summary>
    [ObservableProperty]
    private bool _isPrimaryKeyEditable = true;

    /// <summary>外部キーチェックを編集可能にするかどうか</summary>
    [ObservableProperty]
    private bool _isForeignKeyEditable = true;

    /// <summary>外部キーフラグがリレーション設定により自動管理されているかどうか</summary>
    public bool IsForeignKeyManagedByRelationship { get; set; }

    /// <summary>NULL 許容チェックを編集可能にするかどうか（主キーの場合は不可）</summary>
    public bool IsNullableEditable => !IsPrimaryKey;

    /// <summary>NULL を許容するかどうか（主キーの場合は常に false へ強制する）</summary>
    public bool IsNullable
    {
        get => _isNullable;
        set => SetProperty(ref _isNullable, IsPrimaryKey ? false : value);
    }

    /// <summary>カラムの説明（SQL Server の拡張プロパティ <c>MS_Description</c> と同期する）</summary>
    [ObservableProperty]
    private string _description;

    /// <summary>モデルから ViewModel を生成する</summary>
    /// <param name="model">コピー元の <see cref="Column"/> モデル</param>
    public ColumnViewModel(Column model)
    {
        Id = model.Id;
        _name = model.Name;
        _dataType = model.DataType;
        _isPrimaryKey = model.IsPrimaryKey;
        _isForeignKey = model.IsForeignKey;
        _isNullable = model.IsPrimaryKey ? false : model.IsNullable;
        _description = model.Description ?? string.Empty;
    }

    /// <summary>IsPrimaryKey 変更直前に発火する（変更前スナップショット取得のためのフック）</summary>
    internal event EventHandler? IsPrimaryKeyChanging;

    /// <summary>IsNullable の連動変更を含む全処理完了後に発火する（Undo 記録制御に用いる）</summary>
    internal event EventHandler? IsPrimaryKeyChangeCompleted;

    /// <summary>IsPrimaryKey 変更直前のフック（変更前スナップショット取得を通知する）</summary>
    partial void OnIsPrimaryKeyChanging(bool value)
    {
        IsPrimaryKeyChanging?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>IsPrimaryKey 変更後に NULL 許容の連動更新と完了通知を行う</summary>
    partial void OnIsPrimaryKeyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNullableEditable));

        // 主キー化したカラムは NULL 不可とするため IsNullable を連動して落とす
        if (value && IsNullable)
        {
            IsNullable = false;
        }

        // 連動変更がすべて確定した後に完了を通知する
        IsPrimaryKeyChangeCompleted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>現在の状態をモデルへコピーして返す（保存時に使用する）</summary>
    public Column ToModel() =>
        new()
        {
            Id = Id,
            Name = Name,
            DataType = DataType,
            IsPrimaryKey = IsPrimaryKey,
            IsForeignKey = IsForeignKey,
            IsNullable = IsPrimaryKey ? false : IsNullable,
            Description = Description ?? string.Empty,
        };
}
