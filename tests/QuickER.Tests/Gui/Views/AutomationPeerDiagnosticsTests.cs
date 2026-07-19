using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using QuickER.Converters;
using QuickER.Documents;
using QuickER.Model;
using QuickER.Tests.TestSupport;
using QuickER.ViewModels;
using Xunit;

namespace QuickER.Tests.Gui.Views;

/// <summary>
/// 大きい図で UI オートメーションツリーの要素数が爆発しないことを守る回帰ガード。
/// 常駐 UIA クライアント（スクリーンリーダー・PowerToys 等）は最初のポップアップ表示を契機に
/// ツリーを走査し、その応答はアプリの UI スレッドで処理されるため、要素数が多いと数秒固まる
/// （実測: カード内部露出時 50×40 図で約 12,000 要素・クロスプロセス走査 6 秒）。
/// <see cref="QuickER.Views.EntityCardBorder"/> がカード内部を単一ピアへ畳むことで抑えている。
/// </summary>
public class AutomationPeerDiagnosticsTests
{
    /// <summary>50×40 図で許容する UIA ピア数の上限（現状実測値の約 2 倍を閾値とする）</summary>
    private const int HeavyDiagramPeerBudget = 2000;

    private readonly ITestOutputHelper _output;

    public AutomationPeerDiagnosticsTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>重量級図（50 テーブル×40 カラム・説明表示 ON）の UIA ピア数が予算内に収まることを検証する</summary>
    [Fact(DisplayName = "UIA ピア数: 50x40 重量級図でも予算内（カード内部を露出しない）")]
    public void HeavyDiagram_PeerCount_StaysWithinBudget()
    {
        Exception? captured = null;
        var results = new System.Collections.Generic.List<string>();
        var heavyCount = 0;

        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplicationResources();

                var (emptyLine, _) = MeasureScenario(entityCount: 0, columnCount: 0, label: "空図");
                results.Add(emptyLine);

                var (heavyLine, count) = MeasureScenario(
                    entityCount: 50,
                    columnCount: 40,
                    label: "50テーブル×40カラム"
                );
                results.Add(heavyLine);
                heavyCount = count;
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();

        foreach (var line in results)
        {
            _output.WriteLine(line);
        }

        Assert.Null(captured);

        // カード内部（カラム行の TextBlock 群）が UIA へ露出すると 1 万超に爆発し、
        // 常駐 UIA クライアントの走査で初回ドロップダウンが数秒固まる退行となる
        Assert.InRange(heavyCount, 1, HeavyDiagramPeerBudget);
    }

    /// <summary>指定規模の図を実ウィンドウへ表示し、ピアツリーの構築時間と総ピア数を計測する</summary>
    private static (string Line, int Count) MeasureScenario(
        int entityCount,
        int columnCount,
        string label
    )
    {
        var vm = new MainViewModel();
        var window = new MainWindow(vm);

        // MainWindow ctor の Initialize() が自動保存を復元するため、計測条件を上書きする
        vm.Relationships.Clear();
        vm.Entities.Clear();
        vm.ShowColumnDescriptionsInDiagram = true;

        for (var t = 0; t < entityCount; t++)
        {
            var model = new Entity
            {
                TableName = $"BusinessTable_{t}",
                Description = $"業務テーブル {t}",
            };

            for (var c = 0; c < columnCount; c++)
            {
                model.Columns.Add(
                    new Column
                    {
                        Name = $"ColumnName_{c}",
                        DataType = "nvarchar(200)",
                        IsPrimaryKey = c == 0,
                        IsNullable = c != 0,
                        Description = $"このカラムの業務上の説明テキスト {c}",
                    }
                );
            }

            var layout = new EntityLayout
            {
                X = 60 + (t % 8) * 320,
                Y = 60 + (t / 8) * 1400,
                Width = 280,
            };
            vm.Entities.Add(new EntityViewModel(model, layout));
        }

        window.Show();
        window.UpdateLayout();
        DoEvents();

        // UIA クライアントの初回アクセスを模擬: ルートからピアツリーを全走査（構築）する
        var sw1 = Stopwatch.StartNew();
        var peer = UIElementAutomationPeer.CreatePeerForElement(window);
        var count = WalkPeers(peer);
        sw1.Stop();

        // 2 回目の走査（キャッシュ済み）との差が「初回だけ固まる」現象の説明になる
        var sw2 = Stopwatch.StartNew();
        WalkPeers(peer);
        sw2.Stop();

        window.Close();
        DoEvents();

        return (
            $"{label}: 初回={sw1.ElapsedMilliseconds}ms 2回目={sw2.ElapsedMilliseconds}ms ピア数={count}",
            count
        );
    }

    /// <summary>ピアツリーを再帰走査し、訪問したピア数を返す</summary>
    private static int WalkPeers(AutomationPeer? peer)
    {
        if (peer is null)
        {
            return 0;
        }

        var count = 1;
        var children = peer.GetChildren();

        if (children is not null)
        {
            foreach (var child in children)
            {
                count += WalkPeers(child);
            }
        }

        return count;
    }

    /// <summary>MainWindow の BAML が参照する App レベルリソースを供給する</summary>
    /// <remarks>生成は共有ヘルパーで直列化し、並列テストとの Application 二重生成競合を防ぐ</remarks>
    private static void EnsureApplicationResources() =>
        WpfApplicationTestSupport.EnsureApplicationResources();

    /// <summary>保留中のディスパッチャ処理（レイアウト・描画）を流し切る</summary>
    private static void DoEvents()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false)
        );
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
}
