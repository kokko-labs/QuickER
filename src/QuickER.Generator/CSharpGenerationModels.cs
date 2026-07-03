namespace QuickER.Generator;

/// <summary>Scriban テンプレートへ渡す C# コード生成のルートモデル</summary>
internal sealed class CSharpGenerationModel
{
    /// <summary>生成コードの名前空間</summary>
    public required string NamespaceName { get; init; }

    /// <summary>エンティティクラスの生成モデル一覧</summary>
    public required IReadOnlyList<CSharpClassModel> EntityClasses { get; init; }

    /// <summary>EditModel クラスの生成モデル一覧</summary>
    public required IReadOnlyList<CSharpEditModelClassModel> EditModelClasses { get; init; }

    /// <summary>Mapper クラスの生成モデル一覧</summary>
    public required IReadOnlyList<CSharpMapperModel> MapperClasses { get; init; }

    /// <summary>Repository クラスの生成モデル一覧</summary>
    public required IReadOnlyList<CSharpRepositoryModel> RepositoryClasses { get; init; }

    /// <summary>生成コード冒頭に出力する using 名前空間一覧</summary>
    public required IReadOnlyList<string> Usings { get; init; }

    /// <summary>値オブジェクト（Value Object）クラスの生成モデル一覧（GenerateValueObjects が OFF のときは空）</summary>
    public required IReadOnlyList<CSharpValueObjectModel> ValueObjectClasses { get; init; }

    /// <summary>EF Core（DbContext・Fluent 構成）の生成モデル。GenerateEfCore が OFF のときは null</summary>
    public CSharpEfCoreModel? EfCore { get; init; }
}

/// <summary>EF Core 用コード（DbContext と OnModelCreating の Fluent 構成）のルート生成モデル</summary>
internal sealed class CSharpEfCoreModel
{
    /// <summary>DbContext 上の DbSet プロパティ一覧（宣言順はエンティティ順）</summary>
    public required IReadOnlyList<CSharpEfCoreDbSetModel> DbSets { get; init; }

    /// <summary>エンティティごとの Fluent 構成一覧</summary>
    public required IReadOnlyList<CSharpEfCoreEntityConfigModel> Entities { get; init; }

    /// <summary>EntityBase の永続化対象外メンバー（Ignore する get/set 可能な公開プロパティ名）一覧</summary>
    public required IReadOnlyList<string> IgnoredBaseMembers { get; init; }
}

/// <summary>DbContext の 1 つの DbSet プロパティに対応する生成モデル</summary>
internal sealed class CSharpEfCoreDbSetModel
{
    /// <summary>DbSet の要素となる Entity クラス名</summary>
    public required string EntityClassName { get; init; }

    /// <summary>DbSet プロパティ名（Entity クラス名から "Entity" を除いた複数形風の名前）</summary>
    public required string PropertyName { get; init; }
}

/// <summary>1 エンティティの EF Core Fluent 構成（builder.Entity&lt;T&gt;(...) の中身）の生成モデル</summary>
internal sealed class CSharpEfCoreEntityConfigModel
{
    /// <summary>対象の Entity クラス名</summary>
    public required string EntityClassName { get; init; }

    /// <summary>マッピング先テーブル名（ToTable 用）</summary>
    public required string TableName { get; init; }

    /// <summary>主キーを構成するプロパティ名一覧（HasKey 用。単一・複合の両方に対応）</summary>
    public required IReadOnlyList<string> KeyPropertyNames { get; init; }

    /// <summary>スカラープロパティの構成一覧</summary>
    public required IReadOnlyList<CSharpEfCorePropertyConfigModel> Properties { get; init; }

    /// <summary>このエンティティが principal（親）となるリレーションの構成一覧（HasOne/WithMany 等）</summary>
    public required IReadOnlyList<CSharpEfCoreRelationshipConfigModel> Relationships { get; init; }
}

/// <summary>1 スカラープロパティの EF Core Fluent 構成の生成モデル</summary>
internal sealed class CSharpEfCorePropertyConfigModel
{
    /// <summary>プロパティ名（Property(e =&gt; e.Xxx) 用）</summary>
    public required string PropertyName { get; init; }

    /// <summary>マッピング先カラム名（HasColumnName 用）</summary>
    public required string ColumnName { get; init; }

    /// <summary>必須（非 NULL）かどうか（IsRequired 用）</summary>
    public required bool IsRequired { get; init; }

    /// <summary>文字列の最大長（HasMaxLength 用）。無指定は null</summary>
    public int? MaxLength { get; init; }

    /// <summary>decimal の全体桁数（HasPrecision 用）。無指定・非 decimal は null</summary>
    public int? Precision { get; init; }

    /// <summary>decimal の小数桁数（HasPrecision の第 2 引数）。Precision がある場合のみ有効</summary>
    public int? Scale { get; init; }

