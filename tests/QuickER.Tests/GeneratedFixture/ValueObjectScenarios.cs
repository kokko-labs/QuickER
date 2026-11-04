using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;
using Xunit;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// 値オブジェクト（生成 VO・手書き VO）の生成・検証・生値変換・定義済みインスタンス・等値・書式を、
/// 決まった形のシナリオの表で確かめる（DB 不要・CI 常時実行）。
/// </summary>
/// <remarks>
/// <para>
/// 表は 4 つ＝vo-call（呼び出し 1 つの結果を 1 行の文字列へ写して照合）・vo-same（参照の同一性）・
/// vo-equality（等値の契約。演算子は静的な型が要るので、行のラムダが静的な型で計算した結果を返す）・
/// vo-first-use（初回使用時の fail-fast。行ごとに別の型）。行は <see cref="ValueObjectScenarioRows"/> の
/// 主題ごとのファイルにある。
/// </para>
/// <para>
/// 表を作るだけでは値オブジェクトの型に触れない（呼び出しも期待するインスタンスもラムダで持つ）。
/// 型の初回使用を観測する型は vo-first-use の行のラムダの中でだけ触れる。
/// </para>
/// </remarks>
public sealed class ValueObjectScenarioTests
{
    /// <summary>vo-call の行の Id</summary>
    public static TheoryData<string> CallIds => ValueObjectScenarioRows.Calls.Ids();

    /// <summary>vo-same の行の Id</summary>
    public static TheoryData<string> SameIds => ValueObjectScenarioRows.Sames.Ids();

    /// <summary>vo-equality の行の Id</summary>
    public static TheoryData<string> EqualityIds => ValueObjectScenarioRows.Equalities.Ids();

    /// <summary>vo-first-use の行の Id</summary>
    public static TheoryData<string> FirstUseIds => ValueObjectScenarioRows.FirstUses.Ids();

    [Theory(DisplayName = "[VO] 呼び出しの結果が期待の 1 行と一致する")]
    [MemberData(nameof(CallIds))]
    public void Call(string id)
    {
        var input = ValueObjectScenarioRows.Calls[id].Input;

        AssertRendered(input, id);
    }

    [Theory(DisplayName = "[VO] 結果が期待するインスタンスと同じ（または別の）参照になる")]
    [MemberData(nameof(SameIds))]
    public void Same(string id)
    {
        var input = ValueObjectScenarioRows.Sames[id].Input;

        var actual = input.Call();
        object? value = actual;

        if (input.Status is { } status)
        {
            actual.Should().BeOfType<ValueObjectOutcome.Result>("行が Try 系の状態を表明している");

            var result = (ValueObjectOutcome.Result)actual!;
            var rendered = ValueObjectOutcome.RenderStatus(result);
            ValueObjectOutcome
                .Matches(rendered, status)
                .Should()
                .BeTrue($"行 {id} の状態は「{status}」に一致する（実際は「{rendered}」）");
            value = result.Value;
        }
        else
        {
            actual
                .Should()
                .NotBeOfType<ValueObjectOutcome.Result>("状態を表明しない行は値だけを返す");
        }

        var expected = input.Expected();

        if (input.Same)
        {
            value.Should().BeSameAs(expected);
        }
        else
        {
            value.Should().NotBeSameAs(expected);
        }
    }

    [Theory(DisplayName = "[VO] 等値の契約の結果が期待と一致する")]
    [MemberData(nameof(EqualityIds))]
    public void Equality(string id)
    {
        var input = ValueObjectScenarioRows.Equalities[id].Input;

        AssertRendered(input, id);
    }

    [Theory(DisplayName = "[VO] 不正な宣言は型の初回使用時に fail-fast する")]
    [MemberData(nameof(FirstUseIds))]
    public void FirstUse(string id)
    {
        var input = ValueObjectScenarioRows.FirstUses[id].Input;

        // 初回使用を 1 回だけ起こす（2 回呼ぶと 2 回目は初回でなくなる）
        var act = () => input.FirstUse();

        var thrown = act.Should().Throw<Exception>();
        thrown.Which.Should().BeAssignableTo(input.ExceptionType);
        thrown.WithMessage(input.MessagePattern);
    }

    /// <summary>呼び出しの結果を写し、行の期待（完全一致かパターン）と照合する</summary>
    private static void AssertRendered(ValueObjectCallCase input, string id)
    {
        var rendered = ValueObjectOutcome.Render(input.Call);
        var expected = input.Expected();

        if (input.Exact)
        {
            rendered.Should().Be(expected, $"行 {id} の結果");
        }
        else
        {
            ValueObjectOutcome
                .Matches(rendered, expected)
                .Should()
                .BeTrue($"行 {id} の結果「{rendered}」はパターン「{expected}」に一致する");
        }
    }
}

/// <summary>vo-call・vo-equality の行の入力</summary>
/// <param name="Call">呼び出し（例外も結果として写す）</param>
/// <param name="Expected">期待の 1 行（行を作る時点で型に触れないよう、照合の時点で作る）</param>
/// <param name="Exact">true＝完全一致・false＝<c>*</c> を任意の文字列とするパターン一致</param>
internal sealed record ValueObjectCallCase(Func<object?> Call, Func<string> Expected, bool Exact);

