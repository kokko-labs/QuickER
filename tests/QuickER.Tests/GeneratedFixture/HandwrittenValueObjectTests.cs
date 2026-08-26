using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using AwesomeAssertions;
using Xunit;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// 手書き値オブジェクト（private コンストラクタ＋New＋ValidateCore の 3 メンバ）のレシピを、
/// 生成基盤（<c>GeneratedFixture.g.cs</c>）の実型に対して検証するテスト用 VO。
/// </summary>
/// <remarks>生成物とまったく同じ形（基底継承＋明示的実装 2 つ）で書けることが受け入れ条件そのもの。</remarks>
public sealed class ContactMailValue
    : ValueObjectStringBase<ContactMailValue>,
        IValueObject<ContactMailValue, string>
{
    private ContactMailValue(string value)
        : base(value) { }

    static ContactMailValue IValueObject<ContactMailValue, string>.New(string value) => new(value);

    static void IValueObject<ContactMailValue, string>.ValidateCore(
        string value,
        ref List<string>? errors
    )
    {
        if (!value.Contains('@'))
        {
            (errors ??= new List<string>()).Add("Mail address must contain '@'.");
        }
    }
}

/// <summary>検証規則を持たない手書き VO（ValidateCore 未実装＝インターフェイスの既定実装が効く最小形＝2 メンバ）。</summary>
public sealed class PlainTagValue
    : ValueObjectStringBase<PlainTagValue>,
        IValueObject<PlainTagValue, string>
{
    private PlainTagValue(string value)
        : base(value) { }

    static PlainTagValue IValueObject<PlainTagValue, string>.New(string value) => new(value);
}

/// <summary>
/// 値オブジェクトの Create / TryCreate / Validate 基底集約（第 8 次 A-2）と、名前で引く Create の
/// 基底解決（A-1: FlattenHierarchy）を、手書き VO・生成 VO の両方で検証する（DB 不要・CI 常時実行)。
/// </summary>
/// <remarks>
/// <para>
/// A-2 の要は「基底クラスから継承した静的メソッドが static abstract インターフェイスメンバを満たす」こと。
/// 手書き VO（上の 2 型）が型名経由・型引数経由の両方で Create でき、生成 VO と同じ経路
/// （JSON 変換・<see cref="SqlValueObjectActivator"/>・行 materializer の高速経路）に乗ることを固定する。
/// </para>
/// <para>
/// A-1 の網: <see cref="SqlValueObjectActivator.Wrap"/> と <c>EntitySaveMetadata.ResolveValueObjectReader</c> は
/// Create をリフレクションで名前解決する。Create は基底宣言になったため、<c>FlattenHierarchy</c> を外すと
/// 解決が静かに失敗して Wrap は生値を素通しし（→ ここの表明が赤）、materializer はフォールバックへ落ちる
/// （→ 非 null 表明が赤）。
/// </para>
/// </remarks>
public sealed class HandwrittenValueObjectTests
{
    // ===== A-2: 手書き VO の 3 メンバレシピ =====

    [Fact(
        DisplayName = "[手書きVO] Create は検証を通し、違反は ValueObjectValidationException になる"
    )]
    public void 手書きVOのCreateが基底実装で動く()
    {
        ContactMailValue.Create("a@example.com").Value.Should().Be("a@example.com");

        var act = () => ContactMailValue.Create("no-at-mark");

        var ex = act.Should().Throw<ValueObjectValidationException>().Which;
        ex.ValueObjectType.Should().Be(typeof(ContactMailValue));
        ex.Errors.Should().ContainSingle().Which.Should().Be("Mail address must contain '@'.");
    }

    [Fact(DisplayName = "[手書きVO] TryCreate は成功と失敗の両側で基底実装が動く")]
    public void 手書きVOのTryCreateが両側で動く()
    {
        ContactMailValue.TryCreate("a@example.com", out var ok, out var noErrors).Should().BeTrue();
        ok!.Value.Should().Be("a@example.com");
        noErrors.Should().BeEmpty();

        ContactMailValue.TryCreate("bad", out var ng, out var errors).Should().BeFalse();
        ng.Should().BeNull();
        errors.Should().ContainSingle();
    }

