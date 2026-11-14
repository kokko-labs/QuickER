using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>静的な規則の判定の結果</summary>
/// <param name="PredictedFailure">計画の項目の並びのうち、DB が断ると規則が予言した最初の文（無ければ null）</param>
/// <param name="Violations">規則の違反（空なら合格）</param>
internal sealed record SyncPlanStaticVerdict(
    string? PredictedFailure,
    IReadOnlyList<string> Violations
)
{
    /// <summary>予言も違反も無い</summary>
    public static SyncPlanStaticVerdict None { get; } = new(null, []);
}

/// <summary>
/// 同期計画を DB を使わずに確かめる規則（能力の組の網＝<see cref="SyncPlanCapabilityNetTestsBase"/> と、実 DB の総当たりの
/// 突き合わせ＝<see cref="SyncPlanFuzzRunner"/> が使う）
/// </summary>
/// <remarks>
/// <para>
/// 計画のセクションの項目を並び（<see cref="SyncPlan.Sections"/> の順）に沿って、DB 側の制約（主キー・一意制約・外部キー）と
/// 列の型の状態を追い、各項目を当てる直前の状態で、DB がその文を断るかを DB の規則で判定する。最初に断られる文までを見る
/// （実行はそこで止まる）。
/// </para>
/// <list type="bullet">
///   <item>R1：主キー・一意制約・表を消す前に、それを参照する外部キーがすべて外れている（SQL Server 3725・PostgreSQL 2BP01・
///   Oracle ORA-02273 / ORA-02449・MySQL 1553）。MySQL（<see cref="SyncDialectCapabilities.ForeignKeyRequiresLeadingIndex"/>）は、
///   参照する列が消す索引の先頭に並ぶ外部キーも、ほかの索引が同じ先頭の列を持たなければ数える。</item>
///   <item>R2：SQL Server（<see cref="SyncDialectCapabilities.AlterColumnRequiresKeyConstraintRebuild"/>）の型の変更の
///   ALTER COLUMN の前に、その列を含む一意制約・主キーがすべて外れている（Msg 5074）。外部キー（両端）は
///   <see cref="SyncDialectCapabilities.AlterColumnRequiresForeignKeyRebuild"/> の方言（SQL Server・MySQL）で同じく外れている。
///   NULL 許容だけの変更は数えない（SQL Server は外部キーを張ったまま子の列を NOT NULL にできる＝製品の宣言どおり）。</item>
///   <item>R3：MySQL の索引の削除（一意制約の削除・主キーの解除）の前に、子の列がその索引の先頭に並ぶ外部キーが外れているか、
///   同じ先頭の列を持つ別の索引（主キー・一意制約・外部キーが作った索引）が残っている（MySQL 1553）。</item>
///   <item>R4：方言の名前の範囲で、足す制約の名前が残っている制約と重ならない（SQL Server・Oracle＝スキーマの全制約、
///   PostgreSQL＝主キー・一意制約はスキーマ・外部キーは表、MySQL＝外部キーはスキーマ・一意制約は表）。</item>
///   <item>R5：外部キーを足すとき、参照する列の集合が親の主キーか一意制約と一致する（候補キーの無い付け直しは DB が断る）。</item>
///   <item>R6：表の最後の列を消さない（SQL Server 4923・MySQL 1090・Oracle ORA-12983。PostgreSQL は列の無い表を許すので判定しない。
///   製品は同期後に列の残らない表の列の削除を計画から外す＝<see cref="SyncPlanWarningKind.TableLeftWithoutColumns"/>）。</item>
///   <item>R7：列の型の変更の後、残す外部キーのその列を含む組の両端を DB が必ず断る組にしない（PostgreSQL 42804・Oracle ORA-02267。外し直しの
///   方言は R2 が先に当たる）。断る組は測った事実（<see cref="SyncPlanFuzzDialect.RefusesForeignKeyTypes"/>）で判定する。製品の宣言
///   （<see cref="SyncDialectCapabilities.AreForeignKeyCompatibleTypes"/>）は「真なら DB が必ず受け入れる」保守側なので、偽の組の一部
///   （PostgreSQL の整数の子と小数の親・文字列どうし、Oracle の NUMBER どうし）を DB は受け入れる。その組は予言しない。</item>
///   <item>R8：外部キーを足すとき、組の両端のその時点の型を DB が必ず断る組にしない（MySQL 3780・SQL Server 1778 / 1753・PostgreSQL 42804・
///   Oracle ORA-02267）。DB 側の図の外部キーが同じ型のまま持っていた組（外して付け直す文）は、DB が受け入れたので数えない。断る組の判定は R7 と同じ。
///   この規則が無いと、live の列どうしの型の違う外部キーの追加が警告なしに断られるのを予言できない。</item>
///   <item>R9：同じ列の並びに一意制約を 2 つ張れない方言（Oracle＝<see cref="SyncDialectCapabilities.SupportsUniqueConstraintOnPrimaryKey"/>
///   が偽の逐次 DDL の方言）で、一意制約を足すとき、その時点で残る主キー・一意制約と列集合が同じでない（ORA-02261）。</item>
///   <item>R10：ケースが宣言した既存の行（とモデルの外の依存）で断られる文（<see cref="FuzzLiveSchema.DataFailures"/>）でない
///   （<see cref="DeclaredDataFailure"/>）。計画は値を見ないので予告の警告は求めず、D9 だけを掛ける（断られうる文の名指しの警告が名指しした
///   制約は D9 の外）。</item>
///   <item>R11：MySQL で、残す SET NULL の外部キー（方言が書く参照アクション）の子の列を NOT NULL にしない（1830）。SQL Server も
///   <see cref="SyncDialectCapabilities.SetNullRequiresNullableChildColumn"/> を宣言するが、検査は外部キーを作るときだけで、NULL 許容だけの
///   列の変更は外部キーを張ったまま通る（期待の行 realdb-fk-set-null-child-not-null-keeps-that-fk）。製品はこの文を計画の先頭へ移す
///   （<see cref="SyncPlanSection.IsMovedFirst"/>）。規則が無いと、先頭の文が実際には断られるのに、後ろの別の予言の失敗の前で外した
///   制約を D9 が数える。</item>
///   <item>R12：同じ定義の外部キー（子の表・親の表・列の組の集合）を 2 本持てない Oracle（ORA-02275）で、外部キーを足すとき、その時点で残る
///   外部キーと定義が同じでない。名前だけを変えた外部キーの追加を、元の外部キーの削除を選ばずに流す形。</item>
/// </list>
/// <para>
/// 違反になるのは、断られる文があるのに次のどちらでもないとき：(a) 失敗を予告する警告がある（<see cref="NonPredictingWarnings"/> 以外）
/// うえで、DDL が暗黙コミットされる方言（<see cref="SyncDialectCapabilities.TransactionalDdl"/> が偽）では、断られる文より前に
/// 「後ろで付け直す制約」を外していない（D9）。(b)「表・列の削除のために先に外した制約」が、断られる文より後ろの表・列の削除を待っていない
/// （D9 の規則 4 の形）。トランザクションの方言は失敗しても巻き戻るので、警告があれば (a) の外し方は問わない。利用者が選んだ制約の
/// 削除（付け直さない削除）は、失敗の前に走っても違反にしない（実 DB の表明 1b の規則 1 と同じ）。
/// 「予告の警告がある」は計画全体のどれかの警告（<see cref="NonPredictingWarnings"/> 以外）で判定し、断られる文と警告を結び付けない
/// （実 DB の表明 1 と同じ粒度）。
/// </para>
/// <para>
/// 警告の正しさの規則 W1（<see cref="SpuriousTypeMismatchWarnings"/>）は、予言の有無に依らず掛ける：
/// <see cref="SyncPlanWarningKind.ForeignKeyColumnTypeMismatch"/> が DB にある外部キーを名指しするなら、その外部キーに「端の型を計画が
/// 変え、同期後に両端が合わない組」がある。DB に無い外部キー（選んだ追加・作り直しの名指し）なら、その項目に「DB が受け入れた組でなく、
/// 同期後に両端が合わない組」がある。予告の警告があれば合格にする上の判定は、警告そのものが正しいかを見ないので、偽の警告が
/// 「予言された失敗」として通っていた（P5-2 の試運転の L4・2 回目の V1）。
/// </para>
/// <para>
/// 状態の初期値は渡された DB 側の図（実 DB なら取込の結果、能力の網なら生成器が作った図）。名前の無い制約は DDL と同じ実効名
/// （<see cref="ConstraintNames"/>・<see cref="UniqueConstraintNaming"/>）で置く。再構築の方言（SQLite）は計画が文の並びを持たないので、
/// 何も判定しない。
/// </para>
/// </remarks>
internal static class SyncPlanStaticRules
{
    /// <summary>
    /// 失敗を予告しない種類の警告（項目を計画から外した・再構築の属性の喪失・列の削除に伴う一意制約の削除・説明の文字の置き換え・
    /// 列の残らない表の列の削除を外した・列の無い表の追加を外した・DB のビューを読めなくする変更を外した・DB のトリガーを発火できなくする変更を外した）
    /// </summary>
    public static readonly IReadOnlySet<SyncPlanWarningKind> NonPredictingWarnings =
        new HashSet<SyncPlanWarningKind>
        {
            SyncPlanWarningKind.SelectedItemRequiresUnselectedItem,
            SyncPlanWarningKind.TableRebuildDropsColumnAttribute,
            SyncPlanWarningKind.UniqueConstraintDroppedWithColumn,
            SyncPlanWarningKind.UniqueConstraintReplacedByPrimaryKey,
            SyncPlanWarningKind.DescriptionCharactersReplaced,
            SyncPlanWarningKind.TableLeftWithoutColumns,
            SyncPlanWarningKind.TableWithoutColumns,
            SyncPlanWarningKind.TableRebuildLosesImportDiscardedContent,
            SyncPlanWarningKind.UniqueConstraintRebuildKeptForForeignKey,
            SyncPlanWarningKind.TableChangeBreaksDependentView,
            SyncPlanWarningKind.TableChangeBreaksDependentTrigger,
        };

