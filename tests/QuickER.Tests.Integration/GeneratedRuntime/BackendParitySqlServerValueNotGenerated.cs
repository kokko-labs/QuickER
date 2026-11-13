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
                NotGenerated.Pending(
                    "other-sqlserver-types",
                    "datetime・smalldatetime・money・smallmoney の列への書き込み",
                    "きっかけ待ち（SQL Server の値の往復に、これらの型の列を図へ足すとき）",
                    "フィクスチャの図がこれらの型の列を持たない。SqlParameter の束縛と SqlBulkCopy が同じ値（datetime は 1/300 秒・smalldatetime は分・money は 4 桁への丸め）か同じ例外になることは、この区切りの測定で確かめたが、水準は足していない",
                    NotGenerated.Unguarded
                ),
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
            ]
        );
}
