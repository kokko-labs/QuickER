using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Sqlite;
using QuickER.SqlServer;
using Xunit;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// マルチターゲット生成（sqlserver + sqlite）で共有 Entity の C# 型が方言間で食い違うときの
/// <c>MultiDialectTypeReconciler.DiagnoseTypeMismatches</c> のエラー分岐を検証する。
/// </summary>
/// <remarks>
/// <para>
/// 型解決の整合ケース（可搬型で診断エラーが出ない）は既存の
/// <see cref="MultiTargetRepositoryGenerationTests"/> がカバー済みのため、本クラスはエラー系の
/// 個別分岐（型名不一致・参照/値区分不一致・複数列・辞書欠落によるスキップ・単一方言ガード）に集中する。
/// </para>
/// <para>
/// <c>MultiDialectTypeReconciler</c> は internal（InternalsVisibleTo なし）のため、公開 API である
/// <see cref="CSharpCodeGenerationService.Generate(ErDiagram, IReadOnlyDictionary{Guid, CSharpTypeInfo}, IReadOnlyDictionary{string, IReadOnlyDictionary{Guid, CSharpTypeInfo}}, CodeGenerationOptions)"/>
/// の診断結果（<see cref="GenerationDiagnostic"/>）越しに分岐を観測する。
/// </para>
/// </remarks>
public sealed class MultiDialectTypeReconcilerTests
{
    // 型を突き合わせる材料を安定させるため、列 ID を固定した小さな図を組む。
    // customer_id/age = 値型（int）、name = 参照型（string）で、両方の不一致条件を作れるようにする。
    private static readonly Guid CustomerId = Guid.NewGuid();
    private static readonly Guid Name = Guid.NewGuid();
    private static readonly Guid Age = Guid.NewGuid();

