using System.Collections.Generic;

namespace QuickER.Provider;

/// <summary>スキーマ同期スクリプトの 1 バッチ分の実行結果</summary>
public sealed record SchemaSyncBatchResult(int Index, string Sql, bool Success, string? Error);

/// <summary>スキーマ同期スクリプト全体の実行結果サマリ</summary>
public sealed class SchemaSyncResult
{
    /// <summary>各バッチの実行結果</summary>
    public List<SchemaSyncBatchResult> Batches { get; } = new();

    /// <summary>全バッチ成功で COMMIT したかどうか</summary>
    public bool Committed { get; set; }

    /// <summary>失敗時のエラーメッセージ</summary>
    public string? Error { get; set; }
}
