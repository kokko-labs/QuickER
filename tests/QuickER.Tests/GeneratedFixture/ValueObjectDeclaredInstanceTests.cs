using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using Xunit;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// [DeclaredInstance] の標準的な使い方（手書き VO）: 属性付きフィールドが閉じた集合を成し、
/// [Display] の名前が <c>DeclaredDisplayName</c> に出る。属性なしの便宜定数（Legacy）は集合に入らない。
/// </summary>
/// <remarks>機構の本体は共有基底（レジストリ＋RunValidation＋Materialize）なので、フィクスチャの実基底を継承する型で実検証できる（per-VO 生成出力はこの機能に関与しない）。</remarks>
public sealed class MeasureStatus
    : ValueObjectOrderedBase<MeasureStatus, int>,
        IValueObject<MeasureStatus, int>
{
    [DeclaredInstance, Display(Name = "拾い出し")]
    public static readonly MeasureStatus Preparing = new(1);

    [DeclaredInstance, Display(Name = "測定中")]
    public static readonly MeasureStatus InProgress = new(2);

    // Display なし＝DeclaredDisplayName が null になるアーム
    [DeclaredInstance]
    public static readonly MeasureStatus Completed = new(9);

    // 属性なしの便宜定数＝集合に入らない（値 99 は拒否される）
    public static readonly MeasureStatus Legacy = new(99);

    private MeasureStatus(int value)
        : base(value) { }

    static MeasureStatus IValueObject<MeasureStatus, int>.New(int value) => new(value);

    public override string DisplayValue => DeclaredDisplayName ?? base.DisplayValue;
}

/// <summary>
/// 生成形ミラー（partial フックあり）× [DeclaredInstance] の合成検証用。
/// フック（GetDefinedInstance / OnValidate）と属性が同じ差し込み口を取り合わないことを固定する。
/// </summary>
public sealed partial class StageMode
    : ValueObjectOrderedBase<StageMode, int>,
        IValueObject<StageMode, int>
{
    private StageMode(int value)
        : base(value) { }

    static StageMode IValueObject<StageMode, int>.New(int value) => new(value);

    static void IValueObject<StageMode, int>.ValidateCore(int value, ref List<string>? errors)
    {
        OnValidate(value, ref errors);
    }

    static partial void OnValidate(int value, ref List<string>? errors);

    static bool IValueObject<StageMode, int>.TryGetDefined(int value, out StageMode? defined)
    {
        StageMode? found = null;
        GetDefinedInstance(value, ref found);
        defined = found;
        return found is not null;
    }

    static partial void GetDefinedInstance(int value, ref StageMode? defined);
}

/// <summary>利用者側: 属性とフックを併用する（フックが同じ値 2 を別インスタンスへ引き当てる＝フック優先の検証）。</summary>
public sealed partial class StageMode
{
    [DeclaredInstance, Display(Name = "下書き")]
    public static readonly StageMode Draft = new(1);

    [DeclaredInstance, Display(Name = "公開")]
    public static readonly StageMode Published = new(2);

    /// <summary>フックが値 2 に対して返す別インスタンス（属性なし＝レジストリには載らない）。</summary>
    public static readonly StageMode HookPublished = new(2);

    static partial void GetDefinedInstance(int value, ref StageMode? defined)
    {
        if (value == 2)
        {
            defined = HookPublished;
        }
    }

    static partial void OnValidate(int value, ref List<string>? errors)
    {
        if (value == 3)
        {
            (errors ??= new List<string>()).Add("custom rule for 3");
        }
    }
}

/// <summary>
/// レシピ違反（フィールドを Create で初期化）の安全弁検証用。静的初期化の最中に走った走査が
/// 不完全な表をキャッシュしないこと＝初期化完了後は正しく効くことを固定する。
/// </summary>
public sealed class LateInitStatus
    : ValueObjectOrderedBase<LateInitStatus, int>,
        IValueObject<LateInitStatus, int>
{
    [DeclaredInstance]
    public static readonly LateInitStatus First = Create(1);

    [DeclaredInstance]
    public static readonly LateInitStatus Second = Create(2);

    private LateInitStatus(int value)
        : base(value) { }

    static LateInitStatus IValueObject<LateInitStatus, int>.New(int value) => new(value);
}

/// <summary>
/// 初期化子の無い [DeclaredInstance] フィールド（恒久 null）の挙動固定用。「cctor 実行中の null」と
/// 原理的に区別できないため fail-fast できず、集合は無音で無効のままになる＝docs / XmlDoc が明記する既知の境界。
/// </summary>
public sealed class UninitializedDeclaredStatus
    : ValueObjectOrderedBase<UninitializedDeclaredStatus, int>,
        IValueObject<UninitializedDeclaredStatus, int>
{
    [DeclaredInstance]
    public static readonly UninitializedDeclaredStatus Valid = new(1);

#pragma warning disable CS8618, CS0649 // 意図的な誤用（初期化子なし）を固定するための型
    [DeclaredInstance]
    public static readonly UninitializedDeclaredStatus Broken;
#pragma warning restore CS8618, CS0649

    private UninitializedDeclaredStatus(int value)
        : base(value) { }

    static UninitializedDeclaredStatus IValueObject<UninitializedDeclaredStatus, int>.New(
        int value
    ) => new(value);
}

