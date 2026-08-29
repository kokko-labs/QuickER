using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Xunit;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// 定義済みインスタンスだけを受け付ける列挙型の値オブジェクトを、手書きの型として書けることの検証用 VO。
/// </summary>
/// <remarks>
/// <c>New</c> をルックアップにするだけでも成立するが、生成 VO では <c>New</c> が既に出力済みで差し替えられない。
/// 両者を同じ書き方に揃えるため、ここでも <c>TryGetDefined</c> を使う。
/// </remarks>
public sealed class DataAccessMode
    : ValueObjectOrderedBase<DataAccessMode, int>,
        IValueObject<DataAccessMode, int>
{
    public static readonly DataAccessMode Web = new(1, "Web");
    public static readonly DataAccessMode Database = new(2, "Database");
    public static readonly DataAccessMode Fake = new(3, "Fake");

    private DataAccessMode(int value, string modeName)
        : base(value) => ModeName = modeName;

    public string ModeName { get; }

    public static IEnumerable<DataAccessMode> GetList() => [Web, Database, Fake];

    static DataAccessMode IValueObject<DataAccessMode, int>.New(int value) =>
        GetList().First(x => x.Value == value);

    static bool IValueObject<DataAccessMode, int>.TryGetDefined(
        int value,
        out DataAccessMode? defined
    )
    {
        defined = GetList().FirstOrDefault(x => x.Value == value);

        return defined is not null;
    }

    // TryGetDefined だけでは未定義値を拒めない（New へ落ちるだけ）。対で検証を書くのが契約。
    static void IValueObject<DataAccessMode, int>.ValidateCore(int value, ref List<string>? errors)
    {
        if (!GetList().Any(x => x.Value == value))
        {
            (errors ??= new List<string>()).Add($"DataAccessMode {value} is not defined.");
        }
    }

    // 名前からの生成をジェネリック経路（TryCreateFrom）へ乗せる。扱わない形は基底へ委譲する。
    static bool IValueObject<DataAccessMode>.TryCreateFrom(
        object? raw,
        IFormatProvider? provider,
        out DataAccessMode? result,
        out IReadOnlyList<string> errors
    )
    {
        if (raw is string name && GetList().FirstOrDefault(x => x.ModeName == name) is { } hit)
        {
            result = hit;
            errors = Array.Empty<string>();

            return true;
        }

        return ValueObjectBase<DataAccessMode, int>.TryCreateFrom(
            raw,
            provider,
            out result,
            out errors
        );
    }

    public override string DisplayValue => ModeName;
}

/// <summary>
/// 生成 VO と同じ形（private コンストラクタ＋明示的実装の <c>New</c>／<c>ValidateCore</c>）で出力された型。
/// </summary>
/// <remarks>
/// ここが「生成側の partial」に相当する。<c>New</c> は既に実装済みで、利用者はこれを差し替えられない
/// ——それでも列挙型に拡張できることが、下の <c>ScreenMode</c> の partial で検証する受け入れ条件。
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
        OnValidate(value, errors ??= new List<string>());
    }

    static partial void OnValidate(int value, ICollection<string> errors);
}

/// <summary>生成 VO を利用者が partial で列挙型へ拡張した側（生成器のオプションは使わない）。</summary>
public sealed partial class ScreenMode
{
    public static readonly ScreenMode List = new(1) { ModeName = "List" };
    public static readonly ScreenMode Edit = new(2) { ModeName = "Edit" };

    public string ModeName { get; private init; } = string.Empty;

    public static IEnumerable<ScreenMode> GetList() => [List, Edit];

    static bool IValueObject<ScreenMode, int>.TryGetDefined(int value, out ScreenMode? defined)
    {
        defined = GetList().FirstOrDefault(x => x.Value == value);

        return defined is not null;
    }

    static partial void OnValidate(int value, ICollection<string> errors)
    {
        if (!GetList().Any(x => x.Value == value))
        {
            errors.Add($"ScreenMode {value} is not defined.");
        }
    }

    static bool IValueObject<ScreenMode>.TryCreateFrom(
        object? raw,
        IFormatProvider? provider,
        out ScreenMode? result,
        out IReadOnlyList<string> errors
    )
    {
        if (raw is string name && GetList().FirstOrDefault(x => x.ModeName == name) is { } hit)
        {
            result = hit;
            errors = Array.Empty<string>();

            return true;
        }

        return ValueObjectBase<ScreenMode, int>.TryCreateFrom(
            raw,
            provider,
            out result,
            out errors
        );
    }

    public override string DisplayValue => ModeName;
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

    // 生成 VO は New が明示的実装として出力済みなので差し替えられない。TryGetDefined は Create / TryCreate 側に
    // 割り込むため、利用者の partial だけで列挙型へ拡張できる。
    [Fact(DisplayName = "[定義済み] 生成形の VO も partial の TryGetDefined だけで列挙型にできる")]
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

    // TryGetDefined を書かない型の挙動が変わっていないこと（既定は「定義済みなし」）。
    [Fact(DisplayName = "[定義済み] フックを書かない VO は従来どおり毎回新しいインスタンスを作る")]
    public void フックなしVOは従来どおり()
    {
        CustomerIdValue.Create(5).Should().NotBeSameAs(CustomerIdValue.Create(5));
        CustomerIdValue.Create(5).Should().Be(CustomerIdValue.Create(5));
    }
}
