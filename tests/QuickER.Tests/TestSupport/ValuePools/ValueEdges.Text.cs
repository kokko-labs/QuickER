using System.Linq;

namespace QuickER.Tests.TestSupport.ValuePools;

/// <summary>文字列・バイト列の水準</summary>
public static partial class ValueEdges
{
    /// <summary>長いバイト列の長さの下限</summary>
    public const int LongBinaryLength = 1024;

    private static EdgeRule[] StringRules =>
        [
            Edge<string>(
                "str-empty",
                ValueFamily.String,
                "空文字列（Oracle は NULL と同じに扱い、空かの判定が DATALENGTH・長さで分岐する）",
                v => v.Length == 0
            ),
            Edge<string>(
                "str-non-ascii",
                ValueFamily.String,
                "ASCII の外の文字を含む値（Unicode でない列で ? に化け、Unicode で束縛し直す）",
                v => v.Any(c => c > '\u007f')
            ),
            Edge<string>(
                "str-non-latin1",
                ValueFamily.String,
                "U+00FF を超える文字を含む値（Latin-1 の文字集合の列が持てない）",
                v => v.Any(c => c > 'ÿ')
            ),
            Edge<string>(
                "str-surrogate",
                ValueFamily.String,
                "サロゲートペアを含む値（長さの数え方・BMP の外の文字の扱いが分岐する）",
                v => v.Any(char.IsSurrogate)
            ),
            Edge<string>(
                "str-leading-space",
                ValueFamily.String,
                "空白で始まる値（前方一致と空白の扱いが分岐しうる）",
                v => v.StartsWith(' ')
            ),
            Edge<string>(
                "str-trailing-space",
                ValueFamily.String,
                "空白で終わる値（SQL Server の = と固定長の列は末尾の空白を無視する）",
                v => v.EndsWith(' ')
            ),
            Edge<string>(
                "str-other-whitespace",
                ValueFamily.String,
                "空白以外の空白類を含む値（タブ・改行・全角の空白。空白だけかの判定が割れうる）",
                v => v.Any(c => c != ' ' && char.IsWhiteSpace(c))
            ),
            Edge<string>(
                "str-like-wildcard",
                ValueFamily.String,
                "LIKE のワイルドカード（% と _）を含む値（文字列一致のパターンでエスケープが要る）",
                v => v.Contains('%') || v.Contains('_')
            ),
            Edge<string>(
                "str-quote",
                ValueFamily.String,
                "単引用符を含む値（SQL・DSL のリテラルで重ねて書く）",
                v => v.Contains('\'')
            ),
            ColumnEdge<string>(
                "str-beyond-column-length",
                ValueFamily.String,
                "列の長さを超える値（列の型で束縛すると切られるか、長さで束縛し直す）",
                column => column.Length is not null,
                (v, column) => v.Length > column.Length!.Value
            ),
            ColumnEdge<string>(
                "str-exact-column-length",
                ValueFamily.String,
                "列の長さにちょうど等しい値（長さの判定の境目・固定長の列が詰めない値）",
                column => column.Length is not null,
                (v, column) => v.Length == column.Length!.Value
            ),
        ];

    private static EdgeRule[] BinaryRules =>
        [
            Edge<byte[]>(
                "bin-empty",
                ValueFamily.Binary,
                "空のバイト列（Oracle は NULL と同じに扱い、長さの判定が分岐する）",
                v => v.Length == 0
            ),
            Edge<byte[]>(
                "bin-trailing-zero",
                ValueFamily.Binary,
                "末尾が 0x00 のバイト列（SQL Server は末尾の 0x00 だけ違う値を等しいとみなす）",
                v => v.Length >= 2 && v[^1] == 0
            ),
            Edge<byte[]>(
                "bin-leading-zero",
                ValueFamily.Binary,
                "先頭が 0x00 のバイト列（数として読む並び・16 進の書式が分岐しうる）",
                v => v.Length >= 2 && v[0] == 0
            ),
            ColumnEdge<byte[]>(
                "bin-beyond-column-length",
                ValueFamily.Binary,
                "列の長さを超えるバイト列（列の型で束縛すると切られる）",
                column => column.Length is not null,
                (v, column) => v.Length > column.Length!.Value
            ),
            ColumnEdge<byte[]>(
                "bin-exact-column-length",
                ValueFamily.Binary,
                "列の長さにちょうど等しいバイト列（固定長の列が詰めない値）",
                column => column.Length is not null,
                (v, column) => v.Length == column.Length!.Value
            ),
            ColumnEdge<byte[]>(
                "bin-zero-excess",
                ValueFamily.Binary,
                "列の長さを超え、超えた分が 0x00 だけのバイト列（保存したときの値へ直すと列に入る）",
                column => column.Length is not null,
                (v, column) =>
                    v.Length > column.Length!.Value && v.Skip(column.Length.Value).All(b => b == 0)
            ),
            Edge<byte[]>(
                "bin-long",
                ValueFamily.Binary,
                "長いバイト列（1024 バイト以上。ストリーミングと長さのパラメータが分岐する）",
                v => v.Length >= LongBinaryLength
            ),
        ];
}
