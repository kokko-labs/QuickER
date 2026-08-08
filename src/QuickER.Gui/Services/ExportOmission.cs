namespace QuickER.Services;

/// <summary>出力形式が構造的に表現できないために落ちる情報の種類（言語中立）</summary>
/// <remarks>
/// <para>
/// 検出側（エクスポータ）は言語中立の種類だけを返し、表示側（<c>MainViewModel</c>）が resx で
/// 文言化する。スキーマ同期の警告（<c>SyncPlan.Warnings</c>）と同じ流儀。
/// </para>
/// <para>
/// 件数は持たない。ユーザーが知りたいのは「何が落ちるか」であって、
/// 「列の説明 42 件」のような数字は対処の判断材料にならないため。
/// </para>
/// </remarks>
public enum ExportOmissionKind
{
    /// <summary>テーブルの説明</summary>
    TableDescription,

    /// <summary>テーブルのメモ</summary>
    TableMemo,

    /// <summary>列の説明</summary>
    ColumnDescription,

    /// <summary>列の NULL 許可の指定</summary>
    ColumnNullability,

    /// <summary>複合 UNIQUE 制約（構成列が 2 列以上のもの）</summary>
    CompositeUniqueConstraint,

    /// <summary>UNIQUE 制約の名前</summary>
    UniqueConstraintName,

    /// <summary>外部キーの列対応（親列と子列のペア）</summary>
    ForeignKeyColumnPairs,

    /// <summary>参照アクション（ON DELETE / ON UPDATE）</summary>
    ReferentialAction,

    /// <summary>名前付きクエリ定義</summary>
    NamedQuery,
}
