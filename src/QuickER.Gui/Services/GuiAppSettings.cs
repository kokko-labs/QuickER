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
}

/// <summary>GUI アプリ設定を JSON ファイルへ保存・読込するストア</summary>
public class GuiAppSettingsStore : JsonSettingsStore<GuiAppSettings>
{
    /// <summary>既定の保存先（%APPDATA%\QuickER）で設定ストアを生成する</summary>
    public GuiAppSettingsStore()
        : base("gui-app.json") { }

    /// <summary>保存先フォルダを指定して設定ストアを生成する（テスト用）</summary>
    public GuiAppSettingsStore(string folder)
        : base("gui-app.json", folder) { }
}
