using QuickER.CodeGen.CSharp;
using QuickER.Model;

namespace QuickER.Tests.CodeGen.CSharp.CodegenConfig;

/// <summary>
/// コンパイルの行（<c>CodegenCases.Compile.cs</c> の <c>compile-*</c>）が共通に使う図（元の <c>GeneratedCodeCompilationTests</c> の図をそのまま写したもの）
/// </summary>
/// <remarks>
/// 中身を変えない：<c>customers.valid_from</c>（NOT NULL の値型の計算列）と <c>seq_no</c>（主キーでない NOT NULL の値型の採番列）は、
/// src/QuickER.CodeGen.CSharp/CLAUDE.md「Mapper の 4 分岐の Value」のガードが名指しで頼っている。
/// </remarks>
internal static class CompileDiagrams
{
    /// <summary>
    /// 複合主キー・1対1・1対多・自己参照・VO 対象カラム（int/string/decimal/bool/binary）・rowversion・
    /// 日本語テーブル名・NULL 許容混在を 1 つに収めた、コンパイルの行（compile-*）共通のフルカバレッジ ER 図
    /// </summary>
    /// <remarks>
    /// ここへ 1 列足すと全マトリクスケース（Split × VO × 方言 × EF Core × InMemory × リモート）と一度に交差する。
    /// フィクスチャは 1 個増やすたびに 1 万行前後のチェックイン生成物が増えるため、
    /// 「実行時の挙動を見たい」以外の交差はまずこの図へ足す。
    /// </remarks>
    public static ErDiagram FullCoverage()
    {
        var customer = Guid.NewGuid();
        var customerPk = Guid.NewGuid();

        // UNIQUE 制約の構成列（単一列・複合の 2 本を customers へ張る）
        var customerName = Guid.NewGuid();
        var customerCreatedAt = Guid.NewGuid();

        var order = Guid.NewGuid();
        var orderPk = Guid.NewGuid();
        var orderCustomerFk = Guid.NewGuid();

        // 明細行: (order_id, line_no) の複合主キー。order への FK は複合 PK の一部を兼ねる
        var orderLine = Guid.NewGuid();
        var orderLineOrderFk = Guid.NewGuid();
        var orderLineNo = Guid.NewGuid();

        // 1対1: customer <-> customer_profile
        var customerProfile = Guid.NewGuid();
        var customerProfilePk = Guid.NewGuid();
        var customerProfileFk = Guid.NewGuid();

        // 自己参照: category.parent_category_id -> category.category_id
        var category = Guid.NewGuid();
        var categoryPk = Guid.NewGuid();
        var categoryParentFk = Guid.NewGuid();

        // 日本語テーブル名・日本語カラム名
        var product = Guid.NewGuid();
        var productPk = Guid.NewGuid();

        return new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    Id = customer,
                    TableName = "customers",
                    Columns =
                    [
                        new Column
                        {
                            Id = customerPk,
                            Name = "customer_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = customerName,
                            Name = "name",
                            DataType = "nvarchar(100)",
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "balance",
                            DataType = "decimal(10,2)",
                            IsNullable = true,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "is_active",
                            DataType = "bit",
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "photo",
                            DataType = "varbinary(max)",
                            IsNullable = true,
                        },
                        new Column
                        {
                            Id = customerCreatedAt,
                            Name = "created_at",
                            DataType = "datetime2",
                            IsNullable = false,
                        },
                        // rowversion（store-generated・楽観排他の版）。VO・分割・InMemory・EF Core・リモートの
                        // 全マトリクスケースと交差させ、書き込み除外の方言ゲートと版ガードが同時に成立することを
                        // コンパイル水準で押さえる（PK ではない・1 エンティティに 1 本＝生成時診断に抵触しない）
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "row_ver",
                            DataType = "rowversion",
                            IsNullable = false,
                        },
                        // NOT NULL の計算列（値型）。SQL Server のテンポラルテーブルの期間列がこの形で取り込まれる。
                        // EditModel の確定値は値型なら常に Nullable<T> なので、Mapper の ApplyToEntity が
                        // 「入力があるときだけ代入」の内側で素の代入を出すと CS0266 になる——
                        // 行バージョン列・除外バイナリ列はいずれも byte[]（参照型）でこの経路を踏まないため、
                        // 非 NULL の値型の計算列だけが型検査でしか出ない崩れ方を掘り当てる
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "valid_from",
                            DataType = "datetime2",
                            IsNullable = false,
                            IsComputed = true,
                        },
                        // 主キーでない NOT NULL の採番列（値型）。計算列と同じく Mapper の「入力があるときだけ代入」が
                        // .Value で取り出す経路と、[IdentityColumn] の付与・EF Core の ValueGeneratedOnAddOrUpdate を
                        // 全マトリクスケースでコンパイル水準に通す
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "seq_no",
                            DataType = "bigint",
                            IsNullable = false,
                            IsIdentity = true,
                        },
                    ],
                    // 単一列・複合の 2 本（Entity の [UniqueConstraint] 属性と EditModel の制約テーブルの両方を発火させる）
                    UniqueConstraints =
                    {
                        new UniqueConstraint
                        {
                            Name = "UQ_customers_name",
                            ColumnIds = { customerName },
                        },
                        new UniqueConstraint { ColumnIds = { customerName, customerCreatedAt } },
                    },
                },
                new Entity
                {
                    Id = order,
                    TableName = "orders",
                    Columns =
                    [
                        new Column
                        {
                            Id = orderPk,
                            Name = "order_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = orderCustomerFk,
                            Name = "customer_id",
                            DataType = "int",
                            IsForeignKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "memo",
                            DataType = "nvarchar(200)",
                            IsNullable = true,
                        },
                    ],
                },
                new Entity
                {
                    Id = orderLine,
                    TableName = "order_lines",
                    Columns =
                    [
                        // 複合主キー: order_id（FK 兼務）+ line_no
                        new Column
                        {
                            Id = orderLineOrderFk,
                            Name = "order_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsForeignKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = orderLineNo,
                            Name = "line_no",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "quantity",
                            DataType = "int",
                            IsNullable = false,
                        },
                    ],
                },
                new Entity
                {
                    Id = customerProfile,
                    TableName = "customer_profiles",
                    Columns =
                    [
                        new Column
                        {
                            Id = customerProfilePk,
                            Name = "profile_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = customerProfileFk,
                            Name = "customer_id",
                            DataType = "int",
                            IsForeignKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "bio",
                            DataType = "nvarchar(500)",
                            IsNullable = true,
                        },
                    ],
                },
                new Entity
                {
                    Id = category,
                    TableName = "categories",
                    Columns =
                    [
                        new Column
                        {
                            Id = categoryPk,
                            Name = "category_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = categoryParentFk,
                            Name = "parent_category_id",
                            DataType = "int",
                            IsForeignKey = true,
                            IsNullable = true,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "name",
                            DataType = "nvarchar(50)",
                            IsNullable = false,
                        },
                    ],
                },
                new Entity
                {
                    Id = product,
                    TableName = "商品",
                    Columns =
                    [
                        new Column
                        {
                            Id = productPk,
                            Name = "商品ID",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "商品名",
                            DataType = "nvarchar(100)",
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "単価",
                            DataType = "decimal(12,2)",
                            IsNullable = true,
                        },
                    ],
                },
            ],
            Relationships =
            [
                // 1対多: customers -> orders
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    Type = RelationshipType.OneToMany,
                    SourceEntityId = customer,
                    TargetEntityId = order,
                    ColumnPairs = [new(customerPk, orderCustomerFk)],
                },
                // 1対多: orders -> order_lines（子は複合 PK の一部が FK）
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    Type = RelationshipType.OneToMany,
                    SourceEntityId = order,
                    TargetEntityId = orderLine,
                    ColumnPairs = [new(orderPk, orderLineOrderFk)],
                },
                // 1対1: customers <-> customer_profiles
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    Type = RelationshipType.OneToOne,
                    SourceEntityId = customer,
                    TargetEntityId = customerProfile,
                    ColumnPairs = [new(customerPk, customerProfileFk)],
                },
                // 自己参照: categories -> categories
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    Type = RelationshipType.OneToMany,
                    SourceEntityId = category,
                    TargetEntityId = category,
                    ColumnPairs = [new(categoryPk, categoryParentFk)],
                },
            ],
        };
    }

    /// <summary>
    /// <see cref="FullCoverage"/> に、文字列の主キーの表（tenants）・その列を列名の違う外部キーで持つ子（tenant_members）・
    /// 2 つの列への名前付きクエリ（完全一致と部分一致）を足した図。GuidKey のケースが空振りにならない（主キーがすべて int の
    /// <see cref="FullCoverage"/> では GuidKey の値オブジェクトが 1 つも出ない）ために使う
    /// </summary>
    public static ErDiagram StringKey()
    {
        var diagram = FullCoverage();
        var tenantCode = new Column
        {
            Name = "tenant_code",
            DataType = "nvarchar(36)",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var tenants = new Entity
        {
            TableName = "tenants",
            Columns =
            {
                tenantCode,
                new Column
                {
                    Name = "label",
                    DataType = "nvarchar(50)",
                    IsNullable = false,
                },
            },
        };
        var ownerTenantCode = new Column
        {
            Name = "owner_tenant_code",
            DataType = "nvarchar(36)",
            IsForeignKey = true,
            IsNullable = false,
        };
        var members = new Entity
        {
            TableName = "tenant_members",
            Columns =
            {
                new Column
                {
                    Name = "member_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                ownerTenantCode,
            },
        };
        diagram.Entities.Add(tenants);
        diagram.Entities.Add(members);
        diagram.Relationships.Add(
            new Relationship
            {
                Type = RelationshipType.OneToMany,
                SourceEntityId = tenants.Id,
                TargetEntityId = members.Id,
                ColumnPairs = [new(tenantCode.Id, ownerTenantCode.Id)],
            }
        );

        foreach (
            var (entity, name, condition) in new[]
            {
                (tenants, "CodeEquals", "tenant_code = 'ab'"),
                (tenants, "CodeContains", "tenant_code CONTAINS 'ab'"),
                (members, "OwnerCodeEquals", "owner_tenant_code = 'ab'"),
                (members, "OwnerCodeStartsWith", "owner_tenant_code STARTSWITH 'ab'"),
            }
        )
        {
            diagram.Queries.Add(
                new QueryDefinition
                {
                    EntityId = entity.Id,
                    Name = name,
                    Returns = QueryReturnShape.List,
                    Condition = condition,
                }
            );
        }

        return diagram;
    }

    /// <summary>GuidKey のケースのオプション（値オブジェクトと、名前付きクエリを出すための Repository を生成する）</summary>
    public static CodeGenerationOptions GuidKeyOptions() =>
        new()
        {
            RootNamespace = "Sample.Domain",
            GenerateValueObjects = true,
            GenerateRepositories = true,
            UseGuidKeyForStringPrimaryKey = true,
        };
}
