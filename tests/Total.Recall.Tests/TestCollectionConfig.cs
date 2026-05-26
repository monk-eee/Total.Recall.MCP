// Disable cross-collection test parallelization for this assembly.
//
// Background: the production code base intentionally uses static singletons
// for in-memory state (Metrics counters, StoreRegistry caches, process-wide
// env vars like TOTAL_RECALL_DATA / TOTAL_RECALL_NAMESPACE). xUnit's default
// behaviour parallelises across test classes, which causes tests that mutate
// or assert on those singletons to race with any other test that incidentally
// touches them (every `JsonLineStore.LoadAll()` call bumps `Metrics.CacheHit`
// or `Metrics.CacheMiss`, for example).
//
// Most "stateful" test classes opt into [Collection("ToolTests")] which
// serialises them against each other, but classes outside that collection
// still ran in parallel against it. Rather than try to keep 30+ classes
// correctly attributed forever, disable cross-collection parallelism for
// the whole assembly. Tests inside a single class still run serially by
// default, and total wall-clock cost is modest (~1180 tests in ~30s).
//
// Long-term fix per AGENTS.md design discipline is to inject Metrics /
// StoreRegistry rather than hold them as static state. Tracked in docs/TODO.md.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
