using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using QuickER.Model;

namespace QuickER.Provider;

/// <summary>
/// スキーマ同期で検出される差分の種別
/// </summary>
public enum SchemaDiffKind
{
    /// <summary>テーブルを新規に CREATE する</summary>
    AddTable,

    /// <summary>既存テーブルに列を ADD する</summary>
    AddColumn,

    /// <summary>既存列の型 / NULL 制約を ALTER する（破壊的変更のため既定では非選択）</summary>
    AlterColumn,

    /// <summary>
    /// テーブルの主キー構成を変更する（追加・解除・構成変更・順序変更をテーブル単位で 1 項目に集約。
    /// 既存データによっては失敗しうる破壊的変更のため既定では非選択）
    /// </summary>
    AlterPrimaryKey,

    /// <summary>列を DROP する（破壊的変更のため既定では非選択）</summary>
    DropColumn,

    /// <summary>テーブルを DROP する（破壊的変更のため既定では非選択）</summary>
    DropTable,

    /// <summary>外部キー制約を ADD する</summary>
    AddForeignKey,

    /// <summary>既存の外部キー制約を DROP する（破壊的変更のため既定では非選択）</summary>
    DropForeignKey,

    /// <summary>テーブル説明を設定・更新・削除する</summary>
    SetTableDescription,

    /// <summary>カラム説明を設定・更新・削除する</summary>
    SetColumnDescription,

    /// <summary>列順変更などテーブル再作成が必要なことを知らせる情報専用項目（SQL 生成対象外）</summary>
    RebuildTable,

    /// <summary>列の並び順をダイアグラムに合わせて変更する（対応方言のみ・既定では非選択）</summary>
    ReorderColumns,
}

/// <summary>
/// 同期対象になりうる 1 件の差分。
/// </summary>
public sealed class SchemaDiffItem : INotifyPropertyChanged
{
    /// <summary>差分種別。</summary>
    public SchemaDiffKind Kind { get; init; }

    /// <summary>UI 用の説明文 (例: "テーブル Customer を作成")。</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>対象テーブル名 (スキーマ.テーブル形式または単純名)。</summary>
    public string TableName { get; init; } = string.Empty;

    /// <summary>対象カラム名 (該当する場合)。</summary>
    public string? ColumnName { get; init; }

    /// <summary>差分に対応するエンティティ (新規/変更時)。</summary>
    public Entity? Entity { get; init; }

    /// <summary>差分に対応するカラム (新規/変更時)。</summary>
    public Column? Column { get; init; }

    /// <summary>変更前の列定義 (AlterColumn のみ)。</summary>
    public Column? OldColumn { get; init; }

    /// <summary>差分に対応するリレーション (FK 操作時)。</summary>
    public Relationship? Relationship { get; init; }

    /// <summary>FK 操作で参照される親(PK) エンティティ。</summary>
    public Entity? ParentEntity { get; init; }

    /// <summary>FK 操作で参照される子(FK 保有) エンティティ。</summary>
    public Entity? ChildEntity { get; init; }

    /// <summary>削除する FK 制約の名前 (DropForeignKey のみ)。</summary>
    public string? ForeignKeyName { get; init; }

    /// <summary>変更後の説明文 (SetTable/ColumnDescription)。空文字なら削除を意味する。</summary>
    public string? NewDescription { get; init; }

    /// <summary>変更前の説明文 (SetTable/ColumnDescription)。null = まだ DB に説明が無い。</summary>
    public string? OldDescription { get; init; }

    private bool _isSelected = true;

    /// <summary>
    /// UI 上で選択変更できるか。
    /// 情報表示専用の項目は false にします。
    /// </summary>
    public bool IsSelectable { get; init; } = true;

    /// <summary>UI で実行対象として選択中か。</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>破壊的操作 (DROP/ALTER) の場合 true。</summary>
    /// <remarks>
    /// 主キー変更（<see cref="SchemaDiffKind.AlterPrimaryKey"/>）も破壊的に数える。
    /// 主キーの解除・付け替えは重複データや NULL の存在で失敗しうるうえ、被参照 FK の張り直しを伴うため。
    /// </remarks>
    public bool IsDestructive =>
        Kind
            is SchemaDiffKind.AlterColumn
                or SchemaDiffKind.AlterPrimaryKey
                or SchemaDiffKind.DropColumn
                or SchemaDiffKind.DropTable
                or SchemaDiffKind.DropForeignKey;

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// 計算済みのスキーマ差分一式。
/// </summary>
public sealed class SchemaDiff
{
    /// <summary>すべての差分。</summary>
    public List<SchemaDiffItem> Items { get; } = new();

    /// <summary>差分が 1 件もないか。</summary>
    public bool IsEmpty => Items.Count == 0;
}
