using QuickER.CodeGen.CSharp.Queries;
using QuickER.CodeGen.UI;
using QuickER.Model;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.CodeGen.UI;

/// <summary>
/// クエリ定義ダイアログの検証（<see cref="QueryDefinitionDialogViewModel"/>）へ名前の総当たり（<see cref="NameFuzz"/>）を流し、
/// クエリ名・パラメータ名・射影のフィールド名の重複の判定が <see cref="NameIdentityRule"/> と同じ答えになることを
/// 確かめるテストクラス。
/// </summary>
/// <remarks>
/// 名前の組を持つ定義を図に置いてダイアログを開き、検証のメッセージが出ない（OK できる）ことを「受け付けた」とみなす。
/// 結果は既知の失敗の一覧（<see cref="NameFuzzKnownFailures"/>）と厳密に照合する。
/// </remarks>
public sealed class NameIdentityQueryDialogFuzzTests
{
    [Fact(DisplayName = "クエリ定義ダイアログの重複の判定は名前の規則と同じ答えになる")]
    public void Dialog_AgreesWithNameRule()
    {
        var ledger = new FuzzFailureLog(NameFuzzKnownFailures.Set);

        NameFuzzRunner.Run(
            ledger,
            "query-dialog:query_name",
            NameKind.Query,
            name => Validate(entity => [Query(entity, name)]).Accepted,
            pair =>
                Validate(
                    entity => [Query(entity, pair.First), Query(entity, pair.Second)],
                    pair.Second,
                    queries => queries[1].Name
                ).Outcome,
            QueryDefinitionRules.ToMethodName
        );
        NameFuzzRunner.Run(
            ledger,
            "query-dialog:parameter",
            NameKind.Parameter,
            name => Validate(entity => [WithParameters(Query(entity, "Probe"), name)]).Accepted,
            pair =>
                Validate(
                    entity => [WithParameters(Query(entity, "Probe"), pair.First, pair.Second)],
                    pair.Second,
                    queries => queries[0].Parameters[1].Name
                ).Outcome
        );
        NameFuzzRunner.Run(
            ledger,
            "query-dialog:field",
            NameKind.ProjectionField,
            name => Validate(entity => [WithFields(entity, name)]).Accepted,
            pair =>
                Validate(
                    entity => [WithFields(entity, pair.First, pair.Second)],
                    pair.Second,
                    queries => queries[0].Fields[1].Name
                ).Outcome
        );

        ledger.AssertMatchesKnown("query-dialog:");
    }

    /// <summary>テーブル T(Id, A) と、渡した定義を持つ図でダイアログを開き、検証のメッセージを見る</summary>
    /// <param name="queries">図に置く定義</param>
    /// <param name="second">後の名前（保存形の表明に使う・null は表明しない）</param>
    /// <param name="storedSecond">OK で確定した定義から、後の名前を保存した値を取り出す</param>
    private static (bool Accepted, PairOutcome Outcome) Validate(
        Func<Entity, IEnumerable<QueryDefinition>> queries,
        string? second = null,
        Func<IReadOnlyList<QueryDefinition>, string>? storedSecond = null
    )
    {
        var entity = new Entity
        {
            TableName = "T",
            Columns =
            [
                new Column
                {
                    Name = "Id",
                    DataType = "INTEGER",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column { Name = "A", DataType = "INTEGER" },
            ],
        };
        var diagram = new ErDiagram
        {
            TargetDbms = "sqlite",
            Entities = [entity],
            Queries = queries(entity).ToList(),
        };
        var vm = new QueryDefinitionDialogViewModel(diagram);
        var accepted = vm.StatusMessage.Length == 0;

        if (!accepted || second is null || storedSecond is null)
        {
            return (accepted, PairOutcome.Of(accepted, vm.StatusMessage));
        }

        // OK で確定する定義（ダイアログの ToModel）に、後の名前が保存形で入るか
        vm.OkCommand.Execute(null);
        var stored = storedSecond(vm.Result!);
        IReadOnlyList<string> violations =
            stored == NameIdentityRule.StoredForm(second) ? [] : ["stored-form-differs"];

        return (true, new PairOutcome(true, violations, $"stored [{NameFuzz.Describe(stored)}]"));
    }

    private static QueryDefinition Query(Entity entity, string name) =>
        new()
        {
            EntityId = entity.Id,
            Name = name,
            Returns = QueryReturnShape.List,
        };

    private static QueryDefinition WithParameters(QueryDefinition query, params string[] names)
    {
        query.Parameters = names
            .Select(name => new QueryParameter { Name = name, Type = "int32" })
            .ToList();

        return query;
    }

    private static QueryDefinition WithFields(Entity entity, params string[] names)
    {
        var query = Query(entity, "Probe");
        query.Returns = QueryReturnShape.Projection;
        query.ResultTypeName = "ProbeRow";
        query.Fields = names
            .Select(name => new ProjectionField
            {
                Name = name,
                SourceColumnId = entity.Columns[1].Id,
            })
            .ToList();

        return query;
    }
}
