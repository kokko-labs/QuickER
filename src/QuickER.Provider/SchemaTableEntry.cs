using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using QuickER.Model;

namespace QuickER.Provider;

/// <summary>
/// スキーマ取込処理中にテーブルとその列を索引付きで保持する作業用エントリ（DB 方言横断の共通表現）
/// </summary>
/// <remarks>
/// テーブル→カラム→主キー→説明→外部キーの段階ロードで、後続ステップが列を引けるよう
/// <see cref="ColumnsByName"/>（<see cref="SchemaNames"/> の規則＝前後の空白を除き大文字小文字無視）を保持する。テーブルを一意に識別する
/// <see cref="Key"/> の書式は方言ごとに異なる（SQL Server は <c>[schema].[name]</c>、
/// PostgreSQL / MySQL / Oracle は素のテーブル名）ため、生成側が値を設定する。
/// </remarks>
public sealed class SchemaTableEntry
{
    /// <summary>構築中のエンティティ</summary>
    public Entity Entity { get; init; } = new();

    /// <summary>列名からカラムを引くための索引（後続の PK / 説明 / FK 反映に用いる）</summary>
    /// <remarks>
    /// 採用した列だけが入る（<see cref="TryBeginColumn"/> が衝突した列を弾く）。照合は <see cref="SchemaNames.Comparer"/>、
    /// キーはカタログが返した列名（綴りのまま）。カタログの行から引くときは、衝突で捨てた列の行が採用した列へ
    /// 吸い寄せられないよう <see cref="TryGetColumnExact"/> を使う（主キー・説明・一意制約・外部キーの両端）。
    /// カタログが列名を宣言の綴りで返すもの（SQLite の外部キーの親の列）は、先に <see cref="TryFindColumnName"/> で
    /// その DB の照合により採用した列の綴りへ直してから引く
    /// </remarks>
    public IReadOnlyDictionary<string, Column> ColumnsByName => _columnsByName;

    /// <summary><see cref="ColumnsByName"/> の実体（書き込みは <see cref="AddColumn"/> だけが行う）</summary>
    private readonly Dictionary<string, Column> _columnsByName = new(SchemaNames.Comparer);

    /// <summary>採用した列のカタログ上の名前（綴りのまま）</summary>
    private readonly HashSet<string> _acceptedColumnNames = new(StringComparer.Ordinal);

    /// <summary>テーブルを一意に識別するキー（書式は方言依存）</summary>
    public string Key { get; init; } = "";

    /// <summary>DB のテーブル名（綴りのまま。<see cref="Key"/> がテーブル名そのものでない方言だけが設定する）</summary>
    /// <remarks>
    /// <see cref="Entity.TableName"/> は前後の空白を除いた形（モデルの setter が除く）。前後に空白のある名前を
    /// 告げるため、DB の綴りをここか <see cref="Key"/> に残す（<see cref="SchemaImportWarnings.DetectTrimmedNames"/>）。
    /// </remarks>
    public string? RawName { get; init; }

    /// <summary>このテーブルの制約のうち、DB の名前に前後の空白があったもの（綴りのまま）</summary>
    /// <remarks>
    /// 外部キー名・一意制約名を取り込む共通部品（<see cref="ForeignKeyRelationshipBuilder"/>・
    /// <see cref="UniqueConstraintImportBuilder.Attach"/>）が記録し、<see cref="SchemaImportWarnings.DetectTrimmedNames"/> が告げる。
    /// </remarks>
    public List<string> PaddedConstraintNames { get; } = new();

    /// <summary>
    /// 採用するテーブルを引く辞書の比較器（<see cref="SchemaNames"/> の規則）。キーが <c>[schema].[name]</c> の形（SQL Server）の
    /// ときは角括弧の内側の前後の空白も除いて比べる
    /// </summary>
    public static IEqualityComparer<string> KeyComparer { get; } = new TableKeyComparer();

    /// <summary>
    /// 列を採用してよいか確かめる。同じテーブルにすでに <see cref="SchemaNames.AreSame"/> で同じ名前の列があれば、
    /// 後着を採らず <see cref="SchemaImportWarningKind.ColumnNameCollision"/> で告げる（他の方言のテーブル名の衝突と同じ流儀）
    /// </summary>
    /// <param name="columnName">カタログが返した列名（綴りのまま）</param>
    /// <param name="warnings">取込警告の積み先</param>
    /// <returns>採用してよければ <c>true</c>（続けて <see cref="AddColumn"/> を呼ぶ）</returns>
    public bool TryBeginColumn(string columnName, List<SchemaImportWarning> warnings)
    {
        if (!_columnsByName.TryGetValue(columnName, out var kept))
        {
            return true;
        }

        warnings.Add(
            new SchemaImportWarning(
                SchemaImportWarningKind.ColumnNameCollision,
                Entity.TableName,
                columnName,
                kept.Name
            )
        );

        return false;
    }

