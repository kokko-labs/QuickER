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
///   <item>カラム設定: <c>pk</c> / <c>ref</c> / <c>unique</c> / <c>null</c> / <c>not null</c> / <c>note: '...'</c>（大文字小文字を区別しない）</item>
///   <item><c>Indexes { … }</c> ブロック: <c>unique</c> 設定を持つ索引のみ一意制約として取り込む（<c>(a, b) [unique, name: '…']</c> / 単一列は括弧なしも可）</item>
///   <item><c>Ref:</c> 行: 多重度記号は <c>-</c>（1対1）/ <c>&lt;</c>（1対多）/ <c>&lt;&gt;</c>（多対多）のみ（<c>&gt;</c>（多対1）は未対応）。エンドポイントは単一列 <c>親.a</c> と複合 Ref 構文 <c>親.(a, b)</c> の双方に対応し、<b>行に書かれた列名がそのまま外部キーの構成列になる</b>（推論しない）。参照先の <c>Table</c> より前に書かれていてもよい（テーブルを読み切ってから解析する）</item>
///   <item><c>//</c> 行コメント</item>
/// </list>
/// Project・Enum・TableGroup・複数行 Note ブロック等の DBML 構文は未対応
/// （<c>unique</c> でない索引は「一意制約ではない」ため読み飛ばす）
/// </remarks>
public static partial class DbmlImporter
{
    /// <summary><c>Table 名前 {</c> 形式のテーブル開始行を検出する正規表現</summary>
    private static readonly Regex TableHeaderRegex = TableHeaderLineRegex();

    /// <summary><c>Ref:</c> 行を解析する正規表現</summary>
    private static readonly Regex RelationshipRegex = RelationshipLineRegex();

    /// <summary>カラム設定の <c>note: '...'</c> を解析する正規表現</summary>
    private static readonly Regex NoteRegex = ColumnNoteRegex();

    /// <summary>テーブルブロック内の <c>Note: '...'</c> 行（テーブルの説明）を解析する正規表現</summary>
    private static readonly Regex TableNoteRegex = TableNoteLineRegex();

    /// <summary><c>Ref:</c> 行の設定ブロック内の参照アクション設定を解析する正規表現</summary>
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

    /// <summary>
    /// DBML ファイルを読み込み ER 図へ変換する
    /// </summary>
    public static ErDiagram Load(string path)
    {
        return Parse(File.ReadAllText(path));
    }

