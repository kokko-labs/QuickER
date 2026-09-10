using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// 拡張シム層へ利用者が書く partial の受け入れ条件を、実フィクスチャ（<c>GeneratedFixture.g.cs</c>）の上で検証する。
/// </summary>
/// <remarks>
/// このファイル自身が「利用者が書く拡張」の実物で、下の partial 宣言群（<see cref="EntityBase"/> /
/// <see cref="IValueObject"/> / <see cref="ValueObjectBase{TSelf, TValue}"/> / <see cref="SqlServerRepository{TEntity, TKey}"/>）が
/// 拡張面である。生成コードに一切手を入れず、シムの partial だけで全 Entity・全 VO へ横断的に
/// メンバーとインターフェイスを足せることがこの機能の受け入れ条件そのもの。
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

    [Fact(
        DisplayName = "ValueObjectBase の partial へ足したメンバーが値の形に依らず全 VO で呼べる"
    )]
    public void ValueObjectRootShim_AddedMember_ReachesEveryValueShape()
    {
        // 共通ルートは全 VO が通る唯一の拡張点なので、系統別基底の別なく同じメンバーが生える
        NameValue.Create("Ada").DescribeValue().Should().Be("NameValue:Ada"); // string
        IsActiveValue.Create(true).DescribeValue().Should().Be("IsActiveValue:True"); // bool
        AmountValue.Create(12.5m).DescribeValue().Should().Be("AmountValue:12.5"); // decimal（Ordered）
        CustomerIdValue.Create(7).DescribeValue().Should().Be("CustomerIdValue:7"); // int（Ordered）
        DeliveryDateValue
            .Create(new DateTime(2026, 9, 10))
            .DescribeValue()
            .Should()
            .StartWith("DeliveryDateValue:"); // DateTime

        // この図に具象 VO を持たない系統（binary / GuidKey）も含め、系統別基底 6 本すべてが共通ルートへ着地する
        // （着地しない系統はルートへ足した拡張が届かない枝になるため、型の形で固定する）
        foreach (var familyBaseName in ValueObjectFamilyBaseNames)
        {
            var familyBase = typeof(NameValue).Assembly.GetType(
                $"{typeof(NameValue).Namespace}.{familyBaseName}"
            );

            familyBase.Should().NotBeNull($"系統別基底 {familyBaseName} が生成されている");
            DerivesFromValueObjectRoot(familyBase!)
                .Should()
                .BeTrue(
                    $"系統別基底 {familyBaseName} は共通ルート ValueObjectBase<,> から派生する"
                );
        }
    }

    /// <summary>系統別基底 6 本のリフレクション名（型引数の個数付き）</summary>
    private static readonly string[] ValueObjectFamilyBaseNames =
    [
        "ValueObjectOrderedBase`2",
        "ValueObjectStringBase`1",
        "ValueObjectBooleanBase`1",
        "ValueObjectDateTimeBase`1",
        "ValueObjectBinaryBase`1",
        "ValueObjectGuidKeyBase`1",
    ];

    /// <summary>基底の連鎖をたどって共通ルート <c>ValueObjectBase&lt;,&gt;</c> に着くかを判定する</summary>
    private static bool DerivesFromValueObjectRoot(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (
                current.IsGenericType
                && current.GetGenericTypeDefinition() == typeof(ValueObjectBase<,>)
            )
            {
                return true;
            }
        }

        return false;
    }

    [Fact(
        DisplayName = "拡張面へ足した setter プロパティは列にならない（SQL・値比較・列集合から外れる）"
    )]
    public void EntityShim_AddedProperty_IsNotAColumn()
    {
        var metadata = EntitySaveMetadata.For(typeof(CustomerEntity));

        // 列集合は [Column] を持つプロパティだけ＝拡張面のプロパティは 1 つも入らない
        metadata
            .AllProperties.Select(property => property.Name)
            .Should()
            .NotContain(nameof(EntityBase.ShimNote));

        // 実在しない列名が SQL に混ざらない（混ざると実 DB でだけ "Invalid column name" になる）
        metadata.InsertSql.Should().NotContain(nameof(EntityBase.ShimNote));
        metadata.SelectAllSql.Should().NotContain(nameof(EntityBase.ShimNote));

        // 値比較の対象からも外れる＝拡張面のプロパティ差は「行の値が違う」ことを意味しない
        var left = new CustomerEntity
        {
            CustomerId = CustomerIdValue.Create(1),
            Name = NameValue.Create("Ada"),
            ShimNote = "left",
        };
        var right = new CustomerEntity
        {
            CustomerId = CustomerIdValue.Create(1),
            Name = NameValue.Create("Ada"),
            ShimNote = "right",
        };

        left.HasSameValues(right).Should().BeTrue();
        left.GetValueHashCode().Should().Be(right.GetValueHashCode());
    }

    [Fact(
        DisplayName = "[Column] を付けた利用者プロパティは列として扱われる（オプトインが成立する）"
    )]
    public void HandwrittenProperty_WithColumnAttribute_IsAColumn()
    {
        var metadata = EntitySaveMetadata.For(typeof(ShimOptInEntity));
        var columnNames = metadata.AllProperties.Select(property => property.Name).ToList();

        // [Column] 付き＝列。付いていない利用者プロパティと拡張面のプロパティは列ではない
        columnNames.Should().Contain(nameof(ShimOptInEntity.RowId));
        columnNames.Should().Contain(nameof(ShimOptInEntity.OptedIn));
        columnNames.Should().NotContain(nameof(ShimOptInEntity.NotAColumn));
        columnNames.Should().NotContain(nameof(EntityBase.ShimNote));

        metadata.InsertSql.Should().Contain("opted_in");
        metadata.InsertSql.Should().NotContain(nameof(ShimOptInEntity.NotAColumn));

        // 値比較も [Column] 基準＝オプトインした列だけが「行の値」に入る
        var left = new ShimOptInEntity
        {
            RowId = 1,
            OptedIn = "same",
            NotAColumn = "left",
        };
        var right = new ShimOptInEntity
        {
            RowId = 1,
            OptedIn = "same",
            NotAColumn = "right",
        };

        left.HasSameValues(right).Should().BeTrue();

        right.OptedIn = "different";
        left.HasSameValues(right).Should().BeFalse();
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

/// <summary>
/// 利用者が <c>[Column]</c> でオプトインした列を持つ手書きエンティティ（受け入れ検証用）。
/// </summary>
/// <remarks>
/// ランタイムの列判定はリフレクションなので、生成 part と利用者 part の別は見えない
/// ＝手書きエンティティは「生成エンティティの partial へ利用者がプロパティを足した形」の忠実な代役になる。
/// 図のテーブルを 1 つも汚さずにオプトインの両向き（付けた／付けない）を並べられるため、こちらで固定する。
/// </remarks>
[Table("shim_opt_in_rows")]
public sealed class ShimOptInEntity : EntityBase
{
    /// <summary>主キー</summary>
    [Key]
    [Column("row_id")]
    public int RowId { get; set; }

    /// <summary><c>[Column]</c> を付けた利用者プロパティ＝列として扱われる</summary>
    [Column("opted_in")]
    public string? OptedIn { get; set; }

    /// <summary><c>[Column]</c> の無い利用者プロパティ＝列ではない</summary>
    public string? NotAColumn { get; set; }
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
    /// <summary>
    /// 拡張面へ足した読み書き可能なプロパティ（実在しない列）。<c>[Column]</c> を持たないため列ではなく、
    /// SQL・値比較・列コピーのいずれからも外れる。
    /// </summary>
    public string? ShimNote { get; set; }

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

/// <summary>
/// 値オブジェクトの共通ルートの利用者 partial。系統別基底はすべてこの型から派生するため、ここへ足した
/// メンバーは値の形（string / bool / 日時 / 数値 / バイナリ / GUID キー）に依らず全 VO で使える。
/// </summary>
/// <remarks>型引数リストは繰り返すが、制約は生成側の part が宣言済みなので省略する。</remarks>
public partial class ValueObjectBase<TSelf, TValue>
{
    /// <summary>型名と表示値を 1 行で表す（全 VO 共通で使える）</summary>
    public string DescribeValue() => $"{GetType().Name}:{DisplayValue}";
}

/// <summary>
/// 実装側（方言）拡張シムの利用者 partial。docs「生成される基底クラスの拡張」節のレシピと同形＝
/// manual 実装のクエリが共通で使う protected ヘルパをバックエンド単位で 1 箇所に足せることの
/// コンパイル実証（呼び出しは実 DB が要るため ExtensionShimGenerationTests 側の存在検証と合わせて固定）。
/// </summary>
/// <remarks>型引数リストは繰り返すが、制約は生成側の part が宣言済みなので省略する。</remarks>
public abstract partial class SqlServerRepository<TEntity, TKey>
{
    /// <summary>手動実装のクエリが共通で使う定型（各 {Entity}Repository の partial から呼ぶ）。</summary>
    protected Task<int?> CountBySqlAsync(string sql, object? parameters = null) =>
        ExecuteScalarSqlAsync<int?>(sql, parameters);
}
