namespace QuickER.CodeGen.CSharp;

/// <summary>Scriban テンプレートへ渡す C# コード生成のルートモデル</summary>
internal sealed class CSharpGenerationModel
{
    /// <summary>生成コードの名前空間</summary>
    public required string NamespaceName { get; init; }

    /// <summary>エンティティクラスの生成モデル一覧</summary>
    public required IReadOnlyList<CSharpClassModel> EntityClasses { get; init; }

    /// <summary>EditModel クラスの生成モデル一覧</summary>
    public required IReadOnlyList<CSharpEditModelClassModel> EditModelClasses { get; init; }

    /// <summary>Mapper クラスの生成モデル一覧</summary>
    public required IReadOnlyList<CSharpMapperModel> MapperClasses { get; init; }

    /// <summary>Repository クラスの生成モデル一覧</summary>
    public required IReadOnlyList<CSharpRepositoryModel> RepositoryClasses { get; init; }

    /// <summary>値オブジェクト（Value Object）クラスの生成モデル一覧（GenerateValueObjects が OFF のときは空）</summary>
    public required IReadOnlyList<CSharpValueObjectModel> ValueObjectClasses { get; init; }

    /// <summary>EF Core（DbContext・Fluent 構成）の生成モデル。GenerateEfCore が OFF のときは null</summary>
    public CSharpEfCoreModel? EfCore { get; init; }

    /// <summary>
    /// 同期対象テーブル（<c>rowversion</c> 列を持つテーブル）の生成モデル一覧。FK トポロジカル順（親が先）。
    /// </summary>
    /// <remarks><c>GenerateSyncSupport</c> が OFF のときは空（テンプレートの同期ブロックはスコープ側で落ちる）。</remarks>
    public IReadOnlyList<CSharpSyncTableModel> SyncTables { get; init; } = [];
}

/// <summary>1 つの同期対象テーブル（<c>rowversion</c> 列を持つテーブル）の生成モデル</summary>
/// <remarks>
/// 同期支援は「サーバー（SQL Server）＋ローカル（SQLite）」の 2 方言を同時に扱うため、方言別のクォート規則を
/// テンプレートのスコープ変数（<c>quote_open</c> 等）から得られない。SQL 文はここで両方言分を組み立てて渡す。
/// </remarks>
internal sealed class CSharpSyncTableModel
{
    /// <summary>対象の Entity クラス名（例 <c>SyncItemEntity</c>）</summary>
    public required string EntityClassName { get; init; }

    /// <summary>対象の Repository 契約インターフェイス名（例 <c>ISyncItemRepository</c>）</summary>
    public required string InterfaceName { get; init; }

    /// <summary>主キーの C# 型名（値オブジェクト有効時は VO 型名）</summary>
    public required string KeyTypeName { get; init; }

    /// <summary>対象テーブル名（ジャーナルへ記録する識別子でもある）</summary>
    public required string TableName { get; init; }

    /// <summary>直結差分ソースのクラス名（例 <c>SyncItemDirectSyncSource</c>）</summary>
    public required string SourceClassName { get; init; }

    /// <summary>HTTP 差分ソースのクラス名（例 <c>HttpSyncItemSyncSource</c>・リモートサービス生成時のみ出力）</summary>
    public required string HttpSourceClassName { get; init; }

    /// <summary>
    /// リモートエンドポイントのルート名（例 <c>SyncItem</c>）＝<see cref="CSharpRepositoryModel.RemoteRouteName"/> と同値。
    /// </summary>
    /// <remarks>
    /// 同期専用エンドポイント（<c>POST {prefix}/{ルート名}/Sync*</c>）はサーバー側の <c>RemoteServer</c> バケットが
    /// <c>sync_tables</c> を直接ループして張るため、リポジトリモデルを引かずにルート名を解決できるよう写しておく。
    /// </remarks>
    public required string RemoteRouteName { get; init; }

    /// <summary>同期記述子のクラス名（例 <c>SyncItemSyncTable</c>）</summary>
    public required string TableClassName { get; init; }

    /// <summary>ジャーナル記録デコレータのクラス名（例 <c>JournalingSyncItemRepository</c>）</summary>
    public required string DecoratorClassName { get; init; }

    /// <summary>主キーのプロパティ名</summary>
    public required string KeyPropertyName { get; init; }

    /// <summary>サーバー側の差分取得 SQL（C# 文字列リテラル・SQL Server クォート・昇順）</summary>
    public required string ServerChangesSql { get; init; }

    /// <summary>サーバー側の全キー取得 SQL（C# 文字列リテラル・SQL Server クォート）</summary>
    public required string ServerKeysSql { get; init; }

    /// <summary>ローカルのアンカー導出 SQL（C# 文字列リテラル・SQLite クォート・ミラー列の MAX）</summary>
    public required string LocalAnchorSql { get; init; }

    /// <summary>ローカルの全キー取得 SQL（C# 文字列リテラル・SQLite クォート）</summary>
    public required string LocalKeysSql { get; init; }

    /// <summary>ローカルの存在確認 SQL（C# 文字列リテラル・SQLite クォート・<c>@keys</c> のコレクション展開）</summary>
    public required string LocalExistingKeysSql { get; init; }

    /// <summary>ローカルの全行削除 SQL（C# 文字列リテラル・SQLite クォート・洗い替えの前半）</summary>
    public required string LocalDeleteAllSql { get; init; }

    /// <summary>エンティティからミラー版を読む式（VO 有効時は内包値を取り出す）</summary>
    public required string RowVersionReadExpression { get; init; }

