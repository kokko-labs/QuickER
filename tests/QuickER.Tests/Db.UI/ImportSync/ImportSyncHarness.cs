using System.ComponentModel;
using AwesomeAssertions;
using QuickER.Db.UI;
using QuickER.Db.UI.Resources;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Tests.TestDoubles;

namespace QuickER.Tests.Db.UI.ImportSync;

/// <summary>
/// 取込と同期の表の 1 行の組（記録するダイアログ・ホストの偽物・接続ダイアログと同期のダイアログの提示の偽物・
/// 取込と実行器を偽物にしたプロバイダ・同期のダイアログの VM）と、どの手の後にも掛ける不変条件の見張り
/// </summary>
/// <remarks>
/// <para>
/// 組は記録するダイアログと偽の実行器を必ず渡す（省くと失敗の経路で実の MessageBox が出て止まり、確認を承認した行で実の同期の実行器が
/// 相対パスの shop.db へ走る）。取込（<see cref="RunImportAsync"/>）・同期のコマンド（<see cref="RunSyncCommand"/>）・同期のダイアログの
/// 再計算・実行・選択・閉じるは組の手を通し、組が手の前後を比べる。
/// </para>
/// <para>取込（src/QuickER.Db.UI/DbImportCommandService.cs）の不変条件：</para>
/// <list type="number">
/// <item>取込の接続ダイアログは取込モード・新規作成なし・取込の題名で、初期選択の方言は対象の DBMS をホストで解決したもの（:46-54）。</item>
/// <item>図の置き換えは高々 1 回で、置き換えるのは取込が成功し、確認が要らないか承認され、図を読んだ時点と置き換える時点の世代が同じときだけ。置き換えた図の方言は取込先の方言（:77・:87-102・:107-114）。</item>
/// <item>図を読んだ後に世代が変わり、確認を断らなかったら、置き換えずに取りやめをモーダル（ShowInformation）で 1 件知らせる。変わらなければ取りやめを知らせない（:94-102。src/QuickER.Gui/ViewModels/CLAUDE.md「確認の間に変わった図」）。</item>
/// <item>終端の通知はちょうど 1 件：取り消し・確認を断ったときは 0 件、ほかは成功（ShowInformation か ShowInformationDetails）・取りやめ・失敗（ShowError）のどれか 1 件。成功の通知は置き換えたときだけ、失敗の通知は Db_ImportFailed の文言（:117-133）。警告の整形が例外で抜けるとき（置き換えの後）だけは通知が無い（:126-133）。</item>
/// <item>確認は未保存なら警告つき（ConfirmWarning）・そうでなければ通常（Confirm）で、出すなら 1 回だけ。詳細つきの確認は使わない（:142-173・src/QuickER.Gui.Abstractions/DialogServiceExtensions.cs:19-27）。</item>
/// </list>
/// <para>同期のコマンド（src/QuickER.Db.UI/DbSyncCommandService.cs）の不変条件：</para>
/// <list type="number" start="6">
/// <item>同期の接続ダイアログは同期モード・新規作成あり・同期の題名で、固定する方言は対象の DBMS をホストで解決したもの（:48-58）。ダイアログは何も出さない。</item>
/// <item>接続を取り消したら同期のダイアログを開かず、確定したら 1 回だけ、確定した方言・接続設定と、接続の確定の後に読んだ図（:66）で開く。</item>
/// </list>
/// <para>同期のダイアログ（src/QuickER.Db.UI/SchemaSyncDialogViewModel.cs）の不変条件：</para>
/// <list type="number" start="8">
/// <item>実行中（IsExecuting）は閉じられず、再計算・実行の最中（IsBusy）は再計算・実行・全選択・全解除ができない（:136-139）。行の終わりにはどちらも解けている。</item>
/// <item>実行の結果のモーダル（ShowError / ShowInformation）は実行器を待ち終えた後（IsExecuting が解けた後）に出す（:386-401・:409・:422・:434・:454）。</item>
/// <item>スクリプトが空か実行の最中なら、確認も実行器も呼ばない（:359-368）。確認は警告つき（詳細つきを含む）だけで 1 回、断ったら実行器を呼ばない（:370-373・:524-526）。</item>
/// <item>実行器を呼んだら、結果はモーダル 1 件（失敗・例外は ShowError・コミットは ShowInformation）で知らせ、どの結果でも差分を取り直す。自動で閉じるのはコミットし、取り直しが成功し、差分が 0 件のときだけ。コミットの後の取り直しが失敗したら閉じずに ShowError をもう 1 件出す（成功のモーダルの後に黙って閉じない）。失敗・例外の後の取り直しの失敗はステータス行だけ（失敗のモーダルは出した後で、閉じないので理由が残る）（:403-464）。</item>
/// <item>取り直しが失敗したら、差分・スクリプトを残さない（差分 0 件・HasDiff 偽・スクリプト空。:255-275）。</item>
/// <item>閉じるのは実行中でないときだけで、閉じるときは CloseAction(true)（:671-680）。</item>
/// <item>再計算・実行の最中（IsBusy）に始めた再計算は、取込を呼ばない（再計算中の実行・実行中の再計算を作らない。:143-148）。</item>
/// </list>
/// 8・9 は VM の変更通知とモーダルの直前でも見張り、ほかは手の後と行の終わりに見る。
/// </remarks>
internal sealed class ImportSyncHarness
{
    private readonly object _lock = new();
    private readonly List<string> _violations = [];
    private readonly List<SchemaSyncDialogViewModel> _syncVms = [];
    private readonly Dictionary<SchemaSyncDialogViewModel, ImportSyncProvider> _providers = [];
    private readonly Dictionary<SchemaSyncDialogViewModel, List<bool>> _closes = [];
    private readonly List<Task> _running = [];

