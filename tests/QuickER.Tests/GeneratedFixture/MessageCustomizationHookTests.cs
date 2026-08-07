using System;
using System.Collections;
using System.Linq;
using AwesomeAssertions;
using Xunit;

namespace QuickER.Tests.GeneratedRemoteContractFixture;

/// <summary>
/// 生成コードのメッセージ・表示名カスタマイズ機構（一括＝static Func 差し替え／個別＝Customize* partial）を
/// 固定フィクスチャ上で検証する。
/// </summary>
/// <remarks>
/// <para>
/// 対象はリモート契約フィクスチャ（GeneratedRemoteContractFixture 名前空間）。この名前空間の static クラス
/// （EditModelMessages / GeneratedDisplayNames / ValueObjectValidationMessages）や既定文言を照合する他テストは
/// 存在しない（RemoteContractRuntimeTests は DB 面のみ）ため、static 差し替えや partial フックの常設実装が
/// 並列実行中の他テストへ干渉しない（フィクスチャごとに static クラスの複製を持つ名前空間分離を利用。
/// try/finally 復元だけでは xUnit のクラス並列実行の窓を塞げないため、この分離が本質的な安全策）。
/// </para>
/// <para>
/// static 差し替えテストは同一クラス内（xUnit はクラス内を直列実行）に集約し、finally で必ず既定へ復元する。
/// </para>
/// </remarks>
public class MessageCustomizationHookTests
{
    /// <summary>EditModelMessages.Required の一括差し替えが必須エラーへ反映される</summary>
    [Fact(DisplayName = "EditModelMessages.Required の差し替えが必須メッセージへ反映される")]
    public void RequiredMessage_Replacement_IsReflected()
    {
        var original = EditModelMessages.Required;
        EditModelMessages.Required = static displayName => $"{displayName} を入力してください";

        try
        {
            var model = new CustomerEditModel();

            model.Validate(includeChildren: false).Should().BeFalse();

            GetErrors(model, nameof(CustomerEditModel.BindingName))
                .Should()
                .ContainSingle()
                .Which.Should()
                .Be("Name を入力してください");
        }
        finally
        {
            EditModelMessages.Required = original;
        }
    }

    /// <summary>EditModelMessages.ParseFailed の一括差し替えが変換エラーへ反映される（表示名も文面へ使える）</summary>
    [Fact(DisplayName = "EditModelMessages.ParseFailed の差し替えが変換メッセージへ反映される")]
    public void ParseFailedMessage_Replacement_IsReflected()
    {
        var original = EditModelMessages.ParseFailed;
        EditModelMessages.ParseFailed = static (displayName, inputValue, typeName) =>
            $"{displayName}: '{inputValue}' は {typeName} として解釈できません";

        try
        {
            var model = new OrderEditModel();

            // OrderId は int 変換列。変換失敗はセッターで即エラー登録される
            model.BindingOrderId = "abc";

            GetErrors(model, nameof(OrderEditModel.BindingOrderId))
                .Should()
                .ContainSingle()
                .Which.Should()
                .Be("OrderId: 'abc' は int として解釈できません");
        }
        finally
        {
            EditModelMessages.ParseFailed = original;
        }
    }

    /// <summary>EditModelMessages.JoinValueObjectErrors の一括差し替えが VO 検証エラーの連結へ反映される</summary>
    [Fact(
        DisplayName = "EditModelMessages.JoinValueObjectErrors の差し替えが VO エラー表示へ反映される"
    )]
    public void JoinValueObjectErrors_Replacement_IsReflected()
    {
        var original = EditModelMessages.JoinValueObjectErrors;
        EditModelMessages.JoinValueObjectErrors = static errors =>
            $"{errors.Count} 件: {errors[0]}";

        try
        {
            var model = new CustomerEditModel();

            // NameValue は最大長 50。超過入力で VO 検証エラーが連結 Func を通って登録される
            model.BindingName = new string('a', 51);

            GetErrors(model, nameof(CustomerEditModel.BindingName))
                .Should()
                .ContainSingle()
                .Which.Should()
                .Be("1 件: Enter at most 50 characters. (currently 51 characters)");
        }
        finally
        {
            EditModelMessages.JoinValueObjectErrors = original;
        }
    }

    /// <summary>GeneratedDisplayNames.Resolve の一括差し替えが VO・Entity・必須メッセージの表示名へ反映される</summary>
    [Fact(DisplayName = "GeneratedDisplayNames.Resolve の差し替えが全表示名へ反映される")]
    public void DisplayNameResolver_Replacement_IsReflected()
    {
        var original = GeneratedDisplayNames.Resolve;

        // 「Description を使わない」切替と同型のポリシー差し替え（ここでは判別しやすい装飾に置換）
        GeneratedDisplayNames.Resolve = static (memberName, _) => $"[{memberName}]";

        try
        {
            // VO の静的 DisplayName
            NameValue.DisplayName.Should().Be("[Name]");

            // Entity の DisplayName（Description なし＝基底のクラス名フォールバックも Resolve を通る）
            new CustomerEntity()
                .DisplayName.Should()
                .Be("[CustomerEntity]");

            // EditModel の必須メッセージへも表示名経由で反映される
            var model = new CustomerEditModel();

            model.Validate(includeChildren: false).Should().BeFalse();

            GetErrors(model, nameof(CustomerEditModel.BindingName))
                .Should()
                .ContainSingle()
                .Which.Should()
                .Be("'[Name]' is required.");
        }
        finally
        {
            GeneratedDisplayNames.Resolve = original;
        }
    }

