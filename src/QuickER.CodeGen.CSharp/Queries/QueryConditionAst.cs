namespace QuickER.CodeGen.CSharp.Queries;

/// <summary>名前付きクエリ条件（ミニ DSL）の構文木ノード基底</summary>
/// <remarks>
/// ミニ DSL は「SQL（全方言）と EF Core の LINQ の両方へ翻訳できる」ことを目的とした
/// パース可能なサブセット。文法は 比較（= &lt;&gt; != &lt; &lt;= &gt; &gt;=）・AND / OR / NOT・括弧・
/// IS [NOT] NULL・[NOT] LIKE・[NOT] IN のみで、列参照と <c>@パラメータ</c>・数値/文字列リテラルを扱う。
/// これを超えるクエリは自由 SQL / manual モードの担当（設計判断は tasks/todo.md 参照）。
/// </remarks>
public abstract class ConditionNode { }

/// <summary>AND / OR の 2 項論理ノード</summary>
public sealed class LogicalNode : ConditionNode
{
    /// <summary>論理演算子</summary>
    public required LogicalOperator Operator { get; init; }

    /// <summary>左辺</summary>
    public required ConditionNode Left { get; init; }

    /// <summary>右辺</summary>
    public required ConditionNode Right { get; init; }
}

/// <summary>NOT 単項ノード</summary>
public sealed class NotNode : ConditionNode
{
    /// <summary>否定対象</summary>
    public required ConditionNode Operand { get; init; }
}

/// <summary>比較述語（列 演算子 オペランド）</summary>
public sealed class ComparisonNode : ConditionNode
{
    /// <summary>左辺の列参照</summary>
    public required ColumnReference Column { get; init; }

    /// <summary>比較演算子</summary>
    public required ComparisonOperator Operator { get; init; }

    /// <summary>右辺（パラメータまたはリテラル）</summary>
    public required ConditionOperand Operand { get; init; }
}

/// <summary>NULL 判定述語（列 IS [NOT] NULL）</summary>
public sealed class NullCheckNode : ConditionNode
{
    /// <summary>判定対象の列参照</summary>
    public required ColumnReference Column { get; init; }

    /// <summary>IS NOT NULL かどうか</summary>
    public required bool IsNotNull { get; init; }
}

/// <summary>文字列一致述語（列 [NOT] LIKE / CONTAINS / STARTSWITH / ENDSWITH オペランド）</summary>
/// <remarks>
/// 生成ランタイムの翻訳器（ADO の SqlExpressionTranslator・EF Core の既定翻訳）は文字列の
/// Contains / StartsWith / EndsWith を LIKE へ変換し、値のワイルドカードはエスケープする
/// （インジェクション安全）。DSL の LIKE はこの意味論に写像する：リテラルパターンは
/// <c>%</c> の位置で一致種別へ分解し、<c>LIKE @p</c> は部分一致（値はリテラル扱い）とする。
/// 生の LIKE パターン（内部 <c>%</c> や <c>_</c>）が必要なクエリは自由 SQL / manual の担当。
/// </remarks>
public sealed class StringMatchNode : ConditionNode
{
    /// <summary>対象の列参照</summary>
    public required ColumnReference Column { get; init; }

    /// <summary>否定（NOT LIKE 等）かどうか</summary>
    public required bool Negated { get; init; }

    /// <summary>一致種別</summary>
    public required StringMatchKind Kind { get; init; }

    /// <summary>比較値（パラメータまたは文字列リテラル。リテラルはワイルドカード除去済みの素の値）</summary>
    public required ConditionOperand Operand { get; init; }
}

/// <summary>文字列一致の種別</summary>
public enum StringMatchKind
{
    /// <summary>部分一致（Contains。<c>LIKE '%x%'</c> / <c>LIKE @p</c> / CONTAINS）</summary>
    Contains,

    /// <summary>前方一致（StartsWith。<c>LIKE 'x%'</c> / STARTSWITH）</summary>
    StartsWith,

    /// <summary>後方一致（EndsWith。<c>LIKE '%x'</c> / ENDSWITH）</summary>
    EndsWith,
}

/// <summary>IN 述語（列 [NOT] IN @リストパラメータ）</summary>
public sealed class InNode : ConditionNode
{
    /// <summary>対象の列参照</summary>
    public required ColumnReference Column { get; init; }

    /// <summary>NOT IN かどうか</summary>
    public required bool Negated { get; init; }

    /// <summary>候補集合のリストパラメータ</summary>
    public required ParameterOperand Parameter { get; init; }
}

