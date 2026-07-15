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
