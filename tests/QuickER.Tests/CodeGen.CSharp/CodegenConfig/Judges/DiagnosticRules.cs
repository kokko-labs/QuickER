using QuickER.CodeGen.CSharp;
using QuickER.CodeGen.CSharp.Resources;
using QuickER.Dialects;

namespace QuickER.Tests.CodeGen.CSharp.CodegenConfig.Judges;

/// <summary>期待する診断 1 件</summary>
/// <param name="Severity">重要度</param>
/// <param name="Key">種類（resx のキー）</param>
/// <param name="Items">名指しする項目（それぞれが、捕まえた項目のどれか 1 つに含まれる。null なら項目を見ない）</param>
/// <param name="Mentions">
/// 名指しする語（それぞれが、捕まえた項目のどれかに含まれればよい。項目の数・組み分けは見ない。
/// 元のテストが文の中の語だけを見ているときに、<paramref name="Items"/> と違って項目の組み分けに依らず同じ強さで書く）
/// </param>
/// <param name="MentionsInOrder">
/// <paramref name="Mentions"/> が、この並びで捕まえた項目に現れること（それぞれの語を含む最初の項目の位置が、語の並びどおりに増える。
/// 元のテストが一覧の並び順を見ているときに使う）
/// </param>
internal sealed record ExpectedDiagnostic(
    GenerationDiagnosticSeverity Severity,
    string Key,
    IReadOnlyList<string>? Items = null,
    IReadOnlyList<string>? Mentions = null,
    bool MentionsInOrder = false
)
{
    /// <summary>情報（項目の組み分けを見ず、語だけを名指しする）</summary>
    public static ExpectedDiagnostic InfoMentioning(string key, params string[] mentions) =>
        new(GenerationDiagnosticSeverity.Info, key, null, mentions);

    /// <summary>情報（語が、この並びで項目に現れることまで見る）</summary>
    public static ExpectedDiagnostic InfoMentioningInOrder(string key, params string[] mentions) =>
        new(GenerationDiagnosticSeverity.Info, key, null, mentions, MentionsInOrder: true);

    /// <summary>エラー（項目の組み分けを見ず、語だけを名指しする）</summary>
    public static ExpectedDiagnostic ErrorMentioning(string key, params string[] mentions) =>
        new(GenerationDiagnosticSeverity.Error, key, null, mentions);

    /// <summary>警告（項目の組み分けを見ず、語だけを名指しする）</summary>
    public static ExpectedDiagnostic WarningMentioning(string key, params string[] mentions) =>
        new(GenerationDiagnosticSeverity.Warning, key, null, mentions);

    /// <summary>エラー</summary>
    public static ExpectedDiagnostic Error(string key, params string[] items) =>
        new(GenerationDiagnosticSeverity.Error, key, items);

    /// <summary>警告</summary>
    public static ExpectedDiagnostic Warning(string key, params string[] items) =>
        new(GenerationDiagnosticSeverity.Warning, key, items);

    /// <summary>エラー（項目を見ない。項目が数えきれない・毎回変わるときだけ使う）</summary>
    public static ExpectedDiagnostic ErrorAnyItems(string key) =>
        new(GenerationDiagnosticSeverity.Error, key);

    /// <summary>警告（項目を見ない。項目が数えきれない・毎回変わるときだけ使う）</summary>
    public static ExpectedDiagnostic WarningAnyItems(string key) =>
        new(GenerationDiagnosticSeverity.Warning, key);

    /// <summary>情報（種類の集合だけを見る）</summary>
    public static ExpectedDiagnostic Info(string key) =>
        new(GenerationDiagnosticSeverity.Info, key);

    /// <summary>説明</summary>
    public override string ToString() =>
        $"[{Severity}] {Key}"
        + (Items is null ? string.Empty : " (" + string.Join(" / ", Items) + ")")
        + (Mentions is null ? string.Empty : " 語: " + string.Join(" / ", Mentions));
}

