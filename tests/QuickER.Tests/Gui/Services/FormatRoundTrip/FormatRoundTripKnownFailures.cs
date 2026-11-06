using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Gui.Services.FormatRoundTrip;

/// <summary>形の行 × 形式の既知の失敗（直す区切りのある製品の違いと、受け入れた形式の仕様）</summary>
/// <remarks>
/// <para>
/// ケース ID は「形の行の Id|形式」。理由の断片は製品の中立（英語）の文で書く（エンジンは UI のカルチャを <see cref="FormatUiCulture"/> に置いて流す）。範囲がケース ID と序数で一致し、失敗のメッセージが理由の断片を含むときだけ当たる。
/// 載っているのに起きない項目・載っていない失敗・理由の断片の食い違いはどれも赤になる（直ったら項目を消す）。
/// </para>
/// <para>
/// 宣言（<see cref="FormatAdapter.NotCarried"/>）で受けられない違いだけをここに置く：
/// 表・列の増減（<see cref="Facet.Structure"/>。宣言では説明できない設計）と、取込・生成の例外と、製品の修正を決めた違い。
/// </para>
/// </remarks>
internal static class FormatRoundTripKnownFailures
{
    /// <summary>C# リバースで型を解析できない列が消えることを受け入れた理由</summary>
    private const string UnparsableTypeReason =
        "C# リバースの仕様（2026-11-05 ユーザーの判断）：型カタログで解析できない自由記述型は生成が [DbColumnMeta] を省き"
        + "（src/QuickER.CodeGen.CSharp/CSharpGenerationModels.cs 566 行）、リバースは [DbColumnMeta] の無い列プロパティを警告つきでスキップする"
        + "（docs/import-export.ja.md「C# コード（.cs）」）";

    /// <summary>C# の生成が名前のぶつかりで止まることを受け入れた理由</summary>
    private const string GenerationCollisionReason =
        "C# の生成の仕様：列名・表名がパスカルケースで同じ名前になる図は生成時エラーで止める"
        + "（src/QuickER.CodeGen.CSharp/CLAUDE.md「生成型名・メンバー名のぶつかり」）";

    /// <summary>C# の生成が名前の改行・制御文字で止まることを受け入れた理由</summary>
    private const string GenerationControlCharacterReason =
        "C# の生成の仕様：名前に改行・制御文字を含む図は生成前の診断で止める"
        + "（src/QuickER.CodeGen.CSharp/CLAUDE.md「名前の制御文字の生成前診断」）";

    /// <summary>C# の生成が同じ列ペアの外部キーの重複で止まることを受け入れた理由</summary>
    private const string GenerationDuplicateForeignKeyReason =
        "C# の生成の仕様：同じ列ペアの外部キーが 2 本ある図は生成前に止める"
        + "（src/QuickER.CodeGen.CSharp/CLAUDE.md「ナビゲーション名と外部キーの重複」）";

    /// <summary>ケース ID</summary>
    public static string CaseId(string shapeId, string format) => $"{shapeId}|{format}";

    /// <summary>台帳</summary>
    public static KnownCaseLedger Ledger { get; } =
        new(
            "FormatRoundTrip",
            [
                // Q：同じ列ペアのリレーションが 2 本ある図を、C# の生成は生成前に止める（受け入れた仕様。Excel・DBML・Mermaid は往復する）
                .. new[]
                {
                    "mermaid-plain-constraint-name-not-read-back-duplicate",
                    "mermaid-unnamed-relationships-not-reported",
                }.Select(shape => new KnownEntry(
                    "P3-8-Q",
                    KnownKind.Accepted,
                    null,
                    GenerationDuplicateForeignKeyReason,
                    CaseId(shape, CSharpReverseAdapter.Value),
                    KnownMatch.KeyOnly,
                    ReasonContains: "The same foreign key is defined more than once"
                )),
                // L：名前・型・説明に改行を含む図を、C# の生成は生成前の診断で止める（受け入れた仕様）
                .. new[]
                {
                    "injection-names-with-new-line",
                    "injection-data-type-with-new-line",
                    "injection-description-with-new-line",
                }.Select(shape => new KnownEntry(
                    "P3-8-L",
                    KnownKind.Accepted,
                    null,
                    GenerationControlCharacterReason,
                    CaseId(shape, CSharpReverseAdapter.Value),
                    KnownMatch.KeyOnly,
                    ReasonContains: "a line break or a control character"
                )),
                // R：表 'A B' と 'a b' のように名前の同一性（大文字小文字を無視）で同じ表を並べた図は、取込が断り、C# の生成が止める
                // （受け入れた仕様：docs/import-export.ja.md「形式に共通の規則」・src/QuickER.CodeGen.CSharp/CLAUDE.md「型名のぶつかりを生成時に止める」）。
                // 元の形式（HTML の定義書）は取込を持たず、アンカーの Id が重ならないことだけを見る
                .. new (string Format, string Reason)[]
                {
                    ("Dbml", "is duplicated"),
                    ("Mermaid", "is duplicated"),
                    ("Excel", "duplicate table name"),
                    (CSharpReverseAdapter.Value, "C# の生成がエラーで止まった"),
                }.Select(item => new KnownEntry(
                    "P3-8-R",
                    KnownKind.Accepted,
                    null,
                    "名前の同一性で同じ表を並べた図は、取込は同じ名前のテーブルとして断り（docs/import-export.ja.md「形式に共通の規則」）、"
                        + "C# の生成は型名のぶつかりで止める（src/QuickER.CodeGen.CSharp/CLAUDE.md「型名のぶつかりを生成時に止める」）",
                    CaseId("html-unique-anchor-ids", item.Format),
                    KnownMatch.KeyOnly,
                    ReasonContains: item.Reason
                )),
                // K：名前が同じ識別子に畳まれる図は、C# の生成が生成時エラーで止める（受け入れた仕様）。元の形式（Mermaid）では往復する
                .. new[]
                {
                    "mermaid-folded-column-collision-deterministic",
                    "mermaid-folded-column-name-collision-suffix",
                    "mermaid-folded-column-name-multiple-collisions",
                    "mermaid-unquotable-entity-name-folded",
                }.Select(shape => new KnownEntry(
                    "P3-8-K",
                    KnownKind.Accepted,
                    null,
                    GenerationCollisionReason,
                    CaseId(shape, CSharpReverseAdapter.Value),
                    KnownMatch.KeyOnly,
                    ReasonContains: "C# の生成がエラーで止まった"
                )),
                // F：C# リバースで型を解析できない列が警告つきで消える（受け入れた仕様）
                .. new[]
                {
                    "dbml-quoted-array-type",
                    "dbml-quoted-type-with-spaces-double-precision",
                    "dbml-quoted-type-with-spaces-timestamp",
                    "dbml-type-with-double-quote-my-type",
                    "dbml-type-with-double-quote-mytype",
                    "dbml-type-with-double-quote-od-d-10",
                    "mermaid-empty-data-type",
                    "mermaid-data-type-token-double-precision",
                    "mermaid-data-type-token-enum-a-b",
                    "mermaid-data-type-token-integer-array",
                    "mermaid-data-type-token-quoted-mytype",
                    "mermaid-data-type-token-sys-geography",
                    "mermaid-data-type-token-varchar-20-array",
                }.Select(shape => new KnownEntry(
                    "P3-8-F",
                    KnownKind.Accepted,
                    null,
                    UnparsableTypeReason,
                    CaseId(shape, CSharpReverseAdapter.Value),
                    KnownMatch.KeyOnly,
                    ReasonContains: "表・列が増減した"
                )),
            ]
        );
}
