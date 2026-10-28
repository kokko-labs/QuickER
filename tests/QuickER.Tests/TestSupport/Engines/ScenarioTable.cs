using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace QuickER.Tests.TestSupport.Engines;

/// <summary>決定的なシナリオの 1 行</summary>
/// <typeparam name="TInput">エンジンへ渡す入力の型</typeparam>
/// <param name="Id">シナリオの Id（表の中で一意・序数で比べる）</param>
/// <param name="Finding">由来の指摘の番号（無ければ null）</param>
/// <param name="Description">何を確かめるシナリオか</param>
/// <param name="Input">エンジンへ渡す入力</param>
public sealed record ScenarioRow<TInput>(
    string Id,
    string? Finding,
    string Description,
    TInput Input
);

/// <summary>
/// エンジンの決定的なシナリオの表（Id で引き、Theory のデータにする）
/// </summary>
/// <typeparam name="TInput">エンジンへ渡す入力の型</typeparam>
/// <remarks>
/// 構築時に、空の Id・重複した Id（重複をすべて名指し）・空の説明を断る。並びは渡した順のまま。
/// </remarks>
public sealed class ScenarioTable<TInput> : IEnumerable<ScenarioRow<TInput>>
{
    private readonly IReadOnlyList<ScenarioRow<TInput>> _rows;
    private readonly Dictionary<string, ScenarioRow<TInput>> _byId;

    /// <summary>表を作る（整っていなければ問題をすべて並べて断る）</summary>
    /// <param name="engineId">エンジンの ID</param>
    /// <param name="rows">シナリオ</param>
    public ScenarioTable(string engineId, IEnumerable<ScenarioRow<TInput>> rows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(engineId);
        ArgumentNullException.ThrowIfNull(rows);

        var list = rows.ToList();
        var problems = new List<string>();

        for (var i = 0; i < list.Count; i++)
        {
            var row = list[i];

            if (row is null)
            {
                problems.Add($"{i + 1} 行目が null");
                continue;
            }

            if (string.IsNullOrWhiteSpace(row.Id))
            {
                problems.Add($"{i + 1} 行目の Id が空");
            }

            if (string.IsNullOrWhiteSpace(row.Description))
            {
                problems.Add($"{i + 1} 行目（{row.Id}）の説明が空");
            }
        }

        var duplicates = list.Where(row => row is not null && !string.IsNullOrWhiteSpace(row.Id))
            .GroupBy(row => row.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);

        foreach (var id in duplicates)
        {
            problems.Add($"Id {id} が重複している");
        }

        if (problems.Count > 0)
        {
            throw new ArgumentException(
                $"{engineId} のシナリオの表が整っていない:\n  " + string.Join("\n  ", problems),
                nameof(rows)
            );
        }

        EngineId = engineId;
        _rows = list;
        _byId = list.ToDictionary(row => row.Id, StringComparer.Ordinal);
    }

    /// <summary>エンジンの ID</summary>
    public string EngineId { get; }

    /// <summary>シナリオの数</summary>
    public int Count => _rows.Count;

    /// <summary>Id でシナリオを引く</summary>
    /// <param name="id">シナリオの Id</param>
    /// <exception cref="KeyNotFoundException">表に無い Id</exception>
    public ScenarioRow<TInput> this[string id]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(id);

            if (!_byId.TryGetValue(id, out var row))
            {
                throw new KeyNotFoundException($"{EngineId} のシナリオの表に Id {id} が無い");
            }

            return row;
        }
    }

    /// <summary>Id を表の並びで Theory のデータにする</summary>
    public TheoryData<string> Ids()
    {
        var data = new TheoryData<string>();

        foreach (var row in _rows)
        {
            data.Add(row.Id);
        }

        return data;
    }

    /// <inheritdoc/>
    public IEnumerator<ScenarioRow<TInput>> GetEnumerator() => _rows.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
