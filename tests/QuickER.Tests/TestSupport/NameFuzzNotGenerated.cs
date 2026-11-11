using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// 名前の総当たりの生成器（<see cref="NameFuzz"/>）が作らない形の宣言。入口の総当たりと、名前の DDL の往復が同じ名前を使う
/// </summary>
/// <remarks>
/// 純粋なデータ（Engines の型と BCL だけを参照する）。Integration にも <c>NameFuzz*.cs</c> の Compile のリンクで入る。
/// </remarks>
internal static class NameFuzzNotGenerated
{
    /// <summary>宣言</summary>
    public static NotGeneratedDeclaration Declaration { get; } =
        new(
            "NameFuzz",
            [
                ProtectionNames.EnginePart(EngineIds.Name, "Entrances"),
                ProtectionNames.EnginePart(EngineIds.Name, "DdlRoundTrip"),
            ],
            [
                NotGenerated.Accepted(
                    "empty-control-and-normalization-forms",
                    "空の名前・制御文字と行区切りを含む名前・上限を大きく超える長い名前・NFC と NFD だけが違う名前",
                    "空と制御文字・行区切りは名前の入口（SqlNameText）が、上限を超える利用者の名前は DDL・同期の入口が縮めずに断り、断ることは入口のテストが受け持つ。製品は名前の綴りを書き換えない（SchemaNames）ので、NFC と NFD は別の名前として扱われ分岐が無い",
                    "`SqlNameTextTests`・`IdentifierLimitEntryTests`"
                ),
            ]
        );
}
