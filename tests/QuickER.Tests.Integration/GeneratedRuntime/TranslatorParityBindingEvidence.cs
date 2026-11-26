using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 値の束縛の生成の表（<see cref="TranslatorParityBindingRows"/>）に結んだ分岐の軸の台帳の項目の、分岐に入った証拠の判定（名前で引く）と、
/// 述語の表の派生が流した指摘のセルの行の結果への照合
/// </summary>
/// <remarks>
/// 台帳（<see cref="BranchAxisLedgerEntries"/>）は純粋なデータで、証拠を名前でしか持たない。判定の関数はここに置き、述語の表の基底 2 つの
/// <c>LedgerCells_FollowStatus</c> が派生の実装先で流した指摘のセルの行の結果（<see cref="BindingRunOutcome"/>）へ掛ける。照合の規則は
/// 形の行の <c>SyncPlanBranchEvidence.NetProblems</c> と同じ。
/// </remarks>
internal static class TranslatorParityBindingEvidence
{
    /// <summary>値の束縛の生成の表の台帳の部分の名前</summary>
    public static string TablePart => TranslatorParityBindingRows.TablePart;

    /// <summary>
    /// 証拠の名前 → 判定（行と、その行を流した結果から、直した分岐に入ったか）
    /// </summary>
    public static IReadOnlyDictionary<
        string,
        Func<TranslatorParityBinding, BindingRunOutcome, bool>
    > ByName { get; } =
        new Dictionary<string, Func<TranslatorParityBinding, BindingRunOutcome, bool>>(
            StringComparer.Ordinal
        )
        {
            // SQL Server の QuickER 版で、日付部品と比べる値のパラメータが datetime2 で束縛され、datetime では束縛されない（項目 xb1）
            ["date-part-value-bound-as-datetime2"] = (_, outcome) =>
                outcome.Matched
                && outcome.Parameters.Any(parameter => parameter.ProviderType == "DateTime2")
                && !outcome.Parameters.Any(parameter => parameter.ProviderType == "DateTime"),

            // 値を変える明示の変換を C# と同じに扱う（項目 xt4）：値の側は C# と同じ答えで、パラメータを捕まえる実装先では変換した値で
            // 束縛する。列の側は、変換を名指しした NotSupportedException で断る
            ["narrowing-cast-honoured"] = (row, outcome) =>
                TranslatorParityBindingRows.ColumnCastTypeOf(row) is { } columnCast
                    ? outcome.ExceptionType == typeof(NotSupportedException).FullName
                        && NamesType(outcome.ExceptionMessage, columnCast)
                    : outcome.Matched
                        && (
                            outcome.Parameters.Count == 0
                            || TranslatorParityBindingRows
                                .CastValuesOf(row)
                                .All(value =>
                                    outcome.Parameters.Any(parameter =>
                                        parameter.Value
                                        == Convert.ToString(value, CultureInfo.InvariantCulture)
                                    )
                                )
                        ),

            // PostgreSQL の EF Core 版で、date の列と比べる値のパラメータが、Kind が Utc でない timestamp で送られる（項目 ha1）
            ["utc-date-parameter-sent-without-kind"] = (_, outcome) =>
                outcome.Matched
                && outcome.Parameters.Any(parameter =>
                    parameter.ProviderType == nameof(NpgsqlTypes.NpgsqlDbType.Timestamp)
                    && !parameter.Value.EndsWith("(Utc)", StringComparison.Ordinal)
                ),

            // PostgreSQL の EF Core 版で、date の列と比べるリストの配列のパラメータが date[] で送られない（項目 ha2）
            ["date-list-sent-as-timestamp-array"] = (_, outcome) =>
                outcome.Matched
                && outcome.Parameters.Any(parameter =>
                    parameter.Value.StartsWith('[') && parameter.ProviderType != DateArrayType
                ),

            // SQLite の EF Core 版で、ラムダを含む値の式との整数と decimal の比べ方が書き換えられ、文字列の比較にならない（項目 ha4）
            ["row-free-lambda-rewritten"] = (_, outcome) =>
                outcome.Matched
                && outcome.CommandTexts.Count > 0
                && !outcome.CommandTexts.Any(text =>
                    text.Contains("AS TEXT)", StringComparison.OrdinalIgnoreCase)
                ),
        };

    /// <summary>Npgsql の date[] のパラメータの型の書き方（<see cref="CapturedParameter.ProviderType"/>）</summary>
    private static readonly string DateArrayType = (
        NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Date
    ).ToString();

    /// <summary>例外の文が変換先の型を名指すか（CLR の名前か C# の別名）</summary>
    private static bool NamesType(string? message, Type type) =>
        message is not null
        && (
            message.Contains(type.Name, StringComparison.Ordinal)
            || message.Contains(
                type == typeof(byte) ? "(byte)"
                    : type == typeof(short) ? "(short)"
                    : "(int)",
                StringComparison.Ordinal
            )
        );

