using QuickER.Tests.Gui.UndoRedo;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.TestSupport;

/// <summary>QuickER.Tests にあるテストのエンジンの台帳（エンジンの部分ごとの次元表・シナリオの数・テストのソース・対象の範囲）</summary>
/// <remarks>
/// Integration にも同じ名前の台帳（<c>QuickER.Tests.Integration.EngineRegistry</c>）がある。このファイルは Integration の
/// Compile リンクの glob（<c>NameFuzz*.cs</c>・<c>DslFuzz*.cs</c>・<c>Engines/**</c>）に当たらない名前にしておく。
/// </remarks>
internal static class EngineRegistry
{
    /// <summary>台帳の行（エンジンの ID と部分の組は一意）</summary>
    public static IReadOnlyList<EngineDescriptor> All { get; } =
    [
        DslFuzzDimensions.SqliteMySqlDescriptor,
        NameFuzzDimensions.Descriptor,
        ToolArgumentFuzzDimensions.Descriptor,
        RelationshipTypeChangeDimensions.Descriptor,
    ];
}
