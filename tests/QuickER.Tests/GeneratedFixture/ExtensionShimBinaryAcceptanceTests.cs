using AwesomeAssertions;
using Xunit;

namespace QuickER.Tests.GeneratedBinaryVoFixture;

/// <summary>
/// 値オブジェクトの共通ルートへ書いた partial が、バイナリ系統の VO にも届くことを実フィクスチャ
/// （<c>BinaryVoFixture.g.cs</c>）の上で検証する。
/// </summary>
/// <remarks>
/// 拡張面は生成物の名前空間ごとに存在するため、バイナリ VO を持つこのフィクスチャの名前空間にも
/// ルートの partial を書いて確かめる（<c>GeneratedFixture</c> 側の受け入れ検証は string / bool / 日時 /
/// 数値の 4 系統を実インスタンスで、binary / GuidKey を型の形で押さえている）。
/// </remarks>
public sealed class ExtensionShimBinaryAcceptanceTests
{
    [Fact(DisplayName = "ValueObjectBase の partial へ足したメンバーがバイナリ VO でも呼べる")]
    public void ValueObjectRootShim_AddedMember_ReachesBinaryValueObjects()
    {
        var blob = NoteBlobValue.Create([1, 2, 3]);

        // Base64 の DisplayValue が返る＝共通ルートのメンバーがバイナリ系統の基底越しに効いている
        blob.DescribeValue().Should().Be($"NoteBlobValue:{Convert.ToBase64String([1, 2, 3])}");
        SealValue
            .Create([9])
            .DescribeValue()
            .Should()
            .Be($"SealValue:{Convert.ToBase64String([9])}");
    }
}

/// <summary>
/// バイナリ VO を持つフィクスチャ側の、値オブジェクト共通ルートの利用者 partial。
/// </summary>
/// <remarks>型引数リストは繰り返すが、制約は生成側の part が宣言済みなので省略する。</remarks>
public partial class ValueObjectBase<TSelf, TValue>
{
    /// <summary>型名と表示値を 1 行で表す（全 VO 共通で使える）</summary>
    public string DescribeValue() => $"{GetType().Name}:{DisplayValue}";
}
