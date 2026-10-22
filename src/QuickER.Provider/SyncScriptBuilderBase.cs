using System.Text;
using QuickER.Dialects;
using QuickER.Model;

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
/// 文の区切り規約が根本的に異なる Oracle（通常の文は <c>;</c> で終え、PL/SQL ブロックの後ろにだけ <c>/</c> 行を置く）は
/// この基底を継承せず、<see cref="ISyncScriptBuilder"/> を直接実装する。
/// </para>
/// </remarks>
public abstract class SyncScriptBuilderBase : ISyncScriptBuilder
{
    /// <summary>実行計画のセクションを見出しコメント付きで連結し、同期スクリプトへ変換する</summary>
    /// <remarks>
    /// テーブル再構築を要する方言（SQLite）は本メソッドを上書きし、<see cref="AppendSection"/> でセクション単位の
    /// レンダリングを再利用しつつ、再構築ブロックを織り交ぜる。逐次 DDL 方言はこの既定実装をそのまま用いる
    /// （<see cref="SyncPlan.Rebuilds"/> は空のため参照しない）。
    /// </remarks>
    public virtual string Build(SyncPlan plan)
    {
        // 列型は識別子でも文字列リテラルでもなく、SQL へ素通しで補間するしか無い（DDL 生成と同じ関門）
        SqlTypeText.Validate(plan);

        // 名前の改行・制御文字も入口で止める（コメントのサニタイズ・識別子のエスケープとの二重化）。
        // あわせて方言の上限を超える名前・方言が表せない文字を含む名前も、場所を名指しして止める
        SqlNameText.Validate(plan, IdentifierLimit);

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
    /// Native 方言以外では常に空のため、既定 no-op により他方言の出力はセクションのみになる。
    /// </remarks>
    protected virtual void AppendReorders(StringBuilder sb, SyncPlan plan) { }

    /// <summary>1 セクション分（見出しコメント → 各項目 → 空行）を書き出す</summary>
    /// <remarks>逐次 DDL 方言の骨格そのもの。再構築方言が特定セクションだけ描画する際にも再利用する。</remarks>
    protected void AppendSection(StringBuilder sb, SyncPlanSection section)
    {
        sb.AppendLine($"-- ===== {SectionLabel(section)} ({section.Items.Count} items) =====");

        foreach (var item in section.Items)
        {
            AppendItem(sb, section, item);
        }

        AppendSectionEnd(sb, section);
        sb.AppendLine();
    }

    /// <summary>セクションの全項目の後に書き出す文（既定は何もしない）</summary>
    /// <remarks>
    /// セクションの項目をすべて書いた後でしか決まらない後始末を持つ方言（MySQL の外部キーの索引）だけが上書きする。
    /// </remarks>
    protected virtual void AppendSectionEnd(StringBuilder sb, SyncPlanSection section) { }

    /// <summary>セクション見出しに使う識別（主キー変更のみフェーズを併記する・固定文は英語が正本）</summary>
    /// <remarks>
    /// 主キー変更は Drop / Add の 2 セクションへ分かれるため、見出しだけでは区別できない。フェーズを持たない
    /// セクション（<see cref="PrimaryKeyPhase.None"/>）は種別名のみを見出しに使う。
    /// </remarks>
    private static string SectionLabel(SyncPlanSection section) =>
        section.PrimaryKeyPhase == PrimaryKeyPhase.None
            ? section.Kind.ToString()
            : $"{section.Kind}: {section.PrimaryKeyPhase}";

    /// <summary>1 差分項目を種別に応じた <c>Append*</c> へディスパッチする</summary>
    /// <remarks>プランナーが除外済みのため、想定外の種別（RebuildTable など）は無視する</remarks>
    private void AppendItem(StringBuilder sb, SyncPlanSection section, SchemaDiffItem item)
    {
        switch (section.Kind)
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
                AppendPrimaryKeyPhase(sb, section.PrimaryKeyPhase, item);
                break;

            case SchemaDiffKind.DropForeignKey:
                AppendDropForeignKey(sb, item);
                break;

            case SchemaDiffKind.AddUniqueConstraint:
                AppendAddUniqueConstraint(sb, item);
                break;

            case SchemaDiffKind.DropUniqueConstraint:
                AppendDropUniqueConstraint(sb, item);
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
    /// <summary>この方言の識別子の長さの上限（既定名を縮めるのに使う＝方言の識別子クラスの <c>Limit</c> を返す）</summary>
    /// <remarks>経路の正本は <see cref="ConstraintNames"/> の XmlDoc。抽象にして、方言が宣言を忘れたらビルドで止まる形にする。</remarks>
    protected abstract IdentifierLengthLimit IdentifierLimit { get; }

    /// <summary>このスクリプトが対象とする方言の名前（<see cref="DialectNames"/>）</summary>
    /// <remarks>
    /// 図で表せない定義の扱い（<see cref="UnsupportedTreatments"/>）をこの方言のマスで引くのに使う。
    /// 抽象にして、方言が宣言を忘れたらビルドで止まる形にする。
    /// </remarks>
    protected abstract string DialectName { get; }

    protected abstract void AppendCreateTable(StringBuilder sb, SchemaDiffItem item);

    /// <summary>列追加文を書き出す</summary>
    protected abstract void AppendAddColumn(StringBuilder sb, SchemaDiffItem item);

    /// <summary>列定義変更文を書き出す</summary>
    protected abstract void AppendAlterColumn(StringBuilder sb, SchemaDiffItem item);

    /// <summary>主キー変更の 1 項目を、セクションのフェーズに応じた解除 / 付与へ振り分ける</summary>
    /// <remarks>
    /// フェーズ指定の無いセクション（<see cref="PrimaryKeyPhase.None"/>＝再構築方言の残余など）は、
    /// 解除 → 付与を 1 セクション内で連続出力する。
    /// </remarks>
    private void AppendPrimaryKeyPhase(StringBuilder sb, PrimaryKeyPhase phase, SchemaDiffItem item)
    {
        if (phase is PrimaryKeyPhase.None or PrimaryKeyPhase.Drop)
        {
            AppendDropPrimaryKey(sb, item);
        }

        if (phase is PrimaryKeyPhase.None or PrimaryKeyPhase.Add)
        {
            AppendAddPrimaryKey(sb, item);
        }
    }

    /// <summary>主キー変更の解除フェーズ（旧主キー制約の DROP）文を書き出す</summary>
    /// <remarks>
    /// 既定は「この方言では描画しない」スキップコメント（英語が正本）。主キー変更の DDL を実装した方言だけが
    /// 上書きする。テーブル再構築方言（SQLite）は主キー変更を再構築へ畳むためセクションには現れない。
    /// </remarks>
    protected virtual void AppendDropPrimaryKey(StringBuilder sb, SchemaDiffItem item) =>
        AppendPrimaryKeyNotRendered(sb, PrimaryKeyPhase.Drop, item);

    /// <summary>主キー変更の付与フェーズ（新主キー制約の ADD）文を書き出す</summary>
    /// <remarks>既定は解除フェーズと同じくスキップコメント（英語が正本）。</remarks>
    protected virtual void AppendAddPrimaryKey(StringBuilder sb, SchemaDiffItem item) =>
        AppendPrimaryKeyNotRendered(sb, PrimaryKeyPhase.Add, item);

    /// <summary>主キー変更を描画しない方言のスキップコメント（英語が正本）</summary>
    private static void AppendPrimaryKeyNotRendered(
        StringBuilder sb,
        PrimaryKeyPhase phase,
        SchemaDiffItem item
    ) =>
        sb.AppendLine(
            // テーブル名は自由入力なので改行・制御文字を畳んでコメント行の突き破りを防ぐ
            $"-- Skipped '{SchemaDiffKind.AlterPrimaryKey}' ({phase} phase) on {SqlComment.Sanitize(item.TableName)}: "
                + "primary key changes are not rendered by this dialect."
        );

    /// <summary>外部キー削除文を書き出す</summary>
    protected abstract void AppendDropForeignKey(StringBuilder sb, SchemaDiffItem item);

    /// <summary>一意制約の追加文を書き出す</summary>
    /// <remarks>
    /// 制約名は <see cref="SchemaDiffItem.UniqueConstraintName"/>（図側のモデル名。未設定なら
    /// <see cref="UniqueConstraintNaming.Resolve"/> が <c>UQ_{表}_{列…}</c> を合成する）を用いる。
    /// </remarks>
    protected abstract void AppendAddUniqueConstraint(StringBuilder sb, SchemaDiffItem item);

    /// <summary>一意制約の削除文を書き出す</summary>
    /// <remarks>制約名は <see cref="SchemaDiffItem.UniqueConstraintName"/>（live 側の実名）を用いる。</remarks>
    protected abstract void AppendDropUniqueConstraint(StringBuilder sb, SchemaDiffItem item);

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
