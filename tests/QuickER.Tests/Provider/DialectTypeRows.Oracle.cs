using QuickER.Dialects;
using QuickER.Provider;
using static QuickER.Tests.Provider.DialectTypeRow;

namespace QuickER.Tests.Provider;

internal static partial class DialectTypeRows
{
    private static IEnumerable<DialectTypeRow> OracleRows() =>
        [
            Parse(
                    "parse-oracle-number-p1-integer-kind",
                    "NUMBER(p) は精度により整数型へ振り分けられる",
                    "NUMBER(1)"
                )
                .Expect(DialectNames.Oracle, "ok kind=Boolean"),
            Parse(
                    "parse-oracle-number-p3-integer-kind",
                    "NUMBER(p) は精度により整数型へ振り分けられる",
                    "NUMBER(3)"
                )
                .Expect(DialectNames.Oracle, "ok kind=TinyInt"),
            Parse(
                    "parse-oracle-number-p5-integer-kind",
                    "NUMBER(p) は精度により整数型へ振り分けられる",
                    "NUMBER(5)"
                )
                .Expect(DialectNames.Oracle, "ok kind=SmallInt"),
            Parse(
                    "parse-oracle-number-p10-integer-kind",
                    "NUMBER(p) は精度により整数型へ振り分けられる",
                    "NUMBER(10)"
                )
                .Expect(DialectNames.Oracle, "ok kind=Int32"),
            Parse(
                    "parse-oracle-number-p19-integer-kind",
                    "NUMBER(p) は精度により整数型へ振り分けられる",
                    "NUMBER(19)"
                )
                .Expect(DialectNames.Oracle, "ok kind=Int64"),
            Parse(
                    "parse-oracle-number-scale-decimal",
                    "NUMBER(p,s)（s>0）は Decimal(p,s) として解析される",
                    "NUMBER(10,2)"
                )
                .Expect(DialectNames.Oracle, "ok kind=Decimal p=10 s=2"),
            Parse(
                    "parse-oracle-number-negative-scale-decimal",
                    "NUMBER(p,-s)（負のスケール）も Decimal として解析され同じ表記へ戻る",
                    "NUMBER(10,-2)"
                )
                .Expect(DialectNames.Oracle, "ok kind=Decimal p=10 s=-2"),
            RoundTripText(
                    "roundtrip-oracle-number-negative-scale-same",
                    "NUMBER(p,-s)（負のスケール）も Decimal として解析され同じ表記へ戻る",
                    "NUMBER(10,-2)"
                )
                .Expect(DialectNames.Oracle, "same"),
            Parse(
                    "parse-oracle-number-star-scale-precisionless",
                    "NUMBER(*,s)（精度は最大）も解析できる（精度は未指定として読む）",
                    "NUMBER(*,2)"
                )
                .Expect(DialectNames.Oracle, "ok kind=Decimal p=- s=2"),
            Parse(
                    "parse-oracle-negative-number-length-fails",
                    "符号を許すのはスケールだけ（負の長さ・精度は従来どおり変換不能）",
                    "NUMBER(-5)"
                )
                .Boundary(DialectTypeBoundaries.InvalidArg)
                .Expect(DialectNames.Oracle, "fail"),
            Parse(
                    "parse-oracle-negative-varchar2-length-fails",
                    "符号を許すのはスケールだけ（負の長さ・精度は従来どおり変換不能）",
                    "VARCHAR2(-5)"
                )
                .Boundary(DialectTypeBoundaries.InvalidArg)
                .Expect(DialectNames.Oracle, "fail"),
            Parse(
                    "parse-oracle-negative-number-precision-with-scale-fails",
                    "符号を許すのはスケールだけ（負の長さ・精度は従来どおり変換不能）",
                    "NUMBER(-10,2)"
                )
                .Boundary(DialectTypeBoundaries.InvalidArg)
                .Expect(DialectNames.Oracle, "fail"),
            Parse(
                    "parse-oracle-varchar2-char-unit-length",
                    "長さの単位語（BYTE / CHAR）付きの表記も解析できる（単位は正規型に無いため捨てる）",
                    "VARCHAR2(50 CHAR)"
                )
                .Expect(DialectNames.Oracle, "ok kind=AnsiString len=50"),
            Parse(
                    "parse-oracle-varchar2-byte-unit-length",
                    "長さの単位語（BYTE / CHAR）付きの表記も解析できる（単位は正規型に無いため捨てる）",
                    "VARCHAR2(50 BYTE)"
                )
                .Expect(DialectNames.Oracle, "ok kind=AnsiString len=50"),
            Parse(
                    "parse-oracle-char-char-unit-length",
                    "長さの単位語（BYTE / CHAR）付きの表記も解析できる（単位は正規型に無いため捨てる）",
                    "CHAR(10 CHAR)"
                )
                .Expect(DialectNames.Oracle, "ok kind=AnsiFixedString len=10"),
            Parse(
                    "parse-oracle-number-other-precision-decimal-scale0",
                    "NUMBER(p)（整数型に該当しない精度）は Decimal(p,0) として解析される",
                    "NUMBER(7)"
                )
                .Expect(DialectNames.Oracle, "ok kind=Decimal p=7 s=0"),
            Parse(
                    "parse-oracle-number-noargs-decimal-nulls",
                    "NUMBER（無引数）は精度・スケール null の Decimal として解析される",
                    "NUMBER"
                )
                .Expect(DialectNames.Oracle, "ok kind=Decimal p=- s=-"),
            Parse(
                    "parse-oracle-floating-binary-float-kind",
                    "浮動小数点型を正規型へ解析できる",
                    "BINARY_FLOAT"
                )
                .Expect(DialectNames.Oracle, "ok kind=Float32"),
            Parse(
                    "parse-oracle-floating-binary-double-kind",
                    "浮動小数点型を正規型へ解析できる",
                    "BINARY_DOUBLE"
                )
                .Expect(DialectNames.Oracle, "ok kind=Float64"),
            Parse("parse-oracle-floating-float-kind", "浮動小数点型を正規型へ解析できる", "FLOAT")
                .Expect(DialectNames.Oracle, "ok kind=Float64"),
            Parse(
                    "parse-oracle-floating-float-63-kind",
                    "浮動小数点型を正規型へ解析できる",
                    "FLOAT(63)"
                )
                .Expect(DialectNames.Oracle, "ok kind=Float64"),
            Parse(
                    "parse-oracle-length-nvarchar2-50-kind-length",
                    "文字列・LOB・バイナリ型を正規型へ解析できる",
                    "NVARCHAR2(50)"
                )
                .Expect(DialectNames.Oracle, "ok kind=String len=50"),
            Parse(
                    "parse-oracle-length-varchar2-50-kind-length",
                    "文字列・LOB・バイナリ型を正規型へ解析できる",
                    "VARCHAR2(50)"
                )
                .Expect(DialectNames.Oracle, "ok kind=AnsiString len=50"),
            Parse(
                    "parse-oracle-length-nchar-10-kind-length",
                    "文字列・LOB・バイナリ型を正規型へ解析できる",
                    "NCHAR(10)"
                )
                .Expect(DialectNames.Oracle, "ok kind=FixedString len=10"),
            Parse(
                    "parse-oracle-length-char-10-kind-length",
                    "文字列・LOB・バイナリ型を正規型へ解析できる",
                    "CHAR(10)"
                )
                .Expect(DialectNames.Oracle, "ok kind=AnsiFixedString len=10"),
            Parse(
                    "parse-oracle-length-raw-16-kind-length",
                    "文字列・LOB・バイナリ型を正規型へ解析できる",
                    "RAW(16)"
                )
                .Expect(DialectNames.Oracle, "ok kind=Binary len=16"),
            Parse(
                    "parse-oracle-lob-nclob-max-length",
                    "LOB 型は max（-1）長として解析される",
                    "NCLOB"
                )
                .Expect(DialectNames.Oracle, "ok kind=String len=-1"),
            Parse(
                    "parse-oracle-lob-clob-max-length",
                    "LOB 型は max（-1）長として解析される",
                    "CLOB"
                )
                .Expect(DialectNames.Oracle, "ok kind=AnsiString len=-1"),
            Parse(
                    "parse-oracle-lob-blob-max-length",
                    "LOB 型は max（-1）長として解析される",
                    "BLOB"
                )
                .Expect(DialectNames.Oracle, "ok kind=Binary len=-1"),
            Parse(
                    "parse-oracle-date-datetime",
                    "DATE は（時刻を含むため）DateTime として解析される",
                    "DATE"
                )
                .Expect(DialectNames.Oracle, "ok kind=DateTime"),
            Parse(
                    "parse-oracle-timestamp-precision-datetime",
                    "TIMESTAMP(p) は精度付き DateTime として解析される",
                    "TIMESTAMP(6)"
                )
                .Expect(DialectNames.Oracle, "ok kind=DateTime p=6"),
            Parse(
                    "parse-oracle-timestamp-with-time-zone-datetimeoffset",
                    "TIMESTAMP(p) WITH TIME ZONE は精度付き DateTimeOffset として解析される",
                    "TIMESTAMP(6) WITH TIME ZONE"
                )
                .Expect(DialectNames.Oracle, "ok kind=DateTimeOffset p=6"),
            Parse(
                    "parse-oracle-timestamp-with-local-time-zone-datetimeoffset",
                    "TIMESTAMP WITH LOCAL TIME ZONE は DateTimeOffset として解析される（解釈のみ）",
                    "TIMESTAMP(6) WITH LOCAL TIME ZONE"
                )
                .Expect(DialectNames.Oracle, "ok kind=DateTimeOffset"),
            Parse("parse-oracle-xmltype-xml", "XMLTYPE は Xml として解析される", "XMLTYPE")
                .Expect(DialectNames.Oracle, "ok kind=Xml"),
            Parse(
                    "parse-oracle-case-insensitive-whitespace",
                    "大文字小文字・空白を許容して解析できる",
                    "  nvarchar2( 100 ) "
                )
                .Expect(DialectNames.Oracle, "ok kind=String len=100"),
            Parse(
                    "parse-oracle-legacy-ansi-long-canonical",
                    "LONG・LONG RAW は無制限の型、INTEGER・SMALLINT は NUMBER(*,0) として解析される",
                    "LONG"
                )
                .Expect(DialectNames.Oracle, "ok kind=AnsiString len=-1 p=- s=-"),
            Parse(
                    "parse-oracle-legacy-ansi-long-raw-canonical",
                    "LONG・LONG RAW は無制限の型、INTEGER・SMALLINT は NUMBER(*,0) として解析される",
                    "LONG RAW"
                )
                .Expect(DialectNames.Oracle, "ok kind=Binary len=-1 p=- s=-"),
            Parse(
                    "parse-oracle-legacy-ansi-integer-canonical",
                    "LONG・LONG RAW は無制限の型、INTEGER・SMALLINT は NUMBER(*,0) として解析される",
                    "INTEGER"
                )
                .Expect(DialectNames.Oracle, "ok kind=Decimal len=- p=- s=0"),
            Parse(
                    "parse-oracle-legacy-ansi-smallint-canonical",
                    "LONG・LONG RAW は無制限の型、INTEGER・SMALLINT は NUMBER(*,0) として解析される",
                    "SMALLINT"
                )
                .Expect(DialectNames.Oracle, "ok kind=Decimal len=- p=- s=0"),
            Parse(
                    "parse-oracle-legacy-ansi-integer-canonical-lowercase",
                    "LONG・LONG RAW は無制限の型、INTEGER・SMALLINT は NUMBER(*,0) として解析される",
                    "integer"
                )
                .Expect(DialectNames.Oracle, "ok kind=Decimal len=- p=- s=0"),
            Parse(
                    "parse-oracle-unconvertible-rowid-fails",
                    "変換不能な型は TryParse が false を返す",
                    "ROWID"
                )
                .Expect(DialectNames.Oracle, "fail"),
            Parse(
                    "parse-oracle-unconvertible-urowid-fails",
                    "変換不能な型は TryParse が false を返す",
                    "UROWID"
                )
                .Expect(DialectNames.Oracle, "fail"),
            Parse(
                    "parse-oracle-unconvertible-bfile-fails",
                    "変換不能な型は TryParse が false を返す",
                    "BFILE"
                )
                .Expect(DialectNames.Oracle, "fail"),
            Parse(
                    "parse-oracle-unconvertible-interval-year-to-month-fails",
                    "変換不能な型は TryParse が false を返す",
                    "INTERVAL YEAR TO MONTH"
                )
                .Expect(DialectNames.Oracle, "fail"),
            Parse(
                    "parse-oracle-unconvertible-interval-day-to-second-fails",
                    "変換不能な型は TryParse が false を返す",
                    "INTERVAL DAY TO SECOND"
                )
                .Expect(DialectNames.Oracle, "fail"),
            Parse(
                    "parse-oracle-unconvertible-sdo-geometry-fails",
                    "変換不能な型は TryParse が false を返す",
                    "SDO_GEOMETRY"
                )
                .Expect(DialectNames.Oracle, "fail"),
            Parse(
                    "parse-oracle-unconvertible-no-such-type-fails",
                    "変換不能な型は TryParse が false を返す",
                    "no_such_type"
                )
                .Expect(DialectNames.Oracle, "fail"),
            Parse(
                    "parse-oracle-unconvertible-number-5-fails",
                    "変換不能な型は TryParse が false を返す",
                    "NUMBER(-5)"
                )
                .Boundary(DialectTypeBoundaries.InvalidArg)
                .Expect(DialectNames.Oracle, "fail"),
            Parse(
                    "parse-oracle-unconvertible-number-99999999999-fails",
                    "変換不能な型は TryParse が false を返す",
                    "NUMBER(99999999999)"
                )
                .Boundary(DialectTypeBoundaries.InvalidArg)
                .Expect(DialectNames.Oracle, "fail"),
            Parse(
                    "parse-oracle-unconvertible-varchar2-1-fails",
                    "変換不能な型は TryParse が false を返す",
                    "VARCHAR2(-1)"
                )
                .Boundary(DialectTypeBoundaries.InvalidArg)
                .Expect(DialectNames.Oracle, "fail"),
            Parse(
                    "parse-oracle-number-star-unconstrained-decimal",
                    "NUMBER(*) は素の NUMBER と同じ精度・スケールなしの Decimal",
                    "NUMBER(*)"
                )
                .Expect(DialectNames.Oracle, "ok kind=Decimal len=- p=- s=-"),
            Format(
                    "format-oracle-integer-boolean-number",
                    "整数系の正規型は精度付き NUMBER を生成する",
                    Ct(CanonicalTypeKind.Boolean)
                )
                .Expect(DialectNames.Oracle, "ok NUMBER(1)"),
            Format(
                    "format-oracle-integer-tinyint-number",
                    "整数系の正規型は精度付き NUMBER を生成する",
                    Ct(CanonicalTypeKind.TinyInt)
                )
                .Expect(DialectNames.Oracle, "ok NUMBER(3)"),
            Format(
                    "format-oracle-integer-smallint-number",
                    "整数系の正規型は精度付き NUMBER を生成する",
                    Ct(CanonicalTypeKind.SmallInt)
                )
                .Expect(DialectNames.Oracle, "ok NUMBER(5)"),
            Format(
                    "format-oracle-integer-int32-number",
                    "整数系の正規型は精度付き NUMBER を生成する",
                    Ct(CanonicalTypeKind.Int32)
                )
                .Expect(DialectNames.Oracle, "ok NUMBER(10)"),
            Format(
                    "format-oracle-integer-int64-number",
                    "整数系の正規型は精度付き NUMBER を生成する",
                    Ct(CanonicalTypeKind.Int64)
                )
                .Expect(DialectNames.Oracle, "ok NUMBER(19)"),
            Format(
                    "format-oracle-decimal-number",
                    "TryFormat(Decimal(p,s)) は NUMBER(p,s) を生成する",
                    Ct(CanonicalTypeKind.Decimal, precision: 12, scale: 4)
                )
                .Expect(DialectNames.Oracle, "ok NUMBER(12,4)"),
            Format(
                    "format-oracle-money-number19-4",
                    "TryFormat(Money) は NUMBER(19,4) を生成する",
                    Ct(CanonicalTypeKind.Money)
                )
                .Expect(DialectNames.Oracle, "ok NUMBER(19,4)"),
            Format(
                    "format-oracle-float32-binary-float",
                    "浮動小数点の正規型は BINARY_FLOAT / BINARY_DOUBLE を生成する",
                    Ct(CanonicalTypeKind.Float32)
                )
                .Expect(DialectNames.Oracle, "ok BINARY_FLOAT"),
            Format(
                    "format-oracle-float64-binary-double",
                    "浮動小数点の正規型は BINARY_FLOAT / BINARY_DOUBLE を生成する",
                    Ct(CanonicalTypeKind.Float64)
                )
                .Expect(DialectNames.Oracle, "ok BINARY_DOUBLE"),
            Format(
                    "format-oracle-string-string-50",
                    "文字列系の正規型は Oracle の文字列型を生成する",
                    Ct(CanonicalTypeKind.String, length: 50)
                )
                .Expect(DialectNames.Oracle, "ok NVARCHAR2(50)"),
            Format(
                    "format-oracle-string-string-max",
                    "文字列系の正規型は Oracle の文字列型を生成する",
                    Ct(CanonicalTypeKind.String, length: -1)
                )
                .Expect(DialectNames.Oracle, "ok NCLOB"),
            Format(
                    "format-oracle-string-ansistring-50",
                    "文字列系の正規型は Oracle の文字列型を生成する",
                    Ct(CanonicalTypeKind.AnsiString, length: 50)
                )
                .Expect(DialectNames.Oracle, "ok VARCHAR2(50)"),
            Format(
                    "format-oracle-string-ansistring-max",
                    "文字列系の正規型は Oracle の文字列型を生成する",
                    Ct(CanonicalTypeKind.AnsiString, length: -1)
                )
                .Expect(DialectNames.Oracle, "ok CLOB"),
            Format(
                    "format-oracle-string-fixedstring-10",
                    "文字列系の正規型は Oracle の文字列型を生成する",
                    Ct(CanonicalTypeKind.FixedString, length: 10)
                )
                .Expect(DialectNames.Oracle, "ok NCHAR(10)"),
            Format(
                    "format-oracle-string-ansifixedstring-10",
                    "文字列系の正規型は Oracle の文字列型を生成する",
                    Ct(CanonicalTypeKind.AnsiFixedString, length: 10)
                )
                .Expect(DialectNames.Oracle, "ok CHAR(10)"),
            Format(
                    "format-oracle-binary-raw16",
                    "バイナリの正規型は RAW(n) / BLOB を生成する",
                    Ct(CanonicalTypeKind.Binary, length: 16)
                )
                .Expect(DialectNames.Oracle, "ok RAW(16)"),
            Format(
                    "format-oracle-binary-max-blob",
                    "バイナリの正規型は RAW(n) / BLOB を生成する",
                    Ct(CanonicalTypeKind.Binary, length: -1)
                )
                .Expect(DialectNames.Oracle, "ok BLOB"),
            Format(
                    "format-oracle-fixedbinary-raw16",
                    "バイナリの正規型は RAW(n) / BLOB を生成する",
                    Ct(CanonicalTypeKind.FixedBinary, length: 16)
                )
                .Expect(DialectNames.Oracle, "ok RAW(16)"),
            Format(
                    "format-oracle-date-date",
                    "TryFormat(Date) は DATE を生成する（DATE の非対称変換）",
                    Ct(CanonicalTypeKind.Date)
                )
                .Expect(DialectNames.Oracle, "ok DATE"),
            Format(
                    "format-oracle-datetime-precision-timestamp",
                    "TryFormat(DateTime, 精度あり) は TIMESTAMP(p) を生成する",
                    Ct(CanonicalTypeKind.DateTime, precision: 6)
                )
                .Expect(DialectNames.Oracle, "ok TIMESTAMP(6)"),
            Format(
                    "format-oracle-datetimeoffset-precision-timestamp-tz",
                    "TryFormat(DateTimeOffset, 精度あり) は TIMESTAMP(p) WITH TIME ZONE を生成する",
                    Ct(CanonicalTypeKind.DateTimeOffset, precision: 6)
                )
                .Expect(DialectNames.Oracle, "ok TIMESTAMP(6) WITH TIME ZONE"),
            Format(
                    "format-oracle-time-fails",
                    "TryFormat(Time) は false（Oracle に TIME 型が無い）",
                    Ct(CanonicalTypeKind.Time)
                )
                .Expect(DialectNames.Oracle, "fail"),
            Format(
                    "format-oracle-guid-raw16",
                    "TryFormat(Guid) は RAW(16) を生成する（format-only）",
                    Ct(CanonicalTypeKind.Guid)
                )
                .Expect(DialectNames.Oracle, "ok RAW(16)"),
            Format(
                    "format-oracle-xml-xmltype",
                    "TryFormat(Xml) は XMLTYPE を生成する",
                    Ct(CanonicalTypeKind.Xml)
                )
                .Expect(DialectNames.Oracle, "ok XMLTYPE"),
            Format(
                    "format-oracle-json-clob",
                    "TryFormat(Json) は CLOB を生成する（19c に JSON 型が無い・format-only）",
                    Ct(CanonicalTypeKind.Json)
                )
                .Expect(DialectNames.Oracle, "ok CLOB"),
            Format(
                    "format-oracle-decimal-precisionless-scale2",
                    "精度なし・スケールありの Decimal は NUMBER(*,s) へ書き出す",
                    Ct(CanonicalTypeKind.Decimal, scale: 2)
                )
                .Boundary(DialectTypeBoundaries.Precisionless)
                .Expect(DialectNames.Oracle, "ok NUMBER(*,2)"),
            Format(
                    "format-oracle-decimal-precisionless-scale0",
                    "精度なし・スケールありの Decimal は NUMBER(*,s) へ書き出す",
                    Ct(CanonicalTypeKind.Decimal, scale: 0)
                )
                .Boundary(DialectTypeBoundaries.Precisionless)
                .Expect(DialectNames.Oracle, "ok NUMBER(*,0)"),
            Format(
                    "format-oracle-decimal-precisionless-noscale",
                    "精度なし・スケールありの Decimal は NUMBER(*,s) へ書き出す",
                    Ct(CanonicalTypeKind.Decimal)
                )
                .Boundary(DialectTypeBoundaries.Precisionless)
                .Expect(DialectNames.Oracle, "ok NUMBER"),
            Format(
                    "format-oracle-limit-string-2001-fails",
                    "TryFormat は Oracle の上限を超える値を書き出さない",
                    Ct(CanonicalTypeKind.String, length: 2001)
                )
                .Boundary(DialectTypeBoundaries.OverLimit)
                .Expect(DialectNames.Oracle, "fail"),
            Format(
                    "format-oracle-limit-string-2000-ok",
                    "TryFormat は Oracle の上限を超える値を書き出さない",
                    Ct(CanonicalTypeKind.String, length: 2000)
                )
                .Boundary(DialectTypeBoundaries.AtLimit)
                .Expect(DialectNames.Oracle, "ok"),
            Format(
                    "format-oracle-limit-string-4000-fails",
                    "TryFormat は Oracle の上限を超える値を書き出さない",
                    Ct(CanonicalTypeKind.String, length: 4000)
                )
                .Boundary(DialectTypeBoundaries.OverLimit)
                .Expect(DialectNames.Oracle, "fail"),
            Format(
                    "format-oracle-limit-string-max-ok",
                    "TryFormat は Oracle の上限を超える値を書き出さない",
                    Ct(CanonicalTypeKind.String, length: -1)
                )
                .Expect(DialectNames.Oracle, "ok"),
            Format(
                    "format-oracle-limit-string-0-fails",
                    "TryFormat は Oracle の上限を超える値を書き出さない",
                    Ct(CanonicalTypeKind.String, length: 0)
                )
                .Expect(DialectNames.Oracle, "fail"),
            Format(
                    "format-oracle-limit-ansistring-4001-fails",
                    "TryFormat は Oracle の上限を超える値を書き出さない",
                    Ct(CanonicalTypeKind.AnsiString, length: 4001)
                )
                .Boundary(DialectTypeBoundaries.OverLimit)
                .Expect(DialectNames.Oracle, "fail"),
            Format(
                    "format-oracle-limit-ansistring-4000-ok",
                    "TryFormat は Oracle の上限を超える値を書き出さない",
                    Ct(CanonicalTypeKind.AnsiString, length: 4000)
                )
                .Boundary(DialectTypeBoundaries.AtLimit)
                .Expect(DialectNames.Oracle, "ok"),
            Format(
                    "format-oracle-limit-fixedstring-1001-fails",
                    "TryFormat は Oracle の上限を超える値を書き出さない",
                    Ct(CanonicalTypeKind.FixedString, length: 1001)
                )
                .Boundary(DialectTypeBoundaries.OverLimit)
                .Expect(DialectNames.Oracle, "fail"),
            Format(
                    "format-oracle-limit-fixedstring-1000-ok",
                    "TryFormat は Oracle の上限を超える値を書き出さない",
                    Ct(CanonicalTypeKind.FixedString, length: 1000)
                )
                .Boundary(DialectTypeBoundaries.AtLimit)
                .Expect(DialectNames.Oracle, "ok"),
            Format(
                    "format-oracle-limit-ansifixedstring-2001-fails",
                    "TryFormat は Oracle の上限を超える値を書き出さない",
                    Ct(CanonicalTypeKind.AnsiFixedString, length: 2001)
                )
                .Boundary(DialectTypeBoundaries.OverLimit)
                .Expect(DialectNames.Oracle, "fail"),
            Format(
                    "format-oracle-limit-fixedstring-max-fails",
                    "TryFormat は Oracle の上限を超える値を書き出さない",
                    Ct(CanonicalTypeKind.FixedString, length: -1)
                )
                .Expect(DialectNames.Oracle, "fail"),
            Format(
                    "format-oracle-limit-ansifixedstring-max-fails",
                    "TryFormat は Oracle の上限を超える値を書き出さない",
                    Ct(CanonicalTypeKind.AnsiFixedString, length: -1)
                )
                .Expect(DialectNames.Oracle, "fail"),
            Format(
                    "format-oracle-limit-binary-2001-fails",
                    "TryFormat は Oracle の上限を超える値を書き出さない",
                    Ct(CanonicalTypeKind.Binary, length: 2001)
                )
                .Boundary(DialectTypeBoundaries.OverLimit)
                .Expect(DialectNames.Oracle, "fail"),
            Format(
                    "format-oracle-limit-fixedbinary-max-ok",
                    "TryFormat は Oracle の上限を超える値を書き出さない",
                    Ct(CanonicalTypeKind.FixedBinary, length: -1)
                )
                .Expect(DialectNames.Oracle, "ok"),
            Format(
                    "format-oracle-limit-binary-2000-ok",
                    "TryFormat は Oracle の上限を超える値を書き出さない",
                    Ct(CanonicalTypeKind.Binary, length: 2000)
                )
                .Boundary(DialectTypeBoundaries.AtLimit)
                .Expect(DialectNames.Oracle, "ok"),
            Format(
                    "format-oracle-limit-fixedbinary-2001-fails",
                    "TryFormat は Oracle の上限を超える値を書き出さない",
                    Ct(CanonicalTypeKind.FixedBinary, length: 2001)
                )
                .Boundary(DialectTypeBoundaries.OverLimit)
                .Expect(DialectNames.Oracle, "fail"),
            Format(
                    "format-oracle-limit-decimal-p39-s2-fails",
                    "TryFormat は Oracle の上限を超える値を書き出さない",
                    Ct(CanonicalTypeKind.Decimal, precision: 39, scale: 2)
                )
                .Boundary(DialectTypeBoundaries.OverLimit)
                .Expect(DialectNames.Oracle, "fail"),
            Format(
                    "format-oracle-limit-decimal-p38-s2-ok",
                    "TryFormat は Oracle の上限を超える値を書き出さない",
                    Ct(CanonicalTypeKind.Decimal, precision: 38, scale: 2)
                )
                .Boundary(DialectTypeBoundaries.AtLimit)
                .Expect(DialectNames.Oracle, "ok"),
            Format(
                    "format-oracle-limit-decimal-p10-s128-fails",
                    "TryFormat は Oracle の上限を超える値を書き出さない",
                    Ct(CanonicalTypeKind.Decimal, precision: 10, scale: 128)
                )
                .Boundary(DialectTypeBoundaries.OverLimit)
                .Expect(DialectNames.Oracle, "fail"),
            Format(
                    "format-oracle-limit-decimal-p10-sm85-fails",
                    "TryFormat は Oracle の上限を超える値を書き出さない",
                    Ct(CanonicalTypeKind.Decimal, precision: 10, scale: -85)
                )
                .Boundary(DialectTypeBoundaries.OverLimit)
                .Expect(DialectNames.Oracle, "fail"),
            Format(
                    "format-oracle-limit-decimal-p10-sm84-ok",
                    "TryFormat は Oracle の上限を超える値を書き出さない",
                    Ct(CanonicalTypeKind.Decimal, precision: 10, scale: -84)
                )
                .Boundary(DialectTypeBoundaries.AtLimit)
                .Expect(DialectNames.Oracle, "ok"),
            Format(
                    "format-oracle-limit-datetime-p10-fails",
                    "TryFormat は Oracle の上限を超える値を書き出さない",
                    Ct(CanonicalTypeKind.DateTime, precision: 10)
                )
                .Boundary(DialectTypeBoundaries.OverLimit)
                .Expect(DialectNames.Oracle, "fail"),
            Format(
                    "format-oracle-limit-datetime-p9-ok",
                    "TryFormat は Oracle の上限を超える値を書き出さない",
                    Ct(CanonicalTypeKind.DateTime, precision: 9)
                )
                .Boundary(DialectTypeBoundaries.AtLimit)
                .Expect(DialectNames.Oracle, "ok"),
            Format(
                    "format-oracle-limit-datetimeoffset-p10-fails",
                    "TryFormat は Oracle の上限を超える値を書き出さない",
                    Ct(CanonicalTypeKind.DateTimeOffset, precision: 10)
                )
                .Boundary(DialectTypeBoundaries.OverLimit)
                .Expect(DialectNames.Oracle, "fail"),
            Format(
                    "format-oracle-limit-ansifixedstring-2000-ok",
                    "境界の対：TryFormat は Oracle の上限ちょうどの値を書き出す",
                    Ct(CanonicalTypeKind.AnsiFixedString, length: 2000)
                )
                .Boundary(DialectTypeBoundaries.AtLimit)
                .Expect(DialectNames.Oracle, "ok CHAR(2000)"),
            Format(
                    "format-oracle-limit-fixedbinary-2000-ok",
                    "境界の対：TryFormat は Oracle の上限ちょうどの値を書き出す",
                    Ct(CanonicalTypeKind.FixedBinary, length: 2000)
                )
                .Boundary(DialectTypeBoundaries.AtLimit)
                .Expect(DialectNames.Oracle, "ok RAW(2000)"),
            Format(
                    "format-oracle-limit-decimal-scale127-ok",
                    "境界の対：TryFormat は Oracle の上限ちょうどの値を書き出す",
                    Ct(CanonicalTypeKind.Decimal, precision: 10, scale: 127)
                )
                .Boundary(DialectTypeBoundaries.AtLimit)
                .Expect(DialectNames.Oracle, "ok NUMBER(10,127)"),
            Format(
                    "format-oracle-limit-datetimeoffset-9-ok",
                    "境界の対：TryFormat は Oracle の上限ちょうどの値を書き出す",
                    Ct(CanonicalTypeKind.DateTimeOffset, precision: 9)
                )
                .Boundary(DialectTypeBoundaries.AtLimit)
                .Expect(DialectNames.Oracle, "ok TIMESTAMP(9) WITH TIME ZONE"),
            RoundTripText(
                    "roundtrip-oracle-number-1-same",
                    "パース → フォーマットのラウンドトリップで型が保持される",
                    "NUMBER(1)"
                )
                .Expect(DialectNames.Oracle, "same"),
            RoundTripText(
                    "roundtrip-oracle-number-3-same",
                    "パース → フォーマットのラウンドトリップで型が保持される",
                    "NUMBER(3)"
                )
                .Expect(DialectNames.Oracle, "same"),
            RoundTripText(
                    "roundtrip-oracle-number-5-same",
                    "パース → フォーマットのラウンドトリップで型が保持される",
                    "NUMBER(5)"
                )
                .Expect(DialectNames.Oracle, "same"),
            RoundTripText(
                    "roundtrip-oracle-number-10-same",
                    "パース → フォーマットのラウンドトリップで型が保持される",
                    "NUMBER(10)"
                )
                .Expect(DialectNames.Oracle, "same"),
            RoundTripText(
                    "roundtrip-oracle-number-19-same",
                    "パース → フォーマットのラウンドトリップで型が保持される",
                    "NUMBER(19)"
                )
                .Expect(DialectNames.Oracle, "same"),
            RoundTripText(
                    "roundtrip-oracle-number-10-2-same",
                    "パース → フォーマットのラウンドトリップで型が保持される",
                    "NUMBER(10,2)"
                )
                .Expect(DialectNames.Oracle, "same"),
            RoundTripText(
                    "roundtrip-oracle-binary-float-same",
                    "パース → フォーマットのラウンドトリップで型が保持される",
                    "BINARY_FLOAT"
                )
                .Expect(DialectNames.Oracle, "same"),
            RoundTripText(
                    "roundtrip-oracle-binary-double-same",
                    "パース → フォーマットのラウンドトリップで型が保持される",
                    "BINARY_DOUBLE"
                )
                .Expect(DialectNames.Oracle, "same"),
            RoundTripText(
                    "roundtrip-oracle-nvarchar2-50-same",
                    "パース → フォーマットのラウンドトリップで型が保持される",
                    "NVARCHAR2(50)"
                )
                .Expect(DialectNames.Oracle, "same"),
            RoundTripText(
                    "roundtrip-oracle-varchar2-50-same",
                    "パース → フォーマットのラウンドトリップで型が保持される",
                    "VARCHAR2(50)"
                )
                .Expect(DialectNames.Oracle, "same"),
            RoundTripText(
                    "roundtrip-oracle-nchar-10-same",
                    "パース → フォーマットのラウンドトリップで型が保持される",
                    "NCHAR(10)"
                )
                .Expect(DialectNames.Oracle, "same"),
            RoundTripText(
                    "roundtrip-oracle-char-10-same",
                    "パース → フォーマットのラウンドトリップで型が保持される",
                    "CHAR(10)"
                )
                .Expect(DialectNames.Oracle, "same"),
            RoundTripText(
                    "roundtrip-oracle-nclob-same",
                    "パース → フォーマットのラウンドトリップで型が保持される",
                    "NCLOB"
                )
                .Expect(DialectNames.Oracle, "same"),
            RoundTripText(
                    "roundtrip-oracle-clob-same",
                    "パース → フォーマットのラウンドトリップで型が保持される",
                    "CLOB"
                )
                .Expect(DialectNames.Oracle, "same"),
            RoundTripText(
                    "roundtrip-oracle-raw-16-same",
                    "パース → フォーマットのラウンドトリップで型が保持される",
                    "RAW(16)"
                )
                .Expect(DialectNames.Oracle, "same"),
            RoundTripText(
                    "roundtrip-oracle-blob-same",
                    "パース → フォーマットのラウンドトリップで型が保持される",
                    "BLOB"
                )
                .Expect(DialectNames.Oracle, "same"),
            RoundTripText(
                    "roundtrip-oracle-timestamp-6-same",
                    "パース → フォーマットのラウンドトリップで型が保持される",
                    "TIMESTAMP(6)"
                )
                .Expect(DialectNames.Oracle, "same"),
            RoundTripText(
                    "roundtrip-oracle-timestamp-6-with-time-zone-same",
                    "パース → フォーマットのラウンドトリップで型が保持される",
                    "TIMESTAMP(6) WITH TIME ZONE"
                )
                .Expect(DialectNames.Oracle, "same"),
            RoundTripText(
                    "roundtrip-oracle-xmltype-same",
                    "パース → フォーマットのラウンドトリップで型が保持される",
                    "XMLTYPE"
                )
                .Expect(DialectNames.Oracle, "same"),
            RoundTripText(
                    "roundtrip-oracle-date-asymmetric",
                    "DATE のラウンドトリップは非対称（DATE→DateTime→TIMESTAMP）",
                    "DATE"
                )
                .Expect(DialectNames.Oracle, "ok TIMESTAMP"),
            Catalog("catalog-oracle-default-data-type", "DefaultDataType は NUMBER(10)")
                .Expect(DialectNames.Oracle, "ok default=NUMBER(10)"),
            Normalize(
                    "normalize-oracle-case-timestamp",
                    "Normalize: 取込が返す表記へ寄せる",
                    "TIMESTAMP"
                )
                .Expect(DialectNames.Oracle, "ok TIMESTAMP(6)"),
            Normalize(
                    "normalize-oracle-case-timestamp-lowercase",
                    "Normalize: 取込が返す表記へ寄せる",
                    "timestamp"
                )
                .Expect(DialectNames.Oracle, "ok TIMESTAMP(6)"),
            Normalize(
                    "normalize-oracle-case-timestamp-with-time-zone",
                    "Normalize: 取込が返す表記へ寄せる",
                    "TIMESTAMP WITH TIME ZONE"
                )
                .Expect(DialectNames.Oracle, "ok TIMESTAMP(6) WITH TIME ZONE"),
            Normalize(
                    "normalize-oracle-case-timestamp-with-local-time-zone",
                    "Normalize: 取込が返す表記へ寄せる",
                    "TIMESTAMP  WITH  LOCAL  TIME  ZONE"
                )
                .Expect(DialectNames.Oracle, "ok TIMESTAMP(6) WITH LOCAL TIME ZONE"),
            Normalize(
                    "normalize-oracle-case-timestamp-3",
                    "Normalize: 取込が返す表記へ寄せる",
                    "TIMESTAMP(3)"
                )
                .Expect(DialectNames.Oracle, "ok TIMESTAMP(3)"),
            Normalize(
                    "normalize-oracle-case-timestamp-9-with-time-zone",
                    "Normalize: 取込が返す表記へ寄せる",
                    "TIMESTAMP (9) WITH TIME ZONE"
                )
                .Expect(DialectNames.Oracle, "ok TIMESTAMP(9) WITH TIME ZONE"),
            Normalize(
                    "normalize-oracle-case-number-10-0",
                    "Normalize: 取込が返す表記へ寄せる",
                    "NUMBER(10,0)"
                )
                .Expect(DialectNames.Oracle, "ok NUMBER(10)"),
            Normalize(
                    "normalize-oracle-case-number-10-2",
                    "Normalize: 取込が返す表記へ寄せる",
                    "number(10, 2)"
                )
                .Expect(DialectNames.Oracle, "ok NUMBER(10,2)"),
            Normalize(
                    "normalize-oracle-case-number-10-2-lowercase",
                    "Normalize: 取込が返す表記へ寄せる",
                    "NUMBER(10,-2)"
                )
                .Expect(DialectNames.Oracle, "ok NUMBER(10,-2)"),
            Normalize(
                    "normalize-oracle-case-number",
                    "Normalize: 取込が返す表記へ寄せる",
                    "NUMBER(*)"
                )
                .Expect(DialectNames.Oracle, "ok NUMBER"),
            Normalize(
                    "normalize-oracle-case-number-0",
                    "Normalize: 取込が返す表記へ寄せる",
                    "NUMBER(*,0)"
                )
                .Expect(DialectNames.Oracle, "ok NUMBER(*,0)"),
            Normalize(
                    "normalize-oracle-case-number-2",
                    "Normalize: 取込が返す表記へ寄せる",
                    "NUMBER(*,2)"
                )
                .Expect(DialectNames.Oracle, "ok NUMBER(*,2)"),
            Normalize(
                    "normalize-oracle-case-number-lowercase",
                    "Normalize: 取込が返す表記へ寄せる",
                    "NUMBER"
                )
                .Expect(DialectNames.Oracle, "ok NUMBER"),
            Normalize(
                    "normalize-oracle-case-integer",
                    "Normalize: 取込が返す表記へ寄せる",
                    "INTEGER"
                )
                .Expect(DialectNames.Oracle, "ok NUMBER(*,0)"),
            Normalize("normalize-oracle-case-int", "Normalize: 取込が返す表記へ寄せる", "int")
                .Expect(DialectNames.Oracle, "ok NUMBER(*,0)"),
            Normalize(
                    "normalize-oracle-case-smallint",
                    "Normalize: 取込が返す表記へ寄せる",
                    "SMALLINT"
                )
                .Expect(DialectNames.Oracle, "ok NUMBER(*,0)"),
            Normalize("normalize-oracle-case-float", "Normalize: 取込が返す表記へ寄せる", "FLOAT")
                .Expect(DialectNames.Oracle, "ok FLOAT(126)"),
            Normalize(
                    "normalize-oracle-case-float-63",
                    "Normalize: 取込が返す表記へ寄せる",
                    "FLOAT(63)"
                )
                .Expect(DialectNames.Oracle, "ok FLOAT(63)"),
            Normalize(
                    "normalize-oracle-case-varchar2-50",
                    "Normalize: 取込が返す表記へ寄せる",
                    " VARCHAR2(50) "
                )
                .Expect(DialectNames.Oracle, "ok VARCHAR2(50)"),
            Normalize("normalize-oracle-case-date", "Normalize: 取込が返す表記へ寄せる", "DATE")
                .Expect(DialectNames.Oracle, "ok DATE"),
            Normalize(
                    "normalize-oracle-idempotent-timestamp-with-time-zone",
                    "Normalize: 冪等",
                    "TIMESTAMP WITH TIME ZONE"
                )
                .Expect(DialectNames.Oracle, "ok"),
            Normalize("normalize-oracle-idempotent-number-10-0", "Normalize: 冪等", "NUMBER(10,0)")
                .Expect(DialectNames.Oracle, "ok"),
            Normalize("normalize-oracle-idempotent-integer", "Normalize: 冪等", "INTEGER")
                .Expect(DialectNames.Oracle, "ok"),
            Normalize("normalize-oracle-idempotent-number", "Normalize: 冪等", "NUMBER(*)")
                .Expect(DialectNames.Oracle, "ok"),
            Normalize("normalize-oracle-idempotent-float", "Normalize: 冪等", "FLOAT")
                .Expect(DialectNames.Oracle, "ok"),
            Normalize("normalize-oracle-idempotent-xmltype", "Normalize: 冪等", "XMLTYPE")
                .Expect(DialectNames.Oracle, "ok"),
            ImportSpelling(
                    "import-spelling-oracle-fixed-point-number-p10-s2",
                    "Normalize: 取込の出力は不動点",
                    new ImportedType("NUMBER", Length: 0, Precision: 10, Scale: 2)
                )
                .Expect(DialectNames.Oracle, "ok fixed=1"),
            ImportSpelling(
                    "import-spelling-oracle-fixed-point-number-p10-s0",
                    "Normalize: 取込の出力は不動点",
                    new ImportedType("NUMBER", Length: 0, Precision: 10, Scale: 0)
                )
                .Expect(DialectNames.Oracle, "ok fixed=1"),
            ImportSpelling(
                    "import-spelling-oracle-fixed-point-number-pnull-s0",
                    "Normalize: 取込の出力は不動点",
                    new ImportedType("NUMBER", Length: 0, Precision: null, Scale: 0)
                )
                .Expect(DialectNames.Oracle, "ok fixed=1"),
            ImportSpelling(
                    "import-spelling-oracle-fixed-point-number-pnull-s2",
                    "Normalize: 取込の出力は不動点",
                    new ImportedType("NUMBER", Length: 0, Precision: null, Scale: 2)
                )
                .Expect(DialectNames.Oracle, "ok fixed=1"),
            ImportSpelling(
                    "import-spelling-oracle-fixed-point-number-pnull-snull",
                    "Normalize: 取込の出力は不動点",
                    new ImportedType("NUMBER", Length: 0, Precision: null, Scale: null)
                )
                .Expect(DialectNames.Oracle, "ok fixed=1"),
            ImportSpelling(
                    "import-spelling-oracle-fixed-point-float-p126-snull",
                    "Normalize: 取込の出力は不動点",
                    new ImportedType("FLOAT", Length: 0, Precision: 126, Scale: null)
                )
                .Expect(DialectNames.Oracle, "ok fixed=1"),
            ImportSpelling(
                    "import-spelling-oracle-fixed-point-timestamp-6-with-time-zone-pnull-s6",
                    "Normalize: 取込の出力は不動点",
                    new ImportedType(
                        "TIMESTAMP(6) WITH TIME ZONE",
                        Length: 0,
                        Precision: null,
                        Scale: 6
                    )
                )
                .Expect(DialectNames.Oracle, "ok fixed=1"),
            ImportSpelling(
                    "import-spelling-oracle-fixed-point-timestamp-3-with-local-time-zone-pnull-s3",
                    "Normalize: 取込の出力は不動点",
                    new ImportedType(
                        "TIMESTAMP(3) WITH LOCAL TIME ZONE",
                        Length: 0,
                        Precision: null,
                        Scale: 3
                    )
                )
                .Expect(DialectNames.Oracle, "ok fixed=1"),
            SameType(
                    "same-type-oracle-timestamp-default-precision",
                    "Oracle の capabilities は正規化と参照アクションの畳み込みを配線している",
                    "TIMESTAMP",
                    "TIMESTAMP(6)"
                )
                .Expect(DialectNames.Oracle, "same"),
            Map("map-oracle-blob-byte-array", "バイナリ系は byte[]（参照型）へマップされる", "blob")
                .Expect(DialectNames.Oracle, "ok type=byte[] ref=1"),
            Map(
                    "map-oracle-long-raw-byte-array",
                    "バイナリ系は byte[]（参照型）へマップされる",
                    "long raw"
                )
                .Expect(DialectNames.Oracle, "ok type=byte[] ref=1"),
            Map(
                    "map-oracle-raw-16-byte-array",
                    "バイナリ系は byte[]（参照型）へマップされる",
                    "raw(16)"
                )
                .Expect(DialectNames.Oracle, "ok type=byte[] ref=1"),
            Map(
                    "map-oracle-blob-unbounded",
                    "BLOB・LONG RAW は無制限バイナリ、RAW(n) は有界",
                    "blob"
                )
                .Expect(DialectNames.Oracle, "ok unbounded=1"),
            Map(
                    "map-oracle-long-raw-unbounded",
                    "BLOB・LONG RAW は無制限バイナリ、RAW(n) は有界",
                    "long raw"
                )
                .Expect(DialectNames.Oracle, "ok unbounded=1"),
            Map(
                    "map-oracle-raw-16-unbounded",
                    "BLOB・LONG RAW は無制限バイナリ、RAW(n) は有界",
                    "raw(16)"
                )
                .Expect(DialectNames.Oracle, "ok unbounded=0"),
            Map(
                    "map-oracle-varchar2-100-not-unbounded",
                    "非バイナリ型は IsUnboundedBinary=false になる",
                    "varchar2(100)"
                )
                .Expect(DialectNames.Oracle, "ok unbounded=0"),
            Map(
                    "map-oracle-number-10-not-unbounded",
                    "非バイナリ型は IsUnboundedBinary=false になる",
                    "number(10)"
                )
                .Expect(DialectNames.Oracle, "ok unbounded=0"),
            Map(
                    "map-oracle-sdo-geometry-fallback",
                    "未知の型は IsFallbackType が true になる",
                    "SDO_GEOMETRY"
                )
                .Expect(DialectNames.Oracle, "ok fallback=1"),
            Map("map-oracle-bfile-fallback", "未知の型は IsFallbackType が true になる", "BFILE")
                .Expect(DialectNames.Oracle, "ok fallback=1"),
            Map(
                    "map-oracle-ansi-integer-decimal",
                    "INTEGER・SMALLINT は NUMBER(*,0) と同じ decimal へマップされる",
                    "INTEGER"
                )
                .Expect(DialectNames.Oracle, "ok type=decimal fallback=0 p=- s=-"),
            Map(
                    "map-oracle-ansi-smallint-decimal",
                    "INTEGER・SMALLINT は NUMBER(*,0) と同じ decimal へマップされる",
                    "SMALLINT"
                )
                .Expect(DialectNames.Oracle, "ok type=decimal fallback=0 p=- s=-"),
            Map(
                    "map-oracle-ansi-integer-decimal-lowercase",
                    "INTEGER・SMALLINT は NUMBER(*,0) と同じ decimal へマップされる",
                    "integer"
                )
                .Expect(DialectNames.Oracle, "ok type=decimal fallback=0 p=- s=-"),
            Map(
                    "map-oracle-number-10-not-fallback",
                    "解決できる型は IsFallbackType が false になる",
                    "number(10)"
                )
                .Expect(DialectNames.Oracle, "ok fallback=0"),
            Map(
                    "map-oracle-varchar2-50-not-fallback",
                    "解決できる型は IsFallbackType が false になる",
                    "varchar2(50)"
                )
                .Expect(DialectNames.Oracle, "ok fallback=0"),
            Map(
                    "map-oracle-length-unit-varchar2-50-char-maxlen",
                    "単位語つきの長さからも最大長を読む",
                    "VARCHAR2(50 CHAR)"
                )
                .Expect(DialectNames.Oracle, "ok maxlen=50"),
            Map(
                    "map-oracle-length-unit-varchar2-50-byte-maxlen",
                    "単位語つきの長さからも最大長を読む",
                    "VARCHAR2(50 BYTE)"
                )
                .Expect(DialectNames.Oracle, "ok maxlen=50"),
            Map(
                    "map-oracle-length-unit-char-10-byte-maxlen",
                    "単位語つきの長さからも最大長を読む",
                    "CHAR(10 BYTE)"
                )
                .Expect(DialectNames.Oracle, "ok maxlen=10"),
            Map(
                    "map-oracle-length-unit-char-10-char-maxlen",
                    "単位語つきの長さからも最大長を読む",
                    "CHAR(10 CHAR)"
                )
                .Expect(DialectNames.Oracle, "ok maxlen=10"),
            Map(
                    "map-oracle-length-unit-varchar2-50-maxlen",
                    "単位語つきの長さからも最大長を読む",
                    "VARCHAR2(50)"
                )
                .Expect(DialectNames.Oracle, "ok maxlen=50"),
            Map(
                    "map-oracle-timestamp-timestamp-6-with-local-time-zone-driver-type",
                    "WITH LOCAL TIME ZONE は DateTime、WITH TIME ZONE は DateTimeOffset",
                    "TIMESTAMP(6) WITH LOCAL TIME ZONE"
                )
                .Expect(DialectNames.Oracle, "ok type=DateTime"),
            Map(
                    "map-oracle-timestamp-timestamp-with-local-time-zone-driver-type",
                    "WITH LOCAL TIME ZONE は DateTime、WITH TIME ZONE は DateTimeOffset",
                    "TIMESTAMP WITH LOCAL TIME ZONE"
                )
                .Expect(DialectNames.Oracle, "ok type=DateTime"),
            Map(
                    "map-oracle-timestamp-timestamp-6-with-time-zone-driver-type",
                    "WITH LOCAL TIME ZONE は DateTime、WITH TIME ZONE は DateTimeOffset",
                    "TIMESTAMP(6) WITH TIME ZONE"
                )
                .Expect(DialectNames.Oracle, "ok type=DateTimeOffset"),
            Map(
                    "map-oracle-timestamp-timestamp-6-driver-type",
                    "WITH LOCAL TIME ZONE は DateTime、WITH TIME ZONE は DateTimeOffset",
                    "TIMESTAMP(6)"
                )
                .Expect(DialectNames.Oracle, "ok type=DateTime"),
            Map(
                    "map-oracle-provider-number-10-type",
                    "TypeMapper は NUMBER(10) を int、VARCHAR2 を string に解決する",
                    "NUMBER(10)"
                )
                .Expect(DialectNames.Oracle, "ok type=int"),
            Map(
                    "map-oracle-provider-number-1-type",
                    "TypeMapper は NUMBER(10) を int、VARCHAR2 を string に解決する",
                    "NUMBER(1)"
                )
                .Expect(DialectNames.Oracle, "ok type=bool"),
            Map(
                    "map-oracle-provider-number-19-type",
                    "TypeMapper は NUMBER(10) を int、VARCHAR2 を string に解決する",
                    "NUMBER(19)"
                )
                .Expect(DialectNames.Oracle, "ok type=long"),
            Map(
                    "map-oracle-provider-number-10-2-type",
                    "TypeMapper は NUMBER(10) を int、VARCHAR2 を string に解決する",
                    "NUMBER(10,2)"
                )
                .Expect(DialectNames.Oracle, "ok type=decimal"),
            Map(
                    "map-oracle-provider-varchar2-100-type",
                    "TypeMapper は NUMBER(10) を int、VARCHAR2 を string に解決する",
                    "VARCHAR2(100)"
                )
                .Expect(DialectNames.Oracle, "ok type=string"),
            Map(
                    "map-oracle-provider-raw-16-type",
                    "TypeMapper は NUMBER(10) を int、VARCHAR2 を string に解決する",
                    "RAW(16)"
                )
                .Expect(DialectNames.Oracle, "ok type=byte[]"),
            Map(
                    "map-oracle-provider-timestamp-with-time-zone-type",
                    "TypeMapper は NUMBER(10) を int、VARCHAR2 を string に解決する",
                    "TIMESTAMP WITH TIME ZONE"
                )
                .Expect(DialectNames.Oracle, "ok type=DateTimeOffset"),
            Map(
                    "map-oracle-provider-date-type",
                    "TypeMapper は NUMBER(10) を int、VARCHAR2 を string に解決する",
                    "DATE"
                )
                .Expect(DialectNames.Oracle, "ok type=DateTime"),
            Map(
                    "map-oracle-provider-varchar2-100-maxlen",
                    "TypeMapper は NUMBER(10) を int、VARCHAR2 を string に解決する",
                    "VARCHAR2(100)"
                )
                .Expect(DialectNames.Oracle, "ok maxlen=100"),
            Convert(
                    "convert-oracle-from-sqlserver-int",
                    "sqlserver → oracle の代表変換",
                    new ConvertInput(DialectNames.SqlServer, "int")
                )
                .Expect(DialectNames.Oracle, "converted type=NUMBER(10)"),
            Convert(
                    "convert-oracle-from-sqlserver-bigint",
                    "sqlserver → oracle の代表変換",
                    new ConvertInput(DialectNames.SqlServer, "bigint")
                )
                .Expect(DialectNames.Oracle, "converted type=NUMBER(19)"),
            Convert(
                    "convert-oracle-from-sqlserver-smallint",
                    "sqlserver → oracle の代表変換",
                    new ConvertInput(DialectNames.SqlServer, "smallint")
                )
                .Expect(DialectNames.Oracle, "converted type=NUMBER(5)"),
            Convert(
                    "convert-oracle-from-sqlserver-tinyint",
                    "sqlserver → oracle の代表変換",
                    new ConvertInput(DialectNames.SqlServer, "tinyint")
                )
                .Expect(DialectNames.Oracle, "converted type=NUMBER(3)"),
            Convert(
                    "convert-oracle-from-sqlserver-bit",
                    "sqlserver → oracle の代表変換",
                    new ConvertInput(DialectNames.SqlServer, "bit")
                )
                .Expect(DialectNames.Oracle, "converted type=NUMBER(1)"),
            Convert(
                    "convert-oracle-from-sqlserver-nvarchar-50",
                    "sqlserver → oracle の代表変換",
                    new ConvertInput(DialectNames.SqlServer, "nvarchar(50)")
                )
                .Expect(DialectNames.Oracle, "converted type=NVARCHAR2(50)"),
            Convert(
                    "convert-oracle-from-sqlserver-varchar-50",
                    "sqlserver → oracle の代表変換",
                    new ConvertInput(DialectNames.SqlServer, "varchar(50)")
                )
                .Expect(DialectNames.Oracle, "converted type=VARCHAR2(50)"),
            Convert(
                    "convert-oracle-from-sqlserver-varchar-max",
                    "sqlserver → oracle の代表変換",
                    new ConvertInput(DialectNames.SqlServer, "varchar(max)")
                )
                .Expect(DialectNames.Oracle, "converted type=CLOB"),
            Convert(
                    "convert-oracle-from-sqlserver-nvarchar-max",
                    "sqlserver → oracle の代表変換",
                    new ConvertInput(DialectNames.SqlServer, "nvarchar(max)")
                )
                .Expect(DialectNames.Oracle, "converted type=NCLOB"),
            Convert(
                    "convert-oracle-from-sqlserver-uniqueidentifier",
                    "sqlserver → oracle の代表変換",
                    new ConvertInput(DialectNames.SqlServer, "uniqueidentifier")
                )
                .Expect(DialectNames.Oracle, "converted type=RAW(16)"),
            Convert(
                    "convert-oracle-from-sqlserver-datetime2",
                    "sqlserver → oracle の代表変換",
                    new ConvertInput(DialectNames.SqlServer, "datetime2")
                )
                .Expect(DialectNames.Oracle, "converted type=TIMESTAMP"),
            Convert(
                    "convert-oracle-from-sqlserver-varbinary-max",
                    "sqlserver → oracle の代表変換",
                    new ConvertInput(DialectNames.SqlServer, "varbinary(max)")
                )
                .Expect(DialectNames.Oracle, "converted type=BLOB"),
            Convert(
                    "convert-oracle-from-sqlserver-time7-unconverted",
                    "sqlserver time(7) は oracle へ変換できず Unconverted",
                    new ConvertInput(DialectNames.SqlServer, "time(7)")
                )
                .Expect(DialectNames.Oracle, "unconverted"),
            Convert(
                    "convert-oracle-from-sqlserver-hierarchyid-unconverted",
                    "sqlserver hierarchyid は oracle へ変換できず Unconverted",
                    new ConvertInput(DialectNames.SqlServer, "hierarchyid")
                )
                .Expect(DialectNames.Oracle, "unconverted"),
            Convert(
                    "convert-oracle-to-sqlserver-number-10",
                    "oracle → sqlserver の代表変換",
                    new ConvertInput(DialectNames.Oracle, "NUMBER(10)")
                )
                .Expect(DialectNames.SqlServer, "converted type=int"),
            Convert(
                    "convert-oracle-to-sqlserver-number-19",
                    "oracle → sqlserver の代表変換",
                    new ConvertInput(DialectNames.Oracle, "NUMBER(19)")
                )
                .Expect(DialectNames.SqlServer, "converted type=bigint"),
            Convert(
                    "convert-oracle-to-sqlserver-number-5",
                    "oracle → sqlserver の代表変換",
                    new ConvertInput(DialectNames.Oracle, "NUMBER(5)")
                )
                .Expect(DialectNames.SqlServer, "converted type=smallint"),
            Convert(
                    "convert-oracle-to-sqlserver-number-1",
                    "oracle → sqlserver の代表変換",
                    new ConvertInput(DialectNames.Oracle, "NUMBER(1)")
                )
                .Expect(DialectNames.SqlServer, "converted type=bit"),
            Convert(
                    "convert-oracle-to-sqlserver-nvarchar2-50",
                    "oracle → sqlserver の代表変換",
                    new ConvertInput(DialectNames.Oracle, "NVARCHAR2(50)")
                )
                .Expect(DialectNames.SqlServer, "converted type=nvarchar(50)"),
            Convert(
                    "convert-oracle-to-sqlserver-varchar2-50",
                    "oracle → sqlserver の代表変換",
                    new ConvertInput(DialectNames.Oracle, "VARCHAR2(50)")
                )
                .Expect(DialectNames.SqlServer, "converted type=varchar(50)"),
            Convert(
                    "convert-oracle-to-sqlserver-nclob",
                    "oracle → sqlserver の代表変換",
                    new ConvertInput(DialectNames.Oracle, "NCLOB")
                )
                .Expect(DialectNames.SqlServer, "converted type=nvarchar(max)"),
            Convert(
                    "convert-oracle-to-sqlserver-clob",
                    "oracle → sqlserver の代表変換",
                    new ConvertInput(DialectNames.Oracle, "CLOB")
                )
                .Expect(DialectNames.SqlServer, "converted type=varchar(max)"),
            Convert(
                    "convert-oracle-to-sqlserver-blob",
                    "oracle → sqlserver の代表変換",
                    new ConvertInput(DialectNames.Oracle, "BLOB")
                )
                .Expect(DialectNames.SqlServer, "converted type=varbinary(max)"),
            Convert(
                    "convert-oracle-to-sqlserver-raw-16",
                    "oracle → sqlserver の代表変換",
                    new ConvertInput(DialectNames.Oracle, "RAW(16)")
                )
                .Expect(DialectNames.SqlServer, "converted type=varbinary(16)"),
            Convert(
                    "convert-oracle-to-sqlserver-binary-float",
                    "oracle → sqlserver の代表変換",
                    new ConvertInput(DialectNames.Oracle, "BINARY_FLOAT")
                )
                .Expect(DialectNames.SqlServer, "converted type=real"),
            Convert(
                    "convert-oracle-to-sqlserver-binary-double",
                    "oracle → sqlserver の代表変換",
                    new ConvertInput(DialectNames.Oracle, "BINARY_DOUBLE")
                )
                .Expect(DialectNames.SqlServer, "converted type=float"),
            Convert(
                    "convert-oracle-to-sqlserver-rowid-unconverted",
                    "oracle ROWID は sqlserver へ変換できず Unconverted",
                    new ConvertInput(DialectNames.Oracle, "ROWID")
                )
                .Expect(DialectNames.SqlServer, "unconverted"),
        ];
}
