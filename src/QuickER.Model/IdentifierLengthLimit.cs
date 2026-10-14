using System.Security.Cryptography;
using System.Text;

namespace QuickER.Model;

/// <summary>識別子の長さを数える単位</summary>
public enum IdentifierLengthUnit
{
    /// <summary>上限なし（SQLite）</summary>
    Unlimited,

    /// <summary>UTF-16 のコード単位（SQL Server の <c>sysname</c>＝<c>nvarchar(128)</c>。サロゲート対は 2 と数える）</summary>
    Utf16CodeUnits,

    /// <summary>UTF-8 のバイト数（PostgreSQL・Oracle）</summary>
    Utf8Bytes,

    /// <summary>文字数（MySQL。サロゲート対は 1 文字と数える）</summary>
    Characters,
}

/// <summary>
/// DB 方言の識別子（テーブル名・列名・制約名）の長さの上限と、表せない文字
/// </summary>
/// <remarks>
/// <para>
/// 値の正本は <see cref="IdentifierLengthLimits"/>（方言名の表）で、各方言の識別子クラスの <c>Limit</c> と
/// 同期のケーパビリティはそれを指すだけ（写しを作らない）。Model に置くのは、Provider を参照しない MCP のツールホストも
/// 名前を受けたときに上限を検査するため。
/// </para>
/// <para>
/// テーブル名は、各方言の識別子クラスの <c>Quote</c> と同じく最初の <c>.</c> でスキーマ修飾と表の名前へ分け、
/// 部分ごとに測る（<see cref="FitsTableName"/>）。列名・制約名は分けずに 1 つの識別子として測る（<see cref="Fits"/>）。
/// </para>
/// <para>
/// 長さに加えて、方言が表せない文字を宣言する（<see cref="RepresentsSupplementaryCharacters"/>）。MySQL は識別子の文字集合が
/// <c>utf8mb3</c> で、BMP の外の文字（サロゲート対）を断らずに黙って <c>?</c> へ置き換えるため、名前の入口で断る
/// （<see cref="Accepts"/> / <see cref="AcceptsTableName"/> が長さと文字の両方を見る）。
/// </para>
/// </remarks>
/// <param name="Unit">数える単位</param>
/// <param name="Max">上限（<see cref="IdentifierLengthUnit.Unlimited"/> のときは使わない）</param>
/// <param name="TableFileNameMax">
/// 表の名前をファイル名へ符号化したときの長さの上限（MySQL だけ。<see cref="MySqlFileNameEncoding"/> を参照）。
/// <c>null</c> は検査しない。
/// </param>
/// <param name="RepresentsSupplementaryCharacters">
/// BMP の外の文字（サロゲート対）を識別子に保存できるか（MySQL だけ <c>false</c>＝黙って <c>?</c> へ置き換える）
/// </param>
public sealed record IdentifierLengthLimit(
    IdentifierLengthUnit Unit,
    int Max,
    int? TableFileNameMax = null,
    bool RepresentsSupplementaryCharacters = true
)
{
    /// <summary>上限なし</summary>
    public static IdentifierLengthLimit Unlimited { get; } = new(IdentifierLengthUnit.Unlimited, 0);

    /// <summary>上限が無いか</summary>
    public bool IsUnlimited => Unit == IdentifierLengthUnit.Unlimited;

    /// <summary>1 つの識別子の長さを、この上限の単位で測る</summary>
    public int Measure(string name) =>
        Unit switch
        {
            IdentifierLengthUnit.Unlimited => 0,
            IdentifierLengthUnit.Utf16CodeUnits => name.Length,
            IdentifierLengthUnit.Utf8Bytes => Encoding.UTF8.GetByteCount(name),
            IdentifierLengthUnit.Characters => CountCharacters(name),
            _ => throw new ArgumentOutOfRangeException(nameof(Unit), Unit, null),
        };

    /// <summary>1 つの識別子（列名・制約名）が上限以内か</summary>
    public bool Fits(string name) => IsUnlimited || Measure(name) <= Max;

    /// <summary>テーブル名が上限以内か（スキーマ修飾は最初の <c>.</c> で分けて部分ごとに測る）</summary>
    /// <remarks>
    /// ファイル名の符号化の上限（<see cref="TableFileNameMax"/>）は表の名前の部分だけに掛ける。
    /// スキーマの部分は MySQL ではデータベース名で、QuickER は作らない（既に在る名前を指すだけ）ため検査しない。
    /// </remarks>
    public bool FitsTableName(string tableName)
    {
        if (IsUnlimited)
        {
            return true;
        }

        var dot = tableName.IndexOf('.');
        var schema = dot < 0 ? null : tableName[..dot];
        var table = dot < 0 ? tableName : tableName[(dot + 1)..];

        if (schema is not null && !Fits(schema))
        {
            return false;
        }

        return Fits(table)
            && (
                TableFileNameMax is not { } fileMax
                || MySqlFileNameEncoding.EncodedLength(table) <= fileMax
            );
    }

    /// <summary>名前の文字をすべて保存できるか（BMP の外の文字を保存できない方言で、それを含めば <c>false</c>）</summary>
    public bool CanRepresent(string name) =>
        RepresentsSupplementaryCharacters || !name.Any(char.IsSurrogate);

    /// <summary>列名・制約名として受け付けられるか（長さと文字）</summary>
    public bool Accepts(string name) => Fits(name) && CanRepresent(name);

    /// <summary>テーブル名として受け付けられるか（長さ＝<see cref="FitsTableName"/> と文字）</summary>
    public bool AcceptsTableName(string tableName) =>
        FitsTableName(tableName) && CanRepresent(tableName);

    /// <summary>受け付けられない理由を英語で返す（受け付けられるなら <c>null</c>）</summary>
    /// <param name="name">名前</param>
    /// <param name="isTableName">テーブル名として測るか（スキーマ修飾の分け方とファイル名の上限）</param>
    /// <remarks>DDL・同期の入口の例外文と、MCP の注意文が使う（英語が正本）。GUI の表示は resx で文言化する。</remarks>
    public string? DescribeViolation(string name, bool isTableName)
    {
        if (!CanRepresent(name))
        {
            return "contains a character outside the Basic Multilingual Plane, which this database cannot store";
        }

        var fits = isTableName ? FitsTableName(name) : Fits(name);

        return fits ? null : $"is longer than the limit of {Describe()}";
    }

    /// <summary>上限を人が読む英語の表記にする（例 <c>63 UTF-8 bytes</c>）</summary>
    /// <remarks>例外文・MCP の注意文に載せる（英語が正本）。GUI の表示は resx 側で単位を訳す。</remarks>
    public string Describe() =>
        Unit switch
        {
            IdentifierLengthUnit.Unlimited => "no limit",
            IdentifierLengthUnit.Utf16CodeUnits => $"{Max} UTF-16 code units",
            IdentifierLengthUnit.Utf8Bytes => $"{Max} UTF-8 bytes",
            IdentifierLengthUnit.Characters when TableFileNameMax is { } fileMax =>
                $"{Max} characters (table names also {fileMax} bytes as a file name)",
            IdentifierLengthUnit.Characters => $"{Max} characters",
            _ => throw new ArgumentOutOfRangeException(nameof(Unit), Unit, null),
        };

    /// <summary>QuickER が付ける既定の名前を上限に収める（上限内ならそのまま）</summary>
    /// <param name="name">縮める前の既定の名前（<c>PK_{表}</c>・<c>FK_{子}_{親}</c>・<c>UQ_{表}_{列…}</c> など）</param>
    /// <returns>
    /// 上限内なら <paramref name="name"/> そのもの。超えるときは、先頭から上限に収まるところまで（文字の途中では切らない）と、
    /// <c>_</c> と、縮める前の名前の SHA-256（UTF-8）の先頭 8 桁の 16 進（小文字）をつないだ名前
    /// </returns>
    /// <remarks>
    /// <para>
    /// 末尾に縮める前の名前のハッシュを付けるのは、上限の後ろでだけ違う 2 つの既定名（長いテーブル名を共有する
    /// 外部キー・一意制約）が同じ名前に潰れないため。結果は入力と上限だけで決まる（決定的）。
    /// </para>
    /// <para>
    /// 利用者が付けた名前には使わない（利用者の名前は縮めずに、DDL・同期の入口で場所を示して断る）。
    /// どの上限を渡すかの経路は <see cref="ConstraintNames"/> の XmlDoc を参照。
    /// </para>
    /// </remarks>
    public string Fit(string name)
    {
        if (Fits(name))
        {
            return name;
        }

        var hash = Convert
            .ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(name)))
            .Substring(0, HashLength);
        var suffix = "_" + hash;

        return Prefix(name, Max - Measure(suffix)) + suffix;
    }

    /// <summary>名前を上限で切り詰める（文字の途中では切らない）</summary>
    /// <remarks>
    /// PostgreSQL が上限を超える識別子を黙って切り詰めるときと同じ切り方（UTF-8 で 63 バイト以内の最長の先頭部分）。
    /// 旧版の QuickER が長い既定名で作った PostgreSQL の外部キーを、差分で同じ外部キーとみなすために使う
    /// （<c>SyncDialectCapabilities.TruncatesLongIdentifiers</c>）。上限なしならそのまま返す。
    /// </remarks>
    public string Truncate(string name) => Fits(name) ? name : Prefix(name, Max);

    /// <summary>既定名を縮めるときに付けるハッシュの桁数</summary>
    private const int HashLength = 8;

    /// <summary>先頭から、この上限の単位で <paramref name="budget"/> 以内に収まる最長の部分（文字の途中では切らない）</summary>
    private string Prefix(string name, int budget)
    {
        var sb = new StringBuilder();
        var used = 0;

        foreach (var rune in name.EnumerateRunes())
        {
            var cost = Unit switch
            {
                IdentifierLengthUnit.Utf16CodeUnits => rune.Utf16SequenceLength,
                IdentifierLengthUnit.Utf8Bytes => rune.Utf8SequenceLength,
                _ => 1,
            };

            if (used + cost > budget)
            {
                break;
            }

            used += cost;
            sb.Append(rune.ToString());
        }

        return sb.ToString();
    }

    /// <summary>文字数（Unicode のスカラー値の数。サロゲート対は 1 文字）</summary>
    private static int CountCharacters(string name)
    {
        var count = 0;

        foreach (var _ in name.EnumerateRunes())
        {
            count++;
        }

        return count;
    }
}

