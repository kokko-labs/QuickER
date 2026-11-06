using QuickER.AI;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.AI.UI;

/// <summary>添付リストの表の次元表・台帳の行</summary>
/// <remarks>受け付けの表の 1 行＝（受け付ける範囲, ファイルの種類）。網羅すべき組は全部（網羅しない組は無い）。</remarks>
internal static class AttachmentListDimensions
{
    /// <summary>網羅しない組の照合の範囲</summary>
    public const string Scope = "attachment-list";

    /// <summary>受け付ける範囲</summary>
    public const string Support = "Support";

    /// <summary>ファイルの種類</summary>
    public const string Kind = "Kind";

    /// <summary>次元表</summary>
    public static DimensionTable Table =>
        new(
            EngineIds.Ai,
            [
                new Factor(
                    Support,
                    AttachmentListRows.Supports.Select(entry => new Level(entry.Name)).ToList(),
                    "受け付ける範囲（接続方式とプロバイダーの代表）"
                ),
                new Factor(
                    Kind,
                    Enum.GetValues<ChatAttachmentKind>()
                        .Select(kind => new Level(kind.ToString(), AxisMember.Of(kind)))
                        .ToList(),
                    "ファイルの種類"
                ),
            ]
        );

    /// <summary>受け付けの表の行を数える</summary>
    public static void Observe(CoverageCounter counter, string support, ChatAttachmentKind kind) =>
        counter.Observe(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Support] = support,
                [Kind] = kind.ToString(),
            }
        );

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor =>
        new(
            EngineIds.Ai,
            "Attachments",
            "添付リストの表（受け付ける範囲 × ファイルの種類・操作の列）",
            Table,
            AttachmentListRows.Table.Count,
            [
                "tests/QuickER.Tests/AI.UI/AttachmentListDimensions.cs",
                "tests/QuickER.Tests/AI.UI/AttachmentListRows.cs",
                "tests/QuickER.Tests/AI.UI/AttachmentListTests.cs",
            ],
            ["src/QuickER.AI.UI/AttachmentListViewModel.cs", "src/QuickER.AI/ChatAttachment.cs"]
        );
}
