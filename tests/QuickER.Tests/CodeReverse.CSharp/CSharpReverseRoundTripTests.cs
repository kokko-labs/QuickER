using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.CodeReverse.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.SqlServer;
using QuickER.Tests.GeneratedFixture;

namespace QuickER.Tests.CodeReverse.CSharp;

/// <summary>
/// ラウンドトリップ（本命）: ER 図 → コード生成（IncludeDataAnnotations ON）→ リバース解析 → 元の図と一致を検証する。
/// </summary>
/// <remarks>
/// 一致対象はテーブル/列名・DataType・PK・NULL・説明・UNIQUE 制約（名前と構成列）・
/// リレーション端点名と Type・外部キー制約名と参照アクション。レイアウト・クエリは対象外（コードに存在しないため）。
/// UNIQUE 制約名は「図が名前を持たない制約でも生成コードには合成名が刻まれる」ため、比較は実効名
/// （<c>Name</c> ?? <see cref="UniqueConstraint.SynthesizeName"/>）で行う。
/// </remarks>
public class CSharpReverseRoundTripTests
{
    /// <summary>比較用の列射影（名前・型・PK・NULL 許容・説明）</summary>
    private sealed record ColumnProjection(
        string Name,
        string DataType,
        bool IsPrimaryKey,
        bool IsNullable,
        string Description
    );

    /// <summary>比較用の UNIQUE 制約射影（実効制約名・宣言順の構成列名）</summary>
    private sealed record UniqueConstraintProjection(
        string EffectiveName,
        IReadOnlyList<string> ColumnNames
    );

    /// <summary>比較用のリレーション射影（端点テーブル名・列名・種類・外部キーメタデータ）</summary>
    private sealed record RelationshipProjection(
        string SourceTable,
        string TargetTable,
        RelationshipType Type,
        string? SourceColumn,
        string? TargetColumn,
        string? ConstraintName,
        ForeignKeyReferentialAction OnDelete,
        ForeignKeyReferentialAction OnUpdate
    );

    /// <summary>既存の実行時テスト用フィクスチャ図（VO・1対多・1対1）を往復させて一致を検証する</summary>
    [Fact(DisplayName = "既存フィクスチャ図（VO・1対多・1対1）が往復で一致する")]
    public void RoundTrip_GeneratedFixtureDiagram_Matches()
    {
        var original = GeneratedFixtureDefinition.Build();

        AssertRoundTrips(original, GeneratedFixtureDefinition.Options);
    }

    /// <summary>説明付き・多方言型（string(n)/ansistring/decimal/int32/int64/boolean/date/datetime/guid）と VO を含む図を往復させる</summary>
    [Fact(DisplayName = "説明付き・多方言型・VO を含む図が往復で一致する")]
    public void RoundTrip_RichTypesAndDescriptions_Matches()
    {
        var productId = Guid.NewGuid();
        var productPk = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var categoryPk = Guid.NewGuid();

        var category = new Entity
        {
            Id = categoryId,
            TableName = "categories",
            Description = "Product categories",
            Columns =
            {
                new Column
                {
                    Id = categoryPk,
                    Name = "category_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                    Description = "Category identifier",
                },
                new Column
                {
                    Name = "title",
                    DataType = "nvarchar(100)",
                    IsNullable = false,
                    Description = "Display title",
                },
            },
        };

        var product = new Entity
        {
            Id = productId,
            TableName = "products",
            Description = "Sellable products",
            Columns =
            {
                new Column
                {
                    Id = productPk,
                    Name = "product_id",
                    DataType = "bigint",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "category_id",
                    DataType = "int",
                    IsForeignKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "code",
                    DataType = "varchar(50)",
                    IsNullable = false,
                    Description = "SKU code",
                },
                new Column
                {
                    Name = "price",
                    DataType = "decimal(10,2)",
                    IsNullable = false,
                },
                new Column
                {
                    Name = "in_stock",
                    DataType = "bit",
                    IsNullable = false,
                },
                new Column
                {
                    Name = "released_on",
                    DataType = "date",
                    IsNullable = true,
                },
                new Column
                {
                    Name = "updated_at",
                    DataType = "datetime2",
                    IsNullable = true,
                },
                new Column
                {
                    Name = "public_ref",
                    DataType = "uniqueidentifier",
                    IsNullable = true,
                    Description = "Public reference GUID",
                },
            },
        };

        var diagram = new ErDiagram
        {
            Entities = { category, product },
            Relationships =
            {
                new Relationship
                {
                    Type = RelationshipType.OneToMany,
                    SourceEntityId = categoryId,
                    TargetEntityId = productId,
                    ColumnPairs = [new(categoryPk, product.Columns[1].Id)],
                    OnDelete = ForeignKeyReferentialAction.Cascade,
                    ConstraintName = "FK_products_categories",
                },
            },
        };

        var options = new CodeGenerationOptions
        {
            RootNamespace = "QuickER.Tests.ReverseRoundTrip",
            GenerateValueObjects = true,
            SplitFilesByCategory = false,
        };

        AssertRoundTrips(diagram, options);
    }

