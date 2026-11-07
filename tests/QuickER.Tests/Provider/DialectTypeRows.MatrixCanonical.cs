using QuickER.Dialects;
using QuickER.Provider;
using static QuickER.Tests.Provider.DialectTypeRow;

namespace QuickER.Tests.Provider;

/// <summary>
/// 方言の型の表：代表値（正規型・型トークン）を入力にする観点（パラメータの型の書き出し・パラメータの型解決・型トークン）と、
/// 宣言のセルの表記を移行元にする方言切替の行
/// </summary>
/// <remarks>行は 1 行を 5 方言で流す（型トークンは方言なし）。期待はセルの宣言から導く。</remarks>
internal static partial class DialectTypeRows
{
    private static IEnumerable<DialectTypeRow> MatrixCanonicalRows()
    {
        var natives = MatrixNatives();

        foreach (var kind in Enum.GetValues<CanonicalTypeKind>())
        {
            var sample = RepresentativeSample(kind);
            var name = kind.ToString().ToLowerInvariant();
            var token = GrammarToken(sample);

            // 代表値は全方言の上限の内側なので、パラメータの型の書き出しは書き出し（TryFormat）と同じ表記になる
            var formatParam = FormatParam(
                $"matrix-format-param-{name}",
                $"代表値のパラメータの型の書き出しは、宣言のセルの表記と同じ（{kind}）",
                sample
            );

            // パラメータの型解決は、書き出した表記をマッパーへ通した結果（書き出せない方言では解決しない）
            var paramType = ParamType(
                $"matrix-param-type-{name}",
                $"代表値のトークンのパラメータの型解決は、宣言のセルの表記をマッパーへ通した結果と同じ（{kind}）",
                new ParamTypeInput([token], token)
            );

            foreach (var dialect in DialectNames.All)
            {
                var cell = CellOf(dialect, kind);
                formatParam.Expect(dialect, cell.Native is null ? "fail" : $"ok {cell.Native}");
                paramType.Expect(
                    dialect,
                    cell.Native is null
                        ? "absent"
                        : MapSpec(
                            dialect,
                            cell.Native,
                            NativeOf(natives, dialect, cell.Native).Parsed
                        )
                );
            }

            yield return formatParam;
            yield return paramType;

            yield return TokenParse(
                    $"matrix-token-parse-{name}",
                    $"文法の写しで書いた代表値のトークンは、代表値の正規型として解析される（{kind}）",
                    token
                )
                .Expect($"ok {CanonicalSpec(sample)}");
        }

        // 長さを要する種類の長さ無しは、引数を書かない種類名だけのトークンで往復する（CanonicalTypeToken.HasLengthArgument）
        foreach (
            var kind in Enum.GetValues<CanonicalTypeKind>()
                .Where(CanonicalTypeToken.HasLengthArgument)
        )
        {
            var name = kind.ToString().ToLowerInvariant();

            yield return TokenFormat(
                    $"matrix-token-format-lengthless-{name}",
                    "長さを要する種類の長さ無しは、種類名だけのトークンで往復する",
                    Ct(kind)
                )
                .Boundary(DialectTypeBoundaries.Lengthless)
                .Expect($"ok text={name} roundtrip=1");
        }

        foreach (var row in MatrixConvertRows(natives))
        {
            yield return row;
        }
    }

    /// <summary>（方言, 表記）の宣言を引く</summary>
    private static MatrixNative NativeOf(
        IReadOnlyList<MatrixNative> natives,
        string dialect,
        string native
    ) => natives.Single(item => item.Dialect == dialect && item.Native == native);

    /// <summary>
    /// 方言切替：移行元のセルの表記は、移行先では解析した種類のセルの表記へ変換される（移行先のセルが書き出せなければ変換できない列・
    /// 同じ方言は変換しない）
    /// </summary>
    /// <remarks>
    /// 移行元は、表記を解析した正規型が代表値と同じ形の表記だけ（形が違う表記〔<c>nvarchar(max)</c>・<c>bytea</c>・
    /// <c>decimal(19,4)</c> など〕は、移行先のセルの表記が代表値の書き出しなので期待を導けない）。
    /// </remarks>
    private static IEnumerable<DialectTypeRow> MatrixConvertRows(
        IReadOnlyList<MatrixNative> natives
    )
    {
        foreach (
            var native in natives.Where(native =>
                native.Parsed == RepresentativeSample(native.Parsed.Kind)
            )
        )
        {
            var row = Convert(
                $"matrix-convert-from-{native.Dialect}-{MatrixSlug(native.Native)}",
                $"移行元の宣言のセルの表記は、移行先の同じ種類のセルの表記へ変換される（{native.Parsed.Kind}）",
                new ConvertInput(native.Dialect, native.Native)
            );

            foreach (var to in DialectNames.All)
            {
                var target = CellOf(to, native.Parsed.Kind);
                row.Expect(
                    to,
                    to == native.Dialect ? "none"
                        : target.Native is null ? $"unconverted old=\"{native.Native}\""
                        : $"converted type=\"{target.Native}\" nullable=0 capped=0"
                );
            }

            yield return row;
        }
    }
}
