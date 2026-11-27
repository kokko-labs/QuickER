using System.Collections.Generic;
using System.Linq;
using QuickER.Tests.TestSupport.ValuePools;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 実装先どうしの一致の値の幅（<c>GeneratedRuntime/ParityValues</c>）が作る入力の値の観測：網羅の行と普段の既定の種のシナリオの手が書く値と列の相
/// </summary>
/// <remarks>
/// 読む手と値は値の表の数え方（<see cref="BackendParityValueDimensions"/> の <c>Values</c>）と同じ（注文の日時・金額・メモ・除外 blob、明細の品名・
/// 割引、顧客の備考、連絡先のラベル、blob の書き込み）。列の相は <c>BackendParityFixture</c> の図の宣言の型。種は普段の既定の数
/// （<see cref="BackendParityRuntimeTests.Seeds"/>）で、生成器を同じ種で呼んで読むだけ（乱数を新しく引かない）。
/// </remarks>
internal static class BackendParityValueObservations
{
    private static readonly ValueColumn OrderedAt = ValueColumn.FromSqlType("datetime2");

    private static readonly ValueColumn Amount = ValueColumn.FromSqlType("decimal(10,2)");

    private static readonly ValueColumn Discount = ValueColumn.FromSqlType("decimal(5,2)");

    private static readonly ValueColumn Memo = ValueColumn.FromSqlType("nvarchar(100)");

    private static readonly ValueColumn Item = ValueColumn.FromSqlType("nvarchar(50)");

    private static readonly ValueColumn Blob = ValueColumn.FromSqlType("varbinary(max)");

    /// <summary>網羅の行と普段の既定の種のシナリオの手が書く値</summary>
    internal static IReadOnlyList<ValueObservation> Observe()
    {
        var scenarios = BackendParityCoverageRows
            .Table.Select(row => row.Input.Create())
            .Concat(
                Enumerable
                    .Range(
                        BackendParityRuntimeTests.Seeds.FirstSeed,
                        BackendParityRuntimeTests.Seeds.RegularCount
                    )
                    .Select(BackendParityGenerator.Create)
            );

        return
        [
            .. scenarios.SelectMany(scenario =>
                scenario.Ops.SelectMany(op => Values(op, scenario.Name))
            ),
        ];
    }

    private static IEnumerable<ValueObservation> Values(ParityOp op, string where)
    {
        switch (op)
        {
            case DeviatingOp deviating:
                foreach (var value in Values(deviating.Inner, where))
                {
                    yield return value;
                }

                break;

            case InsertOp { Row: OrderRow order }:
                yield return new(order.At, OrderedAt, $"{where} の注文の日時");
                yield return new(order.Amount, Amount, $"{where} の注文の金額");
                yield return new(order.Memo, Memo, $"{where} の注文のメモ");
                yield return new(order.Payload, Blob, $"{where} の注文の payload");
                yield return new(order.Thumb, Blob, $"{where} の注文の thumb");
                break;

            case InsertOp { Row: LineRow line }:
                yield return new(line.Item, Item, $"{where} の明細の品名");
                yield return new(line.Discount, Discount, $"{where} の明細の割引");
                break;

            case InsertOp { Row: CustomerRow customer }:
                yield return new(customer.Note, Memo, $"{where} の顧客の備考");
                break;

            case EditMemoOp edit:
                yield return new(edit.Memo, Memo, $"{where} のメモの編集");
                break;

            case OutsideEditMemoOp edit:
                yield return new(edit.Memo, Memo, $"{where} の他者のメモの編集");
                break;

            case EditCustomerOp { Note: { } note }:
                yield return new(note, Memo, $"{where} の顧客の備考の編集");
                break;

            case EditContactOp { Target: null, Label: { } label }:
                yield return new(label, Item, $"{where} の連絡先のラベルの編集");
                break;

            case WriteBlobOp write:
                yield return new(write.Data, Blob, $"{where} の blob の書き込み");
                break;
        }
    }
}