    /// <summary>デコレータの削除経路で <c>existing</c> 変数からミラー版を読む式</summary>
    public required string RowVersionReadExistingExpression { get; init; }

    /// <summary>エンティティへミラー版を書く式（VO 有効時は再ラップする）</summary>
    public required string RowVersionWriteExpression { get; init; }

    /// <summary>キー変数 <c>key</c> をジャーナルのテキスト形へ変換する式</summary>
    public required string FormatKeyExpression { get; init; }

    /// <summary>デコレータで <c>entity</c> の主キーをテキスト形へ変換する式</summary>
    public required string FormatKeyEntityExpression { get; init; }

    /// <summary>デコレータで引数 <c>id</c> をテキスト形へ変換する式</summary>
    public required string FormatKeyIdExpression { get; init; }

    /// <summary>ジャーナルのテキスト形 <c>keyText</c> から主キーを復元する式</summary>
    public required string ParseKeyExpression { get; init; }

    /// <summary>デコレータへ追加で挿入する委譲メンバー群（名前付きクエリ・重複事前チェック等。整形済み・無ければ空文字）</summary>
    public string DecoratorDelegationBlock { get; init; } = string.Empty;

    /// <summary>
    /// このテーブルの無制限バイナリ列（除外列）の C# プロパティ名一覧（宣言順。除外オプション OFF・該当列なしでは空）。
    /// </summary>
    /// <remarks>
    /// 行の転送（SELECT / UPDATE）から外れる列＝通常の同期では運ばれない列。空でなければ同期の
    /// <c>IncludeUnboundedBinary</c> 経路と洗い替えの損失ガードが意味を持つ。
    /// </remarks>
    public IReadOnlyList<string> BinaryColumnPropertyNames { get; init; } = [];

    /// <summary>同期記述子へ足す実装インターフェイス宣言（除外列があるとき <c>, ISyncBinaryColumns&lt;キー型&gt;</c>・無ければ空文字）</summary>
    public string BinaryInterfaceDeclaration { get; init; } = string.Empty;

    /// <summary>直結差分ソースへ挿入する除外列アクセサ実装（整形済み・無ければ空文字）</summary>
    public string DirectSourceBinaryBlock { get; init; } = string.Empty;

    /// <summary>HTTP 差分ソースへ挿入する除外列アクセサ実装（整形済み・無ければ空文字）</summary>
    public string HttpSourceBinaryBlock { get; init; } = string.Empty;

    /// <summary>同期記述子へ挿入するローカル側の除外列アクセサ実装（整形済み・無ければ空文字）</summary>
    public string TableBinaryBlock { get; init; } = string.Empty;

    /// <summary>ジャーナル記録デコレータへ挿入する除外列アクセサ（Read は素通し・Write は journal-first。無ければ空文字）</summary>
    public string DecoratorBinaryBlock { get; init; } = string.Empty;
}

/// <summary>EF Core 用コード（DbContext と OnModelCreating の Fluent 構成）のルート生成モデル</summary>
internal sealed class CSharpEfCoreModel
{
    /// <summary>DbContext 上の DbSet プロパティ一覧（宣言順はエンティティ順）</summary>
    public required IReadOnlyList<CSharpEfCoreDbSetModel> DbSets { get; init; }

    /// <summary>エンティティごとの Fluent 構成一覧</summary>
    public required IReadOnlyList<CSharpEfCoreEntityConfigModel> Entities { get; init; }

    /// <summary>EntityBase の永続化対象外メンバー（Ignore する get/set 可能な公開プロパティ名）一覧</summary>
    public required IReadOnlyList<string> IgnoredBaseMembers { get; init; }
}

/// <summary>DbContext の 1 つの DbSet プロパティに対応する生成モデル</summary>
internal sealed class CSharpEfCoreDbSetModel
{
    /// <summary>DbSet の要素となる Entity クラス名</summary>
    public required string EntityClassName { get; init; }

    /// <summary>DbSet プロパティ名（Entity クラス名から "Entity" を除いた複数形風の名前）</summary>
    public required string PropertyName { get; init; }
}

/// <summary>1 エンティティの EF Core Fluent 構成（builder.Entity&lt;T&gt;(...) の中身）の生成モデル</summary>
internal sealed class CSharpEfCoreEntityConfigModel
{
    /// <summary>対象の Entity クラス名</summary>
    public required string EntityClassName { get; init; }

    /// <summary>マッピング先テーブル名（ToTable 用）</summary>
    public required string TableName { get; init; }

    /// <summary>主キーを構成するプロパティ名一覧（HasKey 用。単一・複合の両方に対応）</summary>
    public required IReadOnlyList<string> KeyPropertyNames { get; init; }

    /// <summary>スカラープロパティの構成一覧</summary>
    public required IReadOnlyList<CSharpEfCorePropertyConfigModel> Properties { get; init; }

    /// <summary>このエンティティが principal（親）となるリレーションの構成一覧（HasOne/WithMany 等）</summary>
    public required IReadOnlyList<CSharpEfCoreRelationshipConfigModel> Relationships { get; init; }
}

/// <summary>1 スカラープロパティの EF Core Fluent 構成の生成モデル</summary>
internal sealed class CSharpEfCorePropertyConfigModel
{
    /// <summary>プロパティ名（Property(e =&gt; e.Xxx) 用）</summary>
    public required string PropertyName { get; init; }

    /// <summary>マッピング先カラム名（HasColumnName 用）</summary>
    public required string ColumnName { get; init; }

    /// <summary>必須（非 NULL）かどうか（IsRequired 用）</summary>
    public required bool IsRequired { get; init; }

