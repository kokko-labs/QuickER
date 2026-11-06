using System.Globalization;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace QuickER.Tests.CodeGen.CSharp.CodegenConfig.Judges;

/// <summary>
/// 決まった行が生成コードの宣言（Roslyn のシンボルと構文木）へ表明する期待 1 つ
/// </summary>
/// <remarks>
/// <para>
/// 生成テキストの部分文字列・正規表現・行では判定しない。見るのは、型とメンバーのシンボル（名前空間・宣言するファイル・種類・修飾子・基底・
/// インターフェイス・シグネチャ）、属性とその引数、ファイルの using ディレクティブの集合（並びは見ない）、意味モデルでの参照と呼び出し、
/// シンボルに付いた XmlDoc の要素。
/// </para>
/// <para>
/// 型は単純名（<see cref="ISymbol.Name"/>。ジェネリックの型引数を含まない）で引き、生成物と手動実装のソースに宣言されたものだけを数える
/// （参照アセンブリの型は数えない）。シグネチャ・型の表記は <see cref="Format"/>（名前空間を付けない・組み込み型はキーワード・
/// null 許容の参照型は <c>?</c>）で書く。
/// </para>
/// </remarks>
internal abstract record CodeRule
{
    /// <summary>型・シグネチャ・属性の引数を書く表記（名前空間を付けない）</summary>
    public static SymbolDisplayFormat Format { get; } =
        new(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameOnly,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            memberOptions: SymbolDisplayMemberOptions.IncludeParameters
                | SymbolDisplayMemberOptions.IncludeType
                | SymbolDisplayMemberOptions.IncludeRef,
            propertyStyle: SymbolDisplayPropertyStyle.ShowReadWriteDescriptor,
            parameterOptions: SymbolDisplayParameterOptions.IncludeType
                | SymbolDisplayParameterOptions.IncludeName
                | SymbolDisplayParameterOptions.IncludeDefaultValue
                | SymbolDisplayParameterOptions.IncludeParamsRefOut
                | SymbolDisplayParameterOptions.IncludeExtensionThis,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes
                | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
                | SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers
        );

    /// <summary>すべての <c>.g.cs</c> を選ぶファイル名</summary>
    public const string AllFiles = "*";

    /// <summary>照合する（問題を返す）</summary>
    /// <param name="context">行のコンパイル</param>
    public abstract IEnumerable<string> Check(CodeRuleContext context);

    // ===== 型 =====

    /// <summary>
    /// 単純名 <paramref name="name"/> の型が <paramref name="count"/> 個あり、指定した形を持つ（null の項目は見ない）
    /// </summary>
    /// <param name="name">型の単純名（<c>&lt;</c> を含めば型引数まで書いた表記 <see cref="Format"/> と比べる。例 <c>EfCoreRepositoryCore&lt;TEntity, TKey, TContext&gt;</c>）</param>
    /// <param name="ns">名前空間（完全一致。null なら見ない）</param>
    /// <param name="file">宣言のどれかがあるファイル名（null なら見ない）</param>
    /// <param name="kind">種類（<c>class</c>・<c>interface</c>・<c>struct</c>・<c>record</c>・<c>enum</c>）</param>
    /// <param name="modifiers">宣言の修飾子の集合（空白区切り。全部の宣言の和と完全一致。例 <c>public sealed partial</c>）</param>
    /// <param name="baseType">基底の型（<see cref="Format"/>。<c>object</c> は「基底なし」として空文字列で書く）</param>
    /// <param name="interfaces">直接実装するインターフェイス（<see cref="Format"/>・宣言の順で完全一致）</param>
    /// <param name="count">名前空間・ファイルで絞った後の型の数</param>
    public static CodeRule Type(
        string name,
        string? ns = null,
        string? file = null,
        string? kind = null,
        string? modifiers = null,
        string? baseType = null,
        IReadOnlyList<string>? interfaces = null,
        int count = 1
    ) => new TypeRule(name, ns, file, kind, modifiers, baseType, interfaces, count);

    /// <summary>名前 <paramref name="name"/> の型が（<paramref name="ns"/> を指定すればその名前空間に）型引数の数を問わず宣言されていない</summary>
    /// <param name="name">型の単純名</param>
    /// <param name="ns">名前空間（null ならどこにも無い）</param>
    public static CodeRule NoType(string name, string? ns = null) =>
        new TypeRule(name, ns, null, null, null, null, null, 0);

    /// <summary>条件に当たる型が 1 つも宣言されていない（名前の一部で探すときなど）</summary>
    /// <param name="description">条件の説明（問題の文に使う）</param>
    /// <param name="predicate">条件</param>
    public static CodeRule NoTypes(string description, Func<INamedTypeSymbol, bool> predicate) =>
        new NoTypesRule(description, predicate);

    // ===== メンバー =====

    /// <summary>
    /// 型 <paramref name="type"/> が名前 <paramref name="member"/> のメンバーを宣言し、シグネチャが <paramref name="signature"/> のものが
    /// <paramref name="count"/> 個ある（シグネチャが null なら名前の数を見る）
    /// </summary>
    /// <param name="type">型の単純名（ちょうど 1 つに決まること。決まらなければ <paramref name="ns"/> で絞る）</param>
    /// <param name="member">メンバー名（コンストラクタは <c>.ctor</c>）</param>
    /// <param name="signature">シグネチャ（<see cref="Format"/>。例 <c>Task ReadPhotoAsync(int id, Stream destination, CancellationToken cancellationToken = default)</c>・<c>int Validate { get; set; }</c>）</param>
    /// <param name="modifiers">シグネチャが一致したメンバー（null ならその名前の最初のメンバー）の修飾子の集合（空白区切り・完全一致）</param>
    /// <param name="count">数</param>
    /// <param name="ns">型の名前空間</param>
    public static CodeRule Member(
        string type,
        string member,
        string? signature = null,
        string? modifiers = null,
        int count = 1,
        string? ns = null
    ) => new MemberRule(type, member, signature, modifiers, count, ns);

    /// <summary>型 <paramref name="type"/> が名前 <paramref name="member"/> のメンバーを宣言しない</summary>
    /// <param name="type">型の単純名</param>
    /// <param name="member">メンバー名</param>
    /// <param name="ns">型の名前空間</param>
    public static CodeRule NoMember(string type, string member, string? ns = null) =>
        new MemberRule(type, member, null, null, 0, ns);

    /// <summary>型 <paramref name="type"/> が宣言するメンバーの名前の集合（コンストラクタと暗黙のものを除く）に <paramref name="member"/> が無い・かつ基底にも無い</summary>
    /// <param name="type">型の単純名</param>
    /// <param name="member">メンバー名</param>
    public static CodeRule NoMemberInHierarchy(string type, string member) =>
        new NoMemberInHierarchyRule(type, member);

    /// <summary>
    /// 型 <paramref name="type"/> のメソッド <paramref name="member"/> と、型 <paramref name="otherType"/> のメソッド
    /// <paramref name="otherMember"/> の本体（ブロックか式の本体）が、構文として等価（<c>SyntaxFactory.AreEquivalent</c>。
    /// 空白・コメントは見ない）。どちらもちょうど 1 つの宣言を持つこと
    /// </summary>
    /// <remarks>
    /// 同じ条件の訳し方を 2 つの実装（QuickER 版と EF Core 版・方言ごとの Repository）が共有することを、本体のテキストの部分一致でなく
    /// 構文木で言う。
    /// </remarks>
    /// <param name="type">型の単純名</param>
    /// <param name="member">メソッド名</param>
    /// <param name="otherType">比べる型の単純名</param>
    /// <param name="otherMember">比べるメソッド名</param>
    /// <param name="ns">型の名前空間</param>
    /// <param name="otherNs">比べる型の名前空間</param>
    public static CodeRule SameBody(
        string type,
        string member,
        string otherType,
        string otherMember,
        string? ns = null,
        string? otherNs = null
    ) => new SameBodyRule(type, member, ns, otherType, otherMember, otherNs);

    // ===== 属性 =====

    /// <summary>
    /// 型 <paramref name="type"/>（<paramref name="member"/> を指定すればそのメンバー）に属性 <paramref name="attribute"/> が付く。
    /// 引数つきで書けば、引数まで完全一致（例 <c>SqlColumnType(SqlDbType.NVarChar, Size = 50)</c>・<c>Column("customer_id")</c>）
    /// </summary>
    /// <param name="type">型の単純名</param>
    /// <param name="member">メンバー名（null なら型そのもの）</param>
    /// <param name="attribute">属性名（<c>Attribute</c> を外した名前。括弧を書けば引数も比べる）</param>
    /// <param name="ns">型の名前空間</param>
    public static CodeRule Attribute(
        string type,
        string? member,
        string attribute,
        string? ns = null
    ) => new AttributeRule(type, member, attribute, Present: true, ns);

