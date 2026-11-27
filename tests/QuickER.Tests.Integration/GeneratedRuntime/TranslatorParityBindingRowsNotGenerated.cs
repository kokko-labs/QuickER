using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 値の束縛の生成の表（<c>TranslatorParityBindingRows</c>）が作らない形の宣言
/// （QuickER.Tests にもリンクで入るので、Integration の型を cref でも参照しない）
/// </summary>
/// <remarks>純粋なデータ（Engines の型と BCL だけを参照する）。</remarks>
internal static class TranslatorParityBindingRowsNotGenerated
{
    /// <summary>宣言</summary>
    public static NotGeneratedDeclaration Declaration { get; } =
        new(
            "TranslatorParityBindingRows",
            [ProtectionNames.EnginePart(EngineIds.GeneratedRuntime, "TranslatorParityBindings")],
            [
                NotGenerated.Accepted(
                    "quicker-execute-update",
                    "QuickER 版とインメモリの ExecuteUpdate の経路",
                    "QuickER 版とインメモリの Repository は ExecuteUpdate を持たない（docs/code-generation.md の EF Core モードの節：The generated repositories do not expose ExecuteUpdate）。ExecuteUpdate の経路は EF Core 版の QuickErDbContext にだけあり、EF Core 版の派生だけが流す",
                    NotGenerated.Unguarded
                ),
                NotGenerated.Accepted(
                    "lambda-aggregate-over-captured-collection",
                    "ラムダを含む値の式の単一の値を、捕捉したコレクションの集計（Max(p => p) など）で作る形",
                    "EF Core 版は捕捉したコレクションの集計を、パラメータのコレクションへの副問い合わせとしてサーバーで翻訳し、SQLite のプロバイダが NullReferenceException で断る。欠陥は EF Core 10.0.13 の SQLite プロバイダの SqliteSqlTranslatingExpressionVisitor.GenerateGreatest が呼ぶ ExpressionExtensions.InferTypeMapping にある（EF Core の issue は見つけていない。再現する述語は、捕捉した List<decimal> prices に対する x => x.BigParentId == prices.Max(p => p)）。値の束縛の分岐＝行に依らない値の判定に届かないので、単一の値は Select(p => p).First() で作る（同じく副問い合わせに翻訳される、ラムダの引数を持つ値の式）",
                    NotGenerated.Unguarded
                ),
                NotGenerated.Accepted(
                    "date-part-other-than-date",
                    "日付部品のうち .Date 以外（Year・Month など整数を返す部品）と、オフセットつき日時の列の日付部品",
                    "値の束縛の分岐（列名の分からない式と比べる値の束縛）は .Date と同じ腕を通り、整数を返す部品の値は整数で束縛されて丸めの端を持たない。部品ごとの翻訳と時差の扱いは手書きの述語の Fact が持つ",
                    "`TranslatorParityRuntimeTestsBase.NullableValueAccess_MatchesCSharp`・`TranslatorParityRuntimeTestsBase.DateTimeOffsetParts_MatchCSharp`"
                ),
                NotGenerated.Accepted(
                    "sign-only-cast-with-decimal-beyond-int",
                    "列の側の符号だけの変換（int の列の (uint)）× int の範囲の外の decimal の値（符号の境目 2147483648 など、折り返した負の値の行だけに当たる値）",
                    "符号だけの変換と組む値のプール（符号の境目・符号なしの型の最大）は、変換先の型の値（uint・ulong）で作る（SignCastValueOf）。decimal の値のプールは int の列の値の範囲（1 の周り）で作るので、int の範囲の外の decimal の値を符号だけの変換と組めない。水準を 1 つ足すと網羅の配列が組み直るので足さず、P1（SQLite の QuickER 版で、decimal へ広げた整数の列の元の型の範囲を uint にする腕）は述語の表の行が受け持つ",
                    "`table:TranslatorParityPredicates`（行 `int-key-sign-cast-equals-decimal-sign-boundary`）"
                ),
                NotGenerated.Pending(
                    "column-types-outside-binding-table",
                    "TimeSpan（time）・DateTimeOffset（datetimeoffset）・varchar・binary / varbinary の列の型（述語の表の time-out-of-day・offset・varchar・binary の行が手書きで守る）",
                    "値の束縛の列の型の拡張（未起動）",
                    "列の型の水準を 4 つ足すと、3 因子の組（列の型 × 値の形 × 値のプールほか）で網羅の配列の行が倍近くになり、8 つの実装先で流す時間が増える。足すのは 8 実装先の時間を測ってから。それまでは手書きの述語の表の行が受け持つ",
                    "`table:TranslatorParityPredicates`（行 `time-out-of-day-equals`・`offset-equals-same-instant-other-offset`・`varchar-equals-non-ascii`・`binary-equals-trailing-zero-value`）"
                ),
                NotGenerated.Pending(
                    "datetime2-finer-than-column-scale",
                    "datetime2 の列の小数秒の桁より細かい値（datetime2(2) の列 at2 と 1/100 秒より細かい値）",
                    "値の束縛の列の型の拡張（未起動）",
                    "生成の表の datetime2 の列 at は datetime2（7 桁＝tick と同じ桁）で、DateTime にそれより細かい値は無い。図の datetime2(2) の列 at2 を列の型の水準に足せば作れるが、列の型を足すと網羅の配列の行が増えるので、列の型の拡張の区切りで時間を測ってから足す。それまでは手書きの述語の表の行が受け持つ",
                    "`table:TranslatorParityPredicates`（行 `datetime2-scaled-equals-finer`・`datetime2-scaled-less-finer`）"
                ),
                NotGenerated.Pending(
                    "sign-only-cast-over-narrow-integer-column",
                    "16 ビット以下の符号つきの整数の列 × 符号だけの変換（short → ushort・sbyte → byte）",
                    "値の束縛の列の型の拡張（未起動）",
                    "述語の表の図に smallint・sbyte の列が無い（図を変えないと作れない）。符号だけの変換の水準は int の列の (uint) と int・bigint の列の (ulong) で作り、CASE の折り返しの 2^n（SignWrapModulus）の 2^16・2^8 の腕は翻訳器の変換の表が SQL の文で受け持つ",
                    "`table:TranslatorConversions`（行 `column-sign-cast-short-to-ushort`・`column-sign-cast-sbyte-to-byte`）"
                ),
            ]
        );
}
