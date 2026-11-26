using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace QuickER.Tests.TestSupport.Engines;

/// <summary>新しい行の札の種別</summary>
public enum RowTagKind
{
    /// <summary>種別が無い（札として断る）</summary>
    Unspecified = 0,

    /// <summary>分岐の軸の台帳の項目（<see cref="RowTag.Text"/> は項目の Id）</summary>
    Ledger,

    /// <summary>行・水準の改名（同じ中身の Id を付け替えた）</summary>
    Rename,

    /// <summary>新機能の行（直し残しの受け入れでなく、新しく足した機能を確かめる行）</summary>
    NewFeature,

    /// <summary>既存の水準の対照（すでに表にある水準の、答えの変わらない側を並べる行）</summary>
    ExistingLevelControl,

    /// <summary>文書の特性の固定（文書に定めが無い今の振る舞いを写す行。TestSupport/CLAUDE.md「次元表と網羅」の運用 4）</summary>
    DocumentedCharacteristic,

    /// <summary>
    /// 低の指摘の受け入れ（分岐の軸の台帳に載せない低の指摘を直した行。理由は「指摘の番号（記録）：軸を水準で交差させない理由」の形で書く。
    /// 例「PB1（review-measure3 v4）：Oracle だけの暗黙の削除の数え方。軸は既存の名前の重なりの行が受ける」）
    /// </summary>
    ReviewFinding,
}

/// <summary>外す方言・実装先と理由</summary>
/// <param name="Target">方言・実装先の名前</param>
/// <param name="Reason">外す理由（台帳の水準が出ない方言では、理由に続けて根拠＝相対パス「見出し」か宣言のメンバー名を書く）</param>
public sealed record TargetExclusion(string Target, string Reason);

/// <summary>
/// 新しい行・水準の札：分岐の軸の台帳の項目の Id か、種別つきの理由
/// </summary>
/// <param name="Kind">種別</param>
/// <param name="Text">台帳の項目の Id（<see cref="RowTagKind.Ledger"/>）か理由（ほかの種別）</param>
public sealed record RowTag(RowTagKind Kind, string Text)
{
    /// <summary>行を流さない方言・実装先と理由（同期の期待の行の「新しい行は全実方言」の規則が読む）</summary>
    public IReadOnlyList<TargetExclusion> Excluded { get; init; } = [];

    /// <summary>台帳の項目の札</summary>
    /// <param name="itemId">台帳の項目の Id</param>
    public static RowTag Ledger(string itemId) => new(RowTagKind.Ledger, itemId);

    /// <summary>種別つきの理由の札</summary>
    /// <param name="kind">種別（<see cref="RowTagKind.Ledger"/> と <see cref="RowTagKind.Unspecified"/> 以外）</param>
    /// <param name="reason">理由</param>
    public static RowTag Reason(RowTagKind kind, string reason) => new(kind, reason);

    /// <summary>行を流さない方言・実装先を足した札</summary>
    /// <param name="target">方言・実装先の名前</param>
    /// <param name="reason">外す理由</param>
    public RowTag Except(string target, string reason) =>
        this with
        {
            Excluded = [.. Excluded, new TargetExclusion(target, reason)],
        };

    /// <summary>札の形の問題（種別の無い理由・空の理由・具体的でない理由・外す方言の空の理由）</summary>
    public IEnumerable<string> Problems()
    {
        if (Kind == RowTagKind.Unspecified || !Enum.IsDefined(Kind))
        {
            yield return "札の種別が無い（理由には種別を付ける：改名・新機能の行・既存の水準の対照・文書の特性の固定・低の指摘）";
        }

        if (string.IsNullOrWhiteSpace(Text))
        {
            yield return Kind == RowTagKind.Ledger ? "台帳の項目の Id が空" : "札の理由が空";
        }
        else if (
            Kind is not (RowTagKind.Ledger or RowTagKind.Unspecified)
            && FactorInventoryGuard.IsVagueReason(Text.Trim())
        )
        {
            yield return $"札の理由が具体的でない（{Text}）";
        }
        else if (Kind == RowTagKind.ReviewFinding && !ReviewFindingReason.IsMatch(Text.Trim()))
        {
            yield return $"低の指摘の札の理由が「指摘の番号（記録）：軸を水準で交差させない理由」の形でない（{Text}）";
        }

        foreach (var exclusion in Excluded)
        {
            if (string.IsNullOrWhiteSpace(exclusion?.Target))
            {
                yield return "外す方言・実装先の名前が空";
            }
            else if (string.IsNullOrWhiteSpace(exclusion.Reason))
            {
                yield return $"外す方言・実装先 {exclusion.Target} の理由が空";
            }
        }
    }

    /// <summary>低の指摘の札の理由の形（指摘の番号・全角の括弧の記録・全角のコロンの後ろに空でない理由）</summary>
    private static readonly Regex ReviewFindingReason = new(
        @"^[A-Z]+[0-9]+（[^（）]+）：\S.*$",
        RegexOptions.CultureInvariant
    );

