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
/// Create / TryCreate / Validate・型引数経由の Create の決まった形の表明は <see cref="ValueObjectScenarioTests"/>
/// の表にあり、ここに残すのは JSON の往復・Validate の戻り値（同じコレクションへ 2 回呼ぶ順序依存）・
/// Wrap と materializer の高速経路（生成テンプレートの XmlDoc が名指しする）だけ。
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

    // 集約先を共有する使い方では、コレクションの件数からは「今回の値が失格したか」を読めない。
    // 戻り値がその 1 回の判定だけを表すことを固定する（既存要素があっても true を返す）。
    [Fact(DisplayName = "[手書きVO] Validate の戻り値はその呼び出しの判定だけを表す")]
    public void 手書きVOのValidateの戻り値は呼び出し単位である()
    {
        var errors = new List<string>();

        ContactMailValue.Validate("ng", errors).Should().BeFalse();
        ContactMailValue.Validate("ok@example.com", errors).Should().BeTrue();

        errors.Should().ContainSingle();
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
}
