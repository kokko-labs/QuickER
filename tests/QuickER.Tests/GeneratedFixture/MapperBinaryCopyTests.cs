using AwesomeAssertions;
using Xunit;
using BinaryDocumentEntity = QuickER.Tests.GeneratedBinaryFixture.DocumentEntity;
using BinaryDocumentMapper = QuickER.Tests.GeneratedBinaryFixture.DocumentMapper;
using VoGadgetEntity = QuickER.Tests.GeneratedConcurrencyFixture.GadgetEntity;
using VoGadgetIdValue = QuickER.Tests.GeneratedConcurrencyFixture.GadgetIdValue;
using VoGadgetMapper = QuickER.Tests.GeneratedConcurrencyFixture.GadgetMapper;
using VoNameValue = QuickER.Tests.GeneratedConcurrencyFixture.NameValue;
using VoRowVerValue = QuickER.Tests.GeneratedConcurrencyFixture.RowVerValue;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// Mapper の Entity → EditModel ロードが、バイナリ列を防御的にコピーする（配列を共有しない）ことを検証する。
/// </summary>
/// <remarks>
/// ロードは確定値の直接代入（無損失）だが、<c>byte[]</c> は参照型なので素の代入だと Entity と EditModel が同じ配列を
/// 指し、EditModel 側の書き換えがロード元へ黙って波及する。素の <c>byte[]</c> 列（<c>BinaryFixture</c>）と
/// 値オブジェクト列（<c>ConcurrencyFixture</c>＝VO 有効の rowversion）の両方を対象にする。
/// </remarks>
public sealed class MapperBinaryCopyTests
{
    /// <summary>素の byte[] 列はロード時に複製され、EditModel 側の書き換えが Entity へ波及しない</summary>
    [Fact(DisplayName = "ロード: 素の byte[] 列は防御的コピーされ Entity と配列を共有しない")]
    public void PlainBinaryColumn_IsCopiedOnLoad()
    {
        var entity = new BinaryDocumentEntity
        {
            DocumentId = 1,
            Title = "doc",
            IsPublished = true,
            Payload = [1, 2, 3],
        };

        var editModel = new BinaryDocumentMapper().CreateEditModel(entity);

        editModel.Payload.Should().Equal(1, 2, 3);
        editModel.Payload.Should().NotBeSameAs(entity.Payload);

        // EditModel 側の配列を書き換えてもロード元は不変
        editModel.Payload![0] = 9;
        entity.Payload.Should().Equal(1, 2, 3);
    }

    /// <summary>値オブジェクトのバイナリ列もロード時に内包配列ごと複製される（VO は配列を複製しない契約のため）</summary>
    [Fact(DisplayName = "ロード: 値オブジェクトのバイナリ列も内包配列ごと複製される")]
    public void ValueObjectBinaryColumn_IsCopiedOnLoad()
    {
        var entity = new VoGadgetEntity
        {
            GadgetId = VoGadgetIdValue.Create(1),
            Name = VoNameValue.Create("gadget"),
            RowVer = VoRowVerValue.Create([1, 2, 3, 4, 5, 6, 7, 8]),
        };

        var editModel = new VoGadgetMapper().CreateEditModel(entity);

        // 値としては等しいが、VO インスタンスも内包配列も別物
        editModel.RowVer.Should().Be(entity.RowVer);
        editModel.RowVer.Should().NotBeSameAs(entity.RowVer);
        editModel.RowVer!.Value.Should().NotBeSameAs(entity.RowVer.Value);

        editModel.RowVer.Value[0] = 9;
        entity.RowVer.Value.Should().Equal(1, 2, 3, 4, 5, 6, 7, 8);
    }
}
