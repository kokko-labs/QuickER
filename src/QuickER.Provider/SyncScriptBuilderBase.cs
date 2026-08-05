using System.Text;

namespace QuickER.Provider;

/// <summary>
/// セクション見出し方式（<c>-- ===== {Kind} ({n} items) =====</c>）で同期スクリプトを組み立てる共通基底。
/// </summary>
/// <remarks>
/// <para>
/// SQL Server / PostgreSQL / MySQL は「種別ごとに見出しコメント → 各項目 → 空行」という同一の骨格を持つ。
/// その連結処理をここへ集約し、各方言は種別ごとの DDL 生成（<c>Append*</c> オーバーライド）だけを実装する。
/// </para>
/// <para>
/// 文の区切り規約が根本的に異なる Oracle（「/」のみの行で連結）はこの基底を継承せず、
/// <see cref="ISyncScriptBuilder"/> を直接実装する。
/// </para>
/// </remarks>
public abstract class SyncScriptBuilderBase : ISyncScriptBuilder
{
    /// <summary>実行計画のセクションを見出しコメント付きで連結し、同期スクリプトへ変換する</summary>
    /// <remarks>
    /// テーブル再構築を要する方言（SQLite）は本メソッドを上書きし、<see cref="AppendSection"/> でセクション単位の
    /// レンダリングを再利用しつつ、再構築ブロックを織り交ぜる。逐次 DDL 方言はこの既定実装をそのまま用いる
    /// （<see cref="SyncPlan.Rebuilds"/> は空のため参照しない＝出力は従来どおり）。
    /// </remarks>
    public virtual string Build(SyncPlan plan)
    {
        var sb = new StringBuilder();

        foreach (var section in plan.Sections)
        {
            AppendSection(sb, section);
        }

        // ネイティブ列順変更（MySQL のみ・既定 no-op）。Reorders が空である限り出力はセクションのみで不変。
        AppendReorders(sb, plan);

        return sb.ToString();
    }

    /// <summary>ネイティブ列順変更（<see cref="SyncPlan.Reorders"/>）を書き出す（既定は no-op）</summary>
    /// <remarks>
    /// ネイティブ並べ替えを持つ方言（MySQL）だけが上書きする。<see cref="SyncPlan.Reorders"/> は
    /// Native 方言以外では常に空のため、既定 no-op により他方言の出力はセクションのみで従来どおり不変になる。
    /// </remarks>
    protected virtual void AppendReorders(StringBuilder sb, SyncPlan plan) { }

    /// <summary>1 セクション分（見出しコメント → 各項目 → 空行）を書き出す</summary>
    /// <remarks>逐次 DDL 方言の骨格そのもの。再構築方言が特定セクションだけ描画する際にも再利用する。</remarks>
    protected void AppendSection(StringBuilder sb, SyncPlanSection section)
    {
        sb.AppendLine($"-- ===== {section.Kind} ({section.Items.Count} items) =====");

        foreach (var item in section.Items)
        {
            AppendItem(sb, section.Kind, item);
        }

        sb.AppendLine();
    }

    /// <summary>1 差分項目を種別に応じた <c>Append*</c> へディスパッチする</summary>
    /// <remarks>プランナーが除外済みのため、想定外の種別（RebuildTable など）は無視する</remarks>
    private void AppendItem(StringBuilder sb, SchemaDiffKind kind, SchemaDiffItem item)
    {
        switch (kind)
        {
            case SchemaDiffKind.AddTable:
                AppendCreateTable(sb, item);
                break;

            case SchemaDiffKind.AddColumn:
                AppendAddColumn(sb, item);
                break;

            case SchemaDiffKind.AlterColumn:
                AppendAlterColumn(sb, item);
                break;

            case SchemaDiffKind.AlterPrimaryKey:
                AppendAlterPrimaryKey(sb, item);
                break;

            case SchemaDiffKind.DropForeignKey:
                AppendDropForeignKey(sb, item);
                break;

            case SchemaDiffKind.DropColumn:
                AppendDropColumn(sb, item);
                break;

            case SchemaDiffKind.DropTable:
                AppendDropTable(sb, item);
                break;

            case SchemaDiffKind.AddForeignKey:
                AppendAddForeignKey(sb, item);
                break;

            case SchemaDiffKind.SetTableDescription:
                AppendSetTableDescription(sb, item);
                break;

            case SchemaDiffKind.SetColumnDescription:
                AppendSetColumnDescription(sb, item);
                break;
        }
    }

    /// <summary>CREATE TABLE 文（主キー制約を含む）を書き出す</summary>
    protected abstract void AppendCreateTable(StringBuilder sb, SchemaDiffItem item);

    /// <summary>列追加文を書き出す</summary>
    protected abstract void AppendAddColumn(StringBuilder sb, SchemaDiffItem item);

    /// <summary>列定義変更文を書き出す</summary>
    protected abstract void AppendAlterColumn(StringBuilder sb, SchemaDiffItem item);

    /// <summary>主キー変更（旧主キー制約の解除 → 新主キー制約の付与）文を書き出す</summary>
    /// <remarks>
    /// 既定は「この方言では描画しない」スキップコメント（英語が正本）。主キー変更の DDL を実装した方言だけが
    /// 上書きする。テーブル再構築方言（SQLite）は主キー変更を再構築へ畳むためセクションには現れない。
    /// </remarks>
    protected virtual void AppendAlterPrimaryKey(StringBuilder sb, SchemaDiffItem item) =>
        sb.AppendLine(
            $"-- Skipped '{SchemaDiffKind.AlterPrimaryKey}' on {item.TableName}: "
                + "primary key changes are not rendered by this dialect."
        );

    /// <summary>外部キー削除文を書き出す</summary>
    protected abstract void AppendDropForeignKey(StringBuilder sb, SchemaDiffItem item);

    /// <summary>列削除文を書き出す</summary>
    protected abstract void AppendDropColumn(StringBuilder sb, SchemaDiffItem item);

    /// <summary>テーブル削除文を書き出す</summary>
    protected abstract void AppendDropTable(StringBuilder sb, SchemaDiffItem item);

    /// <summary>外部キー追加文を書き出す</summary>
    protected abstract void AppendAddForeignKey(StringBuilder sb, SchemaDiffItem item);

    /// <summary>テーブル説明の設定文を書き出す</summary>
    protected abstract void AppendSetTableDescription(StringBuilder sb, SchemaDiffItem item);

    /// <summary>列説明の設定文を書き出す</summary>
    protected abstract void AppendSetColumnDescription(StringBuilder sb, SchemaDiffItem item);
}
