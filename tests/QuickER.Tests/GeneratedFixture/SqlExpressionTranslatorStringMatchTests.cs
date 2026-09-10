using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using AwesomeAssertions;
using Xunit;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// 生成ランタイムの <c>SqlExpressionTranslator</c> が「文字列 VO の部分一致」をどこまで LIKE へ翻訳するかを
/// 固定する単体テスト。
/// </summary>
/// <remarks>
/// <para>
/// 判定の正本は翻訳判定契約 <c>IStringMatchValueObject&lt;TSelf&gt;</c> の<b>インターフェイスマップ</b>で、
/// 「その面を実装しているメソッド自身か」を見る。具象 VO は基底からこの面を継承するため、
/// 「面を実装している型のメソッドで、名前が Contains/StartsWith/EndsWith」という緩い判定にすると、
/// 利用者が per-VO の partial へ書いた自前の <c>Contains</c> まで LIKE へ巻き込む
/// ＝メソッドの中身が一切実行されないまま列の部分一致に化ける。
/// </para>
/// <para>
/// 型検査でもビルドでも出ない差なので、両アーム（基底の実装＝LIKE／VO 自身の宣言＝翻訳対象外）を
/// ここで固定する。
/// </para>
/// </remarks>
public sealed class SqlExpressionTranslatorStringMatchTests
{
    /// <summary>列判定用のプローブ。プロパティ名がそのまま列名として使われる（[Column] 属性なし）。</summary>
    private sealed class Probe
    {
        public NameValue? Name { get; set; }

        public ProbeCodeValue? Code { get; set; }
    }

    [Fact(
        DisplayName = "文字列 VO 基底が実装する Contains は LIKE へ翻訳する（インターフェイスマップの対象）"
    )]
    public void BaseDeclaredContains_TranslatesToLike()
    {
        var (sql, parameters) = Run(p => p.Name!.Contains("Ali"));

        sql.Should().Contain("[Name] LIKE");
        parameters.Should().HaveCount(1);
    }

    [Fact(
        DisplayName = "VO 自身が宣言した Contains は LIKE へ翻訳しない（面の継承だけでは対象にしない）"
    )]
    public void ValueObjectDeclaredContains_IsNotTranslatedToLike()
    {
        // 具象 VO も IStringMatchValueObject<TSelf> を継承実装しているため、名前だけの判定では
        // このメソッドまで LIKE になる。インターフェイスマップは実装メソッドだけを引くので翻訳対象外になり、
        // 翻訳できない呼び出しとして明示的に失敗する（黙って別の意味の SQL にならない）。
        var act = () => Run(p => p.Code!.Contains("Ali"));

        act.Should().Throw<NotSupportedException>();
    }

    /// <summary>SQL Server 方言のトランスレータで述語本体を条件へ変換する</summary>
    private static (string Sql, List<SqlQueryParameter> Parameters) Run(
        Expression<Func<Probe, bool>> predicate
    )
    {
        var parameters = new List<SqlQueryParameter>();
        var sql = SqlExpressionTranslator.ToCondition(predicate.Body, parameters);
        return (sql, parameters);
    }
}

/// <summary>
/// 手書きの文字列値オブジェクト（private コンストラクタ＋<c>New</c> の 2 メンバ）。自前の <c>Contains</c> を
/// 宣言して、翻訳判定が「面の継承」ではなく「面の実装」を見ていることの反証材料になる。
/// </summary>
public sealed class ProbeCodeValue
    : ValueObjectStringBase<ProbeCodeValue>,
        IValueObject<ProbeCodeValue, string>
{
    private ProbeCodeValue(string value)
        : base(value) { }

    /// <summary>検証済みの値からインスタンスを作る（公開ファクトリは基底側にある）</summary>
    static ProbeCodeValue IValueObject<ProbeCodeValue, string>.New(string value) => new(value);

    /// <summary>基底の部分一致を隠す利用者独自の判定（列の LIKE ではなく通常のメソッド呼び出し）</summary>
    public new bool Contains(string value) => false;
}
