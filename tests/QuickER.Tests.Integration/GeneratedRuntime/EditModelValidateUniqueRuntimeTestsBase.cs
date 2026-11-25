using System;
using System.Collections;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// EditModel の照合糖衣（<c>ValidateUniqueAsync</c>）の共通基底：保存先に注文 10（顧客 1・100・apple pie）がある状態で、
/// 注文の EditModel の確定値から組んだ Entity をリポジトリの重複チェックへ渡し、違反の構成列をバインディングプロパティへ写すことと、
/// 再検証で古い重複エラーが残らないことを、実装先（QuickER 版 SQLite・インメモリ・リモート）に依らず同じ表明で確かめる。
/// </summary>
/// <remarks>
/// <para>
/// 生成物はフィクスチャごとに別の名前空間の型になる（クエリフィクスチャとリモートサービスのフィクスチャ）ので、EditModel の型は型引数で
/// 受け、組み立て・照合・親への追加は派生のアダプタへ委ねる。エラーは <see cref="INotifyDataErrorInfo"/> 越しに読む。
/// </para>
/// <para>
/// 図の一意制約は orders の memo（単一列）と customer_id＋amount（複合）。ユーザー定義の重複チェック（金額
/// <see cref="CustomCheckAmount"/> を予約済みとして弾く）は実装先のリポジトリの拡張点が持つ（リモートはサーバー側）。
/// </para>
/// </remarks>
/// <typeparam name="TOrderModel">注文の EditModel の型</typeparam>
public abstract class EditModelValidateUniqueRuntimeTestsBase<TOrderModel> : IAsyncLifetime
    where TOrderModel : class, INotifyDataErrorInfo
{
    /// <summary>テスト全体で使うキャンセルトークン</summary>
    protected static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>金額のバインディングプロパティの名前（生成物に依らず同じ）</summary>
    private const string BindingAmount = "BindingAmount";

    /// <summary>顧客 ID のバインディングプロパティの名前</summary>
    private const string BindingCustomerId = "BindingCustomerId";

    /// <summary>メモのバインディングプロパティの名前</summary>
    private const string BindingMemo = "BindingMemo";

    /// <summary>保存先を作り、顧客 1（Alice）と注文 10（顧客 1・100・apple pie）を入れる</summary>
    protected abstract Task SetUpAsync();

    /// <summary>保存先と DI コンテナを片付ける</summary>
    protected abstract Task TearDownAsync();

    /// <summary>入力済みの注文の EditModel を組み立てる（<paramref name="orderId"/> が空なら主キー未入力の新規行）</summary>
    protected abstract TOrderModel NewOrder(
        string orderId,
        int customerId,
        decimal amount,
        string memo
    );

    /// <summary>EditModel の <c>ValidateUniqueAsync</c> を実装先の注文リポジトリで呼ぶ</summary>
    protected abstract Task<bool> ValidateUniqueAsync(TOrderModel model);

    /// <summary>EditModel のメモのバインディングプロパティを書き換える</summary>
    protected abstract void SetMemo(TOrderModel model, string memo);

    /// <summary>EditModel が主キーの確定値を持つか</summary>
    protected abstract bool HasOrderId(TOrderModel model);

    /// <summary>
    /// EditModel を新しい顧客（1・Alice）の EditModel の注文へ足し、顧客を子ごと検証する関数を返す
    /// </summary>
    protected abstract Func<bool> AttachToNewCustomer(TOrderModel model);

    /// <summary>ユーザー定義の重複チェックが予約済みとして弾く金額</summary>
    protected abstract decimal CustomCheckAmount { get; }

    /// <summary>ユーザー定義の重複チェックが返すメッセージ（メッセージを指定しない実装先では null）</summary>
    protected abstract string? CustomCheckMessage { get; }

    public async ValueTask InitializeAsync() => await SetUpAsync();

    public async ValueTask DisposeAsync()
    {
        await TearDownAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>指定プロパティのエラー一覧を取り出す</summary>
    private static string[] GetErrors(TOrderModel model, string propertyName) =>
        ((IEnumerable)model.GetErrors(propertyName)).Cast<string>().ToArray();

    /// <summary>1. 保存先に同じ値の行があると false を返し、構成列のバインディングプロパティへエラーが載る</summary>
    [Fact(DisplayName = "[ValidateUnique] 1: 保存先の重複でエラーが登録される")]
    public async Task Duplicate_RegistersBindingError()
    {
        var model = NewOrder("99", 1, 12m, "apple pie");

        (await ValidateUniqueAsync(model)).Should().BeFalse();

        GetErrors(model, BindingMemo).Should().ContainSingle();
        GetErrors(model, BindingAmount).Should().BeEmpty();
    }

    /// <summary>2. 自分自身（同一主キーの行）は除外されるため、既存行の編集は違反にならない</summary>
    [Fact(DisplayName = "[ValidateUnique] 2: 同一主キーの行は除外される")]
    public async Task SameKey_IsExcluded()
    {
        var model = NewOrder("10", 1, 100m, "apple pie");

        (await ValidateUniqueAsync(model)).Should().BeTrue();
        model.HasErrors.Should().BeFalse();
    }

    /// <summary>3. 重複を解消して再検証すると前回の重複エラーが消える</summary>
    [Fact(DisplayName = "[ValidateUnique] 3: 再検証で古い重複エラーが消える")]
    public async Task Revalidation_ClearsPreviousErrors()
    {
        var model = NewOrder("99", 1, 12m, "apple pie");

        (await ValidateUniqueAsync(model)).Should().BeFalse();

        SetMemo(model, "pear");

        (await ValidateUniqueAsync(model)).Should().BeTrue();
        GetErrors(model, BindingMemo).Should().BeEmpty();
        model.HasErrors.Should().BeFalse();
    }

    /// <summary>4. 複合制約の違反は全構成列のバインディングプロパティへ載る</summary>
    [Fact(DisplayName = "[ValidateUnique] 4: 複合制約の違反が全構成列へ載る")]
    public async Task CompositeViolation_RegistersOnAllMembers()
    {
        var model = NewOrder("99", 1, 100m, "pear");

        (await ValidateUniqueAsync(model)).Should().BeFalse();

        GetErrors(model, BindingCustomerId).Should().ContainSingle();
        GetErrors(model, BindingAmount).Should().ContainSingle();
        GetErrors(model, BindingMemo).Should().BeEmpty();
    }

    /// <summary>5. ユーザー定義の重複チェックの違反は構成列へ載り、メッセージを指定していればそれが優先される</summary>
    [Fact(
        DisplayName = "[ValidateUnique] 5: ユーザー定義フックの違反が構成列へ載る（メッセージは優先）"
    )]
    public async Task CustomViolation_UsesItsOwnMessage()
    {
        var model = NewOrder("99", 2, CustomCheckAmount, "pear");

        (await ValidateUniqueAsync(model)).Should().BeFalse();

        var errors = GetErrors(model, BindingAmount);
        errors.Should().ContainSingle();

        if (CustomCheckMessage is { } message)
        {
            errors[0].Should().Be(message, "フックが指定したメッセージが優先される");
        }
    }

    /// <summary>7. 主キー未入力（新規行）でも保存先の重複が検出される</summary>
    /// <remarks>
    /// 新規行の EditModel は主キーが未入力＝確定値 null のまま <c>ValidateUniqueAsync</c> を呼ぶのが正規の使い方。
    /// 自分自身の除外は主キーがあるときだけ足す（かつ翻訳器が null 等値を <c>IS NULL</c> へ補償する）ので、
    /// 除外が全行を弾いて重複を見逃すことはない。
    /// </remarks>
    [Fact(DisplayName = "[ValidateUnique] 7: 主キー未入力の新規行でも重複が検出される")]
    public async Task NewModelWithoutKey_RegistersBindingError()
    {
        var model = NewOrder(string.Empty, 2, 12m, "apple pie");

        HasOrderId(model).Should().BeFalse("主キー未入力の新規行は確定値を持たない");

        (await ValidateUniqueAsync(model)).Should().BeFalse();

        GetErrors(model, BindingMemo).Should().ContainSingle();
    }

    /// <summary>8. 照合で登録したエラーは、その後のコレクション内重複検証で消えない</summary>
    /// <remarks>
    /// 画面の定型は「照合 → 保存直前にグラフ全体を Validate」で、旧実装はその Validate（コレクション側の
    /// 一括クリア）が照合の結果を巻き添えで消していた。重複エラーはチェックごとに分離して保持する。
    /// </remarks>
    [Fact(DisplayName = "[ValidateUnique] 8: 親の Validate 後も照合由来の重複エラーが残る")]
    public async Task DatabaseError_SurvivesSiblingValidation()
    {
        var model = NewOrder("99", 1, 12m, "apple pie");
        var validateCustomer = AttachToNewCustomer(model);

        (await ValidateUniqueAsync(model)).Should().BeFalse();
        GetErrors(model, BindingMemo).Should().ContainSingle();

        // 兄弟は 1 件だけなのでコレクション内重複はゼロ＝この検証は何も登録しない
        validateCustomer().Should().BeFalse();

        GetErrors(model, BindingMemo).Should().ContainSingle("照合の結果は兄弟間検証で消えない");
    }
}
