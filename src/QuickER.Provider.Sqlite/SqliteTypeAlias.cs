using System.Text.RegularExpressions;

namespace QuickER.Provider.Sqlite;

/// <summary>
/// SQLite / 他方言由来の型別名を代表表記へ解決する（型カタログと型マッパーが共有する別名表）。
/// </summary>
/// <remarks>
/// SQLite の型親和性は名称の部分一致で決まるが、型カタログ・型マッパーは宣言型を厳密名で扱うため、
/// 代表的な別名のみを明示的に正規化する。SQLite の伝統キーワード（<c>integer</c> / <c>boolean</c> 等）や
/// 他方言由来の別名（<c>numeric</c> / <c>character varying</c> 等）を SQL Server 風の代表名へ寄せる。
/// 別名表を 2 か所に持つと、片方だけ足した別名でカタログ（方言の切替）と型マッパー（生成コード）の結果が割れる。
/// </remarks>
internal static partial class SqliteTypeAlias
{
    /// <summary>型名（括弧の引数を除いたもの）を小文字にし、語の間の空白を 1 つへ畳んでから代表表記へ解決する</summary>
    /// <param name="typeName">宣言型の名前（例: <c>INTEGER</c> / <c>double  precision</c>）</param>
    public static string Resolve(string typeName) =>
        WhitespacePattern().Replace(typeName.Trim(), " ").ToLowerInvariant() switch
        {
            // SQLite の INTEGER は最大 8 バイト格納（EF Core も long 対応）のため Int64 として扱う
            "integer" or "int8" => "bigint",
            "int4" => "int",
            "int2" => "smallint",
            "boolean" or "bool" => "bit",
            "numeric" => "decimal",
            "double" or "double precision" => "float",
            "character varying" => "varchar",
            "character" => "char",
            "guid" => "uniqueidentifier",
            "timestamp" => "datetime2",
            // SQL Server の型の名前で、SQLite で同じ振る舞いの代表名があるもの。写さないと型マッパーが string へ倒し、
            // sqlserver と sqlite のマルチターゲットの生成が型の食い違いで断られる（DialectTypeTableTests の
            // MultiTargetDialects_MapEveryTypeNameToSameCSharpType）。
            // smalldatetime は datetime と同じ NUMERIC アフィニティで、ドライバも同じ文字列で書いて読む（分への丸めは無い）
            "smalldatetime" => "datetime",
            // smallmoney は money と同じ NUMERIC アフィニティの数（範囲の検査は無い）
            "smallmoney" => "money",
            // image は blob と同じ無制限バイナリ（byte[] で書く値は blob のまま保存される）
            "image" => "blob",
            var name => name,
        };

    /// <summary>連続する空白（型名の語の区切り）</summary>
    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespacePattern();
}
