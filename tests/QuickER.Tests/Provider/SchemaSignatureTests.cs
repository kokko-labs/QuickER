using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.Tests.Provider;

/// <summary>
/// <see cref="SchemaSignature"/> の一意制約部分（UNIQUE の差が署名へ反映されること・並び順に依存しないこと）を検証するテストクラス
/// </summary>
public class SchemaSignatureTests
{
    /// <summary>2 列を持つエンティティを生成する（一意制約はテスト側で足す）</summary>
    private static Entity BuildEntity()
    {
        var code = new Column { Name = "Code", DataType = "nvarchar(20)" };
        var region = new Column { Name = "Region", DataType = "nvarchar(10)" };

        return new Entity { TableName = "Shop", Columns = { code, region } };
    }

    /// <summary>エンティティ単体の署名を計算する</summary>
    private static string Sign(Entity entity) =>
        SchemaSignature.Compute([entity], Array.Empty<Relationship>());

    /// <summary>一意制約の有無で署名が変わることを検証する</summary>
    [Fact(DisplayName = "Compute: 一意制約の有無で署名が変わる")]
    public void Compute_UniqueConstraintPresence_ChangesSignature()
    {
        var without = BuildEntity();
        var with = BuildEntity();
        with.UniqueConstraints.Add(
            new UniqueConstraint { Name = "UQ_Shop_Code", ColumnIds = [with.Columns[0].Id] }
        );

        Sign(with).Should().NotBe(Sign(without));
    }

    /// <summary>制約名だけが異なる場合も署名が変わることを検証する</summary>
    [Fact(DisplayName = "Compute: 制約名が違えば署名が変わる")]
    public void Compute_DifferentConstraintName_ChangesSignature()
    {
        var a = BuildEntity();
        a.UniqueConstraints.Add(
            new UniqueConstraint { Name = "UQ_A", ColumnIds = [a.Columns[0].Id] }
        );

        var b = BuildEntity();
        b.UniqueConstraints.Add(
            new UniqueConstraint { Name = "UQ_B", ColumnIds = [b.Columns[0].Id] }
        );

        Sign(a).Should().NotBe(Sign(b));
    }

    /// <summary>構成列の宣言順が異なれば署名が変わることを検証する（DDL 出力が変わるため差分として扱う）</summary>
    [Fact(DisplayName = "Compute: 構成列の宣言順が違えば署名が変わる")]
    public void Compute_DifferentColumnOrder_ChangesSignature()
    {
        var a = BuildEntity();
        a.UniqueConstraints.Add(
            new UniqueConstraint { ColumnIds = [a.Columns[0].Id, a.Columns[1].Id] }
        );

        var b = BuildEntity();
        b.UniqueConstraints.Add(
            new UniqueConstraint { ColumnIds = [b.Columns[1].Id, b.Columns[0].Id] }
        );

        Sign(a).Should().NotBe(Sign(b));
    }

    /// <summary>同じ一意制約なら（制約 ID・列 ID が別インスタンスでも）署名が一致することを検証する</summary>
    [Fact(DisplayName = "Compute: 同じ一意制約なら署名は不変")]
    public void Compute_SameUniqueConstraints_SameSignature()
    {
        var a = BuildEntity();
        a.UniqueConstraints.Add(
            new UniqueConstraint
            {
                Name = "UQ_Shop_Code_Region",
                ColumnIds = [a.Columns[0].Id, a.Columns[1].Id],
            }
        );

        var b = BuildEntity();
        b.UniqueConstraints.Add(
            new UniqueConstraint
            {
                Name = "UQ_Shop_Code_Region",
                ColumnIds = [b.Columns[0].Id, b.Columns[1].Id],
            }
        );

        Sign(a).Should().Be(Sign(b));
    }