    /// <summary>文字列の最大長（HasMaxLength 用）。無指定は null</summary>
    public int? MaxLength { get; init; }

    /// <summary>decimal の全体桁数（HasPrecision 用）。無指定・非 decimal は null</summary>
    public int? Precision { get; init; }

    /// <summary>decimal の小数桁数（HasPrecision の第 2 引数）。Precision がある場合のみ有効</summary>
    public int? Scale { get; init; }

    /// <summary>行バージョン列（IsRowVersion 用）かどうか</summary>
    public required bool IsRowVersion { get; init; }

    /// <summary>値オブジェクト型かどうか（HasConversion 構成の要否判定）</summary>
    public required bool IsValueObject { get; init; }

    /// <summary>値オブジェクトのクラス名（IsValueObject=true のときのみ有効）</summary>
    public string ValueObjectClassName { get; init; } = string.Empty;
}

/// <summary>principal（親）から見た 1 リレーションの EF Core Fluent 構成の生成モデル</summary>
/// <remarks>
/// 親エンティティ側に構成をまとめる（HasMany/HasOne → WithOne/WithMany → HasForeignKey → OnDelete）。
/// dependent（子）側は親への単一参照ナビゲーションを 1 つ持つ前提とする
/// </remarks>
internal sealed class CSharpEfCoreRelationshipConfigModel
{
    /// <summary>dependent（子・FK 側）の Entity クラス名</summary>
    public required string DependentClassName { get; init; }

    /// <summary>親側のナビゲーションプロパティ名（1 対多はコレクション、1 対 1 は単一参照）</summary>
    public required string PrincipalNavigationName { get; init; }

    /// <summary>子側の親参照ナビゲーションプロパティ名</summary>
    public required string DependentNavigationName { get; init; }

    /// <summary>1 対多（親がコレクションを持つ）かどうか。false は 1 対 1</summary>
    public required bool IsCollection { get; init; }

    /// <summary>FK を構成する子側プロパティ名一覧（HasForeignKey 用）</summary>
    public required IReadOnlyList<string> ForeignKeyPropertyNames { get; init; }

    /// <summary>親削除時にカスケード削除するかどうか（OnDelete の Cascade/Restrict 切り替え）</summary>
    public required bool CascadeDelete { get; init; }
}

/// <summary>値オブジェクト（Value Object）クラスの生成モデル</summary>
/// <remarks>列名（正規化 Pascal）でグローバルに共有される 1 型に対応する</remarks>
internal sealed class CSharpValueObjectModel
{
    /// <summary>生成する VO クラス名（例: CustomerIdValue）</summary>
    public required string ClassName { get; init; }

    /// <summary>内包する値の C# 型名（TValue。NULL 注釈なし。例: int, string, byte[]）</summary>
    public required string ValueTypeName { get; init; }

    /// <summary>継承する基底クラスの宣言（型引数込み。例: ValueObjectOrderedBase&lt;CustomerIdValue, int&gt;）</summary>
    public required string BaseDeclaration { get; init; }

    /// <summary>実装する汎用インターフェース宣言（例: IValueObject&lt;CustomerIdValue, int&gt;）</summary>
    public required string InterfaceDeclaration { get; init; }

    /// <summary>GuidKey（string で GUID 保持・無引数生成で自動採番）かどうか</summary>
    public required bool IsGuidKey { get; init; }

    /// <summary>診断・エラーメッセージ用の代表カラム名</summary>
    public required string ColumnName { get; init; }

    /// <summary>XML doc summary へ埋め込む列の説明（XML エスケープ・改行畳み込み済み）。空なら定型文へフォールバックする</summary>
    public required string DescriptionXmlDoc { get; init; }

    /// <summary>
    /// 静的 <c>DisplayName</c> の解決へ渡すメンバー名（代表列由来のプロパティ名。例 "Name"）。
    /// 説明が無いときのフォールバック先になる（メッセージの後方互換）。
    /// </summary>
    public required string DisplayNameMemberName { get; init; }

    /// <summary>
    /// 静的 <c>DisplayName</c> の解決へ渡す代表列の説明（C# 文字列リテラルへエスケープ済み）。
    /// 説明が空・空白のみなら <c>null</c>（テンプレートは <c>null</c> リテラルを出す）。
    /// </summary>
    public string? DisplayNameDescription { get; init; }

    /// <summary>string の最大長（自動 MaxLength 検証用）。無指定は null</summary>
    public int? MaxLength { get; init; }

    /// <summary>decimal の精度 p（自動桁数検証用）。非 decimal・無指定は null</summary>
    public int? Precision { get; init; }

    /// <summary>decimal のスケール s（自動桁数検証用）。非 decimal・無指定は null</summary>
    public int? Scale { get; init; }
}

/// <summary>エンティティクラスの生成モデル</summary>
internal sealed class CSharpClassModel
{
    /// <summary>生成するクラス名</summary>
    public required string ClassName { get; init; }

    /// <summary>対応するテーブル名</summary>
    public required string TableName { get; init; }

    /// <summary>テーブルの説明（DB 定義メタ属性 [DbTableMeta] の Description 用）。空なら属性ごと省略する</summary>
    public required string Description { get; init; }

    /// <summary>XML doc summary へ埋め込むテーブルの説明（XML エスケープ・改行畳み込み済み）。空なら定型文へフォールバックする</summary>
    public required string DescriptionXmlDoc { get; init; }

