using System.Linq;
using FluentAssertions;
using QuickER.CodeGen.UI;
using QuickER.Model;

namespace QuickER.Tests.ViewModels;

/// <summary>
/// <see cref="QueryDefinitionDialogViewModel" /> のロード・選択・追加削除・条件即時検証・複製編集境界・
/// 射影フィールドの列由来型を検証するテストクラス
/// </summary>
public class QueryDefinitionDialogViewModelTests
{
    /// <summary>Order エンティティ（CustomerId 主キー・Amount）と既存クエリ 1 件を持つ図を作る</summary>
    private static ErDiagram CreateDiagram(
        out Guid entityId,
        out Guid customerColumnId,
        out Guid amountColumnId
    )
    {
        var entity = new Entity { TableName = "Order" };
        var customer = new Column
        {
            Name = "CustomerId",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var amount = new Column { Name = "Amount", DataType = "decimal(12,2)" };
        entity.Columns.Add(customer);
        entity.Columns.Add(amount);

        entityId = entity.Id;
        customerColumnId = customer.Id;
        amountColumnId = amount.Id;

        return new ErDiagram
        {
            TargetDbms = "sqlserver",
            Entities = { entity },
            Queries =
            {
                new QueryDefinition
                {
                    EntityId = entity.Id,
                    Name = "GetByCustomer",
                    Condition = "CustomerId = @customerId",
                    Parameters =
                    {
                        new QueryParameter { Name = "customerId", Type = "int32" },
                    },
                },
            },
        };
    }

    /// <summary>
    /// 条件・並び順・射影フィールドをひととおり設定した後にエンティティを変更しても、
    /// 例外にならず検証エラー（旧エンティティの列参照）として扱われることを検証する。
    /// </summary>
    [Fact(DisplayName = "設定済みクエリのエンティティ変更は例外にならない")]
    public void ChangingEntity_AfterFullSetup_DoesNotThrow()
    {
        var diagram = CreateDiagram(out var entityId, out var customerColumnId, out _);

        // 2 つ目のエンティティ（列構成が異なる）を追加する
        var product = new Entity { TableName = "Product" };
        product.Columns.Add(
            new Column
            {
                Name = "ProductId",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        diagram.Entities.Add(product);

        var vm = new QueryDefinitionDialogViewModel(diagram);
        var query = vm.SelectedQuery!;

        // 条件・並び順・射影までひととおり設定する
        query.Returns = QueryReturnShape.Projection;
        query.ResultTypeName = "OrderRow";
        query.HasPaging = true;
        query.AddOrderingCommand.Execute(null);
        query.AddFieldCommand.Execute(null);
        query.Fields[0].SourceColumnId = customerColumnId;

        // エンティティを別のエンティティへ変更してもクラッシュしない
        var act = () => query.EntityId = product.Id;
        act.Should().NotThrow();

        // 旧エンティティの列を参照する条件は検証エラーとして表面化する（静かに壊れない）
        query.IsConditionValid.Should().BeFalse();
        query.ConditionDiagnostics.Should().NotBeEmpty();

        // 列選択肢は新エンティティの列に入れ替わる
        vm.SelectedQuery!.AvailableColumns.Select(c => c.Name).Should().Equal("ProductId");

        // 旧エンティティの列を参照していた並び順行は取り除かれる
        query.OrderBy.Should().BeEmpty();

        // 射影フィールドの参照元列は「なし（自由フィールド）」へ解除され、名前・型は保持される
        query.Fields[0].SourceColumnId.Should().BeNull();
        query.Fields[0].Name.Should().Be("Field");
        query.Fields[0].Type.Should().NotBeNullOrEmpty();

        // OK 側の検証も例外なく機能する（押せないだけ）
        vm.OkCommand.CanExecute(null).Should().BeFalse();
    }

    /// <summary>パラメータの列参照型付け：列を選ぶと型が列由来・編集不可になり、ToModel へ載り、解除で戻ることを検証する</summary>
    [Fact(DisplayName = "パラメータの列参照型付けが編集・確定に反映される")]
    public void ParameterSourceColumn_RoundTripsThroughEditing()
    {
        var diagram = CreateDiagram(out _, out var customerColumnId, out _);
        var vm = new QueryDefinitionDialogViewModel(diagram);
        var query = vm.SelectedQuery!;

        query.AddParameterCommand.Execute(null);
        var parameter = query.Parameters[^1];
        parameter.Name = "typedId";

        parameter.SourceColumnId = customerColumnId;

        parameter.IsTypeEditable.Should().BeFalse("列参照時は型トークンを手入力できない");
        parameter.Type.Should().Be("int", "型表示は列の宣言型由来になる");
        query.ToModel().Parameters[^1].SourceColumnId.Should().Be(customerColumnId);

        // 「なし」へ戻すとトークン型付けへ復帰する（型表示は保持）
        parameter.SourceColumnId = null;
        parameter.IsTypeEditable.Should().BeTrue();
        query.ToModel().Parameters[^1].SourceColumnId.Should().BeNull();
    }

    /// <summary>
    /// 列参照（SourceColumnId 非 null）のパラメータ・射影フィールドは、確定（ToModel）時に
    /// 表示用の型トークンを保存しない（Type=null）ことを検証する。列由来で型が決まるため、
    /// 列の宣言型（SQL ネイティブ型）が契約外の値として図ファイルへ漏れるのを防ぐ。
    /// </summary>
    [Fact(DisplayName = "列参照のパラメータ・射影フィールドは Type を保存しない")]
    public void ToModel_ColumnReferencedTypeToken_IsNotPersisted()
    {
        var diagram = CreateDiagram(out _, out var customerColumnId, out var amountColumnId);
        var vm = new QueryDefinitionDialogViewModel(diagram);
        var query = vm.SelectedQuery!;

        // 列参照パラメータ（表示は列由来の型トークンになるが保存はされない）
        query.AddParameterCommand.Execute(null);
        var typedParameter = query.Parameters[^1];
        typedParameter.Name = "typedId";
        typedParameter.SourceColumnId = customerColumnId;

        // 射影フィールド（列参照と自由フィールドを 1 件ずつ）
        query.ReturnsProjection = true;
        query.ResultTypeName = "OrderRow";
        query.AddFieldCommand.Execute(null);
        var columnField = query.Fields[^1];
        columnField.Name = "Amount";
        columnField.SourceColumnId = amountColumnId;
        query.AddFieldCommand.Execute(null);
        var freeField = query.Fields[^1];
        freeField.Name = "Total";
        freeField.Type = "decimal(12,2)";

        var model = query.ToModel();

        // 列参照は Type=null・トークン型付けは Type を保持する
        model.Parameters[^1].Type.Should().BeNull("列参照の型は列由来のため保存しない");
        model.Parameters[0].Type.Should().Be("int32", "トークン型付けは従来どおり保存する");
        model.Fields[0].Type.Should().BeNull();
        model.Fields[1].Type.Should().Be("decimal(12,2)");
    }

    /// <summary>既存クエリがロードされ、選択切替で対象が入れ替わることを検証する（要件 a）</summary>
    [Fact(DisplayName = "既存クエリのロードと選択切替")]
    public void LoadsExistingQueries_AndSwitchesSelection()
    {
        var diagram = CreateDiagram(out var entityId, out _, out _);
        var vm = new QueryDefinitionDialogViewModel(diagram);

        vm.Queries.Should().ContainSingle();
        vm.SelectedQuery.Should().NotBeNull();
        vm.SelectedQuery!.Name.Should().Be("GetByCustomer");
        vm.SelectedQuery.EntityName.Should().Be("Order");
        vm.SelectedQuery.EntityId.Should().Be(entityId);

        // 追加してから選択を切り替えると SelectedQuery が入れ替わる
        vm.AddQueryCommand.Execute(null);
        vm.Queries.Should().HaveCount(2);
        var added = vm.Queries[1];
        vm.SelectedQuery.Should().BeSameAs(added);

        vm.SelectedQuery = vm.Queries[0];
        vm.SelectedQuery.Name.Should().Be("GetByCustomer");
    }

    /// <summary>追加・削除が一覧に反映されることを検証する（要件 b）</summary>
    [Fact(DisplayName = "クエリの追加・削除が一覧へ反映される")]
    public void AddAndRemove_UpdateQueryList()
    {
        var diagram = CreateDiagram(out var entityId, out _, out _);
        var vm = new QueryDefinitionDialogViewModel(diagram);

        vm.AddQueryCommand.Execute(null);
        vm.Queries.Should().HaveCount(2);
        // 新規クエリの既定エンティティは先頭
        vm.Queries[1].EntityId.Should().Be(entityId);

        vm.SelectedQuery = vm.Queries[1];
        vm.RemoveQueryCommand.Execute(null);
        vm.Queries.Should().ContainSingle();
        vm.Queries[0].Name.Should().Be("GetByCustomer");
    }

    /// <summary>条件の即時検証が、不正な列参照で診断を出し、修正で消えることを検証する（要件 c）</summary>
    [Fact(DisplayName = "条件の即時検証が不正列で診断を出し修正で消える")]
    public void Condition_ValidatesImmediately()
    {
        var diagram = CreateDiagram(out _, out _, out _);
        var vm = new QueryDefinitionDialogViewModel(diagram);
        var query = vm.SelectedQuery!;

        // 存在しない列を参照 → 診断が出て無効・OK 不可
        query.Condition = "NoSuchColumn = @customerId";
        query.IsConditionValid.Should().BeFalse();
        query.ConditionDiagnostics.Should().NotBeEmpty();
        vm.OkCommand.CanExecute(null).Should().BeFalse();

        // 正しい列へ修正 → 診断が消えて有効・OK 可
        query.Condition = "CustomerId = @customerId";
        query.IsConditionValid.Should().BeTrue();
        query.ConditionDiagnostics.Should().BeEmpty();
        vm.OkCommand.CanExecute(null).Should().BeTrue();
    }

    /// <summary>OK が編集結果を返し、キャンセルは元定義に影響しないことを検証する（要件 d）</summary>
    [Fact(DisplayName = "OK は編集結果を返し、キャンセルは元へ影響しない")]
    public void Ok_ReturnsResult_Cancel_DoesNotMutateSource()
    {
        // --- キャンセル: 元の QueryDefinition は変更されない ---
        var diagram = CreateDiagram(out _, out _, out _);
        var originalDefinition = diagram.Queries[0];
        var cancelVm = new QueryDefinitionDialogViewModel(diagram);
        bool? cancelClosed = null;
        cancelVm.CloseAction = result => cancelClosed = result;

        cancelVm.SelectedQuery!.Name = "Renamed";
        cancelVm.CancelCommand.Execute(null);

        cancelClosed.Should().BeFalse();
        cancelVm.Result.Should().BeNull();
        // 編集は複製に対して行われるため、入力の定義は元のまま
        originalDefinition.Name.Should().Be("GetByCustomer");

        // --- OK: 編集結果が確定リストへ反映される ---
        var okVm = new QueryDefinitionDialogViewModel(diagram);
        bool? okClosed = null;
        okVm.CloseAction = result => okClosed = result;

        okVm.SelectedQuery!.Name = "GetByCustomerId";
        okVm.OkCommand.Execute(null);

        okClosed.Should().BeTrue();
        okVm.Result.Should().NotBeNull();
        okVm.Result!.Should().ContainSingle();
        okVm.Result[0].Name.Should().Be("GetByCustomerId");
        // OK 後も元定義は不変（結果は別インスタンス）
        originalDefinition.Name.Should().Be("GetByCustomer");
    }

    /// <summary>射影フィールドの参照元列切替で型トークンが列由来になり、外すと編集可能へ戻ることを検証する（要件 e）</summary>
    [Fact(DisplayName = "射影フィールドの参照元列切替で型トークンが列由来になる")]
    public void ProjectionField_SourceColumn_DrivesTypeToken()
    {
        var diagram = CreateDiagram(out _, out _, out var amountColumnId);
        var vm = new QueryDefinitionDialogViewModel(diagram);
        var query = vm.SelectedQuery!;

        query.Returns = QueryReturnShape.Projection;
        query.AddFieldCommand.Execute(null);
        var field = query.Fields.Should().ContainSingle().Subject;

        // 既定は自由フィールド（型を手入力可）
        field.IsTypeEditable.Should().BeTrue();

        // 参照元列（Amount = decimal(12,2)）を選ぶと型が列由来になり編集不可
        field.SourceColumnId = amountColumnId;
        field.IsTypeEditable.Should().BeFalse();
        field.Type.Should().Be("decimal(12,2)");

        // 「なし」へ戻すと再び手入力可（列由来の値は保持）
        field.SourceColumnId = null;
        field.IsTypeEditable.Should().BeTrue();
    }

    /// <summary>実装方式ごとに項目の出し分けフラグ（DSL 面・SQL 面・スカラー可否）が切り替わることを検証する</summary>
    [Fact(DisplayName = "実装方式で表示フラグが切り替わる")]
    public void Implementation_TogglesDisplayFlags()
    {
        var diagram = CreateDiagram(out _, out _, out _);
        var vm = new QueryDefinitionDialogViewModel(diagram);
        var query = vm.SelectedQuery!;

        // 簡易 DSL: 条件・並び順（DSL 面）を表示・SQL 欄は隠す・スカラーは選べない
        query.Implementation = QueryImplementationKind.Dsl;
        query.IsDslImplementation.Should().BeTrue();
        query.ShowSqlEditors.Should().BeFalse();
        query.CanSelectScalar.Should().BeFalse();

        // 生 SQL: SQL 欄を表示・DSL 面は隠す・スカラーを選べる
        query.Implementation = QueryImplementationKind.Sql;
        query.ShowSqlEditors.Should().BeTrue();
        query.IsDslImplementation.Should().BeFalse();
        query.CanSelectScalar.Should().BeTrue();

        // 手動実装: DSL 面・SQL 面ともに隠す・スカラーを選べる
        query.Implementation = QueryImplementationKind.Manual;
        query.IsDslImplementation.Should().BeFalse();
        query.ShowSqlEditors.Should().BeFalse();
        query.CanSelectScalar.Should().BeTrue();
    }

    /// <summary>スカラー選択中に DSL へ切り替えると、戻り値の型が既定（一覧）へリセットされることを検証する</summary>
    [Fact(DisplayName = "スカラー選択中の DSL 切替は戻り値の型を一覧へリセットする")]
    public void ScalarReturn_SwitchingToDsl_ResetsReturnsToList()
    {
        var diagram = CreateDiagram(out _, out _, out _);
        var vm = new QueryDefinitionDialogViewModel(diagram);
        var query = vm.SelectedQuery!;

        // 生 SQL + スカラーは成立する（OK 可能）
        query.Implementation = QueryImplementationKind.Sql;
        query.Returns = QueryReturnShape.Scalar;
        query.ScalarType = "int32";
        vm.OkCommand.CanExecute(null).Should().BeTrue();
        vm.StatusMessage.Should().BeEmpty();

        // DSL へ切り替えるとスカラーが解除され一覧へ戻る（不正な組合せのまま残らない）
        query.Implementation = QueryImplementationKind.Dsl;
        query.Returns.Should().Be(QueryReturnShape.List);
        query.ReturnsScalar.Should().BeFalse();
        query.ShowScalarType.Should().BeFalse();
        vm.OkCommand.CanExecute(null).Should().BeTrue();
        vm.StatusMessage.Should().BeEmpty();
    }

    /// <summary>
    /// 既存図にスカラー×DSL の不正な定義が保存されていた場合（読み込みはフィールド直接代入で
    /// リセットを通らない）、フォーム検証が OK を防ぐことを検証する。
    /// </summary>
    [Fact(DisplayName = "読み込んだスカラー×DSL 定義はフォームエラーで OK 不可になる")]
    public void LoadedScalarDslDefinition_BlocksOk()
    {
        var diagram = CreateDiagram(out var entityId, out _, out _);
        diagram.Queries.Add(
            new QueryDefinition
            {
                EntityId = entityId,
                Name = "SumAmounts",
                Returns = QueryReturnShape.Scalar,
                ScalarType = "decimal(12,2)",
                Implementation = QueryImplementationKind.Dsl,
            }
        );

        var vm = new QueryDefinitionDialogViewModel(diagram);
        vm.SelectedQuery = vm.Queries.Single(q => q.Name == "SumAmounts");

        // 不正な組合せのまま読み込まれ、OK は専用メッセージで防がれる
        vm.SelectedQuery.Returns.Should().Be(QueryReturnShape.Scalar);
        vm.OkCommand.CanExecute(null).Should().BeFalse();
        vm.StatusMessage.Should()
            .Be(QuickER.CodeGen.UI.Resources.Strings.QueryDialog_ScalarRequiresSqlOrManual);

        // 生 SQL へ切り替えれば（スカラーのまま）解消する
        vm.SelectedQuery.Implementation = QueryImplementationKind.Sql;
        vm.OkCommand.CanExecute(null).Should().BeTrue();
        vm.StatusMessage.Should().BeEmpty();
    }

    /// <summary>
    /// 生 SQL の未宣言パラメータがインライン診断として表示され、OK をブロックすること・
    /// パラメータを宣言すると解消することを検証する。
    /// </summary>
    [Fact(DisplayName = "生 SQL 未宣言パラメータはインライン表示され OK をブロックする")]
    public void RawSql_UndeclaredParameter_ShowsInlineAndBlocksOk()
    {
        var diagram = CreateDiagram(out _, out _, out _);
        var vm = new QueryDefinitionDialogViewModel(diagram);
        var query = vm.SelectedQuery!;

        // 既存の条件付き DSL クエリを生 SQL へ切り替え、宣言に無い @ghost を参照する
        query.Implementation = QueryImplementationKind.Sql;
        query.SqlServerSql = "SELECT * FROM [Order] WHERE [X] = @ghost";

        query.IsRawSqlValid.Should().BeFalse();
        query.SqlDiagnostics.Should().Contain(m => m.Contains("ghost"));
        vm.OkCommand.CanExecute(null).Should().BeFalse();
        vm.StatusMessage.Should()
            .Be(QuickER.CodeGen.UI.Resources.Strings.QueryDialog_Status_RawSqlInvalid);

        // @ghost を参照する SQL を、宣言済み @customerId を使うものに直すと解消する
        query.SqlServerSql = "SELECT * FROM [Order] WHERE [CustomerId] = @customerId";
        query.IsRawSqlValid.Should().BeTrue();
        query.SqlDiagnostics.Should().BeEmpty();
        vm.OkCommand.CanExecute(null).Should().BeTrue();
        vm.StatusMessage.Should().BeEmpty();
    }

    /// <summary>生 SQL の未使用パラメータ・複文はインライン表示のみで OK をブロックしないことを検証する</summary>
    [Fact(DisplayName = "生 SQL 未使用・複文はインライン表示のみで OK を通す")]
    public void RawSql_UnusedAndMultiStatement_DoNotBlockOk()
    {
        var diagram = CreateDiagram(out _, out _, out _);
        var vm = new QueryDefinitionDialogViewModel(diagram);
        var query = vm.SelectedQuery!;

        // 既存クエリは @customerId を宣言済み。使わない SQL ＋ 複文にする
        query.Implementation = QueryImplementationKind.Sql;
        query.SqlServerSql = "SELECT * FROM [Order]; SELECT 1";

        // 未使用（customerId）と複文の 2 件がインライン表示される
        query.SqlDiagnostics.Should().HaveCount(2);
        // どちらも OK はブロックしない
        query.IsRawSqlValid.Should().BeTrue();
        vm.OkCommand.CanExecute(null).Should().BeTrue();
        vm.StatusMessage.Should().BeEmpty();
    }

    /// <summary>実装方式を生 SQL 以外へ戻すと SQL 診断がクリアされ有効扱いになることを検証する</summary>
    [Fact(DisplayName = "生 SQL 以外へ切り替えると SQL 診断はクリアされる")]
    public void RawSql_SwitchingAwayFromSql_ClearsDiagnostics()
    {
        var diagram = CreateDiagram(out _, out _, out _);
        var vm = new QueryDefinitionDialogViewModel(diagram);
        var query = vm.SelectedQuery!;

        query.Implementation = QueryImplementationKind.Sql;
        query.SqlServerSql = "SELECT * FROM [Order] WHERE [X] = @ghost";
        query.IsRawSqlValid.Should().BeFalse();

        // 手動実装へ戻すと SQL 検証はスキップ（有効扱い）・診断はクリア
        query.Implementation = QueryImplementationKind.Manual;
        query.IsRawSqlValid.Should().BeTrue();
        query.SqlDiagnostics.Should().BeEmpty();
    }

    /// <summary>非表示になる項目（条件・並び順・SQL 本文）が実装方式の往復でクリアされず保持されることを検証する</summary>
    [Fact(DisplayName = "実装方式の往復で入力値が保持される")]
    public void SwitchingImplementation_RetainsInputs()
    {
        var diagram = CreateDiagram(out _, out _, out _);
        var vm = new QueryDefinitionDialogViewModel(diagram);
        var query = vm.SelectedQuery!;

        // DSL 面（条件・並び順）と SQL 面（本文）をひととおり入力する
        query.Implementation = QueryImplementationKind.Dsl;
        query.Condition = "CustomerId = @customerId";
        query.AddOrderingCommand.Execute(null);
        query.SqlServerSql = "SELECT * FROM [Order]";

        query.OrderBy.Should().ContainSingle();

        // 手動実装へ切り替えても入力は失われない
        query.Implementation = QueryImplementationKind.Manual;
        query.Condition.Should().Be("CustomerId = @customerId");
        query.OrderBy.Should().ContainSingle();
        query.SqlServerSql.Should().Be("SELECT * FROM [Order]");

        // DSL へ戻すと条件・並び順が残っている
        query.Implementation = QueryImplementationKind.Dsl;
        query.Condition.Should().Be("CustomerId = @customerId");
        query.OrderBy.Should().ContainSingle();

        // 生 SQL へ戻すと SQL 本文が残っている
        query.Implementation = QueryImplementationKind.Sql;
        query.SqlServerSql.Should().Be("SELECT * FROM [Order]");
    }
}