    /// <summary>int（値型）・varchar（参照型）・int の 3 列を持つ単一エンティティの可搬図</summary>
    private static ErDiagram BuildDiagram() =>
        new()
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "customers",
                    Columns =
                    [
                        new Column
                        {
                            Id = CustomerId,
                            Name = "customer_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Name,
                            Name = "name",
                            DataType = "varchar(50)",
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Age,
                            Name = "age",
                            DataType = "int",
                            IsNullable = false,
                        },
                    ],
                },
            ],
        };

    /// <summary>sqlite の解決辞書を可変辞書として複製する（人工的な食い違わせ用）</summary>
    private static Dictionary<Guid, CSharpTypeInfo> CloneSqlite(ErDiagram diagram) =>
        new(SqliteCSharpTypeMapper.ResolveColumnTypes(diagram));

    /// <summary>sqlserver（主辞書）＋ sqlite の 2 方言を大小無視キーで束ねる</summary>
    private static Dictionary<string, IReadOnlyDictionary<Guid, CSharpTypeInfo>> ByDialect(
        IReadOnlyDictionary<Guid, CSharpTypeInfo> sqlServer,
        IReadOnlyDictionary<Guid, CSharpTypeInfo> sqlite
    ) => new(StringComparer.OrdinalIgnoreCase) { ["sqlserver"] = sqlServer, ["sqlite"] = sqlite };

    private static CodeGenerationResult Generate(
        ErDiagram diagram,
        IReadOnlyDictionary<Guid, CSharpTypeInfo> primary,
        IReadOnlyDictionary<string, IReadOnlyDictionary<Guid, CSharpTypeInfo>> byDialect,
        params string[] dialects
    ) =>
        new CSharpCodeGenerationService().Generate(
            diagram,
            primary,
            byDialect,
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                RepositoryDialects = dialects,
            }
        );

    private static IEnumerable<GenerationDiagnostic> Errors(CodeGenerationResult result) =>
        result.Diagnostics.Where(d => d.Severity == GenerationDiagnosticSeverity.Error);

    [Fact(
        DisplayName = "方言間で型名だけが食い違う（int と long／どちらも値型）と診断エラーになる"
    )]
    public void DiagnoseTypeMismatches_TypeNameOnly_ReturnsErrorDiagnostic()
    {
        var diagram = BuildDiagram();
        var primary = SqlServerCSharpTypeMapper.ResolveColumnTypes(diagram);

        // 参照/値区分は一致（どちらも値型）させ、型名だけを int → long にずらす
        var sqlite = CloneSqlite(diagram);
        sqlite[CustomerId] = new CSharpTypeInfo { TypeName = "long", IsReferenceType = false };

        var result = Generate(diagram, primary, ByDialect(primary, sqlite), "sqlserver", "sqlite");

        result.HasErrors.Should().BeTrue();
        Errors(result)
            .Should()
            .Contain(d =>
                // 型名不一致であることを、表示言語に依存しないトークン（列名・食い違う型名・方言名）で確認する
                d.Message.Contains("customer_id")
                && d.Message.Contains("long")
                && d.Message.Contains("sqlite")
                && d.Message.Contains("sqlserver")
            );
    }

    [Fact(
        DisplayName = "型名は同じでも参照/値区分が食い違う（string の参照/値）と診断エラーになる"
    )]
    public void DiagnoseTypeMismatches_ReferenceKindOnly_ReturnsErrorDiagnostic()
    {
        var diagram = BuildDiagram();
        var primary = SqlServerCSharpTypeMapper.ResolveColumnTypes(diagram);

        // name は sqlserver では string（参照型）。型名を同じ string のまま、参照区分だけ値型へずらす
        var sqlite = CloneSqlite(diagram);
        sqlite[Name] = new CSharpTypeInfo { TypeName = "string", IsReferenceType = false };

        var result = Generate(diagram, primary, ByDialect(primary, sqlite), "sqlserver", "sqlite");

        result.HasErrors.Should().BeTrue();
        Errors(result)
            .Should()
            .Contain(d =>
                d.Message.Contains("name")
                && d.Message.Contains("sqlite")
                && d.Message.Contains("sqlserver")
            );
    }

    [Fact(DisplayName = "複数列が食い違うと列ごとに診断エラーが出る（短絡せず全件報告）")]
    public void DiagnoseTypeMismatches_MultipleColumns_ReportsErrorPerColumn()
    {
        var diagram = BuildDiagram();
        var primary = SqlServerCSharpTypeMapper.ResolveColumnTypes(diagram);

        // customer_id と age の 2 列を人工的に食い違わせる
        var sqlite = CloneSqlite(diagram);
        sqlite[CustomerId] = new CSharpTypeInfo { TypeName = "long", IsReferenceType = false };
        sqlite[Age] = new CSharpTypeInfo { TypeName = "short", IsReferenceType = false };

        var result = Generate(diagram, primary, ByDialect(primary, sqlite), "sqlserver", "sqlite");

        result.HasErrors.Should().BeTrue();

        // マルチターゲット（sqlserver + sqlite・EF Core なし）では他のエラー要因がないため、
        // エラー診断＝型不一致の列数（2 列）に一致する
        var errors = Errors(result).ToList();
        errors.Should().HaveCount(2);
        errors.Should().Contain(d => d.Message.Contains("customer_id"));
        errors.Should().Contain(d => d.Message.Contains("age"));
    }

    [Fact(
        DisplayName = "対象方言に列が無い（辞書に列 ID なし）と、その列はスキップされ誤検知しない"
    )]
    public void DiagnoseTypeMismatches_ColumnMissingInDialect_SkipsWithoutError()
    {
        var diagram = BuildDiagram();
        var primary = SqlServerCSharpTypeMapper.ResolveColumnTypes(diagram);

        // sqlite 辞書から age を取り除く（主辞書には存在するが対象方言に無い列）→ 比較スキップ
        var sqlite = CloneSqlite(diagram);
        sqlite.Remove(Age);

        var result = Generate(diagram, primary, ByDialect(primary, sqlite), "sqlserver", "sqlite");

        // 残る customer_id・name は方言間で一致するため、欠落列のスキップだけならエラーは出ない
        result.HasErrors.Should().BeFalse();
    }

    [Fact(
        DisplayName = "実効方言が 1 つなら、非実効方言の辞書が食い違っていても診断エラーは出ない（ガード＋実効方言フィルタ）"
    )]
    public void DiagnoseTypeMismatches_SingleEffectiveDialect_IgnoresNonEffectiveDictionary()
    {
        var diagram = BuildDiagram();

        // 主辞書＝図の方言 sqlite（未改変）。byDialect には食い違わせた sqlserver 辞書も入れておく
        var sqlite = SqliteCSharpTypeMapper.ResolveColumnTypes(diagram);
        var tamperedSqlServer = new Dictionary<Guid, CSharpTypeInfo>(
            SqlServerCSharpTypeMapper.ResolveColumnTypes(diagram)
        )
        {
            [CustomerId] = new CSharpTypeInfo { TypeName = "long", IsReferenceType = false },
        };

        // 実効方言は sqlite のみ。突き合わせ対象は sqlite 1 件（< 2）となり早期 return するうえ、
        // 非実効の sqlserver 辞書は実効方言フィルタで除外されるため食い違いは評価されない
        var result = Generate(diagram, sqlite, ByDialect(tamperedSqlServer, sqlite), "sqlite");

        result.HasErrors.Should().BeFalse();
    }
}
