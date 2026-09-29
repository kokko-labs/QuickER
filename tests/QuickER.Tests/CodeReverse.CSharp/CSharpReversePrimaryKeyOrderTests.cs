using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.CodeReverse.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.SqlServer;
using ReverseStrings = QuickER.CodeReverse.CSharp.Resources.Strings;

namespace QuickER.Tests.CodeReverse.CSharp;

/// <summary>
/// 複合主キー自身の列順序を、生成コードの <c>[DbTableMeta(PrimaryKeyOrder = …)]</c> で往復させることを検証する。
/// </summary>
/// <remarks>
/// <para>
/// 主キーの順序は DDL の主キー句と DB 同期の主キー比較にそのまま使われるため、「図 → 生成コード → C# リバース」で
/// 落ちると、次の同期で実 DB と食い違って主キーの張り直しが出る。
/// </para>
/// <para>
/// 生成側は <see cref="Entity.GetReorderedPrimaryKeyColumnNames"/> が食い違いを返すテーブルにだけ順序を載せ
/// （<c>[DbColumnMeta].NativeType</c> の verbatim 併記と同じ「食い違うときだけ出す」方式）、
/// リバース側は主キー列とちょうど一致するときだけ採る。GUI のマージ取込では、コードが順序を書いていない
/// （古い生成コード）ときに限り現在図の順序を引き継ぐ。
/// </para>
/// </remarks>
public class CSharpReversePrimaryKeyOrderTests
{
    private const string TableName = "order_lines";

    private static CodeGenerationOptions Options =>
        new()
        {
            RootNamespace = "QuickER.Tests.ReversePrimaryKeyOrder",
            GenerateValueObjects = false,
            SplitFilesByCategory = false,
        };

