using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 楽観排他ポリシーの中立表現（基底が派生へ「どのモードで呼ぶか」を伝えるための語彙）。
/// </summary>
/// <remarks>
/// 生成される <c>ConcurrencyMode</c> はフィクスチャごとの namespace に別々の型として出るため、共通基底からは名指しできない。
/// 派生のアダプタがこの中立値を自分のフィクスチャの enum へ翻訳する（<see cref="Undefined"/> は
/// <c>(ConcurrencyMode)99</c> ＝列挙に無い値へ翻訳し、入口の fail-fast を突く）。
/// </remarks>
public enum ConcurrencyChoice
{
    /// <summary>版ガードあり（既定）</summary>
    Optimistic,

    /// <summary>版ガードなし（明示的な last-write-wins）</summary>
    ForceOverwrite,

    /// <summary>列挙に無い値（入口で <see cref="ArgumentOutOfRangeException"/> になるべき値）</summary>
    Undefined,
}

/// <summary>
/// rowversion 列を持つテーブルの楽観排他（<c>ConcurrencyMode</c>）を、実装先（QuickER 版 Repository の SQL Server・
/// EF Core・インメモリ・HTTP リモート）と VO の有無を跨いでパリティ検証する共通基底。
/// </summary>
/// <remarks>
/// <para>
/// 「rowversion 列があれば全バックエンドで版チェックが効く」という確定仕様のうち、<b>バックエンドに依らない</b>
/// シナリオを <c>[Fact]</c> として持つ。各派生はリポジトリ生成・シード・「他者による更新」の作り方だけを差し込む。
/// </para>
/// <list type="bullet">
///   <item>保存成功後は新しい版が<b>同一インスタンス</b>へ反映され、再取得なしで続けて保存できる</item>
///   <item>UPDATE / DELETE は版一致を条件に成立し、他者が先に更新していれば競合例外</item>
///   <item><c>ForceOverwrite</c> は版条件を外す（明示的な last-write-wins）</item>
///   <item>単一 <c>UpdateAsync</c> の「行なし」は競合ではなく従来契約の <c>false</c></item>
///   <item><c>insertWhenUpdateMissing</c> は「行なし→INSERT／版が古い→競合」を区別する（倒すと主キー重複に化ける）</item>
///   <item>列挙に無い <c>ConcurrencyMode</c> は入口で <see cref="ArgumentOutOfRangeException"/></item>
/// </list>
/// <para>
/// <b>型パラメータで橋を架ける理由</b>: 生成物はフィクスチャごとに別 namespace へ出るため、<c>SaveConflictException</c> も
/// <c>ConcurrencyMode</c> も <c>DocumentEntity</c> / <c>GadgetEntity</c> も共通基底からは名指しできない。エンティティ型と
/// 競合例外型だけを型引数で受け、値の読み書きは派生のアダプタ（<see cref="GetTitle"/> 等）へ委ねることで、
/// VO 有効の図（版が <c>RowVerValue</c>）と無効の図（版が生の <c>byte[]</c>）が同じシナリオを共有できる。
/// </para>
/// <para>
/// <b>シードの形</b>: ルート <see cref="SeededRootId"/>（子 <see cref="SeededChildId"/> を 1 件持つ）と、
/// 子を持たないルート <see cref="SeededChildlessRootId"/>。削除の版ガード（テスト 7）は子を持たないルートで行う
/// （カスケード削除の有無が実装先で異なるため、削除そのものの版条件だけを見る）。
/// </para>
/// </remarks>
/// <typeparam name="TEntity">ルートエンティティ型（フィクスチャごとに別型）</typeparam>
/// <typeparam name="TConflictException">そのフィクスチャの <c>SaveConflictException</c> 型</typeparam>
[Trait("Category", "Integration")]
public abstract class ConcurrencyRuntimeTestsBase<TEntity, TConflictException>
    where TEntity : class
    where TConflictException : Exception
{
    /// <summary>テスト全体で使うキャンセルトークン</summary>
    protected static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>シードされる「子を 1 件持つ」ルートの ID</summary>
    protected const int SeededRootId = 1;

    /// <summary>シードされる「子を持たない」ルートの ID（削除の版ガード検証用）</summary>
    protected const int SeededChildlessRootId = 2;

    /// <summary>シードされる子の ID</summary>
    protected const int SeededChildId = 100;

    /// <summary>競合として報告されたことを示すメッセージ断片（全実装先で共通の文言）</summary>
    protected const string ConflictMessage = "*modified by another user*";

    // ── 派生が差し込むアダプタ ──

    /// <summary>スキーマ（または空ストア）を用意し、共通のシードを投入する</summary>
    /// <remarks>
    /// ルート <see cref="SeededRootId"/>="alpha"（子 <see cref="SeededChildId"/>="first" つき）と、
    /// 子を持たないルート <see cref="SeededChildlessRootId"/>="beta"。
    /// </remarks>
    protected abstract Task ResetAndSeedAsync();

    /// <summary>ルートエンティティを組み立てる（除外列などは未取得状態＝更新可能な形にしておく）</summary>
    protected abstract TEntity NewEntity(int id, string title);

    /// <summary>ルートを 1 件挿入する（挿入後も同一インスタンスをそのまま更新できる状態にして返す）</summary>
    protected abstract Task InsertAsync(TEntity entity);

    /// <summary>主キーでルートを取得する（行なしは null）</summary>
    protected abstract Task<TEntity?> GetAsync(int id);

    /// <summary>子を伴うルートを取得する（グラフ保存の検証用）</summary>
    protected abstract Task<TEntity?> GetWithChildrenAsync(int id);

    /// <summary>ルートのタイトル相当（VO 有効の図では VO を開いた素の文字列）を読む</summary>
    protected abstract string GetTitle(TEntity entity);

    /// <summary>ルートのタイトル相当を書く（VO 有効の図では VO へ包む）</summary>
    protected abstract void SetTitle(TEntity entity, string title);

    /// <summary>ルートの版を素の <c>byte[]</c> として読む（VO 有効の図では VO を開く）</summary>
    protected abstract byte[]? GetRowVersion(TEntity entity);

    /// <summary>ルートの版を素の <c>byte[]</c> で書く（VO 有効の図では VO へ包む）</summary>
    /// <remarks>版が NOT NULL の図では VO プロパティも非 NULL 許容で生成されるため、null は渡さない契約にする。</remarks>
    protected abstract void SetRowVersion(TEntity entity, byte[] rowVersion);

    /// <summary>ルートを新規（Added）としてマークする</summary>
    protected abstract void MarkAdded(TEntity entity);

    /// <summary>ルートを更新（Updated）としてマークする</summary>
    protected abstract void MarkUpdated(TEntity entity);

    /// <summary>ルートを削除（Removed）としてマークする</summary>
    protected abstract void MarkRemoved(TEntity entity);

    /// <summary>単一更新を実行する</summary>
    protected abstract Task<bool> UpdateAsync(
        TEntity entity,
        ConcurrencyChoice mode = ConcurrencyChoice.Optimistic
    );

    /// <summary>グラフ保存を実行する</summary>
    protected abstract Task<int> SaveAsync(
        TEntity entity,
        ConcurrencyChoice mode = ConcurrencyChoice.Optimistic,
        bool insertWhenUpdateMissing = false
    );

    /// <summary>
    /// 手元のインスタンスを経由せずに 1 行更新して「他者による更新」を作る
    /// （手元のエンティティの版は古いまま残り、実際の競合と同じ状態を決定的に再現できる）。
    /// </summary>
    protected abstract Task BumpByAnotherUserAsync(int id, string title);

    /// <summary>取得済みルートの先頭の子を編集して更新マークを付ける（グラフ保存の巻き戻し検証用）</summary>
    protected abstract void EditFirstChild(TEntity root, string note);

    /// <summary>子の現在値を読む（行なしは null）</summary>
    protected abstract Task<string?> ReadChildNoteAsync(int noteId);

    /// <summary>保存先の現在のタイトルを読む（先勝ち／上書きの確認用・行なしは null）</summary>
    protected async Task<string?> ReadTitleAsync(int id)
    {
        var entity = await GetAsync(id);

        return entity is null ? null : GetTitle(entity);
    }

    // ── 1. 版の反映 ──

    /// <summary>1. Insert / Update / Save の成功後に同一インスタンスの版が進み、再取得なしで次の保存に使える</summary>
    [Fact(
        DisplayName = "[Concurrency] 1: 版反映: Insert / Update / Save 後に同一インスタンスの版が進む"
    )]
    public async Task RowVersion_IsWrittenBack_ToTheSameInstance()
    {
        await ResetAndSeedAsync();

        var entity = NewEntity(5, "inserted");
        await InsertAsync(entity);

        GetRowVersion(entity).Should().NotBeNull("INSERT 直後に採番された版が入る");
        GetRowVersion(entity)!
            .Length.Should()
            .Be(8, "SQL Server の rowversion（およびインメモリの擬似版）は 8 バイト");
        var afterInsert = GetRowVersion(entity);

        SetTitle(entity, "updated");
        (await UpdateAsync(entity)).Should().BeTrue();
        GetRowVersion(entity).Should().NotEqual(afterInsert, "UPDATE のたびに版が進む");
        var afterUpdate = GetRowVersion(entity);

        SetTitle(entity, "saved");
        MarkUpdated(entity);
        (await SaveAsync(entity)).Should().Be(1);
        GetRowVersion(entity).Should().NotEqual(afterUpdate, "グラフ保存でも新版が反映される");

        // 反映された版がそのまま次の更新に使える（再読込せずに更新が通る＝書き戻しが正しい証明）
        SetTitle(entity, "again");
        (await UpdateAsync(entity))
            .Should()
            .BeTrue("反映された版は保存先の現在値と一致するので再読込なしで更新できる");
    }

    // ── 2〜4. 単一 UpdateAsync ──

    /// <summary>2. 他者が先に更新した行を古いインスタンスで更新すると競合になり、保存先は先勝ちのまま</summary>
    [Fact(DisplayName = "[Concurrency] 2: UpdateAsync は版が古いと競合・保存先は先勝ちのまま")]
    public async Task UpdateAsync_Throws_WhenRowVersionIsStale()
    {
        await ResetAndSeedAsync();

        var stale = await GetAsync(SeededRootId);
        stale.Should().NotBeNull();
        GetRowVersion(stale!).Should().NotBeNull("取得時点で版が読める");

        await BumpByAnotherUserAsync(SeededRootId, "by-another-user");

        SetTitle(stale!, "by-me");
        var act = async () => await UpdateAsync(stale!);

        await act.Should()
            .ThrowAsync<TConflictException>()
            .WithMessage(ConflictMessage, "版が一致せず競合として弾かれる");

        (await ReadTitleAsync(SeededRootId))
            .Should()
            .Be("by-another-user", "競合した更新は適用されない（先勝ち）");
    }

    /// <summary>3. ForceOverwrite は版条件を外して上書きし、新しい版を反映する</summary>
    [Fact(
        DisplayName = "[Concurrency] 3: UpdateAsync の ForceOverwrite は版を無視して上書きし新版を反映する"
    )]
    public async Task UpdateAsync_ForceOverwrite_OverwritesAndRefreshesRowVersion()
    {
        await ResetAndSeedAsync();

        var stale = await GetAsync(SeededRootId);
        stale.Should().NotBeNull();
        var staleVersion = GetRowVersion(stale!);

        await BumpByAnotherUserAsync(SeededRootId, "by-another-user");

        SetTitle(stale!, "forced");
        (await UpdateAsync(stale!, ConcurrencyChoice.ForceOverwrite))
            .Should()
            .BeTrue("版条件を外すので更新は成立する");

        (await ReadTitleAsync(SeededRootId))
            .Should()
            .Be("forced", "last-write-wins で上書きされる");
        GetRowVersion(stale!).Should().NotEqual(staleVersion, "上書き後の新しい版が反映される");
    }

    /// <summary>4. 存在しない行の更新は false（競合ではない）</summary>
    [Fact(DisplayName = "[Concurrency] 4: UpdateAsync は行が存在しなければ false")]
    public async Task UpdateAsync_ReturnsFalse_WhenRowIsMissing()
    {
        await ResetAndSeedAsync();

        var missing = NewEntity(999, "ghost");
        SetRowVersion(missing, [0, 0, 0, 0, 0, 0, 0, 1]);

        (await UpdateAsync(missing))
            .Should()
            .BeFalse("行なしは競合ではなく「更新対象なし」＝従来契約の false");
    }

    // ── 5〜8. グラフ保存 ──

    /// <summary>5. 競合ノードを含むグラフ保存は競合例外で全ロールバックする（競合していない子の更新も戻る）</summary>
    [Fact(DisplayName = "[Concurrency] 5: SaveAsync は競合ノードがあると全ロールバックする")]
    public async Task SaveAsync_RollsBackEverything_OnConflict()
    {
        await ResetAndSeedAsync();

        var root = await GetWithChildrenAsync(SeededRootId);
        root.Should().NotBeNull();

        await BumpByAnotherUserAsync(SeededRootId, "by-another-user");

        // 子は競合していないが、親が競合するため全体が巻き戻る
        SetTitle(root!, "by-me");
        MarkUpdated(root!);
        EditFirstChild(root!, "note-by-me");

        var act = async () => await SaveAsync(root!);

        await act.Should().ThrowAsync<TConflictException>();

        (await ReadTitleAsync(SeededRootId)).Should().Be("by-another-user", "親は先勝ちのまま");
        (await ReadChildNoteAsync(SeededChildId))
            .Should()
            .Be("first", "競合していない子の更新もロールバックされる（all-or-nothing）");
    }

    /// <summary>6. ForceOverwrite のグラフ保存は版を無視して成立する</summary>
    [Fact(DisplayName = "[Concurrency] 6: SaveAsync の ForceOverwrite は版を無視して保存する")]
    public async Task SaveAsync_ForceOverwrite_Succeeds()
    {
        await ResetAndSeedAsync();

        var stale = await GetAsync(SeededRootId);
        stale.Should().NotBeNull();

        await BumpByAnotherUserAsync(SeededRootId, "by-another-user");

        SetTitle(stale!, "forced");
        MarkUpdated(stale!);

        (await SaveAsync(stale!, ConcurrencyChoice.ForceOverwrite)).Should().Be(1);
        (await ReadTitleAsync(SeededRootId)).Should().Be("forced");
    }

    /// <summary>7. 削除も版条件で守られ、ForceOverwrite なら通る</summary>
    [Fact(
        DisplayName = "[Concurrency] 7: SaveAsync の削除も版条件で守られ ForceOverwrite なら通る"
    )]
    public async Task SaveAsync_Delete_IsGuardedByRowVersion()
    {
        await ResetAndSeedAsync();

        // 子を持たないルートで行う（カスケード削除の有無は実装先で異なるため、削除そのものの版条件だけを見る）
        var stale = await GetAsync(SeededChildlessRootId);
        stale.Should().NotBeNull();

        await BumpByAnotherUserAsync(SeededChildlessRootId, "by-another-user");

        MarkRemoved(stale!);
        var act = async () => await SaveAsync(stale!);

        await act.Should()
            .ThrowAsync<TConflictException>()
            .WithMessage(ConflictMessage, "版の古い行は削除できない");
        (await GetAsync(SeededChildlessRootId)).Should().NotBeNull("削除はロールバックされる");

        (await SaveAsync(stale!, ConcurrencyChoice.ForceOverwrite)).Should().BeGreaterThan(0);
        (await GetAsync(SeededChildlessRootId)).Should().BeNull("版条件を外せば削除される");
    }

    /// <summary>8. insertWhenUpdateMissing は「行なし→INSERT」「版が古い→競合」を区別する</summary>
    /// <remarks>版違いを INSERT へ倒すと主キー重複に化けるため、区別は回帰テストとして重要。</remarks>
    [Fact(
        DisplayName = "[Concurrency] 8: SaveAsync の insertWhenUpdateMissing は行なしと版違いを区別する"
    )]
    public async Task SaveAsync_InsertWhenUpdateMissing_DistinguishesMissingRowFromStaleVersion()
    {
        await ResetAndSeedAsync();

        // 行なし: INSERT へ切り替わる
        var missing = NewEntity(3, "switched-to-insert");
        MarkUpdated(missing);

        (await SaveAsync(missing, insertWhenUpdateMissing: true))
            .Should()
            .Be(1, "行が無いので INSERT へ切り替わる");
        (await GetAsync(3)).Should().NotBeNull();
        GetRowVersion(missing).Should().NotBeNull("切り替わった INSERT でも版が採番される");

        // 版が古い: INSERT へ倒さず競合として弾く（倒すと主キー重複になる）
        var stale = await GetAsync(SeededRootId);
        await BumpByAnotherUserAsync(SeededRootId, "by-another-user");
        SetTitle(stale!, "by-me");
        MarkUpdated(stale!);

        var act = async () => await SaveAsync(stale!, insertWhenUpdateMissing: true);

        await act.Should()
            .ThrowAsync<TConflictException>()
            .WithMessage(
                ConflictMessage,
                "版違いは INSERT へ倒さず競合として報告する（主キー重複に化けない）"
            );
    }

    // ── 9. 未定義の ConcurrencyMode ──

    /// <summary>
    /// 9. 列挙に無い値は入口で <see cref="ArgumentOutOfRangeException"/>（内部の 2 値分岐は「Optimistic なら版ガード・
    /// さもなくば強制」のため、検証しないと未定義値が黙って last-write-wins へ落ちて版チェックが無効化される）。
    /// </summary>
    [Fact(DisplayName = "[Concurrency] 9: 未定義の ConcurrencyMode は ArgumentOutOfRangeException")]
    public async Task UndefinedConcurrencyMode_IsRejected()
    {
        await ResetAndSeedAsync();

        var stale = await GetAsync(SeededRootId);
        await BumpByAnotherUserAsync(SeededRootId, "by-another-user");

        // 版は古いまま＝未定義値が強制上書きへ落ちれば「更新が通ってしまう」
        SetTitle(stale!, "by-undefined");
        var update = async () => await UpdateAsync(stale!, ConcurrencyChoice.Undefined);

        await update.Should().ThrowAsync<ArgumentOutOfRangeException>().WithParameterName("mode");

        MarkUpdated(stale!);
        var save = async () => await SaveAsync(stale!, ConcurrencyChoice.Undefined);

        await save.Should().ThrowAsync<ArgumentOutOfRangeException>().WithParameterName("mode");

        (await ReadTitleAsync(SeededRootId))
            .Should()
            .Be("by-another-user", "未定義値の保存は 1 件も適用されない");
    }
}
