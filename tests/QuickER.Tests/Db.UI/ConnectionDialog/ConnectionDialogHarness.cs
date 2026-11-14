using System.ComponentModel;
using System.IO;
using AwesomeAssertions;
using QuickER.Db.UI;
using QuickER.Db.UI.Resources;
using QuickER.Platform;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
using QuickER.Tests.TestDoubles;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Db.UI.ConnectionDialog;

/// <summary>門で止めた接続テスト（門を開けて終わりまで待てる）</summary>
internal sealed record RunningTest(ImportGate Gate, Task Run)
{
    /// <summary>門を開けて接続テストの終わりを待つ</summary>
    public async Task FinishAsync()
    {
        Gate.Release.TrySetResult();
        await Run.WaitAsync(TimeSpan.FromSeconds(60));
    }
}

/// <summary>接続ダイアログの表の VM に登録する方言の組</summary>
internal enum ConnectionDialects
{
    /// <summary>SQL Server だけ</summary>
    SqlServerOnly,

    /// <summary>SQL Server と SQLite</summary>
    WithSqlite,

    /// <summary>5 方言すべて</summary>
    All,
}

/// <summary>
/// 接続ダイアログの表の 1 行の組（行ごとの一時フォルダの接続プロファイルのストア・記録するダイアログ・偽のファイルダイアログ・
/// 取込を偽物にした 5 方言・VM）と、どの手の後にも掛ける不変条件の見張り
/// </summary>
/// <remarks>
/// <para>
/// 組は記録するダイアログ・偽のファイルダイアログ・一時フォルダのストアを必ず渡す（省くと失敗の経路で実の MessageBox が出て止まり、
/// 開発機の <c>%LOCALAPPDATA%\QuickER</c> の接続プロファイルを読む）。方言は本物の宣言のまま取込だけを偽物にする
/// （接続テストが実 DB へ繋ぎに行かない）。保存・名前の変更・削除・接続テスト・参照・OK・取り消しは組の手（<see cref="Save"/> など）を通し、
/// 組が手の前後を比べる。
/// </para>
/// <para>不変条件（出典は src/QuickER.Db.UI/DbConnectionDialogViewModel.cs の行）：</para>
/// <list type="number">
/// <item>確定して閉じる（CloseAction(true)）のは Result と ResultProvider が入ったときだけ、取り消して閉じる（CloseAction(false)）のは両方を空にしたときだけ。閉じない手は Result を変えない（:996-998・:1005-1007。検証の失敗 :924-973 は Result を作らず閉じない）。</item>
/// <item>確定した接続設定のタイムアウトは 0 以上・ポートは空か 1〜65535 で、保存したプロファイルも同じ範囲に入る（:433・:438・:448-461・:408-425。確定・接続テスト・保存が同じ検証を通す :607・:658・:924）。</item>
/// <item>同期モードは方言を固定する：選択中の方言は作った時の方言から変わらず、DBMS を選べず、確定した方言は固定した方言。一覧と選択中のプロファイルは固定した方言のものだけ（:182・:354-364・:380-386。一覧の絞り込みは ReloadProfiles）。</item>
/// <item>接続テストの実行中は OK で確定できない（:326・:910。:633-636 で戻す）。行の終わりには接続テストが終わっている。</item>
/// <item>取り消せない上書き・削除は警告つきの確認（ConfirmWarning）だけで聞き、通常の確認・詳細つきの確認は使わない（:730-740・:835-840）。削除は選択中のプロファイルがあれば必ず確認する。</item>
/// <item>プロファイルの保存・名前の変更・削除の後と行の終わりに、一覧（DBMS・名前・接続先）はディスクの実体どおり（同期モードは固定した方言の分。読めないときは読めるようになってから見る）。ただし VM が一覧を読めなかったと言っている間（AreProfilesUnavailable）は、一覧も選択も空で、ステータス行がそれを知らせている（ProfilesUnavailable か、保存の中止の ProfileStoreUnavailable）。失敗は例外を外へ出さない（中止 :701-707・:794-799・:852-857 は何も書かず、書き込みの失敗 :708-712・:800-804・:858-864 は一覧を読み直す :475-480）。</item>
/// <item>読めない保存済みパスワードの暗号ファイルを失わない：プロファイルが残る限り、暗号ファイルは元のバイト列のままか、入力し直したパスワードで書き直されている（:679-688・:789-792）。例外は、利用者が「パスワードを保存」を外して保存したプロファイル（手の前の入力で記録する。断りの文と docs/database.ja.md が案内する、保存をやめる操作）。</item>
/// <item>保存・名前の変更の直後も入力欄（接続先・認証・パスワード・ファイル・タイムアウト・方言）を保存値で塗り直さない（:483-497・:714-718・:816-818）。</item>
/// <item>一覧のプロファイルは（DBMS, 名前）で大文字小文字を無視して一意で、行の終わりにはディスクのプロファイルも同じく一意（:664-674・:771-777）。</item>
/// <item>SQLite の新規作成で選んだファイルは、OK で確定するまで作られない（:896-907・:940-967）。</item>
/// <item>エラーのモーダルは OK の前回接続の記録の失敗だけで出し、出しても確定は止めない（:982-998）。ほかの失敗はステータス行で知らせる（:703-705）。</item>
/// <item>SQLite の新規作成で作ったファイルを、作った後に誰も掴んでいない（:952-957 の <c>SqliteDatabaseFile.CreateEmpty</c> はプールしない接続で作る＝src/QuickER.Provider.Sqlite/SqliteFileConnections.cs）。掴んでいると、作った直後に利用者がファイルを移動・削除できない。排他で開けるかで見る。</item>
/// <item>選択を付け替えた手の後、選択中のプロファイルがあれば、保存名の欄はその名前（選んだプロファイルが入力欄に当たった。:526-578）。同じ項目の選び直し（セッターが何もしない）は対象外。食い違うと、名前の変更・削除が入力欄に出ていないプロファイルに効く。</item>
/// <item>開いた直後、connections.json が壊れていれば（ストアの分類 Corrupt＝JSON として読めない・旧配列形式）、一覧も選択も空で、ステータス行が壊れていること・ファイルの場所・最初に壊れていたファイルの写しの場所を知らせている（ProfilesCorrupt。ReloadProfiles）。読めない一覧を「保存されたものが無い」と同じ空として黙って出さない。</item>
/// <item>参照・新規作成のコマンドは、画面でボタンが出ているときだけ実行できる（参照は ShowFilePath＝SQLite・新規作成は ShowCreateNewFile。CanBrowseFile・CanBrowseNewFile）。ボタンが出ていないのに実行できると、画面に出ていない方言の接続設定にファイルのパスを書く。</item>
/// </list>
/// 3・4 は VM の変更通知でも見張り、1・2・3・5・8〜12・15 は手の後に、6 は保存・名前の変更・削除の後と行の終わりに、7・9・12 は行の終わりに、13 は選択の手の後に、14 は開いた直後に見る。
/// </remarks>
internal sealed class ConnectionDialogHarness : IDisposable
{
    private readonly TestTempFolder _folder = new();
    private readonly object _lock = new();
    private readonly List<string> _violations = [];
    private readonly List<DbConnectionDialogViewModel> _vms = [];
    private readonly Dictionary<DbConnectionDialogViewModel, List<bool>> _closes = [];
    private readonly Dictionary<DbConnectionDialogViewModel, IDatabaseProvider> _fixedDialect = [];
    private readonly HashSet<DbConnectionDialogViewModel> _confirmed = [];
    private readonly List<string> _newFilePaths = [];
    private readonly List<(Guid Id, byte[] Bytes)> _corruptedSecrets = [];
    private readonly HashSet<Guid> _passwordSaveDeclined = [];
    private readonly List<Task> _tests = [];
    private SqlConnectionProfileStore? _store;
    private bool _dpapi;
    private int _errorsSeen;

