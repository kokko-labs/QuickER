using System;
using System.Linq;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.CodeReverse.CSharp;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.PostgreSql;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// PostgreSQL の配列列（<c>integer[]</c> 等）を含む図の生成を検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 配列列はエンティティのプロパティとしては要素型の配列（<c>int[]</c> 等）で生成し、
/// EditModel と値オブジェクトには載せない。EditModel は 1 列＝1 テキスト入力の投影で配列を表す記法が無く
/// （載せると入力文字列を配列へ代入する形になりコンパイルできない）、値オブジェクトにすると等値が
/// <c>EqualityComparer&lt;T&gt;.Default</c>＝参照比較になって同じ内容の配列が等しくならないため。
/// </para>
/// <para>
/// これまで配列列は <c>string</c> へ落ちており、この組み合わせは一度も生成されたことが無い。
/// EditModel・VO の有無を掛けた実コンパイルは、決まった行（<c>CodegenCaseTests</c> の <c>compile-postgresql-array-*</c>）が固定する。
/// </para>
/// </remarks>
public class PostgreSqlArrayColumnGenerationTests
{
    /// <summary>配列列と、それを参照する名前付きクエリを持つ PostgreSQL の図を作る</summary>
    private static ErDiagram BuildDiagram()
    {
        var id = new Column
        {
            Name = "id",
            DataType = "integer",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var tags = new Column
        {
            Name = "tags",
            DataType = "integer[]",
            IsNullable = false,
        };
        var labels = new Column
        {
            Name = "labels",
            DataType = "varchar(20)[]",
            IsNullable = true,
        };
        var codes = new Column
        {
            Name = "codes",
            DataType = "bigint[]",
            IsNullable = true,
        };
        var name = new Column
        {
            Name = "name",
            DataType = "varchar(50)",
            IsNullable = false,
        };
        var entity = new Entity
        {
            TableName = "articles",
            Columns = { id, tags, labels, codes, name },
        };

        return new ErDiagram
        {
            TargetDbms = "postgresql",
            Entities = { entity },
            Queries =
            {
                // 配列でない列で絞り、射影に配列列を含める（配列列が生成モデルの各所を通る）
                new QueryDefinition
                {
                    EntityId = entity.Id,
                    Name = "FindByName",
                    Condition = "name = @name",
                    Parameters =
                    {
                        new QueryParameter { Name = "name", SourceColumnId = name.Id },
                    },
                    Returns = QueryReturnShape.Projection,
                    ResultTypeName = "ArticleSummary",
                    Fields =
                    {
                        new ProjectionField { Name = "Id", SourceColumnId = id.Id },
                        new ProjectionField { Name = "Tags", SourceColumnId = tags.Id },
                    },
                },
            },
        };
    }

    private static CodeGenerationResult Generate(CodeGenerationOptions options)
    {
        var diagram = BuildDiagram();
        return new CSharpCodeGenerationService().Generate(
            diagram,
            PostgreSqlCSharpTypeMapper.ResolveColumnTypes(diagram),
            options
        );
    }

    private static CodeGenerationOptions BaseOptions(bool editModels, bool valueObjects) =>
        new()
        {
            RootNamespace = "Sample.Domain",
            GenerateEditModels = editModels,
            GenerateMappers = editModels,
            GenerateValueObjects = valueObjects,
            GenerateRepositories = false,
            GenerateEfCoreRepositories = true,
        };

    /// <summary>
    /// インメモリ Repository を含めたときの、配列列の列コピーとシード値の形を検証する。
    /// </summary>
    /// <remarks>
    /// 併用した構成の生成コードがコンパイルできることは、決まった行（<c>CodegenCaseTests</c> の
    /// <c>compile-postgresql-array-columns-inmemory{,-vo}</c>）が持つ。
    /// インメモリの列コピーは配列を複製する（ストアと呼び出し側で同じ配列を共有すると、
    /// 呼び出し側の書き換えでストアの行が黙って変わる）。シード値も <c>default</c> の null ではなく
    /// 空配列にする（NOT NULL の配列列へ null を入れない）。どちらもこの経路でしか生成されない。
    /// </remarks>
    [Theory(
        DisplayName = "配列列はインメモリ Repository と併せると、列コピーが配列を複製しシード値は空配列"
    )]
    [InlineData(false)]
    [InlineData(true)]
    public void Generate_ArrayColumnsWithInMemory_CopiesAndSeedsArrays(bool valueObjects)
    {
        var options = BaseOptions(editModels: true, valueObjects: valueObjects) with
        {
            GenerateInMemoryRepositories = true,
        };
        var result = Generate(options);

        result.HasErrors.Should().BeFalse();

        var content = string.Join(Environment.NewLine, result.Files.Select(file => file.Content));

        // 列コピーは byte[] だけでなく配列全般を複製する
        content.Should().Contain("value is Array array ? array.Clone() : value");

        // NOT NULL の配列列のシード値は空配列（default の null ではない）
        content.Should().Contain("Array.Empty<int>()");
    }

    [Fact(DisplayName = "配列列は要素型の配列として Entity へ生成される")]
    public void Generate_ArrayColumns_EmitElementTypedArrays()
    {
        var content = string.Join(
            "\n",
            Generate(BaseOptions(editModels: false, valueObjects: false))
                .Files.Select(file => file.Content)
        );

        content.Should().Contain("public int[] Tags { get; set; } = Array.Empty<int>();");
        content.Should().Contain("public string[]? Labels { get; set; }");
        content.Should().Contain("public long[]? Codes { get; set; }");
    }

    [Fact(DisplayName = "配列列は EditModel に載らない（他の列は載る）")]
    public void Generate_ArrayColumns_AreNotInEditModel()
    {
        var content = string.Join(
            "\n",
            Generate(BaseOptions(editModels: true, valueObjects: false))
                .Files.Select(file => file.Content)
        );

        content.Should().Contain("BindingName");
        content.Should().NotContain("BindingTags");
        content.Should().NotContain("BindingLabels");
    }

    [Fact(DisplayName = "配列列は値オブジェクトにならない（他の列はなる）")]
    public void Generate_ArrayColumns_AreNotValueObjects()
    {
        var content = string.Join(
            "\n",
            Generate(BaseOptions(editModels: false, valueObjects: true))
                .Files.Select(file => file.Content)
        );

        content.Should().Contain("public sealed partial class NameValue");
        content.Should().NotContain("class TagsValue");
        content.Should().NotContain("class LabelsValue");
    }

    /// <summary>
    /// 配列列には方言中立の型トークン（<c>[DbColumnMeta]</c>）が刻まれず、
    /// C# リバースがそれを「トークンの無い列」として警告つきで扱うことを検証する。
    /// </summary>
    /// <remarks>
    /// 正規型に配列の種別が無いため、型カタログは <c>integer[]</c> を解析できずトークンが付かない。
    /// リバースは型を復元できない列をスキップして警告する既存の規則で扱う（例外にはならない）。
    /// トークンの付加は後処理（<c>CanonicalTypeTokenAttacher</c>）なので、生成は
    /// <see cref="DiagramCodeGenerator"/> 経由＝実際の生成経路で行う。
    /// </remarks>
    [Fact(DisplayName = "配列列は型トークンを持たず、C# リバースは警告つきで飛ばす")]
    public void Reverse_ArrayColumns_AreSkippedWithWarning()
    {
        var generated = DiagramCodeGenerator.Generate(
            new PostgreSqlCSharpTypeMapper(),
            new PostgreSqlTypeCatalog(),
            BuildDiagram(),
            BaseOptions(editModels: false, valueObjects: false)
        );

        var entityFile = generated
            .Files.Should()
            .ContainSingle(file => file.Content.Contains("class Article", StringComparison.Ordinal))
            .Subject;

        // 配列でない列にはトークンが付く（対照）
        entityFile.Content.Should().Contain("[DbColumnMeta(\"string(50)\")]");

        // 配列列は [Column] は持つがトークンは持たない
        entityFile.Content.Should().Contain("[Column(\"tags\")]");
        entityFile.Content.Should().NotContain("integer[]\")]");

        var reversed = new CSharpReverseParser().Parse(
            entityFile.Content,
            new PostgreSqlTypeCatalog(),
            IdentifierLengthLimit.Unlimited
        );

        // 例外にならず、配列でない列は復元される
        var entity = reversed.Entities.Should().ContainSingle().Subject;
        entity.Columns.Select(column => column.Name).Should().Equal("id", "name");

        // 飛ばした配列列は警告で名指しされる
        reversed
            .Warnings.Should()
            .Contain(warning => warning.Contains("tags", StringComparison.Ordinal));
        reversed
            .Warnings.Should()
            .Contain(warning => warning.Contains("labels", StringComparison.Ordinal));
    }

    /// <summary>
    /// 配列列を射影した名前付きクエリが、エラーなく生成されコンパイルできることを検証する
    /// （レビュー指摘: 名前付きクエリから配列列を参照した場合の確認）。
    /// </summary>
    [Fact(DisplayName = "名前付きクエリが配列列を射影してもコンパイルできる")]
    public void Generate_NamedQueryProjectingArrayColumn_Compiles()
    {
        var result = Generate(BaseOptions(editModels: true, valueObjects: false));

        result.HasErrors.Should().BeFalse();

        var content = string.Join("\n", result.Files.Select(file => file.Content));
        content.Should().Contain("ArticleSummary");
        content.Should().Contain("int[] Tags");
    }

    /// <summary>配列列を含む UNIQUE 制約を持つ図を作る（単独列と複合の両方）</summary>
    private static ErDiagram BuildDiagramWithArrayUniqueConstraints()
    {
        var diagram = BuildDiagram();
        var entity = diagram.Entities[0];
        var tags = entity.Columns.Single(column => column.Name == "tags");
        var name = entity.Columns.Single(column => column.Name == "name");

        entity.UniqueConstraints.Add(
            new UniqueConstraint { Name = "uq_articles_tags", ColumnIds = [tags.Id] }
        );

        // 名前を持たない複合制約（名指しでは合成名が出る）
        entity.UniqueConstraints.Add(new UniqueConstraint { ColumnIds = [name.Id, tags.Id] });

        return diagram;
    }

    private static CodeGenerationResult GenerateWithArrayUniqueConstraints(
        CodeGenerationOptions options
    )
    {
        var diagram = BuildDiagramWithArrayUniqueConstraints();

        return new CSharpCodeGenerationService().Generate(
            diagram,
            PostgreSqlCSharpTypeMapper.ResolveColumnTypes(diagram),
            options
        );
    }

    /// <summary>
    /// EditModel の DB 照合（<c>ValidateUniqueAsync</c>）が、EditModel に載らない配列列を「照合できない列」として渡すことを検証する
    /// （Repository 側の照合は配列列を含む制約も調べるが、照合用の Entity の配列列は既定値なので、その結果は捨てる）
    /// </summary>
    [Fact(DisplayName = "DB 照合は EditModel に載らない配列列を含む制約の結果を捨てる")]
    public void Generate_ValidateUnique_PassesArrayColumnsAsUnchecked()
    {
        var options = BaseOptions(editModels: true, valueObjects: false) with
        {
            GenerateRepositories = true,
            GenerateEfCoreRepositories = false,
        };
        var content = string.Join(
            "\n",
            GenerateWithArrayUniqueConstraints(options).Files.Select(file => file.Content)
        );

        content
            .Should()
            .Contain(
                "var uncheckedProperties = new List<string> { nameof(ArticleEntity.Tags), nameof(ArticleEntity.Labels), nameof(ArticleEntity.Codes) };"
            );
    }
}
