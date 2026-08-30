using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AwesomeAssertions;
using Xunit;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// 定義済みインスタンスだけを受け付ける列挙型の値オブジェクトを、手書きの型として書けることの検証用 VO。
/// </summary>
/// <remarks>
/// <c>New</c> をルックアップにするだけでも成立するが、生成 VO では <c>New</c> が既に出力済みで差し替えられない。
/// 両者を同じ書き方に揃えるため、ここでも <c>TryGetDefined</c> を使う。手書き型は partial フックを持たないため、
/// <c>TryGetDefined</c> / <c>TryConvertCustomInput</c> を interface メンバとして直接実装する（docs のレシピと同形）。
/// </remarks>
public sealed class DataAccessMode
    : ValueObjectOrderedBase<DataAccessMode, int>,
        IValueObject<DataAccessMode, int>
{
    public static readonly DataAccessMode Web = new(1, "Web");
    public static readonly DataAccessMode Database = new(2, "Database");
    public static readonly DataAccessMode Fake = new(3, "Fake");

    // 一度だけ構築する。ルックアップは生成のたびに走るため割り当てなしに保つ（docs の推奨形）
    private static readonly Dictionary<int, DataAccessMode> Defined = new[]
    {
        Web,
        Database,
        Fake,
    }.ToDictionary(x => x.Value);

    private DataAccessMode(int value, string modeName)
        : base(value) => ModeName = modeName;

    public string ModeName { get; }

    public static IEnumerable<DataAccessMode> GetList() => Defined.Values;

    static DataAccessMode IValueObject<DataAccessMode, int>.New(int value) => Defined[value];

    static bool IValueObject<DataAccessMode, int>.TryGetDefined(
        int value,
        out DataAccessMode? defined
    ) => Defined.TryGetValue(value, out defined);

    // TryGetDefined だけでは未定義値を拒めない（New へ落ちるだけ）。対で検証を書くのが契約。
    static void IValueObject<DataAccessMode, int>.ValidateCore(int value, ref List<string>? errors)
    {
        if (!Defined.ContainsKey(value))
        {
            (errors ??= new List<string>()).Add($"DataAccessMode {value} is not defined.");
        }
    }

    // 名前からの生成は変換フックで受ける。扱わない形は false のまま返せば通常の変換に落ちる。
    // （フックの中から TryCreateFrom / CreateFrom を呼んではいけない＝両者はこのフックを照会するため再帰する）
    static bool IValueObject<DataAccessMode>.TryConvertCustomInput(
        object raw,
        IFormatProvider? provider,
        out DataAccessMode? result
    )
    {
        result = raw is string name ? GetList().FirstOrDefault(x => x.ModeName == name) : null;

        return result is not null;
    }

    public override string DisplayValue => ModeName;
}

/// <summary>
/// 生成 VO と同じ形（private コンストラクタ＋明示的実装の <c>New</c>／<c>ValidateCore</c>＋
/// <c>TryGetDefined</c>／<c>TryConvertCustomInput</c> のブリッジ＋partial フック宣言）で出力された型。
/// </summary>
/// <remarks>
/// ここが「生成側の partial」に相当し、テンプレートの per-VO 出力のミラー。<c>New</c> もブリッジも
/// 実装済みで利用者は差し替えられない——それでも partial フックだけで列挙型に拡張できることが、
/// 下の <c>ScreenMode</c> の partial で検証する受け入れ条件。
/// </remarks>
public sealed partial class ScreenMode
    : ValueObjectOrderedBase<ScreenMode, int>,
        IValueObject<ScreenMode, int>
{
    private ScreenMode(int value)
        : base(value) { }

    static ScreenMode IValueObject<ScreenMode, int>.New(int value) => new(value);

    static void IValueObject<ScreenMode, int>.ValidateCore(int value, ref List<string>? errors)
    {
        OnValidate(value, ref errors);
    }

    static partial void OnValidate(int value, ref List<string>? errors);

    static bool IValueObject<ScreenMode, int>.TryGetDefined(int value, out ScreenMode? defined)
    {
        ScreenMode? found = null;
        GetDefinedInstance(value, ref found);
        defined = found;
        return found is not null;
    }

    static partial void GetDefinedInstance(int value, ref ScreenMode? defined);

    static bool IValueObject<ScreenMode>.TryConvertCustomInput(
        object raw,
        IFormatProvider? provider,
        out ScreenMode? result
    )
    {
        ScreenMode? custom = null;
        ConvertCustomInput(raw, provider, ref custom);
        result = custom;
        return custom is not null;
    }

    static partial void ConvertCustomInput(
        object raw,
        IFormatProvider? provider,
        ref ScreenMode? result
    );
}