    /// <summary>型 <paramref name="type"/>（<paramref name="member"/> を指定すればそのメンバー）に属性 <paramref name="attribute"/>（名前だけで比べる）が付かない</summary>
    /// <param name="type">型の単純名</param>
    /// <param name="member">メンバー名（null なら型そのもの）</param>
    /// <param name="attribute">属性名（<c>Attribute</c> を外した名前）</param>
    /// <param name="ns">型の名前空間</param>
    public static CodeRule NoAttribute(
        string type,
        string? member,
        string attribute,
        string? ns = null
    ) => new AttributeRule(type, member, attribute, Present: false, ns);

    /// <summary>
    /// ソースの全部のプロパティ（<paramref name="type"/> を指定すればその型のプロパティ）のうち、属性 <paramref name="attribute"/>（名前だけ）が付くものが
    /// ちょうど <paramref name="count"/> 個
    /// </summary>
    /// <param name="attribute">属性名（<c>Attribute</c> を外した名前）</param>
    /// <param name="count">数</param>
    /// <param name="type">型の単純名（null なら全部の型）</param>
    public static CodeRule PropertiesWithAttribute(
        string attribute,
        int count,
        string? type = null
    ) => new PropertiesWithAttributeRule(attribute, count, type);

    /// <summary>型 <paramref name="type"/> の全部のプロパティ（静的と継承を除く）に、名前 <paramref name="attribute"/> の属性が付く</summary>
    /// <param name="type">型の単純名</param>
    /// <param name="attribute">属性名（<c>Attribute</c> を外した名前）</param>
    public static CodeRule EveryPropertyHasAttribute(string type, string attribute) =>
        new EveryPropertyHasAttributeRule(type, attribute);

    // ===== using =====

    /// <summary>ファイル <paramref name="file"/>（<see cref="AllFiles"/> なら全部の <c>.g.cs</c>）に <c>using {ns};</c> がちょうど 1 つある（重複も赤）</summary>
    /// <param name="file">ファイル名</param>
    /// <param name="ns">名前空間（エイリアス・static は扱わない）</param>
    public static CodeRule Using(string file, string ns) => new UsingRule(file, ns, Present: true);

    /// <summary>ファイル <paramref name="file"/>（<see cref="AllFiles"/> なら全部の <c>.g.cs</c>）に <c>using {ns};</c> が無い</summary>
    /// <param name="file">ファイル名</param>
    /// <param name="ns">名前空間</param>
    public static CodeRule NoUsing(string file, string ns) =>
        new UsingRule(file, ns, Present: false);

    /// <summary>ファイル <paramref name="file"/> に、名前空間 <paramref name="prefix"/>（とその下）の using が 1 つも無い</summary>
    /// <param name="file">ファイル名（<see cref="AllFiles"/> なら全部の <c>.g.cs</c>）</param>
    /// <param name="prefix">名前空間</param>
    public static CodeRule NoUsingUnder(string file, string prefix) =>
        new NoUsingUnderRule(file, prefix);

    // ===== 参照と呼び出し（意味モデル） =====

    /// <summary>
    /// ファイル <paramref name="file"/>（<see cref="AllFiles"/> なら全部の <c>.g.cs</c>）の名前（using・namespace の宣言を除く）が、名前空間
    /// <paramref name="ns"/>（とその下）の型・メンバーを 1 つも参照しない
    /// </summary>
    /// <param name="file">ファイル名</param>
    /// <param name="ns">名前空間</param>
    public static CodeRule NoReferencesTo(string file, string ns) =>
        new NoReferencesToRule(file, ns);

    /// <summary>
    /// 型 <paramref name="type"/> のメンバー <paramref name="member"/>（同名の全部の本体）の中で、<paramref name="target"/> を呼ぶ式が
    /// <paramref name="count"/> 個ある（count が null なら 1 個以上）
    /// </summary>
    /// <param name="type">型の単純名</param>
    /// <param name="member">メンバー名</param>
    /// <param name="target">呼ぶメソッド（<c>メソッド名</c>・<c>型名.メソッド名</c>。型引数を見るなら <c>Register&lt;OrderEntity&gt;</c>）</param>
    /// <param name="count">数</param>
    /// <param name="ns">型の名前空間</param>
    public static CodeRule Calls(
        string type,
        string member,
        string target,
        int? count = null,
        string? ns = null
    ) => new CallsRule(type, member, target, count, ns);

    /// <summary>
    /// 型 <paramref name="type"/> のメンバー <paramref name="member"/> の本体で、<paramref name="targets"/> に挙げたメソッドを呼ぶ式を
    /// 出てくる順に並べると <paramref name="targets"/> の順と完全一致する（挙げていない呼び出しは数えない）
    /// </summary>
    /// <param name="type">型の単純名</param>
    /// <param name="member">メンバー名</param>
    /// <param name="targets">呼ぶメソッド（<see cref="Calls"/> と同じ書き方。同じ名前を何度挙げてもよい）</param>
    /// <param name="ns">型の名前空間</param>
    public static CodeRule CallOrder(
        string type,
        string member,
        IReadOnlyList<string> targets,
        string? ns = null
    ) => new CallOrderRule(type, member, targets, ns);

    /// <summary>
    /// 型 <paramref name="type"/> のメンバー <paramref name="member"/> の本体で、<paramref name="target"/> を呼ぶ式のうち、引数が
    /// <paramref name="arguments"/> と完全一致するものがちょうど <paramref name="count"/> 個ある
    /// </summary>
    /// <remarks>
    /// 引数は意味モデルで書き直して比べる（構文の綴りでなく束縛の結果）：定数は値（文字列は二重引用符つき）、引数・ローカル・フィールド・プロパティは
    /// 名前（静的メンバーは <c>型名.名前</c>）、<c>this</c> は <c>this</c>、それ以外は <c>?</c>。<paramref name="receiver"/> を指定すれば、呼び出しの
    /// 受け手（<c>_inner.X(...)</c> の <c>_inner</c>）も同じ書き方で比べる。<paramref name="within"/> を指定すれば、その呼び出しの引数の中
    /// （ラムダの本体を含む）にある呼び出しだけを数える。
    /// </remarks>
    /// <param name="type">型の単純名</param>
    /// <param name="member">メンバー名</param>
    /// <param name="target">呼ぶメソッド（<see cref="Calls"/> と同じ書き方）</param>
    /// <param name="arguments">引数の書き直し（例 <c>group</c> と二重引用符つきの <c>SyncNote</c>）</param>
    /// <param name="receiver">受け手の書き直し（null なら見ない）</param>
    /// <param name="within">外側の呼び出し（null なら見ない）</param>
    /// <param name="count">数</param>
    /// <param name="ns">型の名前空間</param>
    public static CodeRule CallArguments(
        string type,
        string member,
        string target,
        IReadOnlyList<string> arguments,
        string? receiver = null,
        string? within = null,
        int count = 1,
        string? ns = null
    ) => new CallArgumentsRule(type, member, target, arguments, receiver, within, count, ns);

    /// <summary>
    /// 型 <paramref name="type"/> のメンバー <paramref name="member"/>（同名の全部の本体）の中で、型 <paramref name="createdType"/> を <c>new</c> する式が
    /// ちょうど <paramref name="count"/> 個ある（<paramref name="arguments"/> を指定すれば、引数が完全一致するものだけを数える）
    /// </summary>
    /// <remarks>
    /// 作る型は意味モデルで束縛した型（<c>new()</c> の目的型指定も含む）。引数は <see cref="CallArguments"/> と同じ書き直しで比べる。
    /// 呼び出し（<see cref="Calls"/>）には数えられないコンストラクタの呼び出しを見るための問い合わせ。
    /// </remarks>
    /// <param name="type">型の単純名</param>
    /// <param name="member">メンバー名（コンストラクタは <c>.ctor</c>）</param>
    /// <param name="createdType">作る型（<c>&lt;</c> を含めば型引数まで書いた表記 <see cref="Format"/>。含まなければ単純名）</param>
    /// <param name="arguments">引数の書き直し（null なら引数を見ない）</param>
    /// <param name="count">数</param>
    /// <param name="ns">型の名前空間</param>
    public static CodeRule Creates(
        string type,
        string member,
        string createdType,
        IReadOnlyList<string>? arguments = null,
        int count = 1,
        string? ns = null
    ) => new CreatesRule(type, member, createdType, arguments, count, ns);

    /// <summary>
    /// 生成物（すべての <c>.g.cs</c>）のどこにも、<paramref name="target"/> を呼ぶ式が無い（メソッドの宣言は残っていても、誰も呼ばない）
    /// </summary>
    /// <remarks>全部の呼び出しを意味モデルで束縛するので 1 行 1〜2 秒かかる。メンバーを絞れるときは <see cref="Calls"/> の count 0 を使う。</remarks>
    /// <param name="target">呼ぶメソッド（<see cref="Calls"/> と同じ書き方。例 <c>EntityBaseCore.MarkUpdated</c>）</param>
    public static CodeRule NoCalls(string target) => new NoCallsRule(target);

