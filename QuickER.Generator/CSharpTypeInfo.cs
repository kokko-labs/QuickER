namespace QuickER.Generator;

/// <summary>
/// SQL Server 型から解決された C# 型の情報
/// </summary>
/// <remarks><see cref="SqlServerCSharpTypeMapper"/> が生成し、Nullable 注釈の付与や [MaxLength] 属性の判定に使う</remarks>
public sealed class CSharpTypeInfo
{
    /// <summary>C# の型名（例: int, string, byte[]）。Nullable 注釈 "?" は含まない</summary>
    public required string TypeName { get; init; }

    /// <summary>参照型かどうか。false の場合は値型で、NULL 許容時に "?" を付けて Nullable&lt;T&gt; にする</summary>
    public required bool IsReferenceType { get; init; }

    /// <summary>文字列型の最大長。varchar(max) や長さ指定なしの場合は null で、[MaxLength] 属性の生成可否を決める</summary>
    public int? MaxLength { get; init; }

    /// <summary>decimal/numeric の精度（全体桁数 p）。指定なしや非 decimal 型は null。値オブジェクトの桁数検証に使う</summary>
    public int? Precision { get; init; }

    /// <summary>decimal/numeric のスケール（小数桁数 s）。指定なしや非 decimal 型は null。値オブジェクトの桁数検証に使う</summary>
    public int? Scale { get; init; }
}