    /// <summary>組を作る（5 方言を取込を偽物にした包みで用意する）</summary>
    public ConnectionDialogHarness()
    {
        SqlServer = new ConnectionDialogProvider(new SqlServerProvider());
        PostgreSql = new ConnectionDialogProvider(new PostgreSqlProvider());
        MySql = new ConnectionDialogProvider(new MySqlProvider());
        Oracle = new ConnectionDialogProvider(new OracleProvider());
        Sqlite = new ConnectionDialogProvider(new SqliteProvider());
    }

    /// <summary>SQL Server</summary>
    public ConnectionDialogProvider SqlServer { get; }

    /// <summary>PostgreSQL</summary>
    public ConnectionDialogProvider PostgreSql { get; }

    /// <summary>MySQL</summary>
    public ConnectionDialogProvider MySql { get; }

    /// <summary>Oracle</summary>
    public ConnectionDialogProvider Oracle { get; }

    /// <summary>SQLite</summary>
    public ConnectionDialogProvider Sqlite { get; }

    /// <summary>記録するダイアログ</summary>
    public StubDialogService Dialogs { get; } = new();

    /// <summary>偽のファイルダイアログ</summary>
    public ConnectionDialogFiles Files { get; } = new();

    /// <summary>行の一時フォルダの接続プロファイルのストア（最初に触ったときに作る。DPAPI は <see cref="UseDpapi"/> で先に選ぶ）</summary>
    public SqlConnectionProfileStore Store => TestStore;

    /// <summary>行の一時フォルダのストアの本体（次の読み取りの直前に connections.json を掴む手を持つ）</summary>
    private ConnectionDialogStore TestStore =>
        (ConnectionDialogStore)(_store ??= new ConnectionDialogStore(_folder.Path, _dpapi));

    /// <summary>
    /// 次の一覧の読み直しの直前に connections.json を掴む（書き込みは成功し、直後の読み直しだけが読めない状況を作る）。
    /// 掴んだファイルは <see cref="ReleaseReadLock"/> か行の終わりで放す
    /// </summary>
    public void LockConnectionsFileOnNextRead() => TestStore.LockOnNextRead();

    /// <summary>
    /// 同じ保存先を指す別のストア（ダイアログを開いた後に connections.json を書き換える、ほかのプロセス
    /// ＝別の QuickER のウィンドウの代役。VM のストアの掴む手の影響を受けない）
    /// </summary>
    public SqlConnectionProfileStore OtherProcessStore => new(_folder.Path, _dpapi);