    /// <summary>
    /// 型 <paramref name="type"/> のメンバー <paramref name="member"/> の本体で、<paramref name="target"/> を呼ぶ式の引数に渡したラムダの本体が参照する
    /// メンバー（<c>宣言した型名.メンバー名</c>）の集合が <paramref name="members"/> と完全一致する
    /// </summary>
    /// <remarks>
    /// 例：<c>entity.Ignore(e =&gt; e.RowState)</c> の <c>EntityBaseCore.RowState</c>。<paramref name="within"/> を指定すれば、その呼び出しの引数の中
    /// （ラムダの本体を含む）にある <paramref name="target"/> の呼び出しだけを見る（<c>modelBuilder.Entity&lt;CustomerEntity&gt;(...)</c> の中だけ、など）。
    /// 参照はプロパティ・フィールド・メソッドのシンボルで数え、ラムダの引数そのもの（<c>e</c>）は数えない。
    /// </remarks>
    /// <param name="type">型の単純名</param>
    /// <param name="member">メンバー名</param>
    /// <param name="target">呼ぶメソッド（<see cref="Calls"/> と同じ書き方）</param>
    /// <param name="members">ラムダが参照するメンバー（<c>宣言した型名.メンバー名</c>）</param>
    /// <param name="within">外側の呼び出し（null なら見ない）</param>
    /// <param name="ns">型の名前空間</param>
    public static CodeRule CallLambdaMembers(
        string type,
        string member,
        string target,
        IReadOnlyList<string> members,
        string? within = null,
        string? ns = null
    ) => new CallLambdaMembersRule(type, member, target, members, within, ns);

    /// <summary>
    /// 型 <paramref name="type"/> のメンバー <paramref name="member"/> の本体で、型 <paramref name="createdType"/> をコレクション初期化子つきで
    /// <c>new</c> する式のうち、初期化子の要素が <paramref name="elements"/> と（並びも含めて）完全一致するものがちょうど 1 つある
    /// </summary>
    /// <remarks>
    /// 要素は <see cref="CallArguments"/> と同じ書き直しで比べる（<c>nameof(ArticleEntity.Tags)</c> は定数なので文字列 <c>"Tags"</c>）。
    /// </remarks>
    /// <param name="type">型の単純名</param>
    /// <param name="member">メンバー名</param>
    /// <param name="createdType">作る型（<c>&lt;</c> を含めば型引数まで書いた表記 <see cref="Format"/>。含まなければ単純名）</param>
    /// <param name="elements">要素の書き直し</param>
    /// <param name="ns">型の名前空間</param>
    public static CodeRule CollectionInitializer(
        string type,
        string member,
        string createdType,
        IReadOnlyList<string> elements,
        string? ns = null
    ) => new CollectionInitializerRule(type, member, createdType, elements, ns);

    /// <summary>
    /// 型 <paramref name="type"/> のメンバー <paramref name="member"/> の本体で、<paramref name="target"/> を呼ぶ式のうち、渡したラムダが
    /// <paramref name="property"/>（<c>宣言した型名.メンバー名</c>）だけを参照するものがちょうど 1 つあり、その呼び出しに続けて鎖状に
    /// 呼んだメソッドの名前の並びが <paramref name="chain"/> を連続した列として含む
    /// </summary>
    /// <remarks>
    /// 例：<c>entity.Property(e =&gt; e.Code).HasColumnName("code").IsRequired().HasMaxLength(20).IsUnicode(false)</c> の
    /// <c>Property</c>（<c>ItemEntity.Code</c>）に続く鎖は <c>HasColumnName, IsRequired, HasMaxLength, IsUnicode</c>。Fluent の呼び出しが
    /// どのプロパティに付いたか、その並びを見るための問い合わせ（引数は見ない）。
    /// </remarks>
    /// <param name="type">型の単純名</param>
    /// <param name="member">メンバー名</param>
    /// <param name="target">鎖の起点の呼び出し（<see cref="Calls"/> と同じ書き方。例 <c>Property</c>）</param>
    /// <param name="property">ラムダが参照するメンバー（<c>型名.メンバー名</c>）</param>
    /// <param name="chain">続く呼び出しのメソッド名（連続した列）</param>
    /// <param name="ns">型の名前空間</param>
    public static CodeRule PropertyChain(
        string type,
        string member,
        string target,
        string property,
        IReadOnlyList<string> chain,
        string? ns = null
    ) => new PropertyChainRule(type, member, target, property, chain, ns);

    /// <summary>
    /// 型 <paramref name="type"/>（<paramref name="member"/> を指定すればそのメンバーの本体）の名前が、シンボル <paramref name="symbol"/>
    /// （<c>型名.メンバー名</c>）を参照する（<paramref name="present"/> が false なら参照しない）
    /// </summary>
    /// <param name="type">型の単純名</param>
    /// <param name="member">メンバー名（null なら型の宣言の全部）</param>
    /// <param name="symbol">参照するシンボル（<c>型名.メンバー名</c>）</param>
    /// <param name="present">参照するか</param>
    /// <param name="ns">型の名前空間</param>
    public static CodeRule ReferencesSymbol(
        string type,
        string? member,
        string symbol,
        bool present = true,
        string? ns = null
    ) => new ReferencesSymbolRule(type, member, symbol, present, ns);

    // ===== XmlDoc =====

    /// <summary>
    /// 型 <paramref name="type"/>（<paramref name="member"/> を指定すればそのメンバー）の XmlDoc の要素 <paramref name="element"/> の中身が
    /// <paramref name="expected"/> と完全一致する（空白の連なりは 1 つにまとめ、前後の空白は外して比べる。要素の中のタグは XML のまま比べる）
    /// </summary>
    /// <param name="type">型の単純名</param>
    /// <param name="member">メンバー名（null なら型そのもの）</param>
    /// <param name="element">要素名（<c>summary</c>・<c>remarks</c> など）</param>
    /// <param name="expected">期待の中身（入力＝図の Description などから決まる文）</param>
    /// <param name="ns">型の名前空間</param>
    public static CodeRule XmlDoc(
        string type,
        string? member,
        string element,
        string expected,
        string? ns = null
    ) => new XmlDocRule(type, member, element, expected, ns);

    /// <summary>
    /// XmlDoc の要素 <paramref name="element"/> の中身が <paramref name="text"/>（<see cref="XmlDoc"/> と同じ比べ方）であるシンボルの集合が、
    /// <paramref name="owners"/>（<c>型名</c> か <c>型名.メンバー名</c>）と完全一致する
    /// </summary>
    /// <param name="element">要素名</param>
    /// <param name="text">中身</param>
    /// <param name="owners">持ち主</param>
    public static CodeRule XmlDocOwners(
        string element,
        string text,
        IReadOnlyList<string> owners
    ) => new XmlDocOwnersRule(element, text, owners);

    // ===== 共有の部品 =====

    /// <summary>修飾子の集合を空白区切りで書いたもの（並びは見ない）</summary>
    protected static HashSet<string> ModifierSet(string modifiers) =>
        modifiers
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>集合を並べて書く</summary>
    protected static string Show(IEnumerable<string> items) =>
        "[" + string.Join(", ", items.Order(StringComparer.Ordinal)) + "]";

    /// <summary>属性の名前（<c>Attribute</c> を外したもの）</summary>
    protected static string AttributeName(AttributeData attribute)
    {
        var name = attribute.AttributeClass?.Name ?? "?";
        return name.EndsWith("Attribute", StringComparison.Ordinal) ? name[..^9] : name;
    }

    /// <summary>属性を引数つきで書く（例 <c>SqlColumnType(SqlDbType.NVarChar, Size = 50)</c>。引数が無ければ名前だけ）</summary>
    protected static string RenderAttribute(AttributeData attribute)
    {
        var arguments = attribute
            .ConstructorArguments.Select(RenderConstant)
            .Concat(
                attribute.NamedArguments.Select(named =>
                    $"{named.Key} = {RenderConstant(named.Value)}"
                )
            )
            .ToList();

        return arguments.Count == 0
            ? AttributeName(attribute)
            : $"{AttributeName(attribute)}({string.Join(", ", arguments)})";
    }

    /// <summary>属性の引数 1 つを書く（列挙は <c>型名.メンバー名</c>、文字列は二重引用符、配列は <c>[a, b]</c>）</summary>
    private static string RenderConstant(TypedConstant constant)
    {
        if (constant.IsNull)
        {
            return "null";
        }

        if (constant.Kind == TypedConstantKind.Array)
        {
            return "[" + string.Join(", ", constant.Values.Select(RenderConstant)) + "]";
        }

        if (constant.Kind == TypedConstantKind.Type && constant.Value is ITypeSymbol typeValue)
        {
            return $"typeof({typeValue.ToDisplayString(Format)})";
        }

        if (constant.Type is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType)
        {
            var member = enumType
                .GetMembers()
                .OfType<IFieldSymbol>()
                .FirstOrDefault(field =>
                    field.HasConstantValue && Equals(field.ConstantValue, constant.Value)
                );

            return member is null
                ? $"({enumType.Name}){Convert.ToString(constant.Value, CultureInfo.InvariantCulture)}"
                : $"{enumType.Name}.{member.Name}";
        }

        return constant.Value switch
        {
            string text => SymbolDisplay.FormatLiteral(text, quote: true),
            bool flag => flag ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            var other => other?.ToString() ?? "null",
        };
    }

