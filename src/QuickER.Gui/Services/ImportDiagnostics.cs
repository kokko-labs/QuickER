using System.IO;
using QuickER.Resources;

namespace QuickER.Services;

/// <summary>テキスト形式（Mermaid / DBML）の取込診断へ行番号を付与するヘルパ</summary>
/// <remarks>
/// 各診断メッセージ（resx）は行番号を持たない書式のまま据え置き、ここで
/// <see cref="Strings.Import_LineDiagnostic"/> による前置だけを行う。行ループ本体を丸ごと囲んで
/// 包み直す使い方をすれば、パーサ内部（カラム定義・リレーション定義の解析）が投げる診断にも
/// メッセージ書式を変えずに位置情報が付く。
/// ファイル全体に紐づく診断（空テキスト・エンティティ 0 件など）は指すべき行が無いため対象外。
/// </remarks>
internal static class ImportDiagnostics
{
    /// <summary>行に紐づく診断へ行番号を前置し、元の例外を内部例外として保持した例外を生成する</summary>
    /// <param name="lineNumber">1 始まりの行番号</param>
    /// <param name="inner">行の解析中に発生した診断例外</param>
    public static InvalidDataException AtLine(int lineNumber, InvalidDataException inner) =>
        new(string.Format(Strings.Import_LineDiagnostic, lineNumber, inner.Message), inner);

    /// <summary>行番号を前置した診断例外を生成する（ループを抜けてから判明する診断用）</summary>
    /// <param name="lineNumber">1 始まりの行番号</param>
    /// <param name="message">整形済みの診断メッセージ</param>
    public static InvalidDataException AtLine(int lineNumber, string message) =>
        new(string.Format(Strings.Import_LineDiagnostic, lineNumber, message));
}