/// <summary>DB 方言ごとの識別子の長さの上限（正本）</summary>
/// <remarks>
/// <para>
/// 値は実 DB で確かめた（Integration の <c>IdentifierLengthLimitIntegrationTests</c> が、上限ちょうどは通り
/// 1 つ超えると断られる〔PostgreSQL は切り詰められる〕ことを 4 方言で固定する）。
/// </para>
/// <list type="bullet">
/// <item>SQL Server: 128（UTF-16 のコード単位。<c>sysname</c>＝<c>nvarchar(128)</c>）</item>
/// <item>PostgreSQL: 63 バイト（<c>NAMEDATALEN - 1</c>。超えた名前は断らずに黙って切り詰める）</item>
/// <item>
/// MySQL: 64 文字（<c>NAME_CHAR_LEN</c>）。表の名前はさらに、ファイル名へ符号化した長さが 251 バイト以内
/// （InnoDB は表ごとに <c>{符号化した名前}.ibd</c> を作り、ファイルシステムの名前の上限 255 バイトから
/// 拡張子の 4 を引いた値。超えると「Got error 168 from storage engine」）
/// </item>
/// <item>Oracle: 128 バイト（12.2 以降・<c>COMPATIBLE</c> が 12.2 以上。それより前の互換設定の DB は 30 バイト＝docs で案内）</item>
/// <item>SQLite: 上限なし</item>
/// </list>
/// <para>
/// MySQL は BMP の外の文字（サロゲート対）を識別子に保存できない（断らずに <c>?</c> へ置き換える＝
/// <c>MySqlIdentifierLengthLimitIntegrationTests.SupplementaryCharacter_IsReplacedWithQuestionMark</c>）。
/// </para>
/// </remarks>
public static class IdentifierLengthLimits
{
    /// <summary>SQL Server</summary>
    public static IdentifierLengthLimit SqlServer { get; } =
        new(IdentifierLengthUnit.Utf16CodeUnits, 128);

