using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Model;
using QuickER.Services;
using QuickER.ViewModels;
using Xunit;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// <see cref="AutoLayoutService.LayoutGrid(IList{EntityViewModel}, IList{RelationshipViewModel}, int)"/>
/// の交差最小化について、絞り込みが結果を変えないことと、大きな図で判定回数の上限に達して打ち切られることを検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 交差最小化は「パス × 全ペア × 影響辺 × 全辺」で、テーブル数に対しておよそ 3 乗で伸びる。
/// 高速化は 2 段で、(1) 入り得ない相手を判定の前に外す絞り込み（結果は変えない）と
/// (2) 判定回数の上限で打ち切る予算（大きな図でのみ結果が変わる）からなる。
/// </para>
/// <para>
/// (1) は結果を変えないため、<b>取り除いても遅くなるだけでテストは赤にならない</b>。
/// ここで固定するのは「絞り込みが結果を変えていないこと」＝ダイジェストが絞り込み導入前の実装と
/// 一致することで、(1) を壊す変更（入り得る相手まで外してしまう等）はこのテストが捕まえる。
/// </para>
/// <para>
/// 期待値のダイジェストは配置（テーブル名と座標）から作る。読んで意味の分かる値ではないが、
/// 「同じ入力なら同じ配置になる」ことと「その配置が変わっていないこと」を 1 つの値で固定できる。
/// 整列の意味そのもの（交差が解消される・格子に並ぶ等）は <see cref="AutoLayoutServiceTests"/> が持つ。
/// </para>
/// </remarks>
public class AutoLayoutOptimizationTests
{
    /// <summary>決定的な疑似乱数（xorshift64）でテーブルとリレーションを組み立てる</summary>
    /// <remarks>
    /// 乱数を使うのは「現実的な密度の結線」を作るためで、シードを固定しているので図は毎回同一。
    /// リレーションは自己参照と重複ペアを除いて指定本数ちょうど作る。
    /// </remarks>
    private static (
        List<EntityViewModel> Entities,
        List<RelationshipViewModel> Relationships
    ) BuildDiagram(int tableCount, int relationshipCount, ulong seed)
    {
        var state = seed;

        ulong Next()
        {
            state ^= state << 13;
            state ^= state >> 7;
            state ^= state << 17;

            return state;
        }

        var entities = new List<EntityViewModel>();

        for (var i = 0; i < tableCount; i++)
        {
            entities.Add(
                new EntityViewModel(
                    new Entity
                    {
                        TableName = $"T{i:D4}",
                        Columns =
                        {
                            new Column { Name = "id", DataType = "int" },
                            new Column { Name = "name", DataType = "varchar(50)" },
                        },
                    },
                    new EntityLayout { X = -999, Y = -999 }
                )
            );
        }

        var relationships = new List<RelationshipViewModel>();
        var used = new HashSet<(int, int)>();

        while (relationships.Count < relationshipCount)
        {
            var a = (int)(Next() % (ulong)tableCount);
            var b = (int)(Next() % (ulong)tableCount);

            if (a == b || !used.Add((Math.Min(a, b), Math.Max(a, b))))
            {
                continue;
            }

            relationships.Add(
                new RelationshipViewModel(
                    new Relationship
                    {
                        SourceEntityId = entities[a].Id,
                        TargetEntityId = entities[b].Id,
                    },
                    entities[a],
                    entities[b]
                )
            );
        }

        return (entities, relationships);
    }

    /// <summary>配置（テーブル名と座標）のダイジェストを求める</summary>
    private static string LayoutDigest(IEnumerable<EntityViewModel> entities)
    {
        var text = new StringBuilder();

        foreach (var entity in entities)
        {
            text.Append(entity.TableName)
                .Append(':')
                .Append(entity.X)
                .Append(',')
                .Append(entity.Y)
                .Append(';');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..16];
    }

    /// <summary>指定の図を整列し、配置のダイジェストを返す</summary>
    private static string LayoutAndDigest(int tableCount, int relationshipCount, ulong seed)
    {
        var (entities, relationships) = BuildDiagram(tableCount, relationshipCount, seed);
        AutoLayoutService.LayoutGrid(entities, relationships);

        return LayoutDigest(entities);
    }

    /// <summary>絞り込みを入れても配置が変わらないことを検証する</summary>
    /// <remarks>
    /// 期待値は絞り込みを入れる前の実装（全セル・全辺を走査していた頃）で採取した。
    /// この 2 水準は判定回数の上限に達しないため、予算の影響も受けない。
    /// </remarks>
    [Theory(DisplayName = "LayoutGrid: 絞り込みを入れても配置は変わらない")]
    [InlineData(50, 50, 2UL, "EAB106AA9E8860CA")]
    [InlineData(100, 100, 3UL, "623E8DC3E011BD48")]
    public void LayoutGrid_WithinBudget_LayoutIsUnchangedByPruning(
        int tableCount,
        int relationshipCount,
        ulong seed,
        string expectedDigest
    )
    {
        LayoutAndDigest(tableCount, relationshipCount, seed * 88172645463325252UL)
            .Should()
            .Be(expectedDigest, "入り得ない相手を外すだけの絞り込みは集計結果を変えない");
    }

    /// <summary>大きな図では判定回数の上限で打ち切られ、しかも結果が決定的であることを検証する</summary>
    /// <remarks>
    /// <para>
    /// 400 テーブル・400 リレーションは上限（判定 2.5 億回）に達する水準で、上限が無かった頃とは
    /// 配置が変わる（その代わり整列が数秒で返る＝実測で 10.2 秒が 1.4 秒）。期待値のダイジェストは
    /// 上限つきの実装で採取した値で、<b>上限を外すと配置が最後まで最適化されて別の値になる</b>
    /// （＝上限が効いていることの固定）。
    /// </para>
    /// <para>
    /// 2 回流して一致を見るのは、打ち切りが<b>時間ではなく判定回数</b>で決まることの固定。
    /// 時間で打ち切ると、速い機械と遅い機械・同じ機械の実行ごとで配置が変わる（＝再現しない図になる）。
    /// </para>
    /// </remarks>
    [Fact(DisplayName = "LayoutGrid: 大きな図は判定回数の上限で打ち切られ、結果は決定的")]
    public void LayoutGrid_LargeDiagram_StopsAtCheckBudgetDeterministically()
    {
        var first = LayoutAndDigest(400, 400, 6UL * 88172645463325252UL);
        var second = LayoutAndDigest(400, 400, 6UL * 88172645463325252UL);

        first.Should().Be("F109C7C41C4DB448", "上限に達したらそこまでの改善を採って打ち切る");
        second.Should().Be(first, "打ち切りは判定回数で決まるため、実行ごとに結果が変わらない");
    }
}
