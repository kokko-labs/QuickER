using System;
using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
using QuickER.Provider.Sync.Steps;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>総当たりの生成器が扱う列の型の種類（方言ごとの型名は <see cref="SyncPlanFuzzDialect.TypeName"/>）</summary>
internal enum FuzzTypeKind
{
    /// <summary>32 ビット整数</summary>
    Int,

    /// <summary>64 ビット整数</summary>
    BigInt,

    /// <summary>可変長の文字列（長さつき）</summary>
    VarChar,

    /// <summary>固定小数点数（精度つき・スケール 2）</summary>
    Decimal,

    /// <summary>日時（長さは秒の小数部の精度。方言の既定の精度は精度を書かない綴り）</summary>
    DateTime,

    /// <summary>固定長の文字列（長さつき）</summary>
    FixedChar,

    /// <summary>可変長のバイト列（長さつき。長さを持たない方言は長さを書かない）</summary>
    Binary,
}

/// <summary>
/// 同期計画の総当たり（<see cref="SyncPlanFuzzTestsBase"/>）が方言ごとに差し替える部分。
/// </summary>
/// <remarks>
/// 図の DB への作成・データの投入・計算列の追加は、すべて方言の同期の実行器（<see cref="ISchemaSyncExecutor"/>）へ
/// スクリプトとして渡す（方言ごとの文の区切り・トランザクションの扱いを総当たりの側で持たない）。
/// </remarks>
internal abstract partial class SyncPlanFuzzDialect
{
    /// <summary>
    /// 外部キーの両端の型の組（子の型・親の型）を DB が必ず断るか（静的な規則 R7・R8 が失敗を予言する組）
    /// </summary>
    /// <param name="dialect">方言名（<see cref="DialectNames"/> の値）</param>
    /// <param name="capabilities">方言の能力</param>
    /// <param name="childType">子の列の型</param>
    /// <param name="parentType">親の列の型</param>
    /// <remarks>
    /// <para>
    /// 製品の宣言（<see cref="SyncDialectCapabilities.AreForeignKeyCompatibleTypes"/>）は「真なら DB が必ず受け入れる」保守側なので、
    /// 偽の組の一部を DB は受け入れる。宣言が偽で、ここに書いた測った事実が「断る」とする組だけを断ると答える。測っていない型の組は
    /// 断るとは言わない（予言しない側に倒す）。宣言が真の組は断らない。
    /// </para>
    /// <list type="bullet">
    ///   <item>PostgreSQL（42804）：数値と文字列の組・子が小数（numeric / decimal）で親が整数・子が浮動小数点数で親が整数か小数。文字列どうし
    ///   （char・varchar・text）と、子が整数か小数で親が小数か浮動小数点数の組は、両方向とも外部キーの追加も張ったままの列の変更も受け入れる。</item>
    ///   <item>Oracle（ORA-02267）：NUMBER と文字列の組・固定長（CHAR / NCHAR）と可変長（VARCHAR2 / NVARCHAR2）の組。NUMBER どうしは精度・
    ///   スケールを問わず受け入れ、CHAR の単位語の違いも受け入れる。CHAR と NCHAR・VARCHAR2 と NVARCHAR2 は、外部キーの追加は断るが張った
    ///   ままの親の列の変更は通すので、断るとは言わない。</item>
    ///   <item>SQL Server・MySQL・SQLite：製品の宣言の否定のまま（SQL Server 1778 / 1753・MySQL 3780 は宣言と同じ範囲で断る。SQLite は
    ///   静的な規則が判定しない）。</item>
    /// </list>
    /// <para>出典はコンテナでの実測（外部キーの追加と、外部キーを張ったまま片方の端の型を変える文。行を 1 つずつ入れた）。</para>
    /// </remarks>
    public static bool RefusesForeignKeyTypes(
        string dialect,
        SyncDialectCapabilities capabilities,
        string childType,
        string parentType
    )
    {
        // MySQL の列ごとの文字集合の指定（形の行の latin1 の水準の DB 側の図だけが書く）は比べない：取込は列の型に文字集合を持ち帰らず、
        // 同期は列の照合順序を読み直して保つ。生成器・形の行は文字集合の違う外部キーの組を作らない
        if (dialect == DialectNames.MySql)
        {
            childType = MySqlCharacterSetClause().Replace(childType, string.Empty);
            parentType = MySqlCharacterSetClause().Replace(parentType, string.Empty);
        }

        if (capabilities.AreForeignKeyCompatibleTypes(childType, parentType))
        {
            return false;
        }

        var child = TypeFamilyOf(dialect, childType);
        var parent = TypeFamilyOf(dialect, parentType);

        return dialect switch
        {
            DialectNames.PostgreSql => (child, parent) switch
            {
                (ForeignKeyTypeFamily.Unknown, _) or (_, ForeignKeyTypeFamily.Unknown) => false,
                _ when IsString(child) != IsString(parent) => true,
                (ForeignKeyTypeFamily.Decimal, ForeignKeyTypeFamily.Integer) => true,
                (
                    ForeignKeyTypeFamily.Float,
                    ForeignKeyTypeFamily.Integer
                        or ForeignKeyTypeFamily.Decimal
                ) => true,
                _ => false,
            },
            DialectNames.Oracle => (child, parent) switch
            {
                (ForeignKeyTypeFamily.Unknown, _) or (_, ForeignKeyTypeFamily.Unknown) => false,
                _ when IsString(child) != IsString(parent) => true,
                (ForeignKeyTypeFamily.FixedString, ForeignKeyTypeFamily.VariableString)
                or (ForeignKeyTypeFamily.VariableString, ForeignKeyTypeFamily.FixedString) => true,
                _ => false,
            },
            _ => true,
        };

        static bool IsString(ForeignKeyTypeFamily family) =>
            family
                is ForeignKeyTypeFamily.String
                    or ForeignKeyTypeFamily.FixedString
                    or ForeignKeyTypeFamily.VariableString;
    }

