namespace QuickER.CodeGen.CSharp;

/// <summary>
/// 生成テンプレートが列・ナビゲーションに依らず発行する「固定メンバー名」と、表示名機構が予約する名前の名簿。
/// </summary>
/// <remarks>
/// <para>
/// ここは名簿の唯一の正本で、次の 2 者が参照する:
/// (1) シンボル表検証（<c>CSharpGenerationModelBuilder.ValidateGeneratedMemberNames</c>）＝列・ナビゲーション由来の名前が
/// 固定メンバーと衝突する出力（CS0102 でコンパイル不能）を Error として止める、
/// (2) ドリフトガードテスト（<c>GeneratedFixedMemberDriftTests</c>）＝実生成した EditModel / Entity クラスの宣言メンバーから
/// 列由来の派生名を差し引いた残余が本名簿と完全一致することを表明し、テンプレートへ固定メンバーが増えた瞬間に落ちる。
/// </para>
/// <para>
/// 収録するのは「派生クラス（生成クラス）自身が宣言する」名前だけに限る。基底クラス（<c>EditModelBase</c> /
/// <c>EntityBase</c>）にしかないメンバー名（<c>Validate</c> / <c>AcceptChanges</c> / <c>SetProperty</c> 等）は、
/// 同名の列プロパティが出ても CS0108 の警告になるだけでコンパイルは通る。名簿へ入れると正当な図を誤って弾くため入れない。
/// </para>
/// <para>
/// 公開しているのはドリフトガードテストが参照するため（このアセンブリに <c>InternalsVisibleTo</c> は無く、
/// テストは公開 API 越しに検証する方針）。生成物側の名前ではなく、生成器が予約する名前の一覧である。
/// </para>
/// </remarks>
public static class GeneratedFixedMemberNames
{
    /// <summary>
    /// EditModel クラスが無条件に宣言する固定メンバー名（<c>Templates/CSharpRuntime/_03_EditModelsAndMappers.scriban</c>）。
    /// </summary>
    /// <remarks>
    /// 発行順: 列テーブル <c>_editModelColumns</c> / <c>EditModelColumns</c>・<c>ValidateSelf</c>・<c>OnValidate</c>・
    /// <c>BeginEditCore</c>・<c>OnBeginEdit</c>・<c>EndEditCore</c>・<c>OnEndEdit</c>・<c>OnCancelEditCore</c>・<c>OnCancelEdit</c>。
    /// 位置ヘルパー（GetNext / GetPrevious / ParentCollection / MoveCore）と、列テーブル駆動の共通実装
    /// （<c>RevertCore</c>・<c>RegisterDuplicateError</c>・確定値スナップショット・<c>CancelEditCore</c>）は
    /// <c>EditModelBase&lt;TSelf&gt;</c>（CRTP 層）側の宣言のため per-type には出ない。文言は中央リゾルバ
    /// <c>EditModelMessages</c> が解決する（per-type の文言フックは無い）。
    /// </remarks>
    public static IReadOnlySet<string> EditModelAlways { get; } =
        Create(
            "_editModelColumns",
            "EditModelColumns",
            "ValidateSelf",
            "OnValidate",
            "BeginEditCore",
            "OnBeginEdit",
            "EndEditCore",
            "OnEndEdit",
            "OnCancelEditCore",
            "OnCancelEdit"
        );

    /// <summary>
    /// <c>EditModelBase&lt;TSelf&gt;</c>（CRTP 層）が宣言する位置ヘルパーの予約名。
    /// </summary>
    /// <remarks>
    /// 基底（<c>EditModelBase&lt;TSelf&gt;</c>）が宣言するため生成 EditModel には出ないが、列由来プロパティが
    /// この名前を取ると基底メンバを隠して型付きの面が壊れるため、衝突は生成エラーにする
    /// （＝シンボル表への登録は続け、名簿照合〔per-type の実宣言〕からは外す）。
    /// </remarks>
    public static IReadOnlySet<string> EditModelPositionHelpers { get; } =
        Create("GetNext", "GetPrevious", "ParentCollection", "MoveCore");

    /// <summary>
    /// カスケード対象（子方向）のナビゲーションを持つ EditModel だけが宣言する固定メンバー名（L1267 <c>RegisterChildren</c>）。
    /// </summary>
    public static IReadOnlySet<string> EditModelWithCascadeNavigations { get; } =
        Create("RegisterChildren");

    /// <summary>
    /// 親モデルの型が一意に定まる EditModel だけが宣言する固定メンバー名（L1322 <c>ParentModel</c>）。
    /// </summary>
    public static IReadOnlySet<string> EditModelWithTypedParentModel { get; } =
        Create("ParentModel");

