using System.Text;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.Provider.SqlServer;

/// <summary>
/// ER 図から SQL Server 向けの DDL（<c>CREATE TABLE</c> / <c>ALTER TABLE ... ADD CONSTRAINT</c>）を生成する
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>識別子は <see cref="SqlIdentifier"/> で角括弧付けする（<c>schema.table</c> は <c>[schema].[table]</c> に分割、<c>]</c> は <c>]]</c> にエスケープ）</item>
///   <item>テーブル・列の説明は拡張プロパティ <c>MS_Description</c> として全 CREATE / FK の後に出力する
///     （取込が <c>MS_Description</c> を読むため、DDL から新規 DB を作るフローでも説明が往復するようにする）</item>
///   <item>共通の DDL 組み立てロジックは <see cref="DdlGeneratorBase"/> を参照</item>
/// </list>
/// </remarks>
public sealed class SqlServerDdlGenerator : DdlGeneratorBase
{
    /// <inheritdoc />
    protected override IdentifierLengthLimit IdentifierLimit => SqlIdentifier.Limit;

    /// <inheritdoc />
    protected override string QuoteQualifiedName(string name) => SqlIdentifier.Bracket(name);

    /// <inheritdoc />
    protected override string QuoteSimpleName(string name) => SqlIdentifier.BracketSimple(name);

    /// <inheritdoc />
    protected override string QuoteConstraintName(string constraintName) =>
        $"[{SqlIdentifier.Escape(constraintName)}]";

    /// <summary>テーブル・列の説明を拡張プロパティ <c>MS_Description</c> の追加文として出力する</summary>
    /// <remarks>
    /// 新規 DB 作成用 DDL のため既存プロパティは無く、存在チェックや update / drop は不要＝<c>sp_addextendedproperty</c> の追加のみ。
    /// バッチ区切り <c>GO</c> は本 DDL の他文（CREATE / ALTER）に合わせて出力しない（同期スクリプトの GO 方式とは別）。
    /// スキーマ・テーブル・列を指す引数は <see cref="SqlServerSyncScriptBuilder"/> の説明文出力と共有する（<see cref="SqlServerExtendedProperty"/>）。
    /// スキーマ修飾の無い表は接続ユーザーの既定スキーマを指すので、既定スキーマの変数を最初に使う文の前で 1 回だけ宣言する
    /// </remarks>
    protected override void AppendDescriptions(StringBuilder sb, ErDiagram diagram)
    {
        // 修飾の無い表の説明は既定スキーマの変数を使うので、最初に使う文の前で 1 回だけ宣言する（DDL は 1 バッチ）
        var declared = false;

        string WithDeclaration(Entity entity, string statement)
        {
            if (declared || !SqlServerExtendedProperty.UsesDefaultSchema(entity.TableName))
            {
                return statement;
            }

            declared = true;
            return SqlServerExtendedProperty.DeclareDefaultSchema + Environment.NewLine + statement;
        }

        AppendDescriptionStatements(
            sb,
            diagram,
            entity =>
                WithDeclaration(
                    entity,
                    BuildAddDescription(entity.TableName, entity.Description, columnName: null)
                ),
            (entity, column) =>
                WithDeclaration(
                    entity,
                    BuildAddDescription(entity.TableName, column.Description, column.Name)
                )
        );
    }

    /// <summary>拡張プロパティ <c>MS_Description</c> を追加する <c>sp_addextendedproperty</c> 文を組み立てる</summary>
    /// <param name="tableName">対象テーブル名（<c>schema.table</c> 形式ならスキーマを分解する。修飾が無ければ既定スキーマ）</param>
    /// <param name="description">設定する説明（N リテラルとしてエスケープする）</param>
    /// <param name="columnName">列名（<c>null</c> ならテーブルレベル、指定時はカラムレベルの拡張プロパティ）</param>
    private static string BuildAddDescription(
        string tableName,
        string description,
        string? columnName
    ) =>
        "EXEC sys.sp_addextendedproperty @name=N'MS_Description', "
        + $"@value=N'{SqlIdentifier.EscapeStringLiteral(description)}', "
        + $"{SqlServerExtendedProperty.LevelArguments(tableName, columnName)};";
}
