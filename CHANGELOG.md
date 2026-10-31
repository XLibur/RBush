# Changelog

## Contents

- [Unreleased](#unreleased)

## Unreleased

### 💥 Breaking Changes

- **The package and assembly are renamed to `XLibur.RBush`.** Replace the `RBush` package reference with `XLibur.RBush`. The namespace stays `RBush`, so no code changes are needed.
- **The library targets `net8.0`, `net10.0` and `net11.0`.** `net47` and `netstandard2.0` are no longer built, so .NET Framework and older .NET versions cannot use this release ([#11](https://github.com/XLibur/RBush/pull/11)).
- **`Insert`, `Delete`, `BulkLoad` and the constructor reject `null`.** A null comparer, a null item and a null `BulkLoad` collection now throw `ArgumentNullException` with the parameter name, instead of failing later with a `NullReferenceException` or an exception that named the wrong parameter ([#5](https://github.com/XLibur/RBush/pull/5)).
- **`ISpatialIndex<T>` has a new member, `SearchContained`.** Code that implements the interface itself must add it. `RBush<T>` already does.
- **`Knn` may call the predicate on different items, in a different order.** On `RBush<T>` it now walks the tree nearest-first and calls the predicate only on items popped in that order, not on every item in the window. Results and their order are unchanged, including ties. A predicate should not have side effects ([#15](https://github.com/XLibur/RBush/pull/15)).
- **Insert and BulkLoad now reject invalid envelopes.** Items whose Envelope has a NaN coordinate or MinX > MaxX / MinY > MaxY throw `ArgumentException` (param `item` / `items`) instead of silently corrupting the tree. `BulkLoad` checks every item first, reports the offending index, and leaves the tree unchanged on failure. A null item in `BulkLoad` now throws `ArgumentNullException` up front. `Update` applies the same check to the item's new envelope before it changes the tree. Points (min == max) and infinite coordinates are still accepted. Measured overhead is within noise (<3%).

### ✨ Features

- **`SearchContained(Envelope)` on `RBush<T>` and `ISpatialIndex<T>` returns only items that lie completely inside a box.** `Search(Envelope)` returns everything that touches the box. The new method skips items that only overlap its edge. Subtrees that are wholly inside the box are added without testing each item.
- **Allocation-free search into a caller list.** `RBush<T>.Search(in Envelope, List<T>)` and `SearchContained(in Envelope, List<T>)` append matches to the supplied list (not cleared) and return the number appended.
- **Count and existence queries that allocate nothing.** `SearchCount(in Envelope)` counts items touching the box; `Any(in Envelope)` stops at the first match. `SearchCount` is not named `Count` because `Count` is already the item-count property.
- **`Contains` and `Update`.** `Contains(T)` looks an item up by envelope descent and comparer equality without removing it. `Update(T item, in Envelope oldEnvelope)` moves an item whose envelope changed and returns false, leaving the tree unchanged, when the item is not found at the old envelope. Items equal under the comparer that sit under the old envelope are all removed and one copy is reinserted.

### 🐛 Bug Fixes

- **`Delete` removes empty nodes and shrinks the tree.** Emptied leaves and the old tree height stayed after heavy deletion, so memory was not freed and later inserts and searches still visited empty nodes. Empty nodes are now dropped, and the root is replaced by its child while it has only one ([#6](https://github.com/XLibur/RBush/pull/6)).
- **`BulkLoad` no longer adds a redundant tree level for exact powers of `maxEntries`.** The tree depth was computed with floating-point logarithms, which can round just above a whole number: 125 items with `maxEntries` 5 gave a tree one level too tall. It now uses integer arithmetic ([#4](https://github.com/XLibur/RBush/pull/4)).
- **The NuGet package includes the README.** The project referenced `readme.md`, but the file is `README.md`, so packing failed on Linux, where file names are case-sensitive.

### ⚡ Performance

- **`Insert` allocates about 70% to 75% less and is 16% to 25% faster.** The split path no longer uses LINQ or copies the child list ([#10](https://github.com/XLibur/RBush/pull/10)), and the insert path list is reused instead of allocated per call ([#12](https://github.com/XLibur/RBush/pull/12)). `maxEntries` 9, 100,000 items: 119 ms and 73.6 MB became 69.5 ms and 13.9 MB. The split scratch buffer now lives on the stack, which removes about 45% more of the remaining allocations ([#17](https://github.com/XLibur/RBush/pull/17)).
- **`Delete` is about 30% faster and allocates nothing.** It built a closure for every leaf it visited, which cost 6 MB for 5,000 deletes in a 10,000 item tree and 76 MB in a 100,000 item tree ([#12](https://github.com/XLibur/RBush/pull/12)).
- **`Knn` is 18% to 66% faster and allocates 40% to 53% less.** It keeps the best `k` candidates in a heap instead of sorting every item in the search window, so `k` of 10 in a 100,000 item tree went from 184 ms and 123 MB to 100 ms and 69 MB ([#15](https://github.com/XLibur/RBush/pull/15)).
- **`Search()` with no arguments is about 5 times faster and allocates about 80% less.** The result list is sized up front and the tree is walked with plain loops, which removes an iterator and a boxed enumerator for every node. 100,000 items went from 4.86 ms and 3.35 MB to 0.67 ms and 0.78 MB ([#16](https://github.com/XLibur/RBush/pull/16)).
- **`Search(Envelope)` allocates 21% to 45% less and is up to 14% faster.** One traversal queue is reused between searches. Concurrent searches stay safe: a search that finds the queue in use makes its own. Results keep their order ([#13](https://github.com/XLibur/RBush/pull/13)).
- **`BulkLoad` allocates about 4% to 8% less** ([#12](https://github.com/XLibur/RBush/pull/12), [#18](https://github.com/XLibur/RBush/pull/18)).
- **`Knn` is now a best-first nearest-neighbour search.** On `RBush<T>`, `Knn(k=10)` with no `maxDistance` takes about 35 us at N=10k (was 2.78 ms) and about 46 us at N=100k (was 35 ms). Allocation drops from 1.5 MB to 2.7 KB at N=10k and from 15.6 MB to 2.7 KB at N=100k, per 20 queries. Results, including the order of equal-distance items, are unchanged. Other `ISpatialIndex<T>` implementations keep the bounded-heap code.
- **`Knn` with `maxDistance` is faster.** The 1000-query benchmark goes from 1.64 ms to 1.51 ms at N=10k and from 7.1 ms to 3.1 ms at N=100k, and allocation drops from 890 KB to 133 KB (10k) and from 4.6 MB to 133 KB (100k). Gains are smaller because the cost is dominated by the result lists.
- **`Knn` calls the predicate less.** It is only called on items popped in nearest-first order, so a query with k results makes about k calls (plus rejected items) instead of testing every item in the window.
- **`BulkLoad` is about 40% faster and allocates about 85% less.** Tree building now partially orders one array in place with quickselect (as the original JS rbush does) instead of fully sorting and copying at every level. `BulkLoad_Class` (default job): N=10k 4.75 ms / 2.7 MB to 2.99 ms / 424 KB; N=100k 82.4 ms / 31.95 MB to 45.3 ms / 3.9 MB. Tree height and node count are unchanged. Items that share a coordinate may be grouped differently, but search results are identical.
- **One shared search traversal.** `Search(Envelope)`, `SearchContained(Envelope)`, the list overloads, `SearchCount` and `Any` all run through one sink-based traversal. `Search_Class` and `Search_Struct` timings and allocations are unchanged.

### 📝 Documentation

- **Thread safety is documented.** Any number of threads can search at once. `Insert`, `BulkLoad`, `Delete` and `Clear` need exclusive access, and an item's `Envelope` must not change while it is in the tree ([#19](https://github.com/XLibur/RBush/pull/19)).
- **The README says this is a fork of `viceroypenguin/RBush`**, and its build and license badges point at this repository ([#20](https://github.com/XLibur/RBush/pull/20)).

### 🔧 Maintenance

- **Releases are automated.** Release Drafter writes the release notes from pull request titles, and PR Autolabel labels each pull request from its Conventional Commits title (`feat:`, `fix:`, `perf:`, `docs:` and so on). The Publish Release workflow rolls this changelog, tags the release and publishes to NuGet through trusted publishing. The Pre-release workflow publishes alpha, beta and rc builds from any branch.
- **The tests run on TUnit 1.76.** They use TUnit's own assertions and run on `net8.0`, `net10.0` and `net11.0`. Coverage uses TUnit's built-in collector in place of coverlet ([#21](https://github.com/XLibur/RBush/pull/21)).
- **A benchmark project covers insert, bulk load, search, delete and nearest-neighbour queries.** It is part of the solution, so CI builds it ([#10](https://github.com/XLibur/RBush/pull/10), [#14](https://github.com/XLibur/RBush/pull/14)).
- **The solution file is now `RBush.slnx`** ([#8](https://github.com/XLibur/RBush/pull/8)).
- **Pull request builds cancel superseded runs**, skip README-only changes, and skip the Codecov upload where the token is not available ([#7](https://github.com/XLibur/RBush/pull/7)).
- **Dead code is removed** ([#18](https://github.com/XLibur/RBush/pull/18)).