    /// <summary>
    /// 次の一覧の読み直しの直前に connections.json を JSON として読めない内容で書き換える（書き込みは成功し、直後の読み直しだけが
    /// 壊れたファイルを読む状況＝書いた直後にほかのプロセスが壊した形を作る）
    /// </summary>
    public void CorruptConnectionsFileOnNextRead() => TestStore.CorruptOnNextRead();

    /// <summary><see cref="LockConnectionsFileOnNextRead"/> で掴んだ connections.json を放す</summary>
    public void ReleaseReadLock() => TestStore.ReleaseLock();

    /// <summary>書き込みの失敗の文言の固定部分（例外の文面より前）</summary>
    public static string WriteFailedPrefix =>
        Strings.DbConnection_ProfileWriteFailed.Split("{0}")[0];

    /// <summary>ストアを DPAPI で暗号化する（読めない暗号ファイルを作る行。ストアに触る前に呼ぶ）</summary>
    public void UseDpapi()
    {
        if (_store is not null)
        {
            throw new InvalidOperationException("ストアを作った後には DPAPI へ切り替えられない");
        }

        _dpapi = true;
    }

    /// <summary>一時フォルダの下のパス</summary>
    public string PathOf(params string[] parts) => Path.Combine([_folder.Path, .. parts]);

    /// <summary>方言の名前で包みを引く</summary>
    public ConnectionDialogProvider Dialect(string name) =>
        All.Single(provider => provider.Name == name);

    /// <summary>取込モード（既定）か同期モードの VM を作る（不変条件の見張りを付ける）</summary>
    public DbConnectionDialogViewModel Create(
        ConnectionDialects dialects = ConnectionDialects.SqlServerOnly,
        DbConnectionDialogMode mode = DbConnectionDialogMode.Import,
        IDatabaseProvider? fixedProvider = null,
        bool allowSqliteFileCreation = false
    )
    {
        var vm = new DbConnectionDialogViewModel(
            Registry(dialects),
            mode,
            fixedProvider,
            Store,
            Dialogs,
            Files,
            allowSqliteFileCreation
        );
        var closes = new List<bool>();
        _closes[vm] = closes;
        vm.CloseAction = result => OnClose(vm, closes, result);

        if (mode == DbConnectionDialogMode.Sync)
        {
            _fixedDialect[vm] = vm.SelectedProvider;
        }

        Watch(vm);
        CheckCorruptFileReportedOnOpen(vm);
        _vms.Add(vm);
        return vm;
    }

    /// <summary>SQLite を選んだ VM を作る（取込は新規作成なし・同期は SQLite 固定で新規作成の可否を選ぶ）</summary>
    public DbConnectionDialogViewModel CreateSqlite(
        bool allowFileCreation = false,
        DbConnectionDialogMode mode = DbConnectionDialogMode.Import
    )
    {
        var vm = Create(
            ConnectionDialects.WithSqlite,
            mode,
            mode == DbConnectionDialogMode.Sync ? Sqlite : null,
            allowFileCreation
        );
        vm.SelectedProvider = Sqlite;
        return vm;
    }

    /// <summary>VM が閉じた記録（true＝確定・false＝取り消し）</summary>
    public IReadOnlyList<bool> Closes(DbConnectionDialogViewModel vm) => _closes[vm];

    /// <summary>名前・接続先・パスワードを指定して SQL Server のプロファイルを 1 件ストアへ入れる（パスワードを保存する）</summary>
    public Guid SeedProfile(
        string name,
        string server = "prod.example.com",
        string password = "prod-secret"
    )
    {
        var profile = new SqlConnectionProfile
        {
            Name = name,
            Dbms = SqlServerProvider.ProviderName,
            Server = server,
            Database = "SalesDb",
            SavePassword = true,
        };
        Store.Upsert(profile, password);
        return profile.Id;
    }

    /// <summary>SQLite のプロファイルを 1 件ストアへ入れる（パスワードは持たない）</summary>
    public Guid SeedSqliteProfile(string name, string filePath)
    {
        var profile = new SqlConnectionProfile
        {
            Name = name,
            Dbms = SqliteProvider.ProviderName,
            FilePath = filePath,
        };
        Store.Upsert(profile, string.Empty);
        return profile.Id;
    }

    /// <summary>connections.json と暗号ファイル（暗号フォルダの位置にファイルがあればそれも）のバイト列の写し（手の前後で比べる）</summary>
    public string[] Snapshot()
    {
        var parts = new List<string>();

        if (File.Exists(Store.ConnectionsPath))
        {
            parts.Add(
                "connections:" + Convert.ToBase64String(File.ReadAllBytes(Store.ConnectionsPath))
            );
        }

        if (Directory.Exists(Store.SecretsFolder))
        {
            foreach (
                var file in Directory.GetFiles(Store.SecretsFolder).Order(StringComparer.Ordinal)
            )
            {
                parts.Add(
                    Path.GetFileName(file) + ":" + Convert.ToBase64String(File.ReadAllBytes(file))
                );
            }
        }
        else if (File.Exists(Store.SecretsFolder))
        {
            parts.Add("blocked:" + Convert.ToBase64String(File.ReadAllBytes(Store.SecretsFolder)));
        }

        return [.. parts];
    }