    /// <summary>行バージョン列（IsRowVersion 用）かどうか</summary>
    public required bool IsRowVersion { get; init; }

    /// <summary>値オブジェクト型かどうか（HasConversion 構成の要否判定）</summary>
    public required bool IsValueObject { get; init; }

    /// <summary>値オブジェクトのクラス名（IsValueObject=true のときのみ有効）</summary>
    public string ValueObjectClassName { get; init; } = string.Empty;
}

/// <summary>principal（親）から見た 1 リレーションの EF Core Fluent 構成の生成モデル</summary>
/// <remarks>
/// 親エンティティ側に構成をまとめる（HasMany/HasOne → WithOne/WithMany → HasForeignKey → OnDelete）。
/// dependent（子）側は親への単一参照ナビゲーションを 1 つ持つ前提とする
/// </remarks>
internal sealed class CSharpEfCoreRelationshipConfigModel
{
    /// <summary>dependent（子・FK 側）の Entity クラス名</summary>
    public required string DependentClassName { get; init; }

    /// <summary>親側のナビゲーションプロパティ名（1 対多はコレクション、1 対 1 は単一参照）</summary>
    public required string PrincipalNavigationName { get; init; }

    /// <summary>子側の親参照ナビゲーションプロパティ名</summary>
    public required string DependentNavigationName { get; init; }

    /// <summary>1 対多（親がコレクションを持つ）かどうか。false は 1 対 1</summary>
    public required bool IsCollection { get; init; }

    /// <summary>FK を構成する子側プロパティ名一覧（HasForeignKey 用）</summary>
    public required IReadOnlyList<string> ForeignKeyPropertyNames { get; init; }

    /// <summary>親削除時にカスケード削除するかどうか（OnDelete の Cascade/Restrict 切り替え）</summary>
    public required bool CascadeDelete { get; init; }
}

/// <summary>値オブジェクト（Value Object）クラスの生成モデル</summary>
/// <remarks>列名（正規化 Pascal）でグローバルに共有される 1 型に対応する</remarks>
internal sealed class CSharpValueObjectModel
{
    /// <summary>生成する VO クラス名（例: CustomerIdValue）</summary>
    public required string ClassName { get; init; }

    /// <summary>内包する値の C# 型名（TValue。NULL 注釈なし。例: int, string, byte[]）</summary>
    public required string ValueTypeName { get; init; }

    /// <summary>継承する基底クラスの宣言（型引数込み。例: ValueObjectOrderedBase&lt;CustomerIdValue, int&gt;）</summary>
    public required string BaseDeclaration { get; init; }

    /// <summary>実装する汎用インターフェース宣言（例: IValueObject&lt;CustomerIdValue, int&gt;）</summary>
    public required string InterfaceDeclaration { get; init; }

    /// <summary>GuidKey（string で GUID 保持・無引数生成で自動採番）かどうか</summary>
    public required bool IsGuidKey { get; init; }

    /// <summary>診断・エラーメッセージ用の代表カラム名</summary>
    public required string ColumnName { get; init; }

    /// <summary>string の最大長（自動 MaxLength 検証用）。無指定は null</summary>
    public int? MaxLength { get; init; }

    /// <summary>decimal の精度 p（自動桁数検証用）。非 decimal・無指定は null</summary>
    public int? Precision { get; init; }

    /// <summary>decimal のスケール s（自動桁数検証用）。非 decimal・無指定は null</summary>
    public int? Scale { get; init; }
}

/// <summary>エンティティクラスの生成モデル</summary>
internal sealed class CSharpClassModel
{
    /// <summary>生成するクラス名</summary>
    public required string ClassName { get; init; }

    /// <summary>対応するテーブル名</summary>
    public required string TableName { get; init; }

    /// <summary>スカラープロパティの生成モデル一覧</summary>
    public required IReadOnlyList<CSharpPropertyModel> Properties { get; init; }

    /// <summary>ナビゲーションプロパティの生成モデル一覧</summary>
    public required IReadOnlyList<CSharpNavigationModel> Navigations { get; init; }
}

/// <summary>エンティティの 1 スカラープロパティに対応する生成モデル</summary>
internal sealed class CSharpPropertyModel
{
    /// <summary>プロパティ名</summary>
    public required string PropertyName { get; init; }

    /// <summary>対応するカラム名</summary>
    public required string ColumnName { get; init; }

    /// <summary>C# 型名</summary>
    public required string TypeName { get; init; }

    /// <summary>NULL 許容かどうか</summary>
    public required bool IsNullable { get; init; }

    /// <summary>参照型かどうか</summary>
    public required bool IsReferenceType { get; init; }

    /// <summary>主キーかどうか</summary>
    public required bool IsPrimaryKey { get; init; }

    /// <summary>外部キーかどうか</summary>
    public required bool IsForeignKey { get; init; }

