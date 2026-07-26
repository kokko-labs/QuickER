using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuickER.AI.Mock;

/// <summary>
/// モックフォルダのルートに置く <c>mock.json</c> のモデル。
/// 画面・遷移・改訂履歴と、元になった ER スキーマのスナップショットを保持する。
/// </summary>
/// <remarks>
/// フォルダレイアウト（フラット構成）:
/// <c>&lt;フォルダ&gt;/mock.json</c>・<c>&lt;フォルダ&gt;/*.html</c>（画面）・<c>&lt;フォルダ&gt;/style.css</c>（共有 CSS・固定名）。
/// 画面間リンクは相対 href（例 <c>href="OrderDetail.html"</c>）、CSS 参照は
/// <c>&lt;link rel="stylesheet" href="style.css"&gt;</c> を用いる規約。
/// </remarks>
public sealed class MockManifest
{
    /// <summary>マニフェストの現行フォーマット版。読み込み時にこれを超える版は新フォーマットとして拒否する</summary>
    public const int CurrentVersion = 1;

    /// <summary>共有 CSS の固定ファイル名</summary>
    public const string StylesheetFileName = "style.css";

    /// <summary>マニフェストファイル名</summary>
    public const string ManifestFileName = "mock.json";

    /// <summary>mock.json の読み書きに用いる共通のシリアライズ設定（camelCase・インデント・日本語を非エスケープ）</summary>
    internal static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // 日本語・記号を \uXXXX へ逃がさず可読な UTF-8 のまま書き出す
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>フォーマット版（現行 <see cref="CurrentVersion"/>）</summary>
    public int Version { get; set; } = CurrentVersion;

    /// <summary>モック全体の表題</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>元になった ER スキーマ記述テキストのスナップショット（<see cref="MockSchemaSerializer.Serialize"/> の結果）</summary>
    public string SourceSchema { get; set; } = string.Empty;

    /// <summary>画面一覧</summary>
    public List<MockScreen> Screens { get; set; } = new();

    /// <summary>画面間遷移の一覧</summary>
    public List<MockTransition> Transitions { get; set; } = new();

    /// <summary>改訂履歴</summary>
    public List<MockRevision> Revisions { get; set; } = new();
}

/// <summary>モックフォルダ内の 1 画面（フォルダ直下の HTML ファイル）</summary>
public sealed class MockScreen
{
    /// <summary>フォルダ直下の HTML ファイル名（例 <c>"OrderList.html"</c>）</summary>
    public string File { get; set; } = string.Empty;

    /// <summary>画面の表示名</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>画面の役割説明</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// この画面が扱うエンティティ（テーブル）と CRUD 操作の宣言一覧（画面×エンティティ連携）。
    /// </summary>
    /// <remarks>
    /// 追加的フィールド（フォーマット版は上げない）。宣言のない画面（古い <c>mock.json</c> を含む）は
    /// null＝<c>entities</c> キーを書き出さない（<see cref="JsonIgnoreAttribute"/>＝WhenWritingNull）。
    /// AI しか知らない「画面ごとの CRUD の使い方」を save_screen の申告で記録する布石で、
    /// 設計書の CRUD 表レンダリングは後段（ステージ 2）で機械的に行う。
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<MockScreenEntity>? Entities { get; set; }
}

/// <summary>画面が扱う 1 エンティティ（テーブル）とその CRUD 操作の宣言</summary>
public sealed class MockScreenEntity
{
    /// <summary>エンティティ（テーブル）名。ER 図のテーブル名と照合する（大文字小文字無視）</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// この画面がエンティティに対して行う CRUD 操作。C/R/U/D の部分集合を C→R→U→D の正順で並べた文字列
    /// （例 <c>"CRU"</c>）。正規化は <see cref="MockFolderStore.NormalizeOperations"/> が担う。
    /// </summary>
    public string Operations { get; set; } = string.Empty;
}

/// <summary>画面から画面への遷移</summary>
public sealed class MockTransition
{
    /// <summary>遷移元の画面ファイル名</summary>
    public string From { get; set; } = string.Empty;

    /// <summary>遷移先の画面ファイル名</summary>
    public string To { get; set; } = string.Empty;

    /// <summary>遷移トリガーの説明（例 <c>"行クリック"</c>）</summary>
    public string Trigger { get; set; } = string.Empty;
}

/// <summary>改訂履歴の 1 エントリ</summary>
public sealed class MockRevision
{
    /// <summary>改訂日時</summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>変更点のメモ</summary>
    public string Note { get; set; } = string.Empty;
}