    /// <summary>プロファイルの暗号ファイルのパス</summary>
    public string SecretFile(Guid id) =>
        Path.Combine(Store.SecretsFolder, id.ToString("N") + ".dat");

    /// <summary>暗号ファイルを復号できないバイト列で置き換え、行の終わりに失われていないかを見張る（別ユーザー・別端末で暗号化されたものの代役）</summary>
    public byte[] CorruptSecret(Guid id)
    {
        var garbage = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        File.WriteAllBytes(SecretFile(id), garbage);
        _corruptedSecrets.Add((id, garbage));
        return garbage;
    }

    /// <summary>接続情報ファイルを排他で掴む（読み取りの中止を決定的に作る。ファイルがある状態で呼ぶ）</summary>
    public FileStream LockConnectionsFile() =>
        new(Store.ConnectionsPath, FileMode.Open, FileAccess.Read, FileShare.None);

    /// <summary>プロファイルの暗号ファイルを排他で掴む（暗号ファイルの削除を失敗させる）</summary>
    public FileStream LockSecret(Guid id) =>
        new(SecretFile(id), FileMode.Open, FileAccess.Read, FileShare.None);

    /// <summary>暗号ファイルのフォルダの位置にファイルを置き、暗号ファイルの書き込みを失敗させる</summary>
    public void BlockSecretsFolder() => File.WriteAllText(Store.SecretsFolder, "not a folder");

    /// <summary>プロファイルを選ぶ（null で選択を外す）</summary>
    public void Select(
        DbConnectionDialogViewModel vm,
        DbConnectionDialogViewModel.ProfileListItem? item
    )
    {
        var before = vm.SelectedProfileItem;
        vm.SelectedProfileItem = item;

        if (
            !ReferenceEquals(before, vm.SelectedProfileItem)
            && vm.SelectedProfile is { } selected
            && vm.ProfileName != selected.Name
        )
        {
            Violate(
                $"不変条件 13：選んだプロファイル {selected.Name} が入力欄に当たっていない（保存名の欄は {vm.ProfileName}）"
            );
        }

        After(vm, "選択");
    }

    /// <summary>保存のコマンドを実行する（不変条件 6・8 を手の前後で見る）</summary>
    public void Save(DbConnectionDialogViewModel vm)
    {
        var fields = Fields(vm);
        RecordDeclinedPasswordSave(vm);
        vm.SaveProfileCommand.Execute(null);
        CheckFieldsKept(vm, fields, "保存");
        CheckListMatchesDisk(vm, "保存");
        After(vm, "保存");
    }

    /// <summary>名前の変更のコマンドを実行する（実行できるときだけ。不変条件 6・8 を手の前後で見る）</summary>
    public void Rename(DbConnectionDialogViewModel vm)
    {
        var fields = Fields(vm);
        vm.RenameProfileCommand.Execute(null);
        CheckFieldsKept(vm, fields, "名前の変更");
        CheckListMatchesDisk(vm, "名前の変更");
        After(vm, "名前の変更");
    }

    /// <summary>削除のコマンドを実行する（選択中なら警告つきの確認が 1 回出ることと、不変条件 6 を見る）</summary>
    public void Delete(DbConnectionDialogViewModel vm)
    {
        var selected = vm.SelectedProfile is not null;
        var warnings = Dialogs.WarningConfirmMessages.Count;
        vm.DeleteProfileCommand.Execute(null);

        if (selected && Dialogs.WarningConfirmMessages.Count != warnings + 1)
        {
            Violate("不変条件 5：選択中のプロファイルの削除で警告つきの確認が出なかった");
        }

        CheckListMatchesDisk(vm, "削除");
        After(vm, "削除");
    }

    /// <summary>接続テストを最後まで実行する</summary>
    public async Task TestAsync(DbConnectionDialogViewModel vm)
    {
        await vm.TestConnectionCommand.ExecuteAsync(null);
        After(vm, "接続テスト");
    }

    /// <summary>接続テストを始める（止めた取込なら実行中のまま返る。行の終わりに組が門を開けて待つ）</summary>
    public Task StartTest(DbConnectionDialogViewModel vm)
    {
        var run = vm.TestConnectionCommand.ExecuteAsync(null);

        lock (_lock)
        {
            _tests.Add(run);
        }

        After(vm, "接続テストの開始");
        return run;
    }

    /// <summary>入力欄を変えるだけの手（コマンドは打たない。手の後の見張りは通す）</summary>
    public void Edit(DbConnectionDialogViewModel vm, Action<DbConnectionDialogViewModel> change)
    {
        change(vm);
        After(vm, "入力");
    }

    /// <summary>接続テストを門で止めて始め、取込が始まるまで待つ（行の本体で <see cref="RunningTest.FinishAsync"/> を呼んで終わらせてもよい）</summary>
    public async Task<RunningTest> BeginTest(
        DbConnectionDialogViewModel vm,
        ConnectionDialogProvider provider
    )
    {
        var gate = provider.Importer.Hold();
        var run = StartTest(vm);
        await gate.Started.Task.WaitAsync(TimeSpan.FromSeconds(30));
        return new RunningTest(gate, run);
    }

