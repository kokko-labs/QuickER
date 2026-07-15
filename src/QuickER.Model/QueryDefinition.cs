namespace QuickER.Model;

/// <summary>
/// エンティティに対する名前付きクエリ 1 件を表すモデル
/// JSON シリアライズの対象となる単純な POCO（保存単位は <see cref="ErDiagram.Queries"/>）
/// </summary>
/// <remarks>
/// エンティティ・列は名前ではなく Guid で参照する（<see cref="Relationship"/> と同じ流儀。
/// 図上のリネームに定義が追従できるようにするため）。
/// 条件（<see cref="Condition"/>）はミニ DSL の文字列で、列名の参照だけは自由テキストになるため、
/// リネーム時の自動書き換えと読み込み・生成時の再検証をセーフティネットとする。
/// </remarks>
public class QueryDefinition
{
    /// <summary>クエリ定義の一意識別子</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>クエリが属するエンティティの ID（<see cref="Entity.Id"/> を参照）</summary>
    public Guid EntityId { get; set; }

    /// <summary>メソッド名の意味部分（例: <c>GetByCustomer</c>。生成時に Async サフィックスを付与する）</summary>
    public string Name { get; set; } = "NewQuery";

    /// <summary>クエリの説明（生成メソッドの XML ドキュメントコメントに反映する）</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>戻り形（一覧・単一・スカラー・件数・射影）</summary>
    public QueryReturnShape Returns { get; set; } = QueryReturnShape.List;

    /// <summary>戻り形がスカラーのときの型（方言中立トークン。例: <c>decimal(12,2)</c>）</summary>
    public string? ScalarType { get; set; }

    /// <summary>クエリのパラメータ一覧（生成メソッドの引数になる）</summary>
    public List<QueryParameter> Parameters { get; set; } = new();

    /// <summary>検索条件（ミニ DSL。<see cref="QueryImplementationKind.Dsl"/> のとき使用。null は無条件）</summary>
    public string? Condition { get; set; }

    /// <summary>並び順（定義に固定。戻り形が一覧・射影のとき使用）</summary>
    public List<QueryOrdering> OrderBy { get; set; } = new();

    /// <summary>ページングを有効にするか（有効なら生成メソッドに take / skip 引数が付く）</summary>
    public bool HasPaging { get; set; }

    /// <summary>実装方式（ミニ DSL・自由 SQL・manual）</summary>
    public QueryImplementationKind Implementation { get; set; } = QueryImplementationKind.Dsl;

    /// <summary>
    /// 自由 SQL の方言別辞書（キーはプロバイダ識別名。例: <c>sqlserver</c> / <c>sqlite</c>）
    /// SQL が与えられていない実装先（EF Core 含む）は manual 扱い＝契約宣言のみ生成される
    /// </summary>
    public Dictionary<string, string> Sql { get; set; } = new();

    /// <summary>戻り形が射影のときの DTO 型名（例: <c>OrderSummaryRow</c>）</summary>
    public string? ResultTypeName { get; set; }

    /// <summary>戻り形が射影のときの出力フィールド一覧</summary>
    public List<ProjectionField> Fields { get; set; } = new();
}

/// <summary>名前付きクエリのパラメータ 1 件（生成メソッドの引数）</summary>
public class QueryParameter
{
    /// <summary>パラメータ名（例: <c>customerId</c>。DSL / SQL では <c>@customerId</c> で参照する）</summary>
    public string Name { get; set; } = "param";

    /// <summary>型（方言中立トークン。例: <c>int32</c> / <c>string(50)</c>。列参照時は無視される）</summary>
    public string Type { get; set; } = "int32";

    /// <summary>リスト型かどうか（IN 条件用。生成メソッドでは IReadOnlyList になる）</summary>
    public bool IsList { get; set; }

    /// <summary>
    /// 型付けの参照元列 ID（<see cref="Column.Id"/>。null＝<see cref="Type"/> トークンで型付け）
    /// </summary>
    /// <remarks>
    /// 指定すると生成メソッドの引数型が「その列の生成型」になる（値オブジェクト有効の図では VO 型・
    /// 無効ならプリミティブ）。列 ID 参照のためリネーム・型変更に追従する。参照先はクエリが属する
    /// エンティティの列に限る。
    /// </remarks>
    public Guid? SourceColumnId { get; set; }
}

/// <summary>名前付きクエリの並び順 1 件（列 ID 参照でリネームに追従する）</summary>
public class QueryOrdering
{
    /// <summary>並び替えキーの列 ID（<see cref="Column.Id"/> を参照）</summary>
    public Guid ColumnId { get; set; }

    /// <summary>降順かどうか（既定は昇順）</summary>
    public bool Descending { get; set; }
}

/// <summary>射影クエリの出力フィールド 1 件（DTO のプロパティになる）</summary>
public class ProjectionField
{
    /// <summary>フィールド名（DTO のプロパティ名。自由 SQL では SELECT の列別名と一致させる）</summary>
    public string Name { get; set; } = "Field";

    /// <summary>型（方言中立トークン。例: <c>string(50)</c> / <c>decimal(12,2)</c>）</summary>
    public string Type { get; set; } = "int32";

    /// <summary>列サブセット射影の参照元列 ID（<see cref="Column.Id"/>。自由 SQL 由来のフィールドは null）</summary>
    public Guid? SourceColumnId { get; set; }
}

/// <summary>名前付きクエリの戻り形</summary>
public enum QueryReturnShape
{
    /// <summary>エンティティの一覧（IReadOnlyList）</summary>
    List,

    /// <summary>エンティティ単一（該当なしは null）</summary>
    Single,

    /// <summary>単一のスカラー値（<see cref="QueryDefinition.ScalarType"/> で型指定）</summary>
    Scalar,

    /// <summary>件数（int）</summary>
    Count,

    /// <summary>射影 DTO の一覧（<see cref="QueryDefinition.Fields"/> で形を定義）</summary>
    Projection,
}

/// <summary>名前付きクエリの実装方式</summary>
public enum QueryImplementationKind
{
    /// <summary>ミニ DSL の条件から全実装先（方言 SQL・EF Core LINQ）へ自動生成する</summary>
    Dsl,

    /// <summary>方言別の自由 SQL（<see cref="QueryDefinition.Sql"/>）を埋め込む。辞書に無い実装先は manual 扱い</summary>
    Sql,

    /// <summary>契約宣言のみ生成し、実装はユーザーが partial クラスで書く</summary>
    Manual,
}