/// <summary>生成 VO を利用者が partial フックで列挙型へ拡張した側（生成器のオプションは使わない）。</summary>
public sealed partial class ScreenMode
{
    public static readonly ScreenMode List = new(1) { ModeName = "List" };
    public static readonly ScreenMode Edit = new(2) { ModeName = "Edit" };

    private static readonly Dictionary<int, ScreenMode> Defined = new[] { List, Edit }.ToDictionary(
        x => x.Value
    );

    public string ModeName { get; private init; } = string.Empty;

    public static IEnumerable<ScreenMode> GetList() => Defined.Values;

    static partial void GetDefinedInstance(int value, ref ScreenMode? defined) =>
        defined = Defined.GetValueOrDefault(value);

    static partial void OnValidate(int value, ref List<string>? errors)
    {
        if (!Defined.ContainsKey(value))
        {
            (errors ??= new List<string>()).Add($"ScreenMode {value} is not defined.");
        }
    }

    static partial void ConvertCustomInput(
        object raw,
        IFormatProvider? provider,
        ref ScreenMode? result
    )
    {
        if (raw is string name)
        {
            result = GetList().FirstOrDefault(x => x.ModeName == name);
        }
    }

    public override string DisplayValue => ModeName;
}

/// <summary>実生成 VO（<see cref="BioValue"/>）へ partial フックを実装した側＝生成された実ブリッジの配線検証用。</summary>
/// <remarks>
/// 値 <c>"#defined"</c> だけを定義済みインスタンスへ引き当て、生値 <c>"@defined"</c> だけを変換フックで受ける。
/// 他の値は素通しなので、BioValue を使う他のテストへは影響しない。
/// </remarks>
public sealed partial class BioValue
{
    /// <summary>フックが返す定義済みインスタンス（Create 経由だと素のインスタンスと区別が付かないため、素の生成経路で作って保持する）。</summary>
    public static readonly BioValue DefinedBio = Create("#defined");

    static partial void GetDefinedInstance(string value, ref BioValue? defined)
    {
        if (value == "#defined")
        {
            defined = DefinedBio;
        }
    }

    static partial void ConvertCustomInput(
        object raw,
        IFormatProvider? provider,
        ref BioValue? result
    )
    {
        if (raw is "@defined")
        {
            result = DefinedBio;
        }
    }

    // 割り当てゼロの網用: 実装があるだけで生成 ValidateCore のフック呼び出し経路が生きる（"#reject" 以外は素通し）
    static partial void OnValidate(string value, ref List<string>? errors)
    {
        if (value == "#reject")
        {
            (errors ??= new List<string>()).Add("rejected for the allocation test");
        }
    }
}

/// <summary>定義済みインスタンスのみを受け付ける値オブジェクト（<c>TryGetDefined</c>）の検証。</summary>
public sealed class ValueObjectDefinedInstanceTests
{
    private static T? ReadCell<T>(object? cell, List<string> errors)
        where T : class, IValueObject<T>
    {
        if (T.TryCreateFrom(cell, null, out var value, out var messages))
        {
            return value;
        }

        errors.AddRange(messages);

        return null;
    }

