using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using QuickER.Model;
using QuickER.Resources;

namespace QuickER.Services;

/// <summary>
/// DBML (Database Markup Language) テキストを解析して ER 図モデルへ変換するインポーター
/// </summary>
/// <remarks>
/// 対応する記法は <see cref="DbmlExporter"/> の出力と往復可能な範囲に限定する
/// <list type="bullet">
///   <item><c>Table 名前 {</c> 〜 <c>}</c> ブロック（1 行 1 カラム定義）</item>
///   <item>カラム設定: <c>pk</c>（<c>primary key</c>）/ <c>unique</c> / <c>null</c> / <c>not null</c> / <c>note: '...'</c> /
///     インライン参照 <c>ref: 記号 テーブル.列</c>（大文字小文字を区別しない。それ以外の設定は読み飛ばす）</item>
///   <item><c>Indexes { … }</c> ブロック: <c>unique</c> 設定を持つ索引を一意制約として、<c>pk</c> 設定を持つ索引を主キーとして取り込む
///     （<c>(a, b) [unique, name: '…']</c> / <c>(b, a) [pk]</c> / 単一列は括弧なしも可）。主キーの索引は構成列と順序を決め、
///     一意制約は作らない（主キーはそれ自体が一意のため）。索引の名前は捨てる（図は主キーの制約名を持たない）</item>
///   <item><c>Ref</c> 行（短縮形）: <c>Ref: 親.a &lt; 子.x</c> と名前付きの <c>Ref 名前: …</c>（名前は制約名になる）。端点の後ろの設定ブロック
///     <c>[delete: …, update: …]</c> を参照アクションとして読む。多重度記号は <c>-</c>（1対1）/ <c>&lt;</c>（1対多）/ <c>&gt;</c>（多対1＝左右を入れ替えた 1対多）/ <c>&lt;&gt;</c>（多対多）。エンドポイントは単一列 <c>親.a</c> と複合 Ref 構文 <c>親.(a, b)</c> の双方に対応し、<b>行に書かれた列名がそのまま外部キーの構成列になる</b>（推論しない）。
///     外部キーの印はこの子側の列に立てる。参照先の <c>Table</c> より前に書かれていてもよい（テーブルを読み切ってから解析する）</item>
///   <item><c>//</c> 行コメント</item>
/// </list>
/// Project・Enum・TableGroup・複数行 Note ブロック等の DBML 構文は未対応
/// （<c>unique</c> でない索引は「一意制約ではない」ため読み飛ばす）
/// </remarks>
public static partial class DbmlImporter
{
    /// <summary><c>Table 名前 {</c> 形式のテーブル開始行を検出する正規表現</summary>
    private static readonly Regex TableHeaderRegex = TableHeaderLineRegex();

    /// <summary><c>Ref</c> 行（短縮形）を解析する正規表現</summary>
    private static readonly Regex RelationshipRegex = RelationshipLineRegex();

    /// <summary><c>Ref</c> 行の短縮形（<c>Ref:</c> / <c>Ref 名前:</c>）の書き出しを検出する正規表現</summary>
    private static readonly Regex RelationshipStartRegex = RelationshipLineStartRegex();

    /// <summary>カラム設定の <c>note: '...'</c> を解析する正規表現</summary>
    private static readonly Regex NoteRegex = ColumnNoteRegex();

    /// <summary>テーブルブロック内の <c>Note: '...'</c> 行（テーブルの説明）を解析する正規表現</summary>
    private static readonly Regex TableNoteRegex = TableNoteLineRegex();

    /// <summary><c>Ref</c> 行の設定ブロック内の参照アクション設定を解析する正規表現</summary>
    private static readonly Regex ReferentialActionRegex = ReferentialActionSettingRegex();

    /// <summary><c>Indexes {</c> ブロック開始行を検出する正規表現</summary>
    private static readonly Regex IndexesHeaderRegex = IndexesHeaderLineRegex();

    /// <summary>索引設定の <c>name: '...'</c> を解析する正規表現</summary>
    private static readonly Regex IndexNameRegex = IndexSettingNameRegex();

    /// <summary>QuickER が扱わない DBML のトップレベルブロック開始行を検出する正規表現</summary>
    private static readonly Regex SkippableBlockRegex = SkippableBlockLineRegex();

    /// <summary><c>Table</c> ブロック内の未対応ブロック開始行を検出する正規表現</summary>
    private static readonly Regex SkippableTableBlockRegex = SkippableTableBlockLineRegex();

    /// <summary><c>Ref</c> で始まる行を検出する正規表現（未対応形式の告知に使う）</summary>
    private static readonly Regex RefKeywordRegex = RefKeywordLineRegex();

    /// <summary><c>Note</c> で始まる行を検出する正規表現（未対応の複数行 Note の告知に使う）</summary>
    private static readonly Regex NoteKeywordRegex = NoteKeywordLineRegex();

    /// <summary>カラム設定の値つき <c>ref:</c> を検出する正規表現</summary>
    private static readonly Regex InlineReferenceStartRegex = InlineReferenceStartLineRegex();

    /// <summary>カラム設定のインライン参照を解析する正規表現</summary>
    private static readonly Regex InlineReferenceRegex = InlineReferenceLineRegex();

    /// <summary>
    /// DBML ファイルを読み込み ER 図へ変換する
    /// </summary>
    /// <param name="targetDbms">
    /// 取り込んだ図の対象 DBMS（返す図の <see cref="ErDiagram.TargetDbms"/> になる。名前の無い外部キーに付ける名前は
    /// この DBMS の識別子の長さの上限で縮める＝経路は <see cref="ConstraintNames"/> の XmlDoc）
    /// </param>
    public static ErDiagram Load(string path, string targetDbms)
    {
        return Parse(File.ReadAllText(path), targetDbms);
    }

