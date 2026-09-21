using AwesomeAssertions;
using QuickER.Provider;
using QuickER.Provider.PostgreSql;
using Xunit;

namespace QuickER.Tests.Provider.PostgreSql;

/// <summary>
/// <see cref="PostgreSqlSchemaImporter.NormalizeFormatType"/>（<c>format_type()</c> の正準表記を
/// QuickER の短縮語彙へ寄せる規則）の単体テスト。
/// </summary>
/// <remarks>
/// 入力側は実 PostgreSQL 16 で <c>format_type(atttypid, atttypmod)</c> が実際に返した表記を採っている。
/// 「その表記がそのまま DDL として通り、再取込で同じ表記へ戻る（不動点）」ことは実 DB 側の
/// <c>PostgreSqlDdlRoundTripIntegrationTests</c> が固定する。
/// </remarks>
public class PostgreSqlSchemaImporterTests
{
    [Theory(
        DisplayName = "NormalizeFormatType: 正準表記を短縮語彙へ正規化する（変えるのは 5 規則だけ）"
    )]
    // --- 素通し（規則に当たらない型は 1 文字も変えない） ---
    [InlineData("boolean", "boolean")]
    [InlineData("smallint", "smallint")]
    [InlineData("integer", "integer")]
    [InlineData("bigint", "bigint")]
    [InlineData("real", "real")]
    [InlineData("double precision", "double precision")]
    [InlineData("money", "money")]
    [InlineData("text", "text")]
    [InlineData("bytea", "bytea")]
    [InlineData("date", "date")]
    [InlineData("uuid", "uuid")]
    [InlineData("xml", "xml")]
    [InlineData("json", "json")]
    [InlineData("jsonb", "jsonb")]
    [InlineData("inet", "inet")]
    [InlineData("cidr", "cidr")]
    [InlineData("macaddr", "macaddr")]
    [InlineData("point", "point")]
    [InlineData("tsvector", "tsvector")]
    [InlineData("numeric", "numeric")]
    [InlineData("numeric(10,2)", "numeric(10,2)")]
    // --- 規則 1: character varying / character → varchar / char ---
    [InlineData("character varying(50)", "varchar(50)")]
    [InlineData("character varying(100)", "varchar(100)")]
    [InlineData("character varying(255)", "varchar(255)")]
    [InlineData("character varying(320)", "varchar(320)")]
    [InlineData("character varying", "varchar")]
    [InlineData("character(10)", "char(10)")]
    [InlineData("character(5)", "char(5)")]
    [InlineData("character(1)", "char(1)")]
    // --- 規則 2/3: timestamp（無修飾は既定精度 6） ---
    [InlineData("timestamp without time zone", "timestamp(6)")]
    [InlineData("timestamp(3) without time zone", "timestamp(3)")]
    [InlineData("timestamp(0) without time zone", "timestamp(0)")]
    [InlineData("timestamp with time zone", "timestamptz(6)")]
    [InlineData("timestamp(3) with time zone", "timestamptz(3)")]
    // --- 規則 4: time without time zone のみ。with time zone 側は従来語彙のまま触らない ---
    [InlineData("time without time zone", "time(6)")]
    [InlineData("time(3) without time zone", "time(3)")]
    [InlineData("time with time zone", "time with time zone")]
    [InlineData("time(3) with time zone", "time(3) with time zone")]
    // --- 規則 5: スケール 0 は畳む（負のスケールは畳まない＝宣言どおり） ---
    [InlineData("numeric(18,0)", "numeric(18)")]
    [InlineData("numeric(10,0)", "numeric(10)")]
    [InlineData("numeric(10,-2)", "numeric(10,-2)")]
    // --- 従来 information_schema 経由では表せず落ちていた修飾（素通しで拾う） ---
    [InlineData("bit(8)", "bit(8)")]
    [InlineData("bit(1)", "bit(1)")]
    [InlineData("bit varying(16)", "bit varying(16)")]
    [InlineData("bit varying", "bit varying")]
    [InlineData("interval", "interval")]
    [InlineData("interval day to second(3)", "interval day to second(3)")]
    [InlineData("interval year to month", "interval year to month")]
    // --- 配列は要素型へ同じ規則を当てて [] を付け直す ---
    [InlineData("integer[]", "integer[]")]
    [InlineData("text[]", "text[]")]
    [InlineData("numeric(10,2)[]", "numeric(10,2)[]")]
    [InlineData("character varying(20)[]", "varchar(20)[]")]
    [InlineData("character(4)[]", "char(4)[]")]
    [InlineData("numeric(8,0)[]", "numeric(8)[]")]
    [InlineData("time without time zone[]", "time(6)[]")]
    [InlineData("timestamp without time zone[]", "timestamp(6)[]")]
    [InlineData("timestamp(3) with time zone[]", "timestamptz(3)[]")]
    // --- ユーザー定義型（列挙型）は名前をそのまま。引用が要る名前は引用込みで持ち帰る ---
    [InlineData("mood", "mood")]
    [InlineData("\"odd-mood\"", "\"odd-mood\"")]
    public void NormalizeFormatType_Cases(string formatType, string expected)
    {
        PostgreSqlSchemaImporter.NormalizeFormatType(formatType).Should().Be(expected);
    }

    [Theory(
        DisplayName = "NormalizeFormatType: 正規化後の表記をもう一度通しても変わらない（不動点）"
    )]
    [InlineData("varchar(50)")]
    [InlineData("char(10)")]
    [InlineData("timestamp(6)")]
    [InlineData("timestamptz(3)")]
    [InlineData("time(6)")]
    [InlineData("time with time zone")]
    [InlineData("numeric(18)")]
    [InlineData("numeric(10,-2)")]
    [InlineData("varchar(20)[]")]
    [InlineData("bit(8)")]
    public void NormalizeFormatType_IsIdempotent(string normalized)
    {
        PostgreSqlSchemaImporter
            .NormalizeFormatType(normalized)
            .Should()
            .Be(normalized, "正規化済みの表記をもう一度正規化しても変わってはいけない");
    }

    [Theory(DisplayName = "引用が要るユーザー定義型名は素通しするが、DDL へは出せない表記になる")]
    // format_type は引用が要る型名を引用込みで返す。表記としては忠実だが SqlTypeText は引用符を通さない
    // ＝図へ黙って入れると後で DDL を叩いた瞬間に図全体が止まるので、取込は
    // SchemaImportWarningKind.ColumnTypeNotEmittable で名指しする（検知の前提をここで固定する）
    [InlineData("\"odd-mood\"")]
    [InlineData("\"Mood\"")]
    [InlineData("\"od;d\"")]
    public void NormalizeFormatType_QuotedUserDefinedTypes_ArePassedThroughButNotEmittable(
        string formatType
    )
    {
        PostgreSqlSchemaImporter.NormalizeFormatType(formatType).Should().Be(formatType);
        SqlTypeText.IsSafe(formatType).Should().BeFalse();
    }

    [Fact(DisplayName = "PostGIS の空間型は素通しし、DDL へ出せる表記のままである")]
    public void NormalizeFormatType_PostGisTypes_ArePassedThroughAndEmittable()
    {
        string[] spatialTypes =
        [
            "geometry",
            "geometry(Point,4326)",
            "geometry(MultiPolygon)",
            "geography(Point,4326)",
        ];

        foreach (var type in spatialTypes)
        {
            PostgreSqlSchemaImporter.NormalizeFormatType(type).Should().Be(type);
            SqlTypeText
                .IsSafe(type)
                .Should()
                .BeTrue($"'{type}' が弾かれると、この 1 列のせいで図全体の DDL 生成が止まる");
        }
    }
}
