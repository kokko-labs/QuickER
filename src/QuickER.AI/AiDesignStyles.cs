namespace QuickER.AI;

/// <summary>AI が生成する識別子名の命名規則</summary>
public enum AiIdentifierNamingStyle
{
    /// <summary>パスカルケース (例: <c>CustomerOrder</c>)</summary>
    PascalCase,

    /// <summary>スネークケース (例: <c>customer_order</c>)</summary>
    SnakeCase,
}

/// <summary>AI が生成するテーブル名の単数形・複数形の方針</summary>
public enum AiTableNameNumberStyle
{
    /// <summary>単数形 (例: <c>Customer</c>)</summary>
    Singular,

    /// <summary>複数形 (例: <c>Customers</c>)</summary>
    Plural,
}
