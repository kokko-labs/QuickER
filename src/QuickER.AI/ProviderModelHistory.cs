namespace QuickER.AI;

/// <summary>
/// プロバイダ別のモデル名 MRU（最近使った順）履歴。AI チャット／AI モックの両ダイアログで共有し、
/// チャットのターンが成功したときに使用モデルを記録して、次回からドロップダウン候補に出す。
/// API キー接続（キーは "openai" / "claude" / "localllm"）と Codex 接続（キーは config.toml の
/// プロバイダ名）が、<see cref="AiSettings"/> の別セクション（<see cref="AiSettings.ApiModelHistory"/> /
/// <see cref="AiSettings.CodexModelHistory"/>）で同じ形式を使う。
/// </summary>
public class ProviderModelHistory
{
    /// <summary>1 プロバイダあたりの履歴の上限件数（超過分は古いものから切り詰める）</summary>
    public const int MaxEntries = ModelMruList.MaxEntries;

    /// <summary>プロバイダ別の MRU 履歴（キーは正規化済みプロバイダ名・値は先頭が最新のモデル名一覧）</summary>
    public Dictionary<string, List<string>> Providers { get; set; } = new();

    /// <summary>プロバイダ名を辞書キーへ正規化する（Trim + 小文字化。表記ゆれで履歴が割れないようにする）</summary>
    private static string NormalizeKey(string provider) =>
        provider?.Trim().ToLowerInvariant() ?? string.Empty;

    /// <summary>指定プロバイダの MRU 履歴を取得する（無ければ空）</summary>
    /// <param name="provider">プロバイダ名（正規化して照合する）</param>
    public IReadOnlyList<string> ModelsFor(string provider) =>
        Providers.TryGetValue(NormalizeKey(provider), out var models) ? models : [];

    /// <summary>
    /// 指定プロバイダの履歴へモデル名を最新として記録する
    /// （MRU 規則は <see cref="ModelMruList.Touch"/> と同一。リストが無ければ自動作成する）。
    /// </summary>
    /// <param name="provider">プロバイダ名</param>
    /// <param name="model">記録するモデル名</param>
    /// <returns>履歴が変化した場合 true（保存の要否判定に使う）</returns>
    public bool Touch(string provider, string model)
    {
        var key = NormalizeKey(provider);

        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        if (!Providers.TryGetValue(key, out var models))
        {
            models = new List<string>();
            Providers[key] = models;
        }

        var changed = ModelMruList.Touch(models, model);

        // 空白モデル等で結局空のままなら、作ったばかりの空リストを残さない
        if (models.Count == 0)
        {
            Providers.Remove(key);
        }

        return changed;
    }

    /// <summary>
    /// 指定プロバイダの履歴からモデル名を削除する（空になったらプロバイダのキーごと削除する）。
    /// </summary>
    /// <param name="provider">プロバイダ名</param>
    /// <param name="model">削除するモデル名</param>
    /// <returns>1 件以上削除した場合 true。見つからなければ false</returns>
    public bool Remove(string provider, string model)
    {
        var key = NormalizeKey(provider);

        if (!Providers.TryGetValue(key, out var models))
        {
            return false;
        }

        var removed = ModelMruList.Remove(models, model);

        if (models.Count == 0)
        {
            Providers.Remove(key);
        }

        return removed;
    }
}