    /// <summary>
    /// DBML テキストを解析して ER 図を生成する
    /// </summary>
    /// <returns>復元した <see cref="ErDiagram"/></returns>
    /// <exception cref="InvalidDataException">構文不正・テーブル名重複・未定義テーブル参照などを検出した場合</exception>
    public static ErDiagram Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidDataException(Strings.Dbml_EmptyText);
        }

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var entities = new Dictionary<string, Entity>(StringComparer.OrdinalIgnoreCase);
        var relationships = new List<Relationship>();
        // Ref: 行の列名は、空テーブルへの既定 PK 列補完まで済んだ後にまとめて列ペアへ解決する
        var pendingRelationshipColumns =
            new List<(Relationship Relationship, List<string> Source, List<string> Target)>();
        // Ref: 行は、参照先テーブルの定義より前に書かれていてもよいよう、行ループを抜けてから解析する
        // （他ツール製の DBML はファイル先頭へ Ref: をまとめる書き方がある）
        var pendingRefLines = new List<(string Line, int LineNumber)>();
        // Indexes ブロックの一意索引は、列定義より前に書かれていても解決できるよう最後にまとめて紐付ける
        // （解決は行ループを抜けた後になるため、診断用に定義行の行番号も持ち回る）
        var pendingUniqueIndexes =
            new List<(Entity Entity, string? Name, List<string> Columns, int LineNumber)>();
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

                        var uniqueIndex = ParseUniqueIndex(line, currentEntity.TableName);

                        if (uniqueIndex is not null)
                        {
                            pendingUniqueIndexes.Add(
                                (
                                    currentEntity,
                                    uniqueIndex.Value.Name,
                                    uniqueIndex.Value.Columns,
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

                    var (column, isUnique) = ParseColumn(line, currentEntity.TableName);
                    currentEntity.Columns.Add(column);

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

                if (line.StartsWith("Ref:", StringComparison.OrdinalIgnoreCase))
                {
                    // 解析はテーブルを読み切ってから（前方参照＝定義より前に書かれた Ref: を受け付ける）
                    pendingRefLines.Add((line, lineNumber));
                    continue;
                }

                // QuickER が表せない情報しか持たないブロックは読み飛ばす（従来と同じ寛容さ）
                if (SkippableBlockRegex.IsMatch(line))
                {
                    skipDepth = Math.Max(0, braceDelta);
                    continue;
                }

                // 名前付き Ref（Ref name: …）とブロック形式（Ref … { … }）は未対応。
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

        foreach (var (refLine, refLineNumber) in pendingRefLines)
        {
            try
            {
                var (relationship, sourceColumns, targetColumns) = ParseRelationship(
                    refLine,
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
        ResolveUniqueIndexes(pendingUniqueIndexes);
        ResolveRelationshipColumns(entities, pendingRelationshipColumns);

        return new ErDiagram { Entities = entities.Values.ToList(), Relationships = relationships };
    }

    /// <summary>
    /// DBML のカラム定義行（<c>名前 型 [設定, ...]</c>）を解析する
    /// </summary>
    /// <remarks>
    /// <para>
    /// 名前は素の識別子と引用識別子（<c>"列 名"</c>）の双方を受け付ける。型名は空白を含んでもよく、
    /// 素の記法（<c>timestamp without time zone</c>）と引用（<c>"double precision"</c>）の双方に対応する。
    /// 設定省略時は NULL 許可を既定とし、<c>pk</c> 指定時は NOT NULL を強制する。
    /// note 内のエスケープ（<c>\\</c> と <c>\'</c>）は <see cref="DbmlLiteral.Unescape"/> で復元する
    /// </para>
    /// <para>
    /// 設定ブロックの開始は <see cref="FindSettingsBracket"/> が決める（最初の <c>[</c> ではない）。
    /// 囲まれていない配列型 <c>integer[]</c> の角括弧を設定の開始と取り違えないため。
    /// </para>
    /// </remarks>
    /// <returns>復元したカラムと、カラム設定 <c>unique</c> が指定されていたか</returns>
    /// <exception cref="InvalidDataException">名前と型の 2 トークンに満たない場合</exception>
    private static (Column Column, bool IsUnique) ParseColumn(string line, string tableName)
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

        foreach (var option in SplitOptions(optionText))
        {
            if (string.Equals(option, "pk", StringComparison.OrdinalIgnoreCase))
            {
                column.IsPrimaryKey = true;
                column.IsNullable = false;
                continue;
            }

            if (string.Equals(option, "ref", StringComparison.OrdinalIgnoreCase))
            {
                column.IsForeignKey = true;
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

        return (column, isUnique);
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
    /// <c>Indexes</c> ブロック内の 1 行を解析し、一意索引なら制約名と構成列名を返す
    /// </summary>
    /// <returns>一意索引なら（名前・構成列名）。<c>unique</c> でない索引は <c>null</c>（読み飛ばす）</returns>
    /// <exception cref="InvalidDataException">索引行の構文が解釈できない場合</exception>
    private static (string? Name, List<string> Columns)? ParseUniqueIndex(
        string line,
        string tableName
    )
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
        string? name = null;

        foreach (var option in SplitOptions(settingText))
        {
            if (string.Equals(option, "unique", StringComparison.OrdinalIgnoreCase))
            {
                isUnique = true;
                continue;
            }

            var nameMatch = IndexNameRegex.Match(option);

            if (nameMatch.Success)
            {
                name = DbmlLiteral.Unescape(nameMatch.Groups["name"].Value);
            }
        }

        // unique でない索引は一意制約ではないため取り込まない（インデックス自体はモデルに持たない）
        return isUnique ? (name, columns) : null;
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
                    string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase)
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

    /// <summary>
    /// DBML の <c>Ref:</c> 行を解析してリレーションを生成する
    /// </summary>
    /// <remarks>
    /// 左辺テーブルを親（Source）、右辺テーブルを子（Target）として扱う。
    /// 行中のカラム名（単一列 <c>親.a</c> / 複合 <c>親.(a, b)</c>）は外部キーの構成列そのものとして持ち帰り、
    /// 後段の <see cref="ResolveRelationshipColumns"/> が列ペアへ解決する（推論はしない）
    /// </remarks>
    /// <exception cref="InvalidDataException">構文不一致、または参照先テーブルが未定義の場合</exception>
    private static (
        Relationship Relationship,
        List<string> SourceColumns,
        List<string> TargetColumns
    ) ParseRelationship(string line, IReadOnlyDictionary<string, Entity> entities)
    {
        var match = RelationshipRegex.Match(line);

        if (!match.Success)
        {
            throw new InvalidDataException(
                string.Format(Strings.Dbml_RelationshipParseError, line)
            );
        }

        var leftTable = UnquoteIdentifier(match.Groups["leftTable"].Value);
        var rightTable = UnquoteIdentifier(match.Groups["rightTable"].Value);
        var symbol = match.Groups["symbol"].Value;
        var (constraintName, onDelete, onUpdate) = ParseRelationshipSettings(
            match.Groups["settings"].Success ? match.Groups["settings"].Value : string.Empty
        );

        if (!entities.ContainsKey(leftTable))
        {
            throw new InvalidDataException(
                string.Format(Strings.Dbml_RelationshipSourceUndefined, leftTable)
            );
        }

        if (!entities.ContainsKey(rightTable))
        {
            throw new InvalidDataException(
                string.Format(Strings.Dbml_RelationshipTargetUndefined, rightTable)
            );
        }

        var relationship = new Relationship
        {
            SourceEntityId = entities[leftTable].Id,
            TargetEntityId = entities[rightTable].Id,
            Type = symbol switch
            {
                "-" => RelationshipType.OneToOne,
                "<" => RelationshipType.OneToMany,
                "<>" => RelationshipType.ManyToMany,
                _ => throw new InvalidDataException(
                    string.Format(Strings.Dbml_UnsupportedRelationshipSymbol, symbol)
                ),
            },
            ConstraintName = constraintName,
            OnDelete = onDelete,
            OnUpdate = onUpdate,
        };

        return (
            relationship,
            ReadEndpointColumns(match, "leftColumns", "leftColumn"),
            ReadEndpointColumns(match, "rightColumns", "rightColumn")
        );
    }

    /// <summary><c>Ref:</c> 行の設定ブロックから制約名（<c>note</c>）と参照アクションを取り出す</summary>
    /// <remarks>
    /// 未知の設定は読み飛ばす。DBML の <c>restrict</c> はモデルに対応する値が無いため
    /// <see cref="ForeignKeyReferentialAction.NoAction"/> へ倒れる
    /// （<see cref="ForeignKeyReferentialActionHelper.Parse"/> の既定）。
    /// 設定が無い場合は制約名 null・両アクション既定を返す
    /// </remarks>
    private static (
        string? ConstraintName,
        ForeignKeyReferentialAction OnDelete,
        ForeignKeyReferentialAction OnUpdate
    ) ParseRelationshipSettings(string settingText)
    {
        string? constraintName = null;
        var onDelete = ForeignKeyReferentialAction.NoAction;
        var onUpdate = ForeignKeyReferentialAction.NoAction;

        foreach (var setting in SplitOptions(settingText))
        {
            var noteMatch = NoteRegex.Match(setting);

            if (noteMatch.Success)
            {
                constraintName = DbmlLiteral.Unescape(noteMatch.Groups["note"].Value);
                continue;
            }

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

        return (constraintName, onDelete, onUpdate);
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
    /// <c>note: 'a, b'</c> のようにクォート内へ含まれるカンマは区切りとして扱わない。
    /// クォートの開閉判定では直前の <c>\</c> によるエスケープを考慮する
    /// </remarks>
    private static IEnumerable<string> SplitOptions(string optionText)
    {
        if (string.IsNullOrWhiteSpace(optionText))
        {
            yield break;
        }

        var builder = new System.Text.StringBuilder();
        var inQuote = false;

        foreach (var ch in optionText)
        {
            if (ch == '\'' && (builder.Length == 0 || builder[^1] != '\\'))
            {
                inQuote = !inQuote;
            }

            if (ch == ',' && !inQuote)
            {
                var item = builder.ToString().Trim();

                if (item.Length > 0)
                {
                    yield return item;
                }

                builder.Clear();
                continue;
            }

            builder.Append(ch);
        }

        var last = builder.ToString().Trim();

        if (last.Length > 0)
        {
            yield return last;
        }
    }

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

    /// <summary>テーブルの列を名前で検索する（大文字小文字を区別しない）</summary>
    private static Column? FindColumn(Entity entity, string columnName) =>
        entity.Columns.FirstOrDefault(column =>
            string.Equals(column.Name, columnName, StringComparison.OrdinalIgnoreCase)
        );

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
    /// <remarks>読み飛ばさないとカラム定義として解釈され、幻のカラムができる</remarks>
    [GeneratedRegex(@"^(checks|records)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex SkippableTableBlockLineRegex();

    /// <summary><c>Ref</c> で始まる行に一致する正規表現を生成する（未対応形式の検出に使う）</summary>
    [GeneratedRegex(@"^ref\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex RefKeywordLineRegex();

    /// <summary><c>Note</c> で始まる行に一致する正規表現を生成する（未対応の複数行 Note の検出に使う）</summary>
    [GeneratedRegex(@"^note\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex NoteKeywordLineRegex();

    /// <summary>テーブルの説明を表す <c>Note: '...'</c> 行に一致する正規表現を生成する</summary>
    [GeneratedRegex(
        @"^Note:\s*'(?<note>(?:\\'|[^'])*)'$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    )]
    private static partial Regex TableNoteLineRegex();

    /// <summary><c>Ref:</c> 行の設定ブロック内の <c>delete: ...</c> / <c>update: ...</c> に一致する正規表現を生成する</summary>
    [GeneratedRegex(
        @"^(?<kind>delete|update):\s*(?<action>.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    )]
    private static partial Regex ReferentialActionSettingRegex();

    /// <summary>
    /// <c>Ref:</c> 行に一致する正規表現を生成する。設定ブロックは <see cref="DbmlExporter"/> 独自形式に合わせ
    /// <c>Ref:</c> 直後へ置き、中身（<c>note</c> / <c>delete</c> / <c>update</c>）は
    /// <see cref="ParseRelationshipSettings"/> が解釈する。エンドポイントは単一列（<c>親.a</c>）と
    /// 複合 Ref 構文（<c>親.(a, b)</c>）の双方を受け付ける
    /// </summary>
    [GeneratedRegex(
        """^Ref:(?:\s*\[(?<settings>[^\]]*)\])?\s*(?<leftTable>"(?:\\.|[^"\\])*"|\w+)\.(?:\((?<leftColumns>[^)]*)\)|(?<leftColumn>"(?:\\.|[^"\\])*"|\w+))\s*(?<symbol><>|<|-)\s*(?<rightTable>"(?:\\.|[^"\\])*"|\w+)\.(?:\((?<rightColumns>[^)]*)\)|(?<rightColumn>"(?:\\.|[^"\\])*"|\w+))\s*$""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    )]
    private static partial Regex RelationshipLineRegex();

    /// <summary>カラム設定の <c>note: '...'</c>（<c>\'</c> エスケープ対応）に一致する正規表現を生成する</summary>
    [GeneratedRegex(
        @"^note:\s*'(?<note>(?:\\'|[^'])*)'$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    )]
    private static partial Regex ColumnNoteRegex();

    /// <summary><c>Indexes {</c> 形式のブロック開始行に一致する正規表現を生成する</summary>
    [GeneratedRegex(@"^Indexes\s*\{$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex IndexesHeaderLineRegex();

    /// <summary>索引設定の <c>name: '...'</c>（<c>\'</c> エスケープ対応）に一致する正規表現を生成する</summary>
    [GeneratedRegex(
        @"^name:\s*'(?<name>(?:\\'|[^'])*)'$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    )]
    private static partial Regex IndexSettingNameRegex();
}