    /// <summary>参照（既存ファイル）のコマンドを実行する（実行できるときだけ＝画面でボタンが出ているときだけ押せるのと同じ）</summary>
    public void BrowseFile(DbConnectionDialogViewModel vm)
    {
        if (vm.BrowseFileCommand.CanExecute(null))
        {
            vm.BrowseFileCommand.Execute(null);
        }

        After(vm, "参照");
    }

    /// <summary>新規作成（保存ダイアログ）のコマンドを実行する（選んだパスを不変条件 10 で見張る）</summary>
    public void BrowseNewFile(DbConnectionDialogViewModel vm)
    {
        if (Files.SaveResult is { } picked)
        {
            _newFilePaths.Add(picked.Path);
        }

        vm.BrowseNewFileCommand.Execute(null);
        After(vm, "新規作成");
    }

    /// <summary>OK のコマンドを実行する（不変条件 1・2・3・11 を手の前後で見る）</summary>
    public void Ok(DbConnectionDialogViewModel vm)
    {
        var closes = _closes[vm].Count;
        var errors = Dialogs.ErrorMessages.Count;
        var result = vm.Result;

        vm.OkCommand.Execute(null);

        var closedNow = _closes[vm].Count > closes;

        if (closedNow)
        {
            _confirmed.Add(vm);
            CheckResultRange(vm.Result);

            if (_fixedDialect.TryGetValue(vm, out var fixedDialect))
            {
                if (!ReferenceEquals(vm.ResultProvider, fixedDialect))
                {
                    Violate(
                        $"不変条件 3：同期モードの確定の方言 {vm.ResultProvider?.Name} が固定した {fixedDialect.Name} と違う"
                    );
                }
            }
        }
        else if (!ReferenceEquals(vm.Result, result))
        {
            Violate("不変条件 1：閉じなかった OK が Result を変えた");
        }

        if (Dialogs.ErrorMessages.Count > errors && !closedNow)
        {
            Violate("不変条件 11：エラーのモーダルを出した OK が確定を止めた");
        }

        After(vm, "OK", errorsAllowed: true);
    }

    /// <summary>取り消しのコマンドを実行する</summary>
    public void Cancel(DbConnectionDialogViewModel vm)
    {
        var closes = _closes[vm].Count;
        vm.CancelCommand.Execute(null);

        if (_closes[vm].Count != closes + 1 || _closes[vm][^1])
        {
            Violate("不変条件 1：取り消しが取り消しとして閉じなかった");
        }

        After(vm, "取り消し");
    }

    /// <summary>行の本体の後に、止めた接続テストを終わらせ、見張った不変条件を表明する</summary>
    public async Task FinishAsync()
    {
        // 読み直しの直前に掴んだ connections.json を放してから、ディスクを読む見張りを掛ける
        (_store as ConnectionDialogStore)?.ReleaseLock();

        foreach (var gate in All.SelectMany(provider => provider.Importer.Gates))
        {
            gate.Release.TrySetResult();
        }

        List<Task> tests;

        lock (_lock)
        {
            tests = [.. _tests];
        }

        foreach (var test in tests)
        {
            await test.WaitAsync(TimeSpan.FromSeconds(60));
        }

        foreach (var vm in _vms)
        {
            if (vm.IsBusy)
            {
                Violate("不変条件 4：行の終わりに接続テストが実行中のまま");
            }

            CheckNoPlainConfirm();
            CheckUnique(vm, "行の終わり");
        }

        if (_vms.Count > 0)
        {
            // 前に作った VM は後の VM の保存を知らないので、最後に作った VM の一覧をディスクと比べる
            CheckListMatchesDisk(_vms[^1], "行の終わり");
        }

        CheckStoredProfilesInRange();
        CheckStoredProfilesUnique();
        CheckCorruptedSecretsKept();
        CheckCreatedFilesReleased("行の終わり");

        List<string> violations;

        lock (_lock)
        {
            violations = [.. _violations];
        }

        violations.Should().BeEmpty();
    }

    /// <summary>一時フォルダを消す（新規作成で作った SQLite のファイルは製品が掴まない＝不変条件 12）</summary>
    public void Dispose()
    {
        (_store as ConnectionDialogStore)?.ReleaseLock();
        _folder.Dispose();
    }

    /// <summary>方言の組のレジストリ</summary>
    private DatabaseProviderRegistry Registry(ConnectionDialects dialects) =>
        new(
            dialects switch
            {
                ConnectionDialects.SqlServerOnly => [SqlServer],
                ConnectionDialects.WithSqlite => [SqlServer, Sqlite],
                _ => All,
            }
        );

    /// <summary>5 方言（SQL Server を先頭に、レジストリの登録順）</summary>
    private ConnectionDialogProvider[] All => [SqlServer, PostgreSql, MySql, Oracle, Sqlite];

