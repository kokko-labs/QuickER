using System.Collections.Generic;
using System.Linq;
using QuickER.Model;
using QuickER.ViewModels;

namespace QuickER.UndoRedo;

/// <summary>
/// 列ペアの子列になる列の外部キーの印（<see cref="ColumnViewModel.IsForeignKey"/> と
/// <see cref="ColumnViewModel.IsForeignKeyManagedByRelationship"/>）を控え、Undo で控えた値へ戻す
/// （リレーションの追加・列ペアの変更のコマンドが共有する）
/// </summary>
/// <remarks>
/// <para>
/// 列がリレーションの子列になると、リレーションの列のルール（<c>MainViewModel.ApplyRelationshipColumnRules</c>）が
/// その列の外部キーの印をリレーションの管理に移す。リレーションから外れると管理下の印は落ちるので、
/// 子列にする前に手で付けた印は、子列にする操作を Undo しただけで消えていた（保存内容が元と一致しない）。
/// </para>
/// <para>
/// 控えるのは子列にする操作を実行した時点の値で、Undo で列の構成を戻した後にその値へ戻す。
/// リレーションを削除したときに管理下の印を落とす意味は変えない（MCP のホストの規則と同じ）。
/// Redo は操作をもう一度実行するので、印はルールの再適用が決める（控えた値は使わない）。
/// </para>
/// </remarks>
internal sealed class ForeignKeyMarkSnapshot
{
    /// <summary>控えた列と、その時点の印</summary>
    private readonly List<(ColumnViewModel Column, bool IsForeignKey, bool IsManaged)> _marks;

    /// <summary><see cref="ForeignKeyMarkSnapshot"/> を生成する</summary>
    private ForeignKeyMarkSnapshot(
        List<(ColumnViewModel Column, bool IsForeignKey, bool IsManaged)> marks
    )
    {
        _marks = marks;
    }

    /// <summary>列ペアの子列（<paramref name="target"/> の列）の、いまの印を控える</summary>
    /// <param name="target">列ペアの子列を持つエンティティ</param>
    /// <param name="pairs">これから子列にする列ペア</param>
    public static ForeignKeyMarkSnapshot Capture(
        EntityViewModel target,
        IEnumerable<RelationshipColumnPair> pairs
    )
    {
        var marks = pairs
            .Select(pair =>
                target.Columns.FirstOrDefault(column => column.Id == pair.TargetColumnId)
            )
            .OfType<ColumnViewModel>()
            .Distinct()
            .Select(column =>
                (column, column.IsForeignKey, column.IsForeignKeyManagedByRelationship)
            )
            .ToList();

        return new ForeignKeyMarkSnapshot(marks);
    }

    /// <summary>控えた印へ戻す</summary>
    public void Restore()
    {
        foreach (var (column, isForeignKey, isManaged) in _marks)
        {
            column.IsForeignKeyManagedByRelationship = isManaged;
            column.IsForeignKey = isForeignKey;
        }
    }
}
