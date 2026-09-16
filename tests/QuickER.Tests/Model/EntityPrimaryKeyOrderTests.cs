using AwesomeAssertions;
using QuickER.Model;

namespace QuickER.Tests.Model;

/// <summary>
/// <see cref="Entity.PrimaryKeyColumnIds"/> と <see cref="Entity.GetPrimaryKeyColumnsInOrder"/> の
/// 実効順の解決規則（膜＝<see cref="Column.IsPrimaryKey"/>・順序＝上書きリスト）を検証するテストクラス
/// </summary>
public class EntityPrimaryKeyOrderTests
{
    /// <summary>指定名の列を主キー指定で生成する</summary>
    private static Column Pk(string name) =>
        new()
        {
            Name = name,
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };

    /// <summary>指定名の列を非主キーで生成する</summary>
    private static Column Plain(string name) =>
        new()
        {
            Name = name,
            DataType = "int",
            IsNullable = true,
        };

    /// <summary>順序リストが空なら列宣言順がそのまま実効順になることを検証する</summary>
    [Fact(DisplayName = "GetPrimaryKeyColumnsInOrder: 順序リストが空なら列宣言順になる")]
    public void EmptyList_FallsBackToDeclarationOrder()
    {
        var a = Pk("a");
        var b = Pk("b");
        var entity = new Entity { TableName = "T", Columns = { a, Plain("memo"), b } };

        entity.GetPrimaryKeyColumnsInOrder().Select(c => c.Name).Should().Equal("a", "b");
    }

    /// <summary>順序リストが列宣言順より優先されることを検証する</summary>
    [Fact(DisplayName = "GetPrimaryKeyColumnsInOrder: 順序リストが列宣言順より優先される")]
    public void ExplicitList_OverridesDeclarationOrder()
    {
        var a = Pk("a");
        var b = Pk("b");
        var entity = new Entity
        {
            TableName = "T",
            Columns = { a, b },
            PrimaryKeyColumnIds = [b.Id, a.Id],
        };

        entity.GetPrimaryKeyColumnsInOrder().Select(c => c.Name).Should().Equal("b", "a");
    }

    /// <summary>順序リストに載っていない主キー列が列宣言順のまま末尾へ回ることを検証する</summary>
    [Fact(DisplayName = "GetPrimaryKeyColumnsInOrder: リスト外の主キー列は列宣言順で末尾へ回る")]
    public void ColumnsMissingFromList_GoToTailInDeclarationOrder()
    {
        var a = Pk("a");
        var b = Pk("b");
        var c = Pk("c");
        var entity = new Entity
        {
            TableName = "T",
            Columns = { a, b, c },
            // c だけが順序指定を持つ
            PrimaryKeyColumnIds = [c.Id],
        };

        entity.GetPrimaryKeyColumnsInOrder().Select(x => x.Name).Should().Equal("c", "a", "b");
    }

    /// <summary>順序リストの一部だけが指定されている場合も、残りが宣言順で続くことを検証する</summary>
    [Fact(DisplayName = "GetPrimaryKeyColumnsInOrder: 部分的な順序指定でも残りは宣言順で続く")]
    public void PartialList_KeepsDeclarationOrderForRest()
    {
        var a = Pk("a");
        var b = Pk("b");
        var c = Pk("c");
        var d = Pk("d");
        var entity = new Entity
        {
            TableName = "T",
            Columns = { a, b, c, d },
            PrimaryKeyColumnIds = [d.Id, b.Id],
        };

        entity.GetPrimaryKeyColumnsInOrder().Select(x => x.Name).Should().Equal("d", "b", "a", "c");
    }

    /// <summary>順序リストに載っていても主キーでない列は対象外になることを検証する</summary>
    [Fact(DisplayName = "GetPrimaryKeyColumnsInOrder: リスト内の非主キー列は無視される")]
    public void NonPrimaryKeyColumnInList_IsIgnored()
    {
        var a = Pk("a");
        var memo = Plain("memo");
        var entity = new Entity
        {
            TableName = "T",
            Columns = { a, memo },
            PrimaryKeyColumnIds = [memo.Id, a.Id],
        };

        entity.GetPrimaryKeyColumnsInOrder().Select(c => c.Name).Should().Equal("a");
    }

