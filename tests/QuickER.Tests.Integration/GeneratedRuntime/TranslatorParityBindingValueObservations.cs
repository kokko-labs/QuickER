using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Tests.TestSupport.ValuePools;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 値の束縛の生成の表（<c>GeneratedRuntime/TranslatorParityBindings</c>）が作る入力の値の観測：全行（網羅の配列と必ず含む行）の述語の比べる値と列の相、
/// 符号だけの変換の行が変換するシードの行の値
/// </summary>
/// <remarks>
/// 行の述語（<see cref="TranslatorParityBinding.Predicate"/>）を <see cref="ValueExpressionObserver"/> で歩く。列の相は述語の表と同じく図の列の型から
/// 引き、列の側の値を変える明示の変換（縮める変換の行）は比べる値の相の変換先、値の側の変換は変換の元の値の相の変換先にする。日付部品の行の
/// 相は <c>date</c>。シードの行の値は、ほかの行では観測しない（この表が水準で選ぶのは比べる値だけ。シードは述語の表の観測が持つ）。
/// 符号だけの変換の行だけは、変換される元の値（負の値を含む列のシードの行の値）が水準 <c>cast-sign-only</c> を作るので、列のシードの値を
/// 変換先（<see cref="ValueColumn.CastTarget"/>）つきで観測する（表と変換先の組ごとに 1 回）。
/// </remarks>
internal static class TranslatorParityBindingValueObservations
{
    /// <summary>全行の比べる値と、符号だけの変換の行が変換するシードの行の値</summary>
    internal static IReadOnlyList<ValueObservation> Observe()
    {
        var observations = new List<ValueObservation>();
        var seeded = new HashSet<(string Column, Type Target)>();

        foreach (var row in TranslatorParityBindingRows.Rows)
        {
            observations.AddRange(
                ValueExpressionObserver.Observe(
                    row.Input.Predicate,
                    TranslatorParityPredicateValueObservations.ColumnOf,
                    $"行 {row.Id}"
                )
            );

            if (
                TranslatorParityBindingRows.SignCastTypeOf(row.Input) is { } target
                && seeded.Add((row.Input.ColumnType, target))
            )
            {
                var cast = new ValueColumn { CastTarget = target };
                var index = 0;

                foreach (var value in TranslatorParityBindingRows.SeedColumnValues(row.Input))
                {
                    index++;
                    observations.Add(
                        new ValueObservation(
                            value,
                            cast,
                            $"行 {row.Id} の列 {row.Input.ColumnType} のシードの {index} 行目（{target.Name} へ変換する値）"
                        )
                    );
                }
            }
        }

        return observations;
    }
}