    /// <summary>ValueObjectValidationMessages の一括差し替えが VO 検証エラーへ反映される</summary>
    [Fact(
        DisplayName = "ValueObjectValidationMessages.MaxLengthExceeded の差し替えが VO エラーへ反映される"
    )]
    public void ValueObjectMessage_Replacement_IsReflected()
    {
        var original = ValueObjectValidationMessages.MaxLengthExceeded;
        ValueObjectValidationMessages.MaxLengthExceeded = static (maxLength, actualLength) =>
            $"{maxLength} 文字以内で入力してください（現在 {actualLength} 文字）";

        try
        {
            NameValue.TryCreate(new string('a', 51), out _, out var errors).Should().BeFalse();

            errors
                .Should()
                .ContainSingle()
                .Which.Should()
                .Be("50 文字以内で入力してください（現在 51 文字）");
        }
        finally
        {
            ValueObjectValidationMessages.MaxLengthExceeded = original;
        }
    }

    /// <summary>CustomizeRequiredErrorMessage の partial 実装が propertyName で対象列だけを差し替える</summary>
    [Fact(DisplayName = "CustomizeRequiredErrorMessage は対象列のみ差し替え・他列は既定文言のまま")]
    public void CustomizeRequiredErrorMessage_Hook_RewritesOnlyTargetProperty()
    {
        var model = new CustomerEditModel();

        model.Validate(includeChildren: false).Should().BeFalse();

        // partial 実装（本ファイル末尾）は CustomerId のみ差し替える
        GetErrors(model, nameof(CustomerEditModel.BindingCustomerId))
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(MessageCustomizationHookConstants.CustomerIdRequiredMessage);

        // 対象外の Name は既定文言のまま
        GetErrors(model, nameof(CustomerEditModel.BindingName))
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be("'Name' is required.");
    }

    /// <summary>CustomizeParseErrorMessage の partial 実装が propertyName で対象列だけを微調整する</summary>
    [Fact(DisplayName = "CustomizeParseErrorMessage は対象列のみ加工・他列は既定文言のまま")]
    public void CustomizeParseErrorMessage_Hook_RewritesOnlyTargetProperty()
    {
        var model = new OrderEditModel();

        // partial 実装（本ファイル末尾）は Amount のみ既定文言へ接尾辞を足す
        model.BindingAmount = "abc";

        GetErrors(model, nameof(OrderEditModel.BindingAmount))
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(
                $"'abc' cannot be converted to decimal.{MessageCustomizationHookConstants.ParseSuffix}"
            );

        // 対象外の OrderId は既定文言のまま
        model.BindingOrderId = "abc";

        GetErrors(model, nameof(OrderEditModel.BindingOrderId))
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be("'abc' cannot be converted to int.");
    }

    /// <summary>EditModelMessages.DuplicateValue の一括差し替えが重複エラーへ反映される（表示名列挙を受け取る）</summary>
    [Fact(DisplayName = "EditModelMessages.DuplicateValue の差し替えが重複メッセージへ反映される")]
    public void DuplicateValueMessage_Replacement_IsReflected()
    {
        var original = EditModelMessages.DuplicateValue;
        EditModelMessages.DuplicateValue = static displayNames =>
            $"{string.Join("・", displayNames)} が重複しています";

        try
        {
            // 複合制約（CustomerId + Amount）は個別フックの対象外なので、一括差し替えの文言がそのまま出る
            var collection = new EditModelCollection<OrderEditModel>
            {
                NewOrderEditModel(10, 1, 100m, "apple pie"),
                NewOrderEditModel(11, 1, 100m, "banana"),
            };

            collection.Validate().Should().BeFalse();

            GetErrors(collection[0], nameof(OrderEditModel.BindingAmount))
                .Should()
                .ContainSingle()
                .Which.Should()
                .Be("CustomerId・Amount が重複しています");
        }
        finally
        {
            EditModelMessages.DuplicateValue = original;
        }
    }