    /// <summary>このエンティティの列を指さない ID が実効順へ影響しないことを検証する</summary>
    [Fact(DisplayName = "GetPrimaryKeyColumnsInOrder: 解決できない ID は無視される")]
    public void UnresolvableId_IsIgnored()
    {
        var a = Pk("a");
        var b = Pk("b");
        var entity = new Entity
        {
            TableName = "T",
            Columns = { a, b },
            PrimaryKeyColumnIds = [Guid.NewGuid(), b.Id, Guid.NewGuid()],
        };

        entity.GetPrimaryKeyColumnsInOrder().Select(c => c.Name).Should().Equal("b", "a");
    }

    /// <summary>主キー列が無ければ空リストになることを検証する</summary>
    [Fact(DisplayName = "GetPrimaryKeyColumnsInOrder: 主キー列が無ければ空になる")]
    public void NoPrimaryKeyColumns_ReturnsEmpty()
    {
        var memo = Plain("memo");
        var entity = new Entity
        {
            TableName = "T",
            Columns = { memo },
            PrimaryKeyColumnIds = [memo.Id],
        };

        entity.GetPrimaryKeyColumnsInOrder().Should().BeEmpty();
    }

    /// <summary>preserveId=false で順序リストが複製後のカラム ID へ張り替わることを検証する</summary>
    [Fact(
        DisplayName = "Clone(preserveId: false): PrimaryKeyColumnIds が複製後のカラム ID へ再マップされる"
    )]
    public void Clone_NewId_RemapsPrimaryKeyColumnIds()
    {
        var a = Pk("a");
        var b = Pk("b");
        var entity = new Entity
        {
            TableName = "T",
            Columns = { a, b },
            PrimaryKeyColumnIds = [b.Id, a.Id],
        };

        var clone = entity.Clone(preserveId: false);

        // 元のカラム ID ではなく複製側のカラム ID を指し、順序（b → a）も維持される
        clone.PrimaryKeyColumnIds.Should().Equal(clone.Columns[1].Id, clone.Columns[0].Id);
        clone.GetPrimaryKeyColumnsInOrder().Select(c => c.Name).Should().Equal("b", "a");
    }

    /// <summary>preserveId=true では順序リストが ID ごとそのまま複製されることを検証する</summary>
    [Fact(
        DisplayName = "Clone(preserveId: true): PrimaryKeyColumnIds は ID ごとそのまま複製される"
    )]
    public void Clone_PreserveId_KeepsPrimaryKeyColumnIds()
    {
        var a = Pk("a");
        var b = Pk("b");
        var entity = new Entity
        {
            TableName = "T",
            Columns = { a, b },
            PrimaryKeyColumnIds = [b.Id, a.Id],
        };

        var clone = entity.Clone(preserveId: true);

        clone.PrimaryKeyColumnIds.Should().Equal(b.Id, a.Id);
        // 複製はリスト実体を共有しない（片方の編集がもう片方へ波及しない）
        clone.PrimaryKeyColumnIds.Should().NotBeSameAs(entity.PrimaryKeyColumnIds);
    }

    /// <summary>エンティティに属さないカラム ID は再マップ対象が無いためそのまま維持されることを検証する</summary>
    [Fact(DisplayName = "Clone(preserveId: false): 対応表に無いカラム ID はそのまま維持される")]
    public void Clone_NewId_KeepsUnknownPrimaryKeyColumnIds()
    {
        var orphan = Guid.NewGuid();
        var entity = new Entity { TableName = "T", PrimaryKeyColumnIds = [orphan] };

        entity.Clone(preserveId: false).PrimaryKeyColumnIds.Should().Equal(orphan);
    }
}