    /// <summary>XmlDoc の要素の中身を比べる形にする（空白の連なりを 1 つにし、前後の空白を外す）</summary>
    protected static string NormalizeDoc(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>シンボルの XmlDoc の要素の中身（無ければ null）</summary>
    protected static string? DocElement(ISymbol symbol, string element)
    {
        var xml = symbol.GetDocumentationCommentXml(
            CultureInfo.InvariantCulture,
            expandIncludes: false
        );

        if (string.IsNullOrWhiteSpace(xml))
        {
            return null;
        }

        var found = XElement.Parse(xml).Element(element);
        return found is null
            ? null
            : NormalizeDoc(string.Concat(found.Nodes().Select(node => node.ToString())));
    }

    // ===== 規則の実体 =====

    private sealed record TypeRule(
        string Name,
        string? Ns,
        string? File,
        string? Kind,
        string? Modifiers,
        string? BaseType,
        IReadOnlyList<string>? Interfaces,
        int Count
    ) : CodeRule
    {
        public override IEnumerable<string> Check(CodeRuleContext context)
        {
            var where =
                $"{Name}{(Ns is null ? "" : $"（{Ns}）")}{(File is null ? "" : $"［{File}］")}";
            var types = context
                .TypesNamed(Name, Ns, anyArity: Count == 0)
                .Where(type =>
                    File is null
                    || type.DeclaringSyntaxReferences.Any(reference =>
                        CodeRuleContext.FileName(reference.SyntaxTree) == File
                    )
                )
                .ToList();

            if (types.Count != Count)
            {
                yield return string.Create(
                    CultureInfo.InvariantCulture,
                    $"コード: 型 {where} が {types.Count} 個（期待 {Count} 個）{CodeRuleContext.Locate(types)}"
                );
                yield break;
            }

            foreach (var type in types)
            {
                var declarations = CodeRuleContext.Declarations(type).ToList();

                if (Kind is not null)
                {
                    var kinds = declarations.Select(CodeRuleContext.KindOf).Distinct().ToList();

                    if (kinds.Count != 1 || kinds[0] != Kind)
                    {
                        yield return $"コード: 型 {where} の種類が {Show(kinds)}（期待 {Kind}）";
                    }
                }

                if (Modifiers is not null)
                {
                    var actual = declarations
                        .SelectMany(declaration => declaration.Modifiers)
                        .Select(token => token.ValueText)
                        .ToHashSet(StringComparer.Ordinal);

                    if (!actual.SetEquals(ModifierSet(Modifiers)))
                    {
                        yield return $"コード: 型 {where} の修飾子が {Show(actual)}（期待 {Show(ModifierSet(Modifiers))}）";
                    }
                }

                if (BaseType is not null)
                {
                    var actual =
                        type.BaseType is null
                        || type.BaseType.SpecialType == SpecialType.System_Object
                        || type.TypeKind != TypeKind.Class
                            ? ""
                            : type.BaseType.ToDisplayString(Format);

                    if (actual != BaseType)
                    {
                        yield return $"コード: 型 {where} の基底が {(actual.Length == 0 ? "(なし)" : actual)}（期待 {(BaseType.Length == 0 ? "(なし)" : BaseType)}）";
                    }
                }

                if (Interfaces is not null)
                {
                    var actual = type
                        .Interfaces.Select(item => item.ToDisplayString(Format))
                        .ToList();

                    if (!actual.SequenceEqual(Interfaces, StringComparer.Ordinal))
                    {
                        yield return $"コード: 型 {where} のインターフェイスが [{string.Join(", ", actual)}]（期待 [{string.Join(", ", Interfaces)}]）";
                    }
                }
            }
        }
    }

    private sealed record NoTypesRule(string Description, Func<INamedTypeSymbol, bool> Predicate)
        : CodeRule
    {
        public override IEnumerable<string> Check(CodeRuleContext context)
        {
            var found = context.Types.Where(Predicate).ToList();

            if (found.Count > 0)
            {
                yield return $"コード: {Description} の型がある {Show(found.Select(type => type.ToDisplayString()))}";
            }
        }
    }

    private sealed record MemberRule(
        string TypeName,
        string MemberName,
        string? Signature,
        string? Modifiers,
        int Count,
        string? Ns
    ) : CodeRule
    {
        public override IEnumerable<string> Check(CodeRuleContext context)
        {
            if (!context.TryGetType(TypeName, Ns, out var type, out var problem))
            {
                yield return problem;
                yield break;
            }

            var members = type.GetMembers(MemberName)
                .Where(member => !member.IsImplicitlyDeclared)
                .ToList();
            var matched = Signature is null
                ? members
                : members.Where(member => member.ToDisplayString(Format) == Signature).ToList();

            if (matched.Count != Count)
            {
                yield return string.Create(
                    CultureInfo.InvariantCulture,
                    $"コード: {TypeName}.{MemberName}{(Signature is null ? "" : $" の {Signature}")} が {matched.Count} 個（期待 {Count} 個。宣言は [{string.Join(" | ", members.Select(member => member.ToDisplayString(Format)))}]）"
                );
                yield break;
            }

            if (Modifiers is not null && matched.Count > 0)
            {
                var actual = CodeRuleContext.MemberModifiers(matched[0]);

                if (!actual.SetEquals(ModifierSet(Modifiers)))
                {
                    yield return $"コード: {TypeName}.{MemberName} の修飾子が {Show(actual)}（期待 {Show(ModifierSet(Modifiers))}）";
                }
            }
        }
    }

    private sealed record NoMemberInHierarchyRule(string TypeName, string MemberName) : CodeRule
    {
        public override IEnumerable<string> Check(CodeRuleContext context)
        {
            if (!context.TryGetType(TypeName, null, out var type, out var problem))
            {
                yield return problem;
                yield break;
            }

            for (var current = type; current is not null; current = current.BaseType)
            {
                if (current.GetMembers(MemberName).Any(member => !member.IsImplicitlyDeclared))
                {
                    yield return $"コード: {TypeName} の継承の鎖の {current.Name} がメンバー {MemberName} を宣言する";
                }
            }
        }
    }

    private sealed record SameBodyRule(
        string TypeName,
        string MemberName,
        string? Ns,
        string OtherTypeName,
        string OtherMemberName,
        string? OtherNs
    ) : CodeRule
    {
        public override IEnumerable<string> Check(CodeRuleContext context)
        {
            var label = $"{TypeName}.{MemberName} と {OtherTypeName}.{OtherMemberName}";
            var body = Body(context, TypeName, MemberName, Ns, out var problem);

            if (body is null)
            {
                yield return problem;
                yield break;
            }

            var other = Body(context, OtherTypeName, OtherMemberName, OtherNs, out problem);

            if (other is null)
            {
                yield return problem;
                yield break;
            }

            if (!SyntaxFactory.AreEquivalent(body, other, topLevel: false))
            {
                yield return $"コード: {label} の本体が構文として等価でない（{body.NormalizeWhitespace().ToFullString()} ／ {other.NormalizeWhitespace().ToFullString()}）";
            }
        }

        /// <summary>メソッドの本体（ブロックか式の本体）。宣言がちょうど 1 つでなければ null と問題の文</summary>
        private static SyntaxNode? Body(
            CodeRuleContext context,
            string typeName,
            string memberName,
            string? ns,
            out string problem
        )
        {
            if (!context.TryGetType(typeName, ns, out var type, out problem))
            {
                return null;
            }

            var declarations = type.GetMembers(memberName)
                .Where(member => !member.IsImplicitlyDeclared)
                .SelectMany(member => member.DeclaringSyntaxReferences)
                .Select(reference => reference.GetSyntax())
                .OfType<MethodDeclarationSyntax>()
                .ToList();

            if (declarations.Count != 1)
            {
                problem = string.Create(
                    CultureInfo.InvariantCulture,
                    $"コード: {typeName}.{memberName} のメソッドの宣言が {declarations.Count} 個（ちょうど 1 つに決まること）"
                );
                return null;
            }

            SyntaxNode? body = (SyntaxNode?)declarations[0].Body ?? declarations[0].ExpressionBody;

            if (body is null)
            {
                problem = $"コード: {typeName}.{memberName} に本体が無い";
            }

            return body;
        }
    }

    private sealed record AttributeRule(
        string TypeName,
        string? MemberName,
        string AttributeText,
        bool Present,
        string? Ns
    ) : CodeRule
    {
        public override IEnumerable<string> Check(CodeRuleContext context)
        {
            if (!context.TryGetType(TypeName, Ns, out var type, out var problem))
            {
                yield return problem;
                yield break;
            }

            var target = $"{TypeName}{(MemberName is null ? "" : "." + MemberName)}";
            IReadOnlyList<ISymbol> symbols = MemberName is null
                ? [type]
                : type.GetMembers(MemberName)
                    .Where(member => !member.IsImplicitlyDeclared)
                    .ToList();

            if (symbols.Count == 0)
            {
                yield return $"コード: {target} が無い";
                yield break;
            }

            var attributes = symbols.SelectMany(symbol => symbol.GetAttributes()).ToList();
            var withArguments = AttributeText.Contains('(', StringComparison.Ordinal);
            var name = withArguments ? AttributeText[..AttributeText.IndexOf('(')] : AttributeText;
            var named = attributes.Where(attribute => AttributeName(attribute) == name).ToList();

            if (!Present)
            {
                if (named.Count > 0)
                {
                    yield return $"コード: {target} に [{name}] が付く（付かないはず）";
                }

                yield break;
            }

            if (named.Count == 0)
            {
                yield return $"コード: {target} に [{name}] が無い（属性は {Show(attributes.Select(RenderAttribute))}）";
            }
            else if (
                withArguments
                && !named.Any(attribute => RenderAttribute(attribute) == AttributeText)
            )
            {
                yield return $"コード: {target} の [{name}] が {Show(named.Select(RenderAttribute))}（期待 [{AttributeText}]）";
            }
        }
    }

    private sealed record PropertiesWithAttributeRule(
        string AttributeText,
        int Count,
        string? TypeName
    ) : CodeRule
    {
        public override IEnumerable<string> Check(CodeRuleContext context)
        {
            var types = TypeName is null
                ? context.Types
                : context.TypesNamed(TypeName, null).ToList();
            var properties = types
                .SelectMany(type => type.GetMembers().OfType<IPropertySymbol>())
                .Where(property =>
                    property
                        .GetAttributes()
                        .Any(attribute => AttributeName(attribute) == AttributeText)
                )
                .ToList();

            if (properties.Count != Count)
            {
                yield return string.Create(
                    CultureInfo.InvariantCulture,
                    $"コード: [{AttributeText}] が付くプロパティが {properties.Count} 個（期待 {Count} 個）{Show(properties.Select(property => $"{property.ContainingType.Name}.{property.Name}"))}"
                );
            }
        }
    }

    private sealed record EveryPropertyHasAttributeRule(string TypeName, string AttributeText)
        : CodeRule
    {
        public override IEnumerable<string> Check(CodeRuleContext context)
        {
            if (!context.TryGetType(TypeName, null, out var type, out var problem))
            {
                yield return problem;
                yield break;
            }

            var properties = type.GetMembers()
                .OfType<IPropertySymbol>()
                .Where(property => !property.IsStatic && !property.IsImplicitlyDeclared)
                .ToList();

            if (properties.Count == 0)
            {
                yield return $"コード: {TypeName} がプロパティを持たない";
            }

            foreach (
                var property in properties.Where(property =>
                    !property
                        .GetAttributes()
                        .Any(attribute => AttributeName(attribute) == AttributeText)
                )
            )
            {
                yield return $"コード: {TypeName}.{property.Name} に [{AttributeText}] が無い";
            }
        }
    }

    private sealed record UsingRule(string File, string Ns, bool Present) : CodeRule
    {
        public override IEnumerable<string> Check(CodeRuleContext context)
        {
            var roots = context.Roots(File).ToList();

            if (roots.Count == 0)
            {
                yield return $"コード: ファイル {File} が無い";
                yield break;
            }

            foreach (var root in roots)
            {
                var count = CodeRuleContext
                    .UsingNamespaces(root)
                    .Count(ns => string.Equals(ns, Ns, StringComparison.Ordinal));

                if (count != (Present ? 1 : 0))
                {
                    yield return string.Create(
                        CultureInfo.InvariantCulture,
                        $"コード: {CodeRuleContext.FileName(root.SyntaxTree)} の using {Ns} が {count} 個（期待 {(Present ? 1 : 0)} 個）"
                    );
                }
            }
        }
    }

    private sealed record NoUsingUnderRule(string File, string Prefix) : CodeRule
    {
        public override IEnumerable<string> Check(CodeRuleContext context)
        {
            var roots = context.Roots(File).ToList();

            if (roots.Count == 0)
            {
                yield return $"コード: ファイル {File} が無い";
                yield break;
            }

            foreach (var root in roots)
            {
                var found = CodeRuleContext
                    .UsingNamespaces(root)
                    .Where(ns => CodeRuleContext.IsUnder(ns, Prefix))
                    .ToList();

                if (found.Count > 0)
                {
                    yield return $"コード: {CodeRuleContext.FileName(root.SyntaxTree)} に {Prefix} の using {Show(found)} がある（無いはず）";
                }
            }
        }
    }

    private sealed record NoReferencesToRule(string File, string Ns) : CodeRule
    {
        public override IEnumerable<string> Check(CodeRuleContext context)
        {
            var roots = context.Roots(File).ToList();

            if (roots.Count == 0)
            {
                yield return $"コード: ファイル {File} が無い";
                yield break;
            }

            foreach (var root in roots)
            {
                var model = context.Compilation.GetSemanticModel(root.SyntaxTree);
                var found = new SortedSet<string>(StringComparer.Ordinal);

                foreach (
                    var name in root.DescendantNodes()
                        .OfType<SimpleNameSyntax>()
                        .Where(name =>
                            name.FirstAncestorOrSelf<UsingDirectiveSyntax>() is null
                            && !(
                                name.FirstAncestorOrSelf<BaseNamespaceDeclarationSyntax>()
                                    is { } declaration
                                && declaration.Name.Span.Contains(name.Span)
                            )
                        )
                )
                {
                    var info = model.GetSymbolInfo(name);

                    foreach (
                        var symbol in info.Symbol is { } single ? [single] : info.CandidateSymbols
                    )
                    {
                        var ns = symbol is INamespaceSymbol nsSymbol
                            ? nsSymbol
                            : (symbol as ITypeSymbol)?.ContainingNamespace
                                ?? symbol.ContainingType?.ContainingNamespace
                                ?? symbol.ContainingNamespace;

                        if (ns is not null && CodeRuleContext.IsUnder(ns.ToDisplayString(), Ns))
                        {
                            found.Add(symbol.ToDisplayString(Format));
                        }
                    }
                }

                if (found.Count > 0)
                {
                    yield return $"コード: {CodeRuleContext.FileName(root.SyntaxTree)} が {Ns} を参照する {Show(found.Take(10))}";
                }
            }
        }
    }

    private sealed record CallsRule(
        string TypeName,
        string MemberName,
        string Target,
        int? Count,
        string? Ns
    ) : CodeRule
    {
        public override IEnumerable<string> Check(CodeRuleContext context)
        {
            if (!context.TryGetType(TypeName, Ns, out var type, out var problem))
            {
                yield return problem;
                yield break;
            }

            if (!context.TryGetCalls(type, MemberName, out var calls, out problem))
            {
                yield return problem;
                yield break;
            }

            var hits = calls.Count(call => CodeRuleContext.CallMatches(call, Target));

            if (Count is { } count ? hits != count : hits == 0)
            {
                yield return string.Create(
                    CultureInfo.InvariantCulture,
                    $"コード: {TypeName}.{MemberName} が {Target} を {hits} 回呼ぶ（期待 {(Count is { } expected ? expected.ToString(CultureInfo.InvariantCulture) : "1 回以上")}）"
                );
            }
        }
    }

    private sealed record CallOrderRule(
        string TypeName,
        string MemberName,
        IReadOnlyList<string> Targets,
        string? Ns
    ) : CodeRule
    {
        public override IEnumerable<string> Check(CodeRuleContext context)
        {
            if (!context.TryGetType(TypeName, Ns, out var type, out var problem))
            {
                yield return problem;
                yield break;
            }

            if (!context.TryGetCalls(type, MemberName, out var calls, out problem))
            {
                yield return problem;
                yield break;
            }

            var distinct = Targets.Distinct(StringComparer.Ordinal).ToList();
            var actual = calls
                .Select(call =>
                    distinct.FirstOrDefault(target => CodeRuleContext.CallMatches(call, target))
                )
                .OfType<string>()
                .ToList();

            if (!actual.SequenceEqual(Targets, StringComparer.Ordinal))
            {
                yield return $"コード: {TypeName}.{MemberName} の呼び出しの順が [{string.Join(", ", actual)}]（期待 [{string.Join(", ", Targets)}]）";
            }
        }
    }

    private sealed record CallArgumentsRule(
        string TypeName,
        string MemberName,
        string Target,
        IReadOnlyList<string> Arguments,
        string? Receiver,
        string? Within,
        int Count,
        string? Ns
    ) : CodeRule
    {
        public override IEnumerable<string> Check(CodeRuleContext context)
        {
            if (!context.TryGetType(TypeName, Ns, out var type, out var problem))
            {
                yield return problem;
                yield break;
            }

            var declarations = type.GetMembers(MemberName)
                .Where(item => !item.IsImplicitlyDeclared)
                .SelectMany(item => item.DeclaringSyntaxReferences)
                .ToList();

            if (declarations.Count == 0)
            {
                yield return $"コード: {TypeName}.{MemberName} が無い";
                yield break;
            }

            var found = new List<string>();
            var expected = Describe(Receiver, Arguments);

            foreach (var reference in declarations)
            {
                var model = context.Compilation.GetSemanticModel(reference.SyntaxTree);

                foreach (
                    var invocation in reference
                        .GetSyntax()
                        .DescendantNodes()
                        .OfType<InvocationExpressionSyntax>()
                )
                {
                    if (
                        model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
                        || !CodeRuleContext.CallMatches(CodeRuleContext.Unreduce(method), Target)
                        || (
                            Within is not null
                            && !CodeRuleContext.IsWithinCall(model, invocation, Within)
                        )
                    )
                    {
                        continue;
                    }

                    var receiver =
                        Receiver is null ? null
                        : invocation.Expression is MemberAccessExpressionSyntax access
                            ? CodeRuleContext.RenderExpression(model, access.Expression)
                        : "this";
                    found.Add(
                        Describe(
                            receiver,
                            invocation
                                .ArgumentList.Arguments.Select(argument =>
                                    CodeRuleContext.RenderExpression(model, argument.Expression)
                                )
                                .ToList()
                        )
                    );
                }
            }

            if (found.Count(item => item == expected) != Count)
            {
                yield return string.Create(
                    CultureInfo.InvariantCulture,
                    $"コード: {TypeName}.{MemberName} の {Target} の呼び出しが {Show(found)}（期待 {expected} が {Count} 個）"
                );
            }
        }

        private static string Describe(string? receiver, IReadOnlyList<string> arguments) =>
            $"{(receiver is null ? "" : receiver + ".")}({string.Join(", ", arguments)})";
    }

    private sealed record CreatesRule(
        string TypeName,
        string MemberName,
        string CreatedType,
        IReadOnlyList<string>? Arguments,
        int Count,
        string? Ns
    ) : CodeRule
    {
        public override IEnumerable<string> Check(CodeRuleContext context)
        {
            if (!context.TryGetType(TypeName, Ns, out var type, out var problem))
            {
                yield return problem;
                yield break;
            }

            var declarations = type.GetMembers(MemberName)
                .Where(item => !item.IsImplicitlyDeclared)
                .SelectMany(item => item.DeclaringSyntaxReferences)
                .ToList();

            if (declarations.Count == 0)
            {
                yield return $"コード: {TypeName}.{MemberName} が無い";
                yield break;
            }

            var found = new List<string>();

            foreach (var reference in declarations)
            {
                var model = context.Compilation.GetSemanticModel(reference.SyntaxTree);

                foreach (
                    var creation in reference
                        .GetSyntax()
                        .DescendantNodes()
                        .OfType<BaseObjectCreationExpressionSyntax>()
                )
                {
                    if (
                        model.GetTypeInfo(creation).Type is not INamedTypeSymbol created
                        || !Matches(created)
                    )
                    {
                        continue;
                    }

                    var actual = (creation.ArgumentList?.Arguments ?? [])
                        .Select(argument =>
                            CodeRuleContext.RenderExpression(model, argument.Expression)
                        )
                        .ToList();

                    if (
                        Arguments is null
                        || actual.SequenceEqual(Arguments, StringComparer.Ordinal)
                    )
                    {
                        found.Add($"({string.Join(", ", actual)})");
                    }
                }
            }

            if (found.Count != Count)
            {
                var expected = Arguments is null
                    ? ""
                    : $"（引数 ({string.Join(", ", Arguments)})）";
                yield return string.Create(
                    CultureInfo.InvariantCulture,
                    $"コード: {TypeName}.{MemberName} の new {CreatedType}{expected} が {found.Count} 個（期待 {Count} 個）"
                );
            }
        }

        private bool Matches(INamedTypeSymbol created) =>
            CreatedType.Contains('<', StringComparison.Ordinal)
                ? created.ToDisplayString(Format) == CreatedType
                : created.Name == CreatedType;
    }

    private sealed record NoCallsRule(string Target) : CodeRule
    {
        public override IEnumerable<string> Check(CodeRuleContext context)
        {
            var found = new SortedSet<string>(StringComparer.Ordinal);

            foreach (var root in context.Roots(AllFiles))
            {
                var model = context.Compilation.GetSemanticModel(root.SyntaxTree);

                foreach (
                    var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>()
                )
                {
                    if (
                        model.GetSymbolInfo(invocation).Symbol is IMethodSymbol method
                        && CodeRuleContext.CallMatches(CodeRuleContext.Unreduce(method), Target)
                    )
                    {
                        var owner = invocation
                            .Ancestors()
                            .OfType<MemberDeclarationSyntax>()
                            .Select(node => model.GetDeclaredSymbol(node))
                            .FirstOrDefault(symbol => symbol is not null);
                        found.Add(
                            owner is null
                                ? CodeRuleContext.FileName(root.SyntaxTree)
                                : $"{owner.ContainingType?.Name ?? owner.Name}.{owner.Name}"
                        );
                    }
                }
            }

            if (found.Count > 0)
            {
                yield return $"コード: {Target} を呼ぶ式がある（呼ばないはず）{Show(found)}";
            }
        }
    }

    private sealed record CallLambdaMembersRule(
        string TypeName,
        string MemberName,
        string Target,
        IReadOnlyList<string> Members,
        string? Within,
        string? Ns
    ) : CodeRule
    {
        public override IEnumerable<string> Check(CodeRuleContext context)
        {
            if (!context.TryGetType(TypeName, Ns, out var type, out var problem))
            {
                yield return problem;
                yield break;
            }

            var declarations = type.GetMembers(MemberName)
                .Where(item => !item.IsImplicitlyDeclared)
                .SelectMany(item => item.DeclaringSyntaxReferences)
                .ToList();

            if (declarations.Count == 0)
            {
                yield return $"コード: {TypeName}.{MemberName} が無い";
                yield break;
            }

            var found = new HashSet<string>(StringComparer.Ordinal);

            foreach (var reference in declarations)
            {
                var model = context.Compilation.GetSemanticModel(reference.SyntaxTree);

                foreach (
                    var invocation in reference
                        .GetSyntax()
                        .DescendantNodes()
                        .OfType<InvocationExpressionSyntax>()
                )
                {
                    if (
                        model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
                        || !CodeRuleContext.CallMatches(CodeRuleContext.Unreduce(method), Target)
                        || (
                            Within is not null
                            && !CodeRuleContext.IsWithinCall(model, invocation, Within)
                        )
                    )
                    {
                        continue;
                    }

                    foreach (
                        var lambda in invocation
                            .ArgumentList.Arguments.Select(argument => argument.Expression)
                            .OfType<LambdaExpressionSyntax>()
                    )
                    {
                        foreach (
                            var name in lambda
                                .Body.DescendantNodesAndSelf()
                                .OfType<SimpleNameSyntax>()
                        )
                        {
                            if (
                                model.GetSymbolInfo(name).Symbol
                                is (IPropertySymbol or IFieldSymbol or IMethodSymbol)
                                    and { ContainingType: { } owner } symbol
                            )
                            {
                                found.Add($"{owner.Name}.{symbol.Name}");
                            }
                        }
                    }
                }
            }

            if (!found.SetEquals(Members))
            {
                yield return $"コード: {TypeName}.{MemberName} の {Target} に渡したラムダが参照するメンバーが {Show(found)}（期待 {Show(Members)}）";
            }
        }
    }

    private sealed record CollectionInitializerRule(
        string TypeName,
        string MemberName,
        string CreatedType,
        IReadOnlyList<string> Elements,
        string? Ns
    ) : CodeRule
    {
        public override IEnumerable<string> Check(CodeRuleContext context)
        {
            if (!context.TryGetType(TypeName, Ns, out var type, out var problem))
            {
                yield return problem;
                yield break;
            }

            var declarations = type.GetMembers(MemberName)
                .Where(item => !item.IsImplicitlyDeclared)
                .SelectMany(item => item.DeclaringSyntaxReferences)
                .ToList();

            if (declarations.Count == 0)
            {
                yield return $"コード: {TypeName}.{MemberName} が無い";
                yield break;
            }

            var found = new List<string>();

            foreach (var reference in declarations)
            {
                var model = context.Compilation.GetSemanticModel(reference.SyntaxTree);

                foreach (
                    var creation in reference
                        .GetSyntax()
                        .DescendantNodes()
                        .OfType<BaseObjectCreationExpressionSyntax>()
                )
                {
                    if (
                        creation.Initializer
                            is not {
                                RawKind: (int)SyntaxKind.CollectionInitializerExpression
                            } initializer
                        || model.GetTypeInfo(creation).Type is not INamedTypeSymbol created
                        || !(
                            CreatedType.Contains('<', StringComparison.Ordinal)
                                ? created.ToDisplayString(Format) == CreatedType
                                : created.Name == CreatedType
                        )
                    )
                    {
                        continue;
                    }

                    found.Add(
                        string.Join(
                            ", ",
                            initializer.Expressions.Select(expression =>
                                CodeRuleContext.RenderExpression(model, expression)
                            )
                        )
                    );
                }
            }

            var expected = string.Join(", ", Elements);

            if (found.Count(actual => actual == expected) != 1 || found.Count != 1)
            {
                yield return $"コード: {TypeName}.{MemberName} の {CreatedType} の初期化子の要素が [{string.Join(" / ", found)}]（期待 1 つだけ [{expected}]）";
            }
        }
    }

    private sealed record PropertyChainRule(
        string TypeName,
        string MemberName,
        string Target,
        string Property,
        IReadOnlyList<string> Chain,
        string? Ns
    ) : CodeRule
    {
        public override IEnumerable<string> Check(CodeRuleContext context)
        {
            if (!context.TryGetType(TypeName, Ns, out var type, out var problem))
            {
                yield return problem;
                yield break;
            }

            var declarations = type.GetMembers(MemberName)
                .Where(item => !item.IsImplicitlyDeclared)
                .SelectMany(item => item.DeclaringSyntaxReferences)
                .ToList();

            if (declarations.Count == 0)
            {
                yield return $"コード: {TypeName}.{MemberName} が無い";
                yield break;
            }

            var chains = new List<List<string>>();

            foreach (var reference in declarations)
            {
                var model = context.Compilation.GetSemanticModel(reference.SyntaxTree);

                foreach (
                    var invocation in reference
                        .GetSyntax()
                        .DescendantNodes()
                        .OfType<InvocationExpressionSyntax>()
                )
                {
                    if (
                        model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
                        || !CodeRuleContext.CallMatches(CodeRuleContext.Unreduce(method), Target)
                    )
                    {
                        continue;
                    }

                    var referenced = invocation
                        .ArgumentList.Arguments.Select(argument => argument.Expression)
                        .OfType<LambdaExpressionSyntax>()
                        .SelectMany(lambda =>
                            lambda.Body.DescendantNodesAndSelf().OfType<SimpleNameSyntax>()
                        )
                        .Select(name => model.GetSymbolInfo(name).Symbol)
                        .OfType<ISymbol>()
                        .Where(symbol => symbol is IPropertySymbol or IFieldSymbol)
                        .Select(symbol => $"{symbol.ContainingType.Name}.{symbol.Name}")
                        .Distinct(StringComparer.Ordinal)
                        .ToList();

                    if (referenced.Count != 1 || referenced[0] != Property)
                    {
                        continue;
                    }

                    // 鎖：起点の呼び出しを受け手とする外側の呼び出しを、外へ向かってたどる
                    var names = new List<string>();
                    SyntaxNode current = invocation;

                    while (
                        current.Parent is MemberAccessExpressionSyntax access
                        && access.Expression == current
                        && access.Parent is InvocationExpressionSyntax outer
                        && outer.Expression == access
                    )
                    {
                        names.Add(access.Name.Identifier.ValueText);
                        current = outer;
                    }

                    chains.Add(names);
                }
            }

            if (chains.Count != 1)
            {
                yield return $"コード: {TypeName}.{MemberName} の {Target}（{Property}）が {chains.Count} 個（ちょうど 1 つ）";
                yield break;
            }

            var actual = chains[0];
            var contiguous = Enumerable
                .Range(0, Math.Max(0, actual.Count - Chain.Count + 1))
                .Any(start => actual.Skip(start).Take(Chain.Count).SequenceEqual(Chain));

            if (!contiguous)
            {
                yield return $"コード: {Property} の鎖が [{string.Join(", ", actual)}]（期待は連続した [{string.Join(", ", Chain)}]）";
            }
        }
    }

    private sealed record ReferencesSymbolRule(
        string TypeName,
        string? MemberName,
        string Symbol,
        bool Present,
        string? Ns
    ) : CodeRule
    {
        public override IEnumerable<string> Check(CodeRuleContext context)
        {
            if (!context.TryGetType(TypeName, Ns, out var type, out var problem))
            {
                yield return problem;
                yield break;
            }

            var nodes = (
                MemberName is null
                    ? type.DeclaringSyntaxReferences
                    : type.GetMembers(MemberName)
                        .Where(item => !item.IsImplicitlyDeclared)
                        .SelectMany(item => item.DeclaringSyntaxReferences)
            ).ToList();
            var where = $"{TypeName}{(MemberName is null ? "" : "." + MemberName)}";

            if (nodes.Count == 0)
            {
                yield return $"コード: {where} が無い";
                yield break;
            }

            var found = nodes.Any(reference =>
            {
                var model = context.Compilation.GetSemanticModel(reference.SyntaxTree);
                return reference
                    .GetSyntax()
                    .DescendantNodes()
                    .OfType<SimpleNameSyntax>()
                    .Any(name =>
                        model.GetSymbolInfo(name).Symbol is { ContainingType: { } owner } symbol
                        && $"{owner.Name}.{symbol.Name}" == Symbol
                    );
            });

            if (found != Present)
            {
                yield return $"コード: {where} が {Symbol} を{(Present ? "参照しない" : "参照する（しないはず）")}";
            }
        }
    }

    private sealed record XmlDocRule(
        string TypeName,
        string? MemberName,
        string Element,
        string Expected,
        string? Ns
    ) : CodeRule
    {
        public override IEnumerable<string> Check(CodeRuleContext context)
        {
            if (!context.TryGetType(TypeName, Ns, out var type, out var problem))
            {
                yield return problem;
                yield break;
            }

            var target = $"{TypeName}{(MemberName is null ? "" : "." + MemberName)}";
            var symbols = MemberName is null
                ? [type]
                : type.GetMembers(MemberName)
                    .Where(member => !member.IsImplicitlyDeclared)
                    .ToList();

            if (symbols.Count != 1)
            {
                yield return string.Create(
                    CultureInfo.InvariantCulture,
                    $"コード: {target} が {symbols.Count} 個（XmlDoc はちょうど 1 つに決まるメンバーで見る）"
                );
                yield break;
            }

            var actual = DocElement(symbols[0], Element);
            var expected = NormalizeDoc(Expected);

            if (actual != expected)
            {
                yield return $"コード: {target} の XmlDoc <{Element}> が「{actual ?? "(無い)"}」（期待「{expected}」）";
            }
        }
    }

    private sealed record XmlDocOwnersRule(
        string Element,
        string Text,
        IReadOnlyList<string> Owners
    ) : CodeRule
    {
        public override IEnumerable<string> Check(CodeRuleContext context)
        {
            var expected = NormalizeDoc(Text);
            var actual = context
                .Types.SelectMany(type =>
                    new ISymbol[] { type }.Concat(
                        type.GetMembers().Where(member => !member.IsImplicitlyDeclared)
                    )
                )
                .Where(symbol => DocElement(symbol, Element) == expected)
                .Select(symbol =>
                    symbol is INamedTypeSymbol
                        ? symbol.Name
                        : $"{symbol.ContainingType.Name}.{symbol.Name}"
                )
                .ToHashSet(StringComparer.Ordinal);

            if (!actual.SetEquals(Owners))
            {
                yield return $"コード: XmlDoc <{Element}> が「{expected}」のシンボルが {Show(actual)}（期待 {Show(Owners)}）";
            }
        }
    }
}

/// <summary>
/// 決まった行の生成物（<c>.g.cs</c> と手動実装のソース）の Roslyn のコンパイル 1 つと、規則が引く索引
/// </summary>
/// <remarks>
/// 構文木のパスは <see cref="FileLayoutOracle.PathOf"/>（置き場つき）で、規則はファイル名（パスの最後）で選ぶ。コンパイルは emit しない
/// （宣言の束縛は問い合わせたときに走る）。探針が同じコンパイルから emit して読み込めるように、参照集合は探針と同じ
/// （<see cref="GeneratedCodeCompiler.CreateCompilation"/>）。
/// </remarks>
internal sealed class CodeRuleContext
{
    private readonly Lazy<IReadOnlyList<INamedTypeSymbol>> _types;

