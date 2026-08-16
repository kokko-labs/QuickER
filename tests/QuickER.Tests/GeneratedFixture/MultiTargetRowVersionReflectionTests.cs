using System.Linq;
using System.Reflection;
using AwesomeAssertions;
using QuickER.Tests.GeneratedMultiTargetRowVersionFixture;

namespace QuickER.Tests.GeneratedMultiTargetRowVersionFixture;

/// <summary>
/// マルチターゲット × rowversion の不変条件を、生成テキストの断片一致ではなく
/// <b>コンパイル済みフィクスチャ型のリフレクション</b>で表明するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 生成テキストの <c>Contain</c> 表明は「そう書かれている」ことしか言わず、テンプレートの整形変更で壊れる。
/// フィクスチャはテストアセンブリで実際にコンパイルされるため、挙動に直結する不変条件は
/// <b>実際にビルドしたメタデータ・属性</b>を読める。ドリフト再生成（<c>QUICKER_REGEN_FIXTURES=1</c>）とは
/// 独立した根拠になる＝「再生成すれば緑になる」構造の受け皿。
/// </para>
/// <para>
/// ここで固定するのは、案 B（マルチターゲット × rowversion）の核心である 2 つの非対称:
/// </para>
/// <list type="bullet">
///   <item>SQL Server 実装は <c>row_ver</c> を INSERT 対象から<b>外す</b>（DB が採番するため明示挿入は実行時エラー）</item>
///   <item>SQLite 実装は同じ列を<b>通常のバイナリ列として書き込む</b>（サーバー版のミラー置き場）</item>
/// </list>
/// <para>
/// あわせて属性の水準でも 2 点＝<c>[StoreGeneratedColumn]</c> は付き、<c>[DbColumnMeta]</c> は付かない
/// （中立トークンは「DB が採番する」を運べず、刻むと C# リバースがただのバイナリ列として復元して版ガードが黙って消える）。
/// </para>
/// </remarks>
public class MultiTargetRowVersionReflectionTests
{
    /// <summary>rowversion 列に対応する生成プロパティ名</summary>
    private const string RowVersionPropertyName = "RowVer";

    /// <summary>主キー列に対応する生成プロパティ名</summary>
    private const string KeyPropertyName = "ItemId";

    /// <summary>
    /// SQL Server エンジンのメタデータが rowversion 列を INSERT / UPDATE 対象から外していることを、
    /// 実際に構築した <c>EntitySaveMetadata</c> から読み取って検証する。
    /// </summary>
    [Fact(
        DisplayName = "SQL Server 実装の EntitySaveMetadata は rowversion 列を書き込み対象から除外する"
    )]
    public void SqlServerMetadata_ShouldExcludeRowVersionFromWrites()
    {
        var metadata = Repositories.SqlServer.EntitySaveMetadata.For(typeof(SyncItemEntity));

        Names(metadata.InsertProperties)
            .Should()
            .NotContain(
                RowVersionPropertyName,
                "SQL Server は rowversion を DB 側で採番するため、明示挿入すると実行時エラーになる"
            );
        Names(metadata.NonKeyProperties)
            .Should()
            .NotContain(
                RowVersionPropertyName,
                "UPDATE の SET 句へ版を書くと、版ガードそのものが意味を失う"
            );

        // 対照: 通常列は当然含まれる（除外が「全部落ちている」わけではないことの確認）
        Names(metadata.InsertProperties).Should().Contain("Name");

        // SELECT では取得する（並行性トークンは読める）
        Names(metadata.SelectProperties).Should().Contain(RowVersionPropertyName);
        metadata
            .RowVersionProperty?.Name.Should()
            .Be(RowVersionPropertyName, "版ガード SQL の組み立てに版プロパティの解決が必要");
    }