    /// <summary>
    /// <c>DefaultDisplayName</c> の override へ渡すテーブルの説明（C# 文字列リテラルへエスケープ済み）。
    /// 説明が空・空白のみなら <c>null</c>＝override を生成せず、基底のクラス名フォールバックに任せる。
    /// </summary>
    public string? DisplayNameDescription { get; init; }

    /// <summary>
    /// 列由来プロパティ名が <c>DisplayName</c> / <c>CustomizeDisplayName</c> と衝突するため、
    /// <c>DisplayName</c> プロパティと <c>CustomizeDisplayName</c> フックの生成を省略するかどうか。
    /// </summary>
    public required bool HasDisplayNameCollision { get; init; }

    /// <summary>スカラープロパティの生成モデル一覧</summary>
    public required IReadOnlyList<CSharpPropertyModel> Properties { get; init; }

    /// <summary>ナビゲーションプロパティの生成モデル一覧</summary>
    public required IReadOnlyList<CSharpNavigationModel> Navigations { get; init; }

    /// <summary>
    /// テーブルの UNIQUE 制約をクラスへ宣言する <c>[UniqueConstraint(...)]</c> 属性行（整形済み・制約なしは空文字）。
    /// </summary>
    /// <remarks>
    /// <c>[DbTableMeta]</c> / <c>[DbColumnMeta]</c> と同じ「DB 定義の自己記述」メタで、実行時の振る舞いは持たない
    /// （重複事前チェックは生成コードが担う）。属性型そのものの出力可否は
    /// <c>emit_unique_constraint_attr</c>（刻む中身が 1 つでもあるか）が決める。
    /// </remarks>
    public string UniqueConstraintAttributesBlock { get; init; } = string.Empty;
}

/// <summary>エンティティの 1 スカラープロパティに対応する生成モデル</summary>
internal sealed class CSharpPropertyModel
{
    /// <summary>プロパティ名</summary>
    public required string PropertyName { get; init; }

    /// <summary>対応するカラム名</summary>
    public required string ColumnName { get; init; }

    /// <summary>C# 型名</summary>
    public required string TypeName { get; init; }

    /// <summary>NULL 許容かどうか</summary>
    public required bool IsNullable { get; init; }

    /// <summary>参照型かどうか</summary>
    public required bool IsReferenceType { get; init; }

    /// <summary>主キーかどうか</summary>
    public required bool IsPrimaryKey { get; init; }

    /// <summary>外部キーかどうか</summary>
    public required bool IsForeignKey { get; init; }

    /// <summary>最大長（文字列型などで指定される場合）</summary>
    public int? MaxLength { get; init; }

    /// <summary>DB カラムのメタ情報: decimal の全体桁数 precision（属性 [SqlColumnType] の Precision 用）</summary>
    public int? FacetPrecision { get; init; }

    /// <summary>DB カラムのメタ情報: decimal の小数桁数 scale（属性 [SqlColumnType] の Scale 用）</summary>
    public int? FacetScale { get; init; }

    /// <summary>
    /// SQL パラメータ型明示化に使う <c>SqlDbType</c> の列挙名（例: "VarChar"）。[SqlColumnType] 属性の生成に使う。
    /// 未知の型は null で、属性を付与せず AddWithValue にフォールバックさせる。
    /// </summary>
    public string? SqlDbTypeName { get; init; }

    /// <summary>[SqlColumnType] の Size に載せる宣言長（n / max=-1 / 無指定=0）。文字列・バイナリ以外は 0</summary>
    public int SqlDeclaredLength { get; init; }

    /// <summary>
    /// 無制限バイナリ列（varbinary(max) / image / 長さ宣言なし BLOB 等）かどうか。<c>ExcludeUnboundedBinaryColumns</c> オプション ON のとき
    /// マーカー属性 [UnboundedBinaryColumn] を付与する対象の識別に使う（判定はプロバイダの型マッパーの責務）。
    /// </summary>
    public bool IsUnboundedBinary { get; init; }

    /// <summary>
    /// DB が値を生成する列（SQL Server の <c>rowversion</c> / <c>timestamp</c> 等）かどうか。オプション非依存で常にマーカー属性
    /// [StoreGeneratedColumn] を付与する対象の識別に使う（判定はプロバイダの型マッパーの責務）。QuickER 版 Repository の INSERT / UPDATE 対象から除外される。
    /// </summary>
    public bool IsRowVersion { get; init; }

    /// <summary>
    /// DB 定義メタ属性（[DbColumnMeta]）へ刻む方言中立の型トークン（例 "string(50)"）。型カタログで解析できない自由記述型は null で属性を省略する。
    /// </summary>
    public string? CanonicalTypeToken { get; init; }

    /// <summary>カラムの説明（DB 定義メタ属性 [DbColumnMeta] の Description 用）。空なら named 引数ごと省略する</summary>
    public required string Description { get; init; }

    /// <summary>XML doc summary へ埋め込む列の説明（XML エスケープ・改行畳み込み済み）。空なら定型文へフォールバックする</summary>
    public required string DescriptionXmlDoc { get; init; }

    /// <summary>フィールド初期化子の式</summary>
    public required string Initializer { get; init; }

    /// <summary>
    /// インメモリ Repository の決定的サンプルデータ（<c>InMemorySampleData</c>）で、この列に代入する値の C# 式。
    /// </summary>
    /// <remarks>
    /// シーダーの <c>for</c> ループ変数 <c>index</c>（1..3）を参照してよい。型・主キー/外部キー・NULL 可・値オブジェクト有無から
    /// 決定的に構築する（FK は親キーの実在値＝<c>index</c> を指し、NULL 可列は 3 件中 1 件を null にする）。既定は空文字。
    /// </remarks>
    public string SampleValueExpression { get; init; } = string.Empty;
}