    /// <inheritdoc/>
    public override string ToString() =>
        Kind == RowTagKind.Ledger ? $"台帳 {Text}" : $"{Kind}: {Text}";
}

/// <summary>札の対象の表の 1 つの Id（行 Id・生成の表の水準・必ず含む行）と札</summary>
/// <param name="Id">Id（表の中で一意）</param>
/// <param name="Tag">札（写しにある Id は null でよい）</param>
public sealed record BaselineEntry(string Id, RowTag? Tag);

/// <summary>札の対象の表（減るだけの写しと照合する単位）</summary>
/// <param name="Name">写しの中の表の名前</param>
/// <param name="Entries">今の Id と札</param>
public sealed record BaselineTable(string Name, IReadOnlyList<BaselineEntry> Entries)
{
    /// <summary>行の表から作る</summary>
    /// <typeparam name="TInput">行の入力の型</typeparam>
    /// <param name="name">写しの中の表の名前</param>
    /// <param name="rows">行</param>
    public static BaselineTable Of<TInput>(string name, IEnumerable<ScenarioRow<TInput>> rows) =>
        new(name, [.. rows.Select(row => new BaselineEntry(row.Id, row.Tag))]);

    /// <summary>生成の表の水準から作る（Id は <c>因子=水準</c>）</summary>
    /// <param name="name">写しの中の表の名前</param>
    /// <param name="table">次元表</param>
    public static BaselineTable LevelsOf(string name, DimensionTable table) =>
        new(
            name,
            [
                .. table.Factors.SelectMany(factor =>
                    factor.Levels.Select(level => new BaselineEntry(
                        $"{factor.Name}={level.Name}",
                        level.Tag
                    ))
                ),
            ]
        );

    /// <summary>生成の表の必ず含む行から作る</summary>
    /// <param name="name">写しの中の表の名前</param>
    /// <param name="rows">必ず含む行</param>
    public static BaselineTable RequiredRowsOf(string name, IEnumerable<RequiredRow> rows) =>
        new(name, [.. rows.Select(row => new BaselineEntry(row.Id, row.Tag))]);
}

/// <summary>生成の表に必ず含める完全な行（指摘の形）</summary>
/// <param name="Id">Id（表の中で一意・台帳の指摘のセルが指す）</param>
/// <param name="Assignment">全因子の水準の割り当て</param>
public sealed record RequiredRow(string Id, IReadOnlyDictionary<string, string> Assignment)
{
    /// <summary>新しい行の札（写しにある行は null のまま）</summary>
    public RowTag? Tag { get; init; }
}

/// <summary>写しに無い 1 つの Id と札</summary>
/// <param name="Table">表の名前</param>
/// <param name="Id">Id</param>
/// <param name="Tag">札（無ければ null＝問題）</param>
public sealed record BaselineNewEntry(string Table, string Id, RowTag? Tag);

/// <summary>写しとの照合の結果</summary>
/// <param name="Problems">問題（札の無い新しい行・札の形・台帳に無い Id・写しに無い表・表に無い写しの表・Id の重複）</param>
/// <param name="Kept">写しのうち今もある Id（表ごと・序数の順。写しにある表だけ＝書き直しはこれを書く）</param>
/// <param name="NewEntries">写しに無い Id と札</param>
public sealed record BaselineResult(
    IReadOnlyList<string> Problems,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Kept,
    IReadOnlyList<BaselineNewEntry> NewEntries
)
{
    /// <summary>「写しに無い行 N 本（台帳 M・理由 K）」と、札の一覧</summary>
    public string Summary()
    {
        var ledger = NewEntries.Count(entry => entry.Tag?.Kind == RowTagKind.Ledger);
        var reasons = NewEntries.Count(entry =>
            entry.Tag is { } tag && tag.Kind != RowTagKind.Ledger
        );
        var lines = new List<string>
        {
            $"写しに無い行 {NewEntries.Count} 本（台帳 {ledger}・理由 {reasons}）",
        };
        lines.AddRange(
            NewEntries.Select(entry =>
                $"  {entry.Table} {entry.Id}: {entry.Tag?.ToString() ?? "札なし"}"
            )
        );

        return string.Join("\n", lines);
    }
}

/// <summary>
/// 新しい行の札と減るだけの写し：札の対象の表の Id を写しと比べ、写しに無い Id に札（台帳の項目の Id か種別つきの理由）を求める
/// </summary>
/// <remarks>
/// <para>
/// 写しは表の名前 → Id の並び（序数の順）の JSON。書き直し（<see cref="RegenerationRequest"/>）は写しのうち今もある Id だけを書く
/// （<see cref="BaselineResult.Kept"/>）。Id を足さず、写しに無い表も書かない（写しから表の項目を消して書き直しても、その表の行は
/// 写しに入らない）。表を一覧に足すときは写しの JSON に表の項目を手で足し、差分にそのまま見えるようにする。
/// </para>
/// <para>
/// 写しのファイルが無いときに全部を書く道は無い（消して作り直す抜け道になるため）。照合は札だけを読み、行の中身は読まない。
/// </para>
/// </remarks>
public static class ScenarioRowBaseline
{
    /// <summary>写しを読む</summary>
    /// <param name="json">写しのテキスト</param>
    /// <exception cref="InvalidDataException">形が違う</exception>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        using var document = JsonDocument.Parse(json);

