using System.Linq;
using AwesomeAssertions;
using Xunit;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// 生成 Mapper（CustomerMapper / OrderMapper / CustomerProfileMapper）と <see cref="MapperBase{TEntity,TEditModel}"/>
/// の変換 API を、コミット済みフィクスチャ（<c>GeneratedFixture.g.cs</c>）の実型に対して検証する（DB 不要・CI 常時実行）。
/// </summary>
/// <remarks>
/// 検証対象は CreateEntity()（新規）、CreateEditModel(entity)/CreateEditModel()、ApplyToEntity/ApplyToEditModel、
/// MapperBase.CreateEntity(em, includeRemoved) の両分岐、CreateEntities（削除済み除外／包含）、
/// CreateEditModels（→ <see cref="EditModelCollection{T}"/>）、および Entity→EditModel→Entity の往復一致（VO 込み）。
/// </remarks>
public sealed class MapperRoundTripTests
{
    // ===== テストデータ生成 =====

    /// <summary>Orders×2・CustomerProfile を持つ Unchanged な CustomerEntity を作る（withProfile=false で子プロファイル無し）。</summary>
    private static CustomerEntity BuildFullCustomer(
        decimal? balance = 250.75m,
        bool withProfile = true
    )
    {
        var entity = new CustomerEntity
        {
            CustomerId = CustomerIdValue.Create(1),
            Name = NameValue.Create("Alice"),
            IsActive = IsActiveValue.Create(true),
            Balance = balance is null ? null : BalanceValue.Create(balance.Value),
        };

        entity.Orders.Add(
            new OrderEntity
            {
                OrderId = OrderIdValue.Create(10),
                CustomerId = CustomerIdValue.Create(1),
                Amount = AmountValue.Create(100.00m),
                Memo = MemoValue.Create("first"),
            }
        );
        entity.Orders.Add(
            new OrderEntity
            {
                OrderId = OrderIdValue.Create(11),
                CustomerId = CustomerIdValue.Create(1),
                Amount = AmountValue.Create(200.00m),
                Memo = null,
            }
        );

        if (withProfile)
        {
            entity.CustomerProfile = new CustomerProfileEntity
            {
                ProfileId = ProfileIdValue.Create(5),
                CustomerId = CustomerIdValue.Create(1),
                Bio = BioValue.Create("hello"),
            };
        }

        return entity;
    }

    private static OrderEntity BuildOrder(int id, DateTime? orderedAt = null) =>
        new OrderEntity
        {
            OrderId = OrderIdValue.Create(id),
            CustomerId = CustomerIdValue.Create(1),
            Amount = AmountValue.Create(id),
            Memo = MemoValue.Create($"m{id}"),
            OrderedAt = orderedAt is null ? null : OrderedAtValue.Create(orderedAt.Value),
        };

    /// <summary>秒未満（7 桁 tick）と DateTimeKind を持つ検証用の日時。既定書式の ToString() では両方が落ちる</summary>
    private static DateTime PreciseOrderedAt() =>
        new DateTime(2026, 8, 10, 14, 30, 15, DateTimeKind.Utc).AddTicks(1_234_567);

    // ===== CreateEntity() 新規 =====

    [Fact(DisplayName = "CreateEntity(): 挿入対象（RowState=Added）の新規エンティティを返す")]
    public void CreateEntity_新規はAdded()
    {
        var entity = new CustomerMapper().CreateEntity();

        entity.RowState.Should().Be(RowState.Added);
        entity.IsAdded.Should().BeTrue();
    }

    [Fact(DisplayName = "CreateEditModel(): 新規挿入用の EditModel（Added・確定値なし）を返す")]
    public void CreateEditModel_新規はAdded()
    {
        var em = new CustomerMapper().CreateEditModel();

        em.RowState.Should().Be(RowState.Added);
        em.IsAdded.Should().BeTrue();
        em.CustomerId.Should().BeNull();
        em.BindingCustomerId.Should().BeEmpty();
    }

    // ===== CreateEditModel(entity) / ApplyToEditModel =====

    [Fact(
        DisplayName = "CreateEditModel(entity): エンティティの確定値・RowState を鏡像でロードする"
    )]
    public void CreateEditModel_ロード()
    {
        var entity = BuildFullCustomer();
        entity.RowState = RowState.Unchanged;

        var em = new CustomerMapper().CreateEditModel(entity);

        em.CustomerId!.Value.Should().Be(1);
        em.Name!.Value.Should().Be("Alice");
        em.Balance!.Value.Should().Be(250.75m);
        em.IsActive!.Value.Should().BeTrue();
        em.RowState.Should().Be(RowState.Unchanged);
        em.Orders.Should().HaveCount(2);
        em.CustomerProfile!.Bio!.Value.Should().Be("hello");
    }