    /// <summary>実装先が流す指摘のセルの行（この表に結んだ結び済みの項目のうち、範囲にその実装先を持つもの）の Id</summary>
    /// <param name="target">実装先</param>
    /// <param name="items">台帳の項目（既定は <see cref="BranchAxisLedgerEntries.Items"/>）</param>
    public static IReadOnlyList<string> CellRowIdsFor(
        RuntimeTarget target,
        IReadOnlyList<BranchAxisItem>? items = null
    ) =>
        [
            .. BoundItems(target, items)
                .Select(item => TranslatorParityBindingRows.RowIdOfRequired(item.Binding!.CellRow))
                .OfType<string>()
                .Where(id =>
                    TranslatorParityBindingRows.RunsOn(
                        target,
                        TranslatorParityBindingRows.Rows[id].Input.Levels
                    )
                )
                .Distinct(StringComparer.Ordinal),
        ];

    /// <summary>
    /// 実装先が流した指摘のセルの行の結果へ、この表に結んだ台帳の項目を照合する。指摘のセルの行はその実装先で流れなければならず、
    /// 修正済みなら証拠が出る。結び済み・未修正なら、その行は C# と割れ、割れはその項目の Id を指摘の名前に持つ既知の失敗の一覧の
    /// Pending（直す区切りが項目の直す区切りと同じ）に当たる。C# と一致したら赤（直ったのに状態を切り替えていない）
    /// </summary>
    /// <param name="target">実装先</param>
    /// <param name="outcomes">流した行の結果（行の Id → 結果）</param>
    /// <param name="items">台帳の項目（既定は <see cref="BranchAxisLedgerEntries.Items"/>）</param>
    /// <param name="known">既知の失敗の一覧の項目（既定は <see cref="TranslatorParityBindingKnownFailures.EntriesFor"/>）</param>
    public static IEnumerable<string> CellProblems(
        RuntimeTarget target,
        IReadOnlyDictionary<string, BindingRunOutcome> outcomes,
        IReadOnlyList<BranchAxisItem>? items = null,
        IReadOnlyList<KnownEntry>? known = null
    )
    {
        var entries = known ?? TranslatorParityBindingKnownFailures.EntriesFor(target);
        var scope = TranslatorParityBindingKnownFailures.ScopeOf(target);

        foreach (var item in BoundItems(target, items))
        {
            var binding = item.Binding!;
            var rowId = TranslatorParityBindingRows.RowIdOfRequired(binding.CellRow);

            if (
                rowId is null
                || !TranslatorParityBindingRows.RunsOn(
                    target,
                    TranslatorParityBindingRows.Rows[rowId].Input.Levels
                )
            )
            {
                yield return $"ledger {item.Id}: the finding's cell {binding.CellRow} is not a required row that {target} runs";
                continue;
            }

            if (!outcomes.TryGetValue(rowId, out var outcome))
            {
                yield return $"ledger {item.Id}: the finding's cell {rowId} did not run on {target}";
                continue;
            }

            if (item.Status == BranchAxisStatus.BoundUnfixed)
            {
                if (outcome.Matched)
                {
                    yield return $"ledger {item.Id}: the finding's cell {rowId} ran on {target} without violations while the item is bound but unfixed (switch the status to fixed)";
                    continue;
                }

                if (
                    !entries.Any(entry =>
                        entry.Kind == KnownKind.Pending
                        && string.Equals(entry.Finding, item.Id, StringComparison.Ordinal)
                        && string.Equals(entry.FixedIn, item.FixedIn, StringComparison.Ordinal)
                        && string.Equals(entry.Scope, scope, StringComparison.Ordinal)
                        && string.Equals(entry.Signature, rowId, StringComparison.Ordinal)
                    )
                )
                {
                    yield return $"ledger {item.Id}: the finding's cell {rowId} on {target} has violations that no pending known failure named {item.Id} (fixed in {item.FixedIn}) receives: {outcome.Describe().ReplaceLineEndings(" ")}";
                }

                continue;
            }

            if (!ByName.TryGetValue(binding.Evidence, out var evidence))
            {
                yield return $"ledger {item.Id}: unknown evidence {binding.Evidence}";
                continue;
            }

            if (!evidence(TranslatorParityBindingRows.Rows[rowId].Input, outcome))
            {
                yield return $"ledger {item.Id}: the finding's cell {rowId} ran on {target} without the evidence {binding.Evidence} (the fixed branch was not entered)";
            }
        }
    }

    /// <summary>この表に結んだ結び済みの項目のうち、指摘のセルの範囲に実装先を持つもの</summary>
    private static IEnumerable<BranchAxisItem> BoundItems(
        RuntimeTarget target,
        IReadOnlyList<BranchAxisItem>? items
    ) =>
        (items ?? BranchAxisLedgerEntries.Items).Where(item =>
            item.Status is BranchAxisStatus.BoundUnfixed or BranchAxisStatus.BoundFixed
            && item.Binding is { } binding
            && string.Equals(binding.TablePart, TablePart, StringComparison.Ordinal)
            && binding.CellTargets.Contains(target.ToString(), StringComparer.Ordinal)
        );
}
