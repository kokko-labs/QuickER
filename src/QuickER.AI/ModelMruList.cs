namespace QuickER.AI;

/// <summary>
/// モデル名の MRU（最近使った順）リスト操作の共通ヘルパ。
/// <see cref="ProviderModelHistory"/> がプロバイダ別リストへ同一規則
/// （Trim・OrdinalIgnoreCase 重複排除・先頭挿入・上限切詰）を適用するために使う。
/// </summary>
internal static class ModelMruList
{
    /// <summary>保持する履歴の上限件数（超過分は古いものから切り詰める）</summary>
    public const int MaxEntries = 20;

    /// <summary>
    /// モデル名を最新として記録する。Trim 後に先頭挿入し、<see cref="StringComparer.OrdinalIgnoreCase"/> で
    /// 重複を排除（新しい表記を採用）、上限を超えたら末尾から切り詰める。
    /// </summary>
    /// <param name="models">MRU 順（先頭が最新）のモデル名リスト</param>
    /// <param name="model">記録するモデル名（前後空白は除去する）</param>
    /// <returns>リストが変化した場合 true（保存の要否判定に使う）。空白のみ・変化なしなら false</returns>
    public static bool Touch(List<string> models, string model)
    {
        var trimmed = model?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(trimmed))
        {
            return false;
        }

        // 既に先頭が同一表記（リストは重複を持たない不変条件のため、これで変化なしと判定できる）なら保存不要
        if (models.Count > 0 && models[0] == trimmed)
        {
            return false;
        }

        // 既存の同一表記（大文字小文字問わず）を除去してから先頭へ挿入する
        models.RemoveAll(m => string.Equals(m, trimmed, StringComparison.OrdinalIgnoreCase));
        models.Insert(0, trimmed);

        // 上限超過分は末尾（最も古い）から切り詰める
        if (models.Count > MaxEntries)
        {
            models.RemoveRange(MaxEntries, models.Count - MaxEntries);
        }

        return true;
    }

    /// <summary>
    /// 指定モデル名をリストから削除する（<see cref="StringComparer.OrdinalIgnoreCase"/> 一致）。
    /// </summary>
    /// <param name="models">MRU 順のモデル名リスト</param>
    /// <param name="model">削除するモデル名</param>
    /// <returns>1 件以上削除した場合 true。見つからなければ false</returns>
    public static bool Remove(List<string> models, string model)
    {
        var trimmed = model?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(trimmed))
        {
            return false;
        }

        var removed = models.RemoveAll(m =>
            string.Equals(m, trimmed, StringComparison.OrdinalIgnoreCase)
        );

        return removed > 0;
    }
}