/// <summary>期待の強さ</summary>
internal enum DiagnosticStrength
{
    /// <summary>エラーと警告は（重要度・種類・項目）の過不足なし、情報は種類の集合が一致、どの書式にも当たらない診断は赤</summary>
    Exact,

    /// <summary>期待した診断を含む（ほかの診断があってもよい）</summary>
    Contains,

    /// <summary>期待した（重要度・種類）の件数がちょうど一致する</summary>
    Count,
}

/// <summary>診断の期待</summary>
/// <param name="Strength">強さ</param>
/// <param name="Diagnostics">期待する診断</param>
/// <param name="Count">件数（<see cref="DiagnosticStrength.Count"/> のとき）</param>
/// <param name="Absent">
/// 出てはいけない診断（強さに依らず掛ける。重要度・種類が同じで、項目を名指ししていれば、そのすべてが捕まえた項目のどれかに含まれるものがあれば赤。
/// 項目が null か空なら、その種類の診断が 1 件でもあれば赤）
/// </param>
internal sealed record DiagnosticExpectation(
    DiagnosticStrength Strength,
    IReadOnlyList<ExpectedDiagnostic> Diagnostics,
    int Count = 0,
    IReadOnlyList<ExpectedDiagnostic>? Absent = null
)
{
    /// <summary>生成時エラーを期待するか（期待にエラーを含むか）</summary>
    public bool ExpectsError =>
        Diagnostics.Any(d => d.Severity == GenerationDiagnosticSeverity.Error);

    /// <summary>完全一致</summary>
    public static DiagnosticExpectation Exact(IReadOnlyList<ExpectedDiagnostic> diagnostics) =>
        new(DiagnosticStrength.Exact, diagnostics);

    /// <summary>包含</summary>
    public static DiagnosticExpectation Contains(params ExpectedDiagnostic[] diagnostics) =>
        new(DiagnosticStrength.Contains, diagnostics);

    /// <summary>件数（診断が項目を名指ししていれば、項目まで一致するものだけを数える）</summary>
    public static DiagnosticExpectation CountOf(ExpectedDiagnostic diagnostic, int count) =>
        new(DiagnosticStrength.Count, [diagnostic], count);

    /// <summary>出てはいけない診断を足す（期待の強さは変えない）</summary>
    /// <param name="absent">出てはいけない診断</param>
    public DiagnosticExpectation Without(params ExpectedDiagnostic[] absent) =>
        this with
        {
            Absent = absent,
        };

    /// <summary>実際の診断を期待と照合する（問題を返す）</summary>
    /// <param name="actual">生成の診断</param>
    public IReadOnlyList<string> Check(IReadOnlyList<GenerationDiagnostic> actual)
    {
        var identified = actual.Select(DiagnosticMatcher.Identify).ToList();
        var problems = new List<string>();

        switch (Strength)
        {
            case DiagnosticStrength.Exact:
                CheckExact(identified, problems);
                break;

            case DiagnosticStrength.Contains:
                foreach (var expected in Diagnostics)
                {
                    if (!identified.Any(candidate => Matches(expected, candidate)))
                    {
                        problems.Add($"診断: 期待した {expected} が無い");
                    }
                }

                break;

            case DiagnosticStrength.Count:
                var target = Diagnostics[0];
                var named = target.Items is { Count: > 0 } || target.Mentions is { Count: > 0 };
                var count = identified.Count(candidate =>
                    named
                        ? Matches(target, candidate)
                        : candidate.Severity == target.Severity && candidate.Key == target.Key
                );

                if (count != Count)
                {
                    problems.Add(
                        string.Create(
                            System.Globalization.CultureInfo.InvariantCulture,
                            $"診断: {target} が {count} 件（期待 {Count} 件）"
                        )
                    );
                }

                break;
        }

        foreach (var absent in Absent ?? [])
        {
            foreach (var found in identified.Where(candidate => ContainsItems(absent, candidate)))
            {
                problems.Add(
                    $"診断: 出てはいけない {absent} が出た（{found.Message.ReplaceLineEndings(" ")}）"
                );
            }
        }

        if (problems.Count > 0)
        {
            problems.Add(
                "  実際の診断: "
                    + string.Join(
                        " || ",
                        identified.Select(d =>
                            $"[{d.Severity}] {d.Key ?? "(どの書式にも当たらない)"} {d.Message.ReplaceLineEndings(" ")}"
                        )
                    )
            );
        }

        return problems;
    }

    private void CheckExact(List<IdentifiedDiagnostic> identified, List<string> problems)
    {
        foreach (var unknown in identified.Where(d => d.Key is null))
        {
            problems.Add($"診断: どの書式にも当たらない [{unknown.Severity}] {unknown.Message}");
        }

        var remaining = identified
            .Where(d => d.Key is not null && d.Severity != GenerationDiagnosticSeverity.Info)
            .ToList();

        foreach (
            var expected in Diagnostics.Where(d => d.Severity != GenerationDiagnosticSeverity.Info)
        )
        {
            var found = remaining.FirstOrDefault(candidate => Matches(expected, candidate));

            if (found is null)
            {
                problems.Add($"診断: 期待した {expected} が無い");
                continue;
            }

            remaining.Remove(found);
        }

        foreach (var extra in remaining)
        {
            problems.Add(
                $"診断: 期待していない [{extra.Severity}] {extra.Key} ({string.Join(" / ", extra.Items)})"
            );
        }

        var expectedInfo = Diagnostics
            .Where(d => d.Severity == GenerationDiagnosticSeverity.Info)
            .Select(d => d.Key)
            .ToHashSet(StringComparer.Ordinal);
        var actualInfo = identified
            .Where(d => d.Key is not null && d.Severity == GenerationDiagnosticSeverity.Info)
            .Select(d => d.Key!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var missing in expectedInfo.Except(actualInfo).Order(StringComparer.Ordinal))
        {
            problems.Add($"診断: 期待した [Info] {missing} が無い");
        }

        foreach (var extra in actualInfo.Except(expectedInfo).Order(StringComparer.Ordinal))
        {
            problems.Add($"診断: 期待していない [Info] {extra}");
        }
    }

    /// <summary>重要度・種類が同じで、期待の項目のそれぞれが捕まえた項目のどれかに含まれるか（項目の数は問わない）</summary>
    private static bool ContainsItems(
        ExpectedDiagnostic expected,
        IdentifiedDiagnostic candidate
    ) =>
        candidate.Severity == expected.Severity
        && candidate.Key == expected.Key
        && (expected.Items ?? [])
            .Concat(expected.Mentions ?? [])
            .All(item =>
                candidate.Items.Any(actual => actual.Contains(item, StringComparison.Ordinal))
            );

    /// <summary>語を含む最初の項目の位置が、語の並びどおりに増えるか</summary>
    private static bool MentionsAreInOrder(
        IReadOnlyList<string> mentions,
        IdentifiedDiagnostic candidate
    )
    {
        var previous = -1;

        foreach (var word in mentions)
        {
            var index = candidate
                .Items.Select((item, position) => (item, position))
                .Where(entry => entry.item.Contains(word, StringComparison.Ordinal))
                .Select(entry => entry.position)
                .DefaultIfEmpty(-1)
                .First();

            if (index <= previous)
            {
                return false;
            }

            previous = index;
        }

        return true;
    }

    /// <summary>重要度・種類が同じで、期待の項目が捕まえた項目へ一対一に含まれるか</summary>
    private static bool Matches(ExpectedDiagnostic expected, IdentifiedDiagnostic candidate)
    {
        if (candidate.Severity != expected.Severity || candidate.Key != expected.Key)
        {
            return false;
        }

        if (
            expected.Mentions is { } mentions
            && !mentions.All(word =>
                candidate.Items.Any(actual => actual.Contains(word, StringComparison.Ordinal))
            )
        )
        {
            return false;
        }

        if (expected.MentionsInOrder && !MentionsAreInOrder(expected.Mentions ?? [], candidate))
        {
            return false;
        }

        if (expected.Items is null)
        {
            return true;
        }

        if (expected.Items.Count != candidate.Items.Count)
        {
            return false;
        }

        var unused = candidate.Items.ToList();

        foreach (var item in expected.Items.OrderByDescending(item => item.Length))
        {
            var hit =
                unused.FirstOrDefault(actual => actual == item)
                ?? unused.FirstOrDefault(actual => actual.Contains(item, StringComparison.Ordinal));

            if (hit is null)
            {
                return false;
            }

            unused.Remove(hit);
        }

        return true;
    }
}

