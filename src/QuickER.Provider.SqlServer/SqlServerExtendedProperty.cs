namespace QuickER.Provider.SqlServer;

/// <summary>
/// 説明（拡張プロパティ <c>MS_Description</c>）の文の、スキーマ・テーブル・列を指す部分を組み立てる
/// （DDL 生成と同期スクリプトが共有する）。
/// </summary>
/// <remarks>
/// <para>
/// スキーマ修飾の無い表名は、<c>CREATE TABLE [Widget]</c> と同じく<b>接続ユーザーの既定スキーマ</b>を指す。
/// 説明の文も同じ表を指すよう、修飾の無い表のスキーマは <c>SCHEMA_NAME()</c>（既定スキーマ）で求める
/// （<c>dbo</c> を焼き込むと、既定スキーマが <c>dbo</c> でないユーザーでは表は既定スキーマに作られるのに説明は
/// <c>dbo</c> の同名の表を探して失敗し、同期全体がロールバックする）。
/// </para>
/// <para>
/// <c>sp_addextendedproperty</c> などの引数には関数呼び出しを書けないため、既定スキーマは変数
/// <see cref="DefaultSchemaVariable"/> に入れて渡す。変数はバッチの中でしか生きないので、宣言
/// （<see cref="DeclareDefaultSchema"/>）は使うバッチの中で 1 回だけ置く（DDL は説明の節で 1 回、
/// 同期スクリプトは説明の文ごと＝文ごとに <c>GO</c> で区切るため）。修飾つきの表名の出力は変数を使わない。
/// </para>
/// </remarks>
public static class SqlServerExtendedProperty
{
    /// <summary>既定スキーマを入れる変数の名前</summary>
    public const string DefaultSchemaVariable = "@default_schema";

    /// <summary>既定スキーマを変数へ入れる宣言（バッチの中で 1 回だけ置く）</summary>
    public const string DeclareDefaultSchema =
        "DECLARE " + DefaultSchemaVariable + " sysname = SCHEMA_NAME();";

    /// <summary>表名がスキーマで修飾されていない（＝既定スキーマを指す）か</summary>
    public static bool UsesDefaultSchema(string tableName) => !tableName.Contains('.');

    /// <summary><c>@level0type</c> 〜 <c>@level2name</c> の引数を組み立てる（<paramref name="columnName"/> が null ならテーブルの説明）</summary>
    public static string LevelArguments(string tableName, string? columnName)
    {
        var schemaArgument = UsesDefaultSchema(tableName)
            ? DefaultSchemaVariable
            : $"N'{SqlIdentifier.EscapeStringLiteral(SqlIdentifier.SchemaOf(tableName))}'";
        var table = SqlIdentifier.TableNameOnly(tableName);

        var levelArgs =
            $"@level0type=N'SCHEMA', @level0name={schemaArgument}, "
            + $"@level1type=N'TABLE',  @level1name=N'{SqlIdentifier.EscapeStringLiteral(table)}'";

        if (columnName is not null)
        {
            levelArgs +=
                $", @level2type=N'COLUMN', @level2name=N'{SqlIdentifier.EscapeStringLiteral(columnName)}'";
        }

        return levelArgs;
    }

    /// <summary>説明の存在確認に使う <c>OBJECT_ID(N'…')</c> を組み立てる</summary>
    /// <remarks>修飾の無い表名は括弧付けして渡す＝<c>OBJECT_ID</c> も既定スキーマで名前を解決する</remarks>
    public static string ObjectIdLiteral(string tableName) =>
        UsesDefaultSchema(tableName)
            ? $"OBJECT_ID(N'{SqlIdentifier.QuoteForDynamicSql(tableName)}')"
            : $"OBJECT_ID(N'{SqlIdentifier.EscapeStringLiteral(SqlIdentifier.SchemaOf(tableName))}.{SqlIdentifier.EscapeStringLiteral(SqlIdentifier.TableNameOnly(tableName))}')";
}
