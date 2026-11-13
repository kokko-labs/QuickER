using System.Runtime.InteropServices;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// このスレッドのキーの状態の表（Win32 の <c>GetKeyboardState</c> / <c>SetKeyboardState</c>）のマウスのボタン
/// （左・右・中・X1・X2）だけを読み書きする。テストが合成の入力を流す間、WPF のコントロールが読む実際のボタンを
/// 「離れている」に固定して、利用者がほかのアプリで押したボタンを混ぜないために使う。
/// </summary>
/// <remarks>
/// <para>
/// WPF のコントロール（<c>ButtonBase</c> の Space の解放・<c>DataGrid</c> のドラッグ選択など）は、ビヘイビアと違って
/// テストの差し替え（<c>BehaviorInput</c>）を通らず、マウスのボタンをこの表（<c>GetKeyState</c>）から読む。全件の実行中に
/// 利用者がほかのアプリでクリックすると、その押下が表に入り、合成の入力の結果が変わる（入力の列のエンジンが全件でだけ
/// 落ちた原因）。人が触らない状態（表のボタンが離れている）が、エンジンのいつもの前提。
/// </para>
/// <para>
/// 書き換えるのはボタンの 5 つだけで、ほかのキー（修飾キー・トグル）には触らない。<see cref="Capture"/> で取った元の値へ
/// <see cref="Saved.Restore"/> で戻す。
/// </para>
/// </remarks>
internal static class MouseButtonKeyState
{
    /// <summary>マウスのボタンの仮想キーコード（左・右・中・X1・X2）</summary>
    internal static readonly int[] ButtonKeys = [0x01, 0x02, 0x04, 0x05, 0x06];

    /// <summary>表の大きさ（仮想キーコードの数）</summary>
    private const int TableSize = 256;

    /// <summary>このスレッドの表の、マウスのボタンを全部「離れている」にする（ほかのキーはそのまま）</summary>
    internal static void Release()
    {
        var table = Read();

        foreach (var key in ButtonKeys)
        {
            table[key] = 0;
        }

        Write(table);
    }

    /// <summary>このスレッドの表のマウスのボタンの値を取る（<see cref="Saved.Restore"/> で戻す）</summary>
    internal static Saved Capture()
    {
        var table = Read();
        return new Saved(ButtonKeys.Select(key => table[key]).ToArray());
    }

    /// <summary>このスレッドの表で、その仮想キーが押されているか（最上位ビット）</summary>
    internal static bool IsDown(int virtualKey) => (Read()[virtualKey] & 0x80) != 0;

    /// <summary>このスレッドの表の、その仮想キーの値を書く（自己検査が「押されている」を作るため）</summary>
    internal static void SetForTests(int virtualKey, bool down)
    {
        var table = Read();
        table[virtualKey] = down ? (byte)0x80 : (byte)0;
        Write(table);
    }

    /// <summary>取ったマウスのボタンの値</summary>
    internal sealed class Saved(byte[] values)
    {
        /// <summary>このスレッドの表のマウスのボタンを、取った値へ戻す（ほかのキーはそのまま）</summary>
        public void Restore()
        {
            var table = Read();

            for (var index = 0; index < ButtonKeys.Length; index++)
            {
                table[ButtonKeys[index]] = values[index];
            }

            Write(table);
        }
    }

    private static byte[] Read()
    {
        var table = new byte[TableSize];

        if (!GetKeyboardState(table))
        {
            throw new InvalidOperationException(
                $"キーの状態の表を読めない（Win32 エラー {Marshal.GetLastWin32Error()}）"
            );
        }

        return table;
    }

    private static void Write(byte[] table)
    {
        if (!SetKeyboardState(table))
        {
            throw new InvalidOperationException(
                $"キーの状態の表を書けない（Win32 エラー {Marshal.GetLastWin32Error()}）"
            );
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetKeyboardState(byte[] state);

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetKeyboardState(byte[] state);
}
