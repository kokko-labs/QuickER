using System.Globalization;
using System.Text;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider.Sync.Steps;

namespace QuickER.Provider;

/// <summary>
/// セクション見出し方式（<c>-- ===== {Kind} ({n} items) =====</c>）で同期スクリプトを組み立てる共通基底。
/// </summary>
/// <remarks>
/// <para>
/// SQL Server / PostgreSQL / MySQL は「種別ごとに見出しコメント → 各文 → 空行」という同一の骨格を持つ。
/// その連結処理をここへ集約し、各方言は文の型ごとの DDL 生成（<c>Append*</c> オーバーライド）だけを実装する。
/// セクションは計画の文の並びを <see cref="StepSections"/> でまとめたもの。
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

        // 列の無い表を作る文も止める（計画は列の無い表の追加を外すので、計画を経ずに組んだ計画への二重の守り）
        SqlTableShape.Validate(plan);

        var sb = new StringBuilder();

        foreach (var section in StepSections.Of(plan))
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

    /// <summary>1 セクション分（見出しコメント → 各文 → 空行）を書き出す</summary>
    /// <remarks>逐次 DDL 方言の骨格そのもの。再構築方言が特定セクションだけ描画する際にも再利用する。</remarks>
    protected void AppendSection(StringBuilder sb, StepSection section)
    {
        sb.AppendLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"-- ===== {SectionLabel(section)} ({section.Steps.Count} items) ====="
            )
        );

        foreach (var step in section.Steps)
        {
            AppendStep(sb, step);
        }

        AppendSectionEnd(sb, section);
        sb.AppendLine();
    }

    /// <summary>セクションの全部の文の後に書き出す文（既定は何もしない）</summary>
    /// <remarks>
    /// セクションの文をすべて書いた後でしか決まらない後始末を持つ方言（MySQL の外部キーの索引）だけが上書きする。
    /// </remarks>
    protected virtual void AppendSectionEnd(StringBuilder sb, StepSection section) { }

    /// <summary>セクション見出しに使う識別（主キー変更のみフェーズを併記する・固定文は英語が正本）</summary>
    /// <remarks>
    /// 主キー変更は Drop / Add の 2 セクションへ分かれるため、見出しだけでは区別できない。フェーズを持たない
    /// セクション（<see cref="PrimaryKeyPhase.None"/>）は種別名のみを見出しに使う。
    /// </remarks>
    private static string SectionLabel(StepSection section) =>
        section.PrimaryKeyPhase == PrimaryKeyPhase.None
            ? section.Kind.ToString()
            : $"{section.Kind}: {section.PrimaryKeyPhase}";

    /// <summary>1 つの文を文の型に応じた <c>Append*</c> へ振り分ける</summary>
    /// <exception cref="System.InvalidOperationException">セクションに入らない文の型のとき（黙って飛ばさない）</exception>
    private void AppendStep(StringBuilder sb, SyncStep step)
    {
        switch (step)
        {
            case CreateTableStep s:
                AppendCreateTable(sb, s);
                break;

            case AddColumnStep s:
                AppendAddColumn(sb, s);
                break;

            case AlterColumnStep s:
                AppendAlterColumn(sb, s);
                break;

            case DropPrimaryKeyStep s:
                AppendDropPrimaryKey(sb, s);
                break;

            case AddPrimaryKeyStep s:
                AppendAddPrimaryKey(sb, s);
                break;

            case DropForeignKeyStep s:
                AppendDropForeignKey(sb, s);
                break;

            case AddUniqueConstraintStep s:
                AppendAddUniqueConstraint(sb, s);
                break;

            case DropUniqueConstraintStep s:
                AppendDropUniqueConstraint(sb, s);
                break;

            case DropColumnStep s:
                AppendDropColumn(sb, s);
                break;

            case DropTableStep s:
                AppendDropTable(sb, s);
                break;

            case AddForeignKeyStep s:
                AppendAddForeignKey(sb, s);
                break;

            case SetTableDescriptionStep s:
                AppendSetTableDescription(sb, s);
                break;

            case SetColumnDescriptionStep s:
                AppendSetColumnDescription(sb, s);
                break;

            default:
                throw new System.InvalidOperationException(
                    $"The step {step.GetType().Name} is not rendered in a section."
                );
        }
    }

    /// <summary>この方言の識別子の長さの上限（既定名を縮めるのに使う＝方言の識別子クラスの <c>Limit</c> を返す）</summary>
    /// <remarks>経路の正本は <see cref="ConstraintNames"/> の XmlDoc。抽象にして、方言が宣言を忘れたらビルドで止まる形にする。</remarks>
    protected abstract IdentifierLengthLimit IdentifierLimit { get; }

    /// <summary>このスクリプトが対象とする方言の名前（<see cref="DialectNames"/>）</summary>
    /// <remarks>
    /// 図で表せない定義の扱い（<see cref="UnsupportedTreatments"/>）をこの方言のマスで引くのに使う。
    /// 抽象にして、方言が宣言を忘れたらビルドで止まる形にする。
    /// </remarks>
    protected abstract string DialectName { get; }

    /// <summary>CREATE TABLE 文（主キー制約を含む）を書き出す</summary>
    protected abstract void AppendCreateTable(StringBuilder sb, CreateTableStep step);

    /// <summary>列追加文を書き出す</summary>
    protected abstract void AppendAddColumn(StringBuilder sb, AddColumnStep step);

    /// <summary>列定義変更文を書き出す</summary>
    protected abstract void AppendAlterColumn(StringBuilder sb, AlterColumnStep step);

    /// <summary>主キー変更の解除フェーズ（旧主キー制約の DROP）文を書き出す</summary>
    /// <remarks>
    /// 既定は「この方言では描画しない」スキップコメント（英語が正本）。主キー変更の DDL を実装した方言だけが
    /// 上書きする。テーブル再構築方言（SQLite）は主キー変更を再構築へ畳むためセクションには現れない。
    /// </remarks>
    protected virtual void AppendDropPrimaryKey(StringBuilder sb, DropPrimaryKeyStep step) =>
        AppendPrimaryKeyNotRendered(sb, PrimaryKeyPhase.Drop, step.TableName);

    /// <summary>主キー変更の付与フェーズ（新主キー制約の ADD）文を書き出す</summary>
    /// <remarks>既定は解除フェーズと同じくスキップコメント（英語が正本）。</remarks>
    protected virtual void AppendAddPrimaryKey(StringBuilder sb, AddPrimaryKeyStep step) =>
        AppendPrimaryKeyNotRendered(sb, PrimaryKeyPhase.Add, step.TableName);

    /// <summary>主キー変更を描画しない方言のスキップコメント（英語が正本）</summary>
    private static void AppendPrimaryKeyNotRendered(
        StringBuilder sb,
        PrimaryKeyPhase phase,
        string tableName
    ) =>
        sb.AppendLine(
            // テーブル名は自由入力なので改行・制御文字を畳んでコメント行の突き破りを防ぐ
            $"-- Skipped '{SchemaDiffKind.AlterPrimaryKey}' ({phase} phase) on {SqlComment.Sanitize(tableName)}: "
                + "primary key changes are not rendered by this dialect."
        );

    /// <summary>外部キー削除文を書き出す</summary>
    protected abstract void AppendDropForeignKey(StringBuilder sb, DropForeignKeyStep step);

    /// <summary>一意制約の追加文を書き出す</summary>
    /// <remarks>
    /// 制約名は文の名前（図側のモデル名。未設定なら <see cref="UniqueConstraintNaming.Resolve"/> が <c>UQ_{表}_{列…}</c> を
    /// 合成する）を用いる。
    /// </remarks>
    protected abstract void AppendAddUniqueConstraint(
        StringBuilder sb,
        AddUniqueConstraintStep step
    );

    /// <summary>一意制約の削除文を書き出す</summary>
    /// <remarks>制約名は文の名前（live 側の実名）を用いる。</remarks>
    protected abstract void AppendDropUniqueConstraint(
        StringBuilder sb,
        DropUniqueConstraintStep step
    );

    /// <summary>列削除文を書き出す</summary>
    protected abstract void AppendDropColumn(StringBuilder sb, DropColumnStep step);

    /// <summary>テーブル削除文を書き出す</summary>
    protected abstract void AppendDropTable(StringBuilder sb, DropTableStep step);

    /// <summary>外部キー追加文を書き出す</summary>
    protected abstract void AppendAddForeignKey(StringBuilder sb, AddForeignKeyStep step);

    /// <summary>テーブル説明の設定文を書き出す</summary>
    protected abstract void AppendSetTableDescription(
        StringBuilder sb,
        SetTableDescriptionStep step
    );

    /// <summary>列説明の設定文を書き出す</summary>
    protected abstract void AppendSetColumnDescription(
        StringBuilder sb,
        SetColumnDescriptionStep step
    );
}