    /// <summary>組を作る（モーダルの直前に不変条件 9 を見張る）</summary>
    public ImportSyncHarness()
    {
        Dialogs.OnBeforeShow = () =>
        {
            foreach (var vm in Snapshot())
            {
                if (vm.IsExecuting)
                {
                    Violate("不変条件 9：実行器を待っている間に結果のモーダルを出した");
                }
            }
        };
    }

    /// <summary>記録するダイアログ</summary>
    public StubDialogService Dialogs { get; } = new();

    // ---------------- 取込と同期のコマンド ----------------

    /// <summary>ホストの偽物を作る（図・未保存・対象の DBMS）</summary>
    public static ImportSyncHost Host(
        ErDiagram? diagram = null,
        bool dirty = false,
        string target = "sqlserver"
    ) =>
        new()
        {
            Diagram = diagram ?? new ErDiagram(),
            Dirty = dirty,
            Target = target,
        };

    /// <summary>接続ダイアログの提示の偽物を作る（答えは null＝取り消し）</summary>
    public static ImportSyncConnectionPresenter Presenter(ImportSyncHost host) => new(host);

    /// <summary>取込のプロバイダ（取込先の方言の名前は fakeimport）を作る</summary>
    public static ImportSyncProvider ImportProvider(
        IReadOnlyList<Entity> entities,
        IReadOnlyList<SchemaImportWarning>? warnings = null
    ) =>
        new(
            new ImportOnlyDialect(),
            new ImportSyncImporter(
                new SchemaImportResult { Entities = entities, Warnings = warnings ?? [] }
            ),
            new ImportSyncExecutor()
        );

