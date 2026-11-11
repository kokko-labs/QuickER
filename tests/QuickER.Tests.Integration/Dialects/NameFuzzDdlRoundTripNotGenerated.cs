using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// 名前の DDL の往復（<c>NameFuzzDdlRoundTripIntegrationTests</c>）が名前の総当たりの名前を置かない形の宣言。名前そのものの作らない形は
/// <c>NameFuzzNotGenerated</c> にある（QuickER.Tests にもリンクで入るので、Integration の型を cref でも参照しない）
/// </summary>
/// <remarks>純粋なデータ（Engines の型と BCL だけを参照する）。</remarks>
internal static class NameFuzzDdlRoundTripNotGenerated
{
    /// <summary>宣言</summary>
    public static NotGeneratedDeclaration Declaration { get; } =
        new(
            "NameFuzzDdlRoundTripIntegrationTests",
            [ProtectionNames.EnginePart(EngineIds.Name, "DdlRoundTrip")],
            [
                NotGenerated.Accepted(
                    "dotted-table-names",
                    "ドットを含むテーブル名",
                    "5 方言とも DDL が最初のドットでスキーマ修飾として分けて引用する（プロバイダの対称性の既知の割り切り）。分け方は方言の識別子の表が受け持つ",
                    "`Dialect/Identifiers`（行 `split-single-quote-is-kept`）"
                ),
                NotGenerated.Accepted(
                    "oracle-double-quote",
                    "Oracle の二重引用符を含む識別子",
                    "Oracle の引用識別子は二重引用符を含められない（DB の事実）",
                    NotGenerated.Unguarded
                ),
            ]
        );
}
