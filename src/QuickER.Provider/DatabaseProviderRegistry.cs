using System.Collections.Generic;
using System.Linq;

namespace QuickER.Provider;

/// <summary>登録済みの <see cref="IDatabaseProvider"/> をプロバイダ名で解決するレジストリ</summary>
/// <remarks>名前の照合は大文字小文字を無視する 新 DBMS 対応時は実装を登録リストへ追加する</remarks>
public sealed class DatabaseProviderRegistry
{
    private readonly Dictionary<string, IDatabaseProvider> _byName;

    /// <summary>登録するプロバイダ群を受け取り、名前索引を構築する</summary>
    public DatabaseProviderRegistry(IEnumerable<IDatabaseProvider> providers)
    {
        _byName = providers.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>登録済みプロバイダの一覧</summary>
    public IReadOnlyCollection<IDatabaseProvider> All => _byName.Values;

    /// <summary>プロバイダ名で解決する。見つからなければ登録済み名を列挙した例外を投げる</summary>
    public IDatabaseProvider Get(string name)
    {
        if (TryGet(name, out var provider))
        {
            return provider;
        }

        var known = string.Join(", ", _byName.Keys.OrderBy(k => k));
        throw new ArgumentException($"未対応のプロバイダ: '{name}'。対応プロバイダ: {known}");
    }

    /// <summary>プロバイダ名で解決を試みる</summary>
    public bool TryGet(string name, out IDatabaseProvider provider)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            return _byName.TryGetValue(name.Trim(), out provider!);
        }

        provider = null!;
        return false;
    }
}