    /// <summary>コンパイルから作る</summary>
    /// <param name="compilation">生成物のコンパイル</param>
    public CodeRuleContext(Microsoft.CodeAnalysis.CSharp.CSharpCompilation compilation)
    {
        Compilation = compilation;
        _types = new(() =>
        {
            var types = new List<INamedTypeSymbol>();
            Collect(compilation.Assembly.GlobalNamespace, types);
            return types;
        });
    }

    /// <summary>コンパイル</summary>
    public Microsoft.CodeAnalysis.CSharp.CSharpCompilation Compilation { get; }

    /// <summary>ソースに宣言された全部の型（入れ子を含む）</summary>
    public IReadOnlyList<INamedTypeSymbol> Types => _types.Value;

    /// <summary>
    /// 名前（と名前空間）で型を引く。<c>&lt;</c> を含む名前は型引数まで書いた表記で比べ、含まない名前はジェネリックでない型だけに当てる
    /// （<paramref name="anyArity"/> なら型引数の数を問わない＝「その名前の型が無い」を見るとき）
    /// </summary>
    public IEnumerable<INamedTypeSymbol> TypesNamed(
        string name,
        string? ns,
        bool anyArity = false
    ) =>
        Types.Where(type =>
            (
                name.Contains('<', StringComparison.Ordinal)
                    ? type.ToDisplayString(CodeRule.Format) == name
                    : type.Name == name && (anyArity || type.Arity == 0)
            ) && (ns is null || type.ContainingNamespace.ToDisplayString() == ns)
        );

