namespace QuickER.Tests.TestSupport.Guards;

/// <summary>規則の自己検査の表：OS との境界</summary>
internal static partial class SourceRuleProbes
{
    private static IEnumerable<SourceRuleProbe> Platform() =>
        [
            // 保存先になりうるフォルダの GetFolderPath と GetTempPath を拾い、表示フォルダ・別の型の同名でない呼び出しは拾わない
            new SourceRuleProbe(
                "app-data-path-lookups-forms",
                "app-data-path-lookups",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        using System;
                        using System.IO;

                        internal static class Probe
                        {
                            public static void Hits()
                            {
                                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                                Path.GetTempPath();
                            }

                            public static void Misses()
                            {
                                Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                                Path.GetTempFileName();
                            }
                        }
                        """,
                        "GetFolderPath(LocalApplicationData)",
                        "GetFolderPath(ApplicationData)",
                        "GetFolderPath(UserProfile)",
                        "GetTempPath"
                    ),
                ]
            ),
            // 構文の規則は、条件コンパイルの外れる分岐（基準の記号では無効なテキスト）の中の呼び出しも拾う
            new SourceRuleProbe(
                "app-data-path-lookups-inactive-branch",
                "app-data-path-lookups",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        using System.IO;

                        internal static class Probe
                        {
                            public static string Root()
                            {
                        #if QUICKER_PROBE_BRANCH
                                return Path.GetTempPath();
                        #else
                                return "";
                        #endif
                            }
                        }
                        """,
                        "GetTempPath"
                    ),
                ]
            ) with
            {
                InactiveBranch = true,
            },
            // シンボルで判定し、using static・別名・補間・局所変数の初期化子の形も拾う。拾わないのは、/ 区切りの相対パスどうし・
            // 文字の多重定義・区切りを含まない前方一致・受け手だけがフルパスのもの
            new SourceRuleProbe(
                "path-prefix-comparisons-forms",
                "path-prefix-comparisons",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        using System;
                        using System.IO;
                        using static System.IO.Path;
                        using P = System.IO.Path;

                        internal static class Probe
                        {
                            public static void Hits(string path, string root)
                            {
                                path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
                                var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
                                path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
                                var full = Path.GetFullPath(root);
                                var withSeparator = full + "\\";
                                path.StartsWith(withSeparator);
                                path.StartsWith($"{root}{DirectorySeparatorChar}");
                                path.StartsWith(root + P.AltDirectorySeparatorChar);
                            }

                            public static void Misses(string path, string root, string relative)
                            {
                                relative.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);
                                path.StartsWith('x');
                                path.StartsWith("interval ", StringComparison.Ordinal);
                                path.StartsWith(root);
                                Path.GetFullPath(path).StartsWith(@"\\", StringComparison.Ordinal);
                            }
                        }
                        """,
                        "string.StartsWith",
                        "string.StartsWith",
                        "string.StartsWith",
                        "string.StartsWith",
                        "string.StartsWith"
                    ),
                ]
            ),
            // シンボルで判定し、using static・別名・目的型の new・非定数の代入・P/Invoke も拾う。CreateProcess の戻り値の起動・
            // UseShellExecute = false・ExternalLauncher の入口・起動でない P/Invoke は拾わない。
            // プローブはソース生成器を通さないので、LibraryImport の部分メソッドの実装が無い（CS8795）ことだけは除く
            new SourceRuleProbe(
                "process-launches-forms",
                "process-launches",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        using System;
                        using System.Diagnostics;
                        using System.Runtime.InteropServices;
                        using QuickER.Platform;
                        using static System.Diagnostics.Process;
                        using P = System.Diagnostics.Process;

                        internal static partial class Probe
                        {
                            public static void Hits(string path, bool shell, ProcessStartInfo info)
                            {
                                Process.Start(path);
                                Start(path, "arg");
                                P.Start(new ProcessStartInfo(path));
                                Process a = new();
                                var b = new P();
                                b.Start();
                                info.UseShellExecute = true;
                                info.UseShellExecute = shell;
                                _ = new ProcessStartInfo(path) { UseShellExecute = !false };
                            }

                            [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
                            private static extern IntPtr ShellExecuteW(IntPtr h, string op, string file, string args, string dir, int show);

                            [DllImport("kernel32.dll", EntryPoint = "CreateProcessW")]
                            private static extern bool Spawn(string app);

                            [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
                            private static partial uint WinExec(string command, uint show);

                            public static void Misses(string path, ProcessStartInfo info)
                            {
                                using var process = ExternalLauncher.CreateProcess(info);
                                process.Start();
                                info.UseShellExecute = false;
                                _ = new ProcessStartInfo(path) { UseShellExecute = false };
                                ExternalLauncher.OpenUrl(new Uri("https://example.com/"));
                            }

                            [DllImport("user32.dll")]
                            private static extern bool SetForegroundWindow(IntPtr hWnd);
                        }
                        """,
                        "Process.Start",
                        "Process.Start",
                        "Process.Start",
                        "new Process",
                        "new Process",
                        "Process.Start（CreateProcess の戻り値でない）",
                        "UseShellExecute = true",
                        "UseShellExecute = (定数でない)",
                        "UseShellExecute = true",
                        "P/Invoke ShellExecuteW",
                        "P/Invoke CreateProcessW",
                        "P/Invoke WinExec"
                    ),
                ]
            ) with
            {
                IgnoredDiagnostics = ["CS8795"],
            },
            // 許す側の判定：CreateProcess の戻り値で初期化した局所変数の起動だけを拾い、ほかの起動は拾わない
            new SourceRuleProbe(
                "process-launch-create-process-starts-forms",
                "process-launch-create-process-starts",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        using System.Diagnostics;
                        using QuickER.Platform;

                        internal static class Probe
                        {
                            public static void Run(ProcessStartInfo info)
                            {
                                using var process = ExternalLauncher.CreateProcess(info);
                                process.Start();
                                var other = new Process();
                                other.Start();
                            }
                        }
                        """,
                        "start"
                    ),
                ]
            ),
            // シンボルの規則は、条件コンパイルの外れる分岐の中も変種のコンパイルで読む
            new SourceRuleProbe(
                "process-launch-create-process-starts-inactive-branch",
                "process-launch-create-process-starts",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        using System.Diagnostics;
                        using QuickER.Platform;

                        internal static class Probe
                        {
                            public static void Run(ProcessStartInfo info)
                            {
                        #if QUICKER_PROBE_BRANCH
                                using var process = ExternalLauncher.CreateProcess(info);
                                process.Start();
                        #endif
                            }
                        }
                        """,
                        "start"
                    ),
                ]
            ) with
            {
                InactiveBranch = true,
            },
            // シンボルで判定し、構文の別の書き方（目的型の new・using static・別名・FileInfo・変数の FileMode・OpenHandle・削除・
            // ラムダの中）も同じ API として拾う。読み取り・ストリームへの書き込みはファイルの書き込みではないので拾わない
            new SourceRuleProbe(
                "raw-file-writes-forms",
                "raw-file-writes",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        using System;
                        using System.IO;
                        using QuickER.Platform;
                        using static System.IO.File;
                        using F = System.IO.File;

                        internal static class Probe
                        {
                            public static void Write(string path, string other, Stream stream)
                            {
                                FileStream a = new(path, FileMode.Create);
                                StreamWriter b = new(path);
                                new FileInfo(path).OpenWrite().Dispose();
                                new FileInfo(path).CreateText().Dispose();
                                WriteAllText(path, "x");
                                F.AppendAllText(path, "x");
                                var mode = FileMode.Open;
                                using var c = new FileStream(path, mode);
                                using var d = File.OpenHandle(path, FileMode.Create, FileAccess.Write);
                                File.Delete(path);
                                Directory.Delete(path, true);
                                new FileInfo(path).CopyTo(other);
                                AtomicFile.Write(path, s => File.WriteAllText(other, "x"));
                            }

                            public static void Read(string path, Stream stream)
                            {
                                using var a = new FileStream(path, FileMode.Open, FileAccess.Read);
                                using var b = File.OpenHandle(path);
                                using var c = new StreamWriter(stream);
                                using var d = File.Open(path, FileMode.Open, FileAccess.Read);
                                File.ReadAllText(path);
                                AtomicFile.Write(path, s => s.WriteByte(1));
                            }
                        }
                        """,
                        "new FileStream",
                        "new StreamWriter",
                        "FileInfo.OpenWrite",
                        "FileInfo.CreateText",
                        "File.WriteAllText",
                        "File.AppendAllText",
                        "new FileStream",
                        "File.OpenHandle",
                        "File.Delete",
                        "Directory.Delete",
                        "FileInfo.CopyTo",
                        "File.WriteAllText"
                    ),
                ]
            ),
            // シンボルの規則は、条件コンパイルの外れる分岐（#else の側）の中の書き込みも変種のコンパイルで読む
            new SourceRuleProbe(
                "raw-file-writes-inactive-branch",
                "raw-file-writes",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        using System.IO;

                        internal static class Probe
                        {
                            public static void Save(string path)
                            {
                        #if !QUICKER_PROBE_BRANCH
                                File.ReadAllText(path);
                        #else
                                File.WriteAllText(path, "x");
                        #endif
                            }
                        }
                        """,
                        "File.WriteAllText"
                    ),
                ]
            ) with
            {
                InactiveBranch = true,
            },
        ];
}