    /// <summary>計画を規則で判定する</summary>
    /// <param name="dialect">方言名（名前の範囲の規則 R4 に使う）</param>
    /// <param name="liveEntities">DB 側の表</param>
    /// <param name="liveRelationships">DB 側の外部キー</param>
    /// <param name="selected">利用者が選んだ差分項目（計画が足した項目と見分ける）</param>
    /// <param name="plan">計画</param>
    /// <param name="capabilities">方言の能力</param>
    /// <param name="dataFailures">
    /// ケースが宣言した、既存の行（とモデルの外の依存）で断られる文（<see cref="FuzzLiveSchema.DataFailures"/>）。規則 R10 がその文を
    /// 断られる文として予言する（無ければ予言しない）
    /// </param>
    public static SyncPlanStaticVerdict Evaluate(
        string dialect,
        IReadOnlyList<Entity> liveEntities,
        IReadOnlyList<Relationship> liveRelationships,
        IReadOnlyCollection<SchemaDiffItem> selected,
        SyncPlan plan,
        SyncDialectCapabilities capabilities,
        IReadOnlyCollection<FuzzDataFailure>? dataFailures = null
    )
    {
        if (!capabilities.SupportsAlterColumn || !capabilities.SupportsForeignKeyAlter)
        {
            return SyncPlanStaticVerdict.None;
        }

        // W1（警告の正しさ）は、DB が断る文の予言の有無に依らず掛ける
        var warningViolations = SpuriousTypeMismatchWarnings(
                liveEntities,
                liveRelationships,
                selected,
                plan,
                capabilities
            )
            .ToList();
        var steps = plan
            .Sections.SelectMany(section => section.Items.Select(item => new Step(section, item)))
            .ToList();
        var state = new State(dialect, capabilities);
        state.Load(liveEntities, liveRelationships);
        var doomedAt = -1;
        string? reason = null;

        var declaredFailure = false;

        for (var k = 0; k < steps.Count; k++)
        {
            reason = state.Check(steps[k]);

            // R10：ケースが宣言した既存の行で断られる文（計画は値を見ないので、予告の警告を求めない＝表明 1 と同じ）
            if (
                reason is null
                && DeclaredDataFailure(steps[k], dataFailures, capabilities) is { } declared
            )
            {
                reason = declared;
                declaredFailure = true;
            }

            if (reason is not null)
            {
                doomedAt = k;
                break;
            }

            state.Apply(steps[k]);
        }

        // M1・D9 の並び（失敗の予言の有無に依らない、暗黙コミットの方言の並びの規則）
        warningViolations.AddRange(
            MovedFirstNotFailing(
                dialect,
                steps,
                doomedAt,
                liveEntities,
                liveRelationships,
                capabilities
            )
        );

        if (!capabilities.TransactionalDdl)
        {
            warningViolations.AddRange(ReAddsAfterNewAdds(steps, plan, capabilities));
        }

        if (reason is null)
        {
            return warningViolations.Count == 0
                ? SyncPlanStaticVerdict.None
                : new SyncPlanStaticVerdict(null, warningViolations);
        }

        var predicted = $"{steps[doomedAt].Describe()}: {reason}";
        var violations = new List<string>(warningViolations);
        var warned = plan.Warnings.Any(w => !NonPredictingWarnings.Contains(w.Kind));

        if (!warned && !declaredFailure)
        {
            violations.Add($"static rule: {predicted} (no warning predicts the failure)");
        }

        if (!capabilities.TransactionalDdl)
        {
            violations.AddRange(
                DroppedBeforeFailure(
                    steps,
                    doomedAt,
                    selected,
                    plan,
                    capabilities,
                    predicted,
                    declaredFailure
                )
            );
        }

        return new SyncPlanStaticVerdict(predicted, violations);
    }

    /// <summary>W1 の違反の文の頭（実 DB の総当たりも、DB に依らないこの規則の違反だけを結果へ加える）</summary>
    public const string W1Prefix = "static W1:";

    /// <summary>M1 の違反の文の頭</summary>
    public const string M1Prefix = "static M1:";

    /// <summary>D9 の並び（付け直しの後ろへ回す新しい外部キーの追加）の違反の文の頭</summary>
    public const string D9OrderPrefix = "static D9 (order):";

    /// <summary>
    /// M1：前寄せしたセクション（<see cref="SyncPlanSection.IsMovedFirst"/>）のどれかの文が、計画の順のその位置で断られると予言される（前寄せの後ろへ進まない）。
    /// </summary>
    /// <remarks>
    /// 前寄せは「残す外部キーに阻まれて必ず断られる文」を、何も外さないうちに失敗させるために先頭へ移す。移した文が先頭の位置では通る
    /// （両端を別々の型へ変える外部キーの、互換の範囲で広げる子の列の変更など）と、断られる文は元の位置に残り、その前で外した制約が
    /// 暗黙コミットで消えたまま残る。規則が判定できる文（キーの削除＝R1・R3、型を変える列の変更＝R2・R7、
    /// <see cref="SyncDialectCapabilities.SetNullRequiresNullableChildColumn"/> の方言の NOT NULL 化＝R11。前寄せは暗黙コミットの方言だけなので
    /// この宣言の方言では MySQL だけ）を最後に持つ前寄せだけを見る。
    /// </remarks>
    private static IEnumerable<string> MovedFirstNotFailing(
        string dialect,
        List<Step> steps,
        int doomedAt,
        IReadOnlyList<Entity> liveEntities,
        IReadOnlyList<Relationship> liveRelationships,
        SyncDialectCapabilities capabilities
    )
    {
        var movedCount = steps.TakeWhile(s => s.Section.IsMovedFirst).Count();

        if (movedCount == 0)
        {
            yield break;
        }

        var last = steps[movedCount - 1];

        // 製品の宣言が偽で DB が受け入れる組（AcceptedTypeMismatchMove）のための前寄せは、宣言どおりの振る舞い（docs の割り切り）なので数えない
        if (AcceptedTypeMismatchMove(dialect, last, liveEntities, liveRelationships, capabilities))
        {
            yield break;
        }
        var judged =
            last.Kind == SchemaDiffKind.DropUniqueConstraint
            || last.Kind == SchemaDiffKind.AlterPrimaryKey
                && last.Section.PrimaryKeyPhase == PrimaryKeyPhase.Drop
            || last.Kind == SchemaDiffKind.AlterColumn
                && last.Item.Column is { } column
                && liveEntities
                    .FirstOrDefault(e => Lower(e.TableName) == last.Table)
                    ?.Columns.FirstOrDefault(c => Lower(c.Name) == Lower(column.Name))
                    is { } liveColumn
                && (
                    !capabilities.AreSameType(liveColumn.DataType, column.DataType)
                    || capabilities.SetNullRequiresNullableChildColumn
                        && ColumnNullability.AllowsNull(liveColumn)
                        && !ColumnNullability.AllowsNull(column)
                );

        if (judged && (doomedAt < 0 || doomedAt >= movedCount))
        {
            yield return doomedAt < 0
                ? $"{M1Prefix} {last.Describe()} is moved first but is not predicted to fail"
                : $"{M1Prefix} {last.Describe()} is moved first but {steps[doomedAt].Describe()} is the statement predicted to fail";
        }
    }

