using System.Linq;
using AwesomeAssertions;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// 拡張シム層へ利用者が書く partial の受け入れ条件を、実フィクスチャ（<c>GeneratedFixture.g.cs</c>）の上で検証する。
/// </summary>
/// <remarks>
/// このファイル自身が「利用者が書く拡張」の実物で、下の partial 宣言 2 つ（<see cref="EntityBase"/> と
/// <see cref="IValueObject"/>）が拡張面である。生成コードに一切手を入れず、シムの partial だけで
/// 全 Entity・全 VO へ横断的にメンバーとインターフェイスを足せることがこの機能の受け入れ条件そのもの。
/// </remarks>
public sealed class ExtensionShimAcceptanceTests
{
    [Fact(
        DisplayName = "EntityBase の partial へ足したメンバーとインターフェイスが全 Entity へ届く"
    )]
    public void EntityShim_AddedMemberAndInterface_ReachEveryEntity()
    {
        var customer = new CustomerEntity();
        var order = new OrderEntity { RowState = RowState.Added };

        // partial で足したメソッドが、図のどの Entity からも呼べる
        customer.DescribeRow().Should().Be("CustomerEntity/Unchanged");
        order.DescribeRow().Should().Be("OrderEntity/Added");

        // partial で足したインターフェイスも同様に全 Entity へ行き渡る
        // （静的な型ではなく object 経由でキャストする＝面が外れたときにコンパイルエラーではなく
        //   テストの赤として出る＝この検証がシムの実体を見ていることの担保）
        object boxed = customer;
        boxed.Should().BeAssignableTo<IShimAudited>();
        ((IShimAudited)boxed).AuditLabel.Should().Be("Entity:CustomerEntity");

        var entityTypes = FixtureTypes()
            .Where(type => typeof(EntityBase).IsAssignableFrom(type) && type != typeof(EntityBase))
            .ToList();

        entityTypes.Should().NotBeEmpty();
        entityTypes.Should().OnlyContain(type => typeof(IShimAudited).IsAssignableFrom(type));
    }

    [Fact(
        DisplayName = "IValueObject の partial へ注入したインターフェイスが既定実装ごと全 VO へ届く"
    )]
    public void ValueObjectMarkerShim_InjectedInterface_ReachesEveryValueObject()
    {
        // マーカーへ注入したインターフェイスは既定実装で満たされるため、VO 側は 1 行も書かなくてよい
        // （Entity 側と同じ理由で object 経由のキャストにする）
        object name = NameValue.Create("Ada");
        object amount = AmountValue.Create(12.5m);

        name.Should().BeAssignableTo<IShimAudited>();
        ((IShimAudited)name).AuditLabel.Should().Be("ValueObject:Ada");
        ((IShimAudited)amount).AuditLabel.Should().Be("ValueObject:12.5");

        // VO の判定は固定ランタイムの IValueObjectCore で行う（マーカー IValueObject 自体で絞ると、
        // シムからマーカーが外れた VO が母集合から消えて検証が空振りする）
        var valueObjectTypes = FixtureTypes()
            .Where(type => typeof(IValueObjectCore).IsAssignableFrom(type))
            .ToList();

        valueObjectTypes.Should().NotBeEmpty();
        valueObjectTypes.Should().OnlyContain(type => typeof(IShimAudited).IsAssignableFrom(type));
    }

    /// <summary>フィクスチャの名前空間に生成された具象型を列挙する</summary>
    private static IReadOnlyList<Type> FixtureTypes() =>
        typeof(CustomerEntity)
            .Assembly.GetTypes()
            .Where(type =>
                type.Namespace == typeof(CustomerEntity).Namespace
                && type is { IsClass: true, IsAbstract: false }
            )
            .ToList();
}

/// <summary>拡張シムへ注入する利用者側のインターフェイス（受け入れ検証用）</summary>
public interface IShimAudited
{
    /// <summary>監査ログ向けの表示ラベル</summary>
    string AuditLabel { get; }
}

/// <summary>
/// Entity 拡張シムの利用者 partial。全 Entity へメンバーとインターフェイスを 1 箇所で足す。
/// </summary>
public partial class EntityBase : IShimAudited
{
    /// <summary>行の型名と変更状態を 1 行で表す（全 Entity 共通で使える）</summary>
    public string DescribeRow() => $"{GetType().Name}/{RowState}";

    /// <summary>注入インターフェイスの実装（明示的実装＝Entity の公開面と JSON 出力を変えない）</summary>
    string IShimAudited.AuditLabel => $"Entity:{GetType().Name}";
}

/// <summary>
/// 値オブジェクトのマーカー拡張シムの利用者 partial。既定実装込みでインターフェイスを注入すると、
/// 全 VO が個別の実装なしにその面を満たす。
/// </summary>
public partial interface IValueObject : IShimAudited
{
    /// <summary>注入インターフェイスの既定実装（マーカーが持つ DisplayValue から組み立てる）</summary>
    string IShimAudited.AuditLabel => $"ValueObject:{DisplayValue}";
}
