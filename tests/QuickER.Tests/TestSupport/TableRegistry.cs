using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.TestSupport;

/// <summary>QuickER.Tests にある単体の表（エンジンの台帳に載らない、決まった形の行の表）の台帳</summary>
/// <remarks>
/// <para>
/// 宣言はこのファイルにまとめ、表のファイルには置かない（表のファイルは行を足す区切りが並行して直すので、同じファイルに置くと
/// ぶつかる）。影響の地図はこの台帳の対象の範囲を <c>table:{名前}</c> の守りとして読む。
/// </para>
/// <para>
/// <c>ScenarioTable</c> を作るテストのソースは、エンジンの部分か、この台帳のどれかのソースに入れる（入れ忘れは影響の地図の
/// ガードが名指しする）。Integration にも同じ名前の台帳（<c>QuickER.Tests.Integration.TableRegistry</c>）がある。このファイルは
/// Integration の Compile リンクの glob に当たらない名前にしておく。
/// </para>
/// </remarks>
internal static class TableRegistry
{
    /// <summary>生成コードのランタイムのフィクスチャの置き場</summary>
    private const string Fixture = "tests/QuickER.Tests/GeneratedFixture/";

    /// <summary>生成テンプレートの置き場</summary>
    private const string Templates = "src/QuickER.CodeGen.CSharp/Templates/CSharpRuntime/";

    /// <summary>台帳の行（名前は一意）</summary>
    public static IReadOnlyList<TableDescriptor> All { get; } =
    [
        new(
            "EditModelScenarios",
            "生成した EditModel・Mapper の決まった形のシナリオ（書式・空欄・範囲の削除・Removed・Mapper の null・兄弟・DB のエラー）",
            [
                Fixture + "EditModelScenarioRows.GeneratedFixture.cs",
                Fixture + "EditModelScenarioRows.InMemoryFixture.cs",
                Fixture + "EditModelScenarioRows.QueryFixture.cs",
                Fixture + "EditModelScenarios.GeneratedFixture.cs",
                Fixture + "EditModelScenarios.InMemoryFixture.cs",
                Fixture + "EditModelScenarios.QueryFixture.cs",
            ],
            [Templates + "_03_EditModelsAndMappers.scriban", "src/QuickER.Runtime"]
        ),
        new(
            "ValueObjectScenarios",
            "値オブジェクトの決まった形のシナリオ（呼び出し・参照の同一性・等値・初回使用）",
            [
                Fixture + "ValueObjectScenarioRows.cs",
                Fixture + "ValueObjectScenarioRows.Conversion.cs",
                Fixture + "ValueObjectScenarioRows.Creation.cs",
                Fixture + "ValueObjectScenarioRows.Declared.cs",
                Fixture + "ValueObjectScenarioRows.Defined.cs",
                Fixture + "ValueObjectScenarioRows.Equality.cs",
                Fixture + "ValueObjectScenarioRows.Formatting.cs",
                Fixture + "ValueObjectScenarioRows.Members.cs",
                Fixture + "ValueObjectScenarios.cs",
            ],
            [Templates + "_01_ValueObjects.scriban", "src/QuickER.Runtime"]
        ),
        new(
            "ForeignKeyReferentialActions",
            "参照アクションと文字列表現の相互変換の表（SQL 表記の往復・表記ゆれの読み取り）",
            ["tests/QuickER.Tests/Model/ForeignKeyReferentialActionTests.cs"],
            ["src/QuickER.Model/ForeignKeyReferentialAction.cs"]
        ),
        new(
            "Converters",
            "XAML の表示の値のコンバーターの表（反転のパラメーター・件数のしきい値・null の扱い。使われていないアプリ本体の BoolToVisibilityConverter は入れない）",
            [
                "tests/QuickER.Tests/Gui/Converters/ConverterRows.cs",
                "tests/QuickER.Tests/Gui/Converters/ConverterTests.cs",
            ],
            [
                "src/QuickER.Gui/Converters/CountToVisibilityConverter.cs",
                "src/QuickER.Gui/Converters/NullToBooleanConverter.cs",
                "src/QuickER.Gui/Converters/NullToVisibilityConverter.cs",
                "src/QuickER.AI.UI/BoolToVisibilityConverter.cs",
            ]
        ),
    ];
}