    /// <summary>
    /// 前寄せした列の型の変更が、製品の宣言（<see cref="SyncDialectCapabilities.AreForeignKeyCompatibleTypes"/>）では残す外部キーの相手の端の
    /// live の型と並べられないが、DB はどの組も受け入れる（<see cref="SyncPlanFuzzDialect.RefusesForeignKeyTypes"/> が偽）変更か
    /// </summary>
    /// <remarks>
    /// 製品は宣言が偽の組を必ず断られるとみなして先頭へ移す（宣言は保守側）。DB が受け入れるのでその文は通り、M1 の「前寄せの文が断られると
    /// 予言される」は成り立たないが、前寄せは宣言どおりの振る舞い。宣言が真の組だけを持つ前寄せ（両端を別々の型へ変える組の、互換の範囲で
    /// 広げる側の変更など）は今までどおり M1 が見る。
    /// </remarks>
    private static bool AcceptedTypeMismatchMove(
        string dialect,
        Step step,
        IReadOnlyList<Entity> liveEntities,
        IReadOnlyList<Relationship> liveRelationships,
        SyncDialectCapabilities capabilities
    )
    {
        if (
            step.Kind != SchemaDiffKind.AlterColumn
            || step.Item.Column is not { } column
            || liveEntities.FirstOrDefault(e => Lower(e.TableName) == step.Table) is not { } table
            || table.Columns.FirstOrDefault(c => Lower(c.Name) == Lower(column.Name))
                is not { } liveColumn
        )
        {
            return false;
        }

        // 変える列を端に持つ live の外部キーの組の（子の型, 親の型）。変える端は新しい型
        var pairs = new List<(string Child, string Parent)>();

        foreach (var relationship in liveRelationships)
        {
            var parent = liveEntities.FirstOrDefault(e => e.Id == relationship.SourceEntityId);
            var child = liveEntities.FirstOrDefault(e => e.Id == relationship.TargetEntityId);

            foreach (var pair in relationship.ColumnPairs)
            {
                var parentColumn = parent?.Columns.FirstOrDefault(c => c.Id == pair.SourceColumnId);
                var childColumn = child?.Columns.FirstOrDefault(c => c.Id == pair.TargetColumnId);

                if (parentColumn is null || childColumn is null)
                {
                    continue;
                }

                if (childColumn == liveColumn || parentColumn == liveColumn)
                {
                    pairs.Add(
                        (
                            childColumn == liveColumn ? column.DataType : childColumn.DataType,
                            parentColumn == liveColumn ? column.DataType : parentColumn.DataType
                        )
                    );
                }
            }
        }

        return pairs.Any(p => !capabilities.AreForeignKeyCompatibleTypes(p.Child, p.Parent))
            && !pairs.Any(p =>
                SyncPlanFuzzDialect.RefusesForeignKeyTypes(dialect, capabilities, p.Child, p.Parent)
            );
    }

    /// <summary>
    /// D9 の並び：DDL を暗黙コミットする方言で、同じ計画の前の方で外した外部キーの付け直しが、新しい外部キー（DB に無い外部キー）の
    /// 追加より後ろにある。
    /// </summary>
    /// <remarks>
    /// 計画は列の値を見ないので、新しい外部キーの追加は既存の行（親の無い値）で断られうる。その後ろに置いた付け直しは走らず、外した
    /// 外部キーが消えたまま残る。付け直しはもともと DB にあった制約なので、データでは断られない。候補キーの警告を出した外部キー
    /// （付け直しが断られうるので最後へ回す）は除く。
    /// </remarks>
    private static IEnumerable<string> ReAddsAfterNewAdds(
        List<Step> steps,
        SyncPlan plan,
        SyncDialectCapabilities capabilities
    )
    {
        var limit = capabilities.IdentifierLengthLimit;
        var warnedForeignKeys = plan
            .Warnings.Where(w =>
                w.Kind
                    is SyncPlanWarningKind.ForeignKeyRebuildMayLoseCandidateKey
                        or SyncPlanWarningKind.ForeignKeyNotOnCandidateKey
            )
            .Select(w => $"{Lower(w.TableName)}|{Lower(w.Detail)}")
            .ToHashSet(StringComparer.Ordinal);
        var dropped = new HashSet<string>(StringComparer.Ordinal);
        string? newAdd = null;

        foreach (var step in steps)
        {
            if (
                step.Kind == SchemaDiffKind.DropForeignKey
                && step.DropIdentity(capabilities) is { } d
            )
            {
                dropped.Add(d);
                continue;
            }

            if (
                step.Kind != SchemaDiffKind.AddForeignKey
                || step.AddIdentity(capabilities) is not { } identity
            )
            {
                continue;
            }

            if (!dropped.Contains(identity))
            {
                newAdd ??= identity;
                continue;
            }

            if (
                newAdd is not null
                && !warnedForeignKeys.Contains(
                    $"{Lower(step.Item.ChildEntity!.TableName)}|{Lower(step.ForeignKeyName(limit))}"
                )
            )
            {
                yield return $"{D9OrderPrefix} {identity} is re-added after the new foreign key {newAdd}, whose add can fail on existing rows";
            }
        }
    }