/// <summary>ナビゲーションプロパティの生成モデル</summary>
internal sealed class CSharpNavigationModel
{
    /// <summary>プロパティ名</summary>
    public required string PropertyName { get; init; }

    /// <summary>参照先の型名</summary>
    public required string TypeName { get; init; }

    /// <summary>コレクション（1 対多の多側）かどうか</summary>
    public required bool IsCollection { get; init; }

    /// <summary>NULL 許容かどうか</summary>
    public required bool IsNullable { get; init; }

    /// <summary>親（参照される側）への参照かどうか</summary>
    public required bool IsParentReference { get; init; }

    /// <summary>カスケード保存/削除の対象（子方向のナビゲーション）かどうか</summary>
    public required bool Cascade { get; init; }

    /// <summary>表示用の型名（コレクションなら要素型を包んだ表記）</summary>
    public required string DisplayTypeName { get; init; }

    /// <summary>カスケード子のバッキングフィールド名（例: _children）。EditModel のカスケード子ナビゲーションでのみ設定</summary>
    public string FieldName { get; init; } = string.Empty;

    /// <summary>フィールド初期化子の式</summary>
    public required string Initializer { get; init; }

    /// <summary>principal 側（参照される側）のテーブル名</summary>
    public required string PrincipalTableName { get; init; }

    /// <summary>principal 側（参照される側）のカラム名</summary>
    public required string PrincipalColumnName { get; init; }

    /// <summary>dependent 側（FK 側）のテーブル名</summary>
    public required string DependentTableName { get; init; }

    /// <summary>dependent 側（FK 側）のカラム名</summary>
    public required string DependentColumnName { get; init; }

    /// <summary>
    /// <c>[NavigationReference]</c> へ追記する外部キーメタデータの名前付き引数（例:
    /// <c>, ConstraintName = "FK_orders_customers", OnDelete = "Cascade"</c>）。既定値のみなら空文字
    /// </summary>
    /// <remarks>
    /// 冗長を避けるため「制約名は非 null のとき・参照アクションは <c>NoAction</c> 以外のとき」だけ出力する
    /// （説明を持たない列で <c>Description</c> を省く既定の流儀と同じ）。C# リバースはこの引数から
    /// 制約名・参照アクションを復元する。
    /// </remarks>
    public string ForeignKeyMetadataArguments { get; init; } = string.Empty;
}

/// <summary>Mapper クラスの生成モデル（インターフェースなし）</summary>
internal sealed class CSharpMapperModel
{
    /// <summary>生成する Mapper クラス名</summary>
    public required string ClassName { get; init; }

    /// <summary>変換元の Entity クラス名</summary>
    public required string EntityClassName { get; init; }

    /// <summary>変換先の EditModel クラス名</summary>
    public required string EditModelClassName { get; init; }

    /// <summary>Entity → EditModel へコピーするプロパティのペア一覧</summary>
    public required IReadOnlyList<CSharpMappingPropertyPair> ScalarProperties { get; init; }

    /// <summary>EditModel が保持するナビゲーションプロパティの情報一覧</summary>
    public required IReadOnlyList<CSharpMapperNavigationModel> NavigationProperties { get; init; }
}

/// <summary>Entity と EditModel の対応プロパティ 1 組に対応する生成モデル</summary>
internal sealed class CSharpMappingPropertyPair
{
    /// <summary>プロパティ名</summary>
    public required string PropertyName { get; init; }

    /// <summary>Entity 側の型名</summary>
    public required string EntityTypeName { get; init; }

    /// <summary>EditModel 側の型名</summary>
    public required string EditModelTypeName { get; init; }

    /// <summary>EditModel 側プロパティが NULL 許容かどうか</summary>
    public required bool EditModelIsNullable { get; init; }

    /// <summary>byte[] 系プロパティかどうか</summary>
    public required bool IsBinary { get; init; }

    /// <summary>
    /// Entity → EditModel のロードで確定値へ代入する式（既定は <c>entity.{プロパティ}</c>）。
    /// </summary>
    /// <remarks>
    /// バイナリ列だけは防御的コピーを挟む。<c>byte[]</c> は参照型なので素の代入だと Entity と EditModel が
    /// 同じ配列を共有し、片方への書き込みがもう片方へ黙って波及する（値オブジェクト列も内包配列を複製しない契約
    /// ＝<c>ValueObjectBinaryBase</c> の XmlDoc のため、配列を写した VO を作り直す）。
    /// </remarks>
    public required string EditModelLoadExpression { get; init; }

    /// <summary>
    /// DB が値を生成する行バージョン列（rowversion / timestamp）かどうか。
    /// </summary>
    /// <remarks>
    /// true のとき Mapper の Entity へのコピーは「入力があるときだけ代入」に切り替える。
    /// DB 採番のため新規行では未入力が正常であり、未入力を欠落として例外にすると新規保存が成立しない。
    /// </remarks>
    public required bool IsRowVersion { get; init; }
}

/// <summary>Mapper が扱うナビゲーションプロパティの生成モデル</summary>
internal sealed class CSharpMapperNavigationModel
{
    /// <summary>プロパティ名</summary>
    public required string PropertyName { get; init; }

    /// <summary>EditModel 側の型名</summary>
    public required string EditModelTypeName { get; init; }

    /// <summary>対応する子の Mapper クラス名（カスケード変換で呼び出す）</summary>
    public required string MapperClassName { get; init; }