/// <summary>
/// 図の形とオプションから期待する診断を決める規則の表（条件 → 種類）
/// </summary>
/// <remarks>
/// <para>
/// 生成は 3 段で診断を積む。A 段（生成前の検証・型の統一）でエラーがあればそこで止まり、B 段（モデルの組み立て）でエラーがあれば止まり、
/// C 段（組み立て後の名指し）まで進めばファイルを出す（src/QuickER.CodeGen.CSharp/CSharpCodeGenerationService.cs の Generate の流れ）。
/// エラーの規則が当たった段より後の段の規則は出ない。
/// </para>
/// <para>規則ごとの出どころ（docs・CLAUDE.md・製品コードのコメント）：</para>
/// <list type="table">
///   <item><term>A MapperRequiresEntityAndEditModel</term><description>Mapper は EditModel を参照する（CSharpCodeGenerationService.Validate のコメント）</description></item>
///   <item><term>A RepositoryRequiresDataAnnotations</term><description>Repository 契約は [Table] / [Key] / [Column] を読む（同上）</description></item>
///   <item><term>A RemoteRequiresContract</term><description>CLAUDE.md「リモート契約生成」：契約が 1 つも無い構成でのリモート指定は生成時エラー</description></item>
///   <item><term>A CompositeKey（警告）</term><description>複合主キーの表を名指し（Validate のコメント「警告: 複合主キー」）</description></item>
///   <item><term>A MultiTargetEfCoreExclusive</term><description>CLAUDE.md「DB アクセスの排他と単独出力」：マルチターゲットと EF Core は排他</description></item>
///   <item><term>A MultiTargetRowVersionColumns（情報）</term><description>CLAUDE.md「マルチターゲット × rowversion」1.：統一した列を Info で通知</description></item>
///   <item><term>A SyncSupportRequiresRepositories / RequiresDialects</term><description>CLAUDE.md（ルート）「同期の生成条件と診断」・docs/cli.md の GenerateSyncSupport</description></item>
///   <item><term>B RelationshipCompositeSkipped（警告）</term><description>複合外部キーは単一キー前提の生成からナビゲーションを外す（CSharpGenerationModelBuilder.ResolveAllNavigations）</description></item>
///   <item><term>B RepositorySingleKeyOnly（警告）</term><description>docs「What gets generated」：Repository は単一主キーが前提。契約を生成するとき、主キーが 1 列でない表（複合主キー・主キーなし）を名指し（CSharpGenerationModelBuilder.BuildRepositoryClass の HasSinglePrimaryKey）</description></item>
///   <item><term>B QuerySkippedWithoutRepository（警告）</term><description>docs「Limits of the simple DSL and named queries」：名前付きクエリは表の Repository のメソッドなので、契約を生成するとき、Repository を作らない表のクエリを 1 件ずつ名指し（CSharpGenerationModelBuilder.WarnQueriesWithoutRepository。表の警告と同じ条件で、契約を生成しない構成では出さない）</description></item>
///   <item><term>B EfCoreKeylessTableExcluded（警告）</term><description>EF Core はマップする全エンティティ型にキーを要求するので、EF Core を生成するとき主キーの無い表を DbContext から外して名指し（CSharpGenerationModelBuilder.BuildEfCoreModel）</description></item>
///   <item><term>B ValueObjectForeignKeyUnified（情報）</term><description>CLAUDE.md「FK 列の値オブジェクト型統一」：列名の違う外部キーが寄ったら名指し</description></item>
///   <item><term>B IncludeGraphSkippedNavigations（情報）</term><description>自己参照は有限の Include ツリーに写せない辺を名指し（CSharpGenerationModelBuilder.IncludeGraph のコメント。契約があるときだけ）</description></item>
///   <item><term>C ExcludedUnboundedBinaryColumns（情報）</term><description>docs「Excluding unbounded binary columns」：除外 ON で除外した列を名指し</description></item>
///   <item><term>C ComputedColumns / IdentityColumns（情報）</term><description>CLAUDE.md「計算列・生成列」「DB が値を拒む採番列」：除外列を常に名指し</description></item>
///   <item><term>C NotNullComputedColumns（警告）</term><description>同期かマルチターゲットで、式を評価しないエンジンへも書く構成のときだけ（AddNotNullComputedColumnWarning のコメント）</description></item>
///   <item><term>C NotNullIdentityColumns（警告）</term><description>CLAUDE.md「DB が値を拒む採番列」：実効方言のどれかが持てない（SQLite）とき</description></item>
///   <item><term>C NonCanonicalTypeSpellingColumns（情報）</term><description>CLAUDE.md「同義の型表記の保存」：注釈 ON で綴りが戻らない列があるとき</description></item>
///   <item><term>C NullablePrimaryKeyColumns（情報）</term><description>src/QuickER.CodeGen.CSharp/CLAUDE.md「主キーの列の NULL 許容」：NULL 許容の印のある主キーの列を NOT NULL とみなして名指し</description></item>
///   <item><term>C SyncSupportTables / SyncSupportUnboundedBinaryColumns（情報）</term><description>docs「Bidirectional sync support」・CSharpCodeGenerationService の同期の Info</description></item>
/// </list>
/// <para>
/// 型の食い違い（<c>CodeGen_Error_TypeMismatch</c>）・フォールバック型・配列・負のスケールは、図の形が出さない
/// （<see cref="ShapeDiagram"/> の表記の選び方）。出たら「どの規則にも当たらない」か「期待していない」で赤になる。
/// </para>
/// </remarks>
internal static class DiagnosticRules
{
    /// <summary>図の形とオプションから、期待する診断を完全一致の期待として作る</summary>
    /// <param name="shape">図の形</param>
    /// <param name="options">実効の生成オプション</param>
    public static DiagnosticExpectation For(ShapeDiagram shape, CodeGenerationOptions options)
    {
        var quicker = options.GenerateRepositories;
        var efCore = options.GenerateEfCoreRepositories;
        var contract = CodegenOptionFacts.GeneratesRepositoryContract(options);
        var dialects = CodegenOptionFacts.Dialects(options);
        var compositeTables = shape.Tables.Where(t => t.PrimaryKey.Count > 1).ToList();
        var diagnostics = new List<ExpectedDiagnostic>();

        // ===== A 段 =====
        if (options.GenerateMappers && !options.GenerateEditModels)
        {
            diagnostics.Add(
                ExpectedDiagnostic.Error(
                    nameof(Strings.CodeGen_Error_MapperRequiresEntityAndEditModel)
                )
            );
        }

        if (contract && !options.IncludeDataAnnotations)
        {
            diagnostics.Add(
                ExpectedDiagnostic.Error(
                    nameof(Strings.CodeGen_Error_RepositoryRequiresDataAnnotations)
                )
            );
        }

        if (CodegenOptionFacts.GeneratesRemote(options) && !contract)
        {
            diagnostics.Add(
                ExpectedDiagnostic.Error(nameof(Strings.CodeGen_Error_RemoteRequiresContract))
            );
        }

        diagnostics.AddRange(
            compositeTables.Select(table =>
                ExpectedDiagnostic.Warning(nameof(Strings.CodeGen_Warning_CompositeKey), table.Name)
            )
        );

        if (quicker && efCore && dialects.Count >= 2)
        {
            diagnostics.Add(
                ExpectedDiagnostic.Error(nameof(Strings.CodeGen_Error_MultiTargetEfCoreExclusive))
            );
        }

        if (shape.RowVersionIsUnifiedAcrossDialects(options))
        {
            diagnostics.Add(
                ExpectedDiagnostic.Info(nameof(Strings.CodeGen_Info_MultiTargetRowVersionColumns))
            );
        }

        if (options.GenerateSyncSupport)
        {
            if (!quicker)
            {
                diagnostics.Add(
                    ExpectedDiagnostic.Error(
                        nameof(Strings.CodeGen_Error_SyncSupportRequiresRepositories)
                    )
                );
            }
            else if (
                dialects.Count != 2
                || !dialects.Contains(DialectNames.SqlServer)
                || !dialects.Contains(DialectNames.Sqlite)
            )
            {
                diagnostics.Add(
                    ExpectedDiagnostic.Error(
                        nameof(Strings.CodeGen_Error_SyncSupportRequiresDialects),
                        string.Join(", ", dialects)
                    )
                );
            }

            if (!shape.Tables.Any(table => table.HasSinglePrimaryKey))
            {
                diagnostics.Add(
                    ExpectedDiagnostic.Error(
                        nameof(Strings.CodeGen_Error_SyncSupportRequiresEligibleTables)
                    )
                );
            }
        }

        if (HasError(diagnostics))
        {
            return DiagnosticExpectation.Exact(diagnostics);
        }

        // ===== B 段 =====
        diagnostics.AddRange(
            shape
                .Relations.Where(relation => relation.IsComposite)
                .Select(relation =>
                    ExpectedDiagnostic.Warning(
                        nameof(Strings.CodeGen_Warning_RelationshipCompositeSkipped),
                        relation.Child.Name
                    )
                )
        );

        if (contract)
        {
            diagnostics.AddRange(
                shape
                    .Tables.Where(table => !table.HasSinglePrimaryKey)
                    .Select(table =>
                        ExpectedDiagnostic.Warning(
                            nameof(Strings.CodeGen_Warning_RepositorySingleKeyOnly),
                            table.Name
                        )
                    )
            );

            // 名前付きクエリは子の表（orders）の Repository に付くので、その表に Repository が無ければクエリも名指しする
            if (shape.QueryName is { } queryName && !shape.Orders.HasSinglePrimaryKey)
            {
                diagnostics.Add(
                    ExpectedDiagnostic.Warning(
                        nameof(Strings.CodeGen_Query_SkippedWithoutRepository),
                        queryName,
                        shape.Orders.Name
                    )
                );
            }
        }

        if (efCore)
        {
            diagnostics.AddRange(
                shape
                    .Tables.Where(table => table.PrimaryKey.Count == 0)
                    .Select(table =>
                        ExpectedDiagnostic.Warning(
                            nameof(Strings.CodeGen_Warning_EfCoreKeylessTableExcluded),
                            table.Name
                        )
                    )
            );
        }

        var foreignKeyNameDiffers = shape
            .Relations.SelectMany(relation => relation.Pairs)
            .Any(pair => pair.Parent.Name != pair.Child.Name);

        if (options.GenerateValueObjects && foreignKeyNameDiffers)
        {
            diagnostics.Add(
                ExpectedDiagnostic.Info(nameof(Strings.CodeGen_Info_ValueObjectForeignKeyUnified))
            );
        }

        var selfReferenceWithContract = shape.Relations.Any(relation =>
            relation.Parent == relation.Child
            && !relation.IsComposite
            && relation.Parent.HasSinglePrimaryKey
        );

        if (contract && selfReferenceWithContract)
        {
            diagnostics.Add(
                ExpectedDiagnostic.Info(nameof(Strings.CodeGen_Info_IncludeGraphSkippedNavigations))
            );
        }

        // ===== C 段 =====
        if (options.ExcludeUnboundedBinaryColumns && shape.UnboundedBinaryColumns.Any())
        {
            diagnostics.Add(
                ExpectedDiagnostic.Info(nameof(Strings.CodeGen_Info_ExcludedUnboundedBinaryColumns))
            );
        }

        if (shape.ComputedColumn is { } computed)
        {
            diagnostics.Add(ExpectedDiagnostic.Info(nameof(Strings.CodeGen_Info_ComputedColumns)));

            var writesToMirror = options.GenerateSyncSupport || (quicker && dialects.Count >= 2);

            if (writesToMirror && !computed.Column.IsNullable)
            {
                diagnostics.Add(
                    ExpectedDiagnostic.Warning(
                        nameof(Strings.CodeGen_Warning_NotNullComputedColumns),
                        $"{shape.Orders.Name}.{computed.Name}"
                    )
                );
            }
        }

        if (shape.IdentityColumn is { } identity)
        {
            diagnostics.Add(ExpectedDiagnostic.Info(nameof(Strings.CodeGen_Info_IdentityColumns)));

            if (
                quicker
                && dialects.Any(dialect => !CanHoldIdentityColumn(dialect))
                && !identity.Column.IsNullable
            )
            {
                diagnostics.Add(
                    ExpectedDiagnostic.Warning(
                        nameof(Strings.CodeGen_Warning_NotNullIdentityColumns),
                        $"{shape.Orders.Name}.{identity.Name}"
                    )
                );
            }
        }

        var nullableKeys = shape
            .Tables.SelectMany(table =>
                table
                    .PrimaryKey.Where(column => column.Column.IsNullable)
                    .Select(column => $"{table.Name}.{column.Name} ({column.Spelling.Text})")
            )
            .ToList();

        if (nullableKeys.Count > 0)
        {
            diagnostics.Add(
                new ExpectedDiagnostic(
                    GenerationDiagnosticSeverity.Info,
                    nameof(Strings.CodeGen_Info_NullablePrimaryKeyColumns),
                    nullableKeys
                )
            );
        }

        if (options.IncludeDataAnnotations && shape.HasNonCanonicalSpelling(options))
        {
            diagnostics.Add(
                ExpectedDiagnostic.Info(
                    nameof(Strings.CodeGen_Info_NonCanonicalTypeSpellingColumns)
                )
            );
        }

        if (options.GenerateSyncSupport)
        {
            diagnostics.Add(
                ExpectedDiagnostic.Info(nameof(Strings.CodeGen_Info_SyncSupportTables))
            );

            var syncTablesHaveUnboundedBinary = shape
                .Tables.Where(table => table.HasSinglePrimaryKey)
                .SelectMany(table => table.Columns)
                .Any(column => column.Spelling.IsUnboundedBinary);

            if (options.ExcludeUnboundedBinaryColumns && syncTablesHaveUnboundedBinary)
            {
                diagnostics.Add(
                    ExpectedDiagnostic.Info(
                        nameof(Strings.CodeGen_Info_SyncSupportUnboundedBinaryColumns)
                    )
                );
            }
        }

        return DiagnosticExpectation.Exact(diagnostics);
    }

    /// <summary>
    /// 方言が「指定した値を拒む採番列」を持てるか（CLAUDE.md「DB が値を拒む採番列」：持てないのは MySQL・SQLite。
    /// QuickER 版 Repository の方言は SQL Server と SQLite だけ）
    /// </summary>
    private static bool CanHoldIdentityColumn(string dialect) =>
        string.Equals(dialect, DialectNames.SqlServer, StringComparison.OrdinalIgnoreCase);

    private static bool HasError(IEnumerable<ExpectedDiagnostic> diagnostics) =>
        diagnostics.Any(d => d.Severity == GenerationDiagnosticSeverity.Error);
}
