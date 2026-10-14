using System.Runtime.CompilerServices;

// 差分の照合の回数（SchemaDiffService.TableMatchings / ColumnMatchings）を、照合を 1 回だけ行うことのガードテストから参照できるように公開する
[assembly: InternalsVisibleTo("QuickER.Tests")]