    /// <summary>単純名（と名前空間）で型をちょうど 1 つ引く</summary>
    public bool TryGetType(string name, string? ns, out INamedTypeSymbol type, out string problem)
    {
        var types = TypesNamed(name, ns).ToList();

        if (types.Count == 1)
        {
            type = types[0];
            problem = "";
            return true;
        }

        type = null!;
        problem = string.Create(
            CultureInfo.InvariantCulture,
            $"コード: 型 {name}{(ns is null ? "" : $"（{ns}）")} が {types.Count} 個（ちょうど 1 つに決まること）{Locate(types)}"
        );
        return false;
    }

    /// <summary>ファイル名で構文木の根を選ぶ（<see cref="CodeRule.AllFiles"/> なら <c>.g.cs</c> の全部）</summary>
    public IEnumerable<CompilationUnitSyntax> Roots(string file) =>
        Compilation
            .SyntaxTrees.Where(tree =>
                file == CodeRule.AllFiles
                    ? FileName(tree).EndsWith(".g.cs", StringComparison.Ordinal)
                    : FileName(tree) == file
            )
            .Select(tree => (CompilationUnitSyntax)tree.GetRoot());

    /// <summary>型のメンバー（同名の全部）の本体にある呼び出しを、出てくる順に引く</summary>
    public bool TryGetCalls(
        INamedTypeSymbol type,
        string member,
        out IReadOnlyList<IMethodSymbol> calls,
        out string problem
    )
    {
        var members = type.GetMembers(member).Where(item => !item.IsImplicitlyDeclared).ToList();

        if (members.Count == 0)
        {
            calls = [];
            problem = $"コード: {type.Name}.{member} が無い";
            return false;
        }

        var found = new List<IMethodSymbol>();

        foreach (var reference in members.SelectMany(item => item.DeclaringSyntaxReferences))
        {
            var node = reference.GetSyntax();
            var model = Compilation.GetSemanticModel(reference.SyntaxTree);

            foreach (var invocation in node.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (model.GetSymbolInfo(invocation).Symbol is IMethodSymbol method)
                {
                    found.Add(Unreduce(method));
                }
            }
        }

        calls = found;
        problem = "";
        return true;
    }