    [Fact(DisplayName = "[定義済み] 手書きの列挙型 VO は Create で定義済みインスタンスを返す")]
    public void 手書き列挙VOは定義済みを返す()
    {
        DataAccessMode.Create(2).Should().BeSameAs(DataAccessMode.Database);
        DataAccessMode.TryCreate(3, out var fake, out _).Should().BeTrue();
        fake.Should().BeSameAs(DataAccessMode.Fake);
        DataAccessMode.Create(1).ModeName.Should().Be("Web");
    }

    [Fact(DisplayName = "[定義済み] 未定義の値は検証エラーになる")]
    public void 未定義値は検証エラー()
    {
        DataAccessMode.TryCreate(9, out var value, out var errors).Should().BeFalse();

        value.Should().BeNull();
        errors.Should().ContainSingle().Which.Should().Be("DataAccessMode 9 is not defined.");

        var act = () => DataAccessMode.Create(9);

        act.Should().Throw<ValueObjectValidationException>();
    }

    // 生成 VO は New もブリッジも出力済みなので差し替えられない。GetDefinedInstance は Create / TryCreate 側に
    // 割り込むブリッジから照会されるため、利用者の partial だけで列挙型へ拡張できる。
    [Fact(
        DisplayName = "[定義済み] 生成形の VO も partial の GetDefinedInstance だけで列挙型にできる"
    )]
    public void 生成VOをpartialで列挙型にできる()
    {
        ScreenMode.Create(1).Should().BeSameAs(ScreenMode.List);
        ScreenMode.TryCreate(2, out var edit, out _).Should().BeTrue();
        edit.Should().BeSameAs(ScreenMode.Edit);

        // New が作る素のインスタンスでは ModeName が空になる。定義済みが返っていることの裏取り。
        ScreenMode.Create(2).ModeName.Should().Be("Edit");
        ScreenMode.TryCreate(9, out _, out var errors).Should().BeFalse();
        errors.Should().ContainSingle().Which.Should().Be("ScreenMode 9 is not defined.");
    }

    [Fact(DisplayName = "[定義済み] 名前からの生成をジェネリック経路へ乗せられる")]
    public void 名前からの生成をジェネリック経路へ乗せられる()
    {
        var errors = new List<string>();

        ReadCell<DataAccessMode>("Fake", errors).Should().BeSameAs(DataAccessMode.Fake);
        ReadCell<DataAccessMode>("2", errors).Should().BeSameAs(DataAccessMode.Database);
        ReadCell<ScreenMode>("Edit", errors).Should().BeSameAs(ScreenMode.Edit);
        ReadCell<ScreenMode>(1, errors).Should().BeSameAs(ScreenMode.List);

        errors.Should().BeEmpty();

        ReadCell<DataAccessMode>("Nope", errors).Should().BeNull();
        errors.Should().ContainSingle();
    }

    // 変換フック導入の眼目＝旧設計では「4 引数の TryCreateFrom を具象型名で呼ぶ」形だけが override を
    // 静的束縛で迂回した。基底がどの呼び形でも TSelf.TryConvertCustomInput を照会することを 4 形すべてで固定する。
    [Fact(DisplayName = "[定義済み] 変換フックはどの呼び形でも効く（4 引数の型名呼びを含む）")]
    public void 変換フックは全呼び形で効く()
    {
        // 4 引数・具象型名（旧設計で唯一 override を迂回した形）
        ScreenMode
            .TryCreateFrom("Edit", CultureInfo.InvariantCulture, out var viaFourArgs, out _)
            .Should()
            .BeTrue();
        viaFourArgs.Should().BeSameAs(ScreenMode.Edit);

        // 3 引数・具象型名
        ScreenMode.TryCreateFrom("Edit", out var viaThreeArgs, out _).Should().BeTrue();
        viaThreeArgs.Should().BeSameAs(ScreenMode.Edit);

        // CreateFrom（例外版）
        ScreenMode
            .CreateFrom("Edit", CultureInfo.InvariantCulture)
            .Should()
            .BeSameAs(ScreenMode.Edit);

        // 型引数経由（ジェネリック取り込み経路）
        var errors = new List<string>();
        ReadCell<ScreenMode>("Edit", errors).Should().BeSameAs(ScreenMode.Edit);
        errors.Should().BeEmpty();

        // 手書き型（interface フック直接実装）も同じ 4 引数型名呼びで効く
        DataAccessMode
            .TryCreateFrom("Database", CultureInfo.InvariantCulture, out var handwritten, out _)
            .Should()
            .BeTrue();
        handwritten.Should().BeSameAs(DataAccessMode.Database);
    }

