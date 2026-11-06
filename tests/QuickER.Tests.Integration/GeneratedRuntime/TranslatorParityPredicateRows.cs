using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using QuickER.Tests.GeneratedTranslatorParityFixture;
using QuickER.Tests.TestSupport.Engines;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>述語の表の行の入力（対象の表と述語）</summary>
public abstract record TranslatorParityPredicate;

/// <summary>probes の述語（シードは <see cref="TranslatorParityRuntimeTestsBase"/> の probes のシード）</summary>
/// <param name="Predicate">述語</param>
public sealed record ProbePredicate(Expression<Func<ProbeEntity, bool>> Predicate)
    : TranslatorParityPredicate;

/// <summary>
/// small_child の述語（<c>big_parent_id</c> が NULL 許容の int。シードは親 3 行と子 4 行＝外部キー 1・1・2・NULL）
/// </summary>
/// <param name="Predicate">述語</param>
public sealed record SmallChildPredicate(Expression<Func<SmallChildEntity, bool>> Predicate)
    : TranslatorParityPredicate;

/// <summary>
/// 翻訳器の一致の述語の表：1 行＝1 つの述語。基底の <see cref="TranslatorParityRuntimeTestsBase.Predicate_MatchesCSharp"/> が、
/// 3 つの実装先（SQL Server・SQLite・インメモリ）で、同じ実装先から読み戻した全行へ C# のラムダを当てた結果と比べる。
/// </summary>
/// <remarks>
/// <para>
/// 行は式の翻訳の NULL の補償の形ごとに並べる：値の側が null の <c>==</c> / <c>!=</c>（左右・リテラル・変数・null を返す呼び出し）、
/// 列側の <c>!=</c>、列どうし、否定の反転、否定の深さ（式の木で <c>Expression.Not</c> を重ねる＝C# のコンパイラは
/// <c>!!x</c> を畳むので書けない）、IN / NOT IN（null の要素・全 null・空・配列・List・null 許容の int）、OR、
/// NULL 許容の decimal、値引数の文字列一致、bool 列。
/// </para>
/// <para>
/// NULL 許容の列の順序比較の否定（ガードなしの <c>!(x.Amount &gt; v)</c>）は行にしない。翻訳器はド・モルガン展開せず
/// <c>NOT (...)</c> で包むので、NULL の行が C# と割れる既知の乖離（docs と <c>src/QuickER.Runtime/CLAUDE.md</c>
/// 「式の翻訳と NULL 補償」）。順序比較の否定は NOT NULL の列か、<c>!= null</c> のガードを付けた形で書く。
/// </para>
/// <para>
/// 列どうしの比較（name と group）は、NULL の 4 つの組（両方 NULL・片側だけ NULL の 2 通り・両方非 NULL の同値と異値）を
/// probes のシードが持つことを前提にする（id 1＝異値、id 10＝name だけ NULL、id 11＝同値、ほかは group が NULL）。
/// </para>
/// </remarks>
public static class TranslatorParityPredicateRows
{
    /// <summary>述語の表</summary>
    public static ScenarioTable<TranslatorParityPredicate> Table { get; } = Build();

    /// <summary>表の Id（Theory のデータ）</summary>
    public static TheoryData<string> Ids() => Table.Ids();

    /// <summary>値の位置に置く「評価すると null になる」メソッド呼び出し（式の木の評価を通る）</summary>
    private static string? ResolveMissing() => null;

    /// <summary>
    /// 述語の本体を <paramref name="times"/> 回だけ明示的な単項 Not で包んだ述語を返す
    /// （<c>!!x</c> と書くと C# のコンパイラが畳むので、木の形をここで決める）
    /// </summary>
    private static Expression<Func<T, bool>> Negate<T>(
        Expression<Func<T, bool>> predicate,
        int times
    )
    {
        Expression body = predicate.Body;

        for (var index = 0; index < times; index++)
        {
            body = Expression.Not(body);
        }

        return Expression.Lambda<Func<T, bool>>(body, predicate.Parameters);
    }

