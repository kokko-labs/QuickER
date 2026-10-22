using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using Xunit;

namespace QuickER.Tests.Provider;

/// <summary><see cref="SchemaDiffItem"/> の複製を検証するテストクラス</summary>
public class SchemaDiffItemTests
{
    /// <summary>
    /// <see cref="SchemaDiffItem.WithForeignKeyColumnPairs"/> が、列ペア以外の公開プロパティをすべて写すことを検証する。
    /// プロパティは反射で全数を列挙して既定と違う値を入れるので、プロパティを足したときに複製が値を落とせば赤になる
    /// （値を作れない型のプロパティを足しても赤になり、このテストの更新を求める）。
    /// </summary>
    [Fact(
        DisplayName = "複製: 列ペアだけを差し替えた複製は、ほかの公開プロパティと選択の状態をすべて写す"
    )]
    public void WithForeignKeyColumnPairs_CopiesEveryOtherProperty()
    {
        var item = new SchemaDiffItem();
        var properties = typeof(SchemaDiffItem)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .ToList();

        foreach (var property in properties)
        {
            property.SetValue(item, SampleValue(property));
        }

        var pairs = new List<ForeignKeyColumnNamePair> { new("p2", "c2"), new("p1", "c1") };
        var copy = item.WithForeignKeyColumnPairs(pairs);

        copy.Should().NotBeSameAs(item);
        copy.ForeignKeyColumnPairs.Should().BeSameAs(pairs);
        item.ForeignKeyColumnPairs.Should()
            .Equal(
                new ForeignKeyColumnNamePair("p1", "c1"),
                new ForeignKeyColumnNamePair("p2", "c2")
            );

        foreach (
            var property in properties.Where(p =>
                p.Name != nameof(SchemaDiffItem.ForeignKeyColumnPairs)
            )
        )
        {
            property.GetValue(copy).Should().Be(property.GetValue(item), property.Name);
        }

        copy.IsDestructive.Should().Be(item.IsDestructive);
    }

    /// <summary>複製は元の項目の変更通知の購読を引き継がないことを検証する</summary>
    [Fact(
        DisplayName = "複製: 列ペアだけを差し替えた複製は、元の項目の変更通知の購読を引き継がない"
    )]
    public void WithForeignKeyColumnPairs_DoesNotCopySubscriptions()
    {
        var item = new SchemaDiffItem { IsSelected = true };
        var raised = 0;
        item.PropertyChanged += (_, _) => raised++;

        var copy = item.WithForeignKeyColumnPairs([new("p", "c")]);
        copy.IsSelected = false;

        raised.Should().Be(0);
        item.IsSelected.Should().BeTrue();
    }

    /// <summary>プロパティの型ごとに、既定と違う値を作る</summary>
    private static object SampleValue(PropertyInfo property)
    {
        var type = property.PropertyType;

        if (type == typeof(string))
        {
            return "value-" + property.Name;
        }

        if (type == typeof(bool))
        {
            return !(bool)property.GetValue(new SchemaDiffItem())!;
        }

        if (type.IsEnum)
        {
            return Enum.GetValues(type).GetValue(1)!;
        }

        if (type == typeof(Entity))
        {
            return new Entity { TableName = property.Name };
        }

        if (type == typeof(Column))
        {
            return new Column { Name = property.Name, DataType = "int" };
        }

        if (type == typeof(Relationship))
        {
            return new Relationship { ConstraintName = property.Name };
        }

        if (type == typeof(SchemaDiffItem))
        {
            return new SchemaDiffItem { TableName = property.Name };
        }

        if (type == typeof(IReadOnlyList<string>))
        {
            return new List<string> { property.Name };
        }

        if (type == typeof(IReadOnlyList<ForeignKeyColumnNamePair>))
        {
            return new List<ForeignKeyColumnNamePair> { new("p1", "c1"), new("p2", "c2") };
        }

        throw new InvalidOperationException(
            $"SchemaDiffItem.{property.Name} の型 {type} の値の作り方がありません。SampleValue に足してください。"
        );
    }
}