    /// <summary>
    /// W1：<see cref="SyncPlanWarningKind.ForeignKeyColumnTypeMismatch"/> が DB にある外部キーを名指しするなら、その外部キーに
    /// 「端の型を計画が変え（<see cref="SyncDialectCapabilities.AreSameType"/> で比べる）、同期後にその組の両端が
    /// <see cref="SyncDialectCapabilities.AreForeignKeyCompatibleTypes"/> で合わない」組がある
    /// </summary>
    /// <remarks>
    /// DB は今その外部キーを両端の型のまま受け入れているので、計画が型を変えない組は、外して付け直しても同じ型で通る。型の決まらない
    /// 組の違い（DB が受け入れている型の違い）を数えて「同期後に両端の型が食い違う（付け直せない）」と名指しするのは偽の警告で、
    /// 計画は外部キーを外し直さずに残し、その外部キーに阻まれる文（主キーの解除など）で失敗する。組単位で見るのは、複合外部キーの
    /// ほかの組の型を変えたときに、DB が受け入れている組まで数えた偽の警告が予言された失敗として通っていたため（P5-2 の試運転の
    /// 2 回目の V1）。型を変えた組の互換は製品と同じ方言の宣言で判定する（宣言の値は実 DB の形の行が確かめる）。
    /// 警告が選んだ外部キーの追加・作り直し（スクリプトが付ける名前）を名指しするなら、その項目の組で判定する：「DB が受け入れた組」
    /// （両端とも計画が型を変えず、DB 側の図の外部キーが同じ組を持つ）でなく、同期後に両端が合わない組がある。DB にまだ無い組は
    /// 型を変えない列どうしでも DB が受け入れたことが無いので数える。作り直しは DB にある外部キーと同じ名前を持つので、項目を先に見る
    /// （列ペアを変える作り直しの新しい組は、DB にある外部キーの組では判定できない）。
    /// </remarks>
    private static IEnumerable<string> SpuriousTypeMismatchWarnings(
        IReadOnlyList<Entity> liveEntities,
        IReadOnlyList<Relationship> liveRelationships,
        IReadOnlyCollection<SchemaDiffItem> selected,
        SyncPlan plan,
        SyncDialectCapabilities capabilities
    )
    {
        var warnings = plan
            .Warnings.Where(w => w.Kind == SyncPlanWarningKind.ForeignKeyColumnTypeMismatch)
            .ToList();

        if (warnings.Count == 0)
        {
            yield break;
        }

        var names = ConstraintNames.ResolveForeignKeyNames(
            liveEntities,
            liveRelationships,
            capabilities.IdentifierLengthLimit
        );

        // 計画が書く列の型（列の変更の項目の新しい型）
        var alteredTypes = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (
            var item in plan
                .Sections.Where(s => s.Kind == SchemaDiffKind.AlterColumn)
                .SelectMany(s => s.Items)
                .Where(i => i.ColumnName is not null && i.Column is not null)
        )
        {
            alteredTypes[ColumnKey(Lower(item.TableName), item.ColumnName!)] =
                item.Column!.DataType;
        }

        // DB 側の図の外部キーが持つ組（子の列・親の列）
        var livePairs = new HashSet<(string Child, string Parent)>();

        foreach (var relationship in liveRelationships)
        {
            var parent = liveEntities.FirstOrDefault(e => e.Id == relationship.SourceEntityId);
            var child = liveEntities.FirstOrDefault(e => e.Id == relationship.TargetEntityId);

            if (
                parent is not null
                && child is not null
                && ForeignKeyColumnPairResolver.Resolve(relationship, parent, child) is { } pairs
            )
            {
                foreach (var pair in pairs)
                {
                    livePairs.Add(
                        (
                            ColumnKey(Lower(child.TableName), pair.ChildColumn),
                            ColumnKey(Lower(parent.TableName), pair.ParentColumn)
                        )
                    );
                }
            }
        }

        foreach (var warning in warnings)
        {
            // 選んだ外部キーの追加・作り直しの名指し（スクリプトが付ける名前。計画が型の食い違いで外した項目＝ExcludedItems も見る：
            // 外した項目は呼び出し側の選んだ項目から除かれているが、警告はその項目の新しい組を名指しする＝親の表を変える作り直しは、DB に
            // ある同じ名前の外部キーの組では判定できない）
            var named = selected
                .Concat(plan.ExcludedItems)
                .Where(i =>
                    i.Kind is SchemaDiffKind.AddForeignKey or SchemaDiffKind.AlterForeignKey
                    && i.ChildEntity is not null
                    && i.ParentEntity is not null
                    && Lower(i.ChildEntity.TableName) == Lower(warning.TableName)
                    && Lower(
                        SyncScriptBuilderHelper.ForeignKeyNameToAdd(
                            i,
                            capabilities.IdentifierLengthLimit
                        )
                    ) == Lower(warning.Detail)
                )
                .ToList();

            if (named.Count > 0)
            {
                if (!named.Any(HasUnacceptedMismatch))
                {
                    yield return $"{W1Prefix} {warning.Kind}({warning.TableName}: {warning.Detail}) names a selected foreign key without a column pair that the database has not accepted and whose ends then do not match";
                }

                continue;
            }

            foreach (var relationship in liveRelationships)
            {
                var parent = liveEntities.FirstOrDefault(e => e.Id == relationship.SourceEntityId);
                var child = liveEntities.FirstOrDefault(e => e.Id == relationship.TargetEntityId);

                if (
                    parent is null
                    || child is null
                    || Lower(child.TableName) != Lower(warning.TableName)
                    || Lower(names[relationship]) != Lower(warning.Detail)
                    || ForeignKeyColumnPairResolver.Resolve(relationship, parent, child)
                        is not { } pairs
                )
                {
                    continue;
                }

                if (
                    !pairs.Any(p =>
                        (TypeChanges(child, p.ChildColumn) || TypeChanges(parent, p.ParentColumn))
                        && !capabilities.AreForeignKeyCompatibleTypes(
                            PostSyncType(child, p.ChildColumn),
                            PostSyncType(parent, p.ParentColumn)
                        )
                    )
                )
                {
                    yield return $"{W1Prefix} {warning.Kind}({warning.TableName}: {warning.Detail}) names a foreign key without a column pair whose type the plan changes and whose ends then do not match";
                }
            }
        }

        bool TypeChanges(Entity entity, string column) =>
            alteredTypes.TryGetValue(ColumnKey(Lower(entity.TableName), column), out var altered)
            && entity.Columns.FirstOrDefault(c => Lower(c.Name) == Lower(column)) is { } live
            && !capabilities.AreSameType(live.DataType, altered);

        // 組の端の同期後の型（計画が書く型か、live の型）
        string PostSyncType(Entity entity, string column) =>
            alteredTypes.TryGetValue(ColumnKey(Lower(entity.TableName), column), out var altered)
                ? altered
                : entity.Columns.FirstOrDefault(c => Lower(c.Name) == Lower(column))?.DataType
                    ?? string.Empty;

        // 選んだ外部キーの項目に、DB が受け入れた組でなく同期後に両端が合わない組がある
        bool HasUnacceptedMismatch(SchemaDiffItem item) =>
            item.ForeignKeyColumnPairs.Any(pair =>
            {
                var (childType, childDecided) = EndType(item.ChildEntity!, pair.ChildColumn);
                var (parentType, parentDecided) = EndType(item.ParentEntity!, pair.ParentColumn);
                var accepted =
                    !childDecided
                    && !parentDecided
                    && livePairs.Contains(
                        (
                            ColumnKey(Lower(item.ChildEntity!.TableName), pair.ChildColumn),
                            ColumnKey(Lower(item.ParentEntity!.TableName), pair.ParentColumn)
                        )
                    );

                return !accepted
                    && !capabilities.AreForeignKeyCompatibleTypes(childType, parentType);
            });

        // 項目の端の同期後の型と、計画がその型を決めるか（計画が書く型・live の型・live に無い列は図の型＝決める）
        (string Type, bool Decided) EndType(Entity diagramEntity, string column)
        {
            var live = liveEntities
                .FirstOrDefault(e => Lower(e.TableName) == Lower(diagramEntity.TableName))
                ?.Columns.FirstOrDefault(c => Lower(c.Name) == Lower(column));
            var key = ColumnKey(Lower(diagramEntity.TableName), column);

            if (alteredTypes.TryGetValue(key, out var altered))
            {
                return (altered, live is null || !capabilities.AreSameType(live.DataType, altered));
            }

            return live is not null
                ? (live.DataType, false)
                : (
                    diagramEntity
                        .Columns.FirstOrDefault(c => Lower(c.Name) == Lower(column))
                        ?.DataType
                        ?? string.Empty,
                    true
                );
        }
    }

    /// <summary>
    /// D9：断られる文より前に外し、その後ろで付け直す制約（作り直し・主キーの変更・暗黙の外し直し）と、断られる文そのものが付け直す
    /// 外部キー（前の方で外したもの）と、断られる文より後ろの表・列の削除のために先に外した制約
    /// </summary>
    private static IEnumerable<string> DroppedBeforeFailure(
        List<Step> steps,
        int doomedAt,
        IReadOnlyCollection<SchemaDiffItem> selected,
        SyncPlan plan,
        SyncDialectCapabilities capabilities,
        string predicted,
        bool dataFailure
    )
    {
        var limit = capabilities.IdentifierLengthLimit;
        var selectedSet = new HashSet<SchemaDiffItem>(selected, ReferenceEqualityComparer.Instance);
        var readdedAfter = steps
            .Skip(doomedAt + 1)
            .Select(s => s.AddIdentity(capabilities))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        var removalsAfter = steps.Skip(doomedAt + 1).Where(s => s.IsRemoval).ToList();

        // 付け直しが候補キーを証明できないと事前に警告した外部キー自身は、消えても違反にしない（製品の契約＝警告して実行する。
        // 実 DB の表明 1b の例外と同じ。巻き添えで消える別の制約は数える）
        var warnedForeignKeys = plan
            .Warnings.Where(w =>
                w.Kind
                    is SyncPlanWarningKind.ForeignKeyRebuildMayLoseCandidateKey
                        or SyncPlanWarningKind.ForeignKeyNotOnCandidateKey
            )
            .Select(w => $"{Lower(w.TableName)}|{Lower(w.Detail)}")
            .ToHashSet(StringComparer.Ordinal);

        // 既存の行で断られうる文・列が空でないと通らない変更の前後で外して付け直すと計画が名指しした制約（製品の契約＝名指しして
        // 部分適用を受け入れる。実 DB の表明 1b の例外と同じ）。警告は表と名前だけを持つので、外部キーと一意制約の両方の身元を作る。
        // 受け入れるのは、予言した失敗がデータで断られる文（R10）のときだけ。構造の規則（R1〜R9・R11・R12）が予言した失敗はデータに依らず
        // 必ず断られるので、計画はその文を先頭へ移すか外し直さないで済ませるべきで、名指しでは足りない
        var namedAroundRefusable = plan
            .Warnings.Where(w =>
                dataFailure
                && w.Kind
                    is SyncPlanWarningKind.ConstraintRebuildAroundEmptyColumnChange
                        or SyncPlanWarningKind.ConstraintRebuildAroundRowDependentChange
            )
            .SelectMany(w =>
            {
                var open = w.Detail.IndexOf(" (", StringComparison.Ordinal);
                var name = Lower(open < 0 ? w.Detail : w.Detail[..open]);
                var table = Lower(w.TableName);
                return name == "primary key"
                    ? new[] { $"PK {table}" }
                    : [$"FK {table}.{name}", $"UQ {table}.{name}"];
            })
            .ToHashSet(StringComparer.Ordinal);

        // 断られる文が、同じ計画の前の方で外した制約の付け直しそのもの（外部キー・作り直す一意制約・主キーの変更の付与）なら、
        // その制約は消えたまま残る（付け直しが候補キーを証明できないと警告した外部キーと、断られうる文として名指しした制約を除く）。
        // この形を数えないと、参照先の唯一の一意制約を同じ計画で外しながら列の変更で外部キーを付け直す同期（最後の付け直しが断られて
        // 外部キーが消える）が、予告の警告があるので予言された失敗として通る
        var failing = steps[doomedAt];

        if (
            failing.AddIdentity(capabilities) is { } failingIdentity
            && steps.Take(doomedAt).Any(s => s.DropIdentity(capabilities) == failingIdentity)
            && !namedAroundRefusable.Contains(failingIdentity)
            && !(
                failing.Kind == SchemaDiffKind.AddForeignKey
                && warnedForeignKeys.Contains(
                    $"{Lower(failing.Item.ChildEntity!.TableName)}|{Lower(failing.ForeignKeyName(limit))}"
                )
            )
        )
        {
            yield return $"static D9: {failingIdentity} is dropped before the failing statement, which is its own re-add ({predicted})";
        }

        foreach (var step in steps.Take(doomedAt))
        {
            if (step.DropIdentity(capabilities) is not { } identity)
            {
                continue;
            }

            if (namedAroundRefusable.Contains(identity))
            {
                continue;
            }

            if (
                step.Kind == SchemaDiffKind.DropForeignKey
                && warnedForeignKeys.Contains(
                    $"{Lower(step.Item.ChildEntity!.TableName)}|{Lower(step.ForeignKeyName(limit))}"
                )
            )
            {
                continue;
            }

            if (readdedAfter.Contains(identity))
            {
                yield return $"static D9: {identity} is dropped before the failing statement and re-added after it ({predicted})";
                continue;
            }

            // 利用者が選んだ削除は、付け直さないので失敗の前に走ってもよい
            if (selectedSet.Contains(step.Item))
            {
                continue;
            }

            if (removalsAfter.FirstOrDefault(r => step.IsJustifiedBy(r)) is { } removal)
            {
                yield return $"static D9 (rule 4): {identity} is dropped for {removal.Describe()}, which runs after the failing statement ({predicted})";
            }
        }
    }

