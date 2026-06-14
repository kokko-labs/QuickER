namespace ERDesigner.Generator;

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

    /// <summary>コレクションかどうか</summary>
    public required bool IsCollection { get; init; }

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

    /// <summary>エラー情報を保持するフィールド名（例: _errorCustomerId）</summary>
    public required string ErrorFieldName { get; init; }

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

    /// <summary>参照型かどうか</summary>
    public required bool IsReferenceType { get; init; }

    /// <summary>byte[] 系プロパティかどうか</summary>
    public required bool IsBinary { get; init; }

    /// <summary>確定値からバインディング文字列へ戻す式</summary>
    public required string RevertBindingExpression { get; init; }
}