/// <summary>不正な形（readonly でない static フィールド）への付与＝初回使用時 fail-fast の検証用。</summary>
public sealed class BrokenDeclaredStatus
    : ValueObjectOrderedBase<BrokenDeclaredStatus, int>,
        IValueObject<BrokenDeclaredStatus, int>
{
#pragma warning disable CA2211 // 意図的な誤用（readonly でない）をテストするための型
    [DeclaredInstance]
    public static BrokenDeclaredStatus Oops = new(1);
#pragma warning restore CA2211

    private BrokenDeclaredStatus(int value)
        : base(value) { }

    static BrokenDeclaredStatus IValueObject<BrokenDeclaredStatus, int>.New(int value) =>
        new(value);
}

/// <summary>バイナリ VO への付与＝非対応 fail-fast の検証用（byte[] の既定比較は参照等値のため集合が壊れる）。</summary>
public sealed class DeclaredBlob
    : ValueObjectBinaryBase<DeclaredBlob>,
        IValueObject<DeclaredBlob, byte[]>
{
    [DeclaredInstance]
    public static readonly DeclaredBlob Empty = new(Array.Empty<byte>());

    private DeclaredBlob(byte[] value)
        : base(value) { }

    static DeclaredBlob IValueObject<DeclaredBlob, byte[]>.New(byte[] value) => new(value);
}

/// <summary>
/// [DeclaredInstance] レジストリ（定義済みインスタンスの宣言的定義）の検証。
/// </summary>
/// <remarks>
/// 引き当て（Materialize のフック優先→レジストリ→new）と未定義値の拒否（RunValidation の
/// ValidateCore→メンバーシップ検査）の両アームを、成功・失敗・合成・安全弁・誤用 fail-fast で固定する。
/// 属性を持たない既存型の挙動・割り当てゼロが変わらないことも同じファイルで固定する。
/// </remarks>
public sealed class ValueObjectDeclaredInstanceTests
{
    [Fact(
        DisplayName = "[宣言済み] 属性付きフィールドが全生成経路で定義済みインスタンスとして返る"
    )]
    public void 属性付きフィールドが全生成経路で返る()
    {
        MeasureStatus.Create(1).Should().BeSameAs(MeasureStatus.Preparing);

        MeasureStatus.TryCreate(2, out var inProgress, out var errors).Should().BeTrue();
        inProgress.Should().BeSameAs(MeasureStatus.InProgress);
        errors.Should().BeEmpty();

        // DB 読み出し経路（値→VO の再ラップ）も同じインスタンス
        SqlValueObjectActivator
            .Wrap(9, typeof(MeasureStatus))
            .Should()
            .BeSameAs(MeasureStatus.Completed);
    }

    [Fact(DisplayName = "[宣言済み] 集合外の値は Create / TryCreate / Validate すべてで拒否される")]
    public void 集合外の値は拒否される()
    {
        MeasureStatus.TryCreate(5, out var value, out var errors).Should().BeFalse();
        value.Should().BeNull();
        errors.Should().ContainSingle().Which.Should().Be("'5' is not a defined MeasureStatus.");

        var act = () => MeasureStatus.Create(5);
        act.Should().Throw<ValueObjectValidationException>();

        var collected = new List<string>();
        MeasureStatus.Validate(5, collected).Should().BeFalse();
        collected.Should().ContainSingle();

        // 属性なしの便宜定数（Legacy=99）は集合に入らない＝99 も拒否される
        MeasureStatus.TryCreate(99, out _, out _).Should().BeFalse();
    }

    [Fact(DisplayName = "[宣言済み] GetDeclaredInstances は宣言順で、属性なしフィールドを含まない")]
    public void 一覧は宣言順で属性なしを含まない()
    {
        MeasureStatus
            .GetDeclaredInstances()
            .Should()
            .Equal(MeasureStatus.Preparing, MeasureStatus.InProgress, MeasureStatus.Completed);

        // 属性を持たない型は空（例外にしない）
        CustomerIdValue.GetDeclaredInstances().Should().BeEmpty();
    }

    [Fact(DisplayName = "[宣言済み] DeclaredDisplayName は [Display] の名前を返し、無い欄は null")]
    public void 表示名はDisplay属性から引ける()
    {
        MeasureStatus.Preparing.DisplayValue.Should().Be("拾い出し");
        MeasureStatus.InProgress.DisplayValue.Should().Be("測定中");

        // [Display] なし → DeclaredDisplayName は null → base.DisplayValue（ToString）へフォールバック
        MeasureStatus.Completed.DisplayValue.Should().Be("9");

        // 値等値で引くので、同値の別インスタンス（new 直呼びは型内でしかできないため Legacy で代用不可）でも
        // 定義済み側の値なら名前が届く＝Create が返す共有インスタンスで検証
        MeasureStatus.Create(1).DisplayValue.Should().Be("拾い出し");
    }