    /// <summary>
    /// R10：計画の文が、ケースが宣言した既存の行（とモデルの外の依存）で断られる文（<see cref="FuzzLiveSchema.DataFailures"/>）なら、その理由
    /// </summary>
    /// <remarks>
    /// 照合は宣言の種類ごとに、外部キーの追加は子の表と実効名、列の変更は表と列（計算列の参照する列の変更は
    /// <see cref="SyncDialectCapabilities.AlterColumnBlockedByComputedColumn"/> の方言だけ）、一意制約の追加は表と実効名、主キーの付与は表、
    /// 列の追加は表と列（<see cref="SyncDialectCapabilities.AddNotNullColumnRequiresEmptyTable"/> の方言だけ）で取る（大文字小文字を無視）。
    /// 宣言した方言の事実で断られない文（暗黙の既定値で埋める MySQL の NOT NULL の列の追加など）は予言しない。
    /// </remarks>
    private static string? DeclaredDataFailure(
        Step step,
        IReadOnlyCollection<FuzzDataFailure>? declared,
        SyncDialectCapabilities capabilities
    )
    {
        if (declared is null || declared.Count == 0)
        {
            return null;
        }

        var limit = capabilities.IdentifierLengthLimit;
        var table =
            step.Kind == SchemaDiffKind.AddForeignKey && step.Item.ChildEntity is { } child
                ? Lower(child.TableName)
                : step.Table;
        var match = declared.FirstOrDefault(d =>
            Lower(d.Table) == table
            && d.Kind switch
            {
                FuzzDataFailureKind.AddForeignKey => step.Kind == SchemaDiffKind.AddForeignKey
                    && Lower(step.ForeignKeyName(limit)) == Lower(d.Name),
                FuzzDataFailureKind.AlterColumn => step.Kind == SchemaDiffKind.AlterColumn
                    && Lower(step.Item.ColumnName ?? "") == Lower(d.Name),
                FuzzDataFailureKind.AlterComputedSource =>
                    capabilities.AlterColumnBlockedByComputedColumn
                        && step.Kind == SchemaDiffKind.AlterColumn
                        && Lower(step.Item.ColumnName ?? "") == Lower(d.Name),
                FuzzDataFailureKind.AddUniqueConstraint => step.Kind
                    == SchemaDiffKind.AddUniqueConstraint
                    && Lower(
                        UniqueConstraintNaming.Resolve(
                            step.Item.UniqueConstraintName,
                            step.Item.TableName,
                            step.Item.UniqueConstraintColumns,
                            limit
                        )
                    ) == Lower(d.Name),
                FuzzDataFailureKind.AddPrimaryKey => step.Kind == SchemaDiffKind.AlterPrimaryKey
                    && step.Section.PrimaryKeyPhase == PrimaryKeyPhase.Add,
                FuzzDataFailureKind.AddColumn => capabilities.AddNotNullColumnRequiresEmptyTable
                    && step.Kind == SchemaDiffKind.AddColumn
                    && Lower(step.Item.ColumnName ?? "") == Lower(d.Name),
                _ => false,
            }
        );

        return match is null ? null : $"R10: declared {match.Kind} refused by existing rows";
    }

    /// <summary>計画の 1 項目（セクションと項目）</summary>
    private sealed record Step(SyncPlanSection Section, SchemaDiffItem Item)
    {
        public SchemaDiffKind Kind => Section.Kind;

        public string Table => Lower(Item.TableName);

        public string Describe() =>
            $"{Kind}{(Section.PrimaryKeyPhase == PrimaryKeyPhase.None ? "" : $":{Section.PrimaryKeyPhase}")} {Item.TableName}"
            + (Item.ColumnName is { } column ? $".{column}" : "")
            + (Item.ForeignKeyName is { } name ? $" ({name})" : "")
            + (Item.UniqueConstraintName is { } unique ? $" ({unique})" : "");

        /// <summary>表・列の削除か</summary>
        public bool IsRemoval => Kind is SchemaDiffKind.DropTable or SchemaDiffKind.DropColumn;

        /// <summary>外す制約の身元（外さない項目は null）</summary>
        public string? DropIdentity(SyncDialectCapabilities capabilities) =>
            Kind switch
            {
                SchemaDiffKind.DropForeignKey => ForeignKeyIdentity(capabilities),
                SchemaDiffKind.DropUniqueConstraint => UniqueIdentity(capabilities),
                SchemaDiffKind.AlterPrimaryKey
                    when Section.PrimaryKeyPhase == PrimaryKeyPhase.Drop => $"PK {Table}",
                _ => null,
            };

        /// <summary>足す制約の身元（足さない項目は null）</summary>
        public string? AddIdentity(SyncDialectCapabilities capabilities) =>
            Kind switch
            {
                SchemaDiffKind.AddForeignKey => ForeignKeyIdentity(capabilities),
                SchemaDiffKind.AddUniqueConstraint => UniqueIdentity(capabilities),
                SchemaDiffKind.AlterPrimaryKey
                    when Section.PrimaryKeyPhase == PrimaryKeyPhase.Add => $"PK {Table}",
                _ => null,
            };

        /// <summary>外した制約が、表・列の削除 <paramref name="removal"/> のためのものか</summary>
        public bool IsJustifiedBy(Step removal)
        {
            var columns = Kind switch
            {
                SchemaDiffKind.DropForeignKey => Item
                    .ForeignKeyColumnPairs.Select(p =>
                        $"{Lower(Item.ChildEntity!.TableName)}|{Lower(p.ChildColumn)}"
                    )
                    .Concat(
                        Item.ForeignKeyColumnPairs.Select(p =>
                            $"{Lower(Item.ParentEntity!.TableName)}|{Lower(p.ParentColumn)}"
                        )
                    ),
                SchemaDiffKind.DropUniqueConstraint => Item.UniqueConstraintColumns.Select(c =>
                    $"{Table}|{Lower(c)}"
                ),
                _ => [],
            };
            var tables =
                Kind == SchemaDiffKind.DropForeignKey
                    ? new[]
                    {
                        Lower(Item.ChildEntity!.TableName),
                        Lower(Item.ParentEntity!.TableName),
                    }
                    : [Table];

            return removal.Kind == SchemaDiffKind.DropTable
                ? tables.Contains(removal.Table)
                : columns.Contains($"{removal.Table}|{Lower(removal.Item.ColumnName!)}");
        }

        /// <summary>外す・足す外部キーの実効名（DDL・同期スクリプトが使う名前）</summary>
        public string ForeignKeyName(IdentifierLengthLimit limit) =>
            SyncScriptBuilderHelper.ForeignKeyNameToAdd(Item, limit);

        /// <summary>
        /// 外部キーの身元（名前を保存する方言は子の表と実効名＝作り直しで列や親が変わっても同じ外部キー。保存しない方言は子の列と親）
        /// </summary>
        private string ForeignKeyIdentity(SyncDialectCapabilities capabilities) =>
            capabilities.PersistsForeignKeyConstraintNames
                ? $"FK {Lower(Item.ChildEntity!.TableName)}.{Lower(ForeignKeyName(capabilities.IdentifierLengthLimit))}"
                : $"FK {Lower(Item.ChildEntity!.TableName)}({string.Join(",", Item.ForeignKeyColumnPairs.Select(p => Lower(p.ChildColumn)))})"
                    + $" -> {Lower(Item.ParentEntity!.TableName)}";

        /// <summary>一意制約の身元（表と実効名＝作り直しで構成列が変わっても同じ一意制約）</summary>
        private string UniqueIdentity(SyncDialectCapabilities capabilities) =>
            $"UQ {Table}.{Lower(UniqueConstraintNaming.Resolve(Item.UniqueConstraintName, Item.TableName, Item.UniqueConstraintColumns, capabilities.IdentifierLengthLimit))}";
    }