    /// <summary>不変条件 12：新規作成で作った SQLite のファイルを排他で開ける（製品の接続がファイルを掴んでいない）</summary>
    /// <remarks>
    /// 組は取込を偽物にしているので、このファイルを開くのは製品の <c>SqliteDatabaseFile.CreateEmpty</c> だけ。
    /// Windows では掴まれたファイルを排他で開けず、移動・削除と同じ答えになる（Linux の排他は SQLite のロックと関係しないので常に開ける）。
    /// </remarks>
    private void CheckCreatedFilesReleased(string when)
    {
        foreach (var path in _newFilePaths.Where(File.Exists))
        {
            try
            {
                using var exclusive = File.Open(
                    path,
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.None
                );
            }
            catch (IOException ex)
            {
                Violate(
                    $"不変条件 12：新規作成のファイル {path} を製品の接続が掴んだまま（{when}・{ex.Message}）"
                );
            }
        }
    }

    private void Violate(string message)
    {
        lock (_lock)
        {
            _violations.Add(message);
        }
    }

    /// <summary>VM の変更通知に見張りを付ける（不変条件 3・4）</summary>
    private void Watch(DbConnectionDialogViewModel vm)
    {
        vm.PropertyChanged += (_, e) => OnPropertyChanged(vm, e);
    }

    private void OnPropertyChanged(DbConnectionDialogViewModel vm, PropertyChangedEventArgs e)
    {
        if (
            e.PropertyName == nameof(DbConnectionDialogViewModel.SelectedProvider)
            && _fixedDialect.TryGetValue(vm, out var fixedDialect)
            && !ReferenceEquals(vm.SelectedProvider, fixedDialect)
        )
        {
            Violate(
                $"不変条件 3：同期モードで方言が {fixedDialect.Name} から {vm.SelectedProvider.Name} へ変わった"
            );
        }

        if (e.PropertyName == nameof(DbConnectionDialogViewModel.IsBusy))
        {
            CheckBusy(vm, "実行中の変更通知");
        }

        if (e.PropertyName == nameof(DbConnectionDialogViewModel.SelectedProfileItem))
        {
            CheckFixedDialectProfiles(vm, "選択の変更通知");
        }
    }

    /// <summary>不変条件 3（同期モードの一覧と選択中のプロファイルは固定した方言のものだけ）</summary>
    private void CheckFixedDialectProfiles(DbConnectionDialogViewModel vm, string when)
    {
        if (!_fixedDialect.TryGetValue(vm, out var fixedDialect))
        {
            return;
        }

        var others = vm
            .Profiles.Where(item => !IsDialect(item.Profile, fixedDialect))
            .Select(item => item.Display)
            .ToList();

        if (others.Count > 0)
        {
            Violate(
                $"不変条件 3：同期モード（{fixedDialect.Name}）の一覧に別の方言のプロファイル {string.Join("・", others)} が並んだ（{when}）"
            );
        }

        if (vm.SelectedProfile is { } selected && !IsDialect(selected, fixedDialect))
        {
            Violate(
                $"不変条件 3：同期モード（{fixedDialect.Name}）で別の方言のプロファイル {selected.Name} が選ばれている（{when}）"
            );
        }
    }

    /// <summary>プロファイルが方言のものか（大文字小文字は無視）</summary>
    private static bool IsDialect(SqlConnectionProfile profile, IDatabaseProvider dialect) =>
        string.Equals(profile.Dbms, dialect.Name, StringComparison.OrdinalIgnoreCase);

    /// <summary>閉じた記録（不変条件 1）</summary>
    private void OnClose(DbConnectionDialogViewModel vm, List<bool> closes, bool result)
    {
        closes.Add(result);

        if (result && (vm.Result is null || vm.ResultProvider is null))
        {
            Violate("不変条件 1：Result か ResultProvider が空のまま確定して閉じた");
        }

        if (!result && (vm.Result is not null || vm.ResultProvider is not null))
        {
            Violate("不変条件 1：Result か ResultProvider を残したまま取り消して閉じた");
        }
    }

    /// <summary>どの手の後にも見る不変条件（3・4・5・9・10・11・15）</summary>
    private void After(DbConnectionDialogViewModel vm, string op, bool errorsAllowed = false)
    {
        if (_fixedDialect.ContainsKey(vm) && vm.CanSelectDbms)
        {
            Violate($"不変条件 3：同期モードで DBMS を選べる（{op}の後）");
        }

        CheckFixedDialectProfiles(vm, $"{op}の後");

        CheckBusy(vm, op);
        CheckNoPlainConfirm();
        CheckUnique(vm, op);
        CheckFileButtons(vm, op);

        if (!_confirmed.Contains(vm))
        {
            foreach (var path in _newFilePaths.Where(File.Exists))
            {
                Violate(
                    $"不変条件 10：OK で確定する前に新規作成のファイル {path} ができた（{op}の後）"
                );
            }
        }

        CheckCreatedFilesReleased(op);

        if (!errorsAllowed && Dialogs.ErrorMessages.Count > _errorsSeen)
        {
            Violate($"不変条件 11：OK 以外の手（{op}）がエラーのモーダルを出した");
        }

        _errorsSeen = Dialogs.ErrorMessages.Count;
    }

    private void CheckBusy(DbConnectionDialogViewModel vm, string when)
    {
        if (vm.IsBusy && vm.OkCommand.CanExecute(null))
        {
            Violate($"不変条件 4：接続テストの実行中に OK で確定できる（{when}）");
        }
    }