/// <summary>vo-same の行の入力</summary>
/// <param name="Call">呼び出し（Try 系・ReadCell は <see cref="ValueObjectOutcome.Result"/> を返す）</param>
/// <param name="Expected">比べるインスタンス（照合の時点で引く）</param>
/// <param name="Same">true＝同じ参照・false＝別の参照</param>
/// <param name="Status">
/// Try 系・ReadCell の行だけが持つ、値を除いた状態のパターン（<see cref="ValueObjectOutcome.RenderStatus"/> と照合）。
/// null の行は値をそのまま返す。
/// </param>
internal sealed record ValueObjectSameCase(
    Func<object?> Call,
    Func<object> Expected,
    bool Same,
    string? Status
);

/// <summary>vo-first-use の行の入力</summary>
/// <param name="FirstUse">型の初回使用（このラムダの中でだけ型に触れる）</param>
/// <param name="ExceptionType">期待の例外の型（派生も可）</param>
/// <param name="MessagePattern">例外のメッセージのパターン（WithMessage の書式）</param>
internal sealed record ValueObjectFirstUseCase(
    Func<object?> FirstUse,
    Type ExceptionType,
    string MessagePattern
);

/// <summary>
/// 呼び出しの結果を、決まった書式の 1 行の文字列へ写す。
/// </summary>
/// <remarks>
/// <para>
/// 書式：例外は <c>throws {例外の型名}: {Message}</c>。値オブジェクトの検証例外なら続けて
/// <c> type={ValueObjectType.FullName} {エラー}</c>、<see cref="ArgumentException"/> 系なら <c> param={ParamName}</c>。
/// Try 系は <c>ok|fail value={値} {エラー}</c>、ReadCell は <c>value={値} {エラー}</c>、Validate は
/// <c>returns {true|false} {エラー}</c>、エラーの一覧だけなら <c>{エラー}</c>。
/// </para>
/// <para>
/// エラーは空なら <c>errors=[]</c>、1 件以上なら件数を添えて <c>errors(2)=[e1|e2]</c>。件数は 1 件以上のときだけ
/// 書くので、パターンの <c>errors(*)=[*]</c> は「1 件以上」を表す（空の <c>errors=[]</c> には括弧が無く一致しない）。
/// </para>
/// <para>
/// 値：null は <c>null</c>、値オブジェクトは <c>{型名}({内包値})</c>、string は引用符で囲み中の <c>"</c> <c>\</c> と
/// 制御文字を \ でエスケープ、bool は <c>true</c> / <c>false</c>、数はインバリアント、byte[] は <c>0x</c>＋大文字の 16 進。
/// decimal は末尾のゼロを落とした形（decimal の等値はスケールを見ないので、等値と同じ強さにする）。
/// DateTime は <c>o</c> 書式で Kind を書かない（DateTime の等値は Kind を見ないので、等値と同じ強さにする）。
/// </para>
/// </remarks>
internal static class ValueObjectOutcome
{
    /// <summary>Try 系・ReadCell・Validate・エラーの一覧の結果（行のラムダが返す）</summary>
    /// <param name="Kind">結果の形</param>
    /// <param name="Ok">Try 系の戻り値・Validate の戻り値（それ以外は未使用）</param>
    /// <param name="Value">得られた値（Try 系・ReadCell）</param>
    /// <param name="Errors">エラーの一覧</param>
    internal sealed record Result(
        ResultKind Kind,
        bool Ok,
        object? Value,
        IReadOnlyCollection<string> Errors
    );

    /// <summary>結果の形</summary>
    internal enum ResultKind
    {
        /// <summary>Try 系（戻り値・値・エラー）</summary>
        Try,

        /// <summary>ReadCell（値と、積んだエラー）</summary>
        Collected,

        /// <summary>Validate（戻り値と、積んだエラー）</summary>
        Validate,

        /// <summary>エラーの一覧だけ</summary>
        Errors,
    }

    /// <summary>Try 系の結果</summary>
    public static Result Try(bool ok, object? value, IReadOnlyCollection<string> errors) =>
        new(ResultKind.Try, ok, value, errors);

    /// <summary>ReadCell の結果（戻り値と、積んだエラー）</summary>
    public static Result Collected(object? value, IReadOnlyCollection<string> errors) =>
        new(ResultKind.Collected, false, value, errors);

    /// <summary>Validate の結果（戻り値と、積んだエラー）</summary>
    public static Result Validated(bool returned, IReadOnlyCollection<string> errors) =>
        new(ResultKind.Validate, returned, null, errors);

    /// <summary>エラーの一覧だけ</summary>
    public static Result ErrorsOnly(IReadOnlyCollection<string> errors) =>
        new(ResultKind.Errors, false, null, errors);

    /// <summary>呼び出しを 1 回だけ実行し、結果（例外を含む）を写す</summary>
    public static string Render(Func<object?> call)
    {
        object? value;

        try
        {
            value = call();
        }
        catch (Exception ex)
        {
            return RenderException(ex);
        }

        return value is Result result ? RenderResult(result) : RenderValue(value);
    }