    /// <summary>外部キーの状態（<paramref name="SetNull"/>＝方言が書く参照アクションの ON DELETE か ON UPDATE が SET NULL）</summary>
    private sealed record ForeignKey(
        string Child,
        IReadOnlyList<string> ChildColumns,
        string Parent,
        IReadOnlyList<string> ParentColumns,
        string Name,
        bool SetNull
    );

    /// <summary>主キー・一意制約の状態（索引になる）</summary>
    private sealed record Key(
        string Table,
        IReadOnlyList<string> Columns,
        string Name,
        bool Primary
    );

    /// <summary>DB 側の制約と列の型の状態</summary>
    private sealed class State(string dialect, SyncDialectCapabilities capabilities)
    {
        private readonly List<ForeignKey> _foreignKeys = [];
        private readonly List<Key> _keys = [];

        /// <summary>外部キーが作った索引（MySQL・子の表と列）</summary>
        private readonly List<(string Table, IReadOnlyList<string> Columns)> _foreignKeyIndexes =
        [];
        private readonly Dictionary<string, string> _types = new(StringComparer.Ordinal);

        /// <summary>今 NULL を許す列（R11）</summary>
        private readonly HashSet<string> _nullable = new(StringComparer.Ordinal);

        /// <summary>
        /// DB 側の図の外部キーが持っていた組と、その時の両端の型（子の列・親の列・子の型・親の型。R8 で DB が受け入れた組を見分ける）
        /// </summary>
        private readonly HashSet<(
            string Child,
            string Parent,
            string ChildType,
            string ParentType
        )> _acceptedPairs = [];

        private IdentifierLengthLimit Limit => capabilities.IdentifierLengthLimit;

        private bool IsMySql => dialect == DialectNames.MySql;

        public void Load(IReadOnlyList<Entity> entities, IReadOnlyList<Relationship> relationships)
        {
            var uniqueNames = ConstraintNames.ResolveUniqueConstraintNames(
                entities,
                relationships,
                Limit
            );

            foreach (var entity in entities)
            {
                AddTable(entity, uniqueNames);
            }

            var names = ConstraintNames.ResolveForeignKeyNames(entities, relationships, Limit);

            foreach (var relationship in relationships)
            {
                var parent = entities.FirstOrDefault(e => e.Id == relationship.SourceEntityId);
                var child = entities.FirstOrDefault(e => e.Id == relationship.TargetEntityId);

                if (
                    parent is null
                    || child is null
                    || ForeignKeyColumnPairResolver.Resolve(relationship, parent, child)
                        is not { } pairs
                )
                {
                    continue;
                }

                AddForeignKey(
                    new ForeignKey(
                        Lower(child.TableName),
                        [.. pairs.Select(p => Lower(p.ChildColumn))],
                        Lower(parent.TableName),
                        [.. pairs.Select(p => Lower(p.ParentColumn))],
                        names[relationship],
                        UsesSetNull(relationship)
                    )
                );

                foreach (var pair in pairs)
                {
                    var childKey = ColumnKey(Lower(child.TableName), pair.ChildColumn);
                    var parentKey = ColumnKey(Lower(parent.TableName), pair.ParentColumn);
                    _acceptedPairs.Add((childKey, parentKey, TypeOf(childKey), TypeOf(parentKey)));
                }
            }
        }

        /// <summary>列の今の型（無ければ空）</summary>
        private string TypeOf(string key) =>
            _types.TryGetValue(key, out var type) ? type : string.Empty;

        /// <summary>列の今の NULL 許容を記録する（<see cref="ColumnNullability.AllowsNull"/>）</summary>
        private void SetNullable(string key, Column column)
        {
            if (ColumnNullability.AllowsNull(column))
            {
                _nullable.Add(key);
            }
            else
            {
                _nullable.Remove(key);
            }
        }

        /// <summary>方言が書く参照アクション（ON DELETE か ON UPDATE）が SET NULL か（製品の SyncPlanner と同じ読み方）</summary>
        private bool UsesSetNull(Relationship relationship) =>
            capabilities.EffectiveOnDelete(relationship.OnDelete)
                == ForeignKeyReferentialAction.SetNull
            || capabilities.EffectiveOnUpdate(relationship.OnUpdate)
                == ForeignKeyReferentialAction.SetNull;

        /// <summary>項目を当てる直前の状態で、DB がその文を断るか（断るなら理由・断らないなら null）</summary>
        public string? Check(Step step) =>
            step.Kind switch
            {
                SchemaDiffKind.DropUniqueConstraint => CheckKeyDrop(
                    FindUnique(
                        step.Table,
                        step.Item.UniqueConstraintName,
                        step.Item.UniqueConstraintColumns
                    )
                ),
                SchemaDiffKind.DropTable => CheckTableDrop(step.Table),
                SchemaDiffKind.AlterPrimaryKey
                    when step.Section.PrimaryKeyPhase == PrimaryKeyPhase.Drop => CheckKeyDrop(
                    _keys.FirstOrDefault(k => k.Primary && k.Table == step.Table)
                ),
                SchemaDiffKind.AlterColumn => CheckAlterColumn(step),
                SchemaDiffKind.AddUniqueConstraint => CheckName(
                    step.Table,
                    UniqueName(step),
                    isForeignKey: false
                ) ?? CheckSameColumnSetKey(step),
                SchemaDiffKind.AlterPrimaryKey
                    when step.Section.PrimaryKeyPhase == PrimaryKeyPhase.Add => IsMySql
                    ? null
                    : CheckName(step.Table, PrimaryKeyName(step.Item.TableName), false),
                SchemaDiffKind.AddForeignKey => CheckForeignKeyAdd(step),
                SchemaDiffKind.AddTable => CheckTableAdd(step),
                SchemaDiffKind.DropColumn => CheckColumnDrop(step),
                _ => null,
            };

        public void Apply(Step step)
        {
            var item = step.Item;

            switch (step.Kind)
            {
                case SchemaDiffKind.DropForeignKey:
                    RemoveForeignKey(step);
                    break;
                case SchemaDiffKind.DropUniqueConstraint:
                    if (
                        FindUnique(
                            step.Table,
                            item.UniqueConstraintName,
                            item.UniqueConstraintColumns
                        ) is
                        { } unique
                    )
                    {
                        _keys.Remove(unique);
                    }

                    break;
                case SchemaDiffKind.DropTable:
                    _keys.RemoveAll(k => k.Table == step.Table);
                    _foreignKeys.RemoveAll(f => f.Child == step.Table);
                    _foreignKeyIndexes.RemoveAll(i => i.Table == step.Table);
                    break;
                case SchemaDiffKind.AddTable when item.Entity is { } entity:
                    AddTable(entity, item.UniqueConstraintNames);
                    break;
                case SchemaDiffKind.AddColumn when item.Column is { } column:
                    _types[ColumnKey(step.Table, column.Name)] = column.DataType;
                    SetNullable(ColumnKey(step.Table, column.Name), column);
                    break;
                case SchemaDiffKind.AlterPrimaryKey
                    when step.Section.PrimaryKeyPhase == PrimaryKeyPhase.Drop:
                    _keys.RemoveAll(k => k.Primary && k.Table == step.Table);
                    break;
                case SchemaDiffKind.AlterPrimaryKey
                    when step.Section.PrimaryKeyPhase == PrimaryKeyPhase.Add
                        && item.Entity is { } entity:
                    var key = entity.GetPrimaryKeyColumnsInOrder();

                    if (key.Count > 0)
                    {
                        _keys.Add(
                            new Key(
                                step.Table,
                                [.. key.Select(c => Lower(c.Name))],
                                PrimaryKeyName(item.TableName),
                                true
                            )
                        );
                    }

                    break;
                case SchemaDiffKind.AlterColumn when item.Column is { } altered:
                    _types[ColumnKey(step.Table, item.ColumnName!)] = altered.DataType;
                    SetNullable(ColumnKey(step.Table, item.ColumnName!), altered);
                    break;
                case SchemaDiffKind.DropColumn:
                    var dropped = Lower(item.ColumnName!);
                    _keys.RemoveAll(k => k.Table == step.Table && k.Columns.Contains(dropped));
                    _foreignKeys.RemoveAll(f =>
                        (f.Child == step.Table && f.ChildColumns.Contains(dropped))
                        || (f.Parent == step.Table && f.ParentColumns.Contains(dropped))
                    );
                    _types.Remove(ColumnKey(step.Table, dropped));
                    break;
                case SchemaDiffKind.AddUniqueConstraint:
                    _keys.Add(
                        new Key(
                            step.Table,
                            [.. item.UniqueConstraintColumns.Select(Lower)],
                            UniqueName(step),
                            false
                        )
                    );
                    break;
                case SchemaDiffKind.AddForeignKey:
                    AddForeignKey(
                        new ForeignKey(
                            Lower(item.ChildEntity!.TableName),
                            [.. item.ForeignKeyColumnPairs.Select(p => Lower(p.ChildColumn))],
                            Lower(item.ParentEntity!.TableName),
                            [.. item.ForeignKeyColumnPairs.Select(p => Lower(p.ParentColumn))],
                            SyncScriptBuilderHelper.ForeignKeyNameToAdd(item, Limit),
                            item.Relationship is { } added && UsesSetNull(added)
                        )
                    );
                    break;
            }
        }

