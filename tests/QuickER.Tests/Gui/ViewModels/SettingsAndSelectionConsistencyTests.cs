using System.Globalization;
using System.IO;
using AwesomeAssertions;
using QuickER.Gui.Abstractions;
using QuickER.Model;
using QuickER.Services;
using QuickER.Tests.TestDoubles;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// 設定メニューの小さな ViewModel の保存先・言語の選び直しと、Ctrl+クリックとリレーションの作成モードの関係を
/// 固定するテストクラス。
/// </summary>
/// <remarks>既知の失敗は台帳（<see cref="Known"/>）に載せる。突き合わせの規則は <see cref="KnownCaseLedger"/>。</remarks>
public class SettingsAndSelectionConsistencyTests
{
    /// <summary>既知の失敗（ケース ID → 期待する失敗の理由の断片）</summary>
    private static readonly KnownCaseLedger Known = new("SettingsAndSelectionConsistencyTests", []);

    /// <summary>再起動サービスのスタブ（呼び出し回数だけ記録する）</summary>
    private sealed class FakeRestartService : IApplicationRestartService
    {
        public int RestartCount { get; private set; }

        public void Restart() => RestartCount++;
    }

    /// <summary>一時フォルダの設定ストアを作る</summary>
    private static GuiAppSettingsStore NewStore() =>
        new(Path.Combine(Path.GetTempPath(), "QuickERTest_" + Guid.NewGuid().ToString("N")));

    [Fact(DisplayName = "保存先を差し替えると、言語と更新確認の設定も差し替えた保存先を使う")]
    public void UsePersistenceForTests_RedirectsSettingsSubViewModels()
    {
        Known.Run(
            "settings-store-redirect",
            () =>
            {
                var store = NewStore();
                var vm = new MainViewModel();

                vm.UsePersistenceForTests(
                    store,
                    Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")
                );

                vm.LanguageSwitch.StoreForTests.Should().BeSameAs(store, "差し替えた保存先を使う");
                vm.UpdateSettings.StoreForTests.Should().BeSameAs(store, "差し替えた保存先を使う");
            }
        );
    }

    [Fact(
        DisplayName = "別の言語を選んで再起動を断ったあと、表示中の言語へ戻すだけなら再起動を尋ねない"
    )]
    public void SelectLanguage_BackToDisplayedLanguage_DoesNotAskRestart()
    {
        Known.Run(
            "language-back-to-displayed",
            () =>
            {
                var original = CultureInfo.CurrentUICulture;

                try
                {
                    CultureInfo.CurrentUICulture = new CultureInfo("ja-JP");
                    var store = NewStore();
                    store.Save(new GuiAppSettings { Language = "ja" });
                    var dialogs = new StubDialogService { ConfirmResult = false };
                    var restart = new FakeRestartService();
                    var vm = new LanguageSwitchViewModel(dialogs, store, restart);

                    vm.SelectLanguageCommand.Execute("en");

                    vm.CurrentLanguage.Should().Be("en", "次の起動で使う言語を示す");
                    dialogs.ConfirmMessages.Should().ContainSingle();

                    vm.SelectLanguageCommand.Execute("ja");

                    store.Load().Language.Should().Be("ja");
                    vm.CurrentLanguage.Should().Be("ja");
                    dialogs
                        .ConfirmMessages.Should()
                        .ContainSingle("表示中の言語へ戻すだけなら再起動を尋ねない");
                    restart.RestartCount.Should().Be(0);
                }
                finally
                {
                    CultureInfo.CurrentUICulture = original;
                }
            }
        );
    }

    [Fact(DisplayName = "リレーションの作成中に Ctrl+クリックで複数選択すると、作成を取りやめる")]
    public void ToggleSelection_CancelsPendingRelationship()
    {
        Known.Run(
            "ctrl-click-cancels-relationship",
            () =>
            {
                var vm = new MainViewModel();
                vm.ReplaceDiagramFromModule(
                    new ErDiagram
                    {
                        Entities =
                        {
                            new Entity { TableName = "A" },
                            new Entity { TableName = "B" },
                            new Entity { TableName = "C" },
                        },
                    }
                );
                vm.StartAddOneToManyCommand.Execute(null);
                vm.OnEntityClicked(vm.Entities[0]);

                vm.ToggleEntitySelection(vm.Entities[1]);
                vm.ToggleEntitySelection(vm.Entities[2]);

                vm.IsRelationshipMode.Should()
                    .BeFalse("Ctrl+クリックでリレーションの作成を取りやめる");
                vm.PendingRelationshipSource.Should().BeNull();

                // 続く普通のクリックはリレーションを作らず、そのテーブルを選ぶだけ
                vm.OnEntityClicked(vm.Entities[1]);

                vm.Relationships.Should().BeEmpty();
                vm.SelectedEntity.Should().BeSameAs(vm.Entities[1]);
            }
        );
    }
}