    /// <summary>採用した列をエンティティと索引へ加える</summary>
    /// <param name="column">構築した列</param>
    /// <param name="columnName">カタログが返した列名（綴りのまま）</param>
    public void AddColumn(Column column, string columnName)
    {
        Entity.Columns.Add(column);
        _columnsByName[columnName] = column;
        _acceptedColumnNames.Add(columnName);
    }

    /// <summary>カタログの行の列名を、綴りまで一致させて採用した列から引く</summary>
    /// <param name="columnName">カタログが返した列名</param>
    /// <param name="column">見つかった列</param>
    /// <remarks>衝突で捨てた列の行（主キー・説明）が、採用した列へ混ざらないようにする（<see cref="TryGetExact"/> の列版）</remarks>
    public bool TryGetColumnExact(string columnName, [NotNullWhen(true)] out Column? column)
    {
        column = null;

        return _acceptedColumnNames.Contains(columnName)
            && _columnsByName.TryGetValue(columnName, out column);
    }

    /// <summary>採用した列のうち条件に合うものを探し、カタログ上の名前（綴りのまま）を返す</summary>
    /// <param name="matches">カタログ上の名前と列を受け取り、探している列なら <c>true</c> を返す条件</param>
    /// <param name="columnName">見つかった列のカタログ上の名前</param>
    /// <returns>条件に合う採用済みの列があれば <c>true</c></returns>
    /// <remarks>
    /// カタログが宣言の綴りで返す列名（SQLite の外部キーの親の列）を、その DB の照合で採用した列の綴りへ直すために使う。
    /// 衝突で捨てた列は採用していないので、捨てた列を指す名前はどの列にも合わない（採用した列へ付け替えない）
    /// </remarks>
    public bool TryFindColumnName(
        Func<string, Column, bool> matches,
        [NotNullWhen(true)] out string? columnName
    )
    {
        foreach (var (name, column) in _columnsByName)
        {
            if (matches(name, column))
            {
                columnName = name;
                return true;
            }
        }

        columnName = null;
        return false;
    }

    /// <summary>取込対象として採用したテーブルを、大文字小文字まで一致させて引く</summary>
    /// <param name="tables">キー → エントリの辞書（5 方言共通で大文字小文字を区別しない）</param>
    /// <param name="key">引くテーブルのキー（書式は <see cref="Key"/> と同じ）</param>
    /// <param name="entry">見つかったエントリ</param>
    /// <returns>キーが序数一致で採用済みのテーブルにあれば <c>true</c></returns>
    /// <remarks>
    /// テーブル辞書は大文字小文字を区別しないので、素で引くと「大文字小文字だけ違うために衝突して捨てたほうの
    /// テーブル」の列・主キー・説明・一意制約・外部キーが、採用したほうのエンティティへ吸い寄せられて混ざる。
    /// 取込の各クエリがカタログから返す名前はどのクエリでも同じ実体名なので、序数一致まで求めても
    /// 正当な行を取りこぼすことはない。取込の全解決点（列・主キー・説明・一意制約・外部キーの両端）でこれを通す。
    /// </remarks>
    public static bool TryGetExact(
        IReadOnlyDictionary<string, SchemaTableEntry> tables,
        string key,
        [NotNullWhen(true)] out SchemaTableEntry? entry
    ) =>
        tables.TryGetValue(key, out entry)
        && string.Equals(entry.Key, key, StringComparison.Ordinal);

    /// <summary><see cref="KeyComparer"/> の実体</summary>
    private sealed class TableKeyComparer : IEqualityComparer<string>
    {
        public bool Equals(string? x, string? y) =>
            string.Equals(Normalize(x), Normalize(y), StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(string obj) =>
            StringComparer.OrdinalIgnoreCase.GetHashCode(Normalize(obj));

        private static string Normalize(string? key) =>
            BracketPadding.Replace(SchemaNames.ToStoredForm(key), string.Empty);

        /// <summary>角括弧の内側の前後の空白（<c>[ a ]</c> の空白）</summary>
        private static readonly Regex BracketPadding = new(
            @"(?<=\[)\s+|\s+(?=\])",
            RegexOptions.Compiled | RegexOptions.CultureInvariant
        );
    }
}