    /// <summary>PostgreSQL</summary>
    public static IdentifierLengthLimit PostgreSql { get; } =
        new(IdentifierLengthUnit.Utf8Bytes, 63);

    /// <summary>MySQL</summary>
    public static IdentifierLengthLimit MySql { get; } =
        new(
            IdentifierLengthUnit.Characters,
            64,
            TableFileNameMax: 251,
            RepresentsSupplementaryCharacters: false
        );

    /// <summary>Oracle</summary>
    public static IdentifierLengthLimit Oracle { get; } = new(IdentifierLengthUnit.Utf8Bytes, 128);

    /// <summary>SQLite</summary>
    public static IdentifierLengthLimit Sqlite { get; } = IdentifierLengthLimit.Unlimited;

    /// <summary>方言名（<see cref="ErDiagram.TargetDbms"/> の値）から上限を引く。知らない方言は上限なし。</summary>
    /// <remarks>方言名の照合は、方言プロバイダーの解決と同じく大文字小文字を区別しない。</remarks>
    public static IdentifierLengthLimit ForDbms(string? dbms)
    {
        foreach (var (name, limit) in ByDbms)
        {
            if (string.Equals(name, dbms?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return limit;
            }
        }

        return IdentifierLengthLimit.Unlimited;
    }

    /// <summary>方言名と上限の表</summary>
    private static readonly (string Name, IdentifierLengthLimit Limit)[] ByDbms =
    [
        ("sqlserver", SqlServer),
        ("postgresql", PostgreSql),
        ("mysql", MySql),
        ("oracle", Oracle),
        ("sqlite", Sqlite),
    ];
}
