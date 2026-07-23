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
    /// <param name="file">画面ファイル名（フォルダ直下・<c>.html</c>・パス区切りや <c>".."</c> 不可）</param>
    /// <param name="name">画面の表示名</param>
    /// <param name="description">画面の役割説明</param>
    /// <param name="html">画面 HTML 全体（空・<c>&lt;html</c> を含まない場合は拒否）</param>
    /// <param name="transitions">この画面を起点とする遷移（既存の同起点遷移を差し替える）</param>
    /// <param name="revisionNote">改訂メモ</param>
    /// <returns>機械検証の警告一覧（保存は拒否しない）</returns>
    public IReadOnlyList<string> SaveScreen(
        string file,
        string name,
        string description,
        string html,
        IReadOnlyList<MockTransition> transitions,
        string revisionNote
    )
    {
        ValidateScreenFileName(file);

        if (string.IsNullOrWhiteSpace(html))
        {
            throw new ArgumentException(
                "html が空です。完全な HTML 全体を指定してください。",
                nameof(html)
            );
        }

        if (!html.Contains("<html", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "HTML として不完全です。<html> を含む単一ファイルの完全な HTML を指定してください。",
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
            _manifest.Screens.Add(
                new MockScreen
                {
                    File = file,
                    Name = name ?? string.Empty,
                    Description = description ?? string.Empty,
                }
            );
        }
        else
        {
            existing.File = file;
            existing.Name = name ?? string.Empty;
            existing.Description = description ?? string.Empty;
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
    private static void ValidateScreenFileName(string file)
    {
        if (string.IsNullOrWhiteSpace(file))
        {
            throw new ArgumentException("画面ファイル名が空です。", nameof(file));
        }

        if (
            file.Contains('/', StringComparison.Ordinal)
            || file.Contains('\\', StringComparison.Ordinal)
            || file.Contains("..", StringComparison.Ordinal)
        )
        {
            throw new ArgumentException(
                $"画面ファイル名にパス区切りや '..' を含めることはできません: {file}",
                nameof(file)
            );
        }

        if (file.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException(
                $"画面ファイル名に使用できない文字が含まれます: {file}",
                nameof(file)
            );
        }

        if (!file.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"画面ファイル名は .html で終わる必要があります: {file}",
                nameof(file)
            );
        }
    }
}