    /// <summary>取込のコマンドを流し、不変条件 1〜5 を見る（警告の整形などで抜けた例外を返す）</summary>
    public async Task<Exception?> RunImportAsync(
        ImportSyncHost host,
        ImportSyncConnectionPresenter presenter
    )
    {
        var before = Counts();
        var confirmAnswer = Dialogs.ConfirmResult;
        var reads = host.Reads.Count;
        var replaced = host.Replaced.Count;
        var calls = presenter.Calls.Count;
        Exception? thrown = null;

        try
        {
            await new DbImportCommandService(host, Dialogs, presenter).RunAsync();
        }
        catch (Exception ex)
        {
            thrown = ex;
        }

        var after = Counts();

        // 不変条件 1
        if (presenter.Calls.Count != calls + 1)
        {
            Violate("不変条件 1：取込が接続ダイアログをちょうど 1 回開かなかった");
        }
        else
        {
            var call = presenter.Calls[^1];
            var expectedFixed = host.Registry.TryGet(host.Target, out var resolved)
                ? resolved
                : null;

            if (
                call.Mode != DbConnectionDialogMode.Import
                || call.AllowSqliteFileCreation
                || call.Title != Strings.Db_ImportTitle
                || !ReferenceEquals(call.FixedProvider, expectedFixed)
            )
            {
                Violate("不変条件 1：取込の接続ダイアログの開き方が違う");
            }
        }

        var picked = presenter.Result;
        var confirms = (after.Confirm - before.Confirm) + (after.Warning - before.Warning);
        var declined = confirms > 0 && !confirmAnswer;
        var replacedNow = host.Replaced.Skip(replaced).ToList();
        var read = host.Reads.Skip(reads).ToList();
        var abandon = Dialogs
            .InformationMessages.Skip(before.Information)
            .Count(message => message == Strings.Db_ImportDiagramChangedDuringConfirm);
        var notifications =
            (after.Information - before.Information)
            + (after.InformationDetails - before.InformationDetails)
            + (after.Error - before.Error);

        // 不変条件 5
        if (after.WarningDetails != before.WarningDetails || confirms > 1)
        {
            Violate("不変条件 5：取込の確認が詳細つきか、2 回以上出た");
        }

        if (confirms == 1 && (after.Warning > before.Warning) != host.Dirty)
        {
            Violate("不変条件 5：取込の確認の水準が未保存の有無と食い違う");
        }

        // 不変条件 2
        if (replacedNow.Count > 1)
        {
            Violate("不変条件 2：図を 2 回以上置き換えた");
        }

        if (replacedNow.Count == 1)
        {
            var (diagram, revision) = replacedNow[0];

            if (picked is null || declined || after.Error != before.Error)
            {
                Violate("不変条件 2：取り消し・断った確認・失敗のあとで図を置き換えた");
            }

            if (read.Count != 1 || read[0].Revision != revision)
            {
                Violate("不変条件 2：図を読んだ時点と置き換えた時点の世代が違うのに置き換えた");
            }

            if (picked is not null && diagram.TargetDbms != picked.Provider.Name)
            {
                Violate("不変条件 2：置き換えた図の方言が取込先の方言でない");
            }
        }

        // 不変条件 3
        var revisionMoved = read.Count == 1 && read[0].Revision != host.Revision;

        if (revisionMoved && !declined && after.Error == before.Error)
        {
            if (abandon != 1 || replacedNow.Count != 0)
            {
                Violate(
                    "不変条件 3：確認の間に図が変わったのに、取りやめずに置き換えたか知らせなかった"
                );
            }
        }
        else if (abandon != 0)
        {
            Violate("不変条件 3：図が変わっていないのに取りやめを知らせた");
        }

        // 不変条件 4
        if (thrown is not null)
        {
            if (replacedNow.Count != 1 || notifications != 0)
            {
                Violate("不変条件 4：置き換えの前に例外で抜けたか、例外のときに通知を出した");
            }
        }
        else if (picked is null || declined)
        {
            if (notifications != 0)
            {
                Violate("不変条件 4：取り消し・断った確認のあとで通知を出した");
            }
        }
        else if (notifications != 1)
        {
            Violate($"不変条件 4：終端の通知が {notifications} 件（ちょうど 1 件のはず）");
        }
        else
        {
            var succeeded =
                after.InformationDetails > before.InformationDetails
                || Dialogs
                    .InformationMessages.Skip(before.Information)
                    .Contains(Strings.Db_ImportCompleted);

            if (succeeded != (replacedNow.Count == 1))
            {
                Violate("不変条件 4：成功の通知と図の置き換えが食い違う");
            }

            var prefix = Strings.Db_ImportFailed.Split("{0}")[0];

            if (
                Dialogs
                    .ErrorMessages.Skip(before.Error)
                    .Any(message => !message.StartsWith(prefix, StringComparison.Ordinal))
            )
            {
                Violate("不変条件 4：取込の失敗の通知が Db_ImportFailed の文言でない");
            }
        }

        return thrown;
    }

    /// <summary>同期のダイアログの提示の偽物を作る</summary>
    public static ImportSyncSchemaSyncPresenter SyncPresenter() => new();

    /// <summary>同期のコマンドを作る</summary>
    public static DbSyncCommandService SyncCommand(
        ImportSyncHost host,
        ImportSyncConnectionPresenter presenter,
        ImportSyncSchemaSyncPresenter sync
    ) => new(host, presenter, sync);

