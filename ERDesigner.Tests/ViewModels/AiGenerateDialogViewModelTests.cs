using System.Threading;
using System.Threading.Tasks;
using ERDesigner.Models;
using ERDesigner.Services;
using ERDesigner.ViewModels;
using FluentAssertions;

namespace ERDesigner.Tests.ViewModels;

/// <summary><see cref="AiGenerateDialogViewModel"/> の生成設定構築と API キー保存挙動を検証するテストクラス</summary>
public class AiGenerateDialogViewModelTests
{
    /// <summary>渡された生成設定を記録するだけのスキーマクライアントのスタブ</summary>
    private sealed class FakeAiSchemaClient : IAiSchemaClient
    {
        /// <summary>最後に GenerateAsync へ渡された設定</summary>
        public AiGenerationSettings? LastSettings { get; private set; }

        /// <summary>設定を記録し、空のスキーマを返す</summary>
        public Task<AiSchemaJson> GenerateAsync(AiGenerationSettings settings, CancellationToken ct = default)
        {
            LastSettings = settings;

            return Task.FromResult(new AiSchemaJson());
        }
    }

    /// <summary>SaveApiKey=true で生成すると API キーが保存されることを検証する</summary>
    [Fact(DisplayName = "SaveApiKey=true のとき Generate で API キーが保存される")]
    public async Task Generate_WithSaveApiKey_PersistsKey()
    {
        var keyName = "OpenAiApiKey";
        var vm = new AiGenerateDialogViewModel(new FakeAiSchemaClient())
        {
            Provider = AiProvider.OpenAI,
            SaveApiKey = true,
            ApiKey = "sk-vm-test-save",
            Prompt = "test",
        };

        try
        {
            await vm.GenerateCommand.ExecuteAsync(null);
            ApiKeyStore.Load(keyName).Should().Be("sk-vm-test-save");
        }
        finally
        {
            ApiKeyStore.Save(keyName, string.Empty);
        }
    }

    /// <summary>SaveApiKey=false で生成すると保存済み API キーが削除されることを検証する</summary>
    [Fact(DisplayName = "SaveApiKey=false のとき Generate で API キーが削除される")]
    public async Task Generate_WithoutSaveApiKey_DeletesKey()
    {
        var keyName = "OpenAiApiKey";
        ApiKeyStore.Save(keyName, "sk-to-delete");

        var vm = new AiGenerateDialogViewModel(new FakeAiSchemaClient())
        {
            Provider = AiProvider.OpenAI,
            SaveApiKey = false,
            ApiKey = "sk-vm-test-nosave",
            Prompt = "test",
        };

        try
        {
            await vm.GenerateCommand.ExecuteAsync(null);
            ApiKeyStore.Load(keyName).Should().BeEmpty();
        }
        finally
        {
            ApiKeyStore.Save(keyName, string.Empty);
        }
    }

    /// <summary>選択した識別子命名規則が生成設定へ渡されることを検証する</summary>
    [Fact(DisplayName = "選択した命名規則が生成設定へ渡される")]
    public async Task Generate_PassesSelectedIdentifierNamingStyle()
    {
        var client = new FakeAiSchemaClient();
        var vm = new AiGenerateDialogViewModel(client)
        {
            Provider = AiProvider.OpenAI,
            SaveApiKey = false,
            ApiKey = "sk-vm-test-style",
            Prompt = "test",
            SelectedIdentifierNamingStyle = new(AiIdentifierNamingStyle.SnakeCase, "スネークケース"),
        };

        await vm.GenerateCommand.ExecuteAsync(null);

        client.LastSettings.Should().NotBeNull();
        client.LastSettings!.IdentifierNamingStyle.Should().Be(AiIdentifierNamingStyle.SnakeCase);
    }

    /// <summary>選択したテーブル名の単複数スタイルが生成設定へ渡されることを検証する</summary>
    [Fact(DisplayName = "選択したテーブル名の単複数が生成設定へ渡される")]
    public async Task Generate_PassesSelectedTableNameNumberStyle()
    {
        var client = new FakeAiSchemaClient();
        var vm = new AiGenerateDialogViewModel(client)
        {
            Provider = AiProvider.OpenAI,
            SaveApiKey = false,
            ApiKey = "sk-vm-test-table-number",
            Prompt = "test",
            SelectedTableNameNumberStyle = new(AiTableNameNumberStyle.Plural, "複数形"),
        };

        await vm.GenerateCommand.ExecuteAsync(null);

        client.LastSettings.Should().NotBeNull();
        client.LastSettings!.TableNameNumberStyle.Should().Be(AiTableNameNumberStyle.Plural);
    }

    /// <summary>更新モード選択時に既存 ER 図が生成設定へ渡され、命名規則は既定が維持されることを検証する</summary>
    [Fact(DisplayName = "更新モード選択時は既存 ER 図付きで生成設定へ渡される")]
    public async Task Generate_PassesUpdateModeAndExistingDiagram()
    {
        var client = new FakeAiSchemaClient();
        var existingDiagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    TableName = "Customer",
                    Columns =
                    [
                        new Column
                        {
                            Name = "Id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    ],
                },
            ],
        };
        var vm = new AiGenerateDialogViewModel(client, existingDiagram)
        {
            Provider = AiProvider.OpenAI,
            SaveApiKey = false,
            ApiKey = "sk-vm-test-update",
            Prompt = "既存顧客テーブルに会員ランクを追加",
            SelectedGenerationMode = new(AiGenerationMode.UpdateExisting, "既存 ER 図に追加・変更"),
        };

        await vm.GenerateCommand.ExecuteAsync(null);

        client.LastSettings.Should().NotBeNull();
        client.LastSettings!.GenerationMode.Should().Be(AiGenerationMode.UpdateExisting);
        client.LastSettings.ExistingDiagram.Should().NotBeNull();
        client.LastSettings.ExistingDiagram!.Entities.Should().ContainSingle();
        client.LastSettings.ExistingDiagram.Entities[0].TableName.Should().Be("Customer");
        client.LastSettings.IdentifierNamingStyle.Should().Be(AiIdentifierNamingStyle.PascalCase);
        client.LastSettings.TableNameNumberStyle.Should().Be(AiTableNameNumberStyle.Singular);
    }

    /// <summary>更新モードへ切り替えると要件サンプルが更新され、命名オプションが無効化されることを検証する</summary>
    [Fact(DisplayName = "更新モードへ切り替えると要件サンプルが更新される")]
    public void SelectedGenerationMode_UpdateExisting_ChangesPromptSample()
    {
        var existingDiagram = new ErDiagram { Entities = [new Entity { TableName = "Customer" }] };
        var vm = new AiGenerateDialogViewModel(new FakeAiSchemaClient(), existingDiagram);

        vm.SelectedGenerationMode = new(AiGenerationMode.UpdateExisting, "既存 ER 図に追加・変更");

        vm.Prompt.Should().Be("会員ランク管理と注文ステータス履歴を追加してください。");
        vm.CanCustomizeNamingOptions.Should().BeFalse();
    }
}
