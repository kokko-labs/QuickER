using System;
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
        public Task<AiSchemaJson> GenerateAsync(AiGenerationSettings settings, CancellationToken ct = default)
            => Task.FromResult(new AiSchemaJson());
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
            Prompt = "test"
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
            Prompt = "test"
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
}
