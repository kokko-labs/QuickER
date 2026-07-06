using System.IO;
using System.Windows.Media.Imaging;

namespace QuickER.Services.Chat;

/// <summary>
/// WPF の画像 API（<see cref="BitmapSource"/>）で画像を縮小する
/// <see cref="QuickER.AI.ChatAttachmentFactory.ImageShrinker"/> の実装。
/// </summary>
/// <remarks>
/// AI 層（QuickER.AI）は WPF 非依存のため縮小の実体を持たず、差し込み口（デリゲート）だけを備える。
/// ここでは元画像をデコードし、長辺を段階的に縮めながら PNG で再エンコードして上限内に収める。
/// GIF/WebP を含む WPF がデコードできる形式を PNG へ正規化するため、出力の MIME は常に image/png になる。
/// </remarks>
public static class WpfImageShrinker
{
    /// <summary>縮小後に必ず PNG へ正規化するため、呼び出し側へ返す MIME タイプ</summary>
    public const string OutputMediaType = "image/png";

    /// <summary>縮小を試みる長辺の候補（大きい順に試し、上限内に収まった時点で採用する）</summary>
    private static readonly int[] MaxEdgeCandidates = [2048, 1600, 1280, 1024, 800, 640];

    /// <summary>
    /// <see cref="QuickER.AI.ChatAttachmentFactory.ImageShrinker"/> シグネチャの縮小デリゲート。
    /// 上限（5MB）内に収まる PNG バイト列を返す。デコード不能・どの候補でも収まらないときは null を返す。
    /// </summary>
    /// <param name="data">元画像のバイト列</param>
    /// <param name="mediaType">元画像の MIME タイプ（判定には使わない・呼び出し側の互換のため受け取る）</param>
    public static byte[]? Shrink(byte[] data, string mediaType)
    {
        BitmapSource source;

        try
        {
            source = Decode(data);
        }
        catch (Exception)
        {
            // WPF がデコードできない形式（破損・未対応の WebP 亜種など）は縮小不能として null を返す
            return null;
        }

        // 長辺を段階的に縮めながら PNG で再エンコードし、上限内に収まった最初の候補を採用する
        foreach (var maxEdge in MaxEdgeCandidates)
        {
            var scaled = ScaleToMaxEdge(source, maxEdge);
            var encoded = EncodePng(scaled);

            if (encoded.LongLength <= QuickER.AI.ChatAttachmentLimits.MaxImageBytes)
            {
                return encoded;
            }
        }

        return null;
    }

    /// <summary>バイト列を <see cref="BitmapSource"/> へデコードする（ストリーム離脱後も使えるよう即時フレーム化）</summary>
    private static BitmapSource Decode(byte[] data)
    {
        using var stream = new MemoryStream(data);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad
        );

        return decoder.Frames[0];
    }

    /// <summary>長辺が指定値以下になるよう等倍縮小する（元が既に小さければそのまま返す）</summary>
    private static BitmapSource ScaleToMaxEdge(BitmapSource source, int maxEdge)
    {
        var longEdge = Math.Max(source.PixelWidth, source.PixelHeight);

        if (longEdge <= maxEdge)
        {
            return source;
        }

        var scale = (double)maxEdge / longEdge;
        var scaled = new TransformedBitmap(
            source,
            new System.Windows.Media.ScaleTransform(scale, scale)
        );
        scaled.Freeze();

        return scaled;
    }

    /// <summary><see cref="BitmapSource"/> を PNG バイト列へエンコードする</summary>
    private static byte[] EncodePng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));

        using var stream = new MemoryStream();
        encoder.Save(stream);

        return stream.ToArray();
    }
}