    /// <summary>
    /// VO なし（GenerateValueObjects=false）で、非 NULL の値型非 PK 列（int/decimal NOT NULL）と
    /// NULL 許容値型列（int NULL）を含む図が往復する。値型 NOT NULL 非 PK 列には <c>[Required]</c> が
    /// 付かない（参照型のみに出るため）ので、旧 <c>[Required]</c> ベース判定では NULL 許容と誤復元される。
    /// プロパティ型構文（<c>?</c> の有無）ベース判定なら正しく往復する。
    /// </summary>
    [Fact(DisplayName = "VO なし・値型 NOT NULL 非 PK 列を含む図が往復で NULL 許容性を保つ")]
    public void RoundTrip_ValueTypeNotNullColumns_WithoutValueObjects_Matches()
    {
        var orderId = Guid.NewGuid();
        var orderPk = Guid.NewGuid();

        var order = new Entity
        {
            Id = orderId,
            TableName = "orders",
            Description = "Sales orders",
            Columns =
            {
                new Column
                {
                    Id = orderPk,
                    Name = "order_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                // 値型 NOT NULL 非 PK 列（[Required] は付かない＝旧判定の欠陥ケース）
                new Column
                {
                    Name = "amount",
                    DataType = "int",
                    IsNullable = false,
                    Description = "Item count",
                },
                new Column
                {
                    Name = "total",
                    DataType = "decimal(10,2)",
                    IsNullable = false,
                    Description = "Order total",
                },
                // 値型 NULL 許容列
                new Column
                {
                    Name = "discount",
                    DataType = "int",
                    IsNullable = true,
                },
                // 参照型 NOT NULL 列（[Required] が付く）
                new Column
                {
                    Name = "code",
                    DataType = "varchar(50)",
                    IsNullable = false,
                },
                // 参照型 NULL 許容列
                new Column
                {
                    Name = "note",
                    DataType = "nvarchar(200)",
                    IsNullable = true,
                },
            },
        };

        var diagram = new ErDiagram { Entities = { order } };

        var options = new CodeGenerationOptions
        {
            RootNamespace = "QuickER.Tests.ReverseRoundTrip",
            GenerateValueObjects = false,
            SplitFilesByCategory = false,
        };

        AssertRoundTrips(diagram, options);
    }

    /// <summary>
    /// UNIQUE 制約（単一・複合／実名・名前なし）と外部キーメタデータ（制約名・ON DELETE / ON UPDATE）を
    /// 持つ図が往復する。制約は <c>[UniqueConstraint]</c>、参照アクションは <c>[NavigationReference]</c> の
    /// 名前付き引数として往復する。
    /// </summary>
    [Fact(DisplayName = "UNIQUE 制約と外部キーメタデータを含む図が往復で一致する")]
    public void RoundTrip_UniqueConstraintsAndForeignKeyMetadata_Matches()
    {
        var tenantId = Guid.NewGuid();
        var tenantPk = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var userPk = Guid.NewGuid();
        var userTenantFk = Guid.NewGuid();
        var userEmailCol = Guid.NewGuid();
        var userLoginCol = Guid.NewGuid();

        var tenant = new Entity
        {
            Id = tenantId,
            TableName = "tenants",
            Columns =
            {
                new Column
                {
                    Id = tenantPk,
                    Name = "tenant_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "code",
                    DataType = "varchar(20)",
                    IsNullable = false,
                },
            },
        };

        // 単一列（実名あり）と複合（名前なし＝生成時に合成名）の 2 つの UNIQUE 制約を持つ
        tenant.UniqueConstraints.Add(
            new UniqueConstraint { Name = "UQ_tenants_code", ColumnIds = { tenant.Columns[1].Id } }
        );

        var user = new Entity
        {
            Id = userId,
            TableName = "users",
            Columns =
            {
                new Column
                {
                    Id = userPk,
                    Name = "user_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Id = userTenantFk,
                    Name = "tenant_id",
                    DataType = "int",
                    IsForeignKey = true,
                    IsNullable = true,
                },
                new Column
                {
                    Id = userEmailCol,
                    Name = "email",
                    DataType = "nvarchar(200)",
                    IsNullable = false,
                },
                new Column
                {
                    Id = userLoginCol,
                    Name = "login_name",
                    DataType = "varchar(50)",
                    IsNullable = false,
                },
            },
        };

        user.UniqueConstraints.Add(
            new UniqueConstraint { Name = "UQ_users_email", ColumnIds = { userEmailCol } }
        );
        // 名前なし複合制約（生成コードには合成名 UQ_users_tenant_id_login_name が刻まれる）
        user.UniqueConstraints.Add(
            new UniqueConstraint { ColumnIds = { userTenantFk, userLoginCol } }
        );

        var diagram = new ErDiagram
        {
            Entities = { tenant, user },
            Relationships =
            {
                new Relationship
                {
                    Type = RelationshipType.OneToMany,
                    SourceEntityId = tenantId,
                    TargetEntityId = userId,
                    ColumnPairs = [new(tenantPk, userTenantFk)],
                    ConstraintName = "FK_users_tenants",
                    OnDelete = ForeignKeyReferentialAction.SetNull,
                    OnUpdate = ForeignKeyReferentialAction.Cascade,
                },
            },
        };

        var options = new CodeGenerationOptions
        {
            RootNamespace = "QuickER.Tests.ReverseRoundTrip",
            GenerateValueObjects = false,
            SplitFilesByCategory = false,
        };

        AssertRoundTrips(diagram, options);
    }

    /// <summary>図を生成 → リバース解析 → 元の図と（比較対象の範囲で）一致することを検証する</summary>
    private static void AssertRoundTrips(ErDiagram original, CodeGenerationOptions options)
    {
        var provider = new SqlServerProvider();
        var generation = DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            original,
            options
        );
        generation.HasErrors.Should().BeFalse("フィクスチャ図の生成でエラーが出てはならない");

        var source = generation.Files.Single().Content;

        var reversed = new CSharpReverseParser().Parse(source, provider.TypeCatalog);

        // テーブル名の集合が一致する
        reversed
            .Entities.Select(entity => entity.TableName)
            .Should()
            .BeEquivalentTo(original.Entities.Select(entity => entity.TableName));

        // 各テーブルの説明と列（名前・型・PK・NULL・説明）が順序込みで一致する
        foreach (var originalEntity in original.Entities)
        {
            var reversedEntity = reversed.Entities.Single(entity =>
                entity.TableName == originalEntity.TableName
            );

            reversedEntity.Description.Should().Be(originalEntity.Description);
            ProjectColumns(reversedEntity)
                .Should()
                .Equal(ProjectColumns(originalEntity), "列の定義が往復で一致する");
            ProjectUniqueConstraints(reversedEntity)
                .Should()
                .BeEquivalentTo(
                    ProjectUniqueConstraints(originalEntity),
                    "UNIQUE 制約（実効名・構成列）が往復で一致する"
                );
        }

        // リレーション端点名と Type が一致する（順序非依存）
        ProjectRelationships(reversed.Entities, reversed.Relationships)
            .Should()
            .BeEquivalentTo(ProjectRelationships(original.Entities, original.Relationships));
    }

