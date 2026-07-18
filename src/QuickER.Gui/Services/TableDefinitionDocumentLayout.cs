namespace QuickER.Services;

/// <summary>テーブル定義書 Excel の行位置・役割タグ定義名・書式バージョンを一元管理する共有定数</summary>
/// <remarks>
/// エクスポータ（<see cref="TableDefinitionDocumentExporter"/>）とインポータ
/// （<see cref="TableDefinitionDocumentImporter"/>）が同じ定数を参照することで、
/// 出力と取込の行位置・タグ名の不整合をコンパイル時に排除する。
/// </remarks>
internal static class TableDefinitionDocumentLayout
{
    /// <summary>テーブル一覧シートの見出し行</summary>
    public const int SummaryHeaderRow = 3;

    /// <summary>テーブル一覧シートのデータ開始行</summary>
    public const int SummaryDataStartRow = 4;

    /// <summary>リレーション一覧シートの見出し行</summary>
    public const int RelationshipHeaderRow = 3;

    /// <summary>リレーション一覧シートのデータ開始行</summary>
    public const int RelationshipDataStartRow = 4;

    /// <summary>詳細シートでテーブル基本情報を格納する行</summary>
    public const int DetailTableInfoRow = 4;

    /// <summary>詳細シートでカラム見出しを配置する行</summary>
    public const int DetailColumnHeaderRow = 6;

    /// <summary>詳細シートでカラム定義が始まる行</summary>
    public const int DetailColumnDataStartRow = 7;

    /// <summary>表紙シートを指す非表示の定義名</summary>
    public const string CoverDefinedName = "QuickER_TableDoc_Cover";

    /// <summary>改訂履歴シートを指す非表示の定義名</summary>
    public const string HistoryDefinedName = "QuickER_TableDoc_History";

    /// <summary>テーブル一覧シートを指す非表示の定義名</summary>
    public const string SummaryDefinedName = "QuickER_TableDoc_Summary";

    /// <summary>リレーション一覧シートを指す非表示の定義名</summary>
    public const string RelationshipsDefinedName = "QuickER_TableDoc_Relationships";

    /// <summary>書式バージョンを保持するカスタムプロパティ名</summary>
    public const string FormatVersionPropertyName = "QuickER_TableDoc_FormatVersion";

    /// <summary>現在の書式バージョン値</summary>
    public const string FormatVersionValue = "1";
}