/// <summary>条件式内の列参照（原文の位置を保持し、検証でモデル上の列へ解決する）</summary>
/// <remarks>位置情報は GUI のリネーム自動書き換え（旧列名のスパンを新列名で置換）に使う</remarks>
public sealed class ColumnReference
{
    /// <summary>原文に書かれた列名</summary>
    public required string Text { get; init; }

    /// <summary>原文内の開始位置（0 始まり）</summary>
    public required int Position { get; init; }

    /// <summary>原文内の長さ</summary>
    public required int Length { get; init; }

    /// <summary>検証で解決したモデル上の正準列名（未解決は null）</summary>
    public string? ResolvedName { get; set; }

    /// <summary>検証で解決した列 ID（未解決は null）</summary>
    public Guid? ResolvedColumnId { get; set; }
}

/// <summary>述語の右辺オペランド基底</summary>
public abstract class ConditionOperand { }

/// <summary>@パラメータ参照オペランド</summary>
public sealed class ParameterOperand : ConditionOperand
{
    /// <summary>原文に書かれたパラメータ名（@ を除く）</summary>
    public required string Text { get; init; }

    /// <summary>原文内の開始位置（@ を含む・0 始まり）</summary>
    public required int Position { get; init; }

    /// <summary>原文内の長さ（@ を含む）</summary>
    public required int Length { get; init; }

    /// <summary>検証で解決した定義上の正準パラメータ名（未解決は null）</summary>
    public string? ResolvedName { get; set; }
}

/// <summary>数値リテラルオペランド（原文表記のまま保持。カルチャ非依存）</summary>
public sealed class NumberOperand : ConditionOperand
{
    /// <summary>原文の数値表記（例: <c>-1</c> / <c>0.5</c>）</summary>
    public required string Literal { get; init; }
}

/// <summary>文字列リテラルオペランド（<c>'...'</c>。<c>''</c> は <c>'</c> のエスケープ）</summary>
public sealed class StringOperand : ConditionOperand
{
    /// <summary>エスケープ解除済みの値</summary>
    public required string Value { get; init; }
}

/// <summary>論理演算子</summary>
public enum LogicalOperator
{
    /// <summary>AND</summary>
    And,

    /// <summary>OR</summary>
    Or,
}

/// <summary>比較演算子（<c>!=</c> は <c>&lt;&gt;</c> の別表記として同一視する）</summary>
public enum ComparisonOperator
{
    /// <summary>=</summary>
    Equal,

    /// <summary>&lt;&gt; / !=</summary>
    NotEqual,

    /// <summary>&lt;</summary>
    Less,

    /// <summary>&lt;=</summary>
    LessOrEqual,

    /// <summary>&gt;</summary>
    Greater,

    /// <summary>&gt;=</summary>
    GreaterOrEqual,
}

/// <summary>条件式の診断 1 件（描画前の診断文言＋原文内の位置）</summary>
/// <param name="Text">
/// 資源キー＋書式引数のまま保持する診断文言。面ごとに言語が異なる（GUI・内蔵チャット＝UI 言語追従／
/// 外部 MCP サーバ＝英語固定）ため、文字列化はフォーマッタがカルチャを明示して行う。
/// </param>
/// <param name="Position">原文内の開始位置（0 始まり。全体に関わる診断は 0）</param>
/// <param name="Length">原文内の長さ（特定できない場合は 0）</param>
public sealed record ConditionDiagnostic(QueryDiagnosticText Text, int Position, int Length)
{
    /// <summary>現在の UI 言語で描画した診断メッセージ（GUI 表示・生成診断など UI 言語追従の面が使う）</summary>
    public string Message => Text.Format(null);
}

/// <summary>条件式のパース結果（構文木＋診断＋列参照一覧）</summary>
public sealed class ConditionParseResult
{
    /// <summary>構文木のルート（構文エラー時は null）</summary>
    public ConditionNode? Root { get; init; }

    /// <summary>構文・検証の診断一覧（空なら成功）</summary>
    public List<ConditionDiagnostic> Diagnostics { get; } = new();

    /// <summary>原文に現れた列参照の一覧（出現順。GUI のリネーム書き換えに使う）</summary>
    public List<ColumnReference> ColumnReferences { get; } = new();

    /// <summary>原文に現れたパラメータ参照の一覧（出現順）</summary>
    public List<ParameterOperand> ParameterReferences { get; } = new();

    /// <summary>診断がなく構文木が得られているか</summary>
    public bool Success => Root is not null && Diagnostics.Count == 0;
}
