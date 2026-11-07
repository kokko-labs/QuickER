using System.IO;
using System.Linq;
using AwesomeAssertions;
using QuickER.Db.UI;
using QuickER.Db.UI.Resources;
using QuickER.Gui.Abstractions;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
using QuickER.Tests.Resources;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Db.UI;

/// <summary>
/// <see cref="DbConnectionDialogViewModel"/> の選択肢の表示名（resx と列挙の宣言の照合）と、名前の変更が 1 回の読み書きで済むことのソースの検査
/// </summary>
/// <remarks>
/// 操作の列（開く・選択・保存・名前の変更・削除・接続テスト・OK・取り消し）は接続ダイアログの表（<c>ConnectionDialogTests</c>）が持つ。
/// ここに残るのは状態を持たない宣言の照合 2 本と、挙動では作れない並びをソースで見張る 1 本。
/// </remarks>
public class DbConnectionDialogViewModelTests : IDisposable
{
    /// <summary>テスト用の一時保存先フォルダ（破棄で中身ごと消す）</summary>
    private readonly TestTempFolder _temp = new();

    /// <summary>SQL Server のみを登録したレジストリ</summary>
    private static readonly DatabaseProviderRegistry Registry = new(
        new IDatabaseProvider[] { new SqlServerProvider() }
    );

    /// <summary>全方言を登録したレジストリ（方言ごとの条件表示の検証用）</summary>
    private static readonly DatabaseProviderRegistry RegistryWithAllDialects = new(
        new IDatabaseProvider[]
        {
            new SqlServerProvider(),
            new PostgreSqlProvider(),
            new MySqlProvider(),
            new OracleProvider(),
            new SqliteProvider(),
        }
    );

    /// <summary>一時保存先フォルダを消す</summary>
    public void Dispose() => _temp.Dispose();

    /// <summary>DPAPI を使わず一時フォルダへ保存するテスト用ストアを生成する</summary>
    private SqlConnectionProfileStore CreateStore() => new(_temp.Path, useDpapi: false);

    /// <summary>取込モードの ViewModel を生成する</summary>
    private DbConnectionDialogViewModel CreateVm(
        SqlConnectionProfileStore store,
        IDialogService? dialogs = null
    ) => new(Registry, DbConnectionDialogMode.Import, fixedProvider: null, store, dialogs);

    /// <summary>全方言を登録した取込モードの ViewModel を生成する</summary>
    private DbConnectionDialogViewModel CreateAllDialectVm(SqlConnectionProfileStore store) =>
        new(RegistryWithAllDialects, DbConnectionDialogMode.Import, fixedProvider: null, store);

    /// <summary>
    /// TLS 要求水準の選択肢が、全 6 値をローカライズ済みの表示名付きで並べることを検証する。
    /// </summary>
    /// <remarks>
    /// 表示名が列挙名そのままだと <c>Require</c>（証明書を検証しない）と <c>VerifyCa</c> の差が
    /// 画面から読み取れない——この設定の存在理由が伝わらないため、resx 由来であることを固定する。
    /// </remarks>
    [Fact(DisplayName = "TLS 要求水準の選択肢は全 6 値をローカライズ済み表示名で並べる")]
    public void SslModes_ExposeLocalizedDisplayNames()
    {
        var vm = CreateAllDialectVm(CreateStore());

        vm.SslModes.Select(i => i.Mode).Should().Equal(Enum.GetValues<DbSslMode>());
        vm.SslModes.Should().OnlyContain(i => !string.IsNullOrWhiteSpace(i.Display));
        vm.SslModes.Single(i => i.Mode == DbSslMode.VerifyFull)
            .Display.Should()
            .Be(Strings.DbConnection_SslMode_VerifyFull);
        // 生の列挙名そのままではない＝意味が読める表示になっている
        vm.SslModes.Single(i => i.Mode == DbSslMode.Require)
            .Display.Should()
            .NotBe(nameof(DbSslMode.Require));
    }

    /// <summary>
    /// 認証方式の選択肢が、全値を resx の表示名つきで並べることを検証する（値を足して表示名を忘れると赤）。
    /// </summary>
    [Fact(DisplayName = "認証方式の選択肢は全値を resx の表示名で並べる")]
    public void AuthModes_ExposeLocalizedDisplayNames()
    {
        var vm = CreateVm(CreateStore());

        vm.AuthModes.Select(i => i.Mode).Should().Equal(Enum.GetValues<DbAuthMode>());
        vm.AuthModes.Should().OnlyContain(i => !string.IsNullOrWhiteSpace(i.Display));
        vm.AuthModes.Single(i => i.Mode == DbAuthMode.Windows)
            .Display.Should()
            .Be(Strings.DbConnection_AuthMode_Windows);
        vm.AuthModes.Single(i => i.Mode == DbAuthMode.UsernamePassword)
            .Display.Should()
            .Be(Strings.DbConnection_AuthMode_UsernamePassword);
        vm.AuthModes.Single(i => i.Mode == DbAuthMode.EntraId)
            .Display.Should()
            .Be(Strings.DbConnection_AuthMode_EntraId);
        // 列挙名そのままではない（Windows は表示名と列挙名が同じ綴りなので見ない）
        vm.AuthModes.Single(i => i.Mode == DbAuthMode.UsernamePassword)
            .Display.Should()
            .NotBe(nameof(DbAuthMode.UsernamePassword));
        vm.AuthModes.Single(i => i.Mode == DbAuthMode.EntraId)
            .Display.Should()
            .NotBe(nameof(DbAuthMode.EntraId));
    }

    /// <summary>名前の変更が 1 回の読み書きで済んでいることをソース上で固定する</summary>
    /// <remarks>
    /// <para>
    /// 「置き換え先の削除」と「対象の改名」を別々に呼ぶと 2 回の読み書きになり、2 回目が読めなかったときに
    /// <b>置き換え先だけが消えて対象は変わらない</b>状態が残る。
    /// </para>
    /// <para>
    /// <b>この退行は挙動テストでは捕まえられない。</b>読み取りを失敗させる手段が接続情報ファイルのロックしかなく、
    /// ロックすると 1 回目（削除）も失敗してしまうため、「1 回目は成功して 2 回目が失敗する」状況を作れない
    /// （store に差し替えの口を足せば作れるが、テストのためだけに本番の面を広げることになる）。
    /// 呼び出しの形をソースで見張る。
    /// </para>
    /// </remarks>
    [Fact(DisplayName = "RenameProfile: 置き換えと改名を 1 回の読み書きで行う")]
    public void RenameProfile_UsesASingleStoreWrite()
    {
        var source = File.ReadAllText(
            Path.Combine(
                NeutralResxFiles.FindRepositoryRoot(),
                "src",
                "QuickER.Db.UI",
                "DbConnectionDialogViewModel.cs"
            )
        );
        var start = source.IndexOf("private void RenameProfile()", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0);

        var end = source.IndexOf("private void DeleteProfile()", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);

        var body = source[start..end];

        // 置き換え先（一覧で見つけた同名か、ストアが衝突で断った後に確認したもの）は同じ 1 回の呼び出しへ渡す。
        // 衝突で断られた呼び出しは何も書かないので、書き直しの 2 回目も「置き換えと改名を 1 回で」のまま
        body.Should().Contain("_store.Rename(target.Id, newName, replacedId)");
        body.Should()
            .NotContain(
                "LoadPassword",
                "名前の変更でパスワードを読み戻して書き戻すと、読めなかったものが消える"
            );
        body.Should().NotContain("_store.Delete(", "削除と改名を別々に呼ぶと 2 回の読み書きになる");
    }
}