    /// <summary>
    /// 主キー 2 列（<c>tenant_id</c> → <c>order_no</c> の宣言順）＋通常列 1 つのテーブルを作る
    /// </summary>
    /// <param name="order">主キーの順序として明示する列名（<c>null</c>＝明示しない）</param>
    /// <param name="description">テーブルの説明</param>
    private static ErDiagram Diagram(string[]? order, string description = "")
    {
        var entity = new Entity
        {
            TableName = TableName,
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

    private static string Generate(ErDiagram diagram)
    {
        var provider = new SqlServerProvider();
        var generation = DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            diagram,
            Options
        );

        generation.HasErrors.Should().BeFalse();

        return generation.Files.Single().Content;
    }

    private static CodeReverseResult Reverse(string source) =>
        new CSharpReverseParser().Parse(source, new SqlServerTypeCatalog());

    /// <summary>リバース結果の主キーを実効順の列名で返す</summary>
    private static List<string> KeyOrder(Entity entity) =>
        entity.GetPrimaryKeyColumnsInOrder().Select(column => column.Name).ToList();

    /// <summary>宣言順と食い違う主キーの順序は、生成 → リバースの往復で戻る</summary>
    [Fact(DisplayName = "宣言順と食い違う主キーの順序は、生成 → リバースで戻る")]
    public void RoundTrip_ReorderedKey_IsRestored()
    {
        var source = Generate(Diagram(["order_no", "tenant_id"]));

        // 値は列名でなくプロパティ名（[UniqueConstraint] と同じ形）
        source
            .Should()
            .Contain("[DbTableMeta(PrimaryKeyOrder = new[] { \"OrderNo\", \"TenantId\" })]");

        var reversed = Reverse(source);

        KeyOrder(reversed.Entities.Single()).Should().Equal("order_no", "tenant_id");
        reversed.TablesWithPrimaryKeyOrder.Should().Contain(TableName);
        reversed.Warnings.Should().BeEmpty();
    }

    /// <summary>
    /// 順序を明示していない図・宣言順どおりに明示した図は属性を出さず、生成物がバイト単位で一致する
    /// </summary>
    [Fact(DisplayName = "宣言順どおりの主キーには順序を出さず、生成物は明示の有無で変わらない")]
    public void Generate_KeyInDeclarationOrder_EmitsNothingAndIsByteIdentical()
    {
        var unstated = Generate(Diagram(order: null));
        var pinned = Generate(Diagram(["tenant_id", "order_no"]));

        unstated.Should().NotContain("PrimaryKeyOrder =");
        pinned.Should().Be(unstated, "宣言順どおりの明示は、明示しないのと同じ生成物になる");
    }

    /// <summary>単一列の主キーには順序を出さない（並べ替える相手が無い）</summary>
    [Fact(DisplayName = "単一列の主キーには順序を出さない")]
    public void Generate_SingleColumnKey_EmitsNothing()
    {
        var diagram = Diagram(order: null);
        diagram.Entities[0].Columns[1].IsPrimaryKey = false;

        Generate(diagram).Should().NotContain("PrimaryKeyOrder =");
    }

    /// <summary>説明と順序の両方を持つテーブルは、1 つの属性へ両方を並べる（説明が先）</summary>
    [Fact(DisplayName = "説明と順序の両方を持つテーブルは 1 つの属性に並べる")]
    public void Generate_DescriptionAndOrder_ShareOneAttribute()
    {
        var source = Generate(Diagram(["order_no", "tenant_id"], description: "Order lines"));

        source
            .Should()
            .Contain(
                "[DbTableMeta(Description = \"Order lines\", PrimaryKeyOrder = new[] { \"OrderNo\", \"TenantId\" })]"
            );

        var reversed = Reverse(source).Entities.Single();

        reversed.Description.Should().Be("Order lines");
        KeyOrder(reversed).Should().Equal("order_no", "tenant_id");
    }

    /// <summary>配列の書き方（暗黙型・型付き・コレクション式）のどれでも読める</summary>
    [Theory(DisplayName = "主キーの順序は配列の 3 つの書き方のどれでも読める")]
    [InlineData("new string[] { \"OrderNo\", \"TenantId\" }")]
    [InlineData("[\"OrderNo\", \"TenantId\"]")]
    public void Reverse_AcceptsEveryArrayForm(string arrayExpression)
    {
        var source = Generate(Diagram(["order_no", "tenant_id"]))
            .Replace(
                "new[] { \"OrderNo\", \"TenantId\" }",
                arrayExpression,
                StringComparison.Ordinal
            );

        var reversed = Reverse(source);

        KeyOrder(reversed.Entities.Single()).Should().Equal("order_no", "tenant_id");
        reversed.Warnings.Should().BeEmpty();
    }

    /// <summary>
    /// 主キー列とちょうど一致しない順序は採らずに警告し、宣言順へ戻す
    /// （未知の名前・主キーでない列・重複・過不足・リテラルでない要素）
    /// </summary>
    [Theory(DisplayName = "主キー列と一致しない順序は採らずに警告し、宣言順へ戻す")]
    [InlineData("new[] { \"OrderNo\", \"Unknown\" }")]
    [InlineData("new[] { \"OrderNo\", \"Memo\" }")]
    [InlineData("new[] { \"OrderNo\", \"OrderNo\" }")]
    [InlineData("new[] { \"OrderNo\" }")]
    [InlineData("new[] { \"OrderNo\", \"TenantId\", \"Memo\" }")]
    [InlineData("new[] { nameof(OrderNo), \"TenantId\" }")]
    public void Reverse_MismatchedOrder_WarnsAndUsesDeclarationOrder(string arrayExpression)
    {
        var source = Generate(Diagram(["order_no", "tenant_id"]))
            .Replace(
                "new[] { \"OrderNo\", \"TenantId\" }",
                arrayExpression,
                StringComparison.Ordinal
            );

        var reversed = Reverse(source);
        var entity = reversed.Entities.Single();

        KeyOrder(entity).Should().Equal("tenant_id", "order_no");
        entity.PrimaryKeyColumnIds.Should().BeEmpty();
        reversed
            .Warnings.Should()
            .ContainSingle()
            .Which.Should()
            .Be(
                string.Format(
                    ReverseStrings.Reverse_PrimaryKeyOrderMismatch,
                    TableName,
                    arrayExpression
                )
            );
        // 不整合でも「コードが指定した」ことは伝える（GUI マージで現在図の順序を持ち込まない）
        reversed.TablesWithPrimaryKeyOrder.Should().Contain(TableName);
    }

    /// <summary>
    /// 古い生成コード（順序を書けない）を GUI でマージ取込しても、現在図の主キーの順序が保たれる
    /// </summary>
    /// <remarks>
    /// 引き継がないと宣言順へ戻り、次の DB 同期で主キーの張り直しが出る。
    /// 生成物から順序の引数だけを消して「古い版の生成コード」を作り、GUI と同じ手順（Guid 引継マージ →
    /// 後処理）を通す。
    /// </remarks>
    [Fact(DisplayName = "古い生成コードをマージ取込しても、現在図の主キーの順序が保たれる")]
    public void Merge_OldGeneratedCode_KeepsCurrentKeyOrder()
    {
        var current = Diagram(["order_no", "tenant_id"]);
        var oldSource = Generate(current)
            .Replace(
                "[DbTableMeta(PrimaryKeyOrder = new[] { \"OrderNo\", \"TenantId\" })]",
                string.Empty,
                StringComparison.Ordinal
            );
        oldSource
            .Should()
            .NotContain("PrimaryKeyOrder =", "順序の引数を持たない＝古い版の生成コード");

        var reversed = Reverse(oldSource);
        var merged = DiagramMergeReconciler.Reconcile(
            current,
            reversed.Entities.ToList(),
            reversed.Relationships.ToList(),
            preserveExistingMemo: true
        );

        ReverseMergePostProcessor.ApplyPrimaryKeyOrder(
            current,
            merged.Entities,
            reversed.TablesWithPrimaryKeyOrder
        );

        KeyOrder(merged.Entities.Single()).Should().Equal("order_no", "tenant_id");
    }

    /// <summary>コードが順序を書いていれば、現在図の順序で上書きしない（コードが勝つ）</summary>
    [Fact(DisplayName = "コードが順序を書いていれば、マージ取込は現在図の順序で上書きしない")]
    public void ApplyPrimaryKeyOrder_CodeSpecifiedOrder_Wins()
    {
        var current = Diagram(["order_no", "tenant_id"]);
        var merged = current.Entities.Select(entity => entity.Clone(preserveId: true)).ToList();
        merged[0].PrimaryKeyColumnIds = [];

        ReverseMergePostProcessor.ApplyPrimaryKeyOrder(
            current,
            merged,
            new HashSet<string>(StringComparer.Ordinal) { TableName }
        );

        merged[0].PrimaryKeyColumnIds.Should().BeEmpty();
    }

    /// <summary>主キー列の集合が変わっていれば、古い順序は持ち込まない（コードが語った構成の変更）</summary>
    [Fact(DisplayName = "主キー列の集合が変わっていれば、マージ取込は古い順序を持ち込まない")]
    public void ApplyPrimaryKeyOrder_KeySetChanged_IsNotCarried()
    {
        var current = Diagram(["order_no", "tenant_id"]);
        var merged = current.Entities.Select(entity => entity.Clone(preserveId: true)).ToList();
        merged[0].PrimaryKeyColumnIds = [];
        merged[0].Columns.Single(column => column.Name == "memo").IsPrimaryKey = true;

        ReverseMergePostProcessor.ApplyPrimaryKeyOrder(
            current,
            merged,
            new HashSet<string>(StringComparer.Ordinal)
        );

        merged[0].PrimaryKeyColumnIds.Should().BeEmpty();
    }
}
