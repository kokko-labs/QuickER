using System;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using Xunit;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// テーブル名・カラム名 → C# 識別子変換（<c>CSharpNameConverter</c>）の境界を検証する。
/// </summary>
/// <remarks>
/// <para>
/// <c>CSharpNameConverter</c> は internal（InternalsVisibleTo なし）のため、公開 API である
/// <see cref="CSharpCodeGenerationService"/> の生成出力越しに変換結果を観測する。観測点は
/// 衝突しにくい識別子として EditModel のバインディングプロパティ <c>Binding{プロパティ名}</c>
/// （既定オプションで各列に生成される）と、エンティティクラス宣言 <c>class {名前}Entity</c>、
/// コレクションナビゲーション（<c>ICollection&lt;…&gt; {複数形}</c>）を用いる。
/// </para>
/// <para>
/// 生成出力（コメント・<c>[Column("…")]</c>）には元のカラム名が原文のまま埋め込まれるため、
/// 変換前の原文を <c>NotContain</c> で確認するのは避け、変換後にのみ現れる派生識別子で照合する。
/// </para>
/// </remarks>
public sealed class CSharpNameConverterTests
{
    private static Column Column(string name, bool primaryKey = false) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            DataType = "int",
            IsPrimaryKey = primaryKey,
            IsNullable = false,
        };

    private static string Generate(ErDiagram diagram) =>
        new CSharpCodeGenerationService()
            .Generate(diagram, new CodeGenerationOptions { RootNamespace = "Sample.Domain" })
            .Files[0]
            .Content;

    [Fact(
        DisplayName = "カラム名 → プロパティ名変換の境界（スネークケース・数字始まり・記号区切り・頭字語・空）"
    )]
    public void ToPropertyName_ConvertsBoundaryColumnNames()
    {
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "sample",
                    Columns =
                    [
                        Column("sample_id", primaryKey: true),
                        // スネークケース → PascalCase 連結
                        Column("customer_full_name"),
                        // 数字始まり → 先頭に "_" を前置して有効識別子にする
                        Column("2fa_status"),
                        // 記号（ハイフン・ドット）は単語区切りとして扱う
                        Column("user-email.address"),
                        // 連続大文字の頭字語も 1 語ずつ Title Case へ正規化（HTTP → Http）
                        Column("orderHTTPCode"),
                        // 有効な単語が無い（記号のみ）場合は "Generated" にフォールバック
                        Column("***"),
                    ],
                },
            ],
        };

        var content = Generate(diagram);

        content.Should().Contain("BindingCustomerFullName");
        content.Should().Contain("Binding_2FaStatus");
        content.Should().Contain("BindingUserEmailAddress");
        content.Should().Contain("BindingOrderHttpCode");
        content.Should().NotContain("BindingOrderHTTPCode");
        content.Should().Contain("BindingGenerated");
    }

    [Fact(
        DisplayName = "C# キーワードと同綴りのカラム名でも @ エスケープされない（PascalCase 化で先頭が大文字になり衝突しないため）"
    )]
    public void ToPropertyName_KeywordSpelledColumn_IsNotAtEscaped()
    {
        // PascalCase 化は必ず先頭を大文字化するため、生成識別子が小文字綴りの C# キーワードと
        // 一致することは構造的に起きない。CSharpNameConverter はこの不変条件に基づき @ エスケープ
        // 機構を持たない（かつて存在した判定分岐は到達不能だったため削除済み）。その不変条件を固定する。
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "reserved",
                    Columns = [Column("reserved_id", primaryKey: true), Column("class")],
                },
            ],
        };

        var content = Generate(diagram);

        content.Should().Contain("BindingClass");
        content.Should().NotContain("@Class");
    }

    [Fact(
        DisplayName = "テーブル名 → エンティティ名の単数形化境界（ies→y・末尾 ss は保持・末尾 s 除去・不規則は非対応）"
    )]
    public void ToEntityClassName_SingularizesWithSimpleRules()
    {
        var diagram = new ErDiagram
        {
            Entities =
            [
                // "ies" → "y"（categories → category）
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "categories",
                    Columns = [Column("category_id", primaryKey: true)],
                },
                // 末尾 "ss" は除去しない（address のまま／Addres にしない）
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "address",
                    Columns = [Column("address_id", primaryKey: true)],
                },
                // 末尾 "s" を除去（orders → order）
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "orders",
                    Columns = [Column("order_id", primaryKey: true)],
                },
                // 不規則変化（people → person）は非対応＝そのまま People
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "people",
                    Columns = [Column("person_id", primaryKey: true)],
                },
            ],
        };

        var content = Generate(diagram);

        content.Should().Contain("class CategoryEntity");
        content.Should().Contain("class AddressEntity");
        content.Should().NotContain("class AddresEntity");
        content.Should().Contain("class OrderEntity");
        content.Should().Contain("class PeopleEntity");
    }

    [Fact(
        DisplayName = "単数形化の副作用で異なるテーブル名が同じエンティティ名に畳まれる（customer / customers → CustomerEntity）"
    )]
    public void ToEntityClassName_SingularizationCanCollideAcrossTables()
    {
        // 単数形化（末尾 s 除去）はテーブル名の表記ゆれを吸収する一方、綴りの違う 2 テーブルを同名クラスへ
        // 畳んでしまう。Entity は partial クラスのため黙って統合され、コンパイル不能な出力になる。
        // 変換規則側は現状維持とし、生成前検証（CSharpCodeGenerationService）がエラーで止める意図を固定する。
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "customer",
                    Columns = [Column("customer_id", primaryKey: true)],
                },
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "customers",
                    Columns = [Column("customer_id", primaryKey: true)],
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions { RootNamespace = "Sample.Domain" }
        );

        result.HasErrors.Should().BeTrue();
        result.Files.Should().BeEmpty();
        result
            .Diagnostics.Should()
            .Contain(diagnostic => diagnostic.Message.Contains("CustomerEntity"));
    }

    [Fact(
        DisplayName = "数字始まり・記号のみのテーブル名は、先頭に \"_\" 前置／\"Generated\" フォールバックでエンティティ名になる"
    )]
    public void ToEntityClassName_DigitLeadingAndSymbolOnlyTableNames()
    {
        var diagram = new ErDiagram
        {
            Entities =
            [
                // 数字始まり → "_" 前置（2020_sales → _2020SaleEntity。s 除去も効く）
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "2020_sales",
                    Columns = [Column("sale_id", primaryKey: true)],
                },
                // 記号のみ → 有効語なしで "Generated" フォールバック → GeneratedEntity
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "###",
                    Columns = [Column("id", primaryKey: true)],
                },
            ],
        };

        var content = Generate(diagram);

        content.Should().Contain("class _2020SaleEntity");
        content.Should().Contain("class GeneratedEntity");
    }

    [Fact(
        DisplayName = "ナビゲーション（コレクション側）の複数形化境界（company → Companies＝y→ies）"
    )]
    public void ToNavigationName_PluralizesCollectionNavigation()
    {
        var owner = Guid.NewGuid();
        var ownerPk = Guid.NewGuid();
        var company = Guid.NewGuid();
        var companyPk = Guid.NewGuid();
        var companyFk = Guid.NewGuid();

        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    Id = owner,
                    TableName = "owner",
                    Columns =
                    [
                        new Column
                        {
                            Id = ownerPk,
                            Name = "owner_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    ],
                },
                new Entity
                {
                    Id = company,
                    TableName = "company",
                    Columns =
                    [
                        new Column
                        {
                            Id = companyPk,
                            Name = "company_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = companyFk,
                            Name = "owner_id",
                            DataType = "int",
                            IsForeignKey = true,
                            IsNullable = false,
                        },
                    ],
                },
            ],
            Relationships =
            [
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    Type = RelationshipType.OneToMany,
                    SourceEntityId = owner,
                    TargetEntityId = company,
                    SourceColumnId = ownerPk,
                    TargetColumnId = companyFk,
                },
            ],
        };

        var content = Generate(diagram);

        // 親（owner）側にコレクションナビが生成され、子テーブル "company" が "Companies" へ複数形化される
        content.Should().Contain("ICollection<CompanyEntity> Companies");
    }
}
