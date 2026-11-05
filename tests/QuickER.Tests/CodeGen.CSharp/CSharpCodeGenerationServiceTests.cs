using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.CodeGen.CSharp.Resources;
using QuickER.Model;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// <see cref="CSharpCodeGenerationService"/> がダイアグラム定義から生成する C# コードの内容を検証するテストクラス
/// </summary>
public class CSharpCodeGenerationServiceTests
{
    /// <summary>
    /// 1対多リレーションからコレクション型ナビゲーションと NavigationReference 属性が生成され、親参照プロパティに JsonIgnore が付与されることを検証する
    /// </summary>
    /// <summary>ナビゲーションプロパティが生成され、親参照側に JsonIgnore 属性が付くことを検証する</summary>
    // 残す理由（P3-4b）：プロパティの初期化子・JSON オプションの初期化子・式本体の条件演算子（式本体）と、型を完全修飾しない書き方（書式）。構造の表明は行 code-navigation-entity-editmodel-mapper-graph へ移した
    [Fact]
    public void Generate_ShouldCreateNavigationAndJsonIgnoreOnParentReference()
    {
        var customer = Guid.NewGuid();
        var order = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderCustomerId = Guid.NewGuid();
        var diagram = new ErDiagram
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
                            Id = customerId,
                            Name = "customer_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    ],
                },
                new Entity
                {
                    Id = order,
                    TableName = "orders",
                    Columns =
                    [
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
                            Id = orderCustomerId,
                            Name = "customer_id",
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
                    SourceEntityId = customer,
                    TargetEntityId = order,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs = [new(customerId, orderCustomerId)],
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions { RootNamespace = "Sample.Domain" }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        // ナビゲーションの初期化子（コレクションは空のリスト・親参照は null!）
        content
            .Should()
            .Contain(
                "public ICollection<OrderEntity> Orders { get; set; } = new List<OrderEntity>();"
            );
        content.Should().Contain("public CustomerEntity Customer { get; set; } = null!;");
        content.Should().Contain("IgnoreReadOnlyProperties = true,");
        // EntityBase が使う namespace は using に含め、テンプレートでは完全修飾しない
        content.Should().NotContain("System.Text.Json.JsonSerializer");
        content.Should().NotContain("System.Collections.StructuralComparisons");
        // ToJson / Clone は共有 static オプションを使い回し（毎回 new しない）、循環は IgnoreCycles で安全化
        content.Should().Contain("ReferenceHandler = ReferenceHandler.IgnoreCycles,");
        content.Should().Contain("writeIndented ? _jsonOptionsIndented : _jsonOptions");
    }

    /// <summary>
    /// パスカルケースのテーブル名がそのままエンティティ名・ナビゲーションプロパティ名に反映されることを検証する
    /// </summary>
    /// <summary>パスカルケースのテーブル名がエンティティ名・ナビゲーション名に保持されることを検証する</summary>
    // 残す理由（P3-4b）：ナビゲーションのプロパティの初期化子（式本体）。構造の表明は行 code-pascal-case-table-names-entity-and-navigation へ移した
    [Fact]
    public void Generate_ShouldPreservePascalCaseTableNamesInEntityAndNavigationNames()
    {
        var category = Guid.NewGuid();
        var item = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var itemCategoryId = Guid.NewGuid();
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    Id = category,
                    TableName = "AirconditionerCategory",
                    Columns =
                    [
                        new Column
                        {
                            Id = categoryId,
                            Name = "AirconditionerCategoryId",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    ],
                },
                new Entity
                {
                    Id = item,
                    TableName = "Airconditioner",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "AirconditionerId",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = itemCategoryId,
                            Name = "AirconditionerCategoryId",
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
                    SourceEntityId = category,
                    TargetEntityId = item,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs = [new(categoryId, itemCategoryId)],
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions { RootNamespace = "Sample.Domain" }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        content
            .Should()
            .Contain(
                "public ICollection<AirconditionerEntity> Airconditioners { get; set; } = new List<AirconditionerEntity>();"
            );
        content
            .Should()
            .Contain(
                "public AirconditionerCategoryEntity AirconditionerCategory { get; set; } = null!;"
            );
    }

    /// <summary>リレーション警告の表示名が Guid でなく「親テーブル → 子テーブル」になることを検証する</summary>
    /// <remarks>
    /// 図の上で対象を特定できない Guid 表示は診断として役に立たないため、
    /// 表示名の規則（制約名の有無に依らずテーブル名ペア・最後の手段のみ Id）を固定する。
    /// </remarks>
    [Theory]
    [InlineData("FK_child_parent", "parents → children")]
    [InlineData(null, "parents → children")]
    public void Generate_CompositeRelationshipWarning_UsesReadableName(
        string? constraintName,
        string expectedDisplayName
    )
    {
        var parentA = new Column
        {
            Name = "a",
            DataType = "int",
            IsPrimaryKey = true,
        };
        var parentB = new Column
        {
            Name = "b",
            DataType = "int",
            IsPrimaryKey = true,
        };
        var childId = new Column
        {
            Name = "id",
            DataType = "int",
            IsPrimaryKey = true,
        };
        var childA = new Column { Name = "a_ref", DataType = "int" };
        var childB = new Column { Name = "b_ref", DataType = "int" };
        var parent = new Entity
        {
            Id = Guid.NewGuid(),
            TableName = "parents",
            Columns = [parentA, parentB],
        };
        var child = new Entity
        {
            Id = Guid.NewGuid(),
            TableName = "children",
            Columns = [childId, childA, childB],
        };
        var relationship = new Relationship
        {
            Id = Guid.NewGuid(),
            SourceEntityId = parent.Id,
            TargetEntityId = child.Id,
            Type = RelationshipType.OneToMany,
            ConstraintName = constraintName,
            ColumnPairs =
            [
                new RelationshipColumnPair(parentA.Id, childA.Id),
                new RelationshipColumnPair(parentB.Id, childB.Id),
            ],
        };
        var diagram = new ErDiagram { Entities = [parent, child], Relationships = [relationship] };

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions { RootNamespace = "Sample.Domain" }
        );

        result.HasErrors.Should().BeFalse();
        var warning = result
            .Diagnostics.Should()
            .ContainSingle(diagnostic =>
                diagnostic.Severity == GenerationDiagnosticSeverity.Warning
                && diagnostic.Message.Contains(
                    Strings.CodeGen_Warning_RelationshipCompositeSkipped.Split("{0}")[0]
                )
            )
            .Subject;
        warning.Message.Should().Contain(expectedDisplayName);
        warning
            .Message.Should()
            .NotContain(relationship.Id.ToString(), "Guid 表示は特定に使えない");
    }

    /// <summary>EditModel にバインディング文字列を確定値へ戻す処理が生成されることを検証する</summary>
    // 残す理由（P3-4b）：基底 EditModelBase の XmlDoc の生成器の固定の英文と、per-type のコメントブロックが無いこと（書式）。構造の表明は行 code-editmodel-input-revert-siblings-row-edit へ移した
    [Fact]
    public void Generate_EditModel_ShouldContainRevertInputMethod()
    {
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "orders",
                    Columns =
                    [
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
                            Id = Guid.NewGuid(),
                            Name = "amount",
                            DataType = "decimal",
                            IsNullable = false,
                        },
                    ],
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions { RootNamespace = "Sample.Domain" }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        // 発見性：拡張ポイント一覧は基底 EditModelBase の XmlDoc へ集約（per-type のコメントブロックは出さない）
        content
            .Should()
            .Contain("Extension points of a generated edit model (implement only what you need");
        content
            .Should()
            .NotContain(
                "// ===== Extension points (implement only what you need in a partial class"
            );
    }