    /// <summary>
    /// Repository 契約面（<c>I{Entity}Repository</c>）が生成される EditModel だけが宣言する固定メンバー名
    /// （DB 照合糖衣 <c>ValidateUniqueAsync</c>）。
    /// </summary>
    /// <remarks>
    /// 発行条件は「Repository 契約の生成が有効」かつ「単一主キー」（＝そのエンティティの <c>I{Entity}Repository</c> が生成される）。
    /// 契約面が無い構成では呼び出し先が存在しないため、メソッドごと出さない。
    /// </remarks>
    public static IReadOnlySet<string> EditModelWithRepositoryFace { get; } =
        Create("ValidateUniqueAsync");

    /// <summary>
    /// テーブルに UNIQUE 制約がある EditModel だけが宣言する固定メンバー名
    /// （制約テーブル <c>_uniquenessConstraints</c> と <c>UniquenessConstraints</c> の override）。
    /// </summary>
    /// <remarks>
    /// コレクション内重複検証（<c>EditModelUniquenessValidator</c>）の入力を「属性のリフレクション」ではなく
    /// 生成コードで宣言するため、制約を持つテーブルの EditModel にだけ発行される。制約が無ければ基底の
    /// 既定実装（空リスト）のままなので、同名の列があっても衝突しない。
    /// </remarks>
    public static IReadOnlySet<string> EditModelWithUniqueConstraints { get; } =
        Create("_uniquenessConstraints", "UniquenessConstraints");

    /// <summary>
    /// EditModel の表示名解決ヘルパ名。テンプレートは発行するが、シンボル表検証の対象には**しない**。
    /// </summary>
    /// <remarks>
    /// 列由来プロパティ名がこれらと一致した場合は、表示名機構そのものを省略して生成を完走させる救済
    /// （<c>CodeGen_Warning_EditModelDisplayNameCollision</c> の Warning）が先に働くため、Error にすると救済済みの図を誤って弾く。
    /// 表示名の差し替えは中央リゾルバ <c>GeneratedDisplayNames.Resolve</c> が担う
    /// （per-type の差し替えフックは無い）＝予約するのは解決ヘルパ 1 つだけ。
    /// </remarks>
    public static IReadOnlySet<string> EditModelDisplayNameHelpers { get; } =
        Create("GetDisplayName");

    /// <summary>
    /// テーブル説明があり、かつ表示名衝突が無い Entity だけが宣言する固定メンバー名
    /// （<c>Templates/CSharpRuntime/_02_Entities.scriban</c> L229-L231 <c>DefaultDisplayName</c> の override）。
    /// </summary>
    /// <remarks>
    /// 発行条件が「テーブル説明あり」なので、説明の無いテーブルでは同名の列があっても衝突しない（診断も出さない）。
    /// </remarks>
    public static IReadOnlySet<string> EntityWithTableDescription { get; } =
        Create("DefaultDisplayName");

    /// <summary>
    /// Entity の表示名機構が予約する名前。列由来プロパティ名がこれと一致すると <c>DefaultDisplayName</c> の
    /// override を省略し（基底のクラス名フォールバックへ委ねる）、Warning で通知する。
    /// </summary>
    /// <remarks>
    /// 表示名の差し替えは中央リゾルバ <c>GeneratedDisplayNames.Resolve</c> が担う
    /// （Entity 側に差し替えフックは無い）＝予約するのは <c>DisplayName</c> プロパティ 1 つだけ。
    /// </remarks>
    public static IReadOnlySet<string> EntityDisplayNameReserved { get; } = Create("DisplayName");

    /// <summary>
    /// Entity / EditModel の両方で予約する、基底の状態管理メンバー名（<c>RowState</c>）。
    /// </summary>
    /// <remarks>
    /// 「基底クラスにしかない名前は名簿へ入れない」方針（クラス remarks）の意図的な例外。<c>RowState</c> は
    /// Mapper の状態転送 <c>entity.RowState = editModel.RowState;</c> が**名前で**束縛するため、列由来プロパティが
    /// この名前を取ると転送先が基底の状態から列プロパティへ静かに差し替わり、基底の <c>RowState</c> が更新されない
    /// （NULL 許容列・string 列は代入互換でコンパイルが通り、既存行の編集が「変更なし」として保存から抜け落ちる。
    /// 非 NULL 値型だけが CS0266 で止まる）。CS0108 の警告どまりで済まない実害があるため衝突は生成エラーにする。
    /// 位置ヘルパーと同じく per-type の実宣言には出ない＝シンボル表検証にのみ登録し、名簿照合からは外す。
    /// </remarks>
    public static IReadOnlySet<string> StateTransferReserved { get; } = Create("RowState");

    /// <summary>序数比較の読み取り専用集合を組み立てる（メンバー名は識別子なので大文字小文字を区別する）</summary>
    private static IReadOnlySet<string> Create(params string[] names) =>
        new HashSet<string>(names, StringComparer.Ordinal);
}