    /// <summary>UNIQUE 制約を「実効名（未設定なら合成名）＋宣言順の構成列名」へ射影する</summary>
    private static List<UniqueConstraintProjection> ProjectUniqueConstraints(Entity entity)
    {
        var columnNamesById = entity.Columns.ToDictionary(
            column => column.Id,
            column => column.Name
        );

        return entity
            .UniqueConstraints.Select(constraint =>
            {
                var columnNames = constraint
                    .ColumnIds.Select(id =>
                        columnNamesById.TryGetValue(id, out var name) ? name : id.ToString()
                    )
                    .ToList();

                return new UniqueConstraintProjection(
                    constraint.Name
                        ?? UniqueConstraint.SynthesizeName(entity.TableName, columnNames),
                    columnNames
                );
            })
            .ToList();
    }

    private static List<ColumnProjection> ProjectColumns(Entity entity) =>
        entity
            .Columns.Select(column => new ColumnProjection(
                column.Name,
                column.DataType,
                column.IsPrimaryKey,
                column.IsNullable,
                column.Description
            ))
            .ToList();

    private static List<RelationshipProjection> ProjectRelationships(
        IReadOnlyList<Entity> entities,
        IReadOnlyList<Relationship> relationships
    )
    {
        var entityById = entities.ToDictionary(entity => entity.Id);
        var columnNameById = entities
            .SelectMany(entity => entity.Columns)
            .ToDictionary(column => column.Id, column => column.Name);

        return relationships
            .Select(relationship => new RelationshipProjection(
                entityById[relationship.SourceEntityId].TableName,
                entityById[relationship.TargetEntityId].TableName,
                relationship.Type,
                ResolveName(
                    columnNameById,
                    relationship.ColumnPairs.FirstOrDefault()?.SourceColumnId
                ),
                ResolveName(
                    columnNameById,
                    relationship.ColumnPairs.FirstOrDefault()?.TargetColumnId
                ),
                relationship.ConstraintName,
                relationship.OnDelete,
                relationship.OnUpdate
            ))
            .ToList();
    }

    private static string? ResolveName(IReadOnlyDictionary<Guid, string> names, Guid? id) =>
        id is { } value && names.TryGetValue(value, out var name) ? name : null;
}
