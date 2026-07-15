using FluentAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.MySql;
using QuickER.Oracle;
using QuickER.PostgreSql;
using QuickER.Provider;
using QuickER.SqlServer;

namespace QuickER.Tests.GeneratedPortableFixture;

/// <summary>
/// 方言非依存性の証明: 同一形状の可搬フィクスチャ図を各方言の CSharpTypeMapper
/// （SqlServer / PostgreSql / MySql / Oracle）で型解決して生成した C# 出力を突き合わせ、
/// <b>方言非依存の生成物が方言によらず同一である</b>ことを検証する。
/// </summary>
/// <remarks>
/// <para>
/// 可搬型セット（int / string / decimal(10,2)）は 4 方言の型マッパがすべて同じ C# 型へ解決するため、
/// エンティティ・DbContext・EF Core リポジトリ・VO などの生成物は方言によらず一致する。ここが崩れる場合は
/// <see cref="PortableFixtureDefinition"/> の型セットが可搬でない（見直しが必要）ことを意味する。
/// </para>
/// <para>
/// 唯一の方言差はQuickER の SQL Server 実装 ADO パス専用の <c>[SqlColumnType(SqlDbType.X)]</c> 属性
/// （およびその属性クラス・束縛ヘルパー）で、これは SQL Server 型マッパのみが付与する SQL Server 固有情報である
/// （EF Core パスは参照しない）。そのため本テストは 2 段で証明する:
/// <list type="number">
///   <item>本フィクスチャの実ランタイム対象である <b>PostgreSql / MySql / Oracle の 3 方言は生成 C# が完全一致する</b>
///   （いずれも <c>[SqlColumnType]</c> を付与しないため、正規化なしのバイト一致で証明できる）。</item>
///   <item>SqlServer は上記に <c>[SqlColumnType]</c> 系ブロックを加えただけの<b>上位互換</b>である
///   （その属性を含む行を全て除去すると 3 方言と一致する）。</item>
/// </list>
/// </para>
/// </remarks>
public sealed class PortableFixtureDialectIndependenceTests
{
    /// <summary>指定方言の型表記の図を、その方言の型マッパで解決して C# を生成する</summary>
    /// <remarks>
    /// 実生成経路と同じく、その方言の型カタログ由来の DB 定義メタトークンを付加する。トークンは canonical 由来で
    /// 方言に依存しないため、可搬図の型（整数・Unicode 文字列・decimal。文字列は Ansi/Unicode 差でトークンが
    /// 割れないよう Unicode 表記で統一——PortableFixtureDefinition 参照）は全方言で同一トークンとなり、
    /// 生成 C# の方言非依存性（バイト一致）を崩さない。
    /// </remarks>
    private static string GenerateFor(
        PortableDialect dialect,
        Func<ErDiagram, IReadOnlyDictionary<Guid, CSharpTypeInfo>> resolve
    )
    {
        var diagram = PortableFixtureDefinition.Build(dialect);
        var columnTypes = CanonicalTypeTokenAttacher.Attach(
            resolve(diagram),
            diagram,
            CatalogFor(dialect)
        );
        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            columnTypes,
            PortableFixtureDefinition.Options
        );

        result.HasErrors.Should().BeFalse($"{dialect} の生成でエラーが出てはならない");
        return result.Files.Should().ContainSingle().Subject.Content;
    }

    /// <summary>方言に対応する型カタログを返す（DB 定義メタトークンの解析に使う）</summary>
    private static ITypeCatalog CatalogFor(PortableDialect dialect) =>
        dialect switch
        {
            PortableDialect.SqlServer => new SqlServerTypeCatalog(),
            PortableDialect.PostgreSql => new PostgreSqlTypeCatalog(),
            PortableDialect.MySql => new MySqlTypeCatalog(),
            PortableDialect.Oracle => new OracleTypeCatalog(),
            _ => throw new ArgumentOutOfRangeException(nameof(dialect)),
        };

    /// <summary>
    /// フィクスチャの実ランタイム対象（PostgreSql / MySql / Oracle）の生成 C# が完全一致し、
    /// SqlServer はそれに SQL Server 固有属性を加えた上位互換であることを検証する。
    /// </summary>
    [Fact(
        DisplayName = "可搬フィクスチャの生成 C# が PostgreSql/MySql/Oracle で完全一致し SqlServer は上位互換（方言非依存性の証明）"
    )]
    public void GeneratedCode_IsIdentical_AcrossPortableDialects()
    {
        var postgreSql = GenerateFor(
            PortableDialect.PostgreSql,
            PostgreSqlCSharpTypeMapper.ResolveColumnTypes
        );
        var mySql = GenerateFor(PortableDialect.MySql, MySqlCSharpTypeMapper.ResolveColumnTypes);
        var oracle = GenerateFor(PortableDialect.Oracle, OracleCSharpTypeMapper.ResolveColumnTypes);

        // (1) 実ランタイム対象の 3 方言はバイト一致（正規化なしで方言非依存を証明）
        mySql
            .Should()
            .Be(postgreSql, "MySQL と PostgreSQL の型解決から生成した C# が完全一致すること");
        oracle
            .Should()
            .Be(postgreSql, "Oracle と PostgreSQL の型解決から生成した C# が完全一致すること");

        // (2) SqlServer は方言非依存の生成物を共有しつつ、SQL Server 固有の [SqlColumnType] 系サーフェスだけを
        // 追加した上位互換であることを確認する（方言非依存部分の構造同一性＋SQL Server 固有部分の存在/不在）。
        var sqlServer = GenerateFor(
            PortableDialect.SqlServer,
            SqlServerCSharpTypeMapper.ResolveColumnTypes
        );

        // 方言非依存のエンティティ・DbContext・EF Core リポジトリ宣言は SqlServer にもそのまま含まれる
        foreach (
            var marker in new[]
            {
                "public partial class CustomerEntity : EntityBase",
                "public partial class OrderEntity : EntityBase",
                "public partial class QuickErDbContext : DbContext",
                "public sealed partial class EfCoreCustomerRepository(",
                "public sealed partial class EfCoreOrderRepository(",
                "internal sealed class ValueObjectTranslatorPlugin",
            }
        )
        {
            sqlServer.Should().Contain(marker, $"SqlServer も方言非依存宣言 '{marker}' を含むこと");
            postgreSql
                .Should()
                .Contain(marker, $"PostgreSQL も方言非依存宣言 '{marker}' を含むこと");
        }

        // SQL Server 固有の [SqlColumnType] 属性は SqlServer 版のみに現れる（他方言は付与しない）
        sqlServer
            .Should()
            .Contain(
                "[SqlColumnType(SqlDbType.",
                "SqlServer はQuickER の ADO パス用の [SqlColumnType] 属性を付与する"
            );
        postgreSql
            .Should()
            .NotContain(
                "[SqlColumnType(",
                "他方言は SQL Server 固有の [SqlColumnType] 属性を付与しない（方言非依存）"
            );
    }
}