    /// <summary>コレクションかどうか</summary>
    public required bool IsCollection { get; init; }

    /// <summary>NULL 許容かどうか（単一参照のとき有効）</summary>
    public required bool IsNullable { get; init; }

    /// <summary>子方向（カスケード変換対象）かどうか。親参照は除外し無限再帰を防ぐ</summary>
    public required bool IsCascade { get; init; }

    /// <summary>principal 側（参照される側）のカラム名</summary>
    public required string PrincipalColumnName { get; init; }

    /// <summary>dependent 側（FK 側）のカラム名</summary>
    public required string DependentColumnName { get; init; }
}

/// <summary>Repository クラスの生成モデル</summary>
internal sealed class CSharpRepositoryModel
{
    /// <summary>方言別実装ブロックの既定値（全対応方言のキーを空文字で持つ。テンプレートの辞書引きを常に成立させる）</summary>
    internal static readonly IReadOnlyDictionary<string, string> EmptyQueryImplBlocks =
        CodeGenerationOptions.SupportedRepositoryDialects.ToDictionary(
            dialect => dialect,
            _ => string.Empty,
            StringComparer.Ordinal
        );

    /// <summary>生成する Repository インターフェース名</summary>
    public required string InterfaceName { get; init; }

    /// <summary>リモート契約生成時に追加するリモート面のインターフェース名（<c>I{Entity}RemoteRepository</c>）</summary>
    /// <remarks>GenerateRemoteContracts が OFF のときは参照されない（テンプレートが ON 時のみ出力・使用する）</remarks>
    public required string RemoteInterfaceName { get; init; }

    /// <summary>生成する Repository 実装クラス名</summary>
    public required string ClassName { get; init; }

    /// <summary>対象の Entity クラス名</summary>
    public required string EntityClassName { get; init; }

    /// <summary>主キーの型名</summary>
    public required string KeyTypeName { get; init; }

    /// <summary>名前付きクエリの契約メンバー群（インターフェイス本体・整形済み。無ければ空文字）</summary>
    public string QueryInterfaceBlock { get; init; } = string.Empty;

    /// <summary>名前付きクエリの共有実装メンバー群（ミニ DSL 系。EF Core・インメモリ実装用。無ければ空文字）</summary>
    public string QuerySharedImplBlock { get; init; } = string.Empty;

    /// <summary>
    /// 名前付きクエリの実装メンバー群（QuickER 版 Repository 用・方言名→整形済みテキスト）。
    /// ミニ DSL 系（全方言共通）と自由 SQL 系（その方言の SQL があるもののみ）を定義順に含む。
    /// 全対応方言のキーを常に持つ（SQL が無い実装先は manual 扱い＝メンバーを含まない）
    /// </summary>
    public IReadOnlyDictionary<string, string> QueryImplBlocksByDialect { get; init; } =
        EmptyQueryImplBlocks;

    /// <summary>名前付きクエリの射影 DTO クラス群（整形済み。無ければ空文字）</summary>
    public string QueryDtoBlock { get; init; } = string.Empty;

    /// <summary>リモートサービス生成時の HTTP クライアント実装クラス名（<c>Http{Entity}RemoteRepository</c>）</summary>
    /// <remarks>GenerateRemoteServices が OFF のときは参照されない（テンプレートが ON 時のみ出力・使用する）</remarks>
    public required string RemoteClientClassName { get; init; }

    /// <summary>リモート呼び出しのルートセグメント（例 <c>Order</c>。クライアント・サーバーで同一文字列を使う）</summary>
    public required string RemoteRouteName { get; init; }

    /// <summary>名前付きクエリの HTTP クライアント転送メソッド群（整形済み。無ければ空文字）</summary>
    public string QueryRemoteClientBlock { get; init; } = string.Empty;

    /// <summary>名前付きクエリのサーバー側エンドポイントマッピング群（Map{Entity}Endpoints 内へ挿入。無ければ空文字）</summary>
    public string QueryRemoteServerBlock { get; init; } = string.Empty;

    /// <summary>名前付きクエリのサーバー側リクエストレコード群（クラスレベルへ挿入。無ければ空文字）</summary>
    public string QueryRemoteServerRecordsBlock { get; init; } = string.Empty;

    /// <summary>
    /// 無制限バイナリ列の Stream アクセサの契約メンバー群（整形済み）。
    /// <c>GenerateRepositories &amp;&amp; ExcludeUnboundedBinaryColumns</c> かつ除外列があるときのみ非空。
    /// 挿入先はテンプレートがリモート契約の有無で出し分ける（リモート面 ON なら <c>I{Entity}RemoteRepository</c>・
    /// OFF なら全機能面 <c>I{Entity}Repository</c>。ランタイム共通の基底 <c>IRemoteRepository</c> には載せない）。
    /// </summary>
    public string BinaryStreamContractBlock { get; init; } = string.Empty;

    /// <summary>
    /// 無制限バイナリ列の Stream アクセサの HTTP クライアント転送メソッド群（<c>Http{Entity}RemoteRepository</c> へ挿入・整形済み）。
    /// GET/PUT/DELETE の専用エンドポイントへ委譲する。リモートサービス生成時のみテンプレートが参照する（無ければ空文字）。
    /// </summary>
    public string BinaryStreamRemoteClientBlock { get; init; } = string.Empty;

    /// <summary>
    /// 無制限バイナリ列の Stream アクセサのサーバー側バイナリエンドポイント群（<c>Map{Entity}Endpoints</c> 内へ挿入・整形済み）。
    /// 除外列ごとに GET（ダウンロード）・PUT（アップロード・サイズ制限解除）・DELETE（NULL 化）の 3 動詞を出力する（無ければ空文字）。
    /// </summary>
    public string BinaryStreamRemoteServerBlock { get; init; } = string.Empty;