    /// <summary>不変条件 15（参照・新規作成のコマンドは、画面でボタンが出ているときだけ実行できる）</summary>
    private void CheckFileButtons(DbConnectionDialogViewModel vm, string when)
    {
        if (vm.BrowseFileCommand.CanExecute(null) != vm.ShowFilePath)
        {
            Violate(
                $"不変条件 15：参照のコマンドの実行可否 {vm.BrowseFileCommand.CanExecute(null)} がボタンの表示 {vm.ShowFilePath} と違う（{when}の後）"
            );
        }

        if (vm.BrowseNewFileCommand.CanExecute(null) != vm.ShowCreateNewFile)
        {
            Violate(
                $"不変条件 15：新規作成のコマンドの実行可否 {vm.BrowseNewFileCommand.CanExecute(null)} がボタンの表示 {vm.ShowCreateNewFile} と違う（{when}の後）"
            );
        }
    }

    private void CheckNoPlainConfirm()
    {
        if (Dialogs.ConfirmMessages.Count > 0 || Dialogs.WarningConfirmDetailsMessages.Count > 0)
        {
            Violate("不変条件 5：警告つきでない確認・詳細つきの確認を出した");
        }
    }

    private void CheckUnique(DbConnectionDialogViewModel vm, string when)
    {
        var duplicated = vm
            .Profiles.GroupBy(item =>
                (item.Profile.Dbms.ToUpperInvariant(), item.Profile.Name.ToUpperInvariant())
            )
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        if (duplicated.Count > 0)
        {
            Violate($"不変条件 9：一覧に同じ（DBMS, 名前）のプロファイルが並んだ（{when}）");
        }
    }

    /// <summary>壊れた connections.json を知らせるステータス行の文（行のストアのパスで組み立てる）</summary>
    public string ProfilesCorruptMessage =>
        string.Format(
            Strings.DbConnection_ProfilesCorrupt,
            Store.ConnectionsPath,
            Store.CorruptBackupPath
        );

    /// <summary>不変条件 14（開いた直後、壊れた connections.json を空の一覧として黙って出さない）</summary>
    /// <remarks>分類はほかのプロセスの代役のストアで読む（行のストアの次の読み取りに掛けた手を消費しない）。</remarks>
    private void CheckCorruptFileReportedOnOpen(DbConnectionDialogViewModel vm)
    {
        if (OtherProcessStore.Read().Status != ConnectionFileStatus.Corrupt)
        {
            return;
        }

        if (vm.Profiles.Count > 0 || vm.SelectedProfileItem is not null)
        {
            Violate("不変条件 14：壊れた connections.json で開いたのに一覧か選択がある");
        }

        if (vm.StatusMessage != ProfilesCorruptMessage)
        {
            Violate(
                $"不変条件 14：壊れた connections.json で開いたのに、ステータス行が壊れていることを知らせていない（{vm.StatusMessage}）"
            );
        }
    }

    /// <summary>不変条件 6（一覧はディスクの実体どおり。ストアが読めないときは行の終わりに見る）</summary>
    private void CheckListMatchesDisk(DbConnectionDialogViewModel vm, string when)
    {
        // 一覧を読めなかったと VM が言っている間は、ディスクと比べずに「空の一覧で、ステータス行が知らせている」ことを見る
        if (vm.AreProfilesUnavailable)
        {
            if (vm.Profiles.Count > 0 || vm.SelectedProfileItem is not null)
            {
                Violate($"不変条件 6：一覧を読めなかったのに一覧か選択が残っている（{when}）");
            }

            if (
                vm.StatusMessage != Strings.DbConnection_ProfilesUnavailable
                && vm.StatusMessage != Strings.DbConnection_ProfileStoreUnavailable
            )
            {
                Violate(
                    $"不変条件 6：一覧を読めなかったことをステータス行が知らせていない（{when}・{vm.StatusMessage}）"
                );
            }

            return;
        }

        // 掴まれて読めない間はストアが空の一覧を返すので、読めるようになってから（行の終わりに）見る
        if (AtomicFile.TryReadAllText(Store.ConnectionsPath, out _) == FileReadStatus.Unavailable)
        {
            return;
        }

        // 同期モードの一覧は固定した方言の分だけ（不変条件 3）なので、ディスクも同じ方言の分と比べる
        var disk = Store
            .Read()
            .Profiles.Where(profile =>
                !_fixedDialect.TryGetValue(vm, out var fixedDialect)
                || IsDialect(profile, fixedDialect)
            );

        // Id を持たない旧形式の要素は読むたびに新しい Id になるので、Id でなく中身（DBMS・名前・接続先）の多重集合で比べる
        var shown = vm.Profiles.Select(item => ProfileKey(item.Profile)).Order().ToList();
        var stored = disk.Select(ProfileKey).Order().ToList();

        if (!shown.SequenceEqual(stored))
        {
            Violate($"不変条件 6：一覧がディスクの実体と違う（{when}）");
        }
    }

    /// <summary>一覧とディスクを比べる鍵</summary>
    private static string ProfileKey(SqlConnectionProfile profile) =>
        string.Join(
            "\u0001",
            profile.Dbms,
            profile.Name,
            profile.Server,
            profile.Database,
            profile.FilePath
        );

