using System;
using System.Collections.Generic;
using QuickER.Tests.TestSupport.Engines;
using RowVerValue = QuickER.Tests.GeneratedConcurrencyFixture.RowVerValue;

namespace QuickER.Tests.GeneratedFixture;

// vo-equality の行：等値の契約（Equals・GetHashCode・== / !=・辞書・HashSet・型付き Equals・EqualityComparer の型）。
// 演算子は静的な型が要るので、行のラムダが静的な型で計算した結果（bool・件数・値）を返す。
// バイナリ VO（RowVerValue）の等値はスパンの SequenceEqual、ハッシュは HashCode.AddBytes による全量ベース。
internal static partial class ValueObjectScenarioRows
{
    private static IEnumerable<ScenarioRow<ValueObjectCallCase>> EqualityRows() =>
        [
            // ===== int VO の Equals / GetHashCode（値ベース） =====
            Exact(
                "equals-int-same-value",
                "VO の Equals / GetHashCode は値ベース（同値は等しい）",
                () => CustomerIdValue.Create(5).Equals(CustomerIdValue.Create(5)),
                "true"
            ),
            Exact(
                "hash-int-same-value",
                "VO の Equals / GetHashCode は値ベース（同値は同一ハッシュ）",
                () =>
                    CustomerIdValue.Create(5).GetHashCode()
                    == CustomerIdValue.Create(5).GetHashCode(),
                "true"
            ),
            Exact(
                "equals-int-different-value",
                "VO の Equals / GetHashCode は値ベース（別の値は等しくない）",
                () => CustomerIdValue.Create(5).Equals(CustomerIdValue.Create(6)),
                "false"
            ),
            Exact(
                "equals-int-other-type-string",
                "VO の Equals / GetHashCode は値ベース（同じ表記の文字列とは等しくない）",
                () => CustomerIdValue.Create(5).Equals("5"),
                "false"
            ),
            // ===== int VO の == / != 演算子（null も扱える） =====
            Exact(
                "operator-int-equal-same-value",
                "VO の == / != 演算子が値ベースで機能し、null も扱える（同値の ==）",
                () => CustomerIdValue.Create(5) == CustomerIdValue.Create(5),
                "true"
            ),
            Exact(
                "operator-int-not-equal-different-value",
                "VO の == / != 演算子が値ベースで機能し、null も扱える（別の値の !=）",
                () => CustomerIdValue.Create(5) != CustomerIdValue.Create(6),
                "true"
            ),
            Exact(
                "operator-int-equal-null-right",
                "VO の == / != 演算子が値ベースで機能し、null も扱える（VO == null）",
                () =>
                {
                    CustomerIdValue? nil = null;

                    return CustomerIdValue.Create(5) == nil;
                },
                "false"
            ),
            Exact(
                "operator-int-equal-null-both",
                "VO の == / != 演算子が値ベースで機能し、null も扱える（null == null）",
                () =>
                {
                    CustomerIdValue? nil = null;

                    return nil == null;
                },
                "true"
            ),
            // ===== 属性・フックを持たない型（毎回新しいインスタンスだが等値） =====
            // 元の表明は Be（Equals(object) による等値）
            Exact(
                "create-without-declared-equal-value",
                "[宣言済み] 属性を持たない型の挙動は従来どおり（毎回新しいインスタンスだが等値）／[定義済み] フックを書かない VO は従来どおり",
                () => ((object)CustomerIdValue.Create(5)).Equals(CustomerIdValue.Create(5)),
                "true"
            ),
            // ===== IEquatable<TSelf>: 既定比較器が object フォールバックに落ちない =====
            // IEquatable<TSelf> が無いと EqualityComparer<T>.Default は ObjectEqualityComparer へ落ち、
            // 比較のたびに Equals(object?) 経由で引数が boxing される（Dictionary / HashSet のキー検索もこれを通る）
            Exact(
                "iequatable-int-implemented",
                "VO は IEquatable<TSelf> を実装し、EqualityComparer<T>.Default が object フォールバックに落ちない（CustomerIdValue の実装）",
                () => typeof(IEquatable<CustomerIdValue>).IsAssignableFrom(typeof(CustomerIdValue)),
                "true"
            ),
            Exact(
                "iequatable-string-implemented",
                "VO は IEquatable<TSelf> を実装し、EqualityComparer<T>.Default が object フォールバックに落ちない（NameValue の実装）",
                () => typeof(IEquatable<NameValue>).IsAssignableFrom(typeof(NameValue)),
                "true"
            ),
            Exact(
                "iequatable-decimal-implemented",
                "VO は IEquatable<TSelf> を実装し、EqualityComparer<T>.Default が object フォールバックに落ちない（AmountValue の実装）",
                () => typeof(IEquatable<AmountValue>).IsAssignableFrom(typeof(AmountValue)),
                "true"
            ),
            Exact(
                "equality-comparer-int-not-object-fallback",
                "VO は IEquatable<TSelf> を実装し、EqualityComparer<T>.Default が object フォールバックに落ちない（比較器の型）",
                () =>
                    EqualityComparer<CustomerIdValue>
                        .Default.GetType()
                        .Name.StartsWith("ObjectEqualityComparer", StringComparison.Ordinal),
                "false"
            ),
            Exact(
                "equality-comparer-int-same-value",
                "VO は IEquatable<TSelf> を実装し、EqualityComparer<T>.Default が object フォールバックに落ちない（比較器で同値）",
                () =>
                    EqualityComparer<CustomerIdValue>.Default.Equals(
                        CustomerIdValue.Create(5),
                        CustomerIdValue.Create(5)
                    ),
                "true"
            ),
            Exact(
                "equality-comparer-int-different-value",
                "VO は IEquatable<TSelf> を実装し、EqualityComparer<T>.Default が object フォールバックに落ちない（比較器で別の値）",
                () =>
                    EqualityComparer<CustomerIdValue>.Default.Equals(
                        CustomerIdValue.Create(5),
                        CustomerIdValue.Create(6)
                    ),
                "false"
            ),
            // ===== Dictionary / HashSet のキー（別インスタンスでも同値なら引ける） =====
            Exact(
                "dictionary-string-key-trygetvalue",
                "VO は Dictionary / HashSet のキーとして値ベースで一致する（別インスタンスの同値で TryGetValue）",
                () =>
                {
                    var map = new Dictionary<NameValue, int> { [NameValue.Create("alice")] = 1 };

                    return map.TryGetValue(NameValue.Create("alice"), out _);
                },
                "true"
            ),
            Exact(
                "dictionary-string-key-found-value",
                "VO は Dictionary / HashSet のキーとして値ベースで一致する（TryGetValue が引いた値）",
                () =>
                {
                    var map = new Dictionary<NameValue, int> { [NameValue.Create("alice")] = 1 };
                    map.TryGetValue(NameValue.Create("alice"), out var found);

                    return found;
                },
                "1"
            ),
            Exact(
                "dictionary-string-key-containskey-absent",
                "VO は Dictionary / HashSet のキーとして値ベースで一致する（無いキーの ContainsKey）",
                () =>
                {
                    var map = new Dictionary<NameValue, int> { [NameValue.Create("alice")] = 1 };

                    return map.ContainsKey(NameValue.Create("bob"));
                },
                "false"
            ),
            Exact(
                "hashset-int-key-count",
                "VO は Dictionary / HashSet のキーとして値ベースで一致する（HashSet は同値を 1 つにまとめる）",
                () =>
                    new HashSet<CustomerIdValue>
                    {
                        CustomerIdValue.Create(1),
                        CustomerIdValue.Create(1),
                        CustomerIdValue.Create(2),
                    }.Count,
                "2"
            ),
            // ===== 型付き Equals(TSelf) =====
            Exact(
                "equals-typed-string-same-value",
                "VO の型付き Equals(TSelf) は同値・null・異型を正しく判定する（同値）",
                () => NameValue.Create("x").Equals(NameValue.Create("x")),
                "true"
            ),
            Exact(
                "equals-typed-string-different-value",
                "VO の型付き Equals(TSelf) は同値・null・異型を正しく判定する（別の値）",
                () => NameValue.Create("x").Equals(NameValue.Create("y")),
                "false"
            ),
            // 同じ下位型（string）を内包する別 VO 型とは等しくない
            Exact(
                "equals-object-string-other-vo-type",
                "VO の型付き Equals(TSelf) は同値・null・異型を正しく判定する（同じ下位型の別 VO 型）",
                () => NameValue.Create("x").Equals((object)MemoValue.Create("x")),
                "false"
            ),
            // null 相手は毎回新しいレシーバーで書く（IEquatable<T> へ Equals(null) を渡すと C# の null 解析が
            // レシーバーを maybe-null へ落とし、以降の同一変数の使用が CS8602 になるため）
            Exact(
                "equals-typed-string-null",
                "VO の型付き Equals(TSelf) は同値・null・異型を正しく判定する（型付きの null）",
                () => NameValue.Create("x").Equals((NameValue?)null),
                "false"
            ),
            Exact(
                "equals-object-string-null",
                "VO の型付き Equals(TSelf) は同値・null・異型を正しく判定する（object の null）",
                () => NameValue.Create("x").Equals((object?)null),
                "false"
            ),
            // ===== バイナリ VO（RowVerValue）は同内容の別配列を等値とみなす =====
            Exact(
                "equals-binary-same-content",
                "バイナリ VO は同内容の別配列を等値とみなし、ハッシュも一致する（Equals）",
                () =>
                    RowVerValue
                        .Create([1, 2, 3, 4, 5, 6, 7, 8, 9])
                        .Equals(RowVerValue.Create([1, 2, 3, 4, 5, 6, 7, 8, 9])),
                "true"
            ),
            Exact(
                "hash-binary-same-content",
                "バイナリ VO は同内容の別配列を等値とみなし、ハッシュも一致する（GetHashCode）",
                () =>
                    RowVerValue.Create([1, 2, 3, 4, 5, 6, 7, 8, 9]).GetHashCode()
                    == RowVerValue.Create([1, 2, 3, 4, 5, 6, 7, 8, 9]).GetHashCode(),
                "true"
            ),
            Exact(
                "operator-binary-equal-same-content",
                "バイナリ VO は同内容の別配列を等値とみなし、ハッシュも一致する（==）",
                () =>
                    RowVerValue.Create([1, 2, 3, 4, 5, 6, 7, 8, 9])
                    == RowVerValue.Create([1, 2, 3, 4, 5, 6, 7, 8, 9]),
                "true"
            ),
            // Equals(object?) は型付きオーバーロードへ委譲する
            Exact(
                "equals-object-binary-same-content",
                "バイナリ VO は同内容の別配列を等値とみなし、ハッシュも一致する（Equals(object)）",
                () =>
                    RowVerValue
                        .Create([1, 2, 3, 4, 5, 6, 7, 8, 9])
                        .Equals((object)RowVerValue.Create([1, 2, 3, 4, 5, 6, 7, 8, 9])),
                "true"
            ),
            // 防御コピーも等値（配列は複製されるが値は同じ）
            Exact(
                "equals-binary-defensive-copy",
                "バイナリ VO は同内容の別配列を等値とみなし、ハッシュも一致する（防御コピー）",
                () =>
                {
                    var left = RowVerValue.Create([1, 2, 3, 4, 5, 6, 7, 8, 9]);

                    return left.CopyValue().Equals(left);
                },
                "true"
            ),
            // ===== バイナリ VO の非等値（長さ違い・異型・null） =====
            Exact(
                "equals-binary-different-length",
                "バイナリ VO は長さ違い・null 相手・異型を等しくないと判定する（長さ違い）",
                () => RowVerValue.Create([1, 2, 3]).Equals(RowVerValue.Create([1, 2, 3, 0])),
                "false"
            ),
            Exact(
                "equals-object-binary-other-type",
                "バイナリ VO は長さ違い・null 相手・異型を等しくないと判定する（異型）",
                () => RowVerValue.Create([1, 2, 3]).Equals((object)"1,2,3"),
                "false"
            ),
            Exact(
                "equals-typed-binary-null",
                "バイナリ VO は長さ違い・null 相手・異型を等しくないと判定する（型付きの null）",
                () => RowVerValue.Create([1, 2, 3]).Equals((RowVerValue?)null),
                "false"
            ),
            Exact(
                "equals-object-binary-null",
                "バイナリ VO は長さ違い・null 相手・異型を等しくないと判定する（object の null）",
                () => RowVerValue.Create([1, 2, 3]).Equals((object?)null),
                "false"
            ),
            // ===== バイナリ VO は先頭 1 バイトの差を等値で検出する（末尾一致に引きずられない） =====
            Exact(
                "equals-binary-leading-byte-differs",
                "バイナリ VO は先頭 1 バイトの差を等値で検出する（末尾一致に引きずられない）（Equals）",
                () => LeadingByteDiffers((left, right) => left.Equals(right)),
                "false"
            ),
            Exact(
                "operator-binary-equal-leading-byte-differs",
                "バイナリ VO は先頭 1 バイトの差を等値で検出する（末尾一致に引きずられない）（==）",
                () => LeadingByteDiffers((left, right) => left == right),
                "false"
            ),
            Exact(
                "operator-binary-not-equal-leading-byte-differs",
                "バイナリ VO は先頭 1 バイトの差を等値で検出する（末尾一致に引きずられない）（!=）",
                () => LeadingByteDiffers((left, right) => left != right),
                "true"
            ),
            // ===== バイナリ VO の IEquatable<TSelf> と Dictionary キー =====
            Exact(
                "iequatable-binary-implemented",
                "バイナリ VO は IEquatable<TSelf> 実装により Dictionary キーとして値ベースで一致する（IEquatable の実装）",
                () => typeof(IEquatable<RowVerValue>).IsAssignableFrom(typeof(RowVerValue)),
                "true"
            ),
            Exact(
                "equality-comparer-binary-not-object-fallback",
                "バイナリ VO は IEquatable<TSelf> 実装により Dictionary キーとして値ベースで一致する（比較器の型）",
                () =>
                    EqualityComparer<RowVerValue>
                        .Default.GetType()
                        .Name.StartsWith("ObjectEqualityComparer", StringComparison.Ordinal),
                "false"
            ),
            Exact(
                "dictionary-binary-key-trygetvalue",
                "バイナリ VO は IEquatable<TSelf> 実装により Dictionary キーとして値ベースで一致する（TryGetValue）",
                () =>
                {
                    var map = new Dictionary<RowVerValue, string>
                    {
                        [RowVerValue.Create([9, 9])] = "v",
                    };

                    return map.TryGetValue(RowVerValue.Create([9, 9]), out _);
                },
                "true"
            ),
            Exact(
                "dictionary-binary-key-found-value",
                "バイナリ VO は IEquatable<TSelf> 実装により Dictionary キーとして値ベースで一致する（引いた値）",
                () =>
                {
                    var map = new Dictionary<RowVerValue, string>
                    {
                        [RowVerValue.Create([9, 9])] = "v",
                    };
                    map.TryGetValue(RowVerValue.Create([9, 9]), out var found);

                    return found;
                },
                "\"v\""
            ),
            Exact(
                "dictionary-binary-key-containskey-absent",
                "バイナリ VO は IEquatable<TSelf> 実装により Dictionary キーとして値ベースで一致する（無いキーの ContainsKey）",
                () =>
                {
                    var map = new Dictionary<RowVerValue, string>
                    {
                        [RowVerValue.Create([9, 9])] = "v",
                    };

                    return map.ContainsKey(RowVerValue.Create([9, 8]));
                },
                "false"
            ),
        ];

    /// <summary>先頭 1 バイトだけ異なる 1024 バイトの 2 つのバイナリ VO を比べる</summary>
    private static bool LeadingByteDiffers(Func<RowVerValue, RowVerValue, bool> compare)
    {
        var a = new byte[1024];
        var b = new byte[1024];
        b[0] = 1;

        return compare(RowVerValue.Create(a), RowVerValue.Create(b));
    }
}
