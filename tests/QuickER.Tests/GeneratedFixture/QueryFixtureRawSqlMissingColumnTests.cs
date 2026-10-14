using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace QuickER.Tests.GeneratedQueryFixture;

/// <summary>
/// 生 SQL のエンティティ取得で列が足りないとき、エラーが取得しようとしたエンティティの型を名指しすることを検証するテストクラス。
/// </summary>
/// <remarks>
/// 主キーを基底クラスで宣言した手書きのエンティティでは、主キーのプロパティを宣言した型は基底クラスになる。
/// エラーが名指しするのは、利用者が取得しようとした型（派生クラス）でなければならない。
/// </remarks>
public class QueryFixtureRawSqlMissingColumnTests
{
    /// <summary>主キーを宣言する手書きの基底クラス</summary>
    public abstract class KeyedThingBase : EntityBase
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }
    }

    /// <summary>主キーを基底クラスから受け継ぐ手書きのエンティティ</summary>
    [Table("things")]
    public sealed class Thing : KeyedThingBase
    {
        [Column("label")]
        public string Label { get; set; } = "";
    }

    [Fact(
        DisplayName = "生 SQL の取得で列が足りないとき、主キーを基底で宣言したエンティティでも取得した型を名指しする"
    )]
    public void MissingColumn_NamesTheEntityType()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 'x' AS label";
        using var reader = command.ExecuteReader();
        reader.Read();

        var act = () => EntitySaveMetadata.For(typeof(Thing)).CreateRawSqlRowPlan(reader);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("The raw SQL SELECT must include the Thing columns*");
    }
}
