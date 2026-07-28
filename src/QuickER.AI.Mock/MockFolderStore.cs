using System.IO;
using System.Text;
using System.Text.Json;

namespace QuickER.AI.Mock;

/// <summary>
/// 1 つのモックフォルダ（フラット構成: <c>mock.json</c>＋<c>*.html</c>＋<c>style.css</c>）への読み書きを担うストア。
/// 1 インスタンス = 1 フォルダで、マニフェストと各ファイルの整合を保ちながらライブ保存する。
/// </summary>
/// <remarks>
/// 書き出しはすべて BOM なし UTF-8。時刻は <see cref="Func{DateTimeOffset}"/> をコンストラクタ注入して
/// テスト可能にしている（改訂履歴のタイムスタンプに用いる）。
/// </remarks>
public sealed class MockFolderStore
{
    /// <summary>BOM なし UTF-8 エンコーディング（全書き出しで共用）</summary>
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>対象フォルダ</summary>
    private readonly string _folder;

    /// <summary>現在時刻の供給元（改訂履歴用・テスト差し替え可能）</summary>
    private readonly Func<DateTimeOffset> _clock;

    /// <summary>フォルダ内で保持しているマニフェスト（このインスタンスが正本を保持し、変更のたびに保存する）</summary>
    private readonly MockManifest _manifest;

    private MockFolderStore(string folder, MockManifest manifest, Func<DateTimeOffset> clock)
    {
        _folder = folder;
        _manifest = manifest;
        _clock = clock;
    }

    /// <summary>対象フォルダのフルパス</summary>
    public string Folder => _folder;

    /// <summary>現在のマニフェスト（読み取り用スナップショット。外部から変更しても内部状態には影響しない）</summary>
    public MockManifest Manifest => CloneManifest(_manifest);

    /// <summary>指定フォルダがモックフォルダ（<c>mock.json</c> を持つ）かどうかを返す</summary>
    public static bool IsMockFolder(string folder) =>
        File.Exists(Path.Combine(folder, MockManifest.ManifestFileName));

    /// <summary>
    /// 新規モックフォルダを作成し、初期マニフェストを書き出してストアを返す。
    /// 既に <c>mock.json</c> が存在する場合は <see cref="InvalidOperationException"/>。
    /// </summary>
    /// <param name="folder">作成先フォルダ</param>
    /// <param name="title">モックの表題</param>
    /// <param name="sourceSchema">元になった ER スキーマ記述テキスト</param>
    /// <param name="clock">現在時刻の供給元（省略時は <see cref="DateTimeOffset.Now"/>）</param>
    public static MockFolderStore CreateNew(
        string folder,
        string title,
        string sourceSchema,
        Func<DateTimeOffset>? clock = null
    )
    {
        var manifestPath = Path.Combine(folder, MockManifest.ManifestFileName);

        if (File.Exists(manifestPath))
        {
            throw new InvalidOperationException(
                $"モックフォルダは既に存在します（{MockManifest.ManifestFileName} が見つかりました）: {folder}"
            );
        }

        Directory.CreateDirectory(folder);

        var manifest = new MockManifest
        {
            Version = MockManifest.CurrentVersion,
            Title = title ?? string.Empty,
            SourceSchema = sourceSchema ?? string.Empty,
        };

        var store = new MockFolderStore(folder, manifest, clock ?? (() => DateTimeOffset.Now));
        store.SaveManifest();

        return store;
    }

    /// <summary>
    /// 既存のモックフォルダを開く。<c>mock.json</c> 不在・JSON 破損・新フォーマット（Version 超過）は
    /// 呼び出し側でユーザー提示できる明確なメッセージの例外を投げる。
    /// </summary>
    /// <param name="folder">開くフォルダ</param>
    /// <param name="clock">現在時刻の供給元（省略時は <see cref="DateTimeOffset.Now"/>）</param>
    public static MockFolderStore Open(string folder, Func<DateTimeOffset>? clock = null)
    {
        var manifestPath = Path.Combine(folder, MockManifest.ManifestFileName);

        if (!File.Exists(manifestPath))
        {
            throw new InvalidOperationException(
                $"モックフォルダではありません（{MockManifest.ManifestFileName} が見つかりません）: {folder}"
            );
        }

        MockManifest? manifest;

        try
        {
            var json = File.ReadAllText(manifestPath);
            manifest = JsonSerializer.Deserialize<MockManifest>(
                json,
                MockManifest.SerializerOptions
            );
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"モックの {MockManifest.ManifestFileName} を解釈できませんでした（破損している可能性があります）: {ex.Message}",
                ex
            );
        }

        if (manifest is null)
        {
            throw new InvalidOperationException(
                $"モックの {MockManifest.ManifestFileName} が空か無効です: {folder}"
            );
        }