    /// <summary>vo-same 用：値を除いた状態（Try 系は <c>ok|fail {エラー}</c>、ReadCell は <c>{エラー}</c>）</summary>
    public static string RenderStatus(Result result) =>
        result.Kind switch
        {
            ResultKind.Try => $"{(result.Ok ? "ok" : "fail")} {RenderErrors(result.Errors)}",
            ResultKind.Collected => RenderErrors(result.Errors),
            _ => throw new NotSupportedException(
                $"vo-same の状態は Try 系と ReadCell だけ（{result.Kind}）"
            ),
        };

    /// <summary><c>*</c> を任意の文字列（空も含む）とするパターン一致（<c>*</c> 以外の文字はそのまま比べる）</summary>
    public static bool Matches(string text, string pattern)
    {
        var parts = pattern.Split('*');

        if (parts.Length == 1)
        {
            return string.Equals(text, pattern, StringComparison.Ordinal);
        }

        if (!text.StartsWith(parts[0], StringComparison.Ordinal))
        {
            return false;
        }

        var position = parts[0].Length;

        for (var i = 1; i < parts.Length - 1; i++)
        {
            var found = text.IndexOf(parts[i], position, StringComparison.Ordinal);

            if (found < 0)
            {
                return false;
            }

            position = found + parts[i].Length;
        }

        var last = parts[^1];

        return text.Length - position >= last.Length
            && text.EndsWith(last, StringComparison.Ordinal);
    }

    private static string RenderResult(Result result) =>
        result.Kind switch
        {
            ResultKind.Try =>
                $"{(result.Ok ? "ok" : "fail")} value={RenderValue(result.Value)} {RenderErrors(result.Errors)}",
            ResultKind.Collected =>
                $"value={RenderValue(result.Value)} {RenderErrors(result.Errors)}",
            ResultKind.Validate =>
                $"returns {(result.Ok ? "true" : "false")} {RenderErrors(result.Errors)}",
            _ => RenderErrors(result.Errors),
        };

    private static string RenderErrors(IEnumerable<string> errors)
    {
        var list = errors.ToList();

        return list.Count == 0
            ? "errors=[]"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"errors({list.Count})=[{string.Join("|", list)}]"
            );
    }

    private static string RenderException(Exception ex)
    {
        var text = new StringBuilder();
        text.Append("throws ").Append(ex.GetType().Name).Append(": ").Append(ex.Message);

        // 生成フィクスチャは名前空間ごとに別の検証例外を持つので、型名で見分けてプロパティを読む
        if (ex.GetType().Name == "ValueObjectValidationException")
        {
            var type = (Type)ex.GetType().GetProperty("ValueObjectType")!.GetValue(ex)!;
            var errors = (IEnumerable<string>)ex.GetType().GetProperty("Errors")!.GetValue(ex)!;
            text.Append(" type=").Append(type.FullName).Append(' ').Append(RenderErrors(errors));
        }
        else if (ex is ArgumentException argument)
        {
            text.Append(" param=").Append(argument.ParamName);
        }

        return text.ToString();
    }

    private static string RenderValue(object? value)
    {
        switch (value)
        {
            case null:
                return "null";
            case string s:
                return RenderString(s);
            case bool b:
                return b ? "true" : "false";
            case byte[] bytes:
                return "0x" + Convert.ToHexString(bytes);
            case decimal d:
                // 末尾のゼロを落とす（decimal の等値はスケールを見ない）
                return (d / 1.0000000000000000000000000000m).ToString(CultureInfo.InvariantCulture);
            case DateTime dt:
                // Kind を書かない（DateTime の等値は Kind を見ない）
                return DateTime
                    .SpecifyKind(dt, DateTimeKind.Unspecified)
                    .ToString("o", CultureInfo.InvariantCulture);
            case int or long or short or byte or double or float:
                return ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture);
        }

        // 生成フィクスチャは名前空間ごとに別の IValueObjectCore を持つので、名前で見分けて内包値を読む
        var core = value
            .GetType()
            .GetInterfaces()
            .FirstOrDefault(i => i.Name == "IValueObjectCore");

        if (core is not null)
        {
            var underlying = core.GetProperty("UnderlyingValue")!.GetValue(value);

            return $"{value.GetType().Name}({RenderValue(underlying)})";
        }

        throw new NotSupportedException(
            $"写せない値の型 {value.GetType().FullName}（形を足すなら書式を決めてから）"
        );
    }

    private static string RenderString(string s)
    {
        var text = new StringBuilder("\"");

        foreach (var c in s)
        {
            switch (c)
            {
                case '"':
                    text.Append("\\\"");
                    break;
                case '\\':
                    text.Append("\\\\");
                    break;
                default:
                    if (char.IsControl(c))
                    {
                        text.Append(string.Create(CultureInfo.InvariantCulture, $"\\u{(int)c:X4}"));
                    }
                    else
                    {
                        text.Append(c);
                    }

                    break;
            }
        }

        return text.Append('"').ToString();
    }
}