    private static ScenarioRow<TranslatorParityPredicate> Probe(
        string id,
        string description,
        Expression<Func<ProbeEntity, bool>> predicate
    ) => new(id, null, description, new ProbePredicate(predicate));

    private static ScenarioRow<TranslatorParityPredicate> SmallChild(
        string id,
        string description,
        Expression<Func<SmallChildEntity, bool>> predicate
    ) => new(id, null, description, new SmallChildPredicate(predicate));

    private static ScenarioTable<TranslatorParityPredicate> Build()
    {
        // 変数（クロージャの捕捉＝式の木では MemberExpression）で渡す値。リテラルの null とは木の形が違う
        string? missing = null;
        int? missingNumber = null;
        var name = "Alice";
        var names = new List<string?> { "Alice", "label" };
        var namesWithNull = new List<string?> { "Alice", null };
        var onlyNulls = new List<string?> { null, null };
        var oneNull = new List<string?> { null };
        var noNames = new List<string?>();
        var arrayWithNull = new string?[] { "Alice", null };
        var ids = new[] { 2, 4 };
        var noIds = Array.Empty<int>();
        var numbersWithNull = new List<int?> { 1, null, 3 };

        List<ScenarioRow<TranslatorParityPredicate>> rows =
        [
            // ---- 値の側が null の == / != は IS [NOT] NULL へ補償する ----
            Probe(
                "null-variable-equals",
                "null 変数との == は IS NULL へ補償される",
                x => x.Name == missing
            ),
            Probe(
                "null-variable-not-equals",
                "null 変数との != は IS NOT NULL へ補償される",
                x => x.Name != missing
            ),
            Probe(
                "null-variable-equals-left",
                "null 変数が左にある == も補償される",
                x => missing == x.Name
            ),
            Probe(
                "null-variable-not-equals-left",
                "null 変数が左にある != も補償される",
                x => missing != x.Name
            ),
            Probe(
                "literal-null-equals",
                "リテラル null の == は IS NULL のまま",
                x => x.Name == null
            ),
            Probe(
                "literal-null-not-equals",
                "リテラル null の != は IS NOT NULL のまま",
                x => x.Name != null
            ),
            Probe(
                "null-returning-call-equals",
                "null を返すメソッド呼び出しも評価の結果で補償される",
                x => x.Name == ResolveMissing()
            ),
            SmallChild(
                "relational-null-variable-greater",
                "関係演算子（>）の null は補償せずパラメータのまま（NULL 許容の int の列。C# の持ち上げた比較も偽）",
                c => c.BigParentId > missingNumber
            ),
            Probe(
                "null-variable-negated-equals",
                "!(== null 変数) は IS NOT NULL へ畳まれる",
                x => !(x.Name == missing)
            ),
            Probe(
                "literal-null-negated-not-equals",
                "!(!= リテラル null) は IS NULL へ畳まれる",
                x => !(x.Name != null)
            ),
            // ---- 列側の != の補償と、== の対照 ----
            Probe(
                "value-equals",
                "null でない値との == は列側を補償しない（NULL の行は元から不一致で C# と同じ）",
                x => x.Name == name
            ),
            Probe(
                "value-not-equals",
                "null でない値との != は (col <> @p OR col IS NULL) へ補償される",
                x => x.Name != name
            ),
            Probe(
                "value-not-equals-left",
                "!= の列側の補償は列が右にあっても働く",
                x => name != x.Name
            ),
            Probe(
                "value-negated-equals",
                "!(==) は != と同じ補償の形になる（NOT で包まない）",
                x => !(x.Name == name)
            ),
            Probe(
                "value-negated-not-equals",
                "!(!=) は == と同じ形になる（否定の深さ 1）",
                x => !(x.Name != name)
            ),
            Probe(
                "value-not-equals-negated-2",
                "!= の二重否定は畳まれ、素の比較と同じ補償の形になる",
                Negate<ProbeEntity>(x => x.Name != name, 2)
            ),
            Probe(
                "value-equals-negated-2",
                "== の二重否定は畳まれ、素の == と同じ形になる",
                Negate<ProbeEntity>(x => x.Name == name, 2)
            ),
            Probe(
                "value-not-equals-negated-3",
                "!= の三重否定は 1 回の否定（==）と同じ形になる",
                Negate<ProbeEntity>(x => x.Name != name, 3)
            ),
            Probe(
                "value-not-equals-negated-4",
                "!= の四重否定は素の比較へ戻る",
                Negate<ProbeEntity>(x => x.Name != name, 4)
            ),
            // ---- 列どうしは両側の NULL を明示した形へ補償する ----
            Probe(
                "column-not-equals",
                "列どうしの != は片側だけ NULL の行を含め、両側 NULL の行を含めない",
                x => x.Name != x.Group
            ),
            Probe(
                "column-equals",
                "列どうしの == は両側 NULL の行を含める",
                x => x.Name == x.Group
            ),
            Probe(
                "column-negated-equals",
                "!(列 == 列) は列どうしの != の補償の形になる",
                x => !(x.Name == x.Group)
            ),
            Probe(
                "column-negated-not-equals",
                "!(列 != 列) は列どうしの == の補償の形になる",
                x => !(x.Name != x.Group)
            ),
            Probe(
                "column-not-equals-negated-2",
                "列どうしの != の二重否定も畳まれ、補償の形が保たれる",
                Negate<ProbeEntity>(x => x.Name != x.Group, 2)
            ),
            Probe(
                "column-equals-negated-2",
                "列どうしの == の二重否定も畳まれ、補償の形が保たれる",
                Negate<ProbeEntity>(x => x.Name == x.Group, 2)
            ),
            Probe(
                "column-not-equals-negated-3",
                "列どうしの != の三重否定は 1 回の否定（列どうしの ==）と同じ行を返す",
                Negate<ProbeEntity>(x => x.Name != x.Group, 3)
            ),
            // ---- 等値以外の否定（NOT NULL の列） ----
            Probe(
                "relational-negated-greater-not-null-column",
                "等値以外の否定は NOT (...) で包まれる（NOT NULL の列）",
                x => !(x.ProbeId > 3)
            ),
            // ---- IN / NOT IN ----
            Probe(
                "in-list",
                "null を含まないリストの IN は素の IN (...) のまま（補償を足さない）",
                x => names.Contains(x.Name)
            ),
            Probe(
                "in-list-with-null",
                "null の要素を含む IN は (IN (...) OR IS NULL) へ補償される",
                x => namesWithNull.Contains(x.Name)
            ),
            Probe(
                "in-list-only-null",
                "全要素が null の IN は IS NULL 単独になる",
                x => onlyNulls.Contains(x.Name)
            ),
            Probe(
                "in-list-empty",
                "空のリストの IN は 1 = 0（全行不一致）",
                x => noNames.Contains(x.Name)
            ),
            Probe(
                "not-in-list",
                "null を含まないリストの NOT IN は (NOT IN (...) OR IS NULL) へ補償される",
                x => !names.Contains(x.Name)
            ),
            Probe(
                "not-in-list-with-null",
                "null の要素を含む NOT IN は (NOT IN (...) AND IS NOT NULL) になる（null は一致なので否定では落とす）",
                x => !namesWithNull.Contains(x.Name)
            ),
            Probe(
                "not-in-list-only-null",
                "全要素が null の NOT IN は IS NOT NULL 単独になる",
                x => !oneNull.Contains(x.Name)
            ),
            Probe(
                "not-in-list-empty",
                "空のリストの NOT IN は 1 = 1（全行一致）",
                x => !noNames.Contains(x.Name)
            ),
            Probe(
                "in-list-negated-2",
                "IN の二重否定は畳まれ、素の IN と同じ形になる",
                Negate<ProbeEntity>(x => names.Contains(x.Name), 2)
            ),
            Probe(
                "in-list-negated-3",
                "IN の三重否定は 1 回の否定（補償つきの NOT IN）と同じ形になる",
                Negate<ProbeEntity>(x => names.Contains(x.Name), 3)
            ),
            Probe(
                "in-array-with-null",
                "配列（span 経由の Contains）の null の要素も IS NULL へ畳まれる",
                x => arrayWithNull.Contains(x.Name)
            ),
            Probe(
                "not-in-array-with-null",
                "配列（span 経由の Contains）の NOT IN も null の要素を落とす形になる",
                x => !arrayWithNull.Contains(x.Name)
            ),
            Probe(
                "in-int-array-span",
                "int の配列の Contains（C# 14 の Span 形）が IN へ翻訳される",
                x => ids.Contains(x.ProbeId)
            ),
            Probe(
                "in-int-array-static",
                "int の配列の静的 Enumerable.Contains（2 引数）が IN へ翻訳される",
                x => Enumerable.Contains(ids, x.ProbeId)
            ),
            Probe(
                "in-int-array-empty",
                "空の int の配列の IN は 1 = 0（0 件）",
                x => noIds.Contains(x.ProbeId)
            ),
            Probe(
                "not-in-int-array-empty",
                "空の int の配列の NOT IN は 1 = 1（全行）",
                x => !noIds.Contains(x.ProbeId)
            ),
            SmallChild(
                "in-nullable-int-list-with-null",
                "null 許容の値型（int?）のリストでも null の要素が IS NULL へ畳まれる",
                c => numbersWithNull.Contains(c.BigParentId)
            ),
            // ---- bool 列の短縮形と否定の深さ ----
            Probe("bool-column", "bool 列（col = 1）", x => x.IsActive),
            Probe("bool-column-not", "bool 列の NOT（col = 0・否定の深さ 1）", x => !x.IsActive),
            Probe(
                "bool-column-negated-2",
                "二重否定の中身が比較でなくても畳まれる（bool 列）",
                Negate<ProbeEntity>(x => x.IsActive, 2)
            ),
            Probe(
                "bool-column-and-equals",
                "bool 列と == を AND で組み合わせる",
                x => x.IsActive && x.Name == "Alice"
            ),
            // ---- OR ----
            Probe(
                "or-else-equals",
                "列どうしでない 2 つの等値の OR が和集合を返す",
                x => x.Name == "Alice" || x.Name == "label"
            ),
            // ---- NULL 許容の decimal ----
            Probe(
                "decimal-not-equals",
                "NULL 許容の decimal の != は等値の行を除き、NULL の行を含める",
                x => x.Amount != 100m
            ),
            Probe(
                "decimal-greater-guarded",
                "NULL 許容の decimal の > が正しい範囲を返す（!= null のガードつき）",
                x => x.Amount != null && x.Amount.Value > 150m
            ),
            Probe(
                "decimal-less-guarded",
                "NULL 許容の decimal の < が正しい範囲を返す（!= null のガードつき）",
                x => x.Amount != null && x.Amount.Value < 250m
            ),
            Probe(
                "decimal-less-or-equal-guarded",
                "NULL 許容の decimal の <= が境界の値を含む（!= null のガードつき）",
                x => x.Amount != null && x.Amount.Value <= 200m
            ),
            Probe(
                "decimal-negated-greater-guarded",
                "NULL 許容の decimal の順序比較の否定（!= null のガードつき）",
                x => x.Amount != null && !(x.Amount.Value > 150m)
            ),
            // ---- 値引数の文字列一致 ----
#pragma warning disable CA1309 // 翻訳器が受け付ける呼び出しの形（Equals(string)）そのものを式の木で確かめる
            Probe(
                "value-argument-equals",
                "値引数の Equals(string) がパラメータ化された等値へ翻訳される",
                x => x.Name != null && x.Name.Equals("Alice")
            ),
#pragma warning restore CA1309
            Probe(
                "value-argument-starts-with",
                "値引数の StartsWith(string) が LIKE へ翻訳される",
                x => x.Name != null && x.Name.StartsWith("Al")
            ),
            Probe(
                "value-argument-ends-with",
                "値引数の EndsWith(string) が LIKE へ翻訳される",
                x => x.Name != null && x.Name.EndsWith("ce")
            ),
        ];

        return new ScenarioTable<TranslatorParityPredicate>(EngineIds.GeneratedRuntime, rows);
    }
}
