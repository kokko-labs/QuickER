namespace QuickER.AI.UI;

/// <summary>チャットの回答の中のリンクを開く口（テストで差し替えるため、開く経路をここ 1 本に限る）</summary>
public interface IChatLinkOpener
{
    /// <summary>リンクを開く</summary>
    /// <param name="uri"><see cref="ChatLinkPolicy"/> で検証済みの URL</param>
    void Open(Uri uri);
}
