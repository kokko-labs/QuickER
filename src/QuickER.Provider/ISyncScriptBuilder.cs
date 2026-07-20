namespace QuickER.Provider;

/// <summary>同期スクリプト生成（DB 方言ごとに実装）</summary>
public interface ISyncScriptBuilder
{
    /// <summary><see cref="SyncPlanner"/> が組み立てた実行計画から対象方言の同期スクリプトを生成する</summary>
    string Build(SyncPlan plan);
}