        if (
            !document.RootElement.TryGetProperty("tables", out var tables)
            || tables.ValueKind != JsonValueKind.Object
        )
        {
            throw new InvalidDataException("写しにオブジェクト tables が無い");
        }

        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        foreach (var table in tables.EnumerateObject())
        {
            if (table.Value.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException($"写しの表 {table.Name} が配列でない");
            }

            result[table.Name] =
            [
                .. table
                    .Value.EnumerateArray()
                    .Select(item =>
                        item.ValueKind == JsonValueKind.String
                            ? item.GetString()!
                            : throw new InvalidDataException(
                                $"写しの表 {table.Name} に文字列でない項目がある"
                            )
                    ),
            ];
        }

        return result;
    }

    /// <summary>写しのテキストを作る（表の名前の序数の順・Id の序数の順・改行は LF）</summary>
    /// <param name="tables">表の名前 → Id</param>
    public static string Render(IReadOnlyDictionary<string, IReadOnlyList<string>> tables)
    {
        ArgumentNullException.ThrowIfNull(tables);

        using var stream = new MemoryStream();

        using (
            var writer = new Utf8JsonWriter(
                stream,
                new JsonWriterOptions
                {
                    Indented = true,
                    NewLine = "\n",
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                }
            )
        )
        {
            writer.WriteStartObject();
            writer.WriteStartObject("tables");

            foreach (var name in tables.Keys.Order(StringComparer.Ordinal))
            {
                writer.WriteStartArray(name);

                foreach (var id in tables[name].Order(StringComparer.Ordinal))
                {
                    writer.WriteStringValue(id);
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    /// <summary>今の表と写しを照合する</summary>
    /// <param name="tables">札の対象の表（今の Id と札）</param>
    /// <param name="baseline">写し（<see cref="Parse"/>）</param>
    /// <param name="ledgerHas">台帳の項目の Id が台帳にあるか</param>
    public static BaselineResult Check(
        IReadOnlyList<BaselineTable> tables,
        IReadOnlyDictionary<string, IReadOnlyList<string>> baseline,
        Func<string, bool> ledgerHas
    )
    {
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(ledgerHas);

        var problems = new List<string>();
        var kept = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var newEntries = new List<BaselineNewEntry>();
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var table in tables)
        {
            if (!names.Add(table.Name))
            {
                problems.Add($"札の対象の表 {table.Name} が一覧に 2 回ある");
                continue;
            }

            foreach (
                var duplicate in table
                    .Entries.GroupBy(entry => entry.Id, StringComparer.Ordinal)
                    .Where(group => group.Count() > 1)
            )
            {
                problems.Add($"{table.Name}: Id {duplicate.Key} が重複している");
            }

            if (!baseline.TryGetValue(table.Name, out var recorded))
            {
                problems.Add(
                    $"{table.Name}: 写しに表が無い（表を一覧に足すときは写しの JSON に表の項目を手で足す。書き直しは写しに無い表を書かない）"
                );
                continue;
            }

            var recordedSet = recorded.ToHashSet(StringComparer.Ordinal);
            var current = table.Entries.Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);
            kept[table.Name] = [.. recorded.Where(current.Contains).Order(StringComparer.Ordinal)];

            foreach (var entry in table.Entries.Where(entry => !recordedSet.Contains(entry.Id)))
            {
                newEntries.Add(new BaselineNewEntry(table.Name, entry.Id, entry.Tag));

                if (entry.Tag is null)
                {
                    problems.Add(
                        $"{table.Name}: 写しに無い行 {entry.Id} に札が無い（台帳の項目の Id か、種別つきの理由を付ける）"
                    );
                    continue;
                }

                problems.AddRange(
                    entry.Tag.Problems().Select(problem => $"{table.Name}: {entry.Id}: {problem}")
                );

                if (
                    entry.Tag.Kind == RowTagKind.Ledger
                    && !string.IsNullOrWhiteSpace(entry.Tag.Text)
                    && !ledgerHas(entry.Tag.Text)
                )
                {
                    problems.Add($"{table.Name}: {entry.Id}: 台帳に無い項目の Id {entry.Tag.Text}");
                }
            }
        }

        problems.AddRange(
            baseline
                .Keys.Where(name => !names.Contains(name))
                .Order(StringComparer.Ordinal)
                .Select(name =>
                    $"写しの表 {name} が札の対象の表の一覧に無い（一覧から外したなら写しの JSON から表の項目を消す）"
                )
        );

        return new BaselineResult(problems, kept, newEntries);
    }
}