    /// <summary>最大長（文字列型などで指定される場合）</summary>
    public int? MaxLength { get; init; }

    /// <summary>DB カラムのメタ情報: decimal の全体桁数 precision（属性 [SqlColumnType] の Precision 用）</summary>
    public int? FacetPrecision { get; init; }

    /// <summary>DB カラムのメタ情報: decimal の小数桁数 scale（属性 [SqlColumnType] の Scale 用）</summary>
    public int? FacetScale { get; init; }

    /// <summary>
    /// SQL パラメータ型明示化に使う <c>SqlDbType</c> の列挙名（例: "VarChar"）。[SqlColumnType] 属性の生成に使う。
    /// 未知の型は null で、属性を付与せず AddWithValue にフォールバックさせる。
    /// </summary>
    public string? SqlDbTypeName { get; init; }

    /// <summary>[SqlColumnType] の Size に載せる宣言長（n / max=-1 / 無指定=0）。文字列・バイナリ以外は 0</summary>
    public int SqlDeclaredLength { get; init; }

    /// <summary>フィールド初期化子の式</summary>
    public required string Initializer { get; init; }
}

/// <summary>ナビゲーションプロパティの生成モデル</summary>
internal sealed class CSharpNavigationModel
{
    /// <summary>プロパティ名</summary>
    public required string PropertyName { get; init; }

    /// <summary>参照先の型名</summary>
    public required string TypeName { get; init; }

    /// <summary>コレクション（1 対多の多側）かどうか</summary>
    public required bool IsCollection { get; init; }

    /// <summary>NULL 許容かどうか</summary>
    public required bool IsNullable { get; init; }

    /// <summary>親（参照される側）への参照かどうか</summary>
    public required bool IsParentReference { get; init; }

    /// <summary>カスケード保存/削除の対象（子方向のナビゲーション）かどうか</summary>
    public required bool Cascade { get; init; }

    /// <summary>表示用の型名（コレクションなら要素型を包んだ表記）</summary>
    public required string DisplayTypeName { get; init; }

    /// <summary>カスケード子のバッキングフィールド名（例: _children）。EditModel のカスケード子ナビゲーションでのみ設定</summary>
    public string FieldName { get; init; } = string.Empty;

    /// <summary>フィールド初期化子の式</summary>
    public required string Initializer { get; init; }

    /// <summary>principal 側（参照される側）のテーブル名</summary>
    public required string PrincipalTableName { get; init; }

    /// <summary>principal 側（参照される側）のカラム名</summary>
    public required string PrincipalColumnName { get; init; }

    /// <summary>dependent 側（FK 側）のテーブル名</summary>
    public required string DependentTableName { get; init; }

    /// <summary>dependent 側（FK 側）のカラム名</summary>
    public required string DependentColumnName { get; init; }
}

/// <summary>Mapper クラスの生成モデル（インターフェースなし）</summary>
internal sealed class CSharpMapperModel
{
    /// <summary>生成する Mapper クラス名</summary>
    public required string ClassName { get; init; }

    /// <summary>変換元の Entity クラス名</summary>
    public required string EntityClassName { get; init; }

    /// <summary>変換先の EditModel クラス名</summary>
    public required string EditModelClassName { get; init; }

    /// <summary>Entity → EditModel へコピーするプロパティのペア一覧</summary>
    public required IReadOnlyList<CSharpMappingPropertyPair> ScalarProperties { get; init; }

    /// <summary>EditModel が保持するナビゲーションプロパティの情報一覧</summary>
    public required IReadOnlyList<CSharpMapperNavigationModel> NavigationProperties { get; init; }
}

/// <summary>Entity と EditModel の対応プロパティ 1 組に対応する生成モデル</summary>
internal sealed class CSharpMappingPropertyPair
{
    /// <summary>プロパティ名</summary>
    public required string PropertyName { get; init; }

    /// <summary>Entity 側の型名</summary>
    public required string EntityTypeName { get; init; }

    /// <summary>EditModel 側の型名</summary>
    public required string EditModelTypeName { get; init; }

    /// <summary>EditModel 側プロパティが NULL 許容かどうか</summary>
    public required bool EditModelIsNullable { get; init; }

    /// <summary>byte[] 系プロパティかどうか</summary>
    public required bool IsBinary { get; init; }

    /// <summary>Entity からバインディング文字列へ変換する式</summary>
    public required string LoadBindingExpression { get; init; }

    /// <summary>バインディング用プロパティ名（例: BindingCustomerId）</summary>
    public required string BindingPropertyName { get; init; }
}

/// <summary>Mapper が扱うナビゲーションプロパティの生成モデル</summary>
internal sealed class CSharpMapperNavigationModel
{
    /// <summary>プロパティ名</summary>
    public required string PropertyName { get; init; }