    // 実際に生成されたブリッジ（GeneratedFixture.g.cs の BioValue）が partial フックへ配線されていることの検証。
    // ScreenMode はテスト内のミラーなので、テンプレート出力そのものはこちらで固定する。
    [Fact(DisplayName = "[定義済み] 実生成 VO のブリッジが partial フックへ配線されている")]
    public void 実生成VOのブリッジが配線されている()
    {
        // GetDefinedInstance: Create と DB 読み出し（Wrap）の両経路で定義済みインスタンスが返る
        BioValue.Create("#defined").Should().BeSameAs(BioValue.DefinedBio);
        SqlValueObjectActivator
            .Wrap("#defined", typeof(BioValue))
            .Should()
            .BeSameAs(BioValue.DefinedBio);

        // ConvertCustomInput: 4 引数の型名呼び（旧設計の迂回形）でもフックが先に効く
        BioValue
            .TryCreateFrom("@defined", CultureInfo.InvariantCulture, out var custom, out _)
            .Should()
            .BeTrue();
        custom.Should().BeSameAs(BioValue.DefinedBio);

        // フックが扱わない値は通常の変換・生成のまま（他テストへの不干渉の裏取り）
        BioValue.Create("plain").Should().NotBeSameAs(BioValue.DefinedBio);
        BioValue.CreateFrom("plain")!.Value.Should().Be("plain");
    }

    // OnValidate の ref 化（遅延確保）の網: 実装した型でも検証を通る生成はリストを確保しない。
    // 対象は実生成 VO（BioValue＝生成された ValidateCore がフックを呼ぶ）＝テンプレートが旧形
    // （呼び出し前に errors ??= new List<string>() で必ず確保）へ戻ると 1 回あたり +32 bytes でここが赤くなる。
    [Fact(DisplayName = "[定義済み] OnValidate を書いた型でも、検証を通る生成は割り当てゼロ")]
    public void OnValidate実装型の成功パスは割り当てゼロ()
    {
        // ウォームアップ（JIT・tier-up 由来の割り当てを測定から追い出す）
        for (var i = 0; i < 50_000; i++)
        {
            BioValue.Create("#defined");
        }

        long delta = 0;

        // 測定中に tier-up が走った回を拾わないよう 3 ラウンド測り、最後のラウンドで表明する
        for (var round = 0; round < 3; round++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();

            for (var i = 0; i < 10_000; i++)
            {
                BioValue.Create("#defined");
            }

            delta = GC.GetAllocatedBytesForCurrentThread() - before;
        }

        delta.Should().Be(0, "定義済みインスタンス＋遅延確保の成功パスは何も確保しない");
    }

    // TryGetDefined / フックを書かない型の挙動が変わっていないこと（既定は「定義済みなし・通常変換のみ」）。
    [Fact(DisplayName = "[定義済み] フックを書かない VO は従来どおり毎回新しいインスタンスを作る")]
    public void フックなしVOは従来どおり()
    {
        CustomerIdValue.Create(5).Should().NotBeSameAs(CustomerIdValue.Create(5));
        CustomerIdValue.Create(5).Should().Be(CustomerIdValue.Create(5));
    }
}
