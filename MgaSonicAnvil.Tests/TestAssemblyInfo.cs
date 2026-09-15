using Xunit;

// UiStrings.Language などのグローバル状態を切り替えるテストがあるため、
// クラス間の並列実行を止めて言語の競合を防ぐ。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
