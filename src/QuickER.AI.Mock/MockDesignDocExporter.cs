using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using QuickER.AI.Mock.Resources;

namespace QuickER.AI.Mock;

/// <summary>
/// モックフォルダの内容から、AI を使わず決定的な文字列処理だけで画面設計書（Markdown）を生成する。
/// </summary>
/// <remarks>
/// タイムスタンプや実行環境に依存する要素を一切含めないため、同じ入力からは常にバイト同一の
/// Markdown を返す（改行は <c>\n</c> 固定）。画面 HTML からの項目抽出は正規表現ヒューリスティックで、
/// HTML パーサは導入しない（取れない要素は黙って漏れてよい設計）。
/// 出力構成はタイトル・画面一覧・画面遷移図（mermaid）・画面×エンティティ（CRUD）表
/// （宣言があるときのみ）・画面ごとのセクションの順で、これのみ。
/// </remarks>
public static class MockDesignDocExporter
{
    /// <summary>モックフォルダ直下へ書き出す設計書のファイル名（GitHub でフォルダを開くと自動表示される規約名）</summary>
    public const string FileName = "README.md";

    /// <summary>&lt;script&gt; ブロック（抽出対象外にするため除去する）</summary>
    private static readonly Regex ScriptBlock = new(
        "<script[\\s\\S]*?</script>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    /// <summary>&lt;style&gt; ブロック（抽出対象外にするため除去する）</summary>
    private static readonly Regex StyleBlock = new(
        "<style[\\s\\S]*?</style>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    /// <summary>任意の HTML タグ（テキスト化の際に除去する）</summary>
    private static readonly Regex AnyTag = new("<[^>]+>", RegexOptions.Compiled);

    /// <summary>連続する空白（空白正規化に用いる）</summary>
    private static readonly Regex Whitespace = new("\\s+", RegexOptions.Compiled);

    /// <summary>属性 1 つ分（name または name="value" / name='value' / name=value）</summary>
    private static readonly Regex Attribute = new(
        "([A-Za-z_:][-A-Za-z0-9_:.]*)(?:\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s\"'>/]+)))?",
        RegexOptions.Compiled
    );

    /// <summary>タグ先頭の <c>&lt;タグ名</c>（属性解析の前に取り除く）</summary>
    private static readonly Regex LeadingTagName = new(
        "^\\s*<\\s*[A-Za-z][A-Za-z0-9]*",
        RegexOptions.Compiled
    );

    private static readonly Regex InputTag = new(
        "<input\\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex TextareaTag = new(
        "<textarea\\b([^>]*)>([\\s\\S]*?)</textarea>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex SelectBlock = new(
        "<select\\b([^>]*)>([\\s\\S]*?)</select>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex OptionBlock = new(
        "<option\\b[^>]*>([\\s\\S]*?)</option>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex ButtonBlock = new(
        "<button\\b[^>]*>([\\s\\S]*?)</button>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex AnchorBlock = new(
        "<a\\b([^>]*)>([\\s\\S]*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex LabelBlock = new(
        "<label\\b([^>]*)>([\\s\\S]*?)</label>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    /// <summary>ラベル内の必須マーカー（class に required を含む span。項目名から除いて備考「必須」へ移す）</summary>
    private static readonly Regex RequiredMarkerSpan = new(
        "<span\\b[^>]*class\\s*=\\s*(?:\"[^\"]*required[^\"]*\"|'[^']*required[^']*')[^>]*>[\\s\\S]*?</span>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex TableBlock = new(
        "<table\\b[^>]*>([\\s\\S]*?)</table>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex CaptionBlock = new(
        "<caption\\b[^>]*>([\\s\\S]*?)</caption>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex TheadBlock = new(
        "<thead\\b[^>]*>([\\s\\S]*?)</thead>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex TrBlock = new(
        "<tr\\b[^>]*>([\\s\\S]*?)</tr>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex ThBlock = new(
        "<th\\b[^>]*>([\\s\\S]*?)</th>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    /// <summary>モックフォルダの内容から画面設計書 Markdown を生成する</summary>
    /// <param name="store">対象のモックフォルダストア</param>
    /// <returns>決定的に組み立てた Markdown 文字列（改行は <c>\n</c>）</returns>
    public static string Export(MockFolderStore store)
    {
        var manifest = store.Manifest;
        var screens = manifest.Screens ?? new List<MockScreen>();
        var transitions = manifest.Transitions ?? new List<MockTransition>();

        // 実在画面（マニフェスト宣言）をファイル名で引ける表を作る（遷移の実在チェック・リンク解決に使う）
        var screenByFile = new Dictionary<string, MockScreen>(StringComparer.OrdinalIgnoreCase);

        foreach (var screen in screens)
        {
            if (!string.IsNullOrWhiteSpace(screen.File) && !screenByFile.ContainsKey(screen.File))
            {
                screenByFile[screen.File] = screen;
            }
        }

        // 画面ファイル名 → mermaid ノード ID の対応表（衝突時は連番付与で一意化）
        var nodeIds = BuildNodeIds(screens);

        var sb = new StringBuilder();

        // 1. タイトル
        var title = string.IsNullOrWhiteSpace(manifest.Title)
            ? Strings.MockDoc_DefaultTitle
            : manifest.Title.Trim();

        sb.Append("# ").Append(title).Append('\n');

        // 2. 画面一覧
        AppendScreenList(sb, screens);

        // 3. 画面遷移図（mermaid）
        AppendTransitionDiagram(sb, screens, transitions, screenByFile, nodeIds);

        // 4. 画面×エンティティ（CRUD）表（宣言が 1 件でもあれば。無ければセクションごと省略）
        AppendCrudTable(sb, screens);

        // 5. 画面ごとのセクション
        foreach (var screen in screens)
        {
            AppendScreenSection(sb, store, screen, transitions, screenByFile);
        }

        return sb.ToString();
    }

    /// <summary>画面一覧の見出しとリンク付きの表を追記する</summary>
    private static void AppendScreenList(StringBuilder sb, IReadOnlyList<MockScreen> screens)
    {
        sb.Append('\n');
        sb.Append("## ").Append(Strings.MockDoc_ScreenListHeading).Append('\n');
        sb.Append('\n');
        sb.Append("| ")
            .Append(Strings.MockDoc_ColScreen)
            .Append(" | ")
            .Append(Strings.MockDoc_ColDescription)
            .Append(" |\n");
        sb.Append("| --- | --- |\n");

        foreach (var screen in screens)
        {
            var link = ScreenLink(screen);
            var description = EscapeCell(screen.Description ?? string.Empty);

            sb.Append("| ").Append(link).Append(" | ").Append(description).Append(" |\n");
        }
    }

    /// <summary>画面遷移図（mermaid flowchart）を追記する（全画面をノード宣言・実在遷移のみエッジ化）</summary>
    private static void AppendTransitionDiagram(
        StringBuilder sb,
        IReadOnlyList<MockScreen> screens,
        IReadOnlyList<MockTransition> transitions,
        IReadOnlyDictionary<string, MockScreen> screenByFile,
        IReadOnlyDictionary<string, string> nodeIds
    )
    {
        sb.Append('\n');
        sb.Append("## ").Append(Strings.MockDoc_TransitionDiagramHeading).Append('\n');
        sb.Append('\n');
        sb.Append("```mermaid\n");
        sb.Append("flowchart LR\n");

        // 全画面をノードとして宣言する（遷移が無い画面も図に現れる）
        foreach (var screen in screens)
        {
            var id = nodeIds[screen.File];
            var label = MermaidLabel(ScreenDisplayName(screen));

            sb.Append("  ").Append(id).Append("[\"").Append(label).Append("\"]\n");
        }

        // From/To の双方が実在画面である遷移のみをエッジにする
        foreach (var transition in transitions)
        {
            if (
                string.IsNullOrWhiteSpace(transition.From)
                || string.IsNullOrWhiteSpace(transition.To)
                || !nodeIds.TryGetValue(transition.From, out var fromId)
                || !nodeIds.TryGetValue(transition.To, out var toId)
                || !screenByFile.ContainsKey(transition.From)
                || !screenByFile.ContainsKey(transition.To)
            )
            {
                continue;
            }

            sb.Append("  ").Append(fromId).Append(" -->");

            if (!string.IsNullOrWhiteSpace(transition.Trigger))
            {
                sb.Append('|').Append(MermaidEdgeText(transition.Trigger)).Append('|');
            }

            sb.Append(' ').Append(toId).Append('\n');
        }

        sb.Append("```\n");
    }

    /// <summary>
    /// 画面×エンティティ（CRUD）表を追記する。行＝全画面（マニフェスト順）・列＝宣言されたエンティティ名
    /// （マニフェスト順に画面を走査した初出順）・セルは正規化済み操作文字列（例 <c>CRU</c>）。
    /// </summary>
    /// <remarks>
    /// 宣言（<see cref="MockScreen.Entities"/>）が 1 件も無ければセクションごと省略する
    /// （既存フォルダの設計書に空表を出さない）。宣言のない画面も空セル行として出し、未宣言が見えるようにする。
    /// </remarks>
    private static void AppendCrudTable(StringBuilder sb, IReadOnlyList<MockScreen> screens)
    {
        // 列見出し＝宣言されたエンティティ名。マニフェスト順に画面を走査した初出順で決定的に並べる
        var columns = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var screen in screens)
        {
            if (screen.Entities is null)
            {
                continue;
            }

            foreach (var entity in screen.Entities)
            {
                var name = entity?.Name ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(name) && seen.Add(name))
                {
                    columns.Add(name);
                }
            }
        }

        // 宣言が 1 件も無ければ表を出さない
        if (columns.Count == 0)
        {
            return;
        }

        sb.Append('\n');
        sb.Append("## ").Append(Strings.MockDoc_CrudHeading).Append('\n');
        sb.Append('\n');

        // ヘッダ行（先頭列は画面・以降は各エンティティ名）
        sb.Append("| ").Append(Strings.MockDoc_ColScreen);

        foreach (var column in columns)
        {
            sb.Append(" | ").Append(EscapeCell(column));
        }

        sb.Append(" |\n");

        // 区切り行（列数＝1＋エンティティ数）
        sb.Append("| ---");

        for (var i = 0; i < columns.Count; i++)
        {
            sb.Append(" | ---");
        }

        sb.Append(" |\n");

        // 本体行（全画面。宣言のない画面も空セル行として出す）
        foreach (var screen in screens)
        {
            sb.Append("| ").Append(ScreenLink(screen));

            foreach (var column in columns)
            {
                sb.Append(" | ").Append(EscapeCell(FindEntityOperations(screen, column)));
            }

            sb.Append(" |\n");
        }
    }

    /// <summary>画面の指定エンティティ列に対する正規化済み操作文字列を返す（該当宣言が無ければ空文字）</summary>
    private static string FindEntityOperations(MockScreen screen, string entityName)
    {
        if (screen.Entities is null)
        {
            return string.Empty;
        }

        foreach (var entity in screen.Entities)
        {
            if (
                entity is not null
                && string.Equals(entity.Name, entityName, StringComparison.Ordinal)
            )
            {
                return entity.Operations ?? string.Empty;
            }
        }

        return string.Empty;
    }

    /// <summary>1 画面分のセクション（説明・遷移元／先・画面項目表）を追記する</summary>
    private static void AppendScreenSection(
        StringBuilder sb,
        MockFolderStore store,
        MockScreen screen,
        IReadOnlyList<MockTransition> transitions,
        IReadOnlyDictionary<string, MockScreen> screenByFile
    )
    {
        sb.Append('\n');
        sb.Append("## ").Append(ScreenDisplayName(screen)).Append('\n');

        // 説明（段落。表セルではないためエスケープ不要）
        if (!string.IsNullOrWhiteSpace(screen.Description))
        {
            sb.Append('\n').Append(screen.Description.Trim()).Append('\n');
        }

        // 遷移先（この画面が起点）・遷移元（この画面が終点）を箇条書きにする
        var outgoing = transitions
            .Where(t =>
                string.Equals(t.From, screen.File, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(t.To)
                && screenByFile.ContainsKey(t.To)
            )
            .ToList();

        var incoming = transitions
            .Where(t =>
                string.Equals(t.To, screen.File, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(t.From)
                && screenByFile.ContainsKey(t.From)
            )
            .ToList();

        if (outgoing.Count > 0 || incoming.Count > 0)
        {
            sb.Append('\n');

            foreach (var transition in outgoing)
            {
                AppendTransitionBullet(
                    sb,
                    Strings.MockDoc_TransitionTo,
                    screenByFile[transition.To],
                    transition.Trigger
                );
            }

            foreach (var transition in incoming)
            {
                AppendTransitionBullet(
                    sb,
                    Strings.MockDoc_TransitionFrom,
                    screenByFile[transition.From],
                    transition.Trigger
                );
            }
        }

        // 画面項目表（機械抽出。抽出ゼロなら表ごと省略）
        var html = store.GetScreenHtml(screen.File);

        if (html is not null)
        {
            var items = ExtractItems(html);

            if (items.Count > 0)
            {
                AppendItemTable(sb, items);
            }
        }
    }

    /// <summary>遷移の箇条書き 1 行を追記する（トリガーが空なら括弧なし）</summary>
    private static void AppendTransitionBullet(
        StringBuilder sb,
        string label,
        MockScreen target,
        string? trigger
    )
    {
        sb.Append("- ").Append(label).Append(": ").Append(ScreenLink(target));

        if (!string.IsNullOrWhiteSpace(trigger))
        {
            // 括弧は言語ごとの書式（en は半角＋前空白・ja は全角）＝resx で UI 言語に追従する
            sb.Append(
                string.Format(
                    CultureInfo.InvariantCulture,
                    Strings.MockDoc_TriggerFormat,
                    InlineText(trigger)
                )
            );
        }

        sb.Append('\n');
    }

    /// <summary>画面項目表（種別・項目・備考の 3 列）を追記する</summary>
    private static void AppendItemTable(StringBuilder sb, IReadOnlyList<DocItem> items)
    {
        sb.Append('\n');
        sb.Append("| ")
            .Append(Strings.MockDoc_ColKind)
            .Append(" | ")
            .Append(Strings.MockDoc_ColItem)
            .Append(" | ")
            .Append(Strings.MockDoc_ColNote)
            .Append(" |\n");
        sb.Append("| --- | --- | --- |\n");

        foreach (var item in items)
        {
            sb.Append("| ")
                .Append(EscapeCell(item.Kind))
                .Append(" | ")
                .Append(EscapeCell(item.Item))
                .Append(" | ")
                .Append(EscapeCell(item.Note))
                .Append(" |\n");
        }
    }

    /// <summary>画面 HTML から画面項目（入力・選択・ボタン・テーブル列）を機械抽出する</summary>
    private static List<DocItem> ExtractItems(string html)
    {
        // script / style ブロックは抽出対象外にするため先に除去する
        var cleaned = ScriptBlock.Replace(html, string.Empty);
        cleaned = StyleBlock.Replace(cleaned, string.Empty);

        // ラベルの事前収集（for 指定ラベル・包含ラベル）。必須マーカー span は項目名から除き、必須フラグとして持つ
        var forLabels = new Dictionary<string, LabelInfo>(StringComparer.Ordinal);
        var wrappingLabels = new List<WrappingLabel>();

        foreach (Match label in LabelBlock.Matches(cleaned))
        {
            var attrs = ParseAttributes(label.Groups[1].Value);
            var inner = label.Groups[2].Value;
            var required = RequiredMarkerSpan.IsMatch(inner);
            var text = Clean(RequiredMarkerSpan.Replace(inner, string.Empty));

            if (attrs.TryGetValue("for", out var forId) && !string.IsNullOrEmpty(forId))
            {
                forLabels[forId] = new LabelInfo(text, required);
            }
            else
            {
                wrappingLabels.Add(new WrappingLabel(inner, text, required));
            }
        }

        var inputs = new List<DocItem>();
        var choices = new List<DocItem>();
        var buttons = new List<(int Index, string Text)>();
        var radios = new List<RadioEntry>();

        // 入力欄・ラジオ／チェックボックス・submit/button（value）を 1 パスで振り分ける
        foreach (Match input in InputTag.Matches(cleaned))
        {
            var attrs = ParseAttributes(input.Value);
            var type = attrs.GetValueOrDefault("type", "text").ToLowerInvariant();

            if (type == "hidden")
            {
                continue;
            }

            if (type == "radio" || type == "checkbox")
            {
                radios.Add(
                    new RadioEntry(
                        attrs.GetValueOrDefault("name", string.Empty),
                        type,
                        attrs,
                        input.Value
                    )
                );

                continue;
            }

            if (type == "submit" || type == "button")
            {
                buttons.Add((input.Index, attrs.GetValueOrDefault("value", string.Empty)));

                continue;
            }

            var (name, placeholderUsed, labelRequired) = ResolveName(
                attrs,
                input.Value,
                forLabels,
                wrappingLabels
            );
            var notes = new List<string> { type };
            var placeholder = attrs.GetValueOrDefault("placeholder", string.Empty);

            if (!placeholderUsed && !string.IsNullOrWhiteSpace(placeholder))
            {
                notes.Add(placeholder);
            }

            if (attrs.ContainsKey("required") || labelRequired)
            {
                notes.Add(Strings.MockDoc_NoteRequired);
            }

            inputs.Add(new DocItem(Strings.MockDoc_KindInput, name, string.Join(" / ", notes)));
        }

        // テキストエリア（複数行入力）
        foreach (Match textarea in TextareaTag.Matches(cleaned))
        {
            var attrs = ParseAttributes(textarea.Groups[1].Value);
            var (name, placeholderUsed, labelRequired) = ResolveName(
                attrs,
                textarea.Value,
                forLabels,
                wrappingLabels
            );
            var notes = new List<string> { Strings.MockDoc_NoteMultiline };
            var placeholder = attrs.GetValueOrDefault("placeholder", string.Empty);

            if (!placeholderUsed && !string.IsNullOrWhiteSpace(placeholder))
            {
                notes.Add(placeholder);
            }

            if (attrs.ContainsKey("required") || labelRequired)
            {
                notes.Add(Strings.MockDoc_NoteRequired);
            }

            inputs.Add(new DocItem(Strings.MockDoc_KindInput, name, string.Join(" / ", notes)));
        }

        // セレクトボックス（選択肢の先頭 3 件を備考に列挙）
        foreach (Match select in SelectBlock.Matches(cleaned))
        {
            var attrs = ParseAttributes(select.Groups[1].Value);
            var (name, _, labelRequired) = ResolveName(
                attrs,
                select.Value,
                forLabels,
                wrappingLabels
            );

            var options = new List<string>();

            foreach (Match option in OptionBlock.Matches(select.Groups[2].Value))
            {
                var text = Clean(option.Groups[1].Value);

                if (!string.IsNullOrEmpty(text))
                {
                    options.Add(text);
                }
            }

            var note = FormatOptionList(options);

            if (attrs.ContainsKey("required") || labelRequired)
            {
                note =
                    note.Length > 0
                        ? note + " / " + Strings.MockDoc_NoteRequired
                        : Strings.MockDoc_NoteRequired;
            }

            choices.Add(new DocItem(Strings.MockDoc_KindChoice, name, note));
        }

        // ラジオ／チェックボックスは name ごとに 1 行へまとめる（初出順を維持）
        foreach (var group in GroupRadios(radios, forLabels, wrappingLabels))
        {
            choices.Add(group);
        }

        // ボタン（<button> / a.btn）を収集し、submit/button と合わせて文書内出現順に並べる
        foreach (Match button in ButtonBlock.Matches(cleaned))
        {
            buttons.Add((button.Index, Clean(button.Groups[1].Value)));
        }

        foreach (Match anchor in AnchorBlock.Matches(cleaned))
        {
            var attrs = ParseAttributes(anchor.Groups[1].Value);
            var css = attrs.GetValueOrDefault("class", string.Empty);

            // class に btn を含むアンカーのみボタン扱い（素のナビゲーションリンクは拾わない）
            if (css.IndexOf("btn", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                buttons.Add((anchor.Index, Clean(anchor.Groups[2].Value)));
            }
        }

        // 行ごとに繰り返される同一テキストのボタン（一覧の「編集」×N 等）は 1 行へ集約する（初出順を維持）
        var buttonItems = buttons
            .Where(b => !string.IsNullOrWhiteSpace(b.Text))
            .OrderBy(b => b.Index)
            .GroupBy(b => b.Text, StringComparer.Ordinal)
            .Select(g => new DocItem(Strings.MockDoc_KindButton, g.Key, string.Empty))
            .ToList();

        // テーブル列（thead の th・無ければ最初の tr の th）
        var tableItems = ExtractTableColumns(cleaned);

        // 種別ごとに固定順（入力→選択→ボタン→テーブル列）で連結する
        var items = new List<DocItem>();
        items.AddRange(inputs);
        items.AddRange(choices);
        items.AddRange(buttonItems);
        items.AddRange(tableItems);

        return items;
    }

    /// <summary>ラジオ／チェックボックスを name ごとに 1 行へまとめる（初出順を維持）</summary>
    private static IEnumerable<DocItem> GroupRadios(
        List<RadioEntry> radios,
        IReadOnlyDictionary<string, LabelInfo> forLabels,
        IReadOnlyList<WrappingLabel> wrappingLabels
    )
    {
        var order = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var radio in radios)
        {
            if (seen.Add(radio.Name))
            {
                order.Add(radio.Name);
            }
        }

        foreach (var name in order)
        {
            var members = radios.Where(r => r.Name == name).ToList();
            var type = members[0].Type;

            // 項目名: name があれば name、無ければ各メンバーのラベルを連結
            string item;

            if (!string.IsNullOrWhiteSpace(name))
            {
                item = name;
            }
            else
            {
                var labels = members
                    .Select(m => ResolveName(m.Attrs, m.Tag, forLabels, wrappingLabels).Name)
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .Distinct(StringComparer.Ordinal);

                item = string.Join(" / ", labels);
            }

            var kindWord =
                type == "radio" ? Strings.MockDoc_NoteRadio : Strings.MockDoc_NoteCheckbox;
            var count = string.Format(
                CultureInfo.InvariantCulture,
                Strings.MockDoc_NoteOptionCountFormat,
                members.Count
            );

            yield return new DocItem(Strings.MockDoc_KindChoice, item, kindWord + " / " + count);
        }
    }

    /// <summary>各テーブルのヘッダ列を抽出し、テーブルごとに 1 行の項目にする</summary>
    private static List<DocItem> ExtractTableColumns(string cleaned)
    {
        var items = new List<DocItem>();
        var tableIndex = 0;

        foreach (Match table in TableBlock.Matches(cleaned))
        {
            tableIndex++;

            var inner = table.Groups[1].Value;

            // ヘッダ行の探索: thead があればその中、無ければ最初の tr
            string headerSource;
            var thead = TheadBlock.Match(inner);

            if (thead.Success)
            {
                headerSource = thead.Groups[1].Value;
            }
            else
            {
                var firstRow = TrBlock.Match(inner);
                headerSource = firstRow.Success ? firstRow.Groups[1].Value : string.Empty;
            }

            var columns = new List<string>();

            foreach (Match th in ThBlock.Matches(headerSource))
            {
                var text = Clean(th.Groups[1].Value);

                if (!string.IsNullOrEmpty(text))
                {
                    columns.Add(text);
                }
            }

            if (columns.Count == 0)
            {
                continue;
            }

            // 項目名: caption があればそれ、無ければ「テーブル N」
            var caption = CaptionBlock.Match(inner);
            var name = caption.Success ? Clean(caption.Groups[1].Value) : string.Empty;

            if (string.IsNullOrEmpty(name))
            {
                name = string.Format(
                    CultureInfo.InvariantCulture,
                    Strings.MockDoc_ItemTableFormat,
                    tableIndex
                );
            }

            items.Add(
                new DocItem(Strings.MockDoc_KindTableColumn, name, string.Join(" / ", columns))
            );
        }

        return items;
    }

    /// <summary>
    /// コントロールの項目名を解決する。優先順は label[for=id] → 包含ラベル → placeholder → name/id。
    /// </summary>
    /// <returns>解決した項目名・placeholder を項目名に採用したか（採用時は備考へは出さない）・ラベルに必須マーカーがあったか</returns>
    private static (string Name, bool PlaceholderUsed, bool LabelRequired) ResolveName(
        IReadOnlyDictionary<string, string> attrs,
        string controlTag,
        IReadOnlyDictionary<string, LabelInfo> forLabels,
        IReadOnlyList<WrappingLabel> wrappingLabels
    )
    {
        var id = attrs.GetValueOrDefault("id", string.Empty);

        // 1. <label for="{id}">
        if (
            !string.IsNullOrEmpty(id)
            && forLabels.TryGetValue(id, out var forLabel)
            && !string.IsNullOrWhiteSpace(forLabel.Text)
        )
        {
            return (forLabel.Text, false, forLabel.Required);
        }

        // 2. コントロールを包含する <label>
        foreach (var label in wrappingLabels)
        {
            if (
                label.Inner.Contains(controlTag, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(label.Text)
            )
            {
                return (label.Text, false, label.Required);
            }
        }

        // 3. placeholder
        var placeholder = attrs.GetValueOrDefault("placeholder", string.Empty);

        if (!string.IsNullOrWhiteSpace(placeholder))
        {
            return (placeholder, true, false);
        }

        // 4. name / id（最後の手段）
        var name = attrs.GetValueOrDefault("name", string.Empty);

        return (!string.IsNullOrEmpty(name) ? name : id, false, false);
    }

    /// <summary>選択肢リストを「A / B / C …」形式の備考にする（先頭 3 件・4 件以上は末尾に「…」）</summary>
    private static string FormatOptionList(IReadOnlyList<string> options)
    {
        if (options.Count == 0)
        {
            return string.Empty;
        }

        var head = string.Join(" / ", options.Take(3));

        return options.Count > 3 ? head + " …" : head;
    }

    /// <summary>画面ファイル名 → mermaid ノード ID の一意対応表を作る（衝突時は連番付与）</summary>
    private static Dictionary<string, string> BuildNodeIds(IReadOnlyList<MockScreen> screens)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<string>(StringComparer.Ordinal);

        foreach (var screen in screens)
        {
            if (string.IsNullOrWhiteSpace(screen.File) || map.ContainsKey(screen.File))
            {
                continue;
            }

            var baseId = MermaidNodeIdBase(screen.File);
            var id = baseId;
            var suffix = 2;

            // 衝突時は連番を付けて一意化する（決定的・宣言順に依存）
            while (!used.Add(id))
            {
                id = baseId + "_" + suffix.ToString(CultureInfo.InvariantCulture);
                suffix++;
            }

            map[screen.File] = id;
        }

        return map;
    }

    /// <summary>ファイル名から mermaid ノード ID の素（拡張子除去・英数字以外は <c>_</c>・先頭数字回避）を作る</summary>
    private static string MermaidNodeIdBase(string file)
    {
        var name = file;
        var dot = name.LastIndexOf('.');

        if (dot > 0)
        {
            name = name.Substring(0, dot);
        }

        var builder = new StringBuilder(name.Length);

        foreach (var ch in name)
        {
            builder.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        }

        var result = builder.ToString();

        // 空・先頭が数字なら英字を前置する（mermaid の識別子として安全にする）
        if (result.Length == 0 || !(char.IsLetter(result[0]) || result[0] == '_'))
        {
            result = "s" + result;
        }

        return result;
    }

    /// <summary>画面の表示名（Name。空ならファイル名）を得る</summary>
    private static string ScreenDisplayName(MockScreen screen) =>
        string.IsNullOrWhiteSpace(screen.Name) ? screen.File : screen.Name.Trim();

    /// <summary>画面へのフォルダ内相対リンク <c>[表示名](ファイル名)</c> を組み立てる</summary>
    private static string ScreenLink(MockScreen screen)
    {
        var text = EscapeLinkText(ScreenDisplayName(screen));

        return "[" + text + "](" + screen.File + ")";
    }

    /// <summary>HTML 断片をプレーンテキスト化する（タグ除去・エンティティのデコード・空白正規化）</summary>
    private static string Clean(string html)
    {
        var withoutTags = AnyTag.Replace(html, string.Empty);
        var decoded = DecodeEntities(withoutTags);

        return Whitespace.Replace(decoded, " ").Trim();
    }

    /// <summary>属性値・トリガー等のプレーン文字列を空白正規化する（タグは含まない前提でエンティティのみ解く）</summary>
    private static string InlineText(string value)
    {
        var decoded = DecodeEntities(value);

        return Whitespace.Replace(decoded, " ").Trim();
    }

    /// <summary>
    /// HTML エンティティをデコードする。BCL の <see cref="System.Net.WebUtility.HtmlDecode(string)"/> に委譲し、
    /// named（&amp;laquo; 等）・数値（&amp;#39; / &amp;#x27;）の全参照を網羅する（決定的・依存追加なし。
    /// &amp;nbsp; は U+00A0 になるが、後段の空白正規化（\s+）が通常スペースへ畳む）。
    /// </summary>
    private static string DecodeEntities(string value) => System.Net.WebUtility.HtmlDecode(value);

    /// <summary>Markdown 表セル向けのエスケープ（改行を空白へ・パイプをエスケープ）</summary>
    private static string EscapeCell(string value)
    {
        var single = value.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');

        return single.Replace("|", "\\|").Trim();
    }

    /// <summary>Markdown リンクテキスト向けのエスケープ（角括弧・パイプ・改行）</summary>
    private static string EscapeLinkText(string value)
    {
        var single = value.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');

        return single.Replace("[", "\\[").Replace("]", "\\]").Replace("|", "\\|").Trim();
    }

    /// <summary>mermaid ノードラベル向けのエスケープ（引用符をエンティティ化・改行を空白へ）</summary>
    private static string MermaidLabel(string value)
    {
        var single = value.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');

        return single.Replace("\"", "&quot;").Trim();
    }

    /// <summary>mermaid エッジラベル向けのエスケープ（区切りのパイプを退避・改行を空白へ）</summary>
    private static string MermaidEdgeText(string value)
    {
        var single = value.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');

        return single.Replace("|", "/").Trim();
    }

    /// <summary>タグ文字列（またはその属性部分）から属性辞書を作る（キーは大文字小文字無視・真偽属性は空値）</summary>
    private static Dictionary<string, string> ParseAttributes(string tagOrAttributes)
    {
        // 先頭の <タグ名 を取り除いてから属性を走査する
        var attributesPart = LeadingTagName.Replace(tagOrAttributes, string.Empty);
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in Attribute.Matches(attributesPart))
        {
            var name = match.Groups[1].Value;

            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            string value;

            if (match.Groups[2].Success)
            {
                value = match.Groups[2].Value;
            }
            else if (match.Groups[3].Success)
            {
                value = match.Groups[3].Value;
            }
            else if (match.Groups[4].Success)
            {
                value = match.Groups[4].Value;
            }
            else
            {
                value = string.Empty;
            }

            dict[name] = DecodeEntities(value);
        }

        return dict;
    }

    /// <summary>設計書 1 行分の項目（種別・項目・備考）</summary>
    private readonly record struct DocItem(string Kind, string Item, string Note);

    /// <summary>for 指定ラベルの抽出テキストと必須マーカーの有無</summary>
    private readonly record struct LabelInfo(string Text, bool Required);

    /// <summary>コントロールを包含する（for を持たない）ラベルの生 HTML・抽出テキスト・必須マーカーの有無</summary>
    private readonly record struct WrappingLabel(string Inner, string Text, bool Required);

    /// <summary>グループ化前のラジオ／チェックボックス 1 個分</summary>
    private readonly record struct RadioEntry(
        string Name,
        string Type,
        Dictionary<string, string> Attrs,
        string Tag
    );
}
