using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 値の束縛の生成の表（<c>TranslatorParityBindingRows</c>）が作らない形の宣言
/// （QuickER.Tests にもリンクで入るので、Integration の型を cref でも参照しない）
/// </summary>
/// <remarks>純粋なデータ（Engines の型と BCL だけを参照する）。</remarks>
internal static class TranslatorParityBindingRowsNotGenerated
{
    /// <summary>手書きの述語の表の重なる行を生成の表へ移し、値の池を正本にする区切り（GeneratedRuntime の CLAUDE.md の綴り）</summary>
    private const string ValuePoolSlice = "値の池の正本";

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
                NotGenerated.Pending(
                    "sign-only-cast-over-negative-column",
                    "32 ビット以下で負の値を持つ列 × 符号だけの変換（int → uint・short → ushort・sbyte → byte と、ulong への変換）",
                    ValuePoolSlice,
                    "列の側の値を変える明示の変換の水準は縮める変換（int → byte・bigint → short・decimal → int）だけで、列の値の池が負の値を持たないので、符号だけの変換が C# の折り返しで答えるか（区切り a のフェーズ 3 の XT4 の直し）は表の水準に出ない。値の池の正本の区切りで、負の値を持つ列の池と符号だけの変換の水準を足す。それまでは手書きの述語の表の行が持つ",
                    "`table:TranslatorParityPredicates`（行 `int-column-sign-cast-equals-uint-beyond-int`・`int-column-sign-cast-greater-uint-max-int`・`in-uint-list-sign-cast-column`・`in-uint-array-sign-cast-column`・`bigint-column-ulong-cast-greater`・`int-column-ulong-cast-greater`）・`table:TranslatorConversions`（行 `column-sign-only-cast`・`column-int-to-ulong-refused`）"
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
            ]
        );
}