    /// <summary>
    /// 拡張メソッドの呼び出しを、静的クラスのメソッドへ戻す（ジェネリックなら推論した型引数を保つ。<c>ReducedFrom</c> は型引数を持たない定義を返すため）
    /// </summary>
    public static IMethodSymbol Unreduce(IMethodSymbol method) =>
        method.ReducedFrom is { } definition
            ? definition.IsGenericMethod
                ? definition.ConstructedFrom.Construct([.. method.TypeArguments])
                : definition
            : method;

    /// <summary>
    /// 式を意味モデルで書き直す（<see cref="CodeRule.CallArguments"/> の比べ方：定数は値・引数とローカルとフィールドとプロパティは名前・
    /// 静的メンバーは <c>型名.名前</c>・<c>this</c> は <c>this</c>・それ以外は <c>?</c>）
    /// </summary>
    public static string RenderExpression(SemanticModel model, ExpressionSyntax expression)
    {
        var constant = model.GetConstantValue(expression);

        if (constant.HasValue)
        {
            return constant.Value switch
            {
                null => "null",
                string text => SymbolDisplay.FormatLiteral(text, quote: true),
                bool flag => flag ? "true" : "false",
                IFormattable formattable => formattable.ToString(
                    null,
                    CultureInfo.InvariantCulture
                ),
                var other => other.ToString() ?? "?",
            };
        }

        if (expression is ThisExpressionSyntax)
        {
            return "this";
        }

        return model.GetSymbolInfo(expression).Symbol switch
        {
            IParameterSymbol parameter => parameter.Name,
            ILocalSymbol local => local.Name,
            IFieldSymbol { IsStatic: true } field => $"{field.ContainingType.Name}.{field.Name}",
            IPropertySymbol { IsStatic: true } property =>
                $"{property.ContainingType.Name}.{property.Name}",
            IFieldSymbol field => field.Name,
            IPropertySymbol property => property.Name,
            _ => "?",
        };
    }

