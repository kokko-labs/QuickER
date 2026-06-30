using QuickER.Provider;
using QuickER.SqlServer;

namespace QuickER.Cli;

/// <summary><c>--provider</c> 名から <see cref="IDatabaseProvider"/> 実装を解決するレジストリ</summary>
/// <remarks>当面 SQL Server のみ。新 DBMS 対応時はここへ 1 行追加する</remarks>
internal static class ProviderRegistry
{
    /// <summary>プロバイダ名を解決する。未対応の場合は例外を投げる</summary>
    public static IDatabaseProvider Resolve(string name)
    {
        return name?.Trim().ToLowerInvariant() switch
        {
            SqlServerProvider.ProviderName => new SqlServerProvider(),
            _ => throw new ArgumentException(
                $"未対応のプロバイダ: '{name}'。対応プロバイダ: {SqlServerProvider.ProviderName}"
            ),
        };
    }
}
