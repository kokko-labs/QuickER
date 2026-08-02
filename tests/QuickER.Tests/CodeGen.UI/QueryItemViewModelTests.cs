using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using QuickER.CodeGen.UI;
using QuickER.Model;

namespace QuickER.Tests.CodeGen.UI;

/// <summary>
/// <see cref="QueryItemViewModel" />（クエリ定義 1 件の編集用 ViewModel）の構築・派生表示・戻り形/実装方式の
/// 切替・条件/生 SQL の即時検証・子行の追加削除・モデル化（ToModel）・シグネチャプレビュー・親通知を検証する
/// テストクラス
/// </summary>
/// <remarks>
/// ダイアログ VM を介さず <see cref="QueryItemViewModel" /> を直接構築して観測可能な振る舞い（プロパティ変更・
/// 派生値・検証状態）を洗い出す。図は 2 エンティティ（Order / Product）で列構成を変えて、エンティティ変更時の
/// 陳腐化した列参照の掃除も検証できるようにする。
/// </remarks>
public class QueryItemViewModelTests
{
    /// <summary>Order（CustomerId 主キー int・Amount decimal）と Product（ProductId 主キー int）の 2 エンティティを作る</summary>
    private static IReadOnlyList<Entity> CreateEntities(
        out Entity order,
        out Guid customerColumnId,
        out Guid amountColumnId,
        out Entity product,
        out Guid productColumnId
    )
    {
        order = new Entity { TableName = "Order" };
        var customer = new Column
        {
            Name = "CustomerId",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var amount = new Column { Name = "Amount", DataType = "decimal(12,2)" };
        order.Columns.Add(customer);
        order.Columns.Add(amount);
        customerColumnId = customer.Id;
        amountColumnId = amount.Id;

        product = new Entity { TableName = "Product" };
        var productId = new Column
        {
            Name = "ProductId",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        product.Columns.Add(productId);
        productColumnId = productId.Id;

        return new List<Entity> { order, product };
    }

    /// <summary>指定エンティティに属する既定のクエリ定義（DSL・一覧・無条件）を作る</summary>
    private static QueryDefinition CreateQuery(Guid entityId, string name = "GetOrders") =>
        new()
        {
            EntityId = entityId,
            Name = name,
            Returns = QueryReturnShape.List,
            Implementation = QueryImplementationKind.Dsl,
        };

    // ===== 構築 =====

    /// <summary>source / entities が null なら構築時に例外になることを検証する</summary>
    [Fact(DisplayName = "source・entities が null なら構築で例外")]
    public void Constructor_NullArguments_Throw()
    {
        var entities = CreateEntities(out var order, out _, out _, out _, out _);

        var nullSource = () => new QueryItemViewModel(null!, entities);
        var nullEntities = () => new QueryItemViewModel(CreateQuery(order.Id), null!);

        nullSource.Should().Throw<ArgumentNullException>();
        nullEntities.Should().Throw<ArgumentNullException>();
    }

    /// <summary>元定義のスカラー・子コレクション（パラメータ・並び順・射影）が複製として読み込まれることを検証する</summary>
    [Fact(DisplayName = "元定義のスカラー値と子コレクションが読み込まれる")]
    public void Constructor_LoadsSourceValuesAndChildren()
    {
        var entities = CreateEntities(
            out var order,
            out var customerColumnId,
            out var amountColumnId,
            out _,
            out _
        );

        var source = new QueryDefinition
        {
            EntityId = order.Id,
            Name = "GetByCustomer",
            Description = "説明",
            Returns = QueryReturnShape.Projection,
            ResultTypeName = "OrderRow",
            Condition = "CustomerId = @customerId",
            HasPaging = true,
            Implementation = QueryImplementationKind.Dsl,
            Parameters =
            {
                new QueryParameter { Name = "customerId", Type = "int32" },
            },
            OrderBy =
            {
                new QueryOrdering { ColumnId = customerColumnId, Descending = true },
            },
            Fields =
            {
                new ProjectionField { Name = "Amount", SourceColumnId = amountColumnId },
            },
        };

        var vm = new QueryItemViewModel(source, entities);

        vm.Id.Should().Be(source.Id);
        vm.EntityId.Should().Be(order.Id);
        vm.Name.Should().Be("GetByCustomer");
        vm.Description.Should().Be("説明");
        vm.Returns.Should().Be(QueryReturnShape.Projection);
        vm.ResultTypeName.Should().Be("OrderRow");
        vm.Condition.Should().Be("CustomerId = @customerId");
        vm.HasPaging.Should().BeTrue();
        vm.Parameters.Should().ContainSingle();
        vm.OrderBy.Should().ContainSingle();
        vm.Fields.Should().ContainSingle();

        // 列参照フィールドは型トークンが列由来（decimal(12,2)）に導出される
        vm.Fields[0].SourceColumnId.Should().Be(amountColumnId);
        vm.Fields[0].Type.Should().Be("decimal(12,2)");
        vm.Fields[0].IsTypeEditable.Should().BeFalse();
    }

    /// <summary>生 SQL 実装の定義から方言別 SQL（sqlserver / sqlite）が読み込まれることを検証する</summary>
    [Fact(DisplayName = "生 SQL 実装の方言別 SQL が読み込まれる")]
    public void Constructor_LoadsDialectSql()
    {
        var entities = CreateEntities(out var order, out _, out _, out _, out _);
        var source = new QueryDefinition
        {
            EntityId = order.Id,
            Name = "RawQuery",
            Implementation = QueryImplementationKind.Sql,
            Sql = new Dictionary<string, string>
            {
                ["sqlserver"] = "SELECT 1",
                ["sqlite"] = "SELECT 2",
            },
        };

        var vm = new QueryItemViewModel(source, entities);

        vm.SqlServerSql.Should().Be("SELECT 1");
        vm.SqliteSql.Should().Be("SELECT 2");
    }

    /// <summary>構築時に対象エンティティの列から選択肢が作られ、「なし」付き選択肢は先頭に None が入ることを検証する</summary>
    [Fact(DisplayName = "利用可能列の選択肢が構築される（None 付きは先頭が None）")]
    public void Constructor_BuildsAvailableColumns()
    {
        var entities = CreateEntities(out var order, out _, out _, out _, out _);
        var vm = new QueryItemViewModel(CreateQuery(order.Id), entities);

        vm.AvailableColumns.Select(c => c.Name).Should().Equal("CustomerId", "Amount");
        vm.AvailableColumnsWithNone[0].Id.Should().BeNull("先頭は「なし＝自由フィールド」");
        vm.AvailableColumnsWithNone.Skip(1)
            .Select(c => c.Name)
            .Should()
            .Equal("CustomerId", "Amount");
    }

    // ===== 派生表示 =====

    /// <summary>生成メソッド名が Async サフィックス付与ルールに従うことを検証する</summary>
    [Theory(DisplayName = "生成メソッド名は Async サフィックスを付与する")]
    [InlineData("GetOrders", "GetOrdersAsync")]
    [InlineData("GetOrdersAsync", "GetOrdersAsync")] // 既に Async なら重ねない
    [InlineData("", "")] // 空名は空
    public void GeneratedMethodName_AppliesAsyncSuffix(string name, string expected)
    {
        var entities = CreateEntities(out var order, out _, out _, out _, out _);
        var vm = new QueryItemViewModel(CreateQuery(order.Id), entities) { Name = name };

        vm.GeneratedMethodName.Should().Be(expected);
    }

    /// <summary>EntityName が選択中エンティティの表示名を返すことを検証する</summary>
    [Fact(DisplayName = "EntityName は選択中エンティティ名を返す")]
    public void EntityName_ReflectsSelectedEntity()
    {
        var entities = CreateEntities(out var order, out _, out _, out var product, out _);
        var vm = new QueryItemViewModel(CreateQuery(order.Id), entities);

        vm.EntityName.Should().Be("Order");

        vm.EntityId = product.Id;
        vm.EntityName.Should().Be("Product");
    }

    /// <summary>戻り形に応じてスカラー型欄・射影設定の表示フラグが切り替わることを検証する</summary>
    [Fact(DisplayName = "戻り形でスカラー型欄・射影設定の表示が切り替わる")]
    public void Returns_TogglesShowFlags()
    {
        var entities = CreateEntities(out var order, out _, out _, out _, out _);
        var vm = new QueryItemViewModel(CreateQuery(order.Id), entities);

        vm.Returns = QueryReturnShape.Scalar;
        vm.ShowScalarType.Should().BeTrue();
        vm.ShowProjection.Should().BeFalse();

        vm.Returns = QueryReturnShape.Projection;
        vm.ShowScalarType.Should().BeFalse();
        vm.ShowProjection.Should().BeTrue();
    }

    /// <summary>実装方式に応じて DSL 面・SQL 面の表示フラグが切り替わることを検証する</summary>
    [Fact(DisplayName = "実装方式で DSL 面・SQL 面の表示が切り替わる")]
    public void Implementation_TogglesShowFlags()
    {
        var entities = CreateEntities(out var order, out _, out _, out _, out _);
        var vm = new QueryItemViewModel(CreateQuery(order.Id), entities);

        vm.Implementation = QueryImplementationKind.Dsl;
        vm.IsDslImplementation.Should().BeTrue();
        vm.ShowSqlEditors.Should().BeFalse();

        vm.Implementation = QueryImplementationKind.Sql;
        vm.IsDslImplementation.Should().BeFalse();
        vm.ShowSqlEditors.Should().BeTrue();

        vm.Implementation = QueryImplementationKind.Manual;
        vm.IsDslImplementation.Should().BeFalse();
        vm.ShowSqlEditors.Should().BeFalse();
    }

    /// <summary>スカラー×DSL の不正組合せ判定（静的述語）と CanSelectScalar を検証する</summary>
    [Theory(DisplayName = "スカラー×DSL の不正判定と CanSelectScalar")]
    [InlineData(QueryReturnShape.Scalar, QueryImplementationKind.Dsl, true)]
    [InlineData(QueryReturnShape.Scalar, QueryImplementationKind.Sql, false)]
    [InlineData(QueryReturnShape.Scalar, QueryImplementationKind.Manual, false)]
    [InlineData(QueryReturnShape.List, QueryImplementationKind.Dsl, false)]
    public void IsScalarDslConflict_And_CanSelectScalar(
        QueryReturnShape returns,
        QueryImplementationKind implementation,
        bool expectedConflict
    )
    {
        QueryItemViewModel
            .IsScalarDslConflict(returns, implementation)
            .Should()
            .Be(expectedConflict);

        var entities = CreateEntities(out var order, out _, out _, out _, out _);
        var vm = new QueryItemViewModel(CreateQuery(order.Id), entities);
        // CanSelectScalar は「スカラー×現在の実装方式」の不正述語の否定＝DSL のときだけ選べない
        // （現在の戻り形には依存しないため returns ではなく implementation だけで決まる）
        vm.Implementation = implementation;
        vm.CanSelectScalar.Should().Be(implementation != QueryImplementationKind.Dsl);
    }

    /// <summary>戻り形ラジオ（bool プロキシ）が true 代入でその戻り形を選び、他が排他的に false になることを検証する</summary>
    [Fact(DisplayName = "戻り形ラジオが排他的に切り替わる")]
    public void ReturnRadios_SwitchExclusively()
    {
        var entities = CreateEntities(out var order, out _, out _, out _, out _);
        var vm = new QueryItemViewModel(CreateQuery(order.Id), entities);

        vm.ReturnsSingle = true;
        vm.Returns.Should().Be(QueryReturnShape.Single);
        vm.ReturnsList.Should().BeFalse();
        vm.ReturnsSingle.Should().BeTrue();

        vm.ReturnsCount = true;
        vm.Returns.Should().Be(QueryReturnShape.Count);
        vm.ReturnsSingle.Should().BeFalse();

        // false 代入は無視される（ラジオの流儀＝true のときだけ選択）
        vm.ReturnsCount = false;
        vm.Returns.Should().Be(QueryReturnShape.Count);
    }

    /// <summary>実装方式ラジオ（bool プロキシ）が true 代入でその方式を選ぶことを検証する</summary>
    [Fact(DisplayName = "実装方式ラジオが排他的に切り替わる")]
    public void ImplementationRadios_SwitchExclusively()
    {
        var entities = CreateEntities(out var order, out _, out _, out _, out _);
        var vm = new QueryItemViewModel(CreateQuery(order.Id), entities);

        vm.ImplementationSql = true;
        vm.Implementation.Should().Be(QueryImplementationKind.Sql);
        vm.ImplementationDsl.Should().BeFalse();

        vm.ImplementationManual = true;
        vm.Implementation.Should().Be(QueryImplementationKind.Manual);
        vm.ImplementationSql.Should().BeFalse();
    }

    // ===== 実装方式切替時のリセット =====

    /// <summary>スカラー選択中に DSL へ切り替えると戻り形が一覧へリセットされることを検証する（不正組合せの防止）</summary>
    [Fact(DisplayName = "スカラー選択中の DSL 切替は戻り形を一覧へ戻す")]
    public void SwitchingToDsl_WhileScalar_ResetsReturnsToList()
    {
        var entities = CreateEntities(out var order, out _, out _, out _, out _);
        var vm = new QueryItemViewModel(CreateQuery(order.Id), entities);

        vm.Implementation = QueryImplementationKind.Sql;
        vm.Returns = QueryReturnShape.Scalar;

        vm.Implementation = QueryImplementationKind.Dsl;
        vm.Returns.Should().Be(QueryReturnShape.List);
    }

    // ===== エンティティ変更に伴う列参照の掃除 =====

    /// <summary>
    /// エンティティ変更時に、利用可能列が入れ替わり、旧エンティティの列を参照していた並び順は行ごと除去され、
    /// 射影フィールド・パラメータの列参照は「なし」へ解除されることを検証する。
    /// </summary>
    [Fact(DisplayName = "エンティティ変更で陳腐化した列参照が掃除される")]
    public void ChangingEntity_ClearsStaleColumnReferences()
    {
        var entities = CreateEntities(
            out var order,
            out var customerColumnId,
            out var amountColumnId,
            out var product,
            out _
        );

        var source = new QueryDefinition
        {
            EntityId = order.Id,
            Name = "GetByCustomer",
            Returns = QueryReturnShape.Projection,
            OrderBy = { new QueryOrdering { ColumnId = customerColumnId } },
            Fields =
            {
                new ProjectionField { Name = "Amount", SourceColumnId = amountColumnId },
            },
            Parameters =
            {
                new QueryParameter { Name = "typedId", SourceColumnId = customerColumnId },
            },
        };
        var vm = new QueryItemViewModel(source, entities);

        vm.EntityId = product.Id;

        vm.AvailableColumns.Select(c => c.Name).Should().Equal("ProductId");
        vm.OrderBy.Should().BeEmpty("旧列参照の並び順行は行ごと除去される");
        vm.Fields[0].SourceColumnId.Should().BeNull("射影フィールドの列参照は解除される");
        vm.Fields[0].Name.Should().Be("Amount", "名前は保持される");
        vm.Parameters[0].SourceColumnId.Should().BeNull("パラメータの列参照も解除される");
    }

    // ===== 条件の即時検証（DSL） =====

    /// <summary>DSL 実装で不正な列を参照する条件は診断が出て無効・修正で有効になることを検証する</summary>
    [Fact(DisplayName = "DSL 条件は不正列で無効・修正で有効になる")]
    public void Condition_ValidatesImmediately()
    {
        var entities = CreateEntities(out var order, out _, out _, out _, out _);
        var source = CreateQuery(order.Id);
        source.Parameters.Add(new QueryParameter { Name = "customerId", Type = "int32" });
        var vm = new QueryItemViewModel(source, entities);

        vm.Condition = "NoSuchColumn = @customerId";
        vm.IsConditionValid.Should().BeFalse();
        vm.ConditionDiagnostics.Should().NotBeEmpty();

        vm.Condition = "CustomerId = @customerId";
        vm.IsConditionValid.Should().BeTrue();
        vm.ConditionDiagnostics.Should().BeEmpty();
    }

    /// <summary>DSL 以外・空条件のときは条件検証をスキップ（有効扱い）することを検証する</summary>
    [Fact(DisplayName = "DSL 以外・空条件は検証をスキップして有効")]
    public void Condition_SkippedWhenNotDslOrEmpty()
    {
        var entities = CreateEntities(out var order, out _, out _, out _, out _);
        var vm = new QueryItemViewModel(CreateQuery(order.Id), entities);

        // 生 SQL 実装では条件欄の検証はスキップ（不正列を書いても有効扱い）
        vm.Implementation = QueryImplementationKind.Sql;
        vm.Condition = "NoSuchColumn = 1";
        vm.IsConditionValid.Should().BeTrue();
        vm.ConditionDiagnostics.Should().BeEmpty();
    }

    // ===== 生 SQL の静的検証 =====

    /// <summary>生 SQL の未宣言パラメータが方言ラベル付きで診断され、IsRawSqlValid=false になることを検証する</summary>
    [Fact(DisplayName = "生 SQL の未宣言パラメータは無効になる")]
    public void RawSql_UndeclaredParameter_IsInvalid()
    {
        var entities = CreateEntities(out var order, out _, out _, out _, out _);
        var vm = new QueryItemViewModel(CreateQuery(order.Id), entities);

        vm.Implementation = QueryImplementationKind.Sql;
        vm.SqlServerSql = "SELECT * FROM [Order] WHERE [X] = @ghost";

        vm.IsRawSqlValid.Should().BeFalse();
        vm.SqlDiagnostics.Should().Contain(m => m.Contains("ghost"));

        // 宣言済みパラメータを使う SQL に直すと解消する
        vm.Parameters.Add(new QueryParameterViewModel(_ => null) { Name = "customerId" });
        vm.SqlServerSql = "SELECT * FROM [Order] WHERE [CustomerId] = @customerId";
        vm.IsRawSqlValid.Should().BeTrue();
        vm.SqlDiagnostics.Should().BeEmpty();
    }

    /// <summary>生 SQL の未使用パラメータ・複文は診断のみで OK をブロックしない（IsRawSqlValid=true）ことを検証する</summary>
    [Fact(DisplayName = "生 SQL の未使用・複文は診断のみで有効のまま")]
    public void RawSql_UnusedAndMultiStatement_StayValid()
    {
        var entities = CreateEntities(out var order, out _, out _, out _, out _);
        var source = CreateQuery(order.Id);
        source.Parameters.Add(new QueryParameter { Name = "customerId", Type = "int32" });
        var vm = new QueryItemViewModel(source, entities);

        vm.Implementation = QueryImplementationKind.Sql;
        vm.SqlServerSql = "SELECT 1; SELECT 2"; // customerId 未使用 ＋ 複文

        vm.SqlDiagnostics.Should().HaveCount(2);
        vm.IsRawSqlValid.Should().BeTrue();
    }

    /// <summary>ページング有効時は take / skip が宣言済み扱いになり、SQL からの参照で未宣言にならないことを検証する</summary>
    [Fact(DisplayName = "ページング有効で take・skip は宣言済み扱いになる")]
    public void RawSql_Paging_DeclaresTakeAndSkip()
    {
        var entities = CreateEntities(out var order, out _, out _, out _, out _);
        var vm = new QueryItemViewModel(CreateQuery(order.Id), entities);

        vm.Implementation = QueryImplementationKind.Sql;
        vm.HasPaging = true;
        vm.SqlServerSql =
            "SELECT * FROM [Order] ORDER BY [CustomerId] OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY";

        vm.IsRawSqlValid.Should().BeTrue("take / skip はページング時に宣言済み扱い");
        vm.SqlDiagnostics.Should().NotContain(m => m.Contains("take") || m.Contains("skip"));
    }

    /// <summary>生 SQL 以外へ戻すと SQL 診断がクリアされ有効扱いになることを検証する</summary>
    [Fact(DisplayName = "生 SQL 以外へ戻すと SQL 診断はクリアされる")]
    public void RawSql_SwitchingAway_ClearsDiagnostics()
    {
        var entities = CreateEntities(out var order, out _, out _, out _, out _);
        var vm = new QueryItemViewModel(CreateQuery(order.Id), entities);

        vm.Implementation = QueryImplementationKind.Sql;
        vm.SqlServerSql = "SELECT * FROM [Order] WHERE [X] = @ghost";
        vm.IsRawSqlValid.Should().BeFalse();

        vm.Implementation = QueryImplementationKind.Manual;
        vm.IsRawSqlValid.Should().BeTrue();
        vm.SqlDiagnostics.Should().BeEmpty();
    }

    // ===== 子行の追加・削除コマンド =====

    /// <summary>パラメータ行の追加・削除がコレクションへ反映されることを検証する</summary>
    [Fact(DisplayName = "パラメータ行を追加・削除できる")]
    public void AddRemoveParameter()
    {
        var entities = CreateEntities(out var order, out _, out _, out _, out _);
        var vm = new QueryItemViewModel(CreateQuery(order.Id), entities);

        vm.AddParameterCommand.Execute(null);
        vm.Parameters.Should().ContainSingle();
        var added = vm.Parameters[0];
        added.Name.Should().Be("param");

        vm.RemoveParameterCommand.Execute(added);
        vm.Parameters.Should().BeEmpty();
    }

    /// <summary>並び順行の追加は先頭列を既定にし、削除で除去されることを検証する</summary>
    [Fact(DisplayName = "並び順行を追加・削除できる（既定は先頭列）")]
    public void AddRemoveOrdering()
    {
        var entities = CreateEntities(out var order, out var customerColumnId, out _, out _, out _);
        var vm = new QueryItemViewModel(CreateQuery(order.Id), entities);

        vm.AddOrderingCommand.Execute(null);
        vm.OrderBy.Should().ContainSingle();
        vm.OrderBy[0].ColumnId.Should().Be(customerColumnId, "既定は利用可能列の先頭");

        vm.RemoveOrderingCommand.Execute(vm.OrderBy[0]);
        vm.OrderBy.Should().BeEmpty();
    }

    /// <summary>射影フィールド行の追加・削除がコレクションへ反映されることを検証する</summary>
    [Fact(DisplayName = "射影フィールド行を追加・削除できる")]
    public void AddRemoveField()
    {
        var entities = CreateEntities(out var order, out _, out _, out _, out _);
        var vm = new QueryItemViewModel(CreateQuery(order.Id), entities);

        vm.AddFieldCommand.Execute(null);
        vm.Fields.Should().ContainSingle();
        vm.Fields[0].Name.Should().Be("Field");

        vm.RemoveFieldCommand.Execute(vm.Fields[0]);
        vm.Fields.Should().BeEmpty();
    }

    // ===== モデル化（ToModel） =====

    /// <summary>ToModel が Id を保持し、名前・スカラー型をトリムして返すことを検証する</summary>
    [Fact(DisplayName = "ToModel は Id を保持し名前・スカラー型をトリムする")]
    public void ToModel_PreservesIdAndTrims()
    {
        var entities = CreateEntities(out var order, out _, out _, out _, out _);
        var vm = new QueryItemViewModel(CreateQuery(order.Id), entities)
        {
            Name = "  GetOrders  ",
            Implementation = QueryImplementationKind.Sql,
            Returns = QueryReturnShape.Scalar,
            ScalarType = "  decimal(12,2)  ",
        };

        var model = vm.ToModel();

        model.Id.Should().Be(vm.Id);
        model.Name.Should().Be("GetOrders");
        model.ScalarType.Should().Be("decimal(12,2)");
    }

    /// <summary>Condition は DSL 実装かつ非空のときだけ保存され、それ以外は null になることを検証する</summary>
    [Fact(DisplayName = "Condition は DSL かつ非空のときだけ保存される")]
    public void ToModel_ConditionOnlyWhenDslAndNonEmpty()
    {
        var entities = CreateEntities(out var order, out _, out _, out _, out _);
        var source = CreateQuery(order.Id);
        source.Parameters.Add(new QueryParameter { Name = "customerId", Type = "int32" });
        var vm = new QueryItemViewModel(source, entities)
        {
            Condition = "CustomerId = @customerId",
        };

        vm.ToModel().Condition.Should().Be("CustomerId = @customerId");

        // 生 SQL へ切り替えると（条件テキストは残るが）保存されない
        vm.Implementation = QueryImplementationKind.Sql;
        vm.ToModel().Condition.Should().BeNull();
    }

    /// <summary>Sql 辞書は生 SQL 実装かつ非空のときだけキーが載ることを検証する</summary>
    [Fact(DisplayName = "Sql 辞書は生 SQL かつ非空のときだけ載る")]
    public void ToModel_SqlDictionaryOnlyWhenSqlAndNonEmpty()
    {
        var entities = CreateEntities(out var order, out _, out _, out _, out _);
        var vm = new QueryItemViewModel(CreateQuery(order.Id), entities)
        {
            SqlServerSql = "SELECT 1",
            SqliteSql = "   ", // 空白のみは載らない
        };

        // DSL 実装のときは SQL 本文があっても辞書は空
        vm.ToModel().Sql.Should().BeEmpty();

        vm.Implementation = QueryImplementationKind.Sql;
        var model = vm.ToModel();
        model.Sql.Should().ContainKey("sqlserver");
        model.Sql["sqlserver"].Should().Be("SELECT 1");
        model.Sql.Should().NotContainKey("sqlite", "空白のみの方言 SQL は保存しない");
    }

    /// <summary>列参照のパラメータ・射影フィールドは ToModel で Type を保存せず（null）、トークン型付けは保持することを検証する</summary>
    [Fact(DisplayName = "列参照のパラメータ・射影は Type を保存しない")]
    public void ToModel_ColumnReferencedType_NotPersisted()
    {
        var entities = CreateEntities(
            out var order,
            out var customerColumnId,
            out var amountColumnId,
            out _,
            out _
        );
        var vm = new QueryItemViewModel(CreateQuery(order.Id), entities)
        {
            Returns = QueryReturnShape.Projection,
        };

        // 列参照パラメータ ＋ トークン型付けパラメータ
        vm.AddParameterCommand.Execute(null);
        vm.Parameters[0].SourceColumnId = customerColumnId; // 列由来
        vm.AddParameterCommand.Execute(null);
        vm.Parameters[1].Type = "int32"; // トークン型付け

        // 列参照フィールド ＋ 自由フィールド
        vm.AddFieldCommand.Execute(null);
        vm.Fields[0].SourceColumnId = amountColumnId;
        vm.AddFieldCommand.Execute(null);
        vm.Fields[1].Type = "decimal(12,2)";

        var model = vm.ToModel();

        model.Parameters[0].Type.Should().BeNull("列参照は列由来のため型を保存しない");
        model.Parameters[0].SourceColumnId.Should().Be(customerColumnId);
        model.Parameters[1].Type.Should().Be("int32");
        model.Fields[0].Type.Should().BeNull();
        model.Fields[1].Type.Should().Be("decimal(12,2)");
    }

    /// <summary>並び順・ページングが ToModel に反映されることを検証する</summary>
    [Fact(DisplayName = "並び順・ページングが ToModel に反映される")]
    public void ToModel_OrderByAndPaging()
    {
        var entities = CreateEntities(out var order, out var customerColumnId, out _, out _, out _);
        var vm = new QueryItemViewModel(CreateQuery(order.Id), entities) { HasPaging = true };

        vm.AddOrderingCommand.Execute(null);
        vm.OrderBy[0].Descending = true;

        var model = vm.ToModel();

        model.HasPaging.Should().BeTrue();
        model.OrderBy.Should().ContainSingle();
        model.OrderBy[0].ColumnId.Should().Be(customerColumnId);
        model.OrderBy[0].Descending.Should().BeTrue();
    }

    // ===== シグネチャプレビュー =====

    /// <summary>一覧戻り形のシグネチャプレビューが戻り型・メソッド名・CancellationToken を含むことを検証する</summary>
    [Fact(DisplayName = "シグネチャプレビューは戻り型・メソッド名・CancellationToken を含む")]
    public void SignaturePreview_ListShape()
    {
        var entities = CreateEntities(out var order, out _, out _, out _, out _);
        var vm = new QueryItemViewModel(CreateQuery(order.Id, "GetByCustomer"), entities);

        vm.SignaturePreview.Should().Contain("Task<IReadOnlyList<OrderEntity>>");
        vm.SignaturePreview.Should().Contain("GetByCustomerAsync");
        vm.SignaturePreview.Should().Contain("CancellationToken cancellationToken = default");
    }

    /// <summary>パラメータ（リスト型）・ページングがシグネチャプレビューの引数へ反映されることを検証する</summary>
    [Fact(DisplayName = "パラメータ・ページングがシグネチャプレビューに反映される")]
    public void SignaturePreview_ParametersAndPaging()
    {
        var entities = CreateEntities(out var order, out _, out _, out _, out _);
        var vm = new QueryItemViewModel(CreateQuery(order.Id), entities) { HasPaging = true };

        vm.AddParameterCommand.Execute(null);
        vm.Parameters[0].Name = "ids";
        vm.Parameters[0].Type = "int32";
        vm.Parameters[0].IsList = true;

        var preview = vm.SignaturePreview;
        preview
            .Should()
            .Contain("IReadOnlyList<int> ids", "リスト型パラメータは IReadOnlyList<> になる");
        preview.Should().Contain("int take");
        preview.Should().Contain("int skip = 0");
    }

    /// <summary>射影戻り形のシグネチャプレビューが DTO 型名（未指定なら Row）を使うことを検証する</summary>
    [Fact(DisplayName = "射影プレビューは DTO 型名（未指定は Row）を使う")]
    public void SignaturePreview_ProjectionShape()
    {
        var entities = CreateEntities(out var order, out _, out _, out _, out _);
        var vm = new QueryItemViewModel(CreateQuery(order.Id), entities)
        {
            Returns = QueryReturnShape.Projection,
        };

        vm.SignaturePreview.Should().Contain("Task<IReadOnlyList<Row>>", "DTO 型名未指定は Row");

        vm.ResultTypeName = "OrderRow";
        vm.SignaturePreview.Should().Contain("Task<IReadOnlyList<OrderRow>>");
    }

    // ===== 親通知 =====

    /// <summary>名前・エンティティ・戻り形などの変更で親コールバックが呼ばれることを検証する（OK 可否の再評価トリガ）</summary>
    [Fact(DisplayName = "編集内容の変更で親コールバックが呼ばれる")]
    public void NotifiesParent_OnEdits()
    {
        var entities = CreateEntities(out var order, out _, out _, out var product, out _);
        var count = 0;
        var vm = new QueryItemViewModel(CreateQuery(order.Id), entities, () => count++);

        vm.Name = "Renamed";
        count.Should().BeGreaterThan(0, "名前変更で親へ通知する");

        var afterName = count;
        vm.EntityId = product.Id;
        count.Should().BeGreaterThan(afterName, "エンティティ変更で親へ通知する");

        var afterEntity = count;
        vm.Returns = QueryReturnShape.Single;
        count.Should().BeGreaterThan(afterEntity, "戻り形変更で親へ通知する");
    }
}