    /// <summary>呼び出し <paramref name="invocation"/> が、<paramref name="within"/> に当たる呼び出しの引数の中（ラムダの本体を含む）にあるか</summary>
    public static bool IsWithinCall(
        SemanticModel model,
        InvocationExpressionSyntax invocation,
        string within
    ) =>
        invocation
            .Ancestors()
            .OfType<ArgumentSyntax>()
            .Select(argument => argument.Parent?.Parent)
            .OfType<InvocationExpressionSyntax>()
            .Any(outer =>
                model.GetSymbolInfo(outer).Symbol is IMethodSymbol outerMethod
                && CallMatches(Unreduce(outerMethod), within)
            );

    /// <summary>呼び出し先が書き方（<c>メソッド名</c>・<c>型名.メソッド名</c>・型引数つき）に当たるか</summary>
    public static bool CallMatches(IMethodSymbol method, string target)
    {
        var typeName = target.Contains('.', StringComparison.Ordinal)
            ? target[..target.LastIndexOf('.')]
            : null;
        var methodPart = typeName is null ? target : target[(target.LastIndexOf('.') + 1)..];
        var name = methodPart.Contains('<', StringComparison.Ordinal)
            ? methodPart[..methodPart.IndexOf('<')]
            : methodPart;

        // 型は単純名か、型引数まで書いた表記（<c>ValueObjectBaseCore&lt;CustomerIdValue, int&gt;.Create</c>。基底が持つ静的メソッドを派生型名で呼んだ式を引く）
        if (
            method.Name != name
            || (
                typeName is not null
                && (
                    typeName.Contains('<', StringComparison.Ordinal)
                        ? method.ContainingType.ToDisplayString(CodeRule.Format) != typeName
                        : method.ContainingType.Name != typeName
                )
            )
        )
        {
            return false;
        }

        if (!methodPart.Contains('<', StringComparison.Ordinal))
        {
            return true;
        }

        var arguments = methodPart[(methodPart.IndexOf('<') + 1)..^1];
        return string.Join(
                ", ",
                method.TypeArguments.Select(item => item.ToDisplayString(CodeRule.Format))
            ) == arguments;
    }

    /// <summary>構文木のファイル名（パスの最後）</summary>
    public static string FileName(SyntaxTree tree) =>
        tree.FilePath[(tree.FilePath.LastIndexOf('/') + 1)..];

    /// <summary>型の宣言の構文（partial の全部）</summary>
    public static IEnumerable<MemberDeclarationSyntax> Declarations(INamedTypeSymbol type) =>
        type
            .DeclaringSyntaxReferences.Select(reference => reference.GetSyntax())
            .OfType<MemberDeclarationSyntax>();

    /// <summary>宣言の種類（<c>class</c>・<c>interface</c>・<c>struct</c>・<c>record</c>・<c>enum</c>・<c>delegate</c>）</summary>
    public static string KindOf(MemberDeclarationSyntax declaration) =>
        declaration switch
        {
            RecordDeclarationSyntax => "record",
            ClassDeclarationSyntax => "class",
            InterfaceDeclarationSyntax => "interface",
            StructDeclarationSyntax => "struct",
            EnumDeclarationSyntax => "enum",
            DelegateDeclarationSyntax => "delegate",
            _ => declaration.Kind().ToString(),
        };

    /// <summary>メンバーの宣言の修飾子の集合（宣言の全部の和）</summary>
    public static HashSet<string> MemberModifiers(ISymbol member) =>
        member
            .DeclaringSyntaxReferences.Select(reference => reference.GetSyntax())
            .Select(node =>
                node is VariableDeclaratorSyntax
                    ? node.FirstAncestorOrSelf<MemberDeclarationSyntax>()
                    : node as MemberDeclarationSyntax
            )
            .OfType<MemberDeclarationSyntax>()
            .SelectMany(declaration => declaration.Modifiers)
            .Select(token => token.ValueText)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>ファイルの using ディレクティブの名前空間（エイリアス・static を除く・名前空間の宣言の中のものを含む）</summary>
    public static IEnumerable<string> UsingNamespaces(CompilationUnitSyntax root) =>
        root.DescendantNodes()
            .OfType<UsingDirectiveSyntax>()
            .Where(directive => directive.Alias is null && directive.StaticKeyword == default)
            .Select(directive => directive.NamespaceOrType.ToString());

    /// <summary>名前空間が <paramref name="prefix"/> かその下か</summary>
    public static bool IsUnder(string ns, string prefix) =>
        ns == prefix || ns.StartsWith(prefix + ".", StringComparison.Ordinal);

    /// <summary>型の置き場（名前空間とファイル）を並べて書く（問題の文に添える）</summary>
    public static string Locate(IEnumerable<INamedTypeSymbol> types)
    {
        var places = types
            .Select(type =>
                $"{type.ContainingNamespace.ToDisplayString()}［{string.Join("/", type.DeclaringSyntaxReferences.Select(reference => FileName(reference.SyntaxTree)))}］"
            )
            .ToList();
        return places.Count == 0 ? "" : $" {string.Join(", ", places)}";
    }

    private static void Collect(INamespaceOrTypeSymbol container, List<INamedTypeSymbol> types)
    {
        foreach (var member in container.GetMembers())
        {
            if (member is INamespaceSymbol ns)
            {
                Collect(ns, types);
            }
            else if (
                member is INamedTypeSymbol type
                && type.Locations.Any(location => location.IsInSource)
            )
            {
                types.Add(type);
                Collect(type, types);
            }
        }
    }
}