    /// <summary>制約リストの並び順が違っても署名が一致することを検証する（取込側の列挙順に左右されない）</summary>
    [Fact(DisplayName = "Compute: 制約リストの並び順が違っても署名は一致する")]
    public void Compute_ConstraintListOrder_DoesNotAffectSignature()
    {
        var a = BuildEntity();
        a.UniqueConstraints.Add(
            new UniqueConstraint { Name = "UQ_1", ColumnIds = [a.Columns[0].Id] }
        );
        a.UniqueConstraints.Add(
            new UniqueConstraint { Name = "UQ_2", ColumnIds = [a.Columns[1].Id] }
        );

        var b = BuildEntity();
        b.UniqueConstraints.Add(
            new UniqueConstraint { Name = "UQ_2", ColumnIds = [b.Columns[1].Id] }
        );
        b.UniqueConstraints.Add(
            new UniqueConstraint { Name = "UQ_1", ColumnIds = [b.Columns[0].Id] }
        );

        Sign(a).Should().Be(Sign(b));
    }

    // ---------------- 主キーの順序 ----------------

    /// <summary>2 列とも主キーにしたエンティティを生成する（順序指定はテスト側で足す）</summary>
    private static Entity BuildCompositeKeyEntity()
    {
        var entity = BuildEntity();
        entity.Columns[0].IsPrimaryKey = true;
        entity.Columns[1].IsPrimaryKey = true;

        return entity;
    }

    /// <summary>主キーの順序だけが違えば署名が変わることを検証する</summary>
    [Fact(DisplayName = "Compute: 主キーの順序が違えば署名が変わる")]
    public void Compute_DifferentPrimaryKeyOrder_ChangesSignature()
    {
        var a = BuildCompositeKeyEntity();
        a.PrimaryKeyColumnIds = [a.Columns[0].Id, a.Columns[1].Id];

        var b = BuildCompositeKeyEntity();
        b.PrimaryKeyColumnIds = [b.Columns[1].Id, b.Columns[0].Id];

        Sign(a).Should().NotBe(Sign(b));
    }

    /// <summary>
    /// 順序リストが空の図と、同じ並びを明示指定した図の署名が一致することを検証する
    /// （既存図と再取込図で不要な置換確認を出さないための要件）
    /// </summary>
    [Fact(DisplayName = "Compute: 順序リスト空と同順の明示指定は署名が一致する")]
    public void Compute_EmptyListAndMatchingExplicitOrder_SameSignature()
    {
        var withoutOrder = BuildCompositeKeyEntity();

        var withOrder = BuildCompositeKeyEntity();
        // 列宣言順と同じ並びを明示指定する
        withOrder.PrimaryKeyColumnIds = [withOrder.Columns[0].Id, withOrder.Columns[1].Id];

        Sign(withoutOrder).Should().Be(Sign(withOrder));
    }

    /// <summary>計算列の指定だけが違うと署名が変わることを検証する</summary>
    /// <remarks>
    /// 計算列は生成コードの INSERT / UPDATE を変える。署名に入っていないと、取込でこれだけが変わっても
    /// 「構造が同じ」とみなされ、置き換えの確認が省かれていた
    /// </remarks>
    [Fact(DisplayName = "Compute: 計算列の指定だけが違えば署名が変わる")]
    public void Compute_ComputedFlag_ChangesSignature()
    {
        var plain = BuildEntity();
        var computed = BuildEntity();
        computed.Columns[0].IsComputed = true;

        Sign(computed).Should().NotBe(Sign(plain));
    }

    /// <summary>採番の印だけが違うと署名が変わることを検証する</summary>
    /// <remarks>採番の印も計算列と同じく生成コードの INSERT / UPDATE を変えるため</remarks>
    [Fact(DisplayName = "Compute: 採番の印だけが違えば署名が変わる")]
    public void Compute_IdentityFlag_ChangesSignature()
    {
        var plain = BuildEntity();
        var identity = BuildEntity();
        identity.Columns[0].IsIdentity = true;

        Sign(identity).Should().NotBe(Sign(plain));
    }

    /// <summary>外部キー印だけが違うと署名が変わることを検証する</summary>
    /// <remarks>手で付けた外部キー印は、取込の結果に無ければ置き換えで黙って消えるため</remarks>
    [Fact(DisplayName = "Compute: 外部キー印だけが違えば署名が変わる")]
    public void Compute_ForeignKeyMarker_ChangesSignature()
    {
        var plain = BuildEntity();
        var marked = BuildEntity();
        marked.Columns[0].IsForeignKey = true;

        Sign(marked).Should().NotBe(Sign(plain));
    }
}
