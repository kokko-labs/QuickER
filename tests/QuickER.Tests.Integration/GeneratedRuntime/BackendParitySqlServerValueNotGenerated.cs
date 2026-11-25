using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 値の往復の生成器（<c>BackendParitySqlServerValueGenerator</c>）が作らない形の宣言
/// （QuickER.Tests にもリンクで入るので、Integration の型を cref でも参照しない）
/// </summary>
/// <remarks>純粋なデータ（Engines の型と BCL だけを参照する）。</remarks>
internal static class BackendParitySqlServerValueNotGenerated
{
    /// <summary>宣言</summary>
    public static NotGeneratedDeclaration Declaration { get; } =
        new(
            "BackendParitySqlServerValueGenerator",
            [ProtectionNames.EnginePart(EngineIds.GeneratedRuntime, "ParityValuesSqlServer")],
            [
                NotGenerated.Accepted(
                    "utf8-collation-varchar",
                    "UTF-8 の照合順序の varchar の列",
                    "UTF-8 の照合順序では列の長さがバイトで数えられ、コードページの置き換えも起きない。フィクスチャは既定の照合順序（コードページ 1252）だけを使い、期待の長さと置き換えの規則もその照合順序のもの",
                    NotGenerated.Unguarded
                ),
                NotGenerated.Accepted(
                    "varchar-excess-mapped-to-space",
                    "Unicode でない列で、長さを超えた分がコードページで半角の空白へ置き換わる文字（全角の空白など）の値",
                    "サーバーはコードページへ置き換えた後の空白を落として保存し、BulkInsert は置き換えの前の文字で長さを照らして断る。サーバーの置き換えの表を写さないと合わせられないので、BulkInsert は半角の空白（U+0020）だけを落とす",
                    NotGenerated.Unguarded
                ),
                NotGenerated.Pending(
                    "smalldatetime-half-minute-boundary",
                    "smalldatetime の列で、秒が 29.999 以上 30.000 以下の値",
                    "SQL Server の datetime・smalldatetime の保存と WHERE を C# とそろえる",
                    "QuickER 版は分を切り上げ、EF Core 版は切り下げて保存する。生成 DbContext が datetime・smalldatetime に HasColumnType を出さないため、EF Core が datetime2 で送り、サーバーが変換する",
                    NotGenerated.Unguarded
                ),
                NotGenerated.Pending(
                    "datetime-rounds-past-max",
                    "datetime の列で、9999-12-31 23:59:59.997 を超え、丸めると範囲の外になる値",
                    "SQL Server の datetime・smalldatetime の保存と WHERE を C# とそろえる",
                    "QuickER 版は断り、EF Core 版は 23:59:59.997 で保存する。生成 DbContext が datetime・smalldatetime に HasColumnType を出さないため、EF Core が datetime2 で送り、サーバーが変換する",
                    NotGenerated.Unguarded
                ),
            ]
        );
}
