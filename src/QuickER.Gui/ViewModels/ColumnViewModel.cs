using CommunityToolkit.Mvvm.ComponentModel;
using QuickER.Model;
using QuickER.Services;

namespace QuickER.ViewModels;

/// <summary>1 カラムをビューへバインドするための ViewModel</summary>
/// <remarks><see cref="INotifyPropertyChanged"/> は CommunityToolkit.Mvvm のソースジェネレーターで自動生成する</remarks>
public partial class ColumnViewModel : ObservableObject
{
    /// <summary>モデルと同一の識別子（保存・読込時のマッチングに用いる）</summary>
    public Guid Id { get; }

    /// <summary>カラム名の実体フィールド</summary>
    private string _name;

    /// <summary>カラム名（前後の空白を除いた形で保持する）</summary>
    /// <remarks>
    /// 同じテーブルにすでに同じ名前（<see cref="SchemaNames.AreSame"/> の規則）の列があるときは、書き換えを受け付けず
    /// 入力欄へ元の名前を戻し、<see cref="NameChangeRejected"/> で知らせる（変更履歴にも残らない）。
    /// 元に戻す・やり直すは <see cref="ApplyName"/> を使い、検査を通さない
    /// </remarks>
    public string Name
    {
        get => _name;
        set
        {
            var stored = SchemaNames.ToStoredForm(value);

            if (
                NameScope?.FirstOrDefault(other =>
                    !ReferenceEquals(other, this) && SchemaNames.AreSame(other.Name, stored)
                ) is
                { } clash
            )
            {
                NameChangeRejected?.Invoke(
                    this,
                    new NameChangeRejectedEventArgs(stored, clash.Name, null)
                );
                OnPropertyChanged();

                return;
            }

            ApplyName(
                stored,
                notifyAlways: !string.Equals(stored, value, StringComparison.Ordinal)
            );
        }
    }

    /// <summary>カラム名を重複の検査なしで書き換える（元に戻す・やり直すの適用用）</summary>
    /// <param name="name">前後の空白を除いたカラム名</param>
    /// <param name="notifyAlways">値が変わらなくても入力欄へ通知する（入力された空白を取り除いた形へ戻すため）</param>
    internal void ApplyName(string name, bool notifyAlways = false)
    {
        if (!SetProperty(ref _name, name, nameof(Name)) && notifyAlways)
        {
            OnPropertyChanged(nameof(Name));
        }
    }

    /// <summary>カラム名の重複を検査する相手（同じテーブルの列の一覧。テーブルに載っていなければ <c>null</c>＝検査しない）</summary>
    internal IReadOnlyCollection<ColumnViewModel>? NameScope { get; set; }

    /// <summary>カラム名の書き換えを、重複のため受け付けなかったときに発火する（所有するテーブルが転送する）</summary>
    internal event EventHandler<NameChangeRejectedEventArgs>? NameChangeRejected;

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

    /// <summary>いずれかの一意制約の構成列かどうかの実体フィールド</summary>
    private bool _isUniqueConstraintMember;

    /// <summary>いずれかの一意制約（UNIQUE）の構成列かどうか</summary>
    /// <remarks>
    /// 一意制約の正本は <see cref="EntityViewModel.UniqueConstraints"/> 側にあり、
    /// このフラグは所有エンティティが制約の増減・構成列変更に追従して設定する派生表示状態
    /// （ER 図カードの UQ 標識の判定に用いる。保存モデルへは書き出さない）
    /// </remarks>
    public bool IsUniqueConstraintMember
    {
        get => _isUniqueConstraintMember;
        internal set
        {
            var before = KeyMark;

            if (SetProperty(ref _isUniqueConstraintMember, value))
            {
                NotifyKeyMarkChangedFrom(before);
            }
        }
    }

    /// <summary>ER 図カードのキー欄に表示する標識（PK &gt; FK &gt; UQ の優先度で 1 つに畳む）</summary>
    public ColumnKeyMark KeyMark =>
        ColumnKeyMarkPalette.Resolve(IsPrimaryKey, IsForeignKey, IsUniqueConstraintMember);

    /// <summary>キー標識の表示文字（標識なしは空文字）</summary>
    public string KeyMarkText => ColumnKeyMarkPalette.GetText(KeyMark);

    /// <summary>キー標識の表示色（#RRGGBB）</summary>
    public string KeyMarkColor => ColumnKeyMarkPalette.GetColor(KeyMark);

    /// <summary>キー標識が実際に変わったときだけ、派生プロパティ 3 種の変更を通知する</summary>
    /// <param name="before">変更前の標識</param>
    /// <remarks>
    /// 標識は PK &gt; FK &gt; UQ の優先度で 1 つに畳むので、下位のフラグが変わっても表示が変わらないことがある
    /// （主キーの列が一意制約の構成列になる・外れるなど）。そのときは通知しない
    /// </remarks>
    private void NotifyKeyMarkChangedFrom(ColumnKeyMark before)
    {
        if (KeyMark == before)
        {
            return;
        }

        OnPropertyChanged(nameof(KeyMark));
        OnPropertyChanged(nameof(KeyMarkText));
        OnPropertyChanged(nameof(KeyMarkColor));
    }

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

    /// <summary>計算列・生成列かどうか（取込で確定し、GUI では編集しない）</summary>
    /// <remarks>
    /// 編集 UI を持たない純粋なパススルー。値を運ばないと「取込 → GUI で編集 → 保存」で
    /// <see cref="Column.IsComputed"/> が黙って消え、生成コードが書き込み対象へ戻してしまう。
    /// </remarks>
    public bool IsComputed { get; }

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
        IsComputed = model.IsComputed;
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
        NotifyKeyMarkChangedFrom(
            ColumnKeyMarkPalette.Resolve(!value, IsForeignKey, IsUniqueConstraintMember)
        );

        // 主キー化したカラムは NULL 不可とするため IsNullable を連動して落とす
        if (value && IsNullable)
        {
            IsNullable = false;
        }

        // 連動変更がすべて確定した後に完了を通知する
        IsPrimaryKeyChangeCompleted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>外部キー変更時にキー標識の表示を更新する</summary>
    partial void OnIsForeignKeyChanged(bool value) =>
        NotifyKeyMarkChangedFrom(
            ColumnKeyMarkPalette.Resolve(IsPrimaryKey, !value, IsUniqueConstraintMember)
        );

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
            IsComputed = IsComputed,
        };
}
