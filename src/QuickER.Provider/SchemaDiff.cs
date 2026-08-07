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

    /// <summary>一意制約（<c>UNIQUE</c>）を ADD する</summary>
    /// <remarks>
    /// 既存データに重複があれば実行時に失敗する（トランザクションのある方言ではロールバックされる）が、
    /// 列追加と同じく「制約を増やすだけ」で既存の定義を壊さないため既定で選択する。
    /// </remarks>
    AddUniqueConstraint,

    /// <summary>既存の一意制約（<c>UNIQUE</c>）を DROP する（破壊的変更のため既定では非選択）</summary>
    DropUniqueConstraint,

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

    /// <summary>
    /// FK の構成列ペア（親列名・子列名。宣言順。Add/DropForeignKey のみ）。
    /// </summary>
    /// <remarks>
    /// 複合外部キーもそのまま運ぶため、レンダラーはこの一覧をカンマ区切りで並べて
    /// <c>FOREIGN KEY (…) REFERENCES … (…)</c> を組み立てる。空なら FK 句を作れないためスキップする
    /// （<see cref="ColumnName"/> は表示・照合の互換のため先頭ペアの子列名が入る）。
    /// </remarks>
    public IReadOnlyList<ForeignKeyColumnNamePair> ForeignKeyColumnPairs { get; init; } = [];

    /// <summary>
    /// 対象の一意制約名 (Add/DropUniqueConstraint のみ)。
    /// </summary>
    /// <remarks>
    /// <see cref="SchemaDiffKind.DropUniqueConstraint"/> では live 側（DB）の実名が入る。
    /// <see cref="SchemaDiffKind.AddUniqueConstraint"/> では図側のモデル名で、未設定なら <c>null</c>＝
    /// レンダラーが <see cref="UniqueConstraintNaming.Resolve"/> で <c>UQ_{表}_{列…}</c> を合成する。
    /// </remarks>
    public string? UniqueConstraintName { get; init; }

    /// <summary>対象の一意制約の構成列名（宣言順。Add/DropUniqueConstraint のみ）。</summary>
    /// <remarks>
    /// 差分の同一性判定は列集合（大文字小文字・順序を無視）で行うが、DDL へは宣言順のまま出力するため
    /// 順序を保持した一覧で運ぶ。
    /// </remarks>
    public IReadOnlyList<string> UniqueConstraintColumns { get; init; } = [];

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
    /// 一意制約の削除（<see cref="SchemaDiffKind.DropUniqueConstraint"/>）も、候補キーを失って被参照 FK を
    /// 壊しうる（＝取り消すには重複データの解消が要る）ため破壊的に数える。
    /// </remarks>
    public bool IsDestructive =>
        Kind
            is SchemaDiffKind.AlterColumn
                or SchemaDiffKind.AlterPrimaryKey
                or SchemaDiffKind.DropColumn
                or SchemaDiffKind.DropTable
                or SchemaDiffKind.DropForeignKey
                or SchemaDiffKind.DropUniqueConstraint;

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
