using System;
using AwesomeAssertions;
using Xunit;

// 生成フィクスチャ自身の namespace に置く＝他フィクスチャの同名型と曖昧にならないようにする
namespace QuickER.Tests.GeneratedBinaryVoFixture;

/// <summary>
/// バイナリ値オブジェクトの <c>TryCreateFrom</c> / <c>CreateFrom</c> が Base64 文字列から生成できることを、
/// コミット済みフィクスチャ（<c>BinaryVoFixture.g.cs</c> の NoteBlobValue）の実型で検証する（DB 不要・CI 常時実行）。
/// </summary>
/// <remarks>
/// 固定する規則: 取り込み経路（CSV のフィールド・表計算のセル）の文字列はバイナリでは Base64＝EditModel の
/// バインディング入力と同じ記法で、<c>RawValueConverter.ConvertCore</c> の文字列分岐が byte[] を引き受ける。
/// これが破れると <c>Convert.ChangeType(string, byte[])</c> の InvalidCastException 経由で全 Base64 入力が
/// 検証例外になり、docs の「取り込みコードは値オブジェクトごとの分岐を持たずに書ける」が成立しない。
/// </remarks>
public sealed class ValueObjectBinaryCreateFromTests
{
    [Fact]
    public void バイナリ値オブジェクトはBase64文字列からTryCreateFromで生成できる()
    {
        var ok = NoteBlobValue.TryCreateFrom("AQID", out var created, out var errors);

        ok.Should().BeTrue();
        errors.Should().BeEmpty();
        created!.Value.Should().Equal([0x01, 0x02, 0x03]);
    }

    [Fact]
    public void バイナリ値オブジェクトはBase64文字列からCreateFromで生成できる()
    {
        var created = NoteBlobValue.CreateFrom("AQID");

        created!.Value.Should().Equal([0x01, 0x02, 0x03]);
    }

    [Fact]
    public void Base64でない文字列は変換不能の検証エラーになる()
    {
        var ok = NoteBlobValue.TryCreateFrom("@@not-base64@@", out var created, out var errors);

        ok.Should().BeFalse();
        created.Should().BeNull();
        errors.Should().ContainSingle("変換不能は例外でなく TryCreateFrom の失敗として返る");
    }

    [Fact]
    public void バイト配列の入力は従来どおり素通しで生成される()
    {
        var ok = NoteBlobValue.TryCreateFrom(new byte[] { 0x0A, 0x0B }, out var created, out _);

        ok.Should().BeTrue();
        created!.Value.Should().Equal([0x0A, 0x0B]);
    }

    [Fact]
    public void 空文字は従来どおり空欄として成功しnullを返す()
    {
        var ok = NoteBlobValue.TryCreateFrom(string.Empty, out var created, out var errors);

        ok.Should().BeTrue();
        created.Should().BeNull("空欄は Base64 変換より先に空欄早期リターンが引き取る");
        errors.Should().BeEmpty();
    }
}