        // ---- 判定 ----

        /// <summary>
        /// R9：Oracle（同じ列の並びに一意制約を 2 つ張れない＝<see cref="SyncDialectCapabilities.SupportsUniqueConstraintOnPrimaryKey"/> が偽の
        /// 逐次 DDL の方言）で、足す一意制約の列集合が、その時点で残る主キー・一意制約の列集合と同じ（ORA-02261）
        /// </summary>
        private string? CheckSameColumnSetKey(Step step)
        {
            if (capabilities.SupportsUniqueConstraintOnPrimaryKey)
            {
                return null;
            }

            var columns = step
                .Item.UniqueConstraintColumns.Select(Lower)
                .Order(StringComparer.Ordinal)
                .ToList();
            var same = _keys.FirstOrDefault(k =>
                k.Table == step.Table
                && k.Columns.Order(StringComparer.Ordinal).SequenceEqual(columns)
            );

            return same is null
                ? null
                : $"R9: unique constraint on ({string.Join(", ", columns)}) duplicates {(same.Primary ? "the primary key" : same.Name)}";
        }

        /// <summary>R1・R3：主キー・一意制約を消す前に、それに支えられた外部キーが外れている</summary>
        private string? CheckKeyDrop(Key? key)
        {
            if (key is null)
            {
                return null;
            }

            var others = _keys
                .Where(k => !ReferenceEquals(k, key) && k.Table == key.Table)
                .ToList();

            foreach (var fk in _foreignKeys)
            {
                // R1：参照する列の集合がこのキーと一致する外部キー（MySQL は先頭に並ぶ外部キーも、ほかの索引が支えなければ）
                if (fk.Parent == key.Table)
                {
                    var exact = SameSet(fk.ParentColumns, key.Columns);
                    var leading =
                        capabilities.ForeignKeyRequiresLeadingIndex
                        && IsPrefix(fk.ParentColumns, key.Columns)
                        && !others.Any(o => IsPrefix(fk.ParentColumns, o.Columns));

                    if (exact && !others.Any(o => SameSet(o.Columns, key.Columns)) || leading)
                    {
                        return $"R1: {Describe(key)} is still referenced by {fk.Name}";
                    }
                }

                // R3：MySQL は子の列がこの索引の先頭に並ぶ外部キーの索引も、ほかの索引が支えなければ要る
                if (
                    IsMySql
                    && fk.Child == key.Table
                    && IsPrefix(fk.ChildColumns, key.Columns)
                    && !others.Any(o => IsPrefix(fk.ChildColumns, o.Columns))
                    && !_foreignKeyIndexes.Any(i =>
                        i.Table == key.Table && IsPrefix(fk.ChildColumns, i.Columns)
                    )
                )
                {
                    return $"R3: the index of {Describe(key)} is needed by {fk.Name}";
                }
            }

            return null;
        }

        /// <summary>R1：表を消す前に、ほかの表からの外部キーが外れている</summary>
        private string? CheckTableDrop(string table) =>
            _foreignKeys.FirstOrDefault(f => f.Parent == table && f.Child != table) is { } fk
                ? $"R1: table {table} is still referenced by {fk.Name}"
                : null;

        /// <summary>R6：表の最後の列を消さない（SQL Server 4923・MySQL 1090・Oracle ORA-12983。PostgreSQL は列の無い表を許す）</summary>
        private string? CheckColumnDrop(Step step)
        {
            if (
                dialect is not (DialectNames.SqlServer or DialectNames.MySql or DialectNames.Oracle)
            )
            {
                return null;
            }

            var dropped = ColumnKey(step.Table, step.Item.ColumnName!);
            var prefix = step.Table + "|";

            return _types.Keys.Any(k =>
                k.StartsWith(prefix, StringComparison.Ordinal)
                && !string.Equals(k, dropped, StringComparison.Ordinal)
            )
                ? null
                : $"R6: dropping {dropped.Replace('|', '.')} leaves table {step.Table} without columns";
        }