    /// <summary>
    /// DBML テキストを解析して ER 図を生成する
    /// </summary>
    /// <param name="targetDbms">
    /// 取り込んだ図の対象 DBMS（返す図の <see cref="ErDiagram.TargetDbms"/> になる。名前の無い外部キーに付ける名前は
    /// この DBMS の識別子の長さの上限で縮める＝経路は <see cref="ConstraintNames"/> の XmlDoc）
    /// </param>
    /// <returns>復元した <see cref="ErDiagram"/></returns>
    /// <exception cref="InvalidDataException">構文不正・テーブル名重複・未定義テーブル参照などを検出した場合</exception>
    public static ErDiagram Parse(string text, string targetDbms)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidDataException(Strings.Dbml_EmptyText);
        }

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var entities = new Dictionary<string, Entity>(SchemaNames.Comparer);
        var relationships = new List<Relationship>();
        // Ref 行の列名は、空テーブルへの既定 PK 列補完まで済んだ後にまとめて列ペアへ解決する
        var pendingRelationshipColumns =
            new List<(Relationship Relationship, List<string> Source, List<string> Target)>();
        // Ref 行とインライン参照は、参照先テーブルの定義より前に書かれていてもよいよう、テーブルへの解決を
        // 行ループを抜けてから行う（他ツール製の DBML はファイル先頭へ Ref をまとめる書き方がある）。
        // 書かれた順に並べておき、その順でリレーションにする
        var pendingReferences = new List<(RefEndpoints Endpoints, int LineNumber)>();
        // Indexes ブロックの一意索引は、列定義より前に書かれていても解決できるよう最後にまとめて紐付ける
        // （解決は行ループを抜けた後になるため、診断用に定義行の行番号も持ち回る）
        var pendingUniqueIndexes =
            new List<(Entity Entity, string? Name, List<string> Columns, int LineNumber)>();
        // 主キーの索引（[pk]）も同じ理由で最後にまとめて確定する（列ごとの pk との突き合わせも全行が揃ってから）
        var pendingPrimaryKeyIndexes =
            new List<(Entity Entity, List<string> Columns, int LineNumber)>();
        Entity? currentEntity = null;
        // 未閉じブロックの診断はループを抜けてから判明するため、ブロック開始行を覚えておく
        var currentEntityLineNumber = 0;
        var inIndexesBlock = false;
        // 複数行にまたがる構文（ブロックコメント・複数行リテラル）の持ち越し状態
        var scanState = ScanState.None;
        // QuickER が扱わない DBML ブロックを読み飛ばしている間の波括弧の深さ
        var skipDepth = 0;

        // 行単位の状態機械: currentEntity が非 null の間は Table ブロック内としてカラム行を解釈する
        for (var index = 0; index < lines.Length; index++)
        {
            var lineNumber = index + 1;
            var (scanned, braceDelta) = ScanLine(lines[index], ref scanState);
            var line = scanned.Trim();

            // 読み飛ばし中のブロックは、対応する閉じ括弧に達するまで中身を解釈しない
            if (skipDepth > 0)
            {
                skipDepth = Math.Max(0, skipDepth + braceDelta);
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            // 行に紐づく診断は、パーサ内部（カラム・索引・リレーション定義）のものも含めて行番号を前置して投げ直す
            try
            {
                if (currentEntity is not null)
                {
                    // Indexes ブロック内は索引定義として解釈する（閉じ括弧で Table ブロックへ戻る）
                    if (inIndexesBlock)
                    {
                        if (line == "}")
                        {
                            inIndexesBlock = false;
                            continue;
                        }

                        var parsedIndex = ParseIndex(line, currentEntity.TableName);

                        if (parsedIndex is { IsPrimaryKey: true })
                        {
                            pendingPrimaryKeyIndexes.Add(
                                (currentEntity, parsedIndex.Value.Columns, lineNumber)
                            );
                        }
                        else if (parsedIndex is { IsUnique: true })
                        {
                            pendingUniqueIndexes.Add(
                                (
                                    currentEntity,
                                    parsedIndex.Value.Name,
                                    parsedIndex.Value.Columns,
                                    lineNumber
                                )
                            );
                        }

                        continue;
                    }

                    if (line == "}")
                    {
                        currentEntity = null;
                        continue;
                    }

                    if (IndexesHeaderRegex.IsMatch(line))
                    {
                        inIndexesBlock = true;
                        continue;
                    }

                    // テーブル内の未対応ブロック（checks / records）はカラム定義として解釈せず読み飛ばす
                    if (SkippableTableBlockRegex.IsMatch(line))
                    {
                        skipDepth = Math.Max(0, braceDelta);
                        continue;
                    }

                    // テーブルの説明（DBML 標準の Note: 行）。カラム定義として解釈しない
                    var tableNote = TableNoteRegex.Match(line);

                    if (tableNote.Success)
                    {
                        currentEntity.Description = DbmlLiteral.Unescape(
                            tableNote.Groups["note"].Value
                        );
                        continue;
                    }

                    // 1 行で閉じない Note（複数行リテラル）は未対応。カラム定義として解釈すると
                    // "Note:" という名前の幻のカラムができる
                    if (NoteKeywordRegex.IsMatch(line))
                    {
                        throw new InvalidDataException(
                            string.Format(Strings.Dbml_UnsupportedLine, line)
                        );
                    }

                    var (column, isUnique, inlineReferences) = ParseColumn(
                        line,
                        currentEntity.TableName
                    );
                    if (
                        currentEntity.Columns.Any(existing =>
                            SchemaNames.AreSame(existing.Name, column.Name)
                        )
                    )
                    {
                        throw new InvalidDataException(
                            string.Format(
                                Strings.Dbml_DuplicateColumn,
                                currentEntity.TableName,
                                column.Name
                            )
                        );
                    }

                    currentEntity.Columns.Add(column);
                    pendingReferences.AddRange(
                        inlineReferences.Select(endpoints => (endpoints, lineNumber))
                    );

                    // カラム設定の unique は「その 1 列だけの名前なし一意制約」を意味する
                    if (isUnique)
                    {
                        currentEntity.UniqueConstraints.Add(
                            new UniqueConstraint { ColumnIds = [column.Id] }
                        );
                    }

                    continue;
                }

                var tableMatch = TableHeaderRegex.Match(line);

                if (tableMatch.Success)
                {
                    var tableName = UnquoteIdentifier(tableMatch.Groups["table"].Value);

                    if (!entities.TryAdd(tableName, new Entity { TableName = tableName }))
                    {
                        throw new InvalidDataException(
                            string.Format(Strings.Dbml_DuplicateEntity, tableName)
                        );
                    }

                    currentEntity = entities[tableName];
                    currentEntityLineNumber = lineNumber;
                    continue;
                }

                if (RelationshipStartRegex.IsMatch(line))
                {
                    // テーブルへの解決は読み切ってから（前方参照＝定義より前に書かれた Ref を受け付ける）
                    pendingReferences.Add((ParseRelationshipLine(line), lineNumber));
                    continue;
                }

                // QuickER が表せない情報しか持たないブロックは読み飛ばす（従来と同じ寛容さ）
                if (SkippableBlockRegex.IsMatch(line))
                {
                    skipDepth = Math.Max(0, braceDelta);
                    continue;
                }

                // ブロック形式（Ref … { … }）は未対応。
                // 読み飛ばすとリレーションが黙って消えるため、行を名指しして拒否する
                if (RefKeywordRegex.IsMatch(line))
                {
                    throw new InvalidDataException(
                        string.Format(Strings.Dbml_UnsupportedRelationshipForm, line)
                    );
                }

                // ここへ来る行は QuickER が解釈できない。黙って捨てると、書式に一致しなかった
                // Table 行ごとテーブルが消えるといった欠落が気づけないまま残る
                throw new InvalidDataException(string.Format(Strings.Dbml_UnsupportedLine, line));
            }
            catch (InvalidDataException ex)
            {
                throw ImportDiagnostics.AtLine(lineNumber, ex);
            }
        }

        if (currentEntity is not null)
        {
            throw ImportDiagnostics.AtLine(
                currentEntityLineNumber,
                string.Format(Strings.Dbml_MissingClosingBrace, currentEntity.TableName)
            );
        }

        if (entities.Count == 0)
        {
            throw new InvalidDataException(Strings.Dbml_NoEntities);
        }

        foreach (var (endpoints, refLineNumber) in pendingReferences)
        {
            try
            {
                var (relationship, sourceColumns, targetColumns) = BuildRelationship(
                    endpoints,
                    entities
                );
                relationships.Add(relationship);
                pendingRelationshipColumns.Add((relationship, sourceColumns, targetColumns));
            }
            catch (InvalidDataException ex)
            {
                throw ImportDiagnostics.AtLine(refLineNumber, ex);
            }
        }

        EnsureEntitiesHaveColumns(entities.Values);
        ResolvePrimaryKeyIndexes(pendingPrimaryKeyIndexes);
        ResolveUniqueIndexes(pendingUniqueIndexes);
        ResolveRelationshipColumns(entities, pendingRelationshipColumns);

        // 名前の無い Ref が同じテーブルの組に 2 本あると既定名が重なるため、ふさがった分だけ名前を付ける
        ConstraintNames.NameUnnamedForeignKeys(
            entities.Values,
            relationships,
            IdentifierLengthLimits.ForDbms(targetDbms)
        );

        return new ErDiagram
        {
            Entities = entities.Values.ToList(),
            Relationships = relationships,
            TargetDbms = targetDbms,
        };
    }

    /// <summary>
    /// DBML のカラム定義行（<c>名前 型 [設定, ...]</c>）を解析する
    /// </summary>
    /// <remarks>
    /// <para>
    /// 名前は素の識別子と引用識別子（<c>"列 名"</c>）の双方を受け付ける。型名は空白を含んでもよく、
    /// 素の記法（<c>timestamp without time zone</c>）と引用（<c>"double precision"</c>）の双方に対応する。
    /// 設定省略時は NULL 許可を既定とし、<c>pk</c>（<c>primary key</c>）指定時は NOT NULL を強制する。
    /// note 内のエスケープ（<c>\\</c> と <c>\'</c>）は <see cref="DbmlLiteral.Unescape"/> で復元する
    /// </para>
    /// <para>
    /// 設定ブロックの開始は <see cref="FindSettingsBracket"/> が決める（最初の <c>[</c> ではない）。
    /// 囲まれていない配列型 <c>integer[]</c> の角括弧を設定の開始と取り違えないため。
    /// </para>
    /// <para>
    /// 値つきの <c>ref: 記号 テーブル.列</c>（インライン参照）は <see cref="ParseInlineReference"/> が
    /// <c>Ref</c> 行と同じ形へ写し、テーブルを読み切った後に <c>Ref</c> 行と同じ解決でリレーションにする。
    /// 値なしの <c>ref</c> は読み飛ばす（列単独では参照先が決まらない）
    /// </para>
    /// </remarks>
    /// <returns>復元したカラムと、カラム設定 <c>unique</c> が指定されていたか、インライン参照の一覧</returns>
    /// <exception cref="InvalidDataException">名前と型の 2 トークンに満たない場合・インライン参照が読めない場合</exception>
    private static (Column Column, bool IsUnique, List<RefEndpoints> InlineReferences) ParseColumn(
        string line,
        string tableName
    )
    {
        var trimmed = line.Trim();
        var bracketStart = FindSettingsBracket(trimmed);
        var bracketEnd = trimmed.LastIndexOf(']');
        var definition = bracketStart >= 0 ? trimmed[..bracketStart].Trim() : trimmed;
        var optionText =
            bracketStart >= 0 && bracketEnd > bracketStart
                ? trimmed[(bracketStart + 1)..bracketEnd]
                : string.Empty;
        var (name, dataType) = SplitNameAndType(definition);

        if (name.Length == 0 || dataType.Length == 0)
        {
            throw new InvalidDataException(
                string.Format(Strings.Dbml_ColumnParseError, tableName, line)
            );
        }

        var column = new Column
        {
            Name = name,
            DataType = dataType,
            IsNullable = true,
        };
        var isUnique = false;
        var inlineReferences = new List<RefEndpoints>();

        foreach (var option in SplitOptions(optionText))
        {
            if (
                string.Equals(option, "pk", StringComparison.OrdinalIgnoreCase)
                || string.Equals(option, "primary key", StringComparison.OrdinalIgnoreCase)
            )
            {
                column.IsPrimaryKey = true;
                column.IsNullable = false;
                continue;
            }

            if (ParseInlineReference(option, tableName, name) is { } inlineReference)
            {
                inlineReferences.Add(inlineReference);
                continue;
            }

            if (string.Equals(option, "unique", StringComparison.OrdinalIgnoreCase))
            {
                isUnique = true;
                continue;
            }

            if (string.Equals(option, "not null", StringComparison.OrdinalIgnoreCase))
            {
                column.IsNullable = false;
                continue;
            }

            if (string.Equals(option, "null", StringComparison.OrdinalIgnoreCase))
            {
                column.IsNullable = true;
                continue;
            }

            var noteMatch = NoteRegex.Match(option);

            if (noteMatch.Success)
            {
                column.Description = DbmlLiteral.Unescape(noteMatch.Groups["note"].Value);
            }
        }

        return (column, isUnique, inlineReferences);
    }

    /// <summary>
    /// カラム設定のインライン参照（<c>ref: 記号 テーブル.列</c>）を、<c>Ref</c> 行と同じ端点の組へ写す
    /// </summary>
    /// <param name="option">カラム設定の 1 項目</param>
    /// <param name="tableName">この列のテーブル名</param>
    /// <param name="columnName">この列の名前</param>
    /// <returns>インライン参照でない設定（値なしの <c>ref</c> を含む）なら <c>null</c></returns>
    /// <remarks>
    /// <c>Ref</c> 行の左辺にこの列を置いた形（<c>Ref: テーブル.列 記号 参照先</c>）と同じ意味にする。ただし 1 対 1（<c>-</c>）は
    /// 左右を入れ替える。DBML は 1 対 1 の外部キーを、<c>Ref</c> 行では右辺の列、インライン参照では <c>ref</c> を書いた列に置くと
    /// 定めているため（<c>Ref</c> 行の左辺は親）。
    /// </remarks>
    /// <exception cref="InvalidDataException">値つきの <c>ref:</c> が読めない形（スキーマ修飾など）の場合</exception>
    private static RefEndpoints? ParseInlineReference(
        string option,
        string tableName,
        string columnName
    )
    {
        if (!InlineReferenceStartRegex.IsMatch(option))
        {
            return null;
        }

        var match = InlineReferenceRegex.Match(option);

        if (!match.Success)
        {
            throw new InvalidDataException(
                string.Format(Strings.Dbml_UnsupportedInlineReference, option)
            );
        }

        var symbol = match.Groups["symbol"].Value;
        var self = (Table: tableName, Columns: new List<string> { columnName });
        var other = (
            Table: UnquoteIdentifier(match.Groups["table"].Value),
            Columns: new List<string> { UnquoteIdentifier(match.Groups["column"].Value) }
        );
        var (left, right) = symbol == "-" ? (other, self) : (self, other);

        return new RefEndpoints(
            left.Table,
            left.Columns,
            symbol,
            right.Table,
            right.Columns,
            Name: null,
            Settings: string.Empty
        );
    }

    /// <summary>
    /// カラム定義の「名前 型」部分を、名前と型へ分ける
    /// </summary>
    /// <remarks>
    /// 名前は先頭の 1 トークン（引用識別子ならその全体）で、残りがすべて型。
    /// 型が引用識別子 1 つだけなら引用符を外す（<c>"double precision"</c> → <c>double precision</c>）。
    /// </remarks>
    private static (string Name, string DataType) SplitNameAndType(string definition)
    {
        var text = definition.Trim();

        if (text.Length == 0)
        {
            return (string.Empty, string.Empty);
        }

        int nameEnd;

        if (text[0] == '"')
        {
            nameEnd = SkipLiteral(text, 0, '"');
        }
        else
        {
            nameEnd = text.IndexOf(' ', StringComparison.Ordinal);

            if (nameEnd < 0)
            {
                return (UnquoteIdentifier(text), string.Empty);
            }
        }

        var name = UnquoteIdentifier(text[..nameEnd].Trim());
        var dataType = text[nameEnd..].Trim();

        return (name, UnquoteIdentifier(dataType));
    }

    /// <summary>
    /// <c>Indexes</c> ブロック内の 1 行を解析し、一意索引・主キーの索引なら名前と構成列名を返す
    /// </summary>
    /// <returns>
    /// <c>unique</c> または <c>pk</c> を持つ索引なら（名前・構成列名・一意か・主キーか）。
    /// どちらでもない索引は <c>null</c>（読み飛ばす）
    /// </returns>
    /// <exception cref="InvalidDataException">索引行の構文が解釈できない場合</exception>
    private static (
        string? Name,
        List<string> Columns,
        bool IsUnique,
        bool IsPrimaryKey
    )? ParseIndex(string line, string tableName)
    {
        var trimmed = line.Trim();
        var bracketStart = FindSettingsBracket(trimmed);
        var bracketEnd = trimmed.LastIndexOf(']');
        var head = (bracketStart >= 0 ? trimmed[..bracketStart] : trimmed).Trim();
        var settingText =
            bracketStart >= 0 && bracketEnd > bracketStart
                ? trimmed[(bracketStart + 1)..bracketEnd]
                : string.Empty;

        if (head.Length == 0)
        {
            throw new InvalidDataException(
                string.Format(Strings.Dbml_IndexParseError, tableName, line)
            );
        }

        // 括弧つき（複数列可）と括弧なし（単一列）のどちらの記法でも列名一覧として扱う。
        // 分割はリテラルの外のカンマだけで行う（引用識別子にカンマを含められるため）
        var columns = (
            head.StartsWith('(') && head.EndsWith(')')
                ? SplitOutsideLiterals(head[1..^1], ',')
                : [head]
        )
            .Select(UnquoteIdentifier)
            .Where(column => column.Length > 0)
            .ToList();

        if (columns.Count == 0)
        {
            throw new InvalidDataException(
                string.Format(Strings.Dbml_IndexParseError, tableName, line)
            );
        }

        var isUnique = false;
        var isPrimaryKey = false;
        string? name = null;

        foreach (var option in SplitOptions(settingText))
        {
            if (string.Equals(option, "unique", StringComparison.OrdinalIgnoreCase))
            {
                isUnique = true;
                continue;
            }

            if (string.Equals(option, "pk", StringComparison.OrdinalIgnoreCase))
            {
                isPrimaryKey = true;
                continue;
            }

            var nameMatch = IndexNameRegex.Match(option);

            if (nameMatch.Success)
            {
                name = DbmlLiteral.Unescape(nameMatch.Groups["name"].Value);
            }
        }

        // unique でも pk でもない索引は取り込まない（インデックス自体はモデルに持たない）
        return isUnique || isPrimaryKey ? (name, columns, isUnique, isPrimaryKey) : null;
    }

    /// <summary>
    /// <c>Indexes</c> ブロックの主キーの索引（<c>[pk]</c>）を、対象エンティティの主キーの構成列と順序として確定する
    /// </summary>
    /// <remarks>
    /// <para>
    /// 構成列は列ごとの <c>pk</c> と同じく NOT NULL へ寄せ、<see cref="Entity.PrimaryKeyColumnIds"/> に索引の並びを入れる。
    /// 列ごとの <c>pk</c> と索引が両方あり、構成列の集合が同じなら受けて順序は索引を採る
    /// （他ツール製の DBML でよくある書き方）。
    /// </para>
    /// <para>
    /// 食い違う指定は推測で合わせず、索引の行を名指しして断る:
    /// 1 テーブルに索引が 2 つ以上・索引の中で同じ列が 2 回・列ごとの <c>pk</c> と構成列の集合が違う。
    /// どれを正としても、書いた人の意図と違う主キーを黙って作ることになるため。
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidDataException">上の断る条件、または索引が参照する列が無い場合</exception>
    private static void ResolvePrimaryKeyIndexes(
        IEnumerable<(Entity Entity, List<string> Columns, int LineNumber)> primaryKeyIndexes
    )
    {
        var resolvedEntities = new HashSet<Entity>();

        foreach (var (entity, columnNames, lineNumber) in primaryKeyIndexes)
        {
            if (!resolvedEntities.Add(entity))
            {
                throw ImportDiagnostics.AtLine(
                    lineNumber,
                    string.Format(Strings.Dbml_PrimaryKeyIndexDuplicate, entity.TableName)
                );
            }

            var keyColumns = new List<Column>(columnNames.Count);

            foreach (var columnName in columnNames)
            {
                var column = entity.Columns.FirstOrDefault(c =>
                    SchemaNames.AreSame(c.Name, columnName)
                );

                if (column is null)
                {
                    throw ImportDiagnostics.AtLine(
                        lineNumber,
                        string.Format(
                            Strings.Dbml_IndexColumnNotFound,
                            entity.TableName,
                            columnName
                        )
                    );
                }

                // 同じ列が 2 回出る索引は、主キーの定義として成り立たない
                if (keyColumns.Contains(column))
                {
                    throw ImportDiagnostics.AtLine(
                        lineNumber,
                        string.Format(
                            Strings.Dbml_PrimaryKeyIndexDuplicateColumn,
                            entity.TableName,
                            column.Name
                        )
                    );
                }

                keyColumns.Add(column);
            }

            var markedColumns = entity.Columns.Where(column => column.IsPrimaryKey).ToList();

            if (
                markedColumns.Count > 0
                && (
                    markedColumns.Count != keyColumns.Count
                    || markedColumns.Any(column => !keyColumns.Contains(column))
                )
            )
            {
                throw ImportDiagnostics.AtLine(
                    lineNumber,
                    string.Format(
                        Strings.Dbml_PrimaryKeyConflict,
                        entity.TableName,
                        string.Join(", ", markedColumns.Select(column => column.Name)),
                        string.Join(", ", keyColumns.Select(column => column.Name))
                    )
                );
            }

            foreach (var column in keyColumns)
            {
                column.IsPrimaryKey = true;
                column.IsNullable = false;
            }

            entity.PrimaryKeyColumnIds = keyColumns.Select(column => column.Id).ToList();
        }
    }

    /// <summary>
    /// <c>Indexes</c> ブロックから集めた一意索引を、対象エンティティの一意制約として確定する
    /// </summary>
    /// <exception cref="InvalidDataException">索引が参照する列がテーブルに存在しない場合</exception>
    private static void ResolveUniqueIndexes(
        IEnumerable<(
            Entity Entity,
            string? Name,
            List<string> Columns,
            int LineNumber
        )> uniqueIndexes
    )
    {
        foreach (var (entity, name, columns, lineNumber) in uniqueIndexes)
        {
            var columnIds = new List<Guid>(columns.Count);

            foreach (var columnName in columns)
            {
                var column = entity.Columns.FirstOrDefault(c =>
                    SchemaNames.AreSame(c.Name, columnName)
                );

                if (column is null)
                {
                    // 解決は行ループを抜けた後だが、診断は索引定義が書かれていた行を指す
                    throw ImportDiagnostics.AtLine(
                        lineNumber,
                        string.Format(
                            Strings.Dbml_IndexColumnNotFound,
                            entity.TableName,
                            columnName
                        )
                    );
                }

                columnIds.Add(column.Id);
            }

            entity.UniqueConstraints.Add(
                new UniqueConstraint { Name = name, ColumnIds = columnIds }
            );
        }
    }

    /// <summary><c>Ref</c> 行・インライン参照から読んだ、テーブルへ解決する前の端点の組</summary>
    /// <param name="LeftTable">左辺のテーブル名</param>
    /// <param name="LeftColumns">左辺の列名（宣言順）</param>
    /// <param name="Symbol">多重度記号（<c>&lt;</c> / <c>&gt;</c> / <c>-</c> / <c>&lt;&gt;</c>）</param>
    /// <param name="RightTable">右辺のテーブル名</param>
    /// <param name="RightColumns">右辺の列名（宣言順）</param>
    /// <param name="Name">名前付き Ref の名前（制約名）</param>
    /// <param name="Settings">端点の後ろの設定ブロックの中身</param>
    private sealed record RefEndpoints(
        string LeftTable,
        List<string> LeftColumns,
        string Symbol,
        string RightTable,
        List<string> RightColumns,
        string? Name,
        string Settings
    );

    /// <summary>
    /// DBML の <c>Ref</c> 行（短縮形）を端点の組へ解析する（テーブルへの解決は <see cref="BuildRelationship"/>）
    /// </summary>
    /// <exception cref="InvalidDataException">構文不一致の場合</exception>
    private static RefEndpoints ParseRelationshipLine(string line)
    {
        var match = RelationshipRegex.Match(line);

        if (!match.Success)
        {
            throw new InvalidDataException(
                string.Format(Strings.Dbml_RelationshipParseError, line)
            );
        }

        return new RefEndpoints(
            UnquoteIdentifier(match.Groups["leftTable"].Value),
            ReadEndpointColumns(match, "leftColumns", "leftColumn"),
            match.Groups["symbol"].Value,
            UnquoteIdentifier(match.Groups["rightTable"].Value),
            ReadEndpointColumns(match, "rightColumns", "rightColumn"),
            match.Groups["name"].Success ? UnquoteIdentifier(match.Groups["name"].Value) : null,
            match.Groups["settings"].Success ? match.Groups["settings"].Value : string.Empty
        );
    }

    /// <summary>
    /// 端点の組をテーブルへ解決してリレーションを生成する
    /// </summary>
    /// <remarks>
    /// 左辺テーブルを親（Source）、右辺テーブルを子（Target）として扱う。多対1（<c>&gt;</c>）は左右を入れ替えて
    /// 1 対多（<c>&lt;</c>）と同じにする（<c>a.x &gt; b.y</c> は <c>b.y &lt; a.x</c> と同じ意味）。
    /// 名前付き Ref の名前を制約名にし、端点の後ろの設定ブロックから参照アクションを読む。
    /// 端点の列名（単一列 <c>親.a</c> / 複合 <c>親.(a, b)</c>）は外部キーの構成列そのものとして持ち帰り、
    /// 後段の <see cref="ResolveRelationshipColumns"/> が列ペアへ解決する（推論はしない）
    /// </remarks>
    /// <exception cref="InvalidDataException">記号が未対応、または参照先テーブルが未定義の場合</exception>
    private static (
        Relationship Relationship,
        List<string> SourceColumns,
        List<string> TargetColumns
    ) BuildRelationship(RefEndpoints endpoints, IReadOnlyDictionary<string, Entity> entities)
    {
        var isManyToOne = endpoints.Symbol == ">";
        var (parentTable, parentColumns, childTable, childColumns) = isManyToOne
            ? (
                endpoints.RightTable,
                endpoints.RightColumns,
                endpoints.LeftTable,
                endpoints.LeftColumns
            )
            : (
                endpoints.LeftTable,
                endpoints.LeftColumns,
                endpoints.RightTable,
                endpoints.RightColumns
            );
        var (onDelete, onUpdate) = ParseRelationshipSettings(endpoints.Settings);

        if (!entities.ContainsKey(parentTable))
        {
            throw new InvalidDataException(
                string.Format(Strings.Dbml_RelationshipSourceUndefined, parentTable)
            );
        }

        if (!entities.ContainsKey(childTable))
        {
            throw new InvalidDataException(
                string.Format(Strings.Dbml_RelationshipTargetUndefined, childTable)
            );
        }

        var relationship = new Relationship
        {
            SourceEntityId = entities[parentTable].Id,
            TargetEntityId = entities[childTable].Id,
            Type = endpoints.Symbol switch
            {
                "-" => RelationshipType.OneToOne,
                "<" or ">" => RelationshipType.OneToMany,
                "<>" => RelationshipType.ManyToMany,
                _ => throw new InvalidDataException(
                    string.Format(Strings.Dbml_UnsupportedRelationshipSymbol, endpoints.Symbol)
                ),
            },
            ConstraintName = endpoints.Name,
            OnDelete = onDelete,
            OnUpdate = onUpdate,
        };

        return (relationship, parentColumns, childColumns);
    }

    /// <summary><c>Ref</c> 行の端点の後ろの設定ブロックから参照アクションを取り出す</summary>
    /// <remarks>
    /// 未知の設定は読み飛ばす。DBML の <c>restrict</c> はモデルに対応する値が無いため
    /// <see cref="ForeignKeyReferentialAction.NoAction"/> へ倒れる
    /// （<see cref="ForeignKeyReferentialActionHelper.Parse"/> の既定）。
    /// 設定が無い場合は両アクション既定を返す（制約名は設定でなく名前付き Ref の名前が運ぶ）
    /// </remarks>
    private static (
        ForeignKeyReferentialAction OnDelete,
        ForeignKeyReferentialAction OnUpdate
    ) ParseRelationshipSettings(string settingText)
    {
        var onDelete = ForeignKeyReferentialAction.NoAction;
        var onUpdate = ForeignKeyReferentialAction.NoAction;

        foreach (var setting in SplitOptions(settingText))
        {
            var actionMatch = ReferentialActionRegex.Match(setting);

            if (!actionMatch.Success)
            {
                continue;
            }

            var action = ForeignKeyReferentialActionHelper.Parse(
                actionMatch.Groups["action"].Value
            );

            if (
                string.Equals(
                    actionMatch.Groups["kind"].Value,
                    "delete",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                onDelete = action;
            }
            else
            {
                onUpdate = action;
            }
        }

        return (onDelete, onUpdate);
    }

    /// <summary><c>Ref:</c> 行のエンドポイント列名を取り出す（単一列・複合 <c>(a, b)</c> のどちらでも列名一覧を返す）</summary>
    private static List<string> ReadEndpointColumns(
        Match match,
        string listGroupName,
        string singleGroupName
    )
    {
        if (!match.Groups[listGroupName].Success)
        {
            return [UnquoteIdentifier(match.Groups[singleGroupName].Value)];
        }

        // 分割はリテラルの外のカンマだけで行う（引用識別子にカンマを含められるため）
        return SplitOutsideLiterals(match.Groups[listGroupName].Value, ',')
            .Select(UnquoteIdentifier)
            .ToList();
    }

    /// <summary>
    /// カラム設定文字列をカンマ区切りで分割する
    /// </summary>
    /// <remarks>
    /// <c>note: 'a, b'</c> のようにリテラル内へ含まれるカンマは区切りとして扱わない。
    /// リテラルの終わりはエスケープを 1 文字ずつ追って決める（<see cref="SplitOutsideLiterals"/>）。
    /// 「直前の 1 文字が <c>\</c> か」で判定すると、<c>\</c> で終わる文字列（<c>'C:\\'</c>）の閉じ引用を
    /// エスケープと取り違え、後ろの設定まで説明に飲み込む
    /// </remarks>
    private static List<string> SplitOptions(string optionText) =>
        SplitOutsideLiterals(optionText, ',');

    /// <summary>行の走査で次の行へ持ち越す状態（複数行にまたがる構文）</summary>
    private enum ScanState
    {
        /// <summary>通常</summary>
        None,

        /// <summary><c>/* … */</c> の途中</summary>
        BlockComment,

        /// <summary>複数行リテラル <c>''' … '''</c> の途中</summary>
        TripleQuote,
    }

    /// <summary>
    /// 1 行からコメントを取り除き、あわせてリテラルの外にある波括弧の増減を数える
    /// </summary>
    /// <param name="raw">元の行</param>
    /// <param name="state">複数行構文の状態（行をまたいで持ち回る）</param>
    /// <returns>コメントを除いた行と、この行での波括弧の増減</returns>
    /// <remarks>
    /// <para>
    /// リテラル（<c>'…'</c> / <c>"…"</c> / <c>''' … '''</c>）の中身は素通しする。<c>//</c> を無条件に
    /// 切り落とすと、説明に URL を書いただけで <c>note: 'see http://example.com'</c> が途中で切れる。
    /// </para>
    /// <para>
    /// 波括弧を数えるのは、QuickER が扱わない DBML のブロック（<c>Project</c> 等）を読み飛ばすため。
    /// リテラルの中の <c>{</c> <c>}</c> を数えると対応がずれてブロックの終わりを見失う。
    /// </para>
    /// </remarks>
    private static (string Text, int BraceDelta) ScanLine(string raw, ref ScanState state)
    {
        var builder = new StringBuilder(raw.Length);
        var braceDelta = 0;
        var index = 0;

        while (index < raw.Length)
        {
            if (state == ScanState.BlockComment)
            {
                var end = raw.IndexOf("*/", index, StringComparison.Ordinal);

                if (end < 0)
                {
                    break;
                }

                state = ScanState.None;
                index = end + 2;
                continue;
            }

            if (state == ScanState.TripleQuote)
            {
                var end = raw.IndexOf("'''", index, StringComparison.Ordinal);

                if (end < 0)
                {
                    break;
                }

                state = ScanState.None;
                index = end + 3;
                continue;
            }

            var rest = raw.AsSpan(index);

            // 複数行リテラルの開始。記号そのものは残す（行の種類の判定に使う）
            if (rest.StartsWith("'''", StringComparison.Ordinal))
            {
                state = ScanState.TripleQuote;
                builder.Append("'''");
                index += 3;
                continue;
            }

            if (rest.StartsWith("//", StringComparison.Ordinal))
            {
                break;
            }

            if (rest.StartsWith("/*", StringComparison.Ordinal))
            {
                state = ScanState.BlockComment;
                index += 2;
                continue;
            }

            var ch = raw[index];

            if (ch is '\'' or '"')
            {
                index = AppendLiteral(raw, index, ch, builder);
                continue;
            }

            if (ch == '{')
            {
                braceDelta++;
            }
            else if (ch == '}')
            {
                braceDelta--;
            }

            builder.Append(ch);
            index++;
        }

        return (builder.ToString(), braceDelta);
    }

    /// <summary>1 行で閉じるリテラルを引用符ごと素通しで写し、次の走査位置を返す</summary>
    /// <remarks>閉じないまま行末に達した場合は、その行の残りをリテラルとして扱う</remarks>
    private static int AppendLiteral(string raw, int start, char quote, StringBuilder builder)
    {
        builder.Append(raw[start]);
        var index = start + 1;

        while (index < raw.Length)
        {
            // エスケープ（\' / \" / \\）は 2 文字で 1 組
            if (raw[index] == '\\' && index + 1 < raw.Length)
            {
                builder.Append(raw[index]).Append(raw[index + 1]);
                index += 2;
                continue;
            }

            builder.Append(raw[index]);
            index++;

            if (raw[index - 1] == quote)
            {
                break;
            }
        }

        return index;
    }

    /// <summary>
    /// 設定ブロック（<c>[…]</c>）の開始位置を返す（無ければ -1）
    /// </summary>
    /// <remarks>
    /// リテラルの外にあり、かつ行頭または空白の直後にある <c>[</c> だけを設定の開始とみなす。
    /// 単純に最初の <c>[</c> を採ると、囲まれていない配列型（<c>integer[]</c>）の角括弧を
    /// 設定の開始と取り違え、型が <c>integer</c> へ化ける。
    /// </remarks>
    private static int FindSettingsBracket(string line)
    {
        var index = 0;

        while (index < line.Length)
        {
            var ch = line[index];

            if (ch is '\'' or '"')
            {
                index = SkipLiteral(line, index, ch);
                continue;
            }

            if (ch == '[' && (index == 0 || char.IsWhiteSpace(line[index - 1])))
            {
                return index;
            }

            index++;
        }

        return -1;
    }

    /// <summary>リテラルを読み飛ばし、次の走査位置を返す</summary>
    private static int SkipLiteral(string line, int start, char quote)
    {
        var index = start + 1;

        while (index < line.Length)
        {
            if (line[index] == '\\' && index + 1 < line.Length)
            {
                index += 2;
                continue;
            }

            index++;

            if (line[index - 1] == quote)
            {
                break;
            }
        }

        return index;
    }

    /// <summary>リテラルの外にある区切り文字で分割する（空要素は捨て、前後の空白は落とす）</summary>
    private static List<string> SplitOutsideLiterals(string text, char separator)
    {
        var items = new List<string>();
        var builder = new StringBuilder();
        var index = 0;

        while (index < text.Length)
        {
            var ch = text[index];

            if (ch is '\'' or '"')
            {
                var end = SkipLiteral(text, index, ch);
                builder.Append(text, index, end - index);
                index = end;
                continue;
            }

            if (ch == separator)
            {
                AddIfNotEmpty(items, builder);
                index++;
                continue;
            }

            builder.Append(ch);
            index++;
        }

        AddIfNotEmpty(items, builder);
        return items;

        static void AddIfNotEmpty(List<string> items, StringBuilder builder)
        {
            var item = builder.ToString().Trim();
            builder.Clear();

            if (item.Length > 0)
            {
                items.Add(item);
            }
        }
    }

    /// <summary>引用識別子（<c>"列 名"</c>）なら引用符を外して復元する（素の識別子はそのまま）</summary>
    private static string UnquoteIdentifier(string token) =>
        token.Length >= 2 && token[0] == '"' && token[^1] == '"'
            ? DbmlLiteral.Unescape(token[1..^1], '"')
            : token;

    /// <summary>
    /// カラムを 1 つも持たないエンティティへ既定の PK 列（<c>ID int</c>）を補う
    /// </summary>
    private static void EnsureEntitiesHaveColumns(IEnumerable<Entity> entities)
    {
        foreach (var entity in entities)
        {
            if (entity.Columns.Count == 0)
            {
                entity.Columns.Add(
                    new Column
                    {
                        Name = "ID",
                        DataType = "int",
                        IsPrimaryKey = true,
                        IsNullable = false,
                    }
                );
            }
        }
    }

    /// <summary>
    /// <c>Ref:</c> 行に書かれた列名を、各リレーションの列ペア（宣言順）へ解決する
    /// </summary>
    /// <remarks>
    /// <b>行の列名が正本</b>で推論は行わない（複合外部キーがそのまま往復する）。解決した子列には FK フラグを立てる。
    /// 多対多はジャンクションテーブル前提のためカラムを割り当てない。
    /// 両側の列数が食い違う行や、テーブルに存在しない列名を含む行は、その行の列対応だけを捨てて
    /// リレーション自体は残す（不正な索引行を読み飛ばすのと同じ寛容さ。列ペアなしのリレーションは
    /// 外部キー句を作らないため、GUI 側で対応付けを補える）
    /// </remarks>
    private static void ResolveRelationshipColumns(
        IReadOnlyDictionary<string, Entity> entities,
        IEnumerable<(
            Relationship Relationship,
            List<string> SourceColumns,
            List<string> TargetColumns
        )> pendingRelationshipColumns
    )
    {
        foreach (var (relationship, sourceColumns, targetColumns) in pendingRelationshipColumns)
        {
            if (relationship.Type == RelationshipType.ManyToMany)
            {
                continue;
            }

            if (sourceColumns.Count == 0 || sourceColumns.Count != targetColumns.Count)
            {
                continue;
            }

            var source = entities.Values.First(entity => entity.Id == relationship.SourceEntityId);
            var target = entities.Values.First(entity => entity.Id == relationship.TargetEntityId);
            var pairs = new List<RelationshipColumnPair>();
            var foreignKeyColumns = new List<Column>();

            for (var i = 0; i < sourceColumns.Count; i++)
            {
                var sourceColumn = FindColumn(source, sourceColumns[i]);
                var targetColumn = FindColumn(target, targetColumns[i]);

                if (sourceColumn is null || targetColumn is null)
                {
                    pairs.Clear();
                    break;
                }

                pairs.Add(new RelationshipColumnPair(sourceColumn.Id, targetColumn.Id));
                foreignKeyColumns.Add(targetColumn);
            }

            if (pairs.Count == 0)
            {
                continue;
            }

            relationship.ColumnPairs = pairs;

            foreach (var column in foreignKeyColumns)
            {
                column.IsForeignKey = true;
            }
        }
    }

    /// <summary>テーブルの列を名前で検索する（前後の空白を除き大文字小文字を区別しない）</summary>
    private static Column? FindColumn(Entity entity, string columnName) =>
        entity.Columns.FirstOrDefault(column => SchemaNames.AreSame(column.Name, columnName));

    /// <summary>
    /// <c>Table 名前 {</c> 形式のテーブル開始行に一致する正規表現を生成する
    /// </summary>
    /// <remarks>
    /// 名前は素の識別子と引用識別子（<c>Table "Order Details" {</c>）の双方を受け付ける。
    /// 名前の後ろのテーブル設定（<c>Table users [owner: 'x'] {</c>）は読み飛ばす
    /// （QuickER が表せない情報しか載らないため、拒否せず無視する）
    /// </remarks>
    [GeneratedRegex(
        """^Table\s+(?<table>"(?:\\.|[^"\\])*"|\S+)\s*(?:\[[^\]]*\]\s*)?\{$""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    )]
    private static partial Regex TableHeaderLineRegex();

    /// <summary>
    /// QuickER が扱わない DBML のトップレベルブロックの開始行に一致する正規表現を生成する
    /// </summary>
    /// <remarks>
    /// いずれも QuickER の意味モデルが表せない情報だけを持つため、ブロックごと読み飛ばす
    /// （他ツールが書いたファイルを従来どおり取り込めるようにするため）。
    /// リレーションを運ぶ <c>Ref</c> は<b>含めない</b>——読み飛ばすと関係が黙って消える
    /// </remarks>
    [GeneratedRegex(
        @"^(project|enum|tablegroup|tablepartial|note|records)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    )]
    private static partial Regex SkippableBlockLineRegex();

    /// <summary>
    /// <c>Table</c> ブロック内の未対応ブロック（<c>checks</c> / <c>records</c>）の開始行に一致する正規表現を生成する
    /// </summary>
    /// <remarks>
    /// <para>読み飛ばさないとカラム定義として解釈され、幻のカラムができる。</para>
    /// <para>
    /// <b>キーワードだけで判定してはいけない</b>＝<c>records int</c> / <c>checks int</c> という<b>列</b>が
    /// ブロックとみなされて無言で消える（エクスポートは列名を素の識別子で書くので往復が壊れる）。
    /// ブロックの開始行は「キーワードのあと、設定の <c>[</c> より前に <c>{</c> がある」形で見分ける
    /// （<c>records t(id) {</c> のように名前や引数を挟む形と、1 行で閉じる <c>checks { ... }</c> の両方を拾う。
    /// 列定義は <c>名前 型 [設定]</c> なので、<c>{</c> が現れるとしても <c>[</c> より後ろになる）。
    /// </para>
    /// </remarks>
    [GeneratedRegex(
        @"^(checks|records)\b[^\[\r\n]*\{",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    )]
    private static partial Regex SkippableTableBlockLineRegex();

    /// <summary><c>Ref</c> で始まる行に一致する正規表現を生成する（未対応形式の検出に使う）</summary>
    [GeneratedRegex(@"^ref\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex RefKeywordLineRegex();

    /// <summary><c>Note</c> で始まる行に一致する正規表現を生成する（未対応の複数行 Note の検出に使う）</summary>
    /// <remarks>
    /// <b>キーワードだけで判定してはいけない</b>＝<c>note varchar</c> という<b>列</b>を複数行 Note とみなして
    /// 取込全体を失敗させる。複数行 Note は <c>Note {</c> か <c>Note: '''</c> の形なので、キーワードの直後が
    /// <c>:</c> か <c>{</c> のときだけ一致させる（1 行の <c>Note: 'テキスト'</c> は手前の
    /// <see cref="TableNoteLineRegex"/> が先に食べる）。
    /// </remarks>
    [GeneratedRegex(@"^note\s*[:{]", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex NoteKeywordLineRegex();

    /// <summary>テーブルの説明を表す <c>Note: '...'</c> 行に一致する正規表現を生成する</summary>
    /// <remarks>
    /// リテラルの中身はエスケープを組で読む（<c>\\.</c>）。<c>\'</c> だけを組にすると、<c>\</c> で終わる文字列
    /// （<c>'C:\\'</c>）の閉じ引用をエスケープと取り違える（カラム設定の note・索引の name も同じ）
    /// </remarks>
    [GeneratedRegex(
        @"^Note:\s*'(?<note>(?:\\.|[^'\\])*)'$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    )]
    private static partial Regex TableNoteLineRegex();

    /// <summary><c>Ref</c> 行の設定ブロック内の <c>delete: ...</c> / <c>update: ...</c> に一致する正規表現を生成する</summary>
    [GeneratedRegex(
        @"^(?<kind>delete|update):\s*(?<action>.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    )]
    private static partial Regex ReferentialActionSettingRegex();

    /// <summary>
    /// <c>Ref</c> 行（DBML 標準の短縮形）に一致する正規表現を生成する。
    /// </summary>
    /// <remarks>
    /// 形は <c>Ref 名前: 親.a &lt; 子.x [delete: …, update: …]</c>。名前（制約名）は省略でき（<c>Ref:</c>）、
    /// 素の識別子と引用識別子（<c>"FK \"x\""</c>＝エスケープは <see cref="UnquoteIdentifier"/> が戻す）の双方を受け付ける。
    /// 設定ブロックは端点の後ろに置き、中身（<c>delete</c> / <c>update</c>）は <see cref="ParseRelationshipSettings"/> が解釈する。
    /// エンドポイントは単一列（<c>親.a</c>）と複合 Ref 構文（<c>親.(a, b)</c>）の双方を受け付ける。
    /// 複合の列リストと設定ブロックは、リテラル（引用識別子・単一引用符の文字列）の中の <c>)</c> / <c>]</c> で
    /// 終わらない（<c>)</c> を含む引用列名の複合外部キーを書き出せるため）
    /// </remarks>
    [GeneratedRegex(
        """^Ref(?:\s+(?<name>"(?:\\.|[^"\\])*"|\w+))?\s*:\s*(?<leftTable>"(?:\\.|[^"\\])*"|\w+)\.(?:\((?<leftColumns>(?:"(?:\\.|[^"\\])*"|[^)"])*)\)|(?<leftColumn>"(?:\\.|[^"\\])*"|\w+))\s*(?<symbol><>|<|>|-)\s*(?<rightTable>"(?:\\.|[^"\\])*"|\w+)\.(?:\((?<rightColumns>(?:"(?:\\.|[^"\\])*"|[^)"])*)\)|(?<rightColumn>"(?:\\.|[^"\\])*"|\w+))\s*(?:\[(?<settings>(?:'(?:\\.|[^'\\])*'|"(?:\\.|[^"\\])*"|[^\]'"])*)\])?\s*$""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    )]
    private static partial Regex RelationshipLineRegex();

    /// <summary>
    /// <c>Ref</c> 行の短縮形の書き出し（<c>Ref:</c> / <c>Ref 名前:</c>）に一致する正規表現を生成する
    /// </summary>
    /// <remarks>
    /// ここに一致した行はリレーション行として <see cref="RelationshipLineRegex"/> で解析し、解析できなければ
    /// 行を名指しして拒否する（端点の前に設定ブロックを置いた形もここで拾って拒否される）。
    /// 名前の後ろに <c>:</c> が無いブロック形式（<c>Ref 名前 {</c>）は一致しない＝<see cref="RefKeywordLineRegex"/> が拒否する
    /// </remarks>
    [GeneratedRegex(
        """^Ref(?:\s+(?:"(?:\\.|[^"\\])*"|\w+))?\s*:""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    )]
    private static partial Regex RelationshipLineStartRegex();

    /// <summary>カラム設定のうち値つきの <c>ref:</c>（インライン参照）の書き出しに一致する正規表現を生成する</summary>
    /// <remarks>ここに一致して <see cref="InlineReferenceLineRegex"/> に一致しない設定は、項目を名指しして断る</remarks>
    [GeneratedRegex(@"^ref\s*:", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex InlineReferenceStartLineRegex();

    /// <summary>カラム設定のインライン参照 <c>ref: 記号 テーブル.列</c> に一致する正規表現を生成する</summary>
    /// <remarks>
    /// 記号は <c>Ref</c> 行と同じ 4 つ。テーブル名・列名は素の識別子と引用識別子の双方を受け付ける。
    /// スキーマ修飾（<c>schema.table.column</c>）は <c>Ref</c> 行と同じく未対応
    /// </remarks>
    [GeneratedRegex(
        """^ref\s*:\s*(?<symbol><>|<|>|-)\s*(?<table>"(?:\\.|[^"\\])*"|\w+)\.(?<column>"(?:\\.|[^"\\])*"|\w+)$""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    )]
    private static partial Regex InlineReferenceLineRegex();

    /// <summary>カラム設定の <c>note: '...'</c>（<c>\'</c> エスケープ対応）に一致する正規表現を生成する</summary>
    [GeneratedRegex(
        @"^note:\s*'(?<note>(?:\\.|[^'\\])*)'$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    )]
    private static partial Regex ColumnNoteRegex();

    /// <summary><c>Indexes {</c> 形式のブロック開始行に一致する正規表現を生成する</summary>
    [GeneratedRegex(@"^Indexes\s*\{$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex IndexesHeaderLineRegex();

    /// <summary>索引設定の <c>name: '...'</c>（<c>\'</c> エスケープ対応）に一致する正規表現を生成する</summary>
    [GeneratedRegex(
        @"^name:\s*'(?<name>(?:\\.|[^'\\])*)'$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    )]
    private static partial Regex IndexSettingNameRegex();
}