    /// <summary>
    /// 外部キーを張ったままの列の型の変更を、外部キーを外し直す方言（<see cref="SyncDialectCapabilities.AlterColumnRequiresForeignKeyRebuild"/>）の
    /// DB が受けるか（静的な規則 R2 の外部キーの枝が予言しない変更）
    /// </summary>
    /// <param name="dialect">方言名（<see cref="DialectNames"/> の値）</param>
    /// <param name="oldType">変える前の型</param>
    /// <param name="newType">変えた後の型</param>
    /// <remarks>
    /// <para>出典は 2026-11-26 のコンテナでの実測（外部キーを張ったまま子の端・親の端の列を MODIFY。行の有無で結果は変わらない）。</para>
    /// <list type="bullet">
    ///   <item>MySQL：可変長の型（varchar・varbinary）を伸ばし、長さの頭のバイト数が変わらない変更だけを受ける（255 バイト以下どうしか
    ///   256 バイト以上どうし。varchar は文字集合の最大のバイト数を掛ける＝コンテナの既定 utf8mb4 は 4 バイトなので 63 文字までが 1 バイトの頭）。
    ///   varchar(20) → (30)・(63)、varchar(100) → (200)、varbinary(20) → (30) は受け、varchar(20) → (64)・(300)、縮小、char・binary の長さの
    ///   変更、varchar と char の入れ替えは 1832 で断る（型の種類の変更・整数の幅は 3780）。</item>
    ///   <item>SQL Server：外部キーを張ったままの列の型の変更をどれも断る（varchar(20) → (30) も Msg 5074）。NULL 許容だけの変更は受ける
    ///   （R2 は型を変える文だけを見る）。</item>
    /// </list>
    /// </remarks>
    public static bool AcceptsForeignKeyColumnChangeInPlace(
        string dialect,
        string oldType,
        string newType,
        SchemaColumnByteLength? oldColumn = null
    )
    {
        if (dialect != DialectNames.MySql)
        {
            return false;
        }

        var before = MySqlStringTypePattern().Match(oldType);
        var after = MySqlStringTypePattern().Match(newType);

        if (
            !before.Success
            || !after.Success
            || !before.Groups["base"].Value.StartsWith("var", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                before.Groups["base"].Value,
                after.Groups["base"].Value,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return false;
        }

        // 列の文字集合：DB の列の事実（取込の事実の代わり）があればそれ、無ければ型の表記の指定、無ければコンテナの既定 utf8mb4。
        // 変えた後の型が別の文字集合を書いていれば文字集合の変更（MySQL は外部キーを張ったまま断る）。書かなければ、同期は列の照合順序を
        // 読み直して保つので文字集合も変わらない
        var characterSet =
            oldColumn?.CharacterSet
            ?? NullIfEmpty(before.Groups["charset"].Value)
            ?? MySqlDefaultCharacterSet;
        var newCharacterSet = NullIfEmpty(after.Groups["charset"].Value);

        if (
            newCharacterSet is not null
            && !string.Equals(newCharacterSet, characterSet, StringComparison.OrdinalIgnoreCase)
        )
        {
            return false;
        }

        var bytesPerUnit = BytesPerUnit(before.Groups["base"].Value, characterSet);
        var oldBytes =
            oldColumn?.OctetLength
            ?? int.Parse(before.Groups["length"].Value, CultureInfo.InvariantCulture)
                * bytesPerUnit;
        var newBytes =
            int.Parse(after.Groups["length"].Value, CultureInfo.InvariantCulture) * bytesPerUnit;

        return newBytes > oldBytes && oldBytes <= 255 == newBytes <= 255;

        static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
    }

    /// <summary>
    /// MySQL の DB の文字列の列の最大バイト数と文字集合（取込が読む information_schema の事実の代わり。MySQL だけ・ほかの方言は空）
    /// </summary>
    /// <param name="dialect">方言名</param>
    /// <param name="entities">DB 側の図の表（型の表記に <c>CHARACTER SET</c> を書いた列はその文字集合、書かない列はコンテナの既定 utf8mb4）</param>
    public static IReadOnlyList<SchemaColumnByteLength> ColumnByteLengths(
        string dialect,
        IReadOnlyList<Entity> entities
    )
    {
        if (dialect != DialectNames.MySql)
        {
            return [];
        }

        var facts = new List<SchemaColumnByteLength>();

        foreach (var entity in entities)
        {
            foreach (var column in entity.Columns)
            {
                if (MySqlStringTypePattern().Match(column.DataType) is not { Success: true } type)
                {
                    continue;
                }

                var characterSet = type.Groups["charset"].Value is { Length: > 0 } declared
                    ? declared
                    : MySqlDefaultCharacterSet;
                facts.Add(
                    new SchemaColumnByteLength(
                        entity.TableName,
                        column.Name,
                        characterSet,
                        int.Parse(type.Groups["length"].Value, CultureInfo.InvariantCulture)
                            * BytesPerUnit(type.Groups["base"].Value, characterSet)
                    )
                );
            }
        }

        return facts;
    }

    /// <summary>コンテナの MySQL の既定の文字集合</summary>
    private const string MySqlDefaultCharacterSet = "utf8mb4";

    /// <summary>MySQL の長さの 1 単位のバイト数（binary 系は 1、文字列は文字集合の最大バイト数）</summary>
    private static int BytesPerUnit(string baseType, string characterSet) =>
        baseType.EndsWith("binary", StringComparison.OrdinalIgnoreCase)
            ? 1
            : characterSet.ToLowerInvariant() switch
            {
                "latin1" or "ascii" or "binary" => 1,
                "utf8" or "utf8mb3" => 3,
                _ => 4,
            };

    /// <summary>MySQL の型の表記の後ろの文字集合の指定</summary>
    [GeneratedRegex(
        @"\s+(?:CHARACTER\s+SET|CHARSET)\s+[A-Za-z0-9_]+\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex MySqlCharacterSetClause();

    /// <summary>MySQL の長さを持つ文字列・バイト列の型の表記（基本の型・長さ・あれば文字集合）</summary>
    [GeneratedRegex(
        @"^\s*(?<base>varchar|varbinary|char|binary)\s*\(\s*(?<length>[0-9]+)\s*\)(?:\s+(?:CHARACTER\s+SET|CHARSET)\s+(?<charset>[A-Za-z0-9_]+))?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex MySqlStringTypePattern();

    /// <summary>
    /// 参照アクション SET DEFAULT の外部キーを、子の列が NOT NULL で既定値を持たないとき DB が作らないか（静的な規則 R13）
    /// </summary>
    /// <param name="dialect">方言名（<see cref="DialectNames"/> の値）</param>
    /// <remarks>
    /// 出典は 2026-11-26 のコンテナでの実測。SQL Server は Msg 1760 で断る（ON DELETE・ON UPDATE のどちらも。NULL 許容で既定値の無い列を
    /// NOT NULL にしてから付け直しても断り、列に DEFAULT 制約があれば受ける。外部キーを張ったままの NOT NULL 化は受ける）。
    /// PostgreSQL・SQLite は作る（削除のときに断られるだけ）。MySQL・Oracle は SET DEFAULT を作らない（製品が実効値へ畳む）。
    /// </remarks>
    public static bool RefusesSetDefaultOnNotNullColumnWithoutDefault(string dialect) =>
        dialect == DialectNames.SqlServer;

    /// <summary>
    /// 網の普段の種と決定的な行・網羅の行で、製品の計画器がこの方言の計画へ足す文の理由（<see cref="ImplicitStepReason"/>）。
    /// 能力の組の網の空振りの検査が、宣言した理由を持つ計画が 1 つ以上あり、宣言に無い理由を持つ計画が無いことを表明し、5 方言の宣言の
    /// 和が列挙の全値と一致することを DB なしの行が表明する
    /// </summary>
    /// <param name="dialect">方言名（<see cref="DialectNames"/> の値）</param>
    /// <remarks>
    /// テーブル再構築の方言（SQLite）は足す文を表の作り直しへ畳むので持たない。主キー・一意制約の外し直しは列の変更で鍵の制約を外し直す
    /// 方言（<see cref="SyncDialectCapabilities.AlterColumnRequiresKeyConstraintRebuild"/>＝SQL Server）だけ、主キーが置き換える一意制約は
    /// 主キーと同じ列集合の一意制約を持てない逐次 DDL の方言（Oracle）だけ。2026-11-26 の網の普段の 5,000 種で数えた。
    /// </remarks>
    public static IReadOnlySet<ImplicitStepReason> ImplicitStepReasonsPlannedFor(string dialect)
    {
        HashSet<ImplicitStepReason> common =
        [
            ImplicitStepReason.PrimaryKeyColumnNotNull,
            ImplicitStepReason.VanishingForeignKey,
            ImplicitStepReason.VanishingUniqueConstraint,
            ImplicitStepReason.ForeignKeyRebuild,
        ];

        return dialect switch
        {
            DialectNames.Sqlite => new HashSet<ImplicitStepReason>(),
            DialectNames.SqlServer =>
            [
                .. common,
                ImplicitStepReason.PrimaryKeyRebuild,
                ImplicitStepReason.UniqueConstraintRebuild,
            ],
            DialectNames.Oracle =>
            [
                .. common,
                ImplicitStepReason.UniqueConstraintReplacedByPrimaryKey,
            ],
            _ => common,
        };
    }

    /// <summary>外部キーの型の事実（<see cref="RefusesForeignKeyTypes"/>）で見分ける型の系統</summary>
    private enum ForeignKeyTypeFamily
    {
        /// <summary>測っていない型</summary>
        Unknown,

        /// <summary>整数</summary>
        Integer,

        /// <summary>小数（PostgreSQL の numeric / decimal・Oracle の NUMBER）</summary>
        Decimal,

        /// <summary>浮動小数点数（PostgreSQL）</summary>
        Float,

        /// <summary>文字列（PostgreSQL の char・varchar・text。どの組も受け入れる）</summary>
        String,

        /// <summary>固定長の文字列（Oracle）</summary>
        FixedString,

        /// <summary>可変長の文字列（Oracle）</summary>
        VariableString,
    }

    /// <summary>型の表記の系統（括弧より前の基本の名前で見分ける）</summary>
    private static ForeignKeyTypeFamily TypeFamilyOf(string dialect, string type)
    {
        var paren = type.IndexOf('(', StringComparison.Ordinal);
        var name = (paren < 0 ? type : type[..paren]).Trim().ToLowerInvariant();

        return dialect switch
        {
            DialectNames.PostgreSql => name switch
            {
                "smallint" or "integer" or "int" or "bigint" or "int2" or "int4" or "int8" =>
                    ForeignKeyTypeFamily.Integer,
                "numeric" or "decimal" => ForeignKeyTypeFamily.Decimal,
                "real" or "double precision" or "float4" or "float8" or "float" =>
                    ForeignKeyTypeFamily.Float,
                "char" or "character" or "bpchar" or "varchar" or "character varying" or "text" =>
                    ForeignKeyTypeFamily.String,
                _ => ForeignKeyTypeFamily.Unknown,
            },
            DialectNames.Oracle => name switch
            {
                "number" => ForeignKeyTypeFamily.Decimal,
                "char" or "nchar" => ForeignKeyTypeFamily.FixedString,
                "varchar2" or "nvarchar2" => ForeignKeyTypeFamily.VariableString,
                _ => ForeignKeyTypeFamily.Unknown,
            },
            _ => ForeignKeyTypeFamily.Unknown,
        };
    }

    /// <summary>報告に出す方言名（<see cref="DialectNames"/> の値）</summary>
    public abstract string Name { get; }

    /// <summary>方言のプロバイダ（取込・差分の能力・DDL・同期スクリプト・実行器）</summary>
    public abstract IDatabaseProvider Provider { get; }

    /// <summary>取込に使う接続文字列</summary>
    public abstract string ImportConnectionString { get; }

    /// <summary>同期の実行器へ渡す接続設定</summary>
    public abstract DbConnectionSettings Settings { get; }

    /// <summary>
    /// 参照アクション（CASCADE / SET NULL）を生成してよいか。SQL Server は循環・複数経路の連鎖を
    /// CREATE の時点で断る（QuickER の外の制約）ため、NO ACTION だけを使う。
    /// </summary>
    public virtual bool AllowCascade => true;

    /// <summary>
    /// 同じ列ペアから同じ親へ外部キーを 2 本張れるか。Oracle は ORA-02275 で断る（QuickER の外の制約）。
    /// </summary>
    public virtual bool AllowDuplicateForeignKeys => true;

    /// <summary>
    /// 同じ列の並びに一意制約を 2 つ張れるか。Oracle は ORA-02261 で断る（QuickER の外の制約）。
    /// </summary>
    public virtual bool AllowDuplicateUniqueConstraints => true;

    /// <summary>
    /// 同じ列の並びの制約（一意制約・外部キー）を名前で見分けるか。SQLite は取込が制約の名前を持ち帰らず、製品は名前の無い制約を
    /// 同じ列集合の DB の制約で満たされるとみなす（仕様）ので、目標の図の変更で同じ列の並びの制約を重ねない（重ねた 2 つは 1 つと
    /// 区別できない）
    /// </summary>
    public virtual bool DistinguishesSameSetConstraints => true;

    /// <summary>日時の型の既定の精度（<see cref="TypeName"/> は、この精度なら精度を書かない綴りを返す）</summary>
    public abstract int DefaultDateTimePrecision { get; }

    /// <summary>
    /// 方言の上限ちょうどの長さを持つ型の種類と長さ（上限が無い方言は null）。DB が型として受け付ける上限ちょうどの型を、
    /// 型の変更の行き先に混ぜる（取込の表記の正規化と型の比較が上限の表記でも閉じているかを見る）
    /// </summary>
    public abstract (FuzzTypeKind Kind, int Length)? LimitType { get; }

    /// <summary>
    /// 同じ型の別の綴り（既定の精度を明示した綴り。無ければ null）。差分は両側を正規化して比べるので、
    /// 綴りだけを変えた目標の図は差分を出してはいけない
    /// </summary>
    public virtual string? RespelledTypeName(FuzzTypeKind kind, int length) => null;

    /// <summary>
    /// 識別子の引用の中で特別な扱いが要る文字（識別子の引用符・文字列の引用符）。列の名前に混ぜて、計画の文と
    /// 動的 SQL が名前を正しく引用しているかを見る
    /// </summary>
    public abstract char SymbolCharacter { get; }

    /// <summary>
    /// 既定と別のスキーマ（同じ名前の表を置いて、名前の逆引きが表の所有者で絞られているかを見る。使わない方言は null）
    /// </summary>
    public virtual string? OtherSchema => null;

    /// <summary>
    /// 精度を書かない小数の型名（DB が精度なしのまま覚える方言だけ。SQL Server・MySQL の素の decimal は (18,0)・(10,0) に正規化されるので
    /// null＝生成器はこの方言で精度なしの列を作らない）
    /// </summary>
    public virtual string? PrecisionlessDecimalTypeName => null;

    /// <summary>
    /// 一意制約が NULL を何行でも許すか（SQL Server は NULL どうしを重なりとみなして 2 行目の NULL で断る）。偽の方言では、行のある表に足した
    /// NULL 許容の列への一意制約を、既存の行で断られる文として宣言する
    /// </summary>
    public virtual bool UniqueAllowsRepeatedNull => true;

    /// <summary>
    /// 一意制約が、構成列の全部が NULL の行だけを検査の外に置き、一部が NULL の行は残りの列で比べるか（Oracle。2026-11-26 の実測：
    /// uq(v) の下の (v, w) = (NULL, 5) の 2 行に (v, w) の一意制約を足すと ORA-02299、PostgreSQL・MySQL・SQLite は受ける）。
    /// 真の方言では、NULL 許容の列の一意制約の上位集合の一意制約を、その列が NULL で残りが同じ行のある表に足す文を、既存の行で断られる
    /// 文として宣言する
    /// </summary>
    public virtual bool UniqueComparesPartiallyNullKeys => false;

    /// <summary>
    /// 網の決定的な行（形の行）が、前寄せの組から一意制約の作り直しを取り下げる形を必ず出すか（MySQL は形の行の必ず含む行 ah2-two-readded-uniques の
    /// 一意制約の双子を両方作り直す形）。真の方言では、決定的な行を流した網の関係 N4 が取り下げのあるケースを 1 つ以上照らしたことを表明する
    /// </summary>
    public virtual bool ScenarioRowsWithdrawUniqueRebuild => false;

    /// <summary>
    /// 製品の計画器が、この方言の計画をテーブル再構築で組むか（SQLite だけ真）。真の方言では、普段の種を流した網の関係 N4 が、既存の表の
    /// 再構築・表を作るだけの再構築・再構築と畳まれずに残ったセクションの両方を持つ計画を、それぞれ 1 つ以上照らしたことを表明する
    /// </summary>
    public virtual bool PlansTableRebuilds => false;

    /// <summary>精度とスケールを指定した小数の型名（<see cref="TypeName"/> の小数はスケール 2）</summary>
    public string DecimalTypeName(int precision, int scale) =>
        TypeName(FuzzTypeKind.Decimal, precision)
            .Replace(",2)", $",{scale})", StringComparison.Ordinal);

    /// <summary>
    /// 同じ系統の固定長と可変長を入れ替えた文字列の型名（<paramref name="kind"/> が可変長なら固定長、固定長なら可変長。長さは同じ）
    /// </summary>
    public virtual string SwappedLengthTypeName(FuzzTypeKind kind, int length) =>
        TypeName(
            kind == FuzzTypeKind.FixedChar ? FuzzTypeKind.VarChar : FuzzTypeKind.FixedChar,
            length
        );

    /// <summary>表の名前を引用する（スキーマで修飾した名前は方言の規則で分けて引用する）</summary>
    public virtual string QuoteTable(string table) => Quote(table);

    /// <summary>日時のリテラル（行の番号から作る）</summary>
    public virtual string DateTimeLiteral(int row) =>
        $"'2024-01-{row.ToString("00", CultureInfo.InvariantCulture)} 10:00:00'";

    /// <summary>バイト列のリテラル（16 進の文字列から作る）</summary>
    public abstract string BinaryLiteral(string hex);

    /// <summary>行数を数えるための接続を作る（開くのは呼び出し側）</summary>
    public abstract DbConnection CreateConnection();

    /// <summary>
    /// 期待の行の実 DB の段の問い合わせ（同期の後の DML を含む）を流す接続を作る（開くのは呼び出し側。既定は
    /// <see cref="CreateConnection"/>＝読み取り専用の接続を作る方言だけが上書きする）
    /// </summary>
    public virtual DbConnection CreateQueryConnection() => CreateConnection();

    /// <summary>
    /// 管理者の接続を作る（開くのは呼び出し側）。期待の行の実 DB の段が、別のユーザー・スキーマの物を読むのに使う（持たない方言は
    /// <see cref="NotSupportedException"/>）
    /// </summary>
    public virtual DbConnection CreateAdminConnection() =>
        throw new NotSupportedException($"[{Name}] 管理者の接続を持たない");

    /// <summary>
    /// 管理者としてスクリプトを流す（失敗は例外）。期待の行の実 DB の段が、別のユーザー・データベースを作る・消すのに使う（持たない方言は
    /// <see cref="NotSupportedException"/>）
    /// </summary>
    public virtual Task ExecuteAdminAsync(string sql, CancellationToken ct) =>
        throw new NotSupportedException($"[{Name}] 管理者の実行を持たない");

    /// <summary>DB を空にする（種ごとに呼ぶ）</summary>
    public abstract Task ResetAsync(CancellationToken ct);

    /// <summary>
    /// 期待の行の実 DB の段の後に、製品の取込・実行器が DB のファイルを掴んだままなら問題の文を返す（掴んでいなければ
    /// <see langword="null"/>）。ファイルの DB を持たない方言は常に <see langword="null"/>
    /// </summary>
    public virtual string? HeldFileProblem() => null;

    /// <summary>
    /// DB のトリガーのうち、今は発火できないものの名前（汎用の表明 5d が同期の前後で比べる）。トリガーの判定を持たない方言は
    /// <see langword="null"/>（比べない）
    /// </summary>
    /// <remarks>判定は製品の実行器の最後の守りと同じ部品を通す（写しを作らない）。</remarks>
    public virtual System.Collections.Generic.IReadOnlySet<string>? BrokenTriggers() => null;

    /// <summary>識別子を引用する</summary>
    public abstract string Quote(string identifier);

    /// <summary>種類と長さ（精度）から方言の型名を作る</summary>
    public abstract string TypeName(FuzzTypeKind kind, int length);

    /// <summary>
    /// 整数の列 <paramref name="source"/> に 1 を足す計算列（生成列）を後から足す文。
    /// QuickER の DDL は式を持たないので、計算列は DB を作った後でこの文で足す。
    /// </summary>
    public abstract string AddComputedColumnSql(string table, string column, string source);

    /// <summary>
    /// 既存の列に既定値を足す文（<paramref name="literal"/> はその列の型のリテラル）。ALTER で既定値を足せない方言は null
    /// </summary>
    public abstract string? AddColumnDefaultSql(string table, string column, string literal);

    /// <summary>文字列のリテラル</summary>
    public virtual string StringLiteral(string value) => $"'{value.Replace("'", "''")}'";

    /// <summary>
    /// 文を同期の実行器を通さずに 1 つのコマンドで流す（<see cref="CreateQueryConnection"/> の接続・失敗は例外）。実行器の検査
    /// （SQLite の壊れたビュー・外部キー違反の増加）を通らない前提を作るためにだけ使う
    /// </summary>
    public async Task ExecuteRawAsync(string sql, CancellationToken ct)
    {
        await using var connection = CreateQueryConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>セットアップのスクリプトを同期の実行器で流す（失敗は例外）</summary>
    public async Task ExecuteSetupAsync(string script, CancellationToken ct)
    {
        var result = await Provider
            .SyncExecutor.ExecuteAsync(Settings, script, ct)
            .ConfigureAwait(false);

        if (!result.Committed)
        {
            throw new InvalidOperationException(
                $"[{Name}] setup script failed: {result.Error}\n{script}"
            );
        }
    }
}

/// <summary>SQLite（実ファイル・Docker 不要）</summary>
/// <remarks>
/// テストの側の接続（行数・探針・問い合わせ・空のファイルの作成）はプールしない。製品の取込・実行器も同じ組み方の接続文字列で
/// ファイルを開くので、テストの側がプールすると、製品がファイルを掴んだままでもテストの接続と区別できない
/// （<see cref="HeldFileProblem"/> が測れない）。
/// </remarks>
internal sealed class SqliteSyncPlanFuzzDialect : SyncPlanFuzzDialect, IDisposable
{
    private SqliteTempDatabase _db = SqliteTempDatabase.Create();

    public override string Name => DialectNames.Sqlite;

    public override IDatabaseProvider Provider { get; } = new SqliteProvider();

    public override bool PlansTableRebuilds => true;

    /// <summary>画面の取込と同じ接続文字列（接続プロファイルのパスから <see cref="SqliteConnectionStringFactory"/> で組む）</summary>
    public override string ImportConnectionString => SqliteConnectionStringFactory.Build(Settings);

    public override DbConnectionSettings Settings => new() { FilePath = _db.FilePath };

    /// <summary>
    /// 行の数・探針の読み取りの接続（照合順序 <see cref="TestCollation"/> と関数 <see cref="TestFunction"/> を登録する＝DB の外の関数を使う
    /// 仮想の生成列も読める。製品の取込・実行器の接続は登録しない）
    /// </summary>
    public override DbConnection CreateConnection() =>
        WithTestDefinitions(new SqliteConnection(NonPooled(_db.ReadOnlyConnectionString)));

    /// <summary>
    /// 問い合わせ・生の準備の接続（照合順序 <see cref="TestCollation"/> と関数 <see cref="TestFunction"/> を登録する＝DB の外で登録した
    /// 照合順序・関数を使う索引・生成列を、期待の行の生の準備で作れる。製品の取込・実行器の接続は登録しない）
    /// </summary>
    public override DbConnection CreateQueryConnection() =>
        WithTestDefinitions(new SqliteConnection(NonPooled(_db.ReadWriteConnectionString)));

    /// <summary>テストの側の接続に照合順序 <see cref="TestCollation"/> と関数 <see cref="TestFunction"/> を登録する</summary>
    /// <remarks>
    /// 関数は開いた後に登録する（開く前の登録は決定的の印を落とし、索引の式・生成列で non-deterministic functions prohibited になる）。
    /// </remarks>
    private static SqliteConnection WithTestDefinitions(SqliteConnection connection)
    {
        connection.CreateCollation(TestCollation, (a, b) => string.CompareOrdinal(a, b));
        connection.StateChange += (_, e) =>
        {
            if (e.CurrentState == System.Data.ConnectionState.Open)
            {
                connection.CreateFunction(TestFunction, (string? s) => s, isDeterministic: true);
            }
        };
        return connection;
    }

    /// <summary>テストの側の接続だけが登録する照合順序の名前（製品の接続には無い）</summary>
    public const string TestCollation = "quicker_test_collation";

    /// <summary>テストの側の接続だけが登録する関数の名前（引数の文字列をそのまま返す・製品の接続には無い）</summary>
    public const string TestFunction = "quicker_test_function";

    /// <summary>
    /// 種ごとに新しいファイルを作る（前の種のジャーナルを持ち越さない）。
    /// 同期の実行器は既存のファイルしか開かないので、ここで空のファイルを作っておく。
    /// </summary>
    public override async Task ResetAsync(CancellationToken ct)
    {
        _db.Dispose();
        _db = SqliteTempDatabase.Create();
        await using var connection = new SqliteConnection(
            NonPooled(_db.ReadWriteCreateConnectionString)
        );
        await connection.OpenAsync(ct).ConfigureAwait(false);
    }

    /// <summary>DB のファイルを排他で開けなければ問題の文を返す（テストの側の接続はプールしないので、掴んでいるのは製品の接続）</summary>
    /// <remarks>
    /// Windows では掴まれたファイルを排他で開けず、移動・削除と同じ答えになる。Linux の排他（flock）は SQLite のロック（fcntl）と
    /// 関係しないので常に開け、この表明は Windows でだけ効く（掴んだファイルを移動・削除できないのは Windows の問題）。
    /// </remarks>
    public override string? HeldFileProblem()
    {
        try
        {
            using var exclusive = File.Open(
                _db.FilePath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None
            );
            return null;
        }
        catch (IOException ex)
        {
            return $"the database file is still held by a product connection after the row ({ex.Message})";
        }
    }

    /// <summary>DB のトリガーのうち今は発火できないもの（製品の <see cref="SqliteTriggers.CollectBroken"/> を通す）</summary>
    public override System.Collections.Generic.IReadOnlySet<string>? BrokenTriggers()
    {
        using var connection = new SqliteConnection(NonPooled(_db.ReadWriteConnectionString));
        connection.Open();

        return SqliteTriggers.CollectBroken(connection, null, DbCommands.DefaultTimeoutSeconds);
    }

    /// <summary>接続文字列にプールなしを立てる</summary>
    private static string NonPooled(string connectionString) =>
        new SqliteConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString;

    public override string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";

    public override string TypeName(FuzzTypeKind kind, int length) =>
        kind switch
        {
            FuzzTypeKind.Int => "INTEGER",
            FuzzTypeKind.BigInt => "BIGINT",
            FuzzTypeKind.VarChar => $"VARCHAR({length})",
            FuzzTypeKind.Decimal => $"NUMERIC({length},2)",
            FuzzTypeKind.DateTime => "DATETIME",
            FuzzTypeKind.FixedChar => $"CHAR({length})",
            FuzzTypeKind.Binary => "BLOB",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    public override bool DistinguishesSameSetConstraints => false;

    /// <inheritdoc/>
    public override string? PrecisionlessDecimalTypeName => "NUMERIC";

    /// <summary>SQLite は宣言の型をそのまま覚え、精度を持たない（既定の精度の別の綴りも無い）</summary>
    public override int DefaultDateTimePrecision => 0;

    /// <summary>SQLite は型の長さに上限を持たない（宣言は検査されない）</summary>
    public override (FuzzTypeKind Kind, int Length)? LimitType => null;

    public override char SymbolCharacter => '"';

    public override string BinaryLiteral(string hex) => $"X'{hex}'";

    public override string AddComputedColumnSql(string table, string column, string source) =>
        $"ALTER TABLE {Quote(table)} ADD COLUMN {Quote(column)} INTEGER GENERATED ALWAYS AS ({Quote(source)} + 1) VIRTUAL;";

    /// <summary>SQLite は ALTER で既定値を足せない（表を作り直すしかない）</summary>
    public override string? AddColumnDefaultSql(string table, string column, string literal) =>
        null;

    public void Dispose() => _db.Dispose();
}

/// <summary>MySQL 8.4（Testcontainers）</summary>
internal sealed class MySqlSyncPlanFuzzDialect(MySqlContainerFixture fixture) : SyncPlanFuzzDialect
{
    public override string Name => DialectNames.MySql;

    public override bool ScenarioRowsWithdrawUniqueRebuild => true;

    public override IDatabaseProvider Provider { get; } = new MySqlProvider();

    public override string ImportConnectionString => fixture.ConnectionString;

    public override DbConnectionSettings Settings => fixture.ToDbConnectionSettings();

    public override DbConnection CreateConnection() =>
        new MySqlConnection(fixture.ConnectionString);

    public override DbConnection CreateAdminConnection() =>
        new MySqlConnection(fixture.RootConnectionString);

    public override Task ExecuteAdminAsync(string sql, CancellationToken ct) =>
        fixture.ExecuteAsRootAsync(sql, ct);

    public override Task ResetAsync(CancellationToken ct) => fixture.ResetSchemaAsync(ct);

    public override string Quote(string identifier) => $"`{identifier.Replace("`", "``")}`";

    public override string TypeName(FuzzTypeKind kind, int length) =>
        kind switch
        {
            FuzzTypeKind.Int => "int",
            FuzzTypeKind.BigInt => "bigint",
            FuzzTypeKind.VarChar => $"varchar({length})",
            FuzzTypeKind.Decimal => $"decimal({length},2)",
            FuzzTypeKind.DateTime when length == DefaultDateTimePrecision => "datetime",
            FuzzTypeKind.DateTime => $"datetime({length})",
            FuzzTypeKind.FixedChar => $"char({length})",
            FuzzTypeKind.Binary => $"varbinary({length})",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    public override int DefaultDateTimePrecision => 0;

    /// <summary>
    /// <c>char(255)</c>（固定長の文字列の上限）。可変長の文字列の上限（<c>varchar(16383)</c>）は主キーの列と合わせると
    /// 行の上限 65,535 バイトを超えるので、主キーの列を必ず持つ総当たりの表では作れない（DB の事実）
    /// </summary>
    public override (FuzzTypeKind Kind, int Length)? LimitType => (FuzzTypeKind.FixedChar, 255);

    public override string? RespelledTypeName(FuzzTypeKind kind, int length) =>
        kind == FuzzTypeKind.DateTime && length == DefaultDateTimePrecision ? "datetime(0)" : null;

    public override char SymbolCharacter => '`';

    public override string BinaryLiteral(string hex) => $"X'{hex}'";

    public override string AddComputedColumnSql(string table, string column, string source) =>
        $"ALTER TABLE {Quote(table)} ADD COLUMN {Quote(column)} int GENERATED ALWAYS AS ({Quote(source)} + 1) VIRTUAL;";

    public override string? AddColumnDefaultSql(string table, string column, string literal) =>
        $"ALTER TABLE {Quote(table)} ALTER COLUMN {Quote(column)} SET DEFAULT {literal};";
}

/// <summary>SQL Server 2022（Testcontainers）</summary>
internal sealed class SqlServerSyncPlanFuzzDialect(SqlServerContainerFixture fixture)
    : SyncPlanFuzzDialect
{
    public override string Name => DialectNames.SqlServer;

    public override IDatabaseProvider Provider { get; } = new SqlServerProvider();

    public override string ImportConnectionString => fixture.ConnectionString;

    public override DbConnectionSettings Settings => fixture.ToDbConnectionSettings();

    public override bool AllowCascade => false;

    /// <inheritdoc/>
    public override bool UniqueAllowsRepeatedNull => false;

    public override DbConnection CreateConnection() => new SqlConnection(fixture.ConnectionString);

    /// <summary>表を全部消し、別のスキーマ（<see cref="OtherSchema"/>）が無ければ作る（スキーマは消さずに使い回す）</summary>
    public override async Task ResetAsync(CancellationToken ct)
    {
        await fixture.ResetSchemaAsync(ct).ConfigureAwait(false);
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"IF SCHEMA_ID(N'{OtherSchema}') IS NULL EXEC(N'CREATE SCHEMA {Quote(OtherSchema)}');";
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public override string Quote(string identifier) => $"[{identifier.Replace("]", "]]")}]";

    public override string OtherSchema => "fz";

    public override string QuoteTable(string table) =>
        SchemaQualifiedName.Split(table) is ({ } schema, var name)
            ? $"{Quote(schema)}.{Quote(name)}"
            : Quote(table);

    public override string TypeName(FuzzTypeKind kind, int length) =>
        kind switch
        {
            FuzzTypeKind.Int => "int",
            FuzzTypeKind.BigInt => "bigint",
            FuzzTypeKind.VarChar => $"nvarchar({length})",
            FuzzTypeKind.Decimal => $"decimal({length},2)",
            FuzzTypeKind.DateTime when length == DefaultDateTimePrecision => "datetime2",
            FuzzTypeKind.DateTime => $"datetime2({length})",
            FuzzTypeKind.FixedChar => $"nchar({length})",
            FuzzTypeKind.Binary => $"varbinary({length})",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    public override int DefaultDateTimePrecision => 7;

    /// <summary>
    /// <c>nvarchar(4000)</c>（可変長の Unicode 文字列の上限）。固定長の <c>nchar(4000)</c> は 8,000 バイトの固定長の列で、
    /// ほかの列と合わせると行の上限 8,060 バイトを CREATE の時点で超えるので使わない（DB の事実）
    /// </summary>
    public override (FuzzTypeKind Kind, int Length)? LimitType => (FuzzTypeKind.VarChar, 4000);

    public override string? RespelledTypeName(FuzzTypeKind kind, int length) =>
        kind == FuzzTypeKind.DateTime && length == DefaultDateTimePrecision ? "datetime2(7)" : null;

    public override char SymbolCharacter => ']';

    public override string BinaryLiteral(string hex) => $"0x{hex}";

    public override string StringLiteral(string value) => "N" + base.StringLiteral(value);

    public override string AddComputedColumnSql(string table, string column, string source) =>
        $"ALTER TABLE {QuoteTable(table)} ADD {Quote(column)} AS ({Quote(source)} + 1);";

    public override string? AddColumnDefaultSql(string table, string column, string literal) =>
        $"ALTER TABLE {QuoteTable(table)} ADD CONSTRAINT {Quote($"DF_{table}_{column}")} DEFAULT ({literal}) FOR {Quote(column)};";
}

/// <summary>PostgreSQL（Testcontainers）</summary>
internal sealed class PostgreSqlSyncPlanFuzzDialect(PostgreSqlContainerFixture fixture)
    : SyncPlanFuzzDialect
{
    public override string Name => DialectNames.PostgreSql;

    public override IDatabaseProvider Provider { get; } = new PostgreSqlProvider();

    public override DbConnection CreateConnection() =>
        new NpgsqlConnection(fixture.ConnectionString);

    /// <inheritdoc/>
    public override string? PrecisionlessDecimalTypeName => "numeric";

    public override string ImportConnectionString => fixture.ConnectionString;

    public override DbConnectionSettings Settings => fixture.ToDbConnectionSettings();

    public override Task ResetAsync(CancellationToken ct) => fixture.ResetSchemaAsync(ct);

    public override string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";

    public override string TypeName(FuzzTypeKind kind, int length) =>
        kind switch
        {
            FuzzTypeKind.Int => "integer",
            FuzzTypeKind.BigInt => "bigint",
            FuzzTypeKind.VarChar => $"varchar({length})",
            FuzzTypeKind.Decimal => $"numeric({length},2)",
            FuzzTypeKind.DateTime when length == DefaultDateTimePrecision => "timestamp",
            FuzzTypeKind.DateTime => $"timestamp({length})",
            FuzzTypeKind.FixedChar => $"char({length})",
            FuzzTypeKind.Binary => "bytea",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    public override int DefaultDateTimePrecision => 6;

    /// <summary><c>varchar(10485760)</c>（長さの上限）</summary>
    public override (FuzzTypeKind Kind, int Length)? LimitType => (FuzzTypeKind.VarChar, 10485760);

    public override string? RespelledTypeName(FuzzTypeKind kind, int length) =>
        kind == FuzzTypeKind.DateTime && length == DefaultDateTimePrecision ? "timestamp(6)" : null;

    public override char SymbolCharacter => '"';

    public override string BinaryLiteral(string hex) => $"decode('{hex}', 'hex')";

    public override string AddComputedColumnSql(string table, string column, string source) =>
        $"ALTER TABLE {Quote(table)} ADD COLUMN {Quote(column)} integer GENERATED ALWAYS AS ({Quote(source)} + 1) STORED;";

    public override string? AddColumnDefaultSql(string table, string column, string literal) =>
        $"ALTER TABLE {Quote(table)} ALTER COLUMN {Quote(column)} SET DEFAULT {literal};";
}

/// <summary>Oracle（Testcontainers・既定では流さない＝<see cref="OracleSyncPlanFuzzTests"/> を参照）</summary>
internal sealed class OracleSyncPlanFuzzDialect(OracleContainerFixture fixture)
    : SyncPlanFuzzDialect
{
    public override string Name => DialectNames.Oracle;

    /// <inheritdoc/>
    public override bool UniqueComparesPartiallyNullKeys => true;

    public override bool AllowDuplicateForeignKeys => false;

    public override bool AllowDuplicateUniqueConstraints => false;

    /// <inheritdoc/>
    public override string? PrecisionlessDecimalTypeName => "NUMBER";

    /// <inheritdoc/>
    /// <remarks>固定長は <c>CHAR(n CHAR)</c>・可変長は <c>NVARCHAR2(n)</c> で系統が違うので、同じ系統の相手（<c>VARCHAR2(n CHAR)</c>・<c>NCHAR(n)</c>）を返す</remarks>
    public override string SwappedLengthTypeName(FuzzTypeKind kind, int length) =>
        kind == FuzzTypeKind.FixedChar ? $"VARCHAR2({length} CHAR)" : $"NCHAR({length})";

    public override IDatabaseProvider Provider { get; } = new OracleProvider();

    public override DbConnection CreateConnection() =>
        new OracleConnection(fixture.ConnectionString);

    public override DbConnection CreateAdminConnection() =>
        new OracleConnection(fixture.SystemConnectionString);

    public override Task ExecuteAdminAsync(string sql, CancellationToken ct) =>
        fixture.ExecuteAsSystemAsync(sql, ct);

    public override string ImportConnectionString => fixture.ConnectionString;

    public override DbConnectionSettings Settings => fixture.ToDbConnectionSettings();

    public override Task ResetAsync(CancellationToken ct) => fixture.ResetSchemaAsync(ct);

    public override string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";

    public override string TypeName(FuzzTypeKind kind, int length) =>
        kind switch
        {
            FuzzTypeKind.Int => "NUMBER(10)",
            FuzzTypeKind.BigInt => "NUMBER(19)",
            FuzzTypeKind.VarChar => $"NVARCHAR2({length})",
            FuzzTypeKind.Decimal => $"NUMBER({length},2)",
            FuzzTypeKind.DateTime when length == DefaultDateTimePrecision => "TIMESTAMP",
            FuzzTypeKind.DateTime => $"TIMESTAMP({length})",
            // 長さの単位語つき（取込は char_used = 'C' の列にだけ単位語を書く）
            FuzzTypeKind.FixedChar => $"CHAR({length} CHAR)",
            FuzzTypeKind.Binary => $"RAW({length})",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    public override int DefaultDateTimePrecision => 6;

    /// <summary><c>NVARCHAR2(2000)</c>（<c>MAX_STRING_SIZE = STANDARD</c> の上限）</summary>
    public override (FuzzTypeKind Kind, int Length)? LimitType => (FuzzTypeKind.VarChar, 2000);

    public override string? RespelledTypeName(FuzzTypeKind kind, int length) =>
        kind == FuzzTypeKind.DateTime && length == DefaultDateTimePrecision ? "TIMESTAMP(6)" : null;

    public override char SymbolCharacter => '\'';

    public override string DateTimeLiteral(int row) => "TIMESTAMP " + base.DateTimeLiteral(row);

    public override string BinaryLiteral(string hex) => $"HEXTORAW('{hex}')";

    public override string AddComputedColumnSql(string table, string column, string source) =>
        $"ALTER TABLE {Quote(table)} ADD ({Quote(column)} NUMBER(10) GENERATED ALWAYS AS ({Quote(source)} + 1) VIRTUAL);";

    public override string? AddColumnDefaultSql(string table, string column, string literal) =>
        $"ALTER TABLE {Quote(table)} MODIFY ({Quote(column)} DEFAULT {literal});";
}
