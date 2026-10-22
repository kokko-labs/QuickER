namespace QuickER.Dialects;

/// <summary>
/// 図で表せない定義の種類（DB にあるが図が表せないもの・図にあるが方言や機能が表せないもの）
/// </summary>
/// <remarks>
/// <para>
/// 種類ごとの向き・ID・名前は <see cref="UnsupportedDefinitionKinds"/>、方言ごとの宣言は <see cref="DialectDeclarations"/>、
/// 機能ごとの扱いは <see cref="UnsupportedTreatments"/> が持つ。種類を足したら 3 つとも書く（網羅テストが欠けを赤にする）。
/// </para>
/// <para>
/// 入れないもの：名前の衝突（<c>SchemaNames</c>）・取込範囲の外・型の表記の揺れ（方言の正規化）・名前の長さと表せない文字
/// （<see cref="IdentifierLengthLimit"/>）。
/// </para>
/// </remarks>
public enum UnsupportedDefinitionKind
{
    /// <summary>A1 計算列・生成列（仮想列・テンポラルの期間列を含む）</summary>
    ComputedColumn,

    /// <summary>A2 既定値 DEFAULT</summary>
    DefaultValue,

    /// <summary>A3 CHECK 制約</summary>
    CheckConstraint,

    /// <summary>A4 照合順序 COLLATE（MySQL は文字セットを含む）</summary>
    Collation,

    /// <summary>A5a 自動採番（DB が指定した値を拒む列）</summary>
    IdentityRejectingValues,

    /// <summary>A5b 自動採番（指定した値も受け付ける列）</summary>
    IdentityAcceptingValues,

    /// <summary>A6a INVISIBLE 列</summary>
    InvisibleColumn,

    /// <summary>A6b 列の ON UPDATE 属性（MySQL の ON UPDATE CURRENT_TIMESTAMP）</summary>
    ColumnOnUpdate,

    /// <summary>A6c SRID 属性（MySQL）</summary>
    SpatialReferenceId,

    /// <summary>A7 主キー列の NULL 許容の補正</summary>
    PrimaryKeyNullabilityAdjusted,

    /// <summary>B1 非一意索引（MySQL の外部キー用の自動索引を含む）</summary>
    NonUniqueIndex,

    /// <summary>B2 制約でない一意インデックス・関数索引・プレフィックス索引・フィルター付き索引</summary>
    NonConstraintUniqueIndex,

    /// <summary>B3 トリガー</summary>
    Trigger,

    /// <summary>B4a 無効化された制約</summary>
    DisabledConstraint,

    /// <summary>B4b 未検証の制約（NOT VALID・NOVALIDATE・is_not_trusted）</summary>
    UnvalidatedConstraint,

    /// <summary>B5 DEFERRABLE・ON CONFLICT・MATCH・NULLS NOT DISTINCT</summary>
    ConstraintClause,

    /// <summary>C1 ビュー</summary>
    View,

    /// <summary>C2a マテリアライズドビュー本体</summary>
    MaterializedView,

    /// <summary>C2b MV ログ表・MV の内部表（コンテナ表）</summary>
    MaterializedViewInternals,

    /// <summary>C3a 仮想テーブル（SQLite の FTS5・R*Tree）</summary>
    VirtualTable,

    /// <summary>C3b シャドウ表（仮想テーブルの付属表）</summary>
    ShadowTable,

    /// <summary>C3c 外部テーブル</summary>
    ExternalTable,

    /// <summary>C4a パーティション親</summary>
    PartitionedTable,

    /// <summary>C4b 子パーティション（PostgreSQL だけが別の表として持つ）</summary>
    ChildPartition,

    /// <summary>C5 テンポラル履歴表</summary>
    TemporalHistoryTable,

    /// <summary>C6 テーブルオプション（WITHOUT ROWID・STRICT・ENGINE・ROW_FORMAT・IOT・UNLOGGED）</summary>
    TableOption,

    /// <summary>C7a 一時表</summary>
    TemporaryTable,

    /// <summary>C7b 拡張が所有する表</summary>
    ExtensionOwnedTable,

    /// <summary>C7c システム表・ツールの表（sqlite_・ms_shipped・sysdiagrams）</summary>
    SystemTable,

