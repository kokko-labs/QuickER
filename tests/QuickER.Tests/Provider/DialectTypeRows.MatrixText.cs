using static QuickER.Tests.Provider.DialectTypeRow;

namespace QuickER.Tests.Provider;

/// <summary>
/// 方言の型の表：宣言のセルの表記を入力にする観点（解析・表記の往復・正規化・同じ型の判定・取込の表記・マッパー）の行
/// </summary>
/// <remarks>行は（方言, 表記）ごと。期待はセルの読み戻しのトークン・再書き出しの表記・取込の表記の宣言から導く。</remarks>
internal static partial class DialectTypeRows
{
    private static IEnumerable<DialectTypeRow> MatrixTextRows()
    {
        var natives = MatrixNatives();

        foreach (var native in natives)
        {
            var dialect = native.Dialect;
            var text = native.Native;
            var slug = $"{dialect}-{MatrixSlug(text)}";
            var cells = string.Join("・", native.Cells);

            yield return Parse(
                    $"matrix-parse-{slug}",
                    $"宣言のセルの表記は読み戻しのトークンの正規型として解析される（{cells}）",
                    text
                )
                .Expect(dialect, $"ok {CanonicalSpec(native.Parsed)}");

            yield return RoundTripText(
                    $"matrix-roundtrip-text-{slug}",
                    $"宣言のセルの表記は解析して書き出し直すと元の表記（再書き出しの宣言があればその表記）に戻る（{cells}）",
                    text
                )
                .Expect(dialect, $"ok {native.Rewritten}");

            yield return Normalize(
                    $"matrix-normalize-{slug}",
                    $"宣言のセルの表記の取込の表記は宣言のとおりで、種類を保つ（{cells}）",
                    text
                )
                .Expect(dialect, $"ok text=\"{native.Imported}\" kind={native.Parsed.Kind}");

            yield return ImportSpelling(
                    $"matrix-import-spelling-{slug}",
                    $"宣言のセルの表記で宣言した列の DB のカタログの値から、取込は取込の表記を組む（不動点。{cells}）",
                    DialectTypeAxis.Get(dialect).CatalogValueOf(text)
                )
                .Expect(dialect, $"ok text=\"{native.Imported}\" fixed=1");

            yield return Map(
                    $"matrix-map-{slug}",
                    $"宣言のセルの表記は、解析した種類から決まる C# の型へマップされる（{cells}）",
                    text
                )
                .Expect(dialect, MapSpec(dialect, text, native.Parsed));

            foreach (var row in MatrixSameTypeRows(native, natives))
            {
                yield return row;
            }
        }
    }

    /// <summary>
    /// 同じ型の判定：自分自身・大文字小文字だけ違う表記・取込の表記は同じ型で、同じ方言の次の表記とは取込の表記が
    /// 大文字小文字を無視して等しいときだけ同じ型
    /// </summary>
    private static IEnumerable<DialectTypeRow> MatrixSameTypeRows(
        MatrixNative native,
        IReadOnlyList<MatrixNative> natives
    )
    {
        var dialect = native.Dialect;
        var text = native.Native;
        var slug = $"{dialect}-{MatrixSlug(text)}";
        var flipped = text.Any(char.IsLower) ? text.ToUpperInvariant() : text.ToLowerInvariant();

        yield return SameType(
                $"matrix-same-type-{slug}-itself",
                "宣言のセルの表記は自分自身と同じ型",
                text,
                text
            )
            .Expect(dialect, "same");

        yield return SameType(
                $"matrix-same-type-{slug}-case",
                "宣言のセルの表記は大文字小文字だけ違う表記と同じ型",
                text,
                flipped
            )
            .Expect(dialect, "same");

        if (native.Imported != text)
        {
            yield return SameType(
                    $"matrix-same-type-{slug}-imported",
                    "宣言のセルの表記は取込の表記と同じ型（docs/database.md の same-type-spellings の表）",
                    text,
                    native.Imported
                )
                .Expect(dialect, "same");
        }

        var sameDialect = natives.Where(other => other.Dialect == dialect).ToList();
        var next = sameDialect[(sameDialect.IndexOf(native) + 1) % sameDialect.Count];

        yield return SameType(
                $"matrix-same-type-{slug}-next",
                "宣言のセルの表記どうしは、取込の表記が大文字小文字を無視して等しいときだけ同じ型",
                text,
                next.Native
            )
            .Expect(
                dialect,
                string.Equals(native.Imported, next.Imported, StringComparison.OrdinalIgnoreCase)
                    ? "same"
                    : "different"
            );
    }
}