    /// <summary>
    /// 無制限バイナリ列の Stream アクセサの薄い実装メンバー群（固定 infra のエンジンへ委譲）。
    /// QuickER 版 Repository 2 方言の実装クラスとインメモリ実装クラスの双方へ同一テキストで挿入する（無ければ空文字）。
    /// </summary>
    public string BinaryStreamThinImplBlock { get; init; } = string.Empty;

    /// <summary>
    /// 無制限バイナリ列の Stream アクセサの EF Core 実装メンバー群（<c>NotSupportedException</c> を投げる。無ければ空文字）。
    /// </summary>
    public string BinaryStreamEfImplBlock { get; init; } = string.Empty;

    /// <summary>
    /// 無制限バイナリ列の Stream アクセサのファイル糖衣（Read/Write{Column}ToFile/FromFile）の拡張メソッド静的クラス全体（整形済み・無ければ空文字）。
    /// </summary>
    public string BinaryStreamFileExtensionsBlock { get; init; } = string.Empty;

    /// <summary>
    /// UNIQUE 制約の重複事前チェック（<c>CheckUniquenessAsync</c>）の契約メンバー（整形済み）。
    /// </summary>
    /// <remarks>
    /// Repository 契約を生成するエンティティでは制約の有無に依らず常に非空（ユーザー定義フックだけでも動くため）。
    /// 挿入先はテンプレートがリモート契約の有無で出し分ける（Stream アクセサと同じ規則＝リモート面 ON なら
    /// <c>I{Entity}RemoteRepository</c>・OFF なら全機能面 <c>I{Entity}Repository</c>）。
    /// </remarks>
    public string UniquenessContractBlock { get; init; } = string.Empty;

    /// <summary>
    /// 重複事前チェックの実装メンバー（式木クエリ経由・全実装先で同一テキスト）。
    /// QuickER 版 Repository 2 方言・インメモリ・EF Core の各実装クラスへ同じテキストで挿入する（無ければ空文字）。
    /// </summary>
    public string UniquenessSharedImplBlock { get; init; } = string.Empty;

    /// <summary>重複事前チェックの HTTP クライアント転送メソッド（<c>Http{Entity}RemoteRepository</c> へ挿入・無ければ空文字）</summary>
    public string UniquenessRemoteClientBlock { get; init; } = string.Empty;

    /// <summary>重複事前チェックのサーバー側エンドポイントマッピング（<c>Map{Entity}Endpoints</c> 内へ挿入・無ければ空文字）</summary>
    public string UniquenessRemoteServerBlock { get; init; } = string.Empty;

    /// <summary>重複事前チェックのサーバー側リクエストレコード（クラスレベルへ挿入・無ければ空文字）</summary>
    public string UniquenessRemoteServerRecordsBlock { get; init; } = string.Empty;
}

// ---- EditModel 専用モデル ----

/// <summary>EditModel クラス全体の生成モデル</summary>
internal sealed class CSharpEditModelClassModel
{
    /// <summary>生成する EditModel クラス名</summary>
    public required string ClassName { get; init; }

    /// <summary>対応するテーブル名</summary>
    public required string TableName { get; init; }

    /// <summary>XML doc summary へ埋め込むテーブルの説明（XML エスケープ・改行畳み込み済み）。空なら定型文へフォールバックする</summary>
    public required string DescriptionXmlDoc { get; init; }

    /// <summary>EditModel のプロパティ生成モデル一覧</summary>
    public required IReadOnlyList<CSharpEditModelPropertyModel> Properties { get; init; }

    /// <summary>ナビゲーションプロパティの生成モデル一覧</summary>
    public required IReadOnlyList<CSharpNavigationModel> Navigations { get; init; }

    /// <summary>カスケード対象（子方向）のナビゲーションを持つかどうか</summary>
    public required bool HasCascadeNavigations { get; init; }

    /// <summary>親モデルの型が一意に定まるときの型付き ParentModel の型名（定まらない場合は null＝基底の EditModelBase? のみ）</summary>
    public string? TypedParentModelTypeName { get; init; }

    /// <summary>
    /// 列由来プロパティ名が表示名解決ヘルパ（<c>GetDisplayName</c> / <c>CustomizePropertyDisplayName</c>）と衝突するため、
    /// 表示名機構（ヘルパ・フック）を省略し、検証メッセージを従来どおりプロパティ名で構築するかどうか。
    /// </summary>
    public required bool HasDisplayNameCollision { get; init; }

    /// <summary>
    /// VO 化されていないプロパティを 1 つ以上持つかどうか。
    /// <c>GetDisplayName</c> ヘルパ／<c>CustomizePropertyDisplayName</c> フックは VO 無効プロパティのみが使うため、
    /// 全プロパティが VO のときはヘルパを生成しない（未使用メンバーを出さない）。
    /// </summary>
    public required bool HasNonValueObjectProperty { get; init; }

    /// <summary>
    /// テーブルの UNIQUE 制約を静的テーブル＋<c>UniquenessConstraints</c> の override として宣言するブロック
    /// （整形済み・制約なしは空文字）。
    /// </summary>
    /// <remarks>
    /// コレクション内重複検証（<c>EditModelUniquenessValidator</c>）の入力。値アクセサはコンパイル済みラムダで、
    /// 検証時のリフレクションは無い（Required 検証と同じ「生成コードで検証する」流儀）。
    /// </remarks>
    public string UniquenessConstraintsBlock { get; init; } = string.Empty;

