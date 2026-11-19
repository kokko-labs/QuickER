using System.Runtime.CompilerServices;

// 差分の照合の回数（SchemaDiffService.TableMatchings / ColumnMatchings）を、照合を 1 回だけ行うことのガードテストから参照できるように公開する
[assembly: InternalsVisibleTo("QuickER.Tests")]

// 同期計画の internal の面を、同期計画の総当たりと能力の組の網から参照できるように公開する：前寄せの印
// （SyncPlan.MovedFirstCount・StepSection.IsMovedFirst）・計画器の項目の印（SchemaDiffItem.PlanMark）・文の振り分け（Sync/Steps の
// StepSections.SectionOf）・計画器の結果と組み立ての結果（Sync の StepPlanner・Sync/Scheduling の StepSchedule）
[assembly: InternalsVisibleTo("QuickER.Tests.Integration")]