    /// <summary>CustomizeDuplicateErrorMessage の partial 実装が構成列で対象制約だけを差し替える</summary>
    [Fact(
        DisplayName = "CustomizeDuplicateErrorMessage は対象制約のみ差し替え・他制約は既定文言のまま"
    )]
    public void CustomizeDuplicateErrorMessage_Hook_RewritesOnlyTargetConstraint()
    {
        // 単一列制約（Memo）の重複＝partial 実装（本ファイル末尾）が固定文言へ差し替える
        var memoDuplicates = new EditModelCollection<OrderEditModel>
        {
            NewOrderEditModel(10, 1, 100m, "apple pie"),
            NewOrderEditModel(11, 2, 50m, "apple pie"),
        };

        memoDuplicates.Validate().Should().BeFalse();

        GetErrors(memoDuplicates[0], nameof(OrderEditModel.BindingMemo))
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(MessageCustomizationHookConstants.DuplicateMemoMessage);

        // 対象外の複合制約は既定文言のまま
        var compositeDuplicates = new EditModelCollection<OrderEditModel>
        {
            NewOrderEditModel(10, 1, 100m, "apple pie"),
            NewOrderEditModel(11, 1, 100m, "banana"),
        };

        compositeDuplicates.Validate().Should().BeFalse();

        GetErrors(compositeDuplicates[0], nameof(OrderEditModel.BindingAmount))
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be("'CustomerId, Amount' is already used.");
    }

    /// <summary>重複検証用に入力済みの注文 EditModel を組み立てる</summary>
    private static OrderEditModel NewOrderEditModel(
        int orderId,
        int customerId,
        decimal amount,
        string memo
    ) =>
        new()
        {
            BindingOrderId = orderId.ToString(),
            BindingCustomerId = customerId.ToString(),
            BindingAmount = amount.ToString(),
            BindingMemo = memo,
        };

    /// <summary>CustomizeValueRequiredErrorMessage の partial 実装が null 入力エラーを差し替える</summary>
    [Fact(DisplayName = "CustomizeValueRequiredErrorMessage が VO の null エラーを差し替える")]
    public void CustomizeValueRequiredErrorMessage_Hook_RewritesNullError()
    {
        MemoValue.TryCreate(null!, out _, out var errors).Should().BeFalse();

        errors
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(MessageCustomizationHookConstants.MemoRequiredMessage);
    }

    /// <summary>エラーメッセージ一覧を文字列で取得する</summary>
    private static System.Collections.Generic.List<string> GetErrors(
        EditModelBase model,
        string propertyName
    ) => ((IEnumerable)model.GetErrors(propertyName)).Cast<string>().ToList();
}

/// <summary>テストで期待する差し替え文言を 1 か所に定義する（partial 実装とアサートで共有）</summary>
internal static class MessageCustomizationHookConstants
{
    /// <summary>CustomerEditModel.CustomerId の必須エラーへ差し替える文言</summary>
    public const string CustomerIdRequiredMessage = "顧客 ID は必須です";

    /// <summary>OrderEditModel.Amount の変換エラーへ足す接尾辞</summary>
    public const string ParseSuffix = " (例: 1234.56)";

    /// <summary>MemoValue の null エラーへ差し替える文言</summary>
    public const string MemoRequiredMessage = "メモには null を渡せません";

    /// <summary>OrderEditModel.Memo の重複エラーへ差し替える文言</summary>
    public const string DuplicateMemoMessage = "そのメモは既に使われています";
}

/// <summary>固定フィクスチャの CustomerEditModel へ必須メッセージの個別フックを注入する partial 実装。</summary>
/// <remarks>再生成でフィクスチャ本体（.g.cs）が上書きされてもこの partial は残る（拡張ポイントの意図どおり）。</remarks>
public partial class CustomerEditModel
{
    /// <summary>CustomerId の必須エラーのみ固定文言へ差し替える（propertyName 分岐の検証用）</summary>
    partial void CustomizeRequiredErrorMessage(string propertyName, ref string message)
    {
        if (propertyName == nameof(CustomerId))
        {
            message = MessageCustomizationHookConstants.CustomerIdRequiredMessage;
        }
    }
}

/// <summary>固定フィクスチャの OrderEditModel へ変換メッセージの個別フックを注入する partial 実装。</summary>
public partial class OrderEditModel
{
    /// <summary>Amount の変換エラーのみ既定文言へ接尾辞を足す（既定 Func → partial の順で通るチェーンの検証用）</summary>
    partial void CustomizeParseErrorMessage(
        string propertyName,
        string inputValue,
        string typeName,
        ref string message
    )
    {
        if (propertyName == nameof(Amount))
        {
            message += MessageCustomizationHookConstants.ParseSuffix;
        }
    }

    /// <summary>単一列制約（Memo のみ）の重複エラーだけ固定文言へ差し替える（構成列分岐の検証用）</summary>
    partial void CustomizeDuplicateErrorMessage(
        System.Collections.Generic.IReadOnlyList<string> propertyNames,
        ref string message
    )
    {
        if (propertyNames.Count == 1 && propertyNames[0] == nameof(Memo))
        {
            message = MessageCustomizationHookConstants.DuplicateMemoMessage;
        }
    }
}

/// <summary>固定フィクスチャの MemoValue へ null エラーの個別フックを注入する partial 実装。</summary>
public sealed partial class MemoValue
{
    /// <summary>null 入力の検証エラーを固定文言へ差し替える</summary>
    static partial void CustomizeValueRequiredErrorMessage(ref string message) =>
        message = MessageCustomizationHookConstants.MemoRequiredMessage;
}
