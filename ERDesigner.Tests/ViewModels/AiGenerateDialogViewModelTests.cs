using System.Threading;
using System.Threading.Tasks;
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
            Provider = AiProvider.OpenAi,
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
            Provider = AiProvider.OpenAi,
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
            Provider = AiProvider.OpenAi,
            SaveApiKey = false,
            ApiKey = "sk-vm-test-style",
            Prompt = "test",
            SelectedIdentifierNamingStyle = new(AiIdentifierNamingStyle.SnakeCase, "スネークケース"),
        };

        await vm.GenerateCommand.ExecuteAsync(null);

        client.LastSettings.Should().NotBeNull();
        client.LastSettings!.IdentifierNamingStyle.Should().Be(AiIdentifierNamingStyle.SnakeCase);
    }
}
