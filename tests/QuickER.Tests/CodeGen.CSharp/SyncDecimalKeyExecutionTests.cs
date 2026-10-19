using System;
using System.Linq;
using System.Reflection;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Tests.GeneratedSyncFixture;
using Xunit;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// decimal の主キーの同期のキーのテキストを、生成→実コンパイル→実行で確かめる
/// （同期のフィクスチャに decimal の主キーの表が無いので、図を組んで生成物の記述子を直接動かす）。
/// </summary>
/// <remarks>
/// キーのテキストは指数表記を使わずに書き（<c>G29</c> は絶対値 1e-5 未満を <c>1E-06</c> と書き、読み戻しの
/// <c>decimal.Parse</c> が <c>FormatException</c> になっていた）、以前の版が指数表記で書いたテキストは、ランの初めの
/// 書き直し（<c>NormalizeKeyText</c>）で新しい形へ直す。書き直しは decimal の主キーの表だけが持つ。
/// </remarks>
public sealed class SyncDecimalKeyExecutionTests
{
    /// <summary>指数表記になる小さな値の往復と、以前の形の書き直し・読み取りを確かめる</summary>
    [Theory(
        DisplayName = "同期の decimal の主キーは指数表記を使わずに書き、以前の指数表記のテキストを書き直して読む"
    )]
    [InlineData("0.000001", "1E-06")]
    [InlineData("-0.000001", "-1E-06")]
    [InlineData("0.0000000000000000000000000001", "1E-28")]
    [InlineData("1.5", "1.5")]
    [InlineData("100", "100")]
    public void DecimalKey_RoundTripsWithoutExponent(string expected, string legacy)
    {
        var descriptor = Descriptor();
        var value = decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture);

        Invoke<string>(descriptor, "FormatKey", value).Should().Be(expected);
        Invoke<decimal>(descriptor, "ParseKey", expected).Should().Be(value);
        Invoke<decimal>(descriptor, "ParseKey", legacy).Should().Be(value);
        Invoke<string>(descriptor, "NormalizeKeyText", legacy).Should().Be(expected);
        Invoke<string>(descriptor, "NormalizeKeyText", expected).Should().Be(expected);
    }

    /// <summary>書き直しを持つのは decimal の主キーの表だけ（int の主キーの表は持たない）</summary>
    [Fact(DisplayName = "同期のキーのテキストの書き直しは decimal の主キーの表だけが持つ")]
    public void OnlyDecimalKeyTable_HasExponentNormalizer()
    {
        var tables = Tables();

        Property(tables, "SyncAmount")
            .GetType()
            .GetProperty("NormalizeKeyText")!
            .GetValue(Property(tables, "SyncAmount"))
            .Should()
            .NotBeNull();
        Property(tables, "SyncOrder")
            .GetType()
            .GetProperty("NormalizeKeyText")!
            .GetValue(Property(tables, "SyncOrder"))
            .Should()
            .BeNull();
    }

    private static Type? _tables;

    private static Type Tables()
    {
        if (_tables is not null)
        {
            return _tables;
        }

        var diagram = SyncFixtureDefinition.Build();
        diagram.Entities.Add(
            new Entity
            {
                TableName = "sync_amounts",
                Columns =
                {
                    new Column
                    {
                        Name = "amount",
                        DataType = "decimal(38,30)",
                        IsPrimaryKey = true,
                        IsNullable = false,
                    },
                    new Column
                    {
                        Name = "memo",
                        DataType = "nvarchar(40)",
                        IsNullable = true,
                    },
                    new Column
                    {
                        Name = "row_ver",
                        DataType = "rowversion",
                        IsNullable = true,
                    },
                },
            }
        );
        var (primary, byDialect) = SyncFixtureDefinition.ResolveColumnTypes(diagram);
        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            primary,
            byDialect,
            SyncFixtureDefinition.Options with
            {
                RootNamespace = "QuickER.Tests.SyncDecimalKeyProbe",
                GenerateRemoteServices = false,
            }
        );
        result.HasErrors.Should().BeFalse();
        var assembly = GeneratedCodeCompiler.CompileAndLoad(result, "SyncDecimalKeyProbe");
        _tables = assembly.GetTypes().Single(type => type.Name == "GeneratedSyncTables");
        return _tables;
    }

    private static object Property(Type tables, string name) =>
        tables.GetProperty(name, BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;

    private static object Descriptor() => Property(Tables(), "SyncAmount");

    private static T Invoke<T>(object descriptor, string property, object argument)
    {
        var function = (Delegate)descriptor.GetType().GetProperty(property)!.GetValue(descriptor)!;
        return (T)function.DynamicInvoke(argument)!;
    }
}
