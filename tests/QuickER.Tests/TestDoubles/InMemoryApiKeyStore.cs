namespace QuickER.Tests.TestDoubles;

/// <summary>
/// API キーの保存・復元をメモリ上で行うテスト用ストア。
/// 実 <c>ApiKeyStore</c>（%APPDATA% 配下へ DPAPI 暗号化保存）へ触れずに VM の API キー seam を満たすために使う
/// （実ファイルを共有すると並列テストが IO 競合を起こすため）。
/// </summary>
/// <remarks>
/// <see cref="Load"/> / <see cref="Save"/> はメソッドグループのまま
/// <c>Func&lt;string, string?&gt;</c> / <c>Action&lt;string, string&gt;</c> の seam へ渡せるシグネチャにしてある。
/// 意味論は実ストアと同じで、空文字・null の保存は削除、未保存キーの読込は空文字を返す。
/// </remarks>
public sealed class InMemoryApiKeyStore
{
    private readonly Dictionary<string, string> _entries = new();

    /// <summary>保存済みのキー名と値（アサーション用）</summary>
    public IReadOnlyDictionary<string, string> Entries => _entries;

    /// <summary>API キーを名前付きで保存する。空文字・null なら該当エントリを削除する</summary>
    /// <param name="name">キーの識別名</param>
    /// <param name="value">保存する API キー</param>
    public void Save(string name, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            _entries.Remove(name);

            return;
        }

        _entries[name] = value;
    }

    /// <summary>名前付きで保存された API キーを返す</summary>
    /// <param name="name">キーの識別名</param>
    /// <returns>保存済みの API キー。未保存なら空文字（実ストアと同じ意味論）</returns>
    public string? Load(string name) =>
        _entries.TryGetValue(name, out var value) ? value : string.Empty;
}