    // フック（GetDefinedInstance / OnValidate）と属性が同じ差し込み口を取り合わないことの固定。
    // ソースジェネレータ方式を退けた理由（合成が壊れる）の裏返しをテストで表明する。
    [Fact(DisplayName = "[宣言済み] フックと属性は併用でき、引き当てはフックが優先される")]
    public void フックと属性は併用できる()
    {
        // フックが値 2 を引き当てる → レジストリ（Published）より優先
        StageMode.Create(2).Should().BeSameAs(StageMode.HookPublished);

        // フックが扱わない値 1 はレジストリが引き当てる
        StageMode.Create(1).Should().BeSameAs(StageMode.Draft);

        // OnValidate とメンバーシップ検査は両方走る（値 3 は両方の違反が積まれる）
        StageMode.TryCreate(3, out _, out var errors).Should().BeFalse();
        errors.Should().Equal("custom rule for 3", "'3' is not a defined StageMode.");
    }

    [Fact(
        DisplayName = "[宣言済み] Create で初期化した型でも不完全な表がキャッシュされない（安全弁）"
    )]
    public void 静的初期化中の走査は表を固定しない()
    {
        // 最初のアクセスで静的初期化が走る。フィールド初期化子の Create は表が未完成のため素通りで成功する
        LateInitStatus.First.Value.Should().Be(1);
        LateInitStatus.Second.Value.Should().Be(2);

        // 初期化完了後は表が正しく組まれる＝引き当ても拒否も効く（不完全な表が固定されていたらここが壊れる）
        LateInitStatus.Create(1).Should().BeSameAs(LateInitStatus.First);
        LateInitStatus.TryCreate(3, out _, out var errors).Should().BeFalse();
        errors.Should().ContainSingle().Which.Should().Be("'3' is not a defined LateInitStatus.");
        LateInitStatus
            .GetDeclaredInstances()
            .Should()
            .Equal(LateInitStatus.First, LateInitStatus.Second);
    }

    // 検出不能な既知の境界の固定: 初期化子なしのフィールドは「cctor 実行中」と区別できないため
    // fail-fast されず、集合は無音で無効のまま（メンバーシップ検証も引き当ても一覧も効かない）。
    // docs / 属性 XmlDoc がこの境界を明記しており、将来この意味論を変えるならこのテストを意識的に更新する。
    [Fact(DisplayName = "[宣言済み] 初期化子なしのフィールドは検出されず、集合は無音で無効のまま")]
    public void 初期化子なしフィールドは集合を無効化する()
    {
        // 集合外の値が通る（検証が効いていない）
        UninitializedDeclaredStatus
            .TryCreate(5, out var outside, out var errors)
            .Should()
            .BeTrue();
        outside!.Value.Should().Be(5);
        errors.Should().BeEmpty();

        // 引き当ても効かない（宣言済みの値でも新しいインスタンスが作られる）
        UninitializedDeclaredStatus
            .Create(1)
            .Should()
            .NotBeSameAs(UninitializedDeclaredStatus.Valid);

        // 一覧も空のまま
        UninitializedDeclaredStatus.GetDeclaredInstances().Should().BeEmpty();
    }

    [Fact(
        DisplayName = "[宣言済み] readonly でないフィールドへの付与は初回使用時に fail-fast する"
    )]
    public void 不正な形への付与はfailfastする()
    {
        var act = () => BrokenDeclaredStatus.Create(1);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*public static readonly*Oops*");
    }

    [Fact(DisplayName = "[宣言済み] バイナリ VO への付与は初回使用時に fail-fast する")]
    public void バイナリVOへの付与はfailfastする()
    {
        var act = () => DeclaredBlob.Create(new byte[] { 1 });

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*not supported on a binary value object*DeclaredBlob*");
    }

    [Fact(DisplayName = "[宣言済み] 属性を持たない型の挙動は従来どおり（毎回新しいインスタンス）")]
    public void 属性なし型は従来どおり()
    {
        CustomerIdValue.Create(5).Should().NotBeSameAs(CustomerIdValue.Create(5));
        CustomerIdValue.Create(5).Should().Be(CustomerIdValue.Create(5));
    }

    // 宣言済み集合の成功パス（検証＝線形走査＋引き当て＝線形走査）が何も確保しないこと。
    // レジストリを辞書化・LINQ 化するなどの書き換えで確保が混入するとここが赤くなる。
    [Fact(DisplayName = "[宣言済み] 宣言済みインスタンスの生成成功パスは割り当てゼロ")]
    public void 宣言済み型の成功パスは割り当てゼロ()
    {
        // ウォームアップ（JIT・tier-up・初回走査の確保を測定から追い出す）
        for (var i = 0; i < 50_000; i++)
        {
            MeasureStatus.Create(2);
        }

        long delta = 0;

        // 測定中に tier-up が走った回を拾わないよう 3 ラウンド測り、最後のラウンドで表明する
        for (var round = 0; round < 3; round++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();

            for (var i = 0; i < 10_000; i++)
            {
                MeasureStatus.Create(2);
            }

            delta = GC.GetAllocatedBytesForCurrentThread() - before;
        }

        delta.Should().Be(0, "宣言済みインスタンスの引き当てと検証は何も確保しない");
    }
}
