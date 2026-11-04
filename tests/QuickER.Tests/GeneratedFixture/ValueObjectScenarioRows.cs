using System;
using System.Collections.Generic;
using System.Globalization;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// <see cref="ValueObjectScenarioTests"/> の 4 つの表と、行を書くための部品。行は主題ごとのファイルにある。
/// </summary>
/// <remarks>
/// 表は式形式のプロパティで、引くたびに作る（partial の static readonly フィールドは初期化の順序で null になり得るため）。
/// 行を並べるだけでは値オブジェクトの型に触れない（呼び出し・期待の値・期待のインスタンスはすべてラムダ）。
/// </remarks>
internal static partial class ValueObjectScenarioRows
{
    /// <summary>vo-call：呼び出し 1 つの結果を 1 行の文字列へ写して照合する</summary>
    internal static ScenarioTable<ValueObjectCallCase> Calls =>
        new(
            EngineIds.GeneratedRuntime,
            [
                .. CreationCallRows(),
                .. MemberCallRows(),
                .. ConversionCallRows(),
                .. DeclaredCallRows(),
                .. DefinedCallRows(),
                .. FormattingCallRows(),
            ]
        );

    /// <summary>vo-same：参照の同一性（定義済み・宣言済みインスタンスの引き当て）</summary>
    internal static ScenarioTable<ValueObjectSameCase> Sames =>
        new(EngineIds.GeneratedRuntime, [.. DeclaredSameRows(), .. DefinedSameRows()]);

    /// <summary>vo-equality：等値の契約（行のラムダが静的な型で計算した結果を返す）</summary>
    internal static ScenarioTable<ValueObjectCallCase> Equalities =>
        new(EngineIds.GeneratedRuntime, EqualityRows());

    /// <summary>vo-first-use：不正な宣言の初回使用時 fail-fast（行ごとに別の型）</summary>
    internal static ScenarioTable<ValueObjectFirstUseCase> FirstUses =>
        new(EngineIds.GeneratedRuntime, FirstUseRows());

    /// <summary>ja-JP（桁区切り・日付の書式の確認用）</summary>
    private static CultureInfo Japanese => CultureInfo.GetCultureInfo("ja-JP");

    /// <summary>完全一致の行</summary>
    private static ScenarioRow<ValueObjectCallCase> Exact(
        string id,
        string description,
        Func<object?> call,
        string expected
    ) => new(id, null, description, new ValueObjectCallCase(call, () => expected, true));

    /// <summary>完全一致の行（期待を照合の時点で作る＝行を作る時点で型に触れない）</summary>
    private static ScenarioRow<ValueObjectCallCase> ExactFrom(
        string id,
        string description,
        Func<object?> call,
        Func<string> expected
    ) => new(id, null, description, new ValueObjectCallCase(call, expected, true));

    /// <summary>パターン一致の行（<c>*</c> は元が包含・件数・空でないこと・見ていない部分にだけ使う）</summary>
    private static ScenarioRow<ValueObjectCallCase> Like(
        string id,
        string description,
        Func<object?> call,
        string pattern
    ) => new(id, null, description, new ValueObjectCallCase(call, () => pattern, false));

    /// <summary>同じ参照になる行</summary>
    private static ScenarioRow<ValueObjectSameCase> SameAs(
        string id,
        string description,
        Func<object?> call,
        Func<object> expected,
        string? status = null
    ) => new(id, null, description, new ValueObjectSameCase(call, expected, true, status));

    /// <summary>別の参照になる行</summary>
    private static ScenarioRow<ValueObjectSameCase> NotSameAs(
        string id,
        string description,
        Func<object?> call,
        Func<object> expected
    ) => new(id, null, description, new ValueObjectSameCase(call, expected, false, null));

    /// <summary>初回使用時に fail-fast する行</summary>
    private static ScenarioRow<ValueObjectFirstUseCase> FirstUseOf(
        string id,
        string description,
        Func<object?> firstUse,
        Type exceptionType,
        string messagePattern
    ) =>
        new(
            id,
            null,
            description,
            new ValueObjectFirstUseCase(firstUse, exceptionType, messagePattern)
        );

    /// <summary>取り込みコードの想定形。下地の型を名指しせずに任意の VO を作る。</summary>
    private static T? ReadCell<T>(object? cell, IFormatProvider? provider, List<string> errors)
        where T : class, IValueObject<T>
    {
        if (T.TryCreateFrom(cell, provider, out var value, out var messages))
        {
            return value;
        }

        errors.AddRange(messages);

        return null;
    }

    /// <summary>ReadCell を呼び、戻り値と積んだエラーを結果にする</summary>
    private static ValueObjectOutcome.Result ReadCellOutcome<T>(
        object? cell,
        IFormatProvider? provider
    )
        where T : class, IValueObject<T>
    {
        var errors = new List<string>();
        var value = ReadCell<T>(cell, provider, errors);

        return ValueObjectOutcome.Collected(value, errors);
    }

    /// <summary>型引数経由で Create を呼ぶ（static abstract ディスパッチの再現）。</summary>
    private static TVo CreateViaTypeParameter<TVo, TValue>(TValue value)
        where TVo : IValueObject<TVo, TValue> => TVo.Create(value);
}