    [Fact(DisplayName = "ApplyToEditModel: 既存 EditModel へエンティティ値を上書きロードする")]
    public void ApplyToEditModel_直接呼び出し()
    {
        var entity = BuildFullCustomer();
        var em = new CustomerEditModel();

        new CustomerMapper().ApplyToEditModel(entity, em);

        em.CustomerId!.Value.Should().Be(1);
        em.Name!.Value.Should().Be("Alice");
        em.RowState.Should().Be(entity.RowState);
    }

    // ===== ApplyToEntity =====

    [Fact(DisplayName = "ApplyToEntity: EditModel の確定値を既存エンティティへ破壊的に反映する")]
    public void ApplyToEntity_直接呼び出し()
    {
        var em = new CustomerMapper().CreateEditModel(BuildFullCustomer());
        var target = new CustomerEntity();

        new CustomerMapper().ApplyToEntity(em, target);

        target.CustomerId!.Value.Should().Be(1);
        target.Name!.Value.Should().Be("Alice");
        target.IsActive!.Value.Should().BeTrue();
    }

    // ===== 往復一致 =====

    [Fact(
        DisplayName = "往復: Entity→EditModel→Entity で全カラム値・子・RowState が VO 単位で一致する"
    )]
    public void 往復一致()
    {
        var original = BuildFullCustomer();
        original.RowState = RowState.Unchanged;
        var mapper = new CustomerMapper();

        var em = mapper.CreateEditModel(original);
        var rebuilt = mapper.CreateEntity(em, includeRemoved: true);

        rebuilt.CustomerId.Should().Be(original.CustomerId);
        rebuilt.Name.Should().Be(original.Name);
        rebuilt.Balance.Should().Be(original.Balance!);
        rebuilt.IsActive.Should().Be(original.IsActive);
        rebuilt.RowState.Should().Be(RowState.Unchanged);

        rebuilt.Orders.Should().HaveCount(2);
        rebuilt.Orders.ElementAt(0).OrderId.Should().Be(original.Orders.ElementAt(0).OrderId);
        rebuilt.Orders.ElementAt(0).Amount.Should().Be(original.Orders.ElementAt(0).Amount);
        rebuilt.Orders.ElementAt(1).Memo.Should().BeNull(); // nullable 未設定は null のまま

        rebuilt.CustomerProfile!.ProfileId.Should().Be(original.CustomerProfile!.ProfileId);
        rebuilt.CustomerProfile.Bio.Should().Be(original.CustomerProfile.Bio!);
    }

    [Fact(DisplayName = "往復: nullable カラム（Balance=null）が null のまま往復する")]
    public void 往復_null許容カラム()
    {
        var original = BuildFullCustomer(balance: null);
        var mapper = new CustomerMapper();

        var rebuilt = mapper.CreateEntity(mapper.CreateEditModel(original));

        rebuilt.Balance.Should().BeNull();
    }

    // ===== 1 対 1 の子ナビゲーション（本質的に 0..1）の再利用時クリア =====

    [Fact(
        DisplayName = "ApplyToEditModel: 子プロファイル無しのエンティティを再ロードすると前回の子が残留しない"
    )]
    public void ApplyToEditModel_子無し再ロードで子がnullへ戻る()
    {
        var mapper = new CustomerMapper();
        var em = mapper.CreateEditModel(BuildFullCustomer());
        em.CustomerProfile.Should().NotBeNull();

        // 同じ EditModel を使い回して、子を持たないエンティティをロードし直す
        mapper.ApplyToEditModel(BuildFullCustomer(withProfile: false), em);

        em.CustomerProfile.Should().BeNull();
    }

    [Fact(
        DisplayName = "ApplyToEntity: 子プロファイル無しの EditModel を再適用すると前回の子が残留しない"
    )]
    public void ApplyToEntity_子無し再適用で子がnullへ戻る()
    {
        var mapper = new CustomerMapper();
        var entity = mapper.CreateEntity(mapper.CreateEditModel(BuildFullCustomer()));
        entity.CustomerProfile.Should().NotBeNull();

        // 同じエンティティを使い回して、子を持たない EditModel を適用し直す
        mapper.ApplyToEntity(mapper.CreateEditModel(BuildFullCustomer(withProfile: false)), entity);

        entity.CustomerProfile.Should().BeNull();
    }