    /// <summary>同期のコマンドを流し、不変条件 6・7 を見る</summary>
    public void RunSyncCommand(
        ImportSyncHost host,
        ImportSyncConnectionPresenter presenter,
        ImportSyncSchemaSyncPresenter sync
    )
    {
        var before = Counts();
        var calls = presenter.Calls.Count;
        var shown = sync.Calls.Count;
        var reads = host.Reads.Count;

        SyncCommand(host, presenter, sync).Run();

        // 不変条件 6
        if (presenter.Calls.Count != calls + 1)
        {
            Violate("不変条件 6：同期が接続ダイアログをちょうど 1 回開かなかった");
            return;
        }

        var call = presenter.Calls[^1];
        var expectedFixed = host.Registry.TryGet(host.Target, out var resolved) ? resolved : null;

        if (
            call.Mode != DbConnectionDialogMode.Sync
            || !call.AllowSqliteFileCreation
            || call.Title != Strings.Db_SyncTitle
            || !ReferenceEquals(call.FixedProvider, expectedFixed)
        )
        {
            Violate("不変条件 6：同期の接続ダイアログの開き方が違う");
        }

        if (!Counts().Equals(before))
        {
            Violate("不変条件 6：同期のコマンドがダイアログを出した");
        }

        // 不変条件 7
        var picked = presenter.Result;

        if (picked is null)
        {
            if (sync.Calls.Count != shown)
            {
                Violate("不変条件 7：接続を取り消したのに同期のダイアログを開いた");
            }

            return;
        }

        var read = host.Reads.Skip(reads).ToList();

        if (sync.Calls.Count != shown + 1 || read.Count != 1)
        {
            Violate("不変条件 7：確定したのに同期のダイアログを 1 回だけ開かなかった");
            return;
        }

        var (provider, settings, entities, relationships) = sync.Calls[^1];

        if (
            !ReferenceEquals(provider, picked.Provider)
            || !ReferenceEquals(settings, picked.Settings)
            || !ReferenceEquals(entities, read[0].Diagram.Entities)
            || !ReferenceEquals(relationships, read[0].Diagram.Relationships)
            || read[0].Sequence < call.ReturnedAt
        )
        {
            Violate(
                "不変条件 7：同期のダイアログへ渡したものが、確定した接続と確定の後に読んだ図でない"
            );
        }
    }

    // ---------------- 同期のダイアログ ----------------

    /// <summary>同期のプロバイダ（本物の方言の組み立て・宣言に、偽の取込と偽の実行器）を作る</summary>
    public static ImportSyncProvider SyncProvider(
        IDatabaseProvider dialect,
        SchemaImportResult? live = null
    ) =>
        new(
            dialect,
            new ImportSyncImporter(live ?? new SchemaImportResult()),
            new ImportSyncExecutor()
        );

    /// <summary>同期のダイアログの VM を作る（閉じた記録と不変条件 8 の見張りを付ける）</summary>
    public SchemaSyncDialogViewModel CreateSync(
        ImportSyncProvider provider,
        DbConnectionSettings? settings = null,
        IReadOnlyList<Entity>? target = null,
        IReadOnlyList<Relationship>? relationships = null
    )
    {
        var vm = new SchemaSyncDialogViewModel(
            provider,
            settings ?? new DbConnectionSettings(),
            target ?? [],
            relationships ?? [],
            Dialogs
        );
        var closes = new List<bool>();
        vm.CloseAction = closes.Add;

        lock (_lock)
        {
            _syncVms.Add(vm);
            _providers[vm] = provider;
            _closes[vm] = closes;
        }

        vm.PropertyChanged += (_, e) => OnSyncPropertyChanged(vm, e);
        return vm;
    }

    /// <summary>同期のダイアログが閉じた記録</summary>
    public IReadOnlyList<bool> Closes(SchemaSyncDialogViewModel vm) => _closes[vm];

    /// <summary>差分を再計算する（止めた取込なら実行中のまま返る。不変条件 12 を終わった後に見る）</summary>
    public Task RefreshAsync(SchemaSyncDialogViewModel vm) => Track(RefreshCoreAsync(vm));