    /// <summary>
    /// DB 照合糖衣 <c>ValidateUniqueAsync</c> のメソッド全体（整形済み）。Repository 契約面が存在するエンティティのみ非空。
    /// </summary>
    public string UniquenessValidationBlock { get; init; } = string.Empty;

    /// <summary>
    /// この EditModel が UNIQUE 制約テーブル（<see cref="UniquenessConstraintsBlock"/>）を宣言するかどうか。
    /// </summary>
    /// <remarks>固定メンバー名簿の条件付き集合（<see cref="GeneratedFixedMemberNames.EditModelWithUniqueConstraints"/>）の発火条件。</remarks>
    public required bool HasUniqueConstraints { get; init; }

    /// <summary>
    /// この EditModel に対応する Repository 契約面（<c>I{Entity}Repository</c>）が生成されるかどうか。
    /// </summary>
    /// <remarks>固定メンバー名簿の条件付き集合（<see cref="GeneratedFixedMemberNames.EditModelWithRepositoryFace"/>）の発火条件。</remarks>
    public required bool HasRepositoryFace { get; init; }
}

/// <summary>EditModel の 1 プロパティに対応する生成モデル</summary>
/// <remarks>確定値プロパティと UI バインディング用文字列プロパティを併せ持つ構成を表す</remarks>
internal sealed record CSharpEditModelPropertyModel
{
    /// <summary>通常プロパティ名（例: CustomerId）</summary>
    public required string PropertyName { get; init; }

    /// <summary>対応するカラム名（テンプレートは参照しない。メンバー名衝突診断の由来表示に使う）</summary>
    public required string ColumnName { get; init; }

    /// <summary>
    /// 表示名機構（VO 無効時の <c>GetDisplayName</c> ヘルパ）へ渡す列の説明（C# 文字列リテラルへエスケープ済み）。
    /// 説明が空・空白のみなら <c>null</c>（ヘルパ呼び出しには <c>null</c> リテラルを渡し、プロパティ名へフォールバックさせる）。
    /// </summary>
    public string? DisplayNameDescription { get; init; }

    /// <summary>XML doc summary へ埋め込む列の説明（XML エスケープ・改行畳み込み済み）。空なら定型文へフォールバックする。フィールド・公開バインディングプロパティ両方のコメントで共用する</summary>
    public required string DescriptionXmlDoc { get; init; }

    /// <summary>通常プロパティの型名（例: int, string?, Guid）</summary>
    public required string TypeName { get; init; }

    /// <summary>通常プロパティのバッキングフィールド名（例: _customerId）</summary>
    public required string FieldName { get; init; }

    /// <summary>バインディング用プロパティ名（例: BindingCustomerId）</summary>
    public required string BindingPropertyName { get; init; }

    /// <summary>バインディング用バッキングフィールド名（例: _bindingCustomerId）</summary>
    public required string BindingFieldName { get; init; }

    /// <summary>バインディング setter で変換に TryParse が必要かどうか（string 型は false）</summary>
    public required bool NeedsParse { get; init; }

    /// <summary>TryParse に使う型名（例: int, Guid）NeedsParse=true のときのみ有効</summary>
    public required string ParseTypeName { get; init; }

    /// <summary>通常プロパティのフィールド初期化子の式（例: = string.Empty）</summary>
    public required string FieldInitializer { get; init; }

    /// <summary>バインディングフィールドの初期化子の式</summary>
    public required string BindingFieldInitializer { get; init; }

    /// <summary>NULL 許容かどうか</summary>
    public required bool IsNullable { get; init; }

    /// <summary>必須項目（Entity 側が非 NULL）かどうか</summary>
    /// <remarks>行バージョン列（<see cref="IsRowVersion"/>）は DB 採番のため非 NULL でも必須にしない。</remarks>
    public required bool IsRequired { get; init; }

    /// <summary>
    /// DB が値を生成する行バージョン列（rowversion / timestamp）かどうか。
    /// </summary>
    /// <remarks>必須検証の除外と、Mapper の「入力があるときだけ代入」への切り替えに使う。</remarks>
    public bool IsRowVersion { get; init; }

    /// <summary>参照型かどうか</summary>
    public required bool IsReferenceType { get; init; }

    /// <summary>byte[] 系プロパティかどうか</summary>
    public required bool IsBinary { get; init; }

    /// <summary>確定値からバインディング文字列へ戻す式</summary>
    public required string RevertBindingExpression { get; init; }

    /// <summary>
    /// 検証メッセージ（必須・入力変換）へ渡す表示名の C# 式。
    /// VO 有効時は <c>{VoClass}.DisplayName</c>、VO 無効時は <c>GetDisplayName(nameof(Prop), "説明")</c>（説明なしは <c>null</c>）。
    /// EditModel が表示名衝突（<see cref="CSharpEditModelClassModel.HasDisplayNameCollision"/>）のときは従来どおり <c>nameof(Prop)</c>。
    /// クラス構築時（<c>with</c>）に確定するため既定は空文字列。
    /// </summary>
    public string DisplayNameExpression { get; init; } = string.Empty;

    /// <summary>確定値が値オブジェクト型かどうか（true なら確定値は VO?、バインド setter は TryCreate で検証して生成する）</summary>
    public bool IsValueObject { get; init; }

    /// <summary>値オブジェクトのクラス名（IsValueObject=true のときのみ有効）</summary>
    public string ValueObjectClassName { get; init; } = string.Empty;
}
