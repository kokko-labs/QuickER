namespace QuickER.AI.Mock;

/// <summary>
/// モック生成の進捗（AI の応答の断片と、実行器・ランナーが出す行）を転送先へ書く。
/// </summary>
/// <remarks>
/// 応答の断片は改行で終わるとは限らないので、最後に書いた文字が改行かを覚え、行の途中で行を書くときは
/// 先に改行を 1 つ入れる（ログで行が AI の文章の末尾へ続かないようにする）。
/// </remarks>
internal sealed class MockProgressWriter(Action<string> sink)
{
    /// <summary>次に書く位置が行の先頭か（まだ何も書いていなければ先頭）</summary>
    private bool _atLineStart = true;

    /// <summary>断片をそのまま書く</summary>
    public void Write(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        sink(text);
        _atLineStart = text[^1] == '\n';
    }

    /// <summary>1 行を書く（行の途中なら先に改行を入れ、末尾に改行を付ける）</summary>
    public void WriteLine(string line)
    {
        Write((_atLineStart ? string.Empty : "\n") + line + "\n");
    }
}
