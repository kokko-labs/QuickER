using System;
using System.IO;
using System.Threading;
using Microsoft.Data.Sqlite;
using QuickER.Tests.Integration;

[assembly: AssemblyFixture(typeof(SqliteTempRoot))]

namespace QuickER.Tests.Integration;

/// <summary>
/// SQLite の一時 DB（<see cref="SqliteTempDatabase"/>）の置き場所と片付け。
/// 置き場所は <c>%TEMP%\quicker-it\&lt;実行&gt;\&lt;db&gt;</c> で、実行（テストのプロセス）ごとに親フォルダを 1 つ持つ。
/// </summary>
/// <remarks>
/// <para>
/// 片付けは 3 段で行う。
/// 1 段目は <see cref="SqliteTempDatabase.Dispose"/> がその場で消す。
/// 生成コードは接続文字列を変えて（<c>Foreign Keys=True</c> などを足して）開くため、その接続のプールが
/// ファイルを開いたままで、その場では消せないことがある。
/// 2 段目は全テストの後（このアセンブリのフィクスチャの <see cref="Dispose"/>）で、全プールを解放してから
/// 実行の親フォルダを消す。テストはもう走っていないので、<c>ClearAllPools</c> が並列のテストのハンドルを壊すことはない。
/// 3 段目は次の実行の始め（最初の一時 DB を作るとき）で、終わった実行の親フォルダを消す。
/// 強制終了で 2 段目が走らなかった残りを回収するため。
/// </para>
/// <para>
/// 実行が走っているかは、親フォルダと並べて置くロックのファイル <c>&lt;実行&gt;.lock</c> で見分ける。
/// 走っている実行はロックのファイルを <see cref="FileShare.None"/> で開いたまま持つ（Linux では flock になる）。
/// 3 段目は、ロックのファイルを同じく <see cref="FileShare.None"/> で開けた実行だけを消す。
/// 開けないとき・判定で例外が出たときは、走っていると見なして触らない。
/// ロックのファイルは親フォルダより先に作り、親フォルダを消し終えてから消す。
/// この順序にすると、ロックの無い親フォルダは生まれない。
/// </para>
/// <para>
/// TEMP 直下に同じ前置きの名前を大量に並べると、8.3 の短い名前がぶつかり、1 回の作成に数百 ms かかる。
/// そのため TEMP 直下には <c>quicker-it</c> の 1 つだけを置き、その下の名前は前置きの無い guid にする。
/// </para>
/// </remarks>
public sealed class SqliteTempRoot : IDisposable
{
    private static readonly Lazy<RunFolder> Run = new(
        RunFolder.Start,
        LazyThreadSafetyMode.ExecutionAndPublication
    );

    /// <summary>実行の親フォルダを置くフォルダ（TEMP 直下）</summary>
    internal static string BaseDirectory => Path.Combine(Path.GetTempPath(), "quicker-it");

    /// <summary>この実行の親フォルダの下に、一時 DB 1 つ分のフォルダを作って返す</summary>
    internal static string CreateDatabaseDirectory()
    {
        var directory = Path.Combine(Run.Value.Directory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>全テストの後：全プールを解放し、この実行の親フォルダとロックのファイルを消す（2 段目）</summary>
    public void Dispose()
    {
        if (Run.IsValueCreated)
        {
            SqliteConnection.ClearAllPools();
            Run.Value.Finish();
        }
    }

    /// <summary>この実行の親フォルダと、それを走っている印として持つロックのファイル</summary>
    private sealed class RunFolder
    {
        private readonly FileStream _lock;
        private readonly string _lockPath;

        private RunFolder(string directory, string lockPath, FileStream lockStream)
        {
            Directory = directory;
            _lockPath = lockPath;
            _lock = lockStream;
        }

        public string Directory { get; }

        /// <summary>終わった実行の残りを消してから（3 段目）、この実行のロックのファイルと親フォルダを作る</summary>
        public static RunFolder Start()
        {
            var baseDirectory = BaseDirectory;
            System.IO.Directory.CreateDirectory(baseDirectory);
            SweepFinishedRuns(baseDirectory);

            var name = Guid.NewGuid().ToString("N");
            var lockPath = Path.Combine(baseDirectory, name + ".lock");
            var lockStream = new FileStream(
                lockPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None
            );
            var directory = Path.Combine(baseDirectory, name);
            System.IO.Directory.CreateDirectory(directory);
            return new RunFolder(directory, lockPath, lockStream);
        }

        /// <summary>親フォルダを消し、消し終えたらロックのファイルを閉じて消す（消せなければ次の実行の 3 段目へ任せる）</summary>
        public void Finish()
        {
            if (!TryDeleteDirectory(Directory))
            {
                Console.Error.WriteLine(
                    $"[SqliteTempRoot] 一時 DB のフォルダを消せなかった（次の実行の始めに消す）: {Directory}"
                );
                return;
            }

            _lock.Dispose();
            TryDeleteFile(_lockPath);
        }

        /// <summary>ロックのファイルを開ける（＝終わった）実行の親フォルダとロックのファイルを消す</summary>
        private static void SweepFinishedRuns(string baseDirectory)
        {
            string[] lockPaths;

            try
            {
                lockPaths = System.IO.Directory.GetFiles(baseDirectory, "*.lock");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return;
            }

            foreach (var lockPath in lockPaths)
            {
                if (!HasFinished(lockPath))
                {
                    continue;
                }

                var directory = Path.Combine(
                    baseDirectory,
                    Path.GetFileNameWithoutExtension(lockPath)
                );

                if (TryDeleteDirectory(directory))
                {
                    TryDeleteFile(lockPath);
                }
            }
        }

        /// <summary>
        /// ロックのファイルを <see cref="FileShare.None"/> で開ければ、その実行は終わっている。
        /// 開けないとき・例外が出たときは走っていると見なす（走っている実行の DB を消さない側へ倒す）。
        /// </summary>
        private static bool HasFinished(string lockPath)
        {
            try
            {
                using var probe = new FileStream(
                    lockPath,
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.None
                );
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>フォルダを消し、消し終えた（もう無い）かを返す</summary>
        private static bool TryDeleteDirectory(string directory)
        {
            try
            {
                if (System.IO.Directory.Exists(directory))
                {
                    System.IO.Directory.Delete(directory, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 消し残しは Exists で判定する
            }

            return !System.IO.Directory.Exists(directory);
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 消せなかったロックのファイルは、次の実行の 3 段目が開けて消す
            }
        }
    }
}
