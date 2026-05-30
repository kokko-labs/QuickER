namespace ERDesigner.Generator;

internal sealed class CSharpGenerationModel
{
    public required string NamespaceName { get; init; }

    public required IReadOnlyList<CSharpClassModel> EntityClasses { get; init; }

    public required IReadOnlyList<CSharpEditModelClassModel> EditModelClasses { get; init; }

    public required IReadOnlyList<CSharpMapperModel> MapperClasses { get; init; }

    public required IReadOnlyList<string> Usings { get; init; }
}

internal sealed class CSharpClassModel
{
    public required string ClassName { get; init; }

    public required string TableName { get; init; }

    public required IReadOnlyList<CSharpPropertyModel> Properties { get; init; }

    public required IReadOnlyList<CSharpNavigationModel> Navigations { get; init; }
}

internal sealed class CSharpPropertyModel
{
    public required string PropertyName { get; init; }

    public required string ColumnName { get; init; }

    public required string TypeName { get; init; }

    public required bool IsNullable { get; init; }

    public required bool IsReferenceType { get; init; }

    public required bool IsPrimaryKey { get; init; }

    public required bool IsForeignKey { get; init; }

    public int? MaxLength { get; init; }

    public required string Initializer { get; init; }
}

internal sealed class CSharpNavigationModel
{
    public required string PropertyName { get; init; }

    public required string TypeName { get; init; }

    public required bool IsCollection { get; init; }

    public required bool IsNullable { get; init; }

    public required bool IsParentReference { get; init; }

    public required string DisplayTypeName { get; init; }

    public required string Initializer { get; init; }

    /// <summary>principal 側 (参照される側) のカラム名です。</summary>
    public required string PrincipalColumnName { get; init; }

    /// <summary>dependent 側 (FK 側) のカラム名です。</summary>
    public required string DependentColumnName { get; init; }
}

/// <summary>Mapper クラスの生成モデルです（インターフェースなし）。</summary>
internal sealed class CSharpMapperModel
{
    public required string ClassName { get; init; }

    public required string EntityClassName { get; init; }

    public required string EditModelClassName { get; init; }

    /// <summary>Entity → EditModel へコピーするプロパティのペアです。</summary>
    public required IReadOnlyList<CSharpMappingPropertyPair> ScalarProperties { get; init; }

    /// <summary>EditModel が保持する navigation プロパティの情報です。</summary>
    public required IReadOnlyList<CSharpMapperNavigationModel> NavigationProperties { get; init; }
}

internal sealed class CSharpMappingPropertyPair
{
    public required string PropertyName { get; init; }

    /// <summary>バインディング用プロパティ名 (例: BindingCustomerId)。</summary>
    public required string BindingPropertyName { get; init; }
}

internal sealed class CSharpMapperNavigationModel
{
    public required string PropertyName { get; init; }

    public required string EditModelTypeName { get; init; }

    public required bool IsCollection { get; init; }

    public required string PrincipalColumnName { get; init; }

    public required string DependentColumnName { get; init; }
}

// ---- EditModel 専用モデル ----

/// <summary>EditModel クラス全体の生成モデルです。</summary>
internal sealed class CSharpEditModelClassModel
{
    public required string ClassName { get; init; }

    public required string TableName { get; init; }

    public required IReadOnlyList<CSharpEditModelPropertyModel> Properties { get; init; }

    public required IReadOnlyList<CSharpNavigationModel> Navigations { get; init; }
}

/// <summary>EditModel の1プロパティに対応する生成モデルです。</summary>
internal sealed class CSharpEditModelPropertyModel
{
    /// <summary>通常プロパティ名 (例: CustomerId)。</summary>
    public required string PropertyName { get; init; }

    /// <summary>通常プロパティの型名 (例: int, string?, Guid)。</summary>
    public required string TypeName { get; init; }

    /// <summary>通常プロパティのバッキングフィールド名 (例: _customerId)。</summary>
    public required string FieldName { get; init; }

    /// <summary>バインディング用プロパティ名 (例: BindingCustomerId)。</summary>
    public required string BindingPropertyName { get; init; }

    /// <summary>バインディング用バッキングフィールド名 (例: _bindingCustomerId)。</summary>
    public required string BindingFieldName { get; init; }

    /// <summary>エラー情報を保持するフィールド名 (例: _errorCustomerId)。</summary>
    public required string ErrorFieldName { get; init; }

    /// <summary>バインディング setter で変換する際の TryParse 式が必要かどうかです。string 型の場合は false。</summary>
    public required bool NeedsParse { get; init; }

    /// <summary>TryParse に使う型名 (例: int, Guid)。NeedsParse=true のときのみ有効。</summary>
    public required string ParseTypeName { get; init; }

    /// <summary>通常プロパティのフィールド初期値式 (例: = string.Empty) です。</summary>
    public required string FieldInitializer { get; init; }

    /// <summary>バインディングフィールドの初期値式です。</summary>
    public required string BindingFieldInitializer { get; init; }

    /// <summary>プロパティが nullable かどうかです。</summary>
    public required bool IsNullable { get; init; }

    /// <summary>プロパティが参照型かどうかです。</summary>
    public required bool IsReferenceType { get; init; }
}