    [Fact(DisplayName = "[手書きVO] Validate は VO を作らずに違反だけを既存コレクションへ足す")]
    public void 手書きVOのValidateが基底実装で動く()
    {
        var errors = new List<string> { "existing" };

        ContactMailValue.Validate("bad", errors);

        errors.Should().Equal("existing", "Mail address must contain '@'.");
    }

    [Fact(
        DisplayName = "[手書きVO] 検証規則なしの VO は ValidateCore の既定実装（検証なし）で成立する"
    )]
    public void 検証なし手書きVOは2メンバで成立する()
    {
        PlainTagValue.Create("anything").Value.Should().Be("anything");

        PlainTagValue.TryCreate("x", out var created, out var errors).Should().BeTrue();
        created!.Value.Should().Be("x");
        errors.Should().BeEmpty();
    }

    [Fact(
        DisplayName = "[手書きVO] 型引数経由の TVo.Create（static abstract ディスパッチ）が継承実装へ届く"
    )]
    public void 型引数経由のCreateが継承実装へ届く()
    {
        CreateViaTypeParameter<ContactMailValue, string>("t@example.com")
            .Value.Should()
            .Be("t@example.com");

        // 生成 VO も同じ経路（従来からの利用形が不変であることの対照）
        CreateViaTypeParameter<NameValue, string>("Alice").Value.Should().Be("Alice");
    }

    [Fact(
        DisplayName = "[手書きVO] JSON 変換（ValueObjectJsonConverterFactory）で生成 VO と同じ扱いを受ける"
    )]
    public void 手書きVOがJSON往復できる()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new ValueObjectJsonConverterFactory());

        var json = JsonSerializer.Serialize(ContactMailValue.Create("j@example.com"), options);

        json.Should().Be("\"j@example.com\"", "JSON には内包値だけが乗る");
        JsonSerializer
            .Deserialize<ContactMailValue>(json, options)!
            .Value.Should()
            .Be("j@example.com");
    }

    // ===== A-1: 名前で引く Create が基底宣言を解決する（FlattenHierarchy の網） =====

    [Theory(
        DisplayName = "[A-1] SqlValueObjectActivator.Wrap が基底宣言の Create を解決して VO を組み立てる"
    )]
    [InlineData(typeof(NameValue), "Alice")]
    [InlineData(typeof(ContactMailValue), "w@example.com")]
    public void Wrapが基底宣言のCreateを解決する(Type valueObjectType, string raw)
    {
        var wrapped = SqlValueObjectActivator.Wrap(raw, valueObjectType);

        // FlattenHierarchy が無いと解決が null になり、Wrap は生値を素通しする（この表明が赤くなる）
        wrapped.Should().BeOfType(valueObjectType);
        ((IValueObject)wrapped!).UnderlyingValue.Should().Be(raw);
    }

    [Theory(
        DisplayName = "[A-1] 行 materializer の高速経路（型特化 accessor＋Create）に VO が乗る"
    )]
    [InlineData(typeof(NameValue))]
    [InlineData(typeof(ContactMailValue))]
    public void Materializerの高速経路がVOを解決する(Type valueObjectType)
    {
        // private static の解決関数を名指しで呼ぶ（null＝フォールバック行き＝高速経路から外れている）
        var resolver = typeof(EntitySaveMetadata).GetMethod(
            "ResolveValueObjectReader",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        resolver.Should().NotBeNull("解決関数が改名されたらこのテストの名指しも追従する");
        resolver!
            .Invoke(null, new object[] { valueObjectType })
            .Should()
            .NotBeNull("VO 列は SetColumnValue フォールバックでなく型特化の高速経路に乗る");
    }

    /// <summary>型引数経由で Create を呼ぶ（static abstract ディスパッチの再現）。</summary>
    private static TVo CreateViaTypeParameter<TVo, TValue>(TValue value)
        where TVo : IValueObject<TVo, TValue> => TVo.Create(value);
}