    /// <summary>EditModel 側の型名</summary>
    public required string EditModelTypeName { get; init; }

    /// <summary>対応する子の Mapper クラス名（カスケード変換で呼び出す）</summary>
    public required string MapperClassName { get; init; }

    /// <summary>コレクションかどうか</summary>
    public required bool IsCollection { get; init; }

    /// <summary>NULL 許容かどうか（単一参照のとき有効）</summary>
    public required bool IsNullable { get; init; }

    /// <summary>子方向（カスケード変換対象）かどうか。親参照は除外し無限再帰を防ぐ</summary>
    public required bool IsCascade { get; init; }

    /// <summary>principal 側（参照される側）のカラム名</summary>
    public required string PrincipalColumnName { get; init; }

    /// <summary>dependent 側（FK 側）のカラム名</summary>
    public required string DependentColumnName { get; init; }
}

/// <summary>Repository クラスの生成モデル</summary>
internal sealed class CSharpRepositoryModel
{
    /// <summary>生成する Repository インターフェース名</summary>
    public required string InterfaceName { get; init; }

    /// <summary>生成する Repository 実装クラス名</summary>
    public required string ClassName { get; init; }

    /// <summary>対象の Entity クラス名</summary>
    public required string EntityClassName { get; init; }

    /// <summary>主キーの型名</summary>
    public required string KeyTypeName { get; init; }
}

// ---- EditModel 専用モデル ----

/// <summary>EditModel クラス全体の生成モデル</summary>
internal sealed class CSharpEditModelClassModel
{
    /// <summary>生成する EditModel クラス名</summary>
    public required string ClassName { get; init; }

    /// <summary>対応するテーブル名</summary>
    public required string TableName { get; init; }

    /// <summary>EditModel のプロパティ生成モデル一覧</summary>
    public required IReadOnlyList<CSharpEditModelPropertyModel> Properties { get; init; }

    /// <summary>ナビゲーションプロパティの生成モデル一覧</summary>
    public required IReadOnlyList<CSharpNavigationModel> Navigations { get; init; }

    /// <summary>カスケード対象（子方向）のナビゲーションを持つかどうか</summary>
    public required bool HasCascadeNavigations { get; init; }

    /// <summary>親モデルの型が一意に定まるときの型付き ParentModel の型名（定まらない場合は null＝基底の EditModelBase? のみ）</summary>
    public string? TypedParentModelTypeName { get; init; }
}

/// <summary>EditModel の 1 プロパティに対応する生成モデル</summary>
/// <remarks>確定値プロパティと UI バインディング用文字列プロパティを併せ持つ構成を表す</remarks>
internal sealed class CSharpEditModelPropertyModel
{
    /// <summary>通常プロパティ名（例: CustomerId）</summary>
    public required string PropertyName { get; init; }

    /// <summary>通常プロパティの型名（例: int, string?, Guid）</summary>
    public required string TypeName { get; init; }

    /// <summary>通常プロパティのバッキングフィールド名（例: _customerId）</summary>
    public required string FieldName { get; init; }

    /// <summary>バインディング用プロパティ名（例: BindingCustomerId）</summary>
    public required string BindingPropertyName { get; init; }

    /// <summary>バインディング用バッキングフィールド名（例: _bindingCustomerId）</summary>
    public required string BindingFieldName { get; init; }

    /// <summary>バインディング setter で変換に TryParse が必要かどうか（string 型は false）</summary>
    public required bool NeedsParse { get; init; }

    /// <summary>TryParse に使う型名（例: int, Guid）NeedsParse=true のときのみ有効</summary>
    public required string ParseTypeName { get; init; }

    /// <summary>通常プロパティのフィールド初期化子の式（例: = string.Empty）</summary>
    public required string FieldInitializer { get; init; }

    /// <summary>バインディングフィールドの初期化子の式</summary>
    public required string BindingFieldInitializer { get; init; }

    /// <summary>NULL 許容かどうか</summary>
    public required bool IsNullable { get; init; }

    /// <summary>必須項目（Entity 側が非 NULL）かどうか</summary>
    public required bool IsRequired { get; init; }

    /// <summary>参照型かどうか</summary>
    public required bool IsReferenceType { get; init; }

    /// <summary>byte[] 系プロパティかどうか</summary>
    public required bool IsBinary { get; init; }

    /// <summary>確定値からバインディング文字列へ戻す式</summary>
    public required string RevertBindingExpression { get; init; }

    /// <summary>確定値が値オブジェクト型かどうか（true なら確定値は VO?、バインド setter は TryCreate で検証して生成する）</summary>
    public bool IsValueObject { get; init; }

    /// <summary>値オブジェクトのクラス名（IsValueObject=true のときのみ有効）</summary>
    public string ValueObjectClassName { get; init; } = string.Empty;
}