    /// <summary>選択中の差分を実行する（止めた実行器なら実行中のまま返る。不変条件 10〜12 を終わった後に見る）</summary>
    public Task ExecuteAsync(SchemaSyncDialogViewModel vm) => Track(ExecuteCoreAsync(vm));

    /// <summary>全選択</summary>
    public void SelectAll(SchemaSyncDialogViewModel vm)
    {
        vm.SelectAllCommand.Execute(null);
        CheckOperable(vm, "全選択");
    }

    /// <summary>全解除</summary>
    public void DeselectAll(SchemaSyncDialogViewModel vm)
    {
        vm.DeselectAllCommand.Execute(null);
        CheckOperable(vm, "全解除");
    }

    /// <summary>閉じるのコマンドを実行する（不変条件 13）</summary>
    public void Close(SchemaSyncDialogViewModel vm)
    {
        var executing = vm.IsExecuting;
        var closes = _closes[vm].Count;
        vm.CloseCommand.Execute(null);
        var closed = _closes[vm].Skip(closes).ToList();

        if (executing ? closed.Count != 0 : !closed.SequenceEqual([true]))
        {
            Violate("不変条件 13：閉じるの結果が実行中かどうかと食い違う");
        }
    }

    /// <summary>行の本体の後に、止めた取込・実行器を開け、実行中が解けることと見張った不変条件を表明する</summary>
    public async Task FinishAsync()
    {
        List<SchemaSyncDialogViewModel> vms = Snapshot();

        foreach (var provider in vms.Select(vm => _providers[vm]))
        {
            foreach (var gate in provider.Importer.Gates)
            {
                gate.TrySetResult(provider.Importer.Result);
            }

            foreach (var (_, release) in provider.Executor.Gates)
            {
                release.TrySetResult();
            }
        }

        List<Task> running;

        lock (_lock)
        {
            running = [.. _running];
        }

        foreach (var task in running)
        {
            await task.WaitAsync(TimeSpan.FromSeconds(60));
        }

        foreach (var vm in vms)
        {
            if (vm.IsBusy || vm.IsExecuting)
            {
                Violate("不変条件 8：行の終わりに再計算・実行の最中のまま");
            }

            CheckOperable(vm, "行の終わり");
        }

        List<string> violations;

        lock (_lock)
        {
            violations = [.. _violations];
        }

        violations.Should().BeEmpty();
    }

    private async Task RefreshCoreAsync(SchemaSyncDialogViewModel vm)
    {
        var importer = _providers[vm].Importer;
        var wasBusy = vm.IsBusy;
        var importCalls = importer.Calls;

        await vm.RefreshCommand.ExecuteAsync(null);

        // 不変条件 14（最中に始めた再計算は取込を呼ばない）
        if (wasBusy && importer.Calls != importCalls)
        {
            Violate("不変条件 14：再計算・実行の最中に始めた再計算が DB を読み直した");
        }

        CheckClearedAfterFailedRefresh(vm, "再計算");
    }

