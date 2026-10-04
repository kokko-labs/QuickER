namespace QuickER.Provider.Sqlite;

/// <summary>
/// SQLite の型アフィニティ（宣言型の名前から列の格納の仕方を決める規則）のうち、型カタログと型マッパーが共有する判定。
/// </summary>
/// <remarks>
/// SQLite は宣言型の名前を次の順で調べて列のアフィニティを決める（SQLite の文書「Datatypes In SQLite」の 3.1 節）:
/// <list type="number">
///   <item><c>INT</c> を含む → INTEGER</item>
///   <item><c>CHAR</c> / <c>CLOB</c> / <c>TEXT</c> を含む → TEXT</item>
///   <item><c>BLOB</c> を含む、または型が無い → BLOB</item>
///   <item><c>REAL</c> / <c>FLOA</c> / <c>DOUB</c> を含む → REAL（8 バイトの倍精度浮動小数点数）</item>
///   <item>それ以外 → NUMERIC</item>
/// </list>
/// REAL アフィニティの列は常に 8 バイトの倍精度で値を持つため、<c>REAL</c> も <c>FLOAT</c> も <c>DOUBLE</c> も、
/// 単精度（C# の <c>float</c>）ではなく倍精度（<c>double</c>・正規型の <c>Float64</c>）として扱う。
/// 単精度で受けると、読み書きのたびに精度を落とす。
/// </remarks>
internal static class SqliteTypeAffinity
{
    /// <summary>宣言型の名前（小文字・括弧の引数を除いたもの）が REAL アフィニティになるか</summary>
    /// <param name="typeName">宣言型の名前（例: <c>real</c> / <c>double precision</c> / <c>float8</c>）</param>
    public static bool IsReal(string typeName) =>
        !typeName.Contains("int", StringComparison.OrdinalIgnoreCase)
        && !typeName.Contains("char", StringComparison.OrdinalIgnoreCase)
        && !typeName.Contains("clob", StringComparison.OrdinalIgnoreCase)
        && !typeName.Contains("text", StringComparison.OrdinalIgnoreCase)
        && !typeName.Contains("blob", StringComparison.OrdinalIgnoreCase)
        && (
            typeName.Contains("real", StringComparison.OrdinalIgnoreCase)
            || typeName.Contains("floa", StringComparison.OrdinalIgnoreCase)
            || typeName.Contains("doub", StringComparison.OrdinalIgnoreCase)
        );
}