    /// <summary>
    /// SQLite エンジンのメタデータが、同じ列を通常列として INSERT / UPDATE 対象に含めていることを検証する。
    /// </summary>
    /// <remarks>
    /// ここが「方言ゲート」の反対側のアーム。SQLite は採番しないため、書けなければサーバー版のミラーを
    /// ローカルへ保存できない（＝ハイブリッド同期が成立しない）。
    /// </remarks>
    [Fact(DisplayName = "SQLite 実装の EntitySaveMetadata は rowversion 列を通常列として書き込む")]
    public void SqliteMetadata_ShouldIncludeRowVersionInWrites()
    {
        var metadata = Repositories.Sqlite.EntitySaveMetadata.For(typeof(SyncItemEntity));

        Names(metadata.InsertProperties)
            .Should()
            .Contain(
                RowVersionPropertyName,
                "SQLite は採番しないため、サーバーで取った版を書き込めなければミラーにならない"
            );
        Names(metadata.NonKeyProperties)
            .Should()
            .Contain(RowVersionPropertyName, "更新時もミラー値を書き替えられる必要がある");

        // NonKeyProperties は「PK を除くだけ」＝主キーだけが落ちていることの対照
        Names(metadata.NonKeyProperties).Should().NotContain(KeyPropertyName);
        Names(metadata.SelectProperties).Should().Contain(RowVersionPropertyName);
    }

    /// <summary>
    /// 同じ列・同じ Entity に対して、2 つのエンジンの書き込み対象が実際に食い違う（非対称が生きている）ことを検証する。
    /// </summary>
    /// <remarks>
    /// 片側だけを見るテストは、両方が同じ側へ倒れた退行（例: 除外が全方言へ効く／どの方言でも効かない）を見逃す。
    /// </remarks>
    [Fact(
        DisplayName = "同一 Entity に対する 2 エンジンの INSERT 対象が rowversion 列だけ食い違う"
    )]
    public void TwoEngines_ShouldDifferOnlyByRowVersionColumn()
    {
        var sqlServer = Names(
            Repositories.SqlServer.EntitySaveMetadata.For(typeof(SyncItemEntity)).InsertProperties
        );
        var sqlite = Names(
            Repositories.Sqlite.EntitySaveMetadata.For(typeof(SyncItemEntity)).InsertProperties
        );

        sqlite
            .Except(sqlServer)
            .Should()
            .Equal(
                [RowVersionPropertyName],
                "2 エンジンの INSERT 対象の差は rowversion 列 1 本だけであるべき"
            );
        sqlServer.Except(sqlite).Should().BeEmpty("SQL Server 側にだけ書く列は無いはず");
    }

    /// <summary>
    /// rowversion プロパティに <c>[StoreGeneratedColumn]</c> が付き、<c>[DbColumnMeta]</c> が付かないことを属性水準で検証する。
    /// </summary>
    /// <remarks>
    /// <c>[DbColumnMeta]</c>（方言中立の型トークン）は「DB が採番する」を運べない。刻むと C# リバースが
    /// ただのバイナリ列として図を復元し、次の生成で版ガードが黙って消える。canonical 種別へ <c>RowVersion</c> を
    /// 足した後もこの不変条件が保たれていることを、属性の実体で押さえる。
    /// </remarks>
    [Fact(
        DisplayName = "rowversion プロパティは [StoreGeneratedColumn] を持ち [DbColumnMeta] を持たない"
    )]
    public void RowVersionProperty_ShouldCarryStoreGeneratedButNotColumnMeta()
    {
        var rowVersion = typeof(SyncItemEntity).GetProperty(RowVersionPropertyName)!;

        rowVersion
            .GetCustomAttributes()
            .Select(attribute => attribute.GetType().Name)
            .Should()
            .Contain(
                nameof(StoreGeneratedColumnAttribute),
                "書き込み除外の判定は属性のリフレクションで行われる"
            )
            .And.NotContain(
                nameof(DbColumnMetaAttribute),
                "中立トークンを刻むと C# リバースが版ガードを落とした図を復元する"
            );

        // 対照: 通常列にはトークンが刻まれる（「この図ではそもそもトークンが出ない」空振りの排除）
        typeof(SyncItemEntity)
            .GetProperty("Name")!
            .GetCustomAttribute<DbColumnMetaAttribute>()
            .Should()
            .NotBeNull("通常列には方言中立トークンが刻まれる");
    }

    /// <summary>プロパティ一覧を名前の一覧へ落とす</summary>
    private static string[] Names(
        System.Collections.Generic.IReadOnlyList<PropertyInfo> properties
    ) => properties.Select(property => property.Name).ToArray();
}
