namespace QuickER.Services;

/// <summary>テーブル定義書 Excel の行位置・列位置・役割タグ定義名・書式バージョンを一元管理する共有定数</summary>
/// <remarks>
/// エクスポータ（<see cref="TableDefinitionDocumentExporter"/>）とインポータ
/// （<see cref="TableDefinitionDocumentImporter"/>）が同じ定数を参照することで、
/// 出力と取込の行位置・列位置・タグ名の不整合をコンパイル時に排除する。
/// </remarks>
internal static class TableDefinitionDocumentLayout
{
    /// <summary>テーブル一覧シートの見出し行</summary>
    public const int SummaryHeaderRow = 3;

    /// <summary>テーブル一覧シートのデータ開始行</summary>
    public const int SummaryDataStartRow = 4;

    /// <summary>テーブル一覧シートのテーブル名列（詳細シートへのリンクを兼ねる）</summary>
    public const int SummaryTableNameColumn = 2;

    /// <summary>テーブル一覧シートの説明列</summary>
    public const int SummaryDescriptionColumn = 3;

    /// <summary>テーブル一覧シートの備考列</summary>
    public const int SummaryMemoColumn = 4;

    /// <summary>リレーション一覧シートの見出し行</summary>
    public const int RelationshipHeaderRow = 3;

    /// <summary>リレーション一覧シートのデータ開始行</summary>
    public const int RelationshipDataStartRow = 4;

    /// <summary>詳細シートのテーブル名タイトル行（A1）</summary>
    public const int DetailTitleRow = 1;

    /// <summary>詳細シートのテーブル説明行（罫線なしのプレーン表示）</summary>
    public const int DetailDescriptionRow = 2;

    /// <summary>詳細シートでカラム見出しを配置する行</summary>
    public const int DetailColumnHeaderRow = 3;

    /// <summary>詳細シートでカラム定義が始まる行</summary>
    public const int DetailColumnDataStartRow = 4;

    /// <summary>テーブル一覧シートを指す非表示の定義名</summary>
    public const string SummaryDefinedName = "QuickER_TableDoc_Summary";

    /// <summary>リレーション一覧シートを指す非表示の定義名</summary>
    public const string RelationshipsDefinedName = "QuickER_TableDoc_Relationships";

    /// <summary>テーブル一覧シートで対象 DBMS を表示する行（タイトルと見出しの間・表示専用）</summary>
    public const int SummaryDbmsRow = 2;

    /// <summary>書式バージョンを保持するカスタムプロパティ名</summary>
    public const string FormatVersionPropertyName = "QuickER_TableDoc_FormatVersion";

    /// <summary>現在の書式バージョン値</summary>
    public const string FormatVersionValue = "1";

    /// <summary>対象 DBMS（プロバイダ識別名）を保持するカスタムプロパティ名</summary>
    /// <remarks>表示セルはローカライズされるため、取込はこのプロパティから言語非依存に復元する</remarks>
    public const string TargetDbmsPropertyName = "QuickER_TableDoc_TargetDbms";
}