    // ===== MapperBase.CreateEntity(em, includeRemoved) の両分岐 =====

    [Fact(
        DisplayName = "CreateEntity(em, includeRemoved): 子コレクションの削除済み要素を分岐で除外／包含する"
    )]
    public void CreateEntity_includeRemoved両分岐()
    {
        var mapper = new CustomerMapper();
        var em = mapper.CreateEditModel(BuildFullCustomer()); // Orders×2
        em.Orders.RemoveAt(1); // 既存要素を削除追跡

        var without = mapper.CreateEntity(em, includeRemoved: false);
        without.Orders.Should().HaveCount(1);

        var with = mapper.CreateEntity(em, includeRemoved: true);
        with.Orders.Should().HaveCount(2);
        with.Orders.Should().Contain(o => o.RowState == RowState.Removed);
    }

    // ===== CreateEntities（コレクション→エンティティ群） =====

    [Fact(DisplayName = "CreateEntities: 削除済みを includeRemoved で除外／包含する")]
    public void CreateEntities_削除済み()
    {
        var orders = new EditModelCollection<OrderEditModel>
        {
            new OrderMapper().CreateEditModel(BuildOrder(1)),
            new OrderMapper().CreateEditModel(BuildOrder(2)),
        };
        orders.RemoveAt(1); // 既存要素を削除追跡

        var om = new OrderMapper();
        om.CreateEntities(orders, includeRemoved: false).Should().HaveCount(1);

        var withRemoved = om.CreateEntities(orders, includeRemoved: true);
        withRemoved.Should().HaveCount(2);
        withRemoved.Should().Contain(o => o.RowState == RowState.Removed);
    }

    // ===== CreateEditModels（→ EditModelCollection） =====

    [Fact(DisplayName = "CreateEditModels: エンティティ列を EditModelCollection へ変換する")]
    public void CreateEditModels()
    {
        var entities = new[] { BuildOrder(1), BuildOrder(2), BuildOrder(3) };

        EditModelCollection<OrderEditModel> col = new OrderMapper().CreateEditModels(entities);

        col.Should().HaveCount(3);
        col[0].OrderId!.Value.Should().Be(1);
        col[2].OrderId!.Value.Should().Be(3);
        // コレクションが所有者として各要素へ設定されている
        col[1].IndexInParent.Should().Be(1);
    }

    // ===== CustomerProfileMapper（VO 版）単体往復 =====

    [Fact(DisplayName = "CustomerProfileMapper: VO 版エンティティの値往復と null Bio の扱い")]
    public void CustomerProfileMapper往復()
    {
        var entity = new CustomerProfileEntity
        {
            ProfileId = ProfileIdValue.Create(7),
            CustomerId = CustomerIdValue.Create(2),
            Bio = BioValue.Create("about me"),
        };
        var mapper = new CustomerProfileMapper();

        var em = mapper.CreateEditModel(entity);
        em.ProfileId!.Value.Should().Be(7);
        em.CustomerId!.Value.Should().Be(2);
        em.Bio!.Value.Should().Be("about me");

        var rebuilt = mapper.CreateEntity(em);
        rebuilt.ProfileId.Should().Be(entity.ProfileId);
        rebuilt.CustomerId.Should().Be(entity.CustomerId);
        rebuilt.Bio.Should().Be(entity.Bio);

        // null Bio
        var nullBio = new CustomerProfileEntity
        {
            ProfileId = ProfileIdValue.Create(8),
            CustomerId = CustomerIdValue.Create(2),
            Bio = null,
        };
        var rebuiltNull = mapper.CreateEntity(mapper.CreateEditModel(nullBio));
        rebuiltNull.Bio.Should().BeNull();
    }

    // ===== ロードの無損失性（確定値の直接代入） =====

