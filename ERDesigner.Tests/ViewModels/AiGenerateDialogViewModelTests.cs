using System.Threading;
using System.Threading.Tasks;
using ERDesigner.Models;
using ERDesigner.Services;
using ERDesigner.ViewModels;
using FluentAssertions;

namespace ERDesigner.Tests.ViewModels;

/// <summary>
/// <see cref="AiGenerateDialogViewModel"/> の API キー保存挙動を検証します。
/// </summary>
public class AiGenerateDialogViewModelTests
{
    private sealed class FakeAiSchemaClient : IAiSchemaClient
    {
        public AiGenerationSettings? LastSettings { get; private set; }

        public Task<AiSchemaJson> GenerateAsync(AiGenerationSettings settings, CancellationToken ct = default)
        {
            LastSettings = settings;

            return Task.FromResult(new AiSchemaJson());
        }
    }

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