        if (manifest.Version > MockManifest.CurrentVersion)
        {
            throw new InvalidOperationException(
                $"より新しいフォーマットのモックです（version={manifest.Version}・このアプリの対応は {MockManifest.CurrentVersion} まで）。アプリを更新してください: {folder}"
            );
        }

        // null 耐性: 古い/部分的な JSON でもリストは常に存在させる
        manifest.Screens ??= new List<MockScreen>();
        manifest.Transitions ??= new List<MockTransition>();
        manifest.Revisions ??= new List<MockRevision>();

        return new MockFolderStore(folder, manifest, clock ?? (() => DateTimeOffset.Now));
    }

    /// <summary>
    /// 画面 HTML を書き出し、マニフェストの画面・遷移・改訂を更新して保存する。
    /// </summary>
    /// <remarks>
    /// 検証エラーの例外文言は<b>英語で固定</b>する。<c>save_screen</c> ツールの失敗結果として
    /// そのまま AI へ返る機械向けメッセージであり、UI 表示文言ではないため（言語方針＝機械向け診断は英語固定）。
    /// </remarks>
    /// <param name="file">画面ファイル名（フォルダ直下・<c>.html</c>・パス区切りや <c>".."</c> 不可）</param>
    /// <param name="name">画面の表示名</param>
    /// <param name="description">画面の役割説明</param>
    /// <param name="html">画面 HTML 全体（空・<c>&lt;html</c> を含まない場合は拒否）</param>
    /// <param name="transitions">この画面を起点とする遷移（既存の同起点遷移を差し替える）</param>
    /// <param name="revisionNote">改訂メモ</param>
    /// <param name="entities">
    /// この画面が扱うエンティティと CRUD 操作の宣言。<c>null</c>＝既存宣言を維持・空リスト＝宣言を消去・
    /// 非空＝正規化して全置換（transitions の毎回全置換とは意図的に非対称。付け忘れで宣言が剝がれるのを防ぐ）。
    /// </param>
    /// <returns>機械検証の警告一覧（保存は拒否しない）</returns>
    public IReadOnlyList<string> SaveScreen(
        string file,
        string name,
        string description,
        string html,
        IReadOnlyList<MockTransition> transitions,
        string revisionNote,
        IReadOnlyList<MockScreenEntity>? entities = null
    )
    {
        ValidateScreenFileName(file);

        if (string.IsNullOrWhiteSpace(html))
        {
            throw new ArgumentException(
                "html is empty. Provide the complete HTML document.",
                nameof(html)
            );
        }

        if (!html.Contains("<html", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "html is not a complete HTML document. Provide a single self-contained HTML document that includes <html>.",
                nameof(html)
            );
        }

        var normalizedTransitions = transitions ?? Array.Empty<MockTransition>();

        // HTML ファイルを書き出す
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, file), html, Utf8NoBom);

        // 画面を upsert（ファイル名一致・大文字小文字無視）
        var existing = _manifest.Screens.FirstOrDefault(s =>
            string.Equals(s.File, file, StringComparison.OrdinalIgnoreCase)
        );

        if (existing is null)
        {
            var added = new MockScreen
            {
                File = file,
                Name = name ?? string.Empty,
                Description = description ?? string.Empty,
            };
            // 新規画面は entities 省略（null）なら宣言なし・指定ありなら正規化して設定する
            added.Entities = ResolveEntities(added.Entities, entities);
            _manifest.Screens.Add(added);
        }
        else
        {
            existing.File = file;
            existing.Name = name ?? string.Empty;
            existing.Description = description ?? string.Empty;
            // upsert 意味論: null=既存維持・空=消去・非空=正規化して全置換
            existing.Entities = ResolveEntities(existing.Entities, entities);
        }

        // この画面を起点（From）とする遷移を差し替える
        _manifest.Transitions.RemoveAll(t =>
            string.Equals(t.From, file, StringComparison.OrdinalIgnoreCase)
        );
        _manifest.Transitions.AddRange(
            normalizedTransitions.Select(t => new MockTransition
            {
                From = string.IsNullOrWhiteSpace(t.From) ? file : t.From,
                To = t.To,
                Trigger = t.Trigger,
            })
        );

        AppendRevision(
            string.IsNullOrWhiteSpace(revisionNote) ? $"Saved screen '{file}'." : revisionNote
        );

        SaveManifest();

        // 保存後のフォルダ状態＋マニフェスト宣言を既知集合として検証する
        var knownScreens = CollectKnownScreenFiles();

        return MockContentValidator.ValidateScreen(file, html, normalizedTransitions, knownScreens);
    }

    /// <summary>
    /// 画面を削除する。HTML 削除・画面除去・その画面が From/To の遷移除去・改訂追記・保存を行う。
    /// </summary>
    /// <param name="file">削除する画面ファイル名</param>
    public void RemoveScreen(string file)
    {
        ValidateScreenFileName(file);

        var existing = _manifest.Screens.FirstOrDefault(s =>
            string.Equals(s.File, file, StringComparison.OrdinalIgnoreCase)
        );

        if (existing is null)
        {
            throw new InvalidOperationException($"画面が見つかりません: {file}");
        }

        var path = Path.Combine(_folder, file);

        if (File.Exists(path))
        {
            File.Delete(path);
        }

        _manifest.Screens.RemoveAll(s =>
            string.Equals(s.File, file, StringComparison.OrdinalIgnoreCase)
        );

        // 起点・終点いずれかがこの画面である遷移を除去する
        _manifest.Transitions.RemoveAll(t =>
            string.Equals(t.From, file, StringComparison.OrdinalIgnoreCase)
            || string.Equals(t.To, file, StringComparison.OrdinalIgnoreCase)
        );

        AppendRevision($"Removed screen '{file}'.");
        SaveManifest();
    }

    /// <summary>共有 CSS（<c>style.css</c>）を書き出し、改訂追記・保存する</summary>
    /// <param name="css">CSS 全体</param>
    /// <param name="revisionNote">改訂メモ</param>
    /// <returns>機械検証の警告一覧（保存は拒否しない）</returns>
    public IReadOnlyList<string> SaveStylesheet(string css, string revisionNote)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(
            Path.Combine(_folder, MockManifest.StylesheetFileName),
            css ?? string.Empty,
            Utf8NoBom
        );

        AppendRevision(
            string.IsNullOrWhiteSpace(revisionNote) ? "Saved shared stylesheet." : revisionNote
        );
        SaveManifest();

        return MockContentValidator.ValidateStylesheet(css ?? string.Empty);
    }

    /// <summary>画面 HTML を読む（未存在なら null）</summary>
    /// <param name="file">画面ファイル名</param>
    public string? GetScreenHtml(string file)
    {
        ValidateScreenFileName(file);

        var path = Path.Combine(_folder, file);

        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    /// <summary>共有 CSS が存在するか</summary>
    public bool HasStylesheet =>
        File.Exists(Path.Combine(_folder, MockManifest.StylesheetFileName));

    /// <summary>共有 CSS を読む（未存在なら null）</summary>
    public string? GetStylesheet()
    {
        var path = Path.Combine(_folder, MockManifest.StylesheetFileName);

        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    /// <summary>再開時などに、スキーマスナップショットを現在図の内容へ更新して保存する</summary>
    /// <param name="schema">最新の ER スキーマ記述テキスト</param>
    public void UpdateSourceSchema(string schema)
    {
        _manifest.SourceSchema = schema ?? string.Empty;
        SaveManifest();
    }

    /// <summary>フォルダ内の実 HTML ファイル ∪ マニフェスト宣言画面を既知画面集合として集める</summary>
    private IReadOnlyCollection<string> CollectKnownScreenFiles()
    {
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (Directory.Exists(_folder))
        {
            foreach (var path in Directory.EnumerateFiles(_folder, "*.html"))
            {
                known.Add(Path.GetFileName(path));
            }
        }

        foreach (var screen in _manifest.Screens)
        {
            if (!string.IsNullOrWhiteSpace(screen.File))
            {
                known.Add(screen.File);
            }
        }

        return known;
    }

    /// <summary>
    /// upsert 意味論に従って画面の entities を解決する。
    /// <paramref name="incoming"/> が <c>null</c> なら現状（<paramref name="current"/>）を維持し、
    /// 空なら消去（<c>null</c>）・非空なら正規化した結果で置換する（全置換で空になる場合も <c>null</c>）。
    /// </summary>
    /// <param name="current">画面が現在保持している宣言</param>
    /// <param name="incoming">保存呼び出しで渡された宣言（null＝未指定）</param>
    private static List<MockScreenEntity>? ResolveEntities(
        List<MockScreenEntity>? current,
        IReadOnlyList<MockScreenEntity>? incoming
    )
    {
        if (incoming is null)
        {
            return current;
        }

        var normalized = NormalizeEntities(incoming).Entities;

        // 宣言が無くなった（消去・全破棄）ときは null で保持し、mock.json に entities キーを残さない
        return normalized.Count == 0 ? null : normalized.ToList();
    }

    /// <summary>
    /// CRUD 操作文字列を正規化する。大文字化し、C/R/U/D 以外を除去・重複除去して C→R→U→D の正順へ並べ替える。
    /// </summary>
    /// <param name="operations">生の操作文字列（例 <c>"urc"</c>・<c>"CRUDX"</c>）</param>
    /// <returns>正規化済み文字列（例 <c>"CRU"</c>）。有効文字が無ければ空文字</returns>
    public static string NormalizeOperations(string? operations)
    {
        if (string.IsNullOrEmpty(operations))
        {
            return string.Empty;
        }

        // 正順の並び。含まれるかを固定順で走査するため位置索引として使う
        const string order = "CRUD";
        var present = new bool[order.Length];

        foreach (var ch in operations)
        {
            var index = order.IndexOf(char.ToUpperInvariant(ch));

            if (index >= 0)
            {
                present[index] = true;
            }
        }

        var builder = new StringBuilder(order.Length);

        for (var i = 0; i < order.Length; i++)
        {
            if (present[i])
            {
                builder.Append(order[i]);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// エンティティ宣言一覧を正規化する。各エントリの名前を Trim し、名前が空のものは黙って除外、
    /// 操作を <see cref="NormalizeOperations"/> で正規化する。正規化後に操作が空になったエントリは
    /// 破棄し、その名前を <see cref="MockScreenEntityNormalization.DiscardedNames"/> に残す。
    /// </summary>
    /// <param name="entities">正規化対象の宣言一覧（null は空として扱う）</param>
    /// <returns>正規化済みの宣言一覧と、操作が空で破棄された名前の一覧</returns>
    public static MockScreenEntityNormalization NormalizeEntities(
        IReadOnlyList<MockScreenEntity>? entities
    )
    {
        var normalized = new List<MockScreenEntity>();
        var discarded = new List<string>();

        if (entities is null)
        {
            return new MockScreenEntityNormalization(normalized, discarded);
        }

        foreach (var entity in entities)
        {
            var entityName = entity?.Name?.Trim() ?? string.Empty;

            // 名前が無いものは同定できないため黙って除外する（壊れた要素の警告は呼び出し側の責務）
            if (string.IsNullOrEmpty(entityName))
            {
                continue;
            }

            var operations = NormalizeOperations(entity!.Operations);

            // 有効な CRUD 文字が 1 つも無いエントリは破棄して名前を控える（警告用）
            if (operations.Length == 0)
            {
                discarded.Add(entityName);
                continue;
            }

            normalized.Add(new MockScreenEntity { Name = entityName, Operations = operations });
        }

        return new MockScreenEntityNormalization(normalized, discarded);
    }

    /// <summary>改訂履歴へ 1 件追記する</summary>
    private void AppendRevision(string note)
    {
        _manifest.Revisions.Add(
            new MockRevision { Timestamp = _clock(), Note = note ?? string.Empty }
        );
    }

    /// <summary>マニフェストを <c>mock.json</c> へ BOM なし UTF-8 で書き出す</summary>
    private void SaveManifest()
    {
        var json = JsonSerializer.Serialize(_manifest, MockManifest.SerializerOptions);
        File.WriteAllText(Path.Combine(_folder, MockManifest.ManifestFileName), json, Utf8NoBom);
    }

    /// <summary>マニフェストのスナップショット複製を JSON ラウンドトリップで作る</summary>
    private static MockManifest CloneManifest(MockManifest source)
    {
        var json = JsonSerializer.Serialize(source, MockManifest.SerializerOptions);

        return JsonSerializer.Deserialize<MockManifest>(json, MockManifest.SerializerOptions)
            ?? new MockManifest();
    }

    /// <summary>画面ファイル名の妥当性を検証する（フォルダ直下・<c>.html</c>・パス区切りや <c>".."</c> 不可）</summary>
    /// <remarks>例外文言はツール結果として AI へ返る機械向けメッセージのため英語で固定する。</remarks>
    private static void ValidateScreenFileName(string file)
    {
        if (string.IsNullOrWhiteSpace(file))
        {
            throw new ArgumentException("The screen file name is empty.", nameof(file));
        }

        if (
            file.Contains('/', StringComparison.Ordinal)
            || file.Contains('\\', StringComparison.Ordinal)
            || file.Contains("..", StringComparison.Ordinal)
        )
        {
            throw new ArgumentException(
                $"The screen file name must not contain path separators or '..': {file}",
                nameof(file)
            );
        }

        if (file.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException(
                $"The screen file name contains characters that cannot be used: {file}",
                nameof(file)
            );
        }

        if (!file.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"The screen file name must end with .html: {file}",
                nameof(file)
            );
        }
    }
}

/// <summary>
/// <see cref="MockFolderStore.NormalizeEntities"/> の結果。正規化済みの宣言一覧と、
/// 操作が空で破棄された名前の一覧（警告文言の材料）を持つ。
/// </summary>
/// <param name="Entities">正規化済みのエンティティ宣言一覧</param>
/// <param name="DiscardedNames">正規化で操作が空になり破棄されたエンティティ名の一覧</param>
public sealed record MockScreenEntityNormalization(
    IReadOnlyList<MockScreenEntity> Entities,
    IReadOnlyList<string> DiscardedNames
);