        /// <summary>R2：型の変更の前に、列を含む一意制約・主キー・外部キーが外れている。R7：残す外部キーの組の両端が変更の後も並べられる</summary>
        private string? CheckAlterColumn(Step step)
        {
            var column = Lower(step.Item.ColumnName!);
            var newType = step.Item.Column?.DataType;

            // R11：MySQL で、残す SET NULL の外部キーの子の列を NOT NULL にする（1830。SQL Server は外部キーを作るときだけ検査する）
            if (
                IsMySql
                && capabilities.SetNullRequiresNullableChildColumn
                && step.Item.Column is { } target
                && !ColumnNullability.AllowsNull(target)
                && _nullable.Contains(ColumnKey(step.Table, column))
                && _foreignKeys.FirstOrDefault(f =>
                    f.SetNull && f.Child == step.Table && f.ChildColumns.Contains(column)
                )
                    is { } setNull
            )
            {
                return $"R11: {step.Table}.{column} is made NOT NULL while {setNull.Name} sets it to NULL";
            }

            if (
                newType is null
                || !_types.TryGetValue(ColumnKey(step.Table, column), out var oldType)
                || capabilities.AreSameType(oldType, newType)
            )
            {
                return null;
            }

            if (
                capabilities.AlterColumnRequiresKeyConstraintRebuild
                && _keys.FirstOrDefault(k => k.Table == step.Table && k.Columns.Contains(column))
                    is { } key
            )
            {
                return $"R2: {step.Table}.{column} is altered while {Describe(key)} contains it";
            }

            if (
                capabilities.AlterColumnRequiresForeignKeyRebuild
                && _foreignKeys.FirstOrDefault(f =>
                    (f.Child == step.Table && f.ChildColumns.Contains(column))
                    || (f.Parent == step.Table && f.ParentColumns.Contains(column))
                )
                    is { } fk
            )
            {
                return $"R2: {step.Table}.{column} is altered while {fk.Name} uses it";
            }

            // R7：残す外部キーのこの列を含む組の両端が、列の変更の後に DB が必ず断る組になる（測った事実＝
            // SyncPlanFuzzDialect.RefusesForeignKeyTypes。製品の宣言が偽でも DB が受け入れる組は予言しない）
            foreach (var kept in _foreignKeys)
            {
                for (var i = 0; i < kept.ChildColumns.Count; i++)
                {
                    var childKey = ColumnKey(kept.Child, kept.ChildColumns[i]);
                    var parentKey = ColumnKey(kept.Parent, kept.ParentColumns[i]);
                    var altered = ColumnKey(step.Table, column);

                    if (childKey != altered && parentKey != altered)
                    {
                        continue;
                    }

                    var childType = childKey == altered ? newType : TypeOf(childKey);
                    var parentType = parentKey == altered ? newType : TypeOf(parentKey);

                    if (
                        childType.Length > 0
                        && parentType.Length > 0
                        && SyncPlanFuzzDialect.RefusesForeignKeyTypes(
                            dialect,
                            capabilities,
                            childType,
                            parentType
                        )
                    )
                    {
                        return $"R7: {step.Table}.{column} is altered to {newType} while {kept.Name} pairs it with {(childKey == altered ? parentType : childType)}";
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// R4・R5・R8：足す外部キーの名前と、参照する列の集合が親の候補キーか、組の両端の今の型が並べられるか
        /// </summary>
        private string? CheckForeignKeyAdd(Step step)
        {
            var item = step.Item;
            var parent = Lower(item.ParentEntity!.TableName);
            var child = Lower(item.ChildEntity!.TableName);
            var parentColumns = item
                .ForeignKeyColumnPairs.Select(p => Lower(p.ParentColumn))
                .ToList();

            if (!_keys.Any(k => k.Table == parent && SameSet(k.Columns, parentColumns)))
            {
                return $"R5: {SyncScriptBuilderHelper.ForeignKeyNameToAdd(item, Limit)} references {parent}({string.Join(",", parentColumns)}), which is not a key";
            }

            // R8：組の両端の今の型を DB が必ず断る（測った事実＝SyncPlanFuzzDialect.RefusesForeignKeyTypes。MySQL 3780・SQL Server 1778 / 1753・
            // PostgreSQL 42804・Oracle ORA-02267）。
            // DB 側の図の外部キーが同じ型のまま持っていた組（外して付け直す文）は、DB が受け入れたので数えない
            foreach (var pair in item.ForeignKeyColumnPairs)
            {
                var childKey = ColumnKey(child, pair.ChildColumn);
                var parentKey = ColumnKey(parent, pair.ParentColumn);
                var childType = TypeOf(childKey);
                var parentType = TypeOf(parentKey);

                if (
                    childType.Length > 0
                    && parentType.Length > 0
                    && !_acceptedPairs.Contains((childKey, parentKey, childType, parentType))
                    && SyncPlanFuzzDialect.RefusesForeignKeyTypes(
                        dialect,
                        capabilities,
                        childType,
                        parentType
                    )
                )
                {
                    return $"R8: {SyncScriptBuilderHelper.ForeignKeyNameToAdd(item, Limit)} pairs {childKey.Replace('|', '.')} ({childType}) with {parentKey.Replace('|', '.')} ({parentType})";
                }
            }

            // R12：同じ定義の外部キー（子の表・親の表・列の組の集合）を 2 本持てない方言（Oracle の ORA-02275）で、残っている外部キーと同じ
            // 定義を足す
            if (dialect == DialectNames.Oracle)
            {
                var pairs = PairSet(
                    item.ForeignKeyColumnPairs.Select(p =>
                        (Lower(p.ChildColumn), Lower(p.ParentColumn))
                    )
                );

                if (
                    _foreignKeys.FirstOrDefault(f =>
                        f.Child == child
                        && f.Parent == parent
                        && PairSet(f.ChildColumns.Zip(f.ParentColumns)) == pairs
                    ) is
                    { } duplicate
                )
                {
                    return $"R12: {SyncScriptBuilderHelper.ForeignKeyNameToAdd(item, Limit)} has the same definition as {duplicate.Name}";
                }
            }

            return CheckName(
                Lower(item.ChildEntity!.TableName),
                SyncScriptBuilderHelper.ForeignKeyNameToAdd(item, Limit),
                isForeignKey: true
            );

            // 列の組の集合（並びを見ない）
            static string PairSet(IEnumerable<(string Child, string Parent)> pairs) =>
                string.Join(
                    ",",
                    pairs.Select(p => $"{p.Parent}>{p.Child}").Order(StringComparer.Ordinal)
                );
        }

        /// <summary>R4：作る表の主キー・一意制約の名前</summary>
        private string? CheckTableAdd(Step step)
        {
            if (step.Item.Entity is not { } entity)
            {
                return null;
            }

            if (
                !IsMySql
                && entity.GetPrimaryKeyColumnsInOrder().Count > 0
                && CheckName(step.Table, PrimaryKeyName(entity.TableName), false) is { } pk
            )
            {
                return pk;
            }

            // 同期スクリプトと同じく、差分が図全体で決めた名前で作る
            return UniqueConstraintNaming
                .ResolveAll(entity, step.Item.UniqueConstraintNames, Limit)
                .Select(u => CheckName(step.Table, u.Name, false))
                .FirstOrDefault(r => r is not null);
        }

        /// <summary>R4：名前の範囲で重ならないか</summary>
        /// <remarks>
        /// MySQL の一意制約は、同じ表の外部キーが自分の名前で作った索引ともぶつかる（Duplicate key name）が、R4 はこの組を数えない。
        /// 数えるには、別の索引ができたときに MySQL が黙って外す外部キーの索引と、同期スクリプトが外部キーの削除の後に外す索引の両方を
        /// 写す必要があり、写し違えると DB が通す計画の失敗を予言してしまう（この組は実 DB の行 mysql-synthesized-unique-name-foreign-key が捕まえる）。
        /// </remarks>
        private string? CheckName(string table, string name, bool isForeignKey)
        {
            var comparison = dialect is DialectNames.SqlServer or DialectNames.MySql
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            IEnumerable<(string Table, string Name)> taken = dialect switch
            {
                DialectNames.SqlServer or DialectNames.Oracle => AllNames(),
                DialectNames.PostgreSql => isForeignKey
                    ? AllNames().Where(n => n.Table == table)
                    : _keys
                        .Select(k => (k.Table, k.Name))
                        .Concat(
                            _foreignKeys.Where(f => f.Child == table).Select(f => (f.Child, f.Name))
                        ),
                DialectNames.MySql => isForeignKey
                    ? _foreignKeys.Select(f => (f.Child, f.Name))
                    : _keys
                        .Where(k => !k.Primary && k.Table == table)
                        .Select(k => (k.Table, k.Name)),
                _ => [],
            };

            return taken.Any(n => string.Equals(n.Name, name, comparison))
                ? $"R4: the name {name} is already used"
                : null;
        }

        private IEnumerable<(string Table, string Name)> AllNames() =>
            _keys
                .Select(k => (k.Table, k.Name))
                .Concat(_foreignKeys.Select(f => (f.Child, f.Name)));

        // ---- 状態の更新 ----

        private void AddTable(
            Entity entity,
            IReadOnlyDictionary<UniqueConstraint, string>? uniqueNames
        )
        {
            var table = Lower(entity.TableName);

            foreach (var column in entity.Columns)
            {
                _types[ColumnKey(table, column.Name)] = column.DataType;
                SetNullable(ColumnKey(table, column.Name), column);
            }

            var key = entity.GetPrimaryKeyColumnsInOrder();

            if (key.Count > 0)
            {
                _keys.Add(
                    new Key(
                        table,
                        [.. key.Select(c => Lower(c.Name))],
                        PrimaryKeyName(entity.TableName),
                        true
                    )
                );
            }

            foreach (var unique in UniqueConstraintNaming.ResolveAll(entity, uniqueNames, Limit))
            {
                _keys.Add(
                    new Key(table, [.. unique.ColumnNames.Select(Lower)], unique.Name, false)
                );
            }
        }

        private void AddForeignKey(ForeignKey fk)
        {
            _foreignKeys.Add(fk);

            // MySQL は、子の列を先頭に持つ索引が無ければ外部キーの名前で索引を作る
            if (
                IsMySql
                && !_keys.Any(k => k.Table == fk.Child && IsPrefix(fk.ChildColumns, k.Columns))
                && !_foreignKeyIndexes.Any(i =>
                    i.Table == fk.Child && IsPrefix(fk.ChildColumns, i.Columns)
                )
            )
            {
                _foreignKeyIndexes.Add((fk.Child, fk.ChildColumns));
            }
        }

        private void RemoveForeignKey(Step step)
        {
            var item = step.Item;
            var child = Lower(item.ChildEntity!.TableName);
            var parent = Lower(item.ParentEntity!.TableName);
            var childColumns = item
                .ForeignKeyColumnPairs.Select(p => Lower(p.ChildColumn))
                .ToList();
            var candidates = _foreignKeys
                .Where(f =>
                    f.Child == child
                    && f.Parent == parent
                    && f.ChildColumns.SequenceEqual(childColumns)
                )
                .ToList();
            var named = item.ForeignKeyName is { } name
                ? candidates.FirstOrDefault(f =>
                    string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)
                )
                : null;

            if ((named ?? candidates.FirstOrDefault()) is { } removed)
            {
                _foreignKeys.Remove(removed);
            }
        }

        private Key? FindUnique(string table, string? name, IReadOnlyList<string> columns)
        {
            var candidates = _keys.Where(k => !k.Primary && k.Table == table).ToList();

            return (
                    name is null
                        ? null
                        : candidates.FirstOrDefault(k =>
                            string.Equals(k.Name, name, StringComparison.OrdinalIgnoreCase)
                        )
                )
                ?? candidates.FirstOrDefault(k =>
                    SameSet(k.Columns, columns.Select(Lower).ToList())
                );
        }

        private string UniqueName(Step step) =>
            UniqueConstraintNaming.Resolve(
                step.Item.UniqueConstraintName,
                step.Item.TableName,
                step.Item.UniqueConstraintColumns,
                Limit
            );

        private string PrimaryKeyName(string table) => ConstraintNames.PrimaryKey(table, Limit);

        private static string Describe(Key key) =>
            $"{(key.Primary ? "primary key" : "unique constraint")} {key.Name} of {key.Table}({string.Join(",", key.Columns)})";
    }

    private static bool SameSet(IReadOnlyCollection<string> a, IReadOnlyCollection<string> b) =>
        a.Count == b.Count && a.ToHashSet(StringComparer.Ordinal).SetEquals(b);

    /// <summary><paramref name="columns"/> が <paramref name="index"/> の先頭に同じ並びで並ぶか</summary>
    private static bool IsPrefix(IReadOnlyList<string> columns, IReadOnlyList<string> index) =>
        columns.Count > 0
        && columns.Count <= index.Count
        && columns.Select((c, i) => index[i] == c).All(x => x);

    private static string ColumnKey(string table, string column) => $"{table}|{Lower(column)}";

    private static string Lower(string name) => name.Trim().ToLowerInvariant();
}
