namespace ERDesigner.Services.Chat;

/// <summary>
/// 接続パネルの状態ドットが表す健全度。Codex / Claude 両エンジンで共通に用いる。
/// </summary>
public enum ConnectionHealth
{
    /// <summary>未確認・接続中など中立（灰）</summary>
    Pending,

    /// <summary>準備完了・送信可能（緑）</summary>
    Ready,

    /// <summary>未ログイン・未検出などユーザーの対応が必要（赤）</summary>
    NeedsAction,
}