    /// <summary>不変条件 2（確定の範囲）</summary>
    private void CheckResultRange(DbConnectionSettings? result)
    {
        if (result is null)
        {
            return;
        }

        if (result.CommandTimeoutSeconds < 0 || result.Port is < 1 or > 65535)
        {
            Violate(
                $"不変条件 2：範囲の外の値で確定した（タイムアウト {result.CommandTimeoutSeconds}・ポート {result.Port}）"
            );
        }
    }

    /// <summary>不変条件 2（保存したプロファイルの範囲）</summary>
    private void CheckStoredProfilesInRange()
    {
        foreach (var profile in Store.Read().Profiles)
        {
            if (profile.CommandTimeoutSeconds < 0 || profile.Port is < 1 or > 65535)
            {
                Violate($"不変条件 2：範囲の外の値のプロファイル {profile.Name} が保存された");
            }
        }
    }

    /// <summary>不変条件 9（行の終わりのディスクのプロファイルも（DBMS, 名前）で一意）</summary>
    /// <remarks>
    /// 一覧だけを見ると、一覧に出ていない別の方言のプロファイルや、読めずに空で開いた一覧の外の同名が見えない。
    /// 画面に出ていないプロファイルへの名前の変更・保存が同じ名前を 2 つ作ったら、ここで捕まえる。
    /// </remarks>
    private void CheckStoredProfilesUnique()
    {
        var duplicated = Store
            .Read()
            .Profiles.GroupBy(profile =>
                (profile.Dbms.ToUpperInvariant(), profile.Name.ToUpperInvariant())
            )
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key.Item1}/{group.Key.Item2}")
            .ToList();

        if (duplicated.Count > 0)
        {
            Violate(
                $"不変条件 9：ディスクに同じ（DBMS, 名前）のプロファイルが並んだ（{string.Join("・", duplicated)}）"
            );
        }
    }

    /// <summary>
    /// 不変条件 7 の例外の記録：「パスワードを保存」を外したまま保存する手が上書きする、読めない暗号ファイルのプロファイルの Id
    /// </summary>
    /// <remarks>
    /// 利用者が保存をやめた操作なので、暗号ファイルを消すのは文書どおり（docs/database.ja.md の読めないパスワードの段落・
    /// 断りの文 DbConnection_SaveRefusedPasswordUnreadable）。手の前の VM の入力（チェックの状態・方言・保存名）で決め、
    /// 保存後のディスクの SavePassword は見ない（空欄のまま「保存」付きで上書きしても、ストアの正規化が SavePassword を偽へ倒すため）。
    /// </remarks>
    private void RecordDeclinedPasswordSave(DbConnectionDialogViewModel vm)
    {
        if (vm.SavePassword || string.IsNullOrWhiteSpace(vm.ProfileName))
        {
            return;
        }

        var name = vm.ProfileName.Trim();

        // 上書き先は（DBMS, 名前）の大文字小文字を無視した一致（製品の SqlConnectionProfile.HasEntryKey は internal）
        foreach (var item in vm.Profiles)
        {
            if (
                string.Equals(
                    item.Profile.Dbms,
                    vm.SelectedProvider.Name,
                    StringComparison.OrdinalIgnoreCase
                )
                && string.Equals(item.Profile.Name, name, StringComparison.OrdinalIgnoreCase)
                && _corruptedSecrets.Any(secret => secret.Id == item.Profile.Id)
            )
            {
                _passwordSaveDeclined.Add(item.Profile.Id);
            }
        }
    }

    /// <summary>不変条件 7（読めない暗号ファイルを失わない。利用者が「パスワードを保存」を外して保存したプロファイルを除く）</summary>
    private void CheckCorruptedSecretsKept()
    {
        var profiles = Store.Read().Profiles;

        foreach (var (id, bytes) in _corruptedSecrets)
        {
            if (!profiles.Any(profile => profile.Id == id) || _passwordSaveDeclined.Contains(id))
            {
                continue;
            }

            var path = SecretFile(id);

            if (File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(bytes))
            {
                continue;
            }

            var reentered = Store.LoadPassword(id);

            if (reentered.Status != SavedPasswordStatus.Loaded || reentered.Value.Length == 0)
            {
                Violate($"不変条件 7：読めない暗号ファイル {id:N} が入力し直しなしに失われた");
            }
        }
    }

    /// <summary>保存・名前の変更が塗り直してはいけない入力欄</summary>
    private static object[] Fields(DbConnectionDialogViewModel vm) =>
        [
            vm.SelectedProvider,
            vm.Host,
            vm.Port,
            vm.Database,
            vm.AuthMode,
            vm.UserId,
            vm.Password,
            vm.TrustServerCertificate,
            vm.SslMode,
            vm.ServiceName,
            vm.FilePath,
            vm.CommandTimeout,
            vm.SavePassword,
        ];

    private void CheckFieldsKept(DbConnectionDialogViewModel vm, object[] before, string op)
    {
        if (!Fields(vm).SequenceEqual(before))
        {
            Violate($"不変条件 8：{op}の直後に入力欄が塗り直された");
        }
    }
}
