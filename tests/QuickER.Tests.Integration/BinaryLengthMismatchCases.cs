using System;
using System.Collections.Generic;
using System.IO;

namespace QuickER.Tests.Integration;

/// <summary>
/// バイナリ列の Stream 書き込みで「Stream が length どおりのバイト数を出さない」3 通りの入力。
/// 直結（SQL Server・SQLite）とインメモリのテストが同じ入力を使い、どのバックエンドでも
/// ArgumentException（ParamName=length）で失敗して既存の blob を変えないことを確かめる。
/// </summary>
internal static class BinaryLengthMismatchCases
{
    /// <summary>(ラベル, 書き込む Stream, 渡す length) を返す（呼ぶたびに新しい Stream を作る）。</summary>
    public static IEnumerable<(string Label, Stream Source, long Length)> Create()
    {
        // シーク可能な Stream に、残りのバイト数と違う length を渡す（従来は length を黙って無視していた）
        yield return ("シーク可能で length が残りと違う", new MemoryStream(new byte[10]), 5);

        // シークできない Stream が length より先に終わる（SQLite は末尾を 0 で埋めて成功していた）
        yield return ("非シークで length より短い", new NonSeekableStream([7, 7, 7, 7]), 10);

        // シークできない Stream が length を超えて続く（SQL Server は Stream の終わりまで保存していた）
        yield return ("非シークで length より長い", new NonSeekableStream(new byte[20]), 10);
    }

    /// <summary>CanSeek を持たない読み取り専用ストリーム。</summary>
    private sealed class NonSeekableStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            _inner.Read(buffer, offset, count);

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
