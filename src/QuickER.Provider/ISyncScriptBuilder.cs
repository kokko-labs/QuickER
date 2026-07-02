using System.Collections.Generic;

namespace QuickER.Provider;

/// <summary>同期スクリプト生成（DB 方言ごとに実装）</summary>
public interface ISyncScriptBuilder
{
    /// <summary>選択された差分項目のみを対象方言の同期スクリプトへ変換する</summary>
    string Build(IEnumerable<SchemaDiffItem> items);
}
