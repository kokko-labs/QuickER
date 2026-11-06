using AwesomeAssertions;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.SqlServer;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Provider;

/// <summary>
/// テーブル名が長く、QuickER が付ける既定の制約名（<c>PK_{表}</c>・<c>FK_{子}_{親}</c>・<c>UQ_{表}_{列}</c>）が方言の上限を超える図で、
/// 既定名の関数（<see cref="ConstraintNames"/>・<see cref="UniqueConstraint.SynthesizeName"/>）が上限を超えるときだけ縮め、すべて上限以内で
/// 互いに違う名前を返すことを 4 方言で検証するテストクラス
/// </summary>
/// <remarks>
/// <para>
/// 既定名は <see cref="IdentifierLengthLimit.Fit"/> で上限に収める。DDL・同期スクリプト・差分のどれか 1 つだけが
/// 縮めずに名前を作ると、同期が DDL で作った制約を別物とみなして追加と削除を出し続ける（外部キー）か、
/// DDL の適用そのものが上限超過で失敗する。上限の経路（方言の上限を渡す）の正本は <see cref="ConstraintNames"/> の XmlDoc。
/// </para>
/// <para>
/// DDL・同期スクリプトが同じ縮めた名前を使うことは方言の DDL の表の行 <c>ddl-long-default-names-are-shortened</c>・
/// <c>sync-add-table-long-default-names-are-shortened</c>、差分が縮めた名前の live を同じ外部キーとみなすことと PostgreSQL の旧版の
/// 切り詰めた名前の吸収（と吸収しない対照・利用者の名前は吸収しないこと）は同期計画の期待の行 <c>diff-long-default-*</c>・
/// <c>diff-pg-user-named-fk-equal-to-shortened-default-is-not-absorbed</c>（Integration・DB なし）が固定する。実 DB では
/// 期待の行 <c>realdb-long-default-*</c> と、吸収の対照の <c>PostgreSqlLongDefaultConstraintNameIntegrationTests</c> が固定する。
/// </para>
/// </remarks>
public class LongDefaultConstraintNameTests
{
    private static readonly IDatabaseProvider[] Providers =
    [
        new SqlServerProvider(),
        new PostgreSqlProvider(),
        new MySqlProvider(),
        new OracleProvider(),
    ];

    /// <summary>上限のある 4 方言</summary>
    public static TheoryData<string> LimitedDialects =>
        ["sqlserver", "postgresql", "mysql", "oracle"];

    private static IDatabaseProvider ProviderOf(string dbms) =>
        Providers.Single(provider => provider.Name == dbms);

    /// <summary>図の既定名（主キー 2・一意制約 1・外部キー 2）と、縮める前の名前</summary>
    private static List<(string Name, string Unshortened)> ExpectedNames(
        ErDiagram diagram,
        IdentifierLengthLimit limit
    )
    {
        var parent = diagram.Entities[0];
        var child = diagram.Entities[1];
        var unlimited = IdentifierLengthLimit.Unlimited;
        var names = new List<(string, string)>
        {
            (
                ConstraintNames.PrimaryKey(parent.TableName, limit),
                ConstraintNames.PrimaryKey(parent.TableName, unlimited)
            ),
            (
                ConstraintNames.PrimaryKey(child.TableName, limit),
                ConstraintNames.PrimaryKey(child.TableName, unlimited)
            ),
            (
                UniqueConstraint.SynthesizeName(child.TableName, ["bill_id", "ship_id"], limit),
                UniqueConstraint.SynthesizeName(child.TableName, ["bill_id", "ship_id"], unlimited)
            ),
        };
        var details = ConstraintNames.ResolveForeignKeyNameDetails(
            diagram.Entities,
            diagram.Relationships,
            limit
        );
        names.AddRange(
            diagram.Relationships.Select(r => (details[r].Name, details[r].UnshortenedName))
        );

        return names;
    }

    [Theory(DisplayName = "既定名は上限を超えるときだけ縮み、すべて上限以内で互いに違う（4 方言）")]
    [MemberData(nameof(LimitedDialects))]
    public void DefaultNames_AreShortenedWithinLimitAndDistinct(string dbms)
    {
        var limit = ProviderOf(dbms).SyncCapabilities.IdentifierLengthLimit;
        var names = ExpectedNames(LongNameDiagrams.Build(limit, dbms), limit);

        names.Should().AllSatisfy(pair => limit.Fits(pair.Name).Should().BeTrue(pair.Name));
        names.Should().AllSatisfy(pair => pair.Name.Should().NotBe(pair.Unshortened));
        names.Select(pair => pair.Name).Should().OnlyHaveUniqueItems();
    }
}