    private async Task ExecuteCoreAsync(SchemaSyncDialogViewModel vm)
    {
        var provider = _providers[vm];
        var before = Counts();
        var executorCalls = provider.Executor.Calls;
        var importCalls = provider.Importer.Calls;
        var closes = _closes[vm].Count;
        var blocked = string.IsNullOrWhiteSpace(vm.ScriptPreview) || vm.IsBusy;
        var confirmAnswer = Dialogs.ConfirmResult;

        await vm.ExecuteCommand.ExecuteAsync(null);

        var after = Counts();
        var confirms =
            (after.Warning - before.Warning) + (after.WarningDetails - before.WarningDetails);
        var executed = provider.Executor.Calls - executorCalls;

        // 不変条件 10
        if (after.Confirm != before.Confirm || confirms > 1)
        {
            Violate("不変条件 10：実行の確認が警告つきでないか、2 回以上出た");
        }

        if (blocked && (confirms != 0 || executed != 0))
        {
            Violate("不変条件 10：スクリプトが空か実行の最中なのに、確認か実行器を呼んだ");
        }

        if (confirms == 1 && !confirmAnswer && executed != 0)
        {
            Violate("不変条件 10：確認を断ったのに実行器を呼んだ");
        }

        // 不変条件 11
        if (executed > 1)
        {
            Violate("不変条件 11：1 回の実行で実行器を 2 回以上呼んだ");
        }

        if (executed == 1)
        {
            var outcome = provider.Executor.LastOutcome;
            var refreshFailed = provider.Importer.LastFailed;
            var errors = after.Error - before.Error;
            var information = after.Information - before.Information;
            var expected =
                outcome == ExecutorOutcome.Committed ? (refreshFailed ? 1 : 0, 1) : (1, 0);

            if ((errors, information) != expected)
            {
                Violate(
                    $"不変条件 11：実行器の結果 {outcome} のモーダルが エラー {errors} 件・情報 {information} 件"
                );
            }

            if (provider.Importer.Calls <= importCalls)
            {
                Violate("不変条件 11：実行器の結果の後に差分を取り直さなかった");
            }

            var autoClosed = _closes[vm].Skip(closes).Contains(true);
            var shouldClose =
                outcome == ExecutorOutcome.Committed && !refreshFailed && vm.DiffItems.Count == 0;

            if (autoClosed != shouldClose)
            {
                Violate(
                    "不変条件 11：自動で閉じたかどうかが、コミットして取り直しが成功し差分 0 件と食い違う"
                );
            }
        }
        else if (_closes[vm].Count != closes)
        {
            Violate("不変条件 11：実行器を呼ばない実行でダイアログを閉じた");
        }

        CheckClearedAfterFailedRefresh(vm, "実行");
    }

    /// <summary>不変条件 12（取り直しが失敗したら差分・スクリプトを残さない）</summary>
    private void CheckClearedAfterFailedRefresh(SchemaSyncDialogViewModel vm, string op)
    {
        if (!_providers[vm].Importer.LastFailed)
        {
            return;
        }

        if (vm.DiffItems.Count != 0 || vm.HasDiff || vm.ScriptPreview.Length != 0)
        {
            Violate($"不変条件 12：取り直しが失敗した後（{op}）に差分かスクリプトが残った");
        }
    }

    private void OnSyncPropertyChanged(SchemaSyncDialogViewModel vm, PropertyChangedEventArgs e)
    {
        if (
            e.PropertyName
            is nameof(SchemaSyncDialogViewModel.IsBusy)
                or nameof(SchemaSyncDialogViewModel.IsExecuting)
        )
        {
            CheckOperable(vm, $"{e.PropertyName} の変更通知");
        }
    }

    /// <summary>不変条件 8（実行中は閉じられず、最中は操作できない）</summary>
    private void CheckOperable(SchemaSyncDialogViewModel vm, string when)
    {
        if (vm.IsExecuting && vm.CloseCommand.CanExecute(null))
        {
            Violate($"不変条件 8：実行中に閉じられる（{when}）");
        }

        if (
            vm.IsBusy
            && (
                vm.RefreshCommand.CanExecute(null)
                || vm.ExecuteCommand.CanExecute(null)
                || vm.SelectAllCommand.CanExecute(null)
                || vm.DeselectAllCommand.CanExecute(null)
            )
        )
        {
            Violate($"不変条件 8：再計算・実行の最中に操作できる（{when}）");
        }
    }

    private Task Track(Task task)
    {
        lock (_lock)
        {
            _running.Add(task);
        }

        return task;
    }

    private List<SchemaSyncDialogViewModel> Snapshot()
    {
        lock (_lock)
        {
            return [.. _syncVms];
        }
    }

    private void Violate(string message)
    {
        lock (_lock)
        {
            _violations.Add(message);
        }
    }

    /// <summary>ダイアログの記録の件数</summary>
    private DialogCounts Counts() =>
        new(
            Dialogs.ConfirmMessages.Count,
            Dialogs.WarningConfirmMessages.Count,
            Dialogs.WarningConfirmDetailsMessages.Count,
            Dialogs.InformationMessages.Count,
            Dialogs.InformationDetailsMessages.Count,
            Dialogs.ErrorMessages.Count
        );

    /// <summary>ダイアログの記録の件数（確認・警告つきの確認・詳細つきの確認・情報・内訳つきの情報・エラー）</summary>
    private readonly record struct DialogCounts(
        int Confirm,
        int Warning,
        int WarningDetails,
        int Information,
        int InformationDetails,
        int Error
    );
}
