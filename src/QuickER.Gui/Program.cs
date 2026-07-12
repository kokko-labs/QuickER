using System;
using Velopack;

namespace QuickER
{
    /// <summary>
    /// アプリケーションのエントリポイント。Velopack のインストール/更新フックを
    /// WPF 初期化より前に処理してから <see cref="App"/> を起動する。
    /// </summary>
    /// <remarks>
    /// 既定の App.xaml 由来 <c>Main</c> は使わず（csproj で
    /// <c>EnableDefaultApplicationDefinition=false</c>）、ここを <c>StartupObject</c> に指定する。
    /// <see cref="VelopackApp.Build"/>().Run() はインストール直後・更新適用時の各フックを処理し、
    /// 通常起動時はそのまま制御を返す（Velopack 公式手順）。
    /// </remarks>
    internal static class Program
    {
        /// <summary>プロセスのエントリポイント（WPF アプリより前に Velopack を初期化する）</summary>
        [STAThread]
        private static void Main(string[] args)
        {
            // インストール/更新フックを WPF 初期化より前に処理する（Velopack 公式手順）。
            // 未インストール実行時は何もせずそのまま返る。
            VelopackApp.Build().Run();

            App app = new();
            app.InitializeComponent();
            app.Run();
        }
    }
}
