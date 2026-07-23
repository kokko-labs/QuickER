using QuickER.Settings;

namespace QuickER.Services;

/// <summary>GUI アプリ全体の設定（表示言語など、次回起動時に復元する永続化対象）</summary>
public class GuiAppSettings
{
    /// <summary>
    /// 表示言語コード（<c>"ja"</c> / <c>"en"</c>。<c>null</c>＝未設定で OS 言語から導出する）。
    /// 切替は再起動反映方式のため、この値は起動最初期のカルチャ適用でのみ参照する。
    /// </summary>
    public string? Language { get; set; }

    /// <summary>ダイアグラム上の表示トグル（自動保存で書き込まれ、次回起動時に復元する）</summary>
    public DiagramViewSettings DiagramView { get; set; } = new();

    /// <summary>
    /// 現在編集中の文書メタ（紐付くファイルパス・最終既知ファイルハッシュ・ダーティ状態）。
    /// 自動保存の作業状態（last_diagram.json）とは別に、どのファイルに紐付いているかを記録する。
    /// </summary>
    public CurrentDocumentSettings CurrentDocument { get; set; } = new();
}

/// <summary>現在編集中の文書がどのファイルに紐付いているかを表す復元メタ（自動保存対象）</summary>
/// <remarks>
/// 作業状態そのもの（意味モデル＋レイアウト）は last_diagram.json 側が持つ。ここはファイルとの
/// 対応関係のみを保持する。<see cref="LastKnownHash"/> は最後に読込／上書き保存した時点のファイル内容の
/// SHA-256（16 進文字列）で、外部変更検知（ステージ B）で現ファイルとの一致判定に用いる。
/// </remarks>
public class CurrentDocumentSettings
{
    /// <summary>紐付くファイルのフルパス（未保存＝無題のときは null）</summary>
    public string? FilePath { get; set; }

    /// <summary>最後に読込／上書き保存した時点のファイル内容の SHA-256（16 進・未保存時は null）</summary>
    public string? LastKnownHash { get; set; }

    /// <summary>最終読込／上書き保存以降に未保存の変更があるか（次回起動時にタイトルの * 表示へ反映する）</summary>
    public bool IsDirty { get; set; }
}

/// <summary>ダイアグラム上の表示トグル（次回起動時に復元する自動保存対象）</summary>
public class DiagramViewSettings
{
    /// <summary>ダイアグラム上にカラム説明を表示するかどうか</summary>
    public bool ShowColumnDescriptions { get; set; }

    /// <summary>ダイアグラム上に NULL 許容を表示するかどうか</summary>
    public bool ShowNullability { get; set; } = true;

    /// <summary>ダイアグラム上で簡易表示（PK/FK カラムのみ）を行うかどうか</summary>
    public bool IsCompactView { get; set; }
}

/// <summary>
/// GUI アプリ設定を JSON ファイル（%APPDATA%\QuickER\gui-settings.json）へ保存・読込するストア。
/// GUI 全体の設定と UI 状態を 1 ファイルへ集約し、書き込みは Load → 該当セクションのみ変更 → Save の
/// read-modify-write で行う（他のセクションを消さないため）。
/// </summary>
public class GuiAppSettingsStore : JsonSettingsStore<GuiAppSettings>
{
    /// <summary>既定の保存ファイル名</summary>
    public const string DefaultFileName = "gui-settings.json";

    /// <summary>既定の保存先（%APPDATA%\QuickER）で設定ストアを生成する</summary>
    public GuiAppSettingsStore()
        : base(DefaultFileName) { }

    /// <summary>保存先フォルダを指定して設定ストアを生成する（テスト用）</summary>
    public GuiAppSettingsStore(string folder)
        : base(DefaultFileName, folder) { }
}