    /// <summary>Repository インターフェイス・実装・DI 登録などの基盤コードが生成されることを検証する</summary>
    // 残す理由（P3-4b）：SQL Server の SQL の断片（SELECT・EXISTS・INSERT・UPDATE・DELETE の文と、実在確認に使う文の選び方）・列の並びを組む式（式本体）・主コンストラクタの改行（書式）。構造の表明は行 code-repository-infrastructure-contracts-and-registration へ移した
    [Fact]
    public void Generate_ShouldCreateRepositoryInfrastructure()
    {
        var diagram = new ErDiagram
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
                            Id = Guid.NewGuid(),
                            Name = "customer_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "name",
                            DataType = "nvarchar(100)",
                            IsNullable = false,
                        },
                    ],
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        content
            .Should()
            .Contain(
                "public sealed partial class CustomerRepository(\r\n    ISqlConnectionFactory connectionFactory,\r\n    ISaveHookRegistry? saveHooks = null,\r\n    ISqlExecutor? sqlExecutor = null\r\n)"
            );
        // カラム一覧は columnList へ抽出して SELECT 系で共用する（無制限バイナリ列を除いた SELECT 用列集合）
        content
            .Should()
            .Contain(
                "var columnList = string.Join(\", \", selectProperties.Select(property => EntitySaveMetadata.QuoteIdentifier(GetColumnName(property))));"
            );
        content
            .Should()
            .Contain(
                "SelectByIdSql = $\"SELECT {columnList} FROM {tableName} WHERE {EntitySaveMetadata.QuoteIdentifier(keyColumnName)} = @id;\""
            );
        // 実在確認は全列 SELECT ではなく EXISTS 専用文を持つ（行の中身は要らないため）
        content
            .Should()
            .Contain(
                "$\"SELECT CASE WHEN EXISTS (SELECT 1 FROM {tableName} WHERE {EntitySaveMetadata.QuoteIdentifier(keyColumnName)} = @id) THEN 1 ELSE 0 END;\""
            );
        content
            .Should()
            .Contain("new SqlCommand(metadata.ExistsByIdSql, connection)")
            .And.NotContain("new SqlCommand(metadata.SelectByIdSql, connection)");
        // INSERT の列名・値の並びは局所変数へ抽出して、版返却付き変種（InsertReturningSql）と共用する
        content
            .Should()
            .Contain(
                "var insertColumnList = string.Join(\", \", insertProperties.Select(property => EntitySaveMetadata.QuoteIdentifier(GetColumnName(property))));"
            )
            .And.Contain(
                "var insertValueList = string.Join(\", \", insertProperties.Select(property => $\"@{property.Name}\"));"
            )
            .And.Contain(
                "$\"INSERT INTO {tableName} ({insertColumnList}) VALUES ({insertValueList});\""
            );
        content
            .Should()
            .Contain(
                "$\"UPDATE {tableName} SET {updateSetClause} WHERE {EntitySaveMetadata.QuoteIdentifier(keyColumnName)} = @id;\""
            );
        content
            .Should()
            .Contain(
                "DeleteSql = $\"DELETE FROM {tableName} WHERE {EntitySaveMetadata.QuoteIdentifier(keyColumnName)} = @id;\""
            );
    }

    /// <summary>
    /// 生 SQL 実行の 3 メソッド（QueryBySqlAsync / ExecuteSqlAsync / ExecuteScalarSqlAsync）が
    /// IRepository インターフェイスと SqlServerRepository 基底の両方に生成され、
    /// パラメータ束縛・厳密マッピングの補助（BindRawSqlParameters / MapEntityFromRawSql）も出力されることを検証する
    /// </summary>
    // 残す理由（P3-4b）：フィールドの初期化子・パラメータを足す式・例外フィルタ（式本体）と、列不足の例外の文言。構造の表明は行 code-repository-raw-sql-methods-delegate-to-executor へ移した
    [Fact]
    public void Generate_Repository_ShouldEmitRawSqlMethods()
    {
        var result = new CSharpCodeGenerationService().Generate(
            SingleEntityDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        // 委譲先は SqlExecutor（Repository が private readonly で保持）
        content
            .Should()
            .Contain(
                "private readonly ISqlExecutor _sqlExecutor = sqlExecutor ?? new SqlExecutor(connectionFactory);"
            );
        content
            .Should()
            .Contain(
                "command.Parameters.AddWithValue($\"@{property.Name}\", value ?? DBNull.Value);"
            );
        // 厳密マッピング（列不足は全列を含む例外でラップ）
        content
            .Should()
            .Contain(
                "catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException)"
            );
        content.Should().Contain("columns ({ColumnList})");
    }

    /// <summary>
    /// エンティティ非依存の生 SQL 実行器 <c>ISqlExecutor</c> / <c>SqlExecutor</c> が生成され、
    /// 任意型射影 <c>QueryProjectionBySqlAsync</c>（単一値モード・DTO モード・typo ガード）と
    /// DI 登録（Singleton）が出力されることを検証する
    /// </summary>
    // 残す理由（P3-4b）：単一値モードの型判定の式（式本体）と、DTO モード・typo ガードの例外の文言。構造の表明は行 code-repository-sql-executor-and-projection へ移した
    [Fact]
    public void Generate_Repository_ShouldEmitSqlExecutorAndProjection()
    {
        var result = new CSharpCodeGenerationService().Generate(
            SingleEntityDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        content.Should().Contain("actual == typeof(byte[])");
        // DTO モード: 引数なしコンストラクタ必須・位置指定 record 非対応の例外
        content.Should().Contain("positional records are not supported");
        // typo ガード: 1 列も一致しないと列名・プロパティ名を含む例外
        content.Should().Contain("No column in the result set matches");
    }

    /// <summary>
    /// SQL パラメータ型明示化のため、Repository 生成時に Entity プロパティへ [SqlColumnType(...)] が
    /// DB 型に応じて付与されること（varchar(50)→VarChar+Size50 / nvarchar(max)→NVarChar+Size-1 /
    /// decimal(10,2)→Precision10/Scale2 / int→Int / 未知型→属性なし）を検証する
    /// </summary>
    // 残す理由（P3-4b）：属性のコンストラクタの式本体と、明示 SqlParameter を組む式・Size の安全ガードの式（式本体）。構造の表明は行 code-repository-sql-column-type-attributes-by-db-type へ移した
    [Fact]
    public void Generate_Repository_ShouldEmitSqlColumnTypeAttributes()
    {
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "items",
                    Columns =
                    [
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
                            Name = "code",
                            DataType = "varchar(50)",
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "note",
                            DataType = "nvarchar(max)",
                            IsNullable = true,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "price",
                            DataType = "decimal(10,2)",
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "location",
                            DataType = "geography",
                            IsNullable = true,
                        },
                    ],
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        content
            .Should()
            .Contain("public SqlColumnTypeAttribute(SqlDbType dbType) => DbType = dbType;");
        // ランタイムは属性から明示 SqlParameter を組み立て、Size 安全ガードで超過時は値長を使う
        content.Should().Contain("var parameter = new SqlParameter(name, dbType);");
        content
            .Should()
            .Contain(
                "parameter.Size = valueLength > attribute.Size ? valueLength : attribute.Size;"
            );
    }

    /// <summary>Repository にラムダ式ベースのクエリビルダー（Query / Where / OrderBy / 終端メソッド）が生成されることを検証する</summary>
    // 残す理由（P3-4b）：ページングの SQL Server の SQL の断片。構造の表明は行 code-repository-lambda-query-builder へ移した
    [Fact]
    public void Generate_ShouldCreateLambdaQueryBuilder()
    {
        var result = new CSharpCodeGenerationService().Generate(
            SingleEntityDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        content.Should().Contain("FETCH NEXT {take.Value} ROWS ONLY");
    }

    /// <summary>catch 内のロールバックへ CancellationToken.None を渡すコードが生成されることを検証する（キャンセル済みトークンでロールバックが中断され元例外が覆われる問題の回帰防止）</summary>
    [Fact]
    public void Generate_ShouldRollbackWithCancellationTokenNone()
    {
        var result = new CSharpCodeGenerationService().Generate(
            SingleEntityDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        content
            .Should()
            .Contain(
                "await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);"
            );
        content.Should().NotContain("await transaction.RollbackAsync(cancellationToken);");
    }

    /// <summary>コレクションの Contains（配列・List など）が SQL の IN 句へ変換されるコードが生成されることを検証する</summary>
    // 残す理由（P3-4b）：Contains の判定の条件式（式本体）と、IN 句の SQL Server の SQL の断片。構造の表明は行 code-repository-collection-contains-in-clause-helpers へ移した
    [Fact]
    public void Generate_ShouldTranslateCollectionContainsToInClause()
    {
        var result = new CSharpCodeGenerationService().Generate(
            SingleEntityDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        // 静的 Enumerable.Contains（配列・2 引数）／C# 14 の Span 版 MemoryExtensions.Contains（3 引数・比較子 null）と、
        // インスタンス Contains（List/HashSet）の双方を判定
        content.Should().Contain("if (call.Object is null && call.Arguments.Count is 2 or 3)");
        content
            .Should()
            .Contain("is not ConstantExpression { Value: null }", "非 null 比較子は翻訳対象外");
        content.Should().Contain("if (call.Object is not null && call.Arguments.Count == 1)");
        // 非 null 要素をパラメータ化して IN (...) を生成、空コレクションは恒偽（否定は恒真）条件
        content.Should().Contain("(false, false) => $\"{column} IN ({values})\"");
        content.Should().Contain("(false, false) => \"1 = 0\"");
        content.Should().Contain("(false, true) => \"1 = 1\"");
        // null 要素は IS NULL へ畳み、NOT IN は列側の NULL を無条件補償する（C# / EF Core の意味論）
        content
            .Should()
            .Contain("(true, false) => $\"({column} IN ({values}) OR {column} IS NULL)\"");
        content
            .Should()
            .Contain("(true, true) => $\"({column} NOT IN ({values}) AND {column} IS NOT NULL)\"");
        content
            .Should()
            .Contain("(false, true) => $\"({column} NOT IN ({values}) OR {column} IS NULL)\"");
    }

    /// <summary>IsNullOrEmpty・日付コンポーネント・Equals（大文字小文字無視含む）と、値オブジェクトの .Value 解決コードが生成されることを検証する</summary>
    // 残す理由（P3-4b）：空判定・日付部品・大文字小文字無視の SQL Server の SQL の断片と、値オブジェクトの判定の式（式本体）。構造の表明は行 code-repository-null-or-empty-date-part-equals-helpers へ移した
    [Fact]
    public void Generate_ShouldTranslateNullOrEmptyDatePartAndEquals()
    {
        var result = new CSharpCodeGenerationService().Generate(
            SingleEntityDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateValueObjects = true,
                GenerateRepositories = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        // SQL Server の空判定はバイト長（= '' は末尾の空白を無視する）・空白判定は char.IsWhiteSpace の全文字の否定クラス
        content.Should().Contain("({neColumn} IS NULL OR DATALENGTH({neColumn}) = 0)");
        content
            .Should()
            .Contain(
                "({neColumn} IS NULL OR {neColumn} NOT LIKE N'%[^' + {WhiteSpaceCharacters} + N']%')"
            );
        content.Should().Contain("\"Year\" => $\"YEAR({column})\"");
        content.Should().Contain("\"Date\" => $\"CAST({column} AS date)\"");
        content.Should().Contain("$\"LOWER({eqColumn}) = LOWER({eqParameter})\"");
        content
            .Should()
            .Contain("typeof(IValueObjectCore).IsAssignableFrom(member.Member.DeclaringType)");
    }

    /// <summary>
    /// 値オブジェクト構成でも行マテリアライザの型特殊化が有効で、VO 列は静的 Create を式木から直呼びすることを検証する。
    /// </summary>
    /// <remarks>
    /// 以前は型特殊化機構全体が「VO 無効時のみ」出力されており、VO 構成では VO 化されない素の列まで含めて
    /// 全列が <c>GetValue</c>（boxing）＋<c>PropertyInfo.SetValue</c>＋<c>SqlValueObjectActivator.Wrap</c>
    /// （<c>Convert.ChangeType</c> 込み）のフォールバックへ落ちていた。
    /// </remarks>
    // 残す理由（P3-4b）：型特殊化アクセサ表の鍵と getter の対応・式木を組む分岐（式本体）。構造の表明は行 code-value-object-typed-materializer-table へ移した
    [Fact]
    public void Generate_ValueObjects_ShouldKeepTypeSpecializedMaterializer()
    {
        var result = new CSharpCodeGenerationService().Generate(
            SingleEntityDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateValueObjects = true,
                GenerateRepositories = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        content.Should().Contain("[typeof(decimal)] = Getter(nameof(DbDataReader.GetDecimal)),");

        // 分岐 1: 非 VO プロパティ×対応型は typed getter 直読み
        content.Should().Contain("if (_typedReaders.TryGetValue(underlying, out var getter))");
        content
            .Should()
            .Contain("Expression read = Expression.Call(readerParam, getter, ordinal);");

        // 分岐 2: VO プロパティは内包値を typed getter で読み、静的 Create を式木から直呼びする
        content
            .Should()
            .Contain("if (ResolveValueObjectReader(propertyType) is (var voGetter, var voCreate))");
        content
            .Should()
            .Contain(
                string.Join(
                        "\n",
                        "            Expression created = Expression.Call(",
                        "                voCreate,",
                        "                Expression.Call(readerParam, voGetter, ordinal)",
                        "            );"
                    )
                    .ReplaceLineEndings()
            );

        // Create は IValueObject<TSelf, TValue> の TValue から解決し、リフレクション Invoke は挟まない
        content
            .Should()
            .Contain("i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValueObject<,>)");
        content
            .Should()
            .Contain(
                "if (!_typedReaders.TryGetValue(iface.GetGenericArguments()[1], out var getter))"
            );

        // 分岐 3: 対応表にない型（byte[] / enum など）は従来のフォールバックのまま
        content.Should().Contain("_setColumnValueMethod,");
    }

    /// <summary>RowState ベースのカスケード Save 基盤（EntityBase / SaveAsync / 保存エンジン）が生成されることを検証する</summary>
    // 残す理由（P3-4b）：プロパティ・オブジェクトの初期化子、null 要素を落とす式、ConfigureAwait まで続く呼び出しの連なり、更新欠落時の分岐、ロード時の状態の代入（式本体）。構造の表明は行 code-repository-cascade-save-infrastructure へ移した
    [Fact]
    public void Generate_ShouldCreateCascadeSaveInfrastructure()
    {
        var result = new CSharpCodeGenerationService().Generate(
            SingleEntityDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        content.Should().Contain("public RowState RowState { get; set; } = RowState.Unchanged;");
        content.Should().Contain("DestinationTableName = _metadata.TableName,");
        // null 要素はリーダーへ渡す前に落とす（BulkInsert の共通契約＝スキップ）
        content.Should().Contain("entities.Where(entity => entity is not null)");
        content
            .Should()
            .Contain(
                "await bulkCopy.WriteToServerAsync(reader, cancellationToken).ConfigureAwait(false);"
            );
        // 更新欠落時の方針（既定は例外、insertWhenUpdateMissing で INSERT へ切替）
        content.Should().Contain("if (insertWhenUpdateMissing)");
        // DB ロード時は Unchanged に確定する
        content.Should().Contain("entity.RowState = RowState.Unchanged;");
    }

    /// <summary>Include のある Query の取得が FOR JSON＋STJ で生成され、Include / ThenInclude（多階層）が生成されることを検証する（Include の無い取得のプレーン SELECT は SqlServerPlainQueryRuntimeTests が実 DB で固定する）</summary>
    // 残す理由（P3-4b）：FOR JSON・CONVERT・JSON_QUERY の SQL Server の SQL の断片。構造の表明は行 code-repository-json-include-query-helpers へ移した
    [Fact]
    public void Generate_ShouldCreateJsonIncludeQuery()
    {
        var result = new CSharpCodeGenerationService().Generate(
            SingleEntityDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        content.Should().Contain("FOR JSON PATH;");
        // 列はプロパティ名へ別名付け（float / real の列は可逆な書式の文字列）、単一参照は WITHOUT_ARRAY_WRAPPER
        content
            .Should()
            .Contain(
                "parts.Add($\"{column} AS {EntitySaveMetadata.QuoteIdentifier(property.Name)}\");"
            );
        content.Should().Contain("column = $\"CONVERT(varchar(30), {column}, 3)\";");
        content.Should().Contain("WITHOUT_ARRAY_WRAPPER");
        // ネストした FOR JSON はそのままだと文字列化されるため JSON_QUERY で包む
        content
            .Should()
            .Contain(
                "JSON_QUERY((SELECT {childProjection} FROM {childTable} AS {childAlias} WHERE {correlation} FOR JSON PATH{arrayMode})) AS {EntitySaveMetadata.QuoteIdentifier(node.Property.Name)}"
            );
    }

    /// <summary>条件付き一括削除（ExecuteDeleteAsync）とカスケード削除基盤が生成されることを検証する</summary>
    // 残す理由（P3-4b）：一括削除・カスケード削除の SQL Server の SQL の断片と、循環カスケードの例外の文言。構造の表明は行 code-repository-execute-delete-and-cascade-planner へ移した
    [Fact]
    public void Generate_ShouldCreateExecuteDeleteOnQuery()
    {
        var result = new CSharpCodeGenerationService().Generate(
            SingleEntityDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        // 非カスケードはセットベースの DELETE
        content.Should().Contain("$\"DELETE FROM {TableName}{whereClause};\"");
        // 列名は名前に含まれる ] を二重化する共有ヘルパー経由でクォートする（素の [{…}] 埋め込みではない）
        content
            .Should()
            .Contain(
                "IN (SELECT {EntitySaveMetadata.QuoteIdentifier(navigation.PrincipalColumn)} FROM {parentTable}{parentScopeWhere})"
            );
        // 循環カスケードは未対応として明示的に例外
        content.Should().Contain("Cyclic cascade");
    }

    /// <summary>Repository の SQL 生成でナビゲーションプロパティが列に含まれないことを検証する</summary>
    // 残す理由（P3-4b）：ナビゲーションを外す条件式・ナビゲーションの初期化子（式本体）。構造の表明は行 code-repository-navigation-properties-declared へ、SQL にナビゲーションが混ざらないことは行 exec-sqlite-repository-navigation-properties-are-not-columns へ移した
    [Fact]
    public void Generate_RepositorySql_ShouldExcludeNavigationProperties()
    {
        var customer = Guid.NewGuid();
        var order = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderCustomerId = Guid.NewGuid();
        var diagram = new ErDiagram
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
                            Id = customerId,
                            Name = "customer_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "name",
                            DataType = "nvarchar(100)",
                            IsNullable = false,
                        },
                    ],
                },
                new Entity
                {
                    Id = order,
                    TableName = "orders",
                    Columns =
                    [
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
                            Id = orderCustomerId,
                            Name = "customer_id",
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
                    SourceEntityId = customer,
                    TargetEntityId = order,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs = [new(customerId, orderCustomerId)],
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        content
            .Should()
            .Contain("property.GetCustomAttribute<NavigationReferenceAttribute>() is null");
        content
            .Should()
            .Contain(
                "public ICollection<OrderEntity> Orders { get; set; } = new List<OrderEntity>();"
            );
        content.Should().Contain("public CustomerEntity Customer { get; set; } = null!;");
    }

    /// <summary>バイナリ・値型カラムで安全なバインディング変換（Base64 / TryParse）が生成されることを検証する</summary>
    // 残す理由（P3-4b）：プロパティ・フィールドの初期化子（式本体）。構造の表明は行 code-editmodel-binary-and-value-type-binding-conversions へ移した
    [Fact]
    public void Generate_EditModel_WithBinaryAndValueTypes_ShouldUseSafeBindingConversions()
    {
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "files",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "file_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
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
                            Name = "filedata",
                            DataType = "varbinary(max)",
                            IsNullable = false,
                        },
                    ],
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        content.Should().Contain("public byte[] Filedata { get; set; } = Array.Empty<byte>();");
        content
            .Should()
            .Contain(
                "private static readonly EntitySaveMetadata _metadata = EntitySaveMetadata.For(typeof(TEntity));"
            );
        content
            .Should()
            .Contain(
                "private readonly ISqlConnectionFactory _connectionFactory = connectionFactory;"
            );
    }

    /// <summary>出力が 1MB（Scriban 既定の LimitToString）を超える大規模スキーマでも末尾まで生成され、切り捨てられないことを検証する</summary>
    // 残す理由（P3-4b）：出力の長さ・末尾の省略記号と、主コンストラクタの改行（書式）。構造の表明は行 code-large-schema-emits-last-entity-and-repository へ移した
    [Fact]
    public void Generate_LargeSchemaExceeding1MB_ShouldNotTruncateOutput()
    {
        // 全生成対象を有効にした 200 エンティティで出力は 1MB を大きく超える（Scriban 既定上限超過の回帰確認）
        var entities = Enumerable
            .Range(1, 200)
            .Select(index => new Entity
            {
                Id = Guid.NewGuid(),
                TableName = $"table_{index}",
                Columns =
                [
                    new Column
                    {
                        Id = Guid.NewGuid(),
                        Name = "id",
                        DataType = "int",
                        IsPrimaryKey = true,
                        IsNullable = false,
                    },
                    new Column
                    {
                        Id = Guid.NewGuid(),
                        Name = "name",
                        DataType = "nvarchar(100)",
                        IsNullable = false,
                    },
                    new Column
                    {
                        Id = Guid.NewGuid(),
                        Name = "amount",
                        DataType = "decimal",
                        IsNullable = true,
                    },
                    new Column
                    {
                        Id = Guid.NewGuid(),
                        Name = "created_at",
                        DataType = "datetime2",
                        IsNullable = false,
                    },
                ],
            })
            .ToList();

        var result = new CSharpCodeGenerationService().Generate(
            new ErDiagram { Entities = entities },
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        content.Length.Should().BeGreaterThan(1_048_576);
        // 切り捨て時に付与される省略記号で終わっていないこと
        content.TrimEnd().Should().NotEndWith("...");
        content
            .Should()
            .Contain(
                "public sealed partial class Table200Repository(\r\n    ISqlConnectionFactory connectionFactory,\r\n    ISaveHookRegistry? saveHooks = null,\r\n    ISqlExecutor? sqlExecutor = null\r\n)"
            );
    }

    /// <summary>リレーションが無くても Repository を生成する場合に NavigationReference 属性が定義され、生成コードがコンパイル可能であることを検証する</summary>
    // 残す理由（P3-4b）：属性でナビゲーションを外す条件式（式本体）。構造の表明は行 code-repository-navigation-reference-attribute-defined へ移した
    [Fact]
    public void Generate_RepositoryWithoutRelationships_ShouldEmitNavigationReferenceAttribute()
    {
        var result = new CSharpCodeGenerationService().Generate(
            SingleEntityDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        content
            .Should()
            .Contain("property.GetCustomAttribute<NavigationReferenceAttribute>() is null");
    }

    /// <summary>[SqlColumnType].IntegralDigits が Precision - Scale を返し、decimal 以外（Precision 未指定）は -1 を返すことを検証する</summary>
    // 残す理由（P3-4b）：IntegralDigits の式本体。構造の表明は行 code-sql-column-type-integral-digits-property へ移した
    [Fact]
    public void Generate_SqlColumnType_IntegralDigitsProperty_ShouldComputeFromPrecisionAndScale()
    {
        var result = new CSharpCodeGenerationService().Generate(
            ValueObjectDiagram(),
            new CodeGenerationOptions { RootNamespace = "Sample.Domain" }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        content
            .Should()
            .Contain("public int IntegralDigits => Precision > 0 ? Precision - Scale : -1;");
    }

    /// <summary>
    /// ランタイム中核 API の 4 件（DI なしの SaveHookRegistry・非ジェネリックな AddSaveHook・カスケード版 MarkAdded・
    /// 構造化された SaveConflictException）が、QuickER 版 Repository の生成物へ期待どおりの形で出力されることを検証する
    /// </summary>
    // 残す理由（P3-4b）：実体の MarkRemoved の式本体と、競合例外の主コンストラクタの改行（書式）。構造の表明は行 code-runtime-core-api-signatures へ移した
    [Fact]
    public void Generate_RuntimeCoreApis_ShouldEmitExpectedSignatures()
    {
        var result = new CSharpCodeGenerationService().Generate(
            SingleEntityDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        // MarkRemoved は単一ノードのまま
        content.Should().Contain("public void MarkRemoved() => RowState = RowState.Removed;");
        content
            .Should()
            .Contain(
                "public SaveConflictException(\r\n        string message,\r\n        SaveConflictReason reason,\r\n        string? entityTypeName,\r\n        string? key\r\n    )"
            );
    }

    /// <summary>主キー 1 列のみを持つ単純なエンティティ 1 件のダイアグラムを生成する</summary>
    private static ErDiagram SingleEntityDiagram() =>
        new()
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "items",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "item_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    ],
                },
            ],
        };

    /// <summary>VO 生成 ON で基底・インターフェイス・具象 VO が出力され、Entity/EditModel の型が VO になることを検証する</summary>
    // 残す理由（P3-4b）：DisplayValue の既定の式本体と、JSON の変換器の登録・VO の主キーの初期化子（式本体）。構造の表明は行 code-value-objects-emitted-and-applied へ移した
    [Fact]
    public void Generate_ValueObjects_Enabled_ShouldEmitAndApplyValueObjects()
    {
        var result = new CSharpCodeGenerationService().Generate(
            ValueObjectDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateValueObjects = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        content.Should().Contain("public virtual string DisplayValue => ToString();");
        // Entity の型が VO（非 NULL PK は null! 初期化）
        content.Should().Contain("public CustomerIdValue CustomerId { get; set; } = null!;");
        // EntityBase / Repository の JSON オプションに VO 変換器が登録される
        content.Should().Contain("Converters = { new ValueObjectJsonConverterFactory() },");
    }

    /// <summary>
    /// 列の説明が確定値プロパティの XmlDoc summary にも載ることを検証する
    /// （バインディングプロパティだけに載せると、コードから主に触る確定値側で説明が引けない）
    /// </summary>
    // 残す理由（P3-4b）：確定値の XmlDoc の summary の生成器の固定の英文（Confirmed value of … の定型文と、列の説明との連結）。構造で言い換えられる表明は無い
    [Fact]
    public void Generate_EditModel_ColumnDescription_ShouldAppearOnConfirmedProperty()
    {
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "accounts",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "account_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "balance",
                            DataType = "decimal(10,2)",
                            IsNullable = true,
                            Description = "Account balance.",
                        },
                    ],
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions { RootNamespace = "Sample.Domain" }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        content
            .Should()
            .Contain(
                "<summary>Account balance. - Confirmed value of Balance (written by the input conversion"
            );
        // 説明の無い列は従来の定型文のまま
        content.Should().Contain("<summary>Confirmed value of AccountId (written by");
    }

    /// <summary>VO 生成テスト用の代表的なダイアグラム（PK/FK 共有・各種型を含む）</summary>
    private static ErDiagram ValueObjectDiagram()
    {
        var customer = Guid.NewGuid();
        var order = Guid.NewGuid();
        var custPk = Guid.NewGuid();
        var orderFk = Guid.NewGuid();
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
                            Id = custPk,
                            Name = "customer_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "name",
                            DataType = "nvarchar(50)",
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
                            Name = "photo",
                            DataType = "varbinary(max)",
                            IsNullable = true,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "is_active",
                            DataType = "bit",
                            IsNullable = false,
                        },
                    ],
                },
                new Entity
                {
                    Id = order,
                    TableName = "orders",
                    Columns =
                    [
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
                            Id = orderFk,
                            Name = "customer_id",
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
                    SourceEntityId = customer,
                    TargetEntityId = order,
                    ColumnPairs = [new(custPk, orderFk)],
                    Type = RelationshipType.OneToMany,
                },
            ],
        };
    }

    // ---- EF Core（DbContext・Fluent 構成）----

    // ---- EF Core（EF Core 版 Repository・SqlExecutor・DI 拡張）----

    /// <summary>EF Core 生成 ON で EF Core 版 Repository（基底クラス＋エンティティ別実装）が生成されることを検証する</summary>
    // 残す理由（P3-4b）：基底の主コンストラクタへ渡す引数と、Entry の状態の代入（式本体）。構造の表明は行 code-efcore-repositories-core-shim-and-per-entity へ移した
    [Fact]
    public void Generate_EfCore_ShouldEmitEfCoreRepositories()
    {
        var result = new CSharpCodeGenerationService().Generate(
            ValueObjectDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateEfCoreRepositories = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        // 拡張シム（現行名）: 実装を Core へ委ね、コンストラクタ引数をそのまま転送する
        content
            .Should()
            .Contain(
                ") : EfCoreRepositoryCore<TEntity, TKey, TContext>(contextFactory, saveHooks, sqlExecutor)"
            );
        content
            .Should()
            .Contain(
                ") : EfCoreRepository<CustomerEntity, int, QuickErDbContext>(contextFactory, saveHooks, sqlExecutor), ICustomerRepository"
            );
        // 単発の追加・一括追加は Entry.State 代入（Add/AddRange のグラフ走査を避け、既存版と同じ範囲を挿入する）
        content.Should().Contain("context.Entry(entity).State = EntityState.Added;");
    }

    /// <summary>EF Core 版 SaveAsync がカスケードの閉包を RowState → EntityState 変換で登録し、競合を SaveConflictException へ変換することを検証する</summary>
    // 残す理由（P3-4b）：RowState から EntityState への switch の腕・競合の catch と例外の生成・状態の代入（式本体）。構造の表明は行 code-efcore-save-tracks-cascade-closure へ移した
    [Fact]
    public void Generate_EfCore_ShouldConvertRowStateOfCascadeClosureInSave()
    {
        var result = new CSharpCodeGenerationService().Generate(
            ValueObjectDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateEfCoreRepositories = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        content.Should().Contain("RowState.Added => EntityState.Added,");
        content.Should().Contain("RowState.Updated => EntityState.Modified,");
        content.Should().Contain("RowState.Removed => EntityState.Deleted,");
        content.Should().Contain("_ => EntityState.Unchanged,");

        // DbUpdateConcurrencyException → 既存契約（SaveConflictException / insertWhenUpdateMissing 切替）
        content.Should().Contain("catch (DbUpdateConcurrencyException ex)");
        content.Should().Contain("throw new SaveConflictException(");
        content.Should().Contain("entry.State = EntityState.Added;");
    }

    /// <summary>EF Core 生成 ON の SqlQuery に実行器差し替えバックエンド（式木捕捉・EF Core 実行）が追加されることを検証する</summary>
    // 残す理由（P3-4b）：並び順の捕捉・実行器の注入・Include の合成・OrderBy の名前の式（式本体）。構造の表明は行 code-efcore-sql-query-executor-backend へ移した
    [Fact]
    public void Generate_EfCore_ShouldAddExecutorBackendToSqlQuery()
    {
        var result = new CSharpCodeGenerationService().Generate(
            ValueObjectDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateEfCoreRepositories = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        content
            .Should()
            .Contain("_orderSelectors.Add(new SqlQueryOrdering(keySelector, Descending: false));");
        content
            .Should()
            .Contain("new(new EfCoreSqlQueryExecutor<TEntity, TContext>(_contextFactory));");
        content.Should().Contain("query = query.Include(path);");
        content.Should().Contain("nameof(Queryable.OrderBy)");
    }

    /// <summary>
    /// EF Core 生成 OFF（QuickER 版 Repository 単独）でも、SqlQuery は実行器抽象（ISqlQueryExecutor / SqlQueryPlan）経由へ統一され、
    /// 方言別 ADO 実行器（SqlServerSqlQueryExecutor）が出力される一方、EF Core 版クラスは一切出力されないことを検証する。
    /// </summary>
    /// <remarks>
    /// M2a のランタイム統一により、SqlQuery は常に <c>ISqlQueryExecutor&lt;TEntity&gt;</c> 経由で実行するよう変更された
    /// （以前は EF Core 生成時のみ実行器抽象が出て、QuickER 版 Repository は方言 SQL を SqlQuery 内に埋め込んでいた）。
    /// EF Core 依存（EfCore プレフィックスのクラス・DbContext 等）が漏れないことは引き続き守る。
    /// </remarks>
    // 残す理由（P3-4b）：Query() の式本体（方言別 ADO 実行器の注入）。構造の表明は行 code-efcore-disabled-sql-query-unified-through-ado-executor へ移した
    [Fact]
    public void Generate_EfCore_Disabled_ShouldStillUnifyThroughAdoExecutor()
    {
        var result = new CSharpCodeGenerationService().Generate(
            ValueObjectDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        content
            .Should()
            .Contain(
                "public SqlQuery<TEntity> Query() => new(new SqlServerSqlQueryExecutor<TEntity>(_connectionFactory));"
            );
    }

    /// <summary>
    /// インメモリ Repository とランタイムパッケージ参照モードの併用で、参照パッケージの案内が Core＋InMemory になる（ADO / EF Core を含まない）ことを検証する。
    /// </summary>
    /// <remarks>
    /// 生成物の側（固定 infra が出ない・InMemory パッケージの using が出る）は決まった行
    /// <c>code-inmemory-runtime-package-leaves-fixed-infra-out</c> が見る。
    /// </remarks>
    // 残す理由（P3-4b）：RuntimePackageReferenceGuidance.Compute の戻り値（生成物ではないので決まった行の判定の外）。生成物の側は行 code-inmemory-runtime-package-leaves-fixed-infra-out が見る
    [Fact(
        DisplayName = "インメモリ Repository ＋ UseRuntimePackages の参照パッケージの案内は Core＋InMemory になる"
    )]
    public void Generate_InMemoryWithRuntimePackages_UsesInMemoryPackage()
    {
        var options = new CodeGenerationOptions
        {
            RootNamespace = "Sample.Domain",
            GenerateRepositories = false,
            GenerateEfCoreRepositories = false,
            GenerateInMemoryRepositories = true,
            UseRuntimePackages = true,
        };

        RuntimePackageReferenceGuidance
            .Compute(options)
            .Should()
            .Equal(RuntimePackages.Core, RuntimePackages.InMemory);
    }

    /// <summary>
    /// リモートサービス生成とランタイムパッケージ参照モードの併用で、参照パッケージの案内が Core＋SqlServer＋AspNetCore になることを検証する。
    /// </summary>
    /// <remarks>
    /// 生成物の側（サーバー実装の固定部が出ない・AspNetCore パッケージの using・エンドポイントと <c>OnServerError</c> の partial フックが残る）は
    /// 決まった行 <c>code-remote-server-runtime-package-leaves-engine-out</c> が見る。
    /// </remarks>
    // 残す理由（P3-4b）：RuntimePackageReferenceGuidance.Compute の戻り値（生成物ではないので決まった行の判定の外）。生成物の側は行 code-remote-server-runtime-package-leaves-engine-out が見る
    [Fact(
        DisplayName = "リモートサービス生成 ＋ UseRuntimePackages の参照パッケージの案内は Core＋SqlServer＋AspNetCore になる"
    )]
    public void Generate_RemoteServicesWithRuntimePackages_UsesAspNetCorePackage()
    {
        var options = new CodeGenerationOptions
        {
            RootNamespace = "Sample.Domain",
            GenerateRepositories = true,
            GenerateRemoteServices = true,
            UseRuntimePackages = true,
        };

        RuntimePackageReferenceGuidance
            .Compute(options)
            .Should()
            .Equal(RuntimePackages.Core, RuntimePackages.SqlServer, RuntimePackages.AspNetCore);
    }

    /// <summary>varbinary(max)（無制限バイナリ）と rowversion（有界バイナリ）を持つ単一エンティティ図（除外機能の検証用）</summary>
    private static ErDiagram BinaryColumnDiagram() =>
        new()
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "documents",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "document_id",
                            DataType = "int",
                            IsPrimaryKey = true,
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
                            Id = Guid.NewGuid(),
                            Name = "row_version",
                            DataType = "rowversion",
                            IsNullable = false,
                        },
                    ],
                },
            ],
        };

    /// <summary>
    /// ExcludeUnboundedBinaryColumns=true のとき、varbinary(max) 列にマーカー属性が付与され、
    /// Info 診断が 1 件出ることを検証する（rowversion 列には付与されない。ヘッダには出さない＝
    /// 生成コード側の可視化は属性が担い、一覧は生成時の診断でのみ通知する）。
    /// </summary>
    // 残す理由（P3-4b）：ヘッダーに除外列の一覧の固定文を出さないこと（生成器の固定の文）。構造の表明は行 code-excluded-unbounded-binary-marker-on-photo-only へ移した
    [Fact(DisplayName = "無制限バイナリ除外 ON: マーカー付与・Info 診断（rowversion は対象外）")]
    public void Generate_ExcludeUnboundedBinary_On_MarksColumnAndReportsInfo()
    {
        var result = new CSharpCodeGenerationService().Generate(
            BinaryColumnDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                ExcludeUnboundedBinaryColumns = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result
            .Files.Single(f => f.FileName.EndsWith(".g.cs", StringComparison.Ordinal))
            .Content;
        // ヘッダには除外列一覧を出さない（属性と Info 診断で可視化する）
        content.Should().NotContain("無制限バイナリ列（SELECT / UPDATE 除外）");
    }

    /// <summary>
    /// rowversion 列を持つ図の SQL Server 方言で、楽観排他（ConcurrencyMode）の契約と版条件付き SQL 変種が
    /// 生成されることを検証する（意味論の実 DB 検証は SqlServerConcurrencyRuntimeTests）。
    /// </summary>
    // 残す理由（P3-4b）：版条件・新版の返却の SQL Server の SQL の断片と、競合の例外の文言。構造の表明は行 code-rowversion-concurrency-contract-and-metadata へ移した
    [Fact(
        DisplayName = "楽観排他: ConcurrencyMode 契約と版条件付き SQL 変種が SQL Server 方言に出る"
    )]
    public void Generate_RowVersionColumn_EmitsConcurrencyContractAndVersionedSql()
    {
        var result = new CSharpCodeGenerationService().Generate(
            BinaryColumnDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result
            .Files.Single(f => f.FileName.EndsWith(".g.cs", StringComparison.Ordinal))
            .Content;
        // 版条件は @originalRowVersion・新版の返却は OUTPUT INSERTED
        content
            .Should()
            .Contain("OUTPUT INSERTED.")
            .And.Contain("{rowVersionColumn} = @originalRowVersion;");
        content.Should().Contain("was modified by another user");
    }
}
