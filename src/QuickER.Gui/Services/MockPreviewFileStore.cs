using System.IO;
using System.Text;

namespace QuickER.Services;

/// <summary>
/// モックプレビュー用の HTML を一時ファイルへ UTF-8 で書き出し、<c>file:///</c> URI を生成する小さなストア。
/// </summary>
/// <remarks>
/// <see cref="Microsoft.Web.WebView2"/> の <c>NavigateToString</c> は約 2MB の上限があるため、
/// 大きな HTML でも扱えるよう一時ファイル経由（<c>file:///</c> Navigate）でプレビューする。
/// 書き出し・URI 生成のロジックは WebView2 に依存せずテスト可能な形（本クラス）へ切り出している。
/// </remarks>
public sealed class MockPreviewFileStore
{
    /// <summary>一時ファイルの置き場（<c>%TEMP%\QuickER\mock-preview</c> 配下）</summary>
    private readonly string _folder;

    /// <summary>版ごとにファイル名を変えるための連番（ブラウザキャッシュ回避）</summary>
    private int _revision;

    /// <summary>既定の一時フォルダ（<c>%TEMP%\QuickER\mock-preview</c>）でストアを生成する</summary>
    public MockPreviewFileStore()
        : this(Path.Combine(Path.GetTempPath(), "QuickER", "mock-preview")) { }

    /// <summary>保存先フォルダを指定してストアを生成する（テスト用）</summary>
    /// <param name="folder">HTML を書き出すフォルダ</param>
    public MockPreviewFileStore(string folder)
    {
        _folder = folder;
    }

    /// <summary>書き出し先フォルダのフルパス</summary>
    public string Folder => _folder;

    /// <summary>
    /// HTML を新しい一時ファイルへ UTF-8（BOM なし）で書き出し、そのファイルの <c>file:///</c> URI を返す。
    /// </summary>
    /// <param name="html">書き出す HTML 全体</param>
    /// <returns>WebView2 に Navigate させる <c>file:///</c> 絶対 URI</returns>
    public Uri Write(string html)
    {
        Directory.CreateDirectory(_folder);

        // 版ごとに別名（mock-000001.html …）にして、同一 URI の再ナビゲートによるキャッシュ表示を避ける
        var fileName = $"mock-{++_revision:D6}.html";
        var path = Path.Combine(_folder, fileName);
        File.WriteAllText(path, html, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        return new Uri(path);
    }

    /// <summary>書き出した一時ファイルをすべて削除する（ベストエフォート）</summary>
    public void Clear()
    {
        try
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, recursive: true);
            }
        }
        catch (IOException)
        {
            // 使用中などで削除できない場合は無視する
        }
        catch (UnauthorizedAccessException)
        {
            // 権限不足は無視する
        }
    }
}
