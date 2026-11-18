using System.Runtime.CompilerServices;

// 差分の照合の回数（SchemaDiffService.TableMatchings / ColumnMatchings）を、照合を 1 回だけ行うことのガードテストから参照できるように公開する
[assembly: InternalsVisibleTo("QuickER.Tests")]

// 同期計画の internal の面を、同期計画の総当たりと能力の組の網から参照できるように公開する：前寄せの印
// （SyncPlanSection.IsMovedFirst）・計画器の項目の印（SchemaDiffItem.PlanMark）・計画の文の型と写し（Sync/Steps）・
// 計画器の途中の結果を受け取る入れ物と新しい組み立ての結果（Sync/Scheduling の SyncPlanProbe・SequentialSchedule）
[assembly: InternalsVisibleTo("QuickER.Tests.Integration")]
