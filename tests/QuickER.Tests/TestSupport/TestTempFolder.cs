using System.IO;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// テストが使う一時フォルダを <c>%TEMP%\QuickERTests\&lt;guid&gt;</c> に作り、破棄で中身ごと消す
/// </summary>
/// <remarks>
/// TEMP 直下に同じ前置きの名前を大量に並べると 8.3 の短い名前がぶつかり、フォルダ 1 つの作成に数百 ms かかる。
/// そのため直下には固定名の親 <c>QuickERTests</c> だけを置き、子の名前は前置きの無い guid にする（先頭がばらつく）。
/// 消すのはベストエフォートで、ファイルを掴まれていて消せないときは黙って残す
/// （掴んでいるもの＝SQLite の接続のプールなどは、作ったテストの側で放す）。
/// 親の名前を書くのはこのファイルだけにする（ソースの規則 <c>test-temp-folder-root-copies</c>）。
/// テストが一時フォルダを自分で組むと、消し忘れ・失敗の経路での消し損ねを写すことになる。
/// </remarks>
public sealed class TestTempFolder : IDisposable
{
    /// <summary>一時フォルダを作る</summary>
    public TestTempFolder()
    {
        Path = System.IO.Path.Combine(Root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    /// <summary>一時フォルダの親（<c>%TEMP%\QuickERTests</c>）</summary>
    /// <remarks>
    /// 作らないパス（存在の判定を差し込む偽のパス）の置き場にだけ使う。作るフォルダは必ずインスタンスで作り、破棄で消す。
    /// </remarks>
    public static string Root =>
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "QuickERTests");

    /// <summary>一時フォルダのフルパス</summary>
    public string Path { get; }

    /// <summary>一時フォルダを中身ごと消す（消せなければ残す）</summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 後始末はベストエフォート（無いフォルダの DirectoryNotFoundException も IOException に含まれる）
        }
    }
}
