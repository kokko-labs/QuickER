using QuickER.CodeGen.CSharp;
using QuickER.CodeReverse.CSharp;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider.SqlServer;
using QuickER.Tests.GeneratedFixture;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Gui.Services.FormatRoundTrip;

/// <summary>C# リバースの往復のテストから移した形の行</summary>
internal static partial class FormatShapeRows
{
    private static IEnumerable<ScenarioRow<FormatShape>> CSharpReverseRows() =>
        [
            // CSharpReverseRoundTripTests
            CSharpRoundTrip(
                "csharp-generated-fixture",
                "既存フィクスチャ図（VO・1対多・1対1）が往復で一致する",
                GeneratedFixtureDefinition.Build,
                GeneratedFixtureDefinition.Options
            ),
            CSharpRoundTrip(
                "csharp-rich-types-descriptions",
                "説明付き・多方言型・VO を含む図が往復で一致する",
                CSharpRichTypesDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "QuickER.Tests.ReverseRoundTrip",
                    GenerateValueObjects = true,
                    SplitFilesByCategory = false,
                }
            ),
            CSharpRoundTrip(
                "csharp-value-type-not-null",
                "VO なし・値型 NOT NULL 非 PK 列を含む図が往復で NULL 許容性を保つ",
                CSharpValueTypeNotNullDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "QuickER.Tests.ReverseRoundTrip",
                    GenerateValueObjects = false,
                    SplitFilesByCategory = false,
                }
            ),
            CSharpRoundTrip(
                "csharp-computed-column",
                "計算列が [ComputedColumn] として往復で一致する",
                CSharpComputedDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Computed",
                    OutputFileName = "Computed.g.cs",
                    GenerateRepositories = true,
                    IncludeDataAnnotations = true,
                }
            ),
            CSharpRoundTrip(
                "csharp-identity-column",
                "主キーでない採番列が [IdentityColumn] として往復で一致する",
                CSharpIdentityDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Identity",
                    OutputFileName = "Identity.g.cs",
                    GenerateRepositories = true,
                    IncludeDataAnnotations = true,
                }
            ),
            Shape("csharp-identity-primary-key", "主キーの採番列はリバースで普通の列に戻る")
                .Build(CSharpIdentityPrimaryKeyDiagram)
                .Settings(
                    new FormatSettings(
                        CSharpOptions: new CodeGenerationOptions
                        {
                            RootNamespace = "Sample.IdentityKey",
                            OutputFileName = "IdentityKey.g.cs",
                            GenerateRepositories = true,
                            IncludeDataAnnotations = true,
                        },
                        CSharpDialect: "sqlserver"
                    )
                )
                .Expect(
                    e =>
                        e.Imported.Count("Structure table *", 1)
                            .Count("Structure column *", 1)
                            .None("IdentityKey *")
                            .None("IdentityNonKey *"),
                    CSharpReverseAdapter.Value
                ),
            CSharpRoundTrip(
                "csharp-unique-and-foreign-key-metadata",
                "UNIQUE 制約と外部キーメタデータを含む図が往復で一致する",
                CSharpUniqueAndForeignKeyDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "QuickER.Tests.ReverseRoundTrip",
                    GenerateValueObjects = false,
                    SplitFilesByCategory = false,
                }
            ),
            // CSharpReversePrimaryKeyOrderTests
            Shape(
                    "csharp-reordered-primary-key",
                    "宣言順と食い違う主キーの順序は、生成 → リバースで戻る"
                )
                .Build(() => CSharpPrimaryKeyOrderDiagram(["order_no", "tenant_id"]))
                .Settings(
                    new FormatSettings(
                        CSharpOptions: new CodeGenerationOptions
                        {
                            RootNamespace = "QuickER.Tests.ReversePrimaryKeyOrder",
                            GenerateValueObjects = false,
                            SplitFilesByCategory = false,
                        },
                        CSharpDialect: "sqlserver"
                    )
                )
                .Expect(
                    e =>
                    {
                        e.Output.Contains(
                            "[DbTableMeta(PrimaryKeyOrder = new[] { \"OrderNo\", \"TenantId\" })]"
                        );
                        // リバースの結果の TablesWithPrimaryKeyOrder は取り込んだ図に載らないので、出力を元のテストと同じ引数で解析し直して見る
                        e.Output.That(
                            "リバースの TablesWithPrimaryKeyOrder が order_lines を含む",
                            lines =>
                                new CSharpReverseParser()
                                    .Parse(
                                        lines[0],
                                        new SqlServerTypeCatalog(),
                                        IdentifierLengthLimit.Unlimited
                                    )
                                    .TablesWithPrimaryKeyOrder.Contains("order_lines")
                        );
                        e.Imported.Count("Structure table *", 1)
                            .Single("PrimaryKeyOrder order_lines = order_no,tenant_id");
                        e.Warnings.Empty();
                    },
                    CSharpReverseAdapter.Value
                ),
            // CSharpReverseVerbatimTypeTests
            .. new[]
            {
                "datetime",
                "smalldatetime",
                "numeric(10,2)",
                "ntext",
                "text",
                "image",
                "smallmoney",
                "float(24)",
            }.Select(dataType =>
                Shape(
                        $"csharp-verbatim-type-{Slug(dataType)}",
                        $"非可逆な型表記は往復で元の綴りのまま戻る（{dataType}）"
                    )
                    .Build(() => CSharpVerbatimDiagram(dataType))
                    .Settings(
                        new FormatSettings(
                            CSharpOptions: new CodeGenerationOptions
                            {
                                RootNamespace = "QuickER.Tests.ReverseVerbatim",
                                GenerateValueObjects = false,
                                SplitFilesByCategory = false,
                            },
                            CSharpDialect: "sqlserver"
                        )
                    )
                    .Expect(
                        e =>
                        {
                            e.Output.Contains($"NativeType = \"{dataType}\"");
                            e.Imported.Count("Structure table *", 1)
                                .Single($"Type samples.probe = {dataType}");
                            e.Warnings.Empty();
                        },
                        CSharpReverseAdapter.Value
                    )
                    .Row()
            ),
            .. CSharpTypeSpellingRows(),
        ];

    /// <summary>
    /// 説明と主キーの順序の属性・方言ごとの型の綴りの往復（CSharpReversePrimaryKeyOrderTests・CSharpReverseBareDecimalTests・
    /// CSharpReverseMySqlUnsignedTests・CSharpReversePrecisionlessDecimalTests から移した。生成時の Info 診断は元のテストに残す）
    /// </summary>
    private static IEnumerable<ScenarioRow<FormatShape>> CSharpTypeSpellingRows() =>
        [
            // CSharpReversePrimaryKeyOrderTests.Generate_DescriptionAndOrder_ShareOneAttribute
            Shape(
                    "csharp-description-and-primary-key-order",
                    "説明と順序の両方を持つテーブルは 1 つの属性に並べる"
                )
                .Build(() =>
                    CSharpPrimaryKeyOrderDiagram(
                        ["order_no", "tenant_id"],
                        description: "Order lines"
                    )
                )
                .Settings(
                    new FormatSettings(
                        CSharpOptions: FormatRuleRows.PrimaryKeyOrderOptions,
                        CSharpDialect: DialectNames.SqlServer
                    )
                )
                .Expect(
                    e =>
                    {
                        e.Output.Contains(
                            "[DbTableMeta(Description = \"Order lines\", PrimaryKeyOrder = new[] { \"OrderNo\", \"TenantId\" })]"
                        );
                        e.Imported.Single("TableDescription order_lines = \"Order lines\"")
                            .Single("PrimaryKeyOrder order_lines = order_no,tenant_id");
                    },
                    CSharpReverseAdapter.Value
                ),
            // CSharpReverseBareDecimalTests.RoundTrip_BareDecimal_KeepsSpellingWithoutWarning：
            // 素の decimal は既定の精度・スケールのトークンで刻まれ、元の綴りが併記され、警告なしに decimal へ戻る
            .. new (string Dialect, string Token)[]
            {
                (DialectNames.SqlServer, "decimal(18,0)"),
                (DialectNames.MySql, "decimal(10,0)"),
            }.Select(item =>
                Shape(
                        $"csharp-bare-decimal-{item.Dialect}",
                        $"素の decimal は既定の精度のトークンと元の綴りで刻まれ、警告なしに戻る（{item.Dialect}）"
                    )
                    .Build(() => CSharpBareDecimalDiagram(item.Dialect))
                    .Settings(
                        new FormatSettings(
                            CSharpOptions: new CodeGenerationOptions
                            {
                                RootNamespace = "QuickER.Tests.ReverseBareDecimal",
                                GenerateValueObjects = false,
                                SplitFilesByCategory = false,
                            },
                            CSharpDialect: item.Dialect
                        )
                    )
                    .Expect(
                        e =>
                        {
                            e.Output.Contains(
                                $"[DbColumnMeta(\"{item.Token}\", NativeType = \"decimal\""
                            );
                            e.Imported.Single("Type samples.amount = decimal");
                            e.Warnings.Empty();
                        },
                        CSharpReverseAdapter.Value
                    )
                    .Row()
            ),
            // CSharpReverseMySqlUnsignedTests.RoundTrip_IntegerSpelling_IsPreserved：
            // トークンが範囲を表し、元の綴りが往復で戻り、プロパティはドライバの型のまま
            // （tinyint unsigned はトークンから同じ綴りへ書き戻せるので verbatim を持たない）
            .. new (string DataType, string MetaAttribute, string ClrType)[]
            {
                ("tinyint unsigned", "[DbColumnMeta(\"tinyint\")]", "byte"),
                ("tinyint", "[DbColumnMeta(\"smallint\", NativeType = \"tinyint\")]", "sbyte"),
                (
                    "smallint unsigned",
                    "[DbColumnMeta(\"int32\", NativeType = \"smallint unsigned\")]",
                    "ushort"
                ),
                (
                    "int unsigned",
                    "[DbColumnMeta(\"int64\", NativeType = \"int unsigned\")]",
                    "uint"
                ),
                (
                    "bigint unsigned",
                    "[DbColumnMeta(\"decimal(20,0)\", NativeType = \"bigint unsigned\")]",
                    "ulong"
                ),
            }.Select(item =>
                Shape(
                        $"csharp-mysql-integer-{Slug(item.DataType)}",
                        $"MySQL の符号つき・符号なし整数は往復で元の綴りのまま戻る（{item.DataType}）"
                    )
                    .Build(() => CSharpVerbatimDiagram(item.DataType))
                    .Settings(
                        new FormatSettings(
                            CSharpOptions: new CodeGenerationOptions
                            {
                                RootNamespace = "QuickER.Tests.ReverseMySqlUnsigned",
                                GenerateValueObjects = false,
                                SplitFilesByCategory = false,
                            },
                            CSharpDialect: DialectNames.MySql
                        )
                    )
                    .Expect(
                        e =>
                        {
                            e.Output.Contains(item.MetaAttribute)
                                .Contains($"public {item.ClrType}? Probe {{");
                            e.Imported.Single($"Type samples.probe = {item.DataType}");
                            e.Warnings.Empty();
                        },
                        CSharpReverseAdapter.Value
                    )
                    .Row()
            ),
            // CSharpReversePrecisionlessDecimalTests.RoundTrip_PrecisionlessScaledNumber_IsPreserved：
            // NUMBER(*,s) は decimal(*,s) のトークンで刻まれ（同じ方言へ書き戻すと綴りが揃うので verbatim 併記は不要）、Oracle へ戻る
            .. new (string DataType, string Token)[]
            {
                ("NUMBER(*,2)", "decimal(*,2)"),
                ("NUMBER(*,0)", "decimal(*,0)"),
            }.Select(item =>
                Shape(
                        $"csharp-oracle-precisionless-{Slug(item.DataType)}",
                        $"NUMBER(*,s) は decimal(*,s) のトークンで刻まれ Oracle へ戻る（{item.DataType}）"
                    )
                    .Build(() => CSharpOraclePrecisionlessDiagram(item.DataType))
                    .Settings(
                        new FormatSettings(
                            CSharpOptions: new CodeGenerationOptions
                            {
                                RootNamespace = "QuickER.Tests.ReversePrecisionless",
                                GenerateValueObjects = false,
                                SplitFilesByCategory = false,
                            },
                            CSharpDialect: DialectNames.Oracle
                        )
                    )
                    .Expect(
                        e =>
                        {
                            e.Output.Contains($"[DbColumnMeta(\"{item.Token}\"")
                                .Absent("NativeType =");
                            e.Imported.Single($"Type SAMPLES.PROBE = {item.DataType}");
                            e.Warnings.Empty();
                        },
                        CSharpReverseAdapter.Value
                    )
                    .Row()
            ),
        ];

    /// <summary>
    /// 素の decimal の列 1 本と、方言の既定の型の主キーを持つ図（CSharpReverseBareDecimalTests.Diagram。対象 DBMS は生成の方言）
    /// </summary>
    internal static ErDiagram CSharpBareDecimalDiagram(string dialect)
    {
        var provider = CSharpReverseAdapter.Providers.Get(dialect);

        return new ErDiagram
        {
            TargetDbms = provider.Name,
            Entities =
            {
                new Entity
                {
                    TableName = "samples",
                    Columns =
                    {
                        new Column
                        {
                            Name = "sample_id",
                            DataType = provider.TypeCatalog.DefaultDataType,
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Name = "amount",
                            DataType = "decimal",
                            IsNullable = true,
                        },
                    },
                },
            },
        };
    }

    /// <summary>型表記 1 つを持つ 1 テーブルの Oracle の図（CSharpReversePrecisionlessDecimalTests.SingleColumnDiagram）</summary>
    private static ErDiagram CSharpOraclePrecisionlessDiagram(string dataType) =>
        new()
        {
            TargetDbms = DialectNames.Oracle,
            Entities =
            {
                new Entity
                {
                    TableName = "SAMPLES",
                    Columns =
                    {
                        new Column
                        {
                            Name = "SAMPLE_ID",
                            DataType = "NUMBER(10)",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Name = "PROBE",
                            DataType = dataType,
                            IsNullable = true,
                        },
                    },
                },
            },
        };

    /// <summary>
    /// C# の生成（SQL Server・指定のオプション）→ リバースで、元の図と CSharpReverseRoundTripTests.AssertRoundTrips と同じ範囲で一致する行
    /// </summary>
    private static FormatShapeBuilder CSharpRoundTrip(
        string id,
        string description,
        Func<ErDiagram> build,
        CodeGenerationOptions options
    ) =>
        Shape(id, description)
            .Build(build)
            .Settings(new FormatSettings(CSharpOptions: options, CSharpDialect: "sqlserver"))
            .Expect(
                e =>
                {
                    e.ImportedDiagram.Add(
                        (
                            "テーブル名の集合が元の図と一致する",
                            d =>
                                d.Entities.Select(entity => entity.TableName)
                                    .Order(StringComparer.Ordinal)
                                    .SequenceEqual(
                                        build()
                                            .Entities.Select(entity => entity.TableName)
                                            .Order(StringComparer.Ordinal),
                                        StringComparer.Ordinal
                                    )
                        )
                    );
                    e.ImportedDiagram.Add(
                        (
                            "各テーブルの説明と列（名前・型・PK・NULL・説明・計算列・採番列）が順序込みで元の図と一致する",
                            d =>
                                build()
                                    .Entities.All(original =>
                                        d.Entities.Count(entity =>
                                            entity.TableName == original.TableName
                                        ) == 1
                                        && d.Entities.First(entity =>
                                            entity.TableName == original.TableName
                                        )
                                            is var reversed
                                        && reversed.Description == original.Description
                                        && ReverseColumns(reversed)
                                            .SequenceEqual(ReverseColumns(original))
                                    )
                        )
                    );
                    e.ImportedDiagram.Add(
                        (
                            "各テーブルの UNIQUE 制約（実効名・構成列）が元の図と一致する（順不同）",
                            d =>
                                build()
                                    .Entities.All(original =>
                                        d.Entities.FirstOrDefault(entity =>
                                            entity.TableName == original.TableName
                                        )
                                            is { } reversed
                                        && ReverseUniques(reversed)
                                            .SequenceEqual(ReverseUniques(original))
                                    )
                        )
                    );
                    e.ImportedDiagram.Add(
                        (
                            "リレーション（端点名・種類・先頭の列ペアの列名・制約名・参照アクション）が元の図と一致する（順不同）",
                            d =>
                                ReverseRelationships(d).SequenceEqual(ReverseRelationships(build()))
                        )
                    );
                },
                CSharpReverseAdapter.Value
            );

    /// <summary>列の射影（名前・型・PK・NULL 許容・説明・計算列・採番列。AssertRoundTrips.ProjectColumns と同じ）</summary>
    private static IEnumerable<string> ReverseColumns(Entity entity) =>
        entity.Columns.Select(column =>
            $"{column.Name}|{column.DataType}|{column.IsPrimaryKey}|{column.IsNullable}|{column.Description}|{column.IsComputed}|{column.IsIdentity}"
        );

    /// <summary>
    /// UNIQUE 制約の射影（実効名＝名前か合成名と構成列名。AssertRoundTrips.ProjectUniqueConstraints と同じ。元の BeEquivalentTo と同じく並びを問わない）
    /// </summary>
    private static IEnumerable<string> ReverseUniques(Entity entity)
    {
        var names = entity.Columns.ToDictionary(column => column.Id, column => column.Name);

        return entity
            .UniqueConstraints.Select(constraint =>
            {
                var columnNames = constraint
                    .ColumnIds.Select(id =>
                        names.TryGetValue(id, out var name) ? name : id.ToString()
                    )
                    .ToList();
                var effective =
                    constraint.Name
                    ?? UniqueConstraint.SynthesizeName(
                        entity.TableName,
                        columnNames,
                        IdentifierLengthLimit.Unlimited
                    );

                return $"{effective}|{string.Join(",", columnNames.Order(StringComparer.Ordinal))}";
            })
            .Order(StringComparer.Ordinal);
    }

    /// <summary>
    /// リレーションの射影（端点テーブル名・種類・先頭の列ペアの列名・制約名・参照アクション。AssertRoundTrips.ProjectRelationships と同じ。並びを問わない）
    /// </summary>
    private static IEnumerable<string> ReverseRelationships(ErDiagram diagram)
    {
        var tables = diagram.Entities.ToDictionary(entity => entity.Id, entity => entity.TableName);
        var columns = diagram
            .Entities.SelectMany(entity => entity.Columns)
            .ToDictionary(column => column.Id, column => column.Name);

        string? Name(Guid? id) =>
            id is { } value && columns.TryGetValue(value, out var name) ? name : null;

        return diagram
            .Relationships.Select(r =>
                $"{tables.GetValueOrDefault(r.SourceEntityId)}|{tables.GetValueOrDefault(r.TargetEntityId)}|{r.Type}|{Name(r.ColumnPairs.FirstOrDefault()?.SourceColumnId)}|{Name(r.ColumnPairs.FirstOrDefault()?.TargetColumnId)}|{r.ConstraintName}|{r.OnDelete}|{r.OnUpdate}"
            )
            .Order(StringComparer.Ordinal);
    }

    /// <summary>説明付き・多方言型の図（CSharpReverseRoundTripTests.RoundTrip_RichTypesAndDescriptions_Matches）</summary>
    private static ErDiagram CSharpRichTypesDiagram()
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

        return new ErDiagram
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
    }

    /// <summary>値型 NOT NULL 非 PK 列を含む図（CSharpReverseRoundTripTests.RoundTrip_ValueTypeNotNullColumns_WithoutValueObjects_Matches）</summary>
    private static ErDiagram CSharpValueTypeNotNullDiagram()
    {
        var order = new Entity
        {
            Id = Guid.NewGuid(),
            TableName = "orders",
            Description = "Sales orders",
            Columns =
            {
                new Column
                {
                    Id = Guid.NewGuid(),
                    Name = "order_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
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
                new Column
                {
                    Name = "discount",
                    DataType = "int",
                    IsNullable = true,
                },
                new Column
                {
                    Name = "code",
                    DataType = "varchar(50)",
                    IsNullable = false,
                },
                new Column
                {
                    Name = "note",
                    DataType = "nvarchar(200)",
                    IsNullable = true,
                },
            },
        };

        return new ErDiagram { Entities = { order } };
    }

    /// <summary>計算列を持つ図（CSharpReverseRoundTripTests.RoundTrip_ComputedColumn_Matches）</summary>
    private static ErDiagram CSharpComputedDiagram() =>
        new()
        {
            TargetDbms = "sqlserver",
            Entities =
            {
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "items",
                    Columns =
                    {
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "item_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "qty",
                            DataType = "int",
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "total",
                            DataType = "decimal(21,2)",
                            IsNullable = true,
                            IsComputed = true,
                        },
                    },
                },
            },
        };

    /// <summary>主キーでない採番列を持つ図（CSharpReverseRoundTripTests.RoundTrip_IdentityColumn_Matches）</summary>
    private static ErDiagram CSharpIdentityDiagram() =>
        new()
        {
            TargetDbms = "sqlserver",
            Entities =
            {
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "items",
                    Columns =
                    {
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "item_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "qty",
                            DataType = "int",
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "seq",
                            DataType = "bigint",
                            IsNullable = false,
                            IsIdentity = true,
                        },
                    },
                },
            },
        };

    /// <summary>主キー item_id が採番列の 1 テーブルの図（CSharpReverseRoundTripTests.ReverseIdentityPrimaryKeyDiagram）</summary>
    private static ErDiagram CSharpIdentityPrimaryKeyDiagram() =>
        new()
        {
            TargetDbms = "sqlserver",
            Entities =
            {
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "items",
                    Columns =
                    {
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "item_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                            IsIdentity = true,
                        },
                    },
                },
            },
        };

    /// <summary>UNIQUE 制約（単一・複合／実名・名前なし）と外部キーメタデータを持つ図（CSharpReverseRoundTripTests.RoundTrip_UniqueConstraintsAndForeignKeyMetadata_Matches）</summary>
    private static ErDiagram CSharpUniqueAndForeignKeyDiagram()
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
        user.UniqueConstraints.Add(
            new UniqueConstraint { ColumnIds = { userTenantFk, userLoginCol } }
        );

        return new ErDiagram
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
    }

    /// <summary>主キー 2 列（tenant_id → order_no の宣言順）＋通常列 1 つのテーブル（CSharpReversePrimaryKeyOrderTests.Diagram）</summary>
    /// <param name="order">主キーの順序として明示する列名（null＝明示しない）</param>
    /// <param name="description">テーブルの説明</param>
    internal static ErDiagram CSharpPrimaryKeyOrderDiagram(string[]? order, string description = "")
    {
        var entity = new Entity
        {
            TableName = "order_lines",
            Description = description,
            Columns =
            {
                new Column
                {
                    Name = "tenant_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "order_no",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "memo",
                    DataType = "nvarchar(50)",
                    IsNullable = true,
                },
            },
        };

        if (order is not null)
        {
            entity.PrimaryKeyColumnIds = order
                .Select(name => entity.Columns.Single(column => column.Name == name).Id)
                .ToList();
        }

        return new ErDiagram { Entities = { entity } };
    }

    /// <summary>型表記 1 つを持つ 1 テーブルの図（CSharpReverseVerbatimTypeTests.SingleColumnDiagram）</summary>
    internal static ErDiagram CSharpVerbatimDiagram(string dataType) =>
        new()
        {
            Entities =
            {
                new Entity
                {
                    TableName = "samples",
                    Columns =
                    {
                        new Column
                        {
                            Name = "sample_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Name = "probe",
                            DataType = dataType,
                            IsNullable = true,
                        },
                    },
                },
            },
        };
}