    /// <summary>
    /// 本丸: ロードが「バインディング文字列 → TryParse」で確定値を再構築していた頃は、ユーザーが触っていない
    /// DateTime 列でも秒未満（7 桁 tick）と <see cref="DateTimeKind"/> がロードの瞬間に落ち、そのまま保存で書き戻っていた。
    /// </summary>
    [Fact(
        DisplayName = "往復: 無操作の DateTime 列が tick 単位で一致し DateTimeKind も保たれる（ロードは無損失）"
    )]
    public void 往復_DateTime精度とKindが無損失()
    {
        var orderedAt = PreciseOrderedAt();
        var entity = BuildOrder(1, orderedAt);
        var mapper = new OrderMapper();

        // 一切編集せずに EditModel を経由させる
        var rebuilt = mapper.CreateEntity(mapper.CreateEditModel(entity));

        rebuilt.OrderedAt.Should().NotBeNull();
        rebuilt.OrderedAt!.Value.Ticks.Should().Be(orderedAt.Ticks);
        rebuilt.OrderedAt.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact(
        DisplayName = "ロード: DateTime 列の表示文字列は従来どおり ToString() の既定書式（\"O\" 化しない）"
    )]
    public void ロード_DateTimeの表示書式は従来どおり()
    {
        var orderedAt = PreciseOrderedAt();

        var em = new OrderMapper().CreateEditModel(BuildOrder(1, orderedAt));

        em.BindingOrderedAt.Should().Be(orderedAt.ToString());
        em.BindingOrderedAt.Should().NotBe(orderedAt.ToString("O"));
    }

    [Fact(
        DisplayName = "編集: ユーザーが入力した欄だけが入力文字列の精度になる（パース経路は従来どおり）"
    )]
    public void 編集_入力欄はパース経路を通る()
    {
        var em = new OrderMapper().CreateEditModel(BuildOrder(1, PreciseOrderedAt()));

        em.BindingOrderedAt = "2026-01-02 03:04:05";

        em.HasErrors.Should().BeFalse();
        em.OrderedAt!.Value.Should().Be(new DateTime(2026, 1, 2, 3, 4, 5));
        em.OrderedAt.Value.Ticks.Should().Be(new DateTime(2026, 1, 2, 3, 4, 5).Ticks);
    }

    [Fact(DisplayName = "編集: DateTime 欄の不正文字列は従来どおり変換エラーになる")]
    public void 編集_不正入力は変換エラー()
    {
        var em = new OrderMapper().CreateEditModel(BuildOrder(1, PreciseOrderedAt()));

        em.BindingOrderedAt = "not a date";

        em.HasErrors.Should().BeTrue();
        em.GetErrors(nameof(OrderEditModel.BindingOrderedAt)).Cast<string>().Should().NotBeEmpty();
    }

    [Fact(
        DisplayName = "ロード: 変換エラーを抱えた EditModel を再ロードするとエラーが消えて確定値が鏡像になる"
    )]
    public void ロード_再ロードで変換エラーが消える()
    {
        var mapper = new OrderMapper();
        var em = mapper.CreateEditModel(BuildOrder(1, PreciseOrderedAt()));
        em.BindingOrderedAt = "not a date";
        em.HasErrors.Should().BeTrue();

        var orderedAt = PreciseOrderedAt().AddDays(3);
        mapper.ApplyToEditModel(BuildOrder(2, orderedAt), em);

        em.HasErrors.Should().BeFalse();
        em.OrderedAt!.Value.Ticks.Should().Be(orderedAt.Ticks);
        em.BindingOrderedAt.Should().Be(orderedAt.ToString());
    }

    [Fact(DisplayName = "ロード: NULL の DateTime 列は null のまま往復し、表示文字列は空になる")]
    public void ロード_null日時()
    {
        var mapper = new OrderMapper();

        var em = mapper.CreateEditModel(BuildOrder(1));

        em.OrderedAt.Should().BeNull();
        em.BindingOrderedAt.Should().BeEmpty();
        mapper.CreateEntity(em).OrderedAt.Should().BeNull();
    }

    [Fact(
        DisplayName = "ロード: 確定値の直接代入でも RowState は鏡像のまま（Updated へ昇格しない）"
    )]
    public void ロード_RowStateは昇格しない()
    {
        var entity = BuildOrder(1, PreciseOrderedAt());
        entity.RowState = RowState.Unchanged;

        var em = new OrderMapper().CreateEditModel(entity);

        em.RowState.Should().Be(RowState.Unchanged);
        em.IsUpdated.Should().BeFalse();
    }

    // ===== OrderMapper 単体往復 =====

    [Fact(DisplayName = "OrderMapper: 単一エンティティの値往復（VO 込み）")]
    public void OrderMapper往復()
    {
        var entity = BuildOrder(42);
        var mapper = new OrderMapper();

        var rebuilt = mapper.CreateEntity(mapper.CreateEditModel(entity));

        rebuilt.OrderId.Should().Be(entity.OrderId);
        rebuilt.CustomerId.Should().Be(entity.CustomerId);
        rebuilt.Amount.Should().Be(entity.Amount);
        rebuilt.Memo.Should().Be(entity.Memo!);
    }
}
