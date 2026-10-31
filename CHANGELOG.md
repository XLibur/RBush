# Changelog

## Contents

- [Unreleased](#unreleased)

## Unreleased

### 💥 Breaking Changes

- **The package and assembly are renamed to `XLibur.RBush`.** Replace the `RBush` package reference with `XLibur.RBush`. The namespace stays `RBush`, so no code changes are needed.
- **The library targets `net8.0`, `net10.0` and `net11.0`.** `net47` and `netstandard2.0` are no longer built, so .NET Framework and older .NET versions cannot use this release ([#11](https://github.com/XLibur/RBush/pull/11)).
- **`Insert`, `Delete`, `BulkLoad` and the constructor reject `null`.** A null comparer, a null item and a null `BulkLoad` collection now throw `ArgumentNullException` with the parameter name, instead of failing later with a `NullReferenceException` or an exception that named the wrong parameter ([#5](https://github.com/XLibur/RBush/pull/5)).
- **`Knn` may call the predicate on different items, in a different order.** It now keeps the best `k` items while it scans, instead of sorting everything first, so the predicate runs on candidates in scan order and not in distance order. Results and their order are unchanged, including ties. A predicate should not have side effects ([#15](https://github.com/XLibur/RBush/pull/15)).

### ✨ Features

- **`RBush<T>.SearchContained(Envelope)` returns only items that lie completely inside a box.** `Search(Envelope)` returns everything that touches the box. The new method skips items that only overlap its edge. Subtrees that are wholly inside the box are added without testing each item.

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
