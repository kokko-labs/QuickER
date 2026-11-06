namespace QuickER.Tests.TestSupport.Guards;

/// <summary>規則の自己検査の表：名前・方言の宣言（QuickER.Model・QuickER.Dialects の正本を外で写さない）</summary>
internal static partial class SourceRuleProbes
{
    private static IEnumerable<SourceRuleProbe> Names() =>
        [
            // 置換規則の写しは連鎖のときだけ拾い（ファイルに 1 件）、片方だけの置換は拾わない
            new SourceRuleProbe(
                "constraint-name-safe-rule-copies-forms",
                "constraint-name-safe-rule-copies",
                [
                    new ProbeFile(
                        "Copy.cs",
                        """
                        internal static class Copy
                        {
                            public static string Safe(string name) => name.Replace(".", "_").Replace(" ", "_");
                            public static string Again(string name) => name.Replace( "." , "_" ) .Replace( " ", "_" );
                        }
                        """,
                        "Replace(\".\", \"_\").Replace(\" \", \"_\")"
                    ),
                    new ProbeFile(
                        "Miss.cs",
                        """
                        internal static class Miss
                        {
                            public static string Dot(string name) => name.Replace(".", "_");
                            public static string Space(string name) => name.Replace(" ", "_").Replace(".", "_");
                        }
                        """
                    ),
                ]
            ),
            // 既定名の組み立ては補間・連結の 4 つの形を 1 行に 1 件で拾い、ほかの接頭辞・連結でない形は拾わない
            new SourceRuleProbe(
                "constraint-name-default-compositions-forms",
                "constraint-name-default-compositions",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        internal static class Probe
                        {
                            public static string Fk(string t, string r) => $"FK_{t}_{r}";
                            public static string Pk(string t) => "PK_" + t;
                            public static string Fk2(string t) => "FK_"  +  t;
                            public static string Both(string t) => $"PK_{t}" + ("FK_" + t);
                            public static string Unique(string t) => $"UQ_{t}";
                            public static string Plain() => "FK_";
                            public static string Concat(string t) => string.Concat("PK_", t);
                        }
                        """,
                        "$\"FK_{",
                        "\"PK_\" +",
                        "\"FK_\" +",
                        "$\"PK_{"
                    ),
                ]
            ),
            // 文字列で読む規則は、条件コンパイルの外れる分岐の中の写しも拾う
            new SourceRuleProbe(
                "constraint-name-default-compositions-inactive-branch",
                "constraint-name-default-compositions",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        internal static class Probe
                        {
                            public static string Name(string t)
                            {
                        #if QUICKER_PROBE_BRANCH
                                return $"FK_{t}";
                        #else
                                return t;
                        #endif
                            }
                        }
                        """,
                        "$\"FK_{"
                    ),
                ]
            ) with
            {
                InactiveBranch = true,
            },
            // 修飾した呼び出し・using static の呼び出しを含むメンバーの名前で拾い、宣言と別の名前は拾わない
            new SourceRuleProbe(
                "unique-constraint-synthesize-name-calls-forms",
                "unique-constraint-synthesize-name-calls",
                [
                    new ProbeFile(
                        "probe.cs",
                        """
                        using static QuickER.Model.UniqueConstraint;
                        class Probe
                        {
                            static string SynthesizeName(string a) => a;
                            void Qualified() => QuickER.Model.UniqueConstraint.SynthesizeName("t", ["c"], limit);
                            void Static() => SynthesizeName("t", ["c"], limit);
                            void Other() => UniqueConstraint.SynthesizeNameLater("t");
                        }
                        """,
                        "Qualified",
                        "Static"
                    ),
                ]
            ),
            // 構文の規則は、条件コンパイルの外れる分岐の中の呼び出しも拾う
            new SourceRuleProbe(
                "unique-constraint-synthesize-name-calls-inactive-branch",
                "unique-constraint-synthesize-name-calls",
                [
                    new ProbeFile(
                        "probe.cs",
                        """
                        class Probe
                        {
                            string Name()
                            {
                        #if !QUICKER_PROBE_BRANCH
                                return "";
                        #else
                                return UniqueConstraint.SynthesizeName("t", ["c"], 63);
                        #endif
                            }
                        }
                        """,
                        "Name"
                    ),
                ]
            ) with
            {
                InactiveBranch = true,
            },
            // 名前の参照（修飾の右端・単独）を拾い、コメント・XmlDoc・文字列の中の言及は拾わない
            new SourceRuleProbe(
                "model-dialect-lookups-forms",
                "model-dialect-lookups",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        /// <summary>IdentifierLengthLimits は引かない</summary>
                        internal static class Probe
                        {
                            // IdentifierLengthLimits.For(dbms)
                            public static int A(string dbms) => QuickER.Dialects.IdentifierLengthLimits.For(dbms);
                            public static int B(string dbms) => IdentifierLengthLimits.For(dbms);
                            public static string C() => "IdentifierLengthLimits";
                        }
                        """,
                        "IdentifierLengthLimits",
                        "IdentifierLengthLimits"
                    ),
                ]
            ),
            // 比較の指定・変換を含みモデルの名前に触れる文を、文の単位（見出しの式・文・式本体のメンバー）で 1 回だけ、トークンを空白
            // なしで連結した先頭 90 字で拾う。修飾した比較の指定も拾い、モデルの名前に触れない文は拾わない
            new SourceRuleProbe(
                "schema-name-comparisons-forms",
                "schema-name-comparisons",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        internal static class Probe
                        {
                            public static bool A(Entity e, string n) => string.Equals(e.TableName, n, StringComparison.OrdinalIgnoreCase);

                            public static void B(Column c, string n, string columnName)
                            {
                                if (c.Name.Trim() == n)
                                {
                                    Done();
                                }

                                var x = n.Trim();
                                var y = global::System.StringComparer.Ordinal.Equals(columnName, n);
                                var w = string.Equals(c.Name.Trim(), n.ToLowerInvariant(), StringComparison.Ordinal);
                                var z = Entities.Where(entity => string.Equals(entity.TableName, someVeryLongVariableNameForTheProbe, StringComparison.Ordinal)).ToList();
                                var v = string.Equals(n, x, StringComparison.CurrentCulture);
                            }
                        }
                        """,
                        "publicstaticboolA(Entitye,stringn)=>string.Equals(e.TableName,n,StringComparison.OrdinalIg",
                        "c.Name.Trim()==n",
                        "vary=global::System.StringComparer.Ordinal.Equals(columnName,n);",
                        "varw=string.Equals(c.Name.Trim(),n.ToLowerInvariant(),StringComparison.Ordinal);",
                        "varz=Entities.Where(entity=>string.Equals(entity.TableName,someVeryLongVariableNameForTheP"
                    ),
                ]
            ),
            // ファイル全体の文脈で読む構文の規則も、条件コンパイルの外れる分岐の中の文を拾う
            new SourceRuleProbe(
                "schema-name-comparisons-inactive-branch",
                "schema-name-comparisons",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        internal static class Probe
                        {
                            public static bool Same(Entity e, string n)
                            {
                        #if QUICKER_PROBE_BRANCH
                                return e.TableName.ToLowerInvariant() == n;
                        #else
                                return false;
                        #endif
                            }
                        }
                        """,
                        "returne.TableName.ToLowerInvariant()==n;"
                    ),
                ]
            ) with
            {
                InactiveBranch = true,
            },
            // 方言名と大文字小文字を無視して完全に一致する文字列リテラルを綴りどおりの値で拾い、部分一致・前後の空白・補間文字列・
            // コメントは拾わない
            new SourceRuleProbe(
                "dialect-name-literals-forms",
                "dialect-name-literals",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        internal static class Probe
                        {
                            // "oracle"
                            public static string A() => "sqlserver";
                            public static string B() => "SQLite";
                            public static string C() => "mysql.data";
                            public static string D() => $"sqlite";
                            public static string E() => " postgresql";
                            public static string F() => "SqliteConnection";
                            public static string G() => @"MySQL";
                        }
                        """,
                        "sqlserver",
                        "SQLite",
                        "MySQL"
                    ),
                ]
            ),
            // 第 1 引数が '.' か "." の Split・IndexOf・LastIndexOf・Contains をメソッドの名前で拾い、ほかの区切り・ほかのメソッド・
            // 第 1 引数でない "." は拾わない
            new SourceRuleProbe(
                "schema-qualified-name-dot-splits-forms",
                "schema-qualified-name-dot-splits",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        internal static class Probe
                        {
                            public static void Hits(string name)
                            {
                                name.Split('.');
                                name.IndexOf(".");
                                name.Contains('.');
                                name.LastIndexOf(".", StringComparison.Ordinal);
                            }

                            public static void Misses(string name)
                            {
                                name.Split(',');
                                name.Replace(".", "");
                                name.StartsWith(".");
                                name.IndexOf("x", ".".Length);
                                name.Split(new[] { '.' });
                            }
                        }
                        """,
                        "Split",
                        "IndexOf",
                        "Contains",
                        "LastIndexOf"
                    ),
                ]
            ),
            // 能力の宣言の record の初期化子を持つプロパティだけを拾い、初期化子の無いプロパティ・ほかの型は拾わない
            new SourceRuleProbe(
                "sync-capabilities-initializers-forms",
                "sync-capabilities-initializers",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        public sealed record SyncDialectCapabilities
                        {
                            public required bool A { get; init; } = true;
                            public required bool B { get; init; }
                        }

                        public sealed record Other
                        {
                            public bool C { get; init; } = true;
                        }
                        """,
                        "A"
                    ),
                ]
            ),
            // 依存の 3 種類を「要素名 Include」で拾い、ほかの項目は拾わない
            new SourceRuleProbe(
                "dialects-project-references-forms",
                "dialects-project-references",
                [
                    new ProbeFile(
                        "Probe.csproj",
                        """
                        <Project Sdk="Microsoft.NET.Sdk">
                          <PropertyGroup>
                            <TargetFramework>net10.0</TargetFramework>
                          </PropertyGroup>
                          <ItemGroup>
                            <PackageReference Include="Some.Package" />
                            <ProjectReference Include="..\Other\Other.csproj" />
                            <FrameworkReference Include="Microsoft.AspNetCore.App" />
                            <Compile Include="Extra.cs" />
                            <None Include="readme.txt" />
                          </ItemGroup>
                        </Project>
                        """,
                        "PackageReference Some.Package",
                        "ProjectReference ..\\Other\\Other.csproj",
                        "FrameworkReference Microsoft.AspNetCore.App"
                    ),
                ]
            ),
        ];
}