    /// <summary>C9 除外した表を参照する外部キー</summary>
    ForeignKeyToExcludedTable,

    /// <summary>D1a 基底型へ平坦化される型（PostgreSQL のドメイン型・SQL Server の別名型）</summary>
    FlattenedType,

    /// <summary>D1b ユーザー定義型の本体（PostgreSQL の ENUM・複合型、SQL Server の CLR 型、Oracle のオブジェクト型）</summary>
    UserDefinedType,

    /// <summary>D2a 解析できない型（geometry・hierarchyid・sql_variant・xml など）</summary>
    UnparsableType,

    /// <summary>D2b DDL へ出せない型表記</summary>
    UnemittableTypeNotation,

    /// <summary>D2c 型を宣言していない列（SQLite。BLOB として取り込む）</summary>
    UndeclaredColumnType,

    /// <summary>N1 前後に空白のある表名・列名・制約名</summary>
    PaddedName,

    /// <summary>T1 列が 1 本も読めない表</summary>
    TableWithoutReadableColumns,

    /// <summary>A1b 計算列の型・NULL 許容の変更（式を変えない）</summary>
    ComputedColumnTypeChange,

    /// <summary>B6 主キーと同じ列集合の一意制約</summary>
    UniqueConstraintOnPrimaryKey,

    /// <summary>B7a 複合主キー・主キーなしの表（Repository は単一主キーのみ・EF Core は主キーなしを除外）</summary>
    CompositeOrMissingPrimaryKey,

    /// <summary>B7b 複合外部キー（C# 生成がナビゲーションと EF Fluent を作らない）</summary>
    CompositeForeignKey,

    /// <summary>B7c 多対多（C# 生成がナビゲーションを作らない・DDL は注記だけ）</summary>
    ManyToManyRelationship,

    /// <summary>D3 EditModel で入力できない列（配列・inet）</summary>
    NotEditableColumn,

    /// <summary>D5 方言が表せない型（負のスケール・長さなしの固定長・上限超過・rowversion）</summary>
    UnrepresentableType,

    /// <summary>E1 参照アクション ON UPDATE（Oracle が持たない）</summary>
    OnUpdateReferentialAction,

    /// <summary>E2 参照アクション SET DEFAULT（MySQL は効かず、Oracle は持たない）</summary>
    SetDefaultReferentialAction,

    /// <summary>E3 参照アクション SET NULL の外部キーの NOT NULL の子の列（SQL Server・MySQL は作れず、ほかの方言は親の削除で失敗する）</summary>
    SetNullOnNotNullColumn,

    /// <summary>E5 説明（コメント）（SQLite が持たない）</summary>
    Description,

    /// <summary>E6 スキーマ付きテーブル名（ドット付き）</summary>
    SchemaQualifiedTableName,

    /// <summary>X1 列順（SQL Server・PostgreSQL・Oracle は同期で並び替えられない）</summary>
    ColumnOrder,

    /// <summary>X2 外部キーの制約名（SQLite が永続化しない）</summary>
    ForeignKeyConstraintName,

    /// <summary>X3 複合外部キーの列の並び（SQL Server・MySQL は参照先キーの並びと一致が必要）</summary>
    CompositeForeignKeyColumnOrder,

    /// <summary>X4 外部キー両端の型の不整合</summary>
    ForeignKeyTypeMismatch,

    /// <summary>X5 候補キーでない列を参照する外部キー</summary>
    ForeignKeyNotOnCandidateKey,
}

/// <summary>図で表せない定義の向き</summary>
public enum UnsupportedDirection
{
    /// <summary>向き 1：DB にあるが図が表せない</summary>
    InDatabaseOnly,

    /// <summary>向き 2：図にあるが方言・機能が表せない</summary>
    InDiagramOnly,
}

/// <summary>種類の向き・ID・名前</summary>
/// <param name="Id">扱いの表の ID（例：<c>A1</c>・<c>C3c</c>）</param>
/// <param name="Direction">向き</param>
/// <param name="HasDiagramMark">図に印を持つか（保存後も機能の振る舞いを変える種類だけ）</param>
public sealed record UnsupportedKindInfo(
    string Id,
    UnsupportedDirection Direction,
    bool HasDiagramMark
);
