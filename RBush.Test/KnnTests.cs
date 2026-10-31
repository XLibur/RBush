using TUnit.Assertions.Enums;

namespace RBush.Test;

public class KnnTests
{
	private static readonly Point[] s_points = Point.CreatePoints(
		new double[,]
		{
			{87,55,87,56},  {38,13,39,16}, {7,47,8,47},   {89,9,91,12},  {4,58,5,60},   {0,11,1,12},   {0,5,0,6},     {69,78,73,78},
			{56,77,57,81},  {23,7,24,9},   {68,24,70,26}, {31,47,33,50}, {11,13,14,15}, {1,80,1,80},   {72,90,72,91}, {59,79,61,83},
			{98,77,101,77}, {11,55,14,56}, {98,4,100,6},  {21,54,23,58}, {44,74,48,74}, {70,57,70,61}, {32,9,33,12},  {43,87,44,91},
			{38,60,38,60},  {62,48,66,50}, {16,87,19,91}, {5,98,9,99},   {9,89,10,90},  {89,2,92,6},   {41,95,45,98}, {57,36,61,40},
			{50,1,52,1},    {93,87,96,88}, {29,42,33,42}, {34,43,36,44}, {41,64,42,65}, {87,3,88,4},   {56,50,56,52}, {32,13,35,15},
			{3,8,5,11},     {16,33,18,33}, {35,39,38,40}, {74,54,78,56}, {92,87,95,90}, {12,97,16,98}, {76,39,78,40}, {16,93,18,95},
			{62,40,64,42},  {71,87,71,88}, {60,85,63,86}, {39,52,39,56}, {15,18,19,18}, {91,62,94,63}, {10,16,10,18}, {5,86,8,87},
			{85,85,88,86},  {44,84,44,88}, {3,94,3,97},   {79,74,81,78}, {21,63,24,66}, {16,22,16,22}, {68,97,72,97}, {39,65,42,65},
			{51,68,52,69},  {61,38,61,42}, {31,65,31,65}, {16,6,19,6},   {66,39,66,41}, {57,32,59,35}, {54,80,58,84}, {5,67,7,71},
			{49,96,51,98},  {29,45,31,47}, {31,72,33,74}, {94,25,95,26}, {14,7,18,8},   {29,0,31,1},   {48,38,48,40}, {34,29,34,32},
			{99,21,100,25}, {79,3,79,4},   {87,1,87,5},   {9,77,9,81},   {23,25,25,29}, {83,48,86,51}, {79,94,79,95}, {33,95,33,99},
			{1,14,1,14},    {33,77,34,77}, {94,56,98,59}, {75,25,78,26}, {17,73,20,74}, {11,3,12,4},   {45,12,47,12}, {38,39,39,39},
			{99,3,103,5},   {41,92,44,96}, {79,40,79,41}, {29,2,29,4},
		});

	[Test]
	public async Task FindsNNeighbors()
	{
		var bush = new RBush<Point>();
		bush.BulkLoad(s_points);
		var result = bush.Knn(10, 40, 40);
		var expected = s_points
			.OrderBy(b => b.DistanceTo(40, 40))
			.Take(10)
			.ToList();
		await Assert.That(result).IsEquivalentTo(expected, CollectionOrdering.Matching);
	}

	[Test]
	public void DoesNotThrowIfRequestingTooManyItems()
	{
		var bush = new RBush<Point>();
		bush.BulkLoad(s_points);

		_ = bush.Knn(1000, 40, 40);
	}

	/// <summary>
	/// This test is not correct in original javascript library
	/// </summary>
	[Test]
	public async Task FindAllNeighborsForMaxDistance()
	{
		var bush = new RBush<Point>();
		bush.BulkLoad(s_points);

		var result = bush.Knn(0, 40, 40, maxDistance: 10);
		var expected = s_points
			.Where(b => b.DistanceTo(40, 40) <= 10)
			.OrderBy(b => b.DistanceTo(40, 40))
			.ToList();
		await Assert.That(result).IsEquivalentTo(expected, CollectionOrdering.Matching);
	}

	[Test]
	public async Task FindNNeighborsForMaxDistance()
	{
		var bush = new RBush<Point>();
		bush.BulkLoad(s_points);

		var result = bush.Knn(1, 40, 40, maxDistance: 10);
		var expected = s_points
			.OrderBy(b => b.DistanceTo(40, 40))
			.Take(1)
			.ToList();

		await Assert.That(result).IsEquivalentTo(expected, CollectionOrdering.Matching);
	}

	[Test]
	public void DoesNotThrowIfRequestingTooManyItemsForMaxDistance()
	{
		var bush = new RBush<Point>();
		bush.BulkLoad(s_points);

		_ = bush.Knn(1000, 40, 40, maxDistance: 10);
	}

	private static readonly Point[] s_richData = Point.CreatePoints(
		new double[,]
		{
			{ 1, 2, 1, 2 }, { 3, 3, 3, 3 }, { 5, 5, 5, 5 },
			{ 4, 2, 4, 2 }, { 2, 4, 2, 4 }, { 5, 3, 5, 3 },
		});

	[Test]
	public async Task FindNeighborsThatSatisfyAGivenPredicate()
	{
		var bush = new RBush<Point>();
		bush.BulkLoad(s_richData);

		var result = bush.Knn(1, 2, 4, predicate: p => p.Envelope.MinX != 2);
		var expected = s_richData
			.Where(p => p.Envelope.MinX != 2)
			.OrderBy(b => b.DistanceTo(2, 4))
			.Take(1)
			.ToList();
		await Assert.That(result).IsEquivalentTo(expected, CollectionOrdering.Matching);
	}

	// The original LINQ implementation, kept as the reference for the equivalence test.
	private static List<Point> ReferenceKnn(
		RBush<Point> tree, int k, double x, double y, double? maxDistance, Func<Point, bool>? predicate)
	{
		var items = maxDistance == null
			? tree.Search()
			: tree.Search(new Envelope(x - maxDistance.Value, y - maxDistance.Value, x + maxDistance.Value, y + maxDistance.Value));

		var distances = items
			.Select(i => (Item: i, Distance: i.Envelope.DistanceTo(x, y)))
			.OrderBy(i => i.Distance)
			.AsEnumerable();

		if (maxDistance.HasValue)
			distances = distances.TakeWhile(i => i.Distance <= maxDistance.Value);
		if (predicate != null)
			distances = distances.Where(i => predicate(i.Item));
		if (k > 0)
			distances = distances.Take(k);

		return distances.Select(i => i.Item).ToList();
	}

	[Test]
	public async Task MatchesReferenceImplementationIncludingTies()
	{
		var rng = new Random(1234);
		// integer grid coordinates force many equal distances (ties)
		var points = Enumerable.Range(0, 600)
			.Select(_ =>
			{
				var px = rng.Next(0, 30);
				var py = rng.Next(0, 30);
				return new Point(px, py, px + rng.Next(0, 2), py + rng.Next(0, 2));
			})
			.ToArray();

		var tree = new RBush<Point>();
		tree.BulkLoad(points);

		Func<Point, bool>?[] predicates = [null, p => p.Envelope.MinX % 2 == 0, p => false];
		double?[] maxDistances = [null, 0, 3.5, 12, 100];
		int[] ks = [-1, 0, 1, 5, 50, 599, 600, 5000];

		foreach (var k in ks)
			foreach (var maxDistance in maxDistances)
				foreach (var predicate in predicates)
				{
					for (var q = 0; q < 5; q++)
					{
						var qx = rng.Next(-5, 35);
						var qy = rng.Next(-5, 35);
						var expected = ReferenceKnn(tree, k, qx, qy, maxDistance, predicate);
						var actual = tree.Knn(k, qx, qy, maxDistance, predicate);
						await Assert.That(actual.Count).IsEqualTo(expected.Count);
						for (var i = 0; i < expected.Count; i++)
							await Assert.That(actual[i]).IsSameReferenceAs(expected[i]);
					}
				}
	}

	[Test]
	[Arguments(4)]
	[Arguments(9)]
	[Arguments(64)]
	public async Task MatchesReferenceForInsertedAndDeletedTrees(int maxEntries)
	{
		var rng = new Random(99);
		var points = Enumerable.Range(0, 800)
			.Select(_ =>
			{
				var px = rng.Next(0, 25);
				var py = rng.Next(0, 25);
				return new Point(px, py, px, py);
			})
			.ToArray();

		var tree = new RBush<Point>(maxEntries);
		foreach (var p in points)
			tree.Insert(p);
		foreach (var p in points.Where((_, i) => i % 3 == 0))
			_ = tree.Delete(p);

		for (var q = 0; q < 60; q++)
		{
			var qx = rng.Next(-3, 28);
			var qy = rng.Next(-3, 28);
			var k = rng.Next(0, 40);
			double? maxDistance = q % 2 == 0 ? null : rng.Next(0, 10);
			var expected = ReferenceKnn(tree, k, qx, qy, maxDistance, predicate: null);
			var actual = tree.Knn(k, qx, qy, maxDistance);
			await Assert.That(actual.Count).IsEqualTo(expected.Count);
			for (var i = 0; i < expected.Count; i++)
				await Assert.That(actual[i]).IsSameReferenceAs(expected[i]);
		}
	}

	[Test]
	public async Task OnlyCallsPredicateForCandidatesThatCanEnterTheResult()
	{
		var tree = new RBush<Point>();
		tree.BulkLoad(s_points);

		var calls = 0;
		var result = tree.Knn(3, 40, 40, predicate: _ =>
		{
			calls++;
			return true;
		});

		await Assert.That(result.Count).IsEqualTo(3);
		await Assert.That(calls).IsEqualTo(3);
	}

	[Test]
	public async Task FallsBackForOtherSpatialIndexes()
	{
		var tree = new RBush<Point>();
		tree.BulkLoad(s_points);
		var wrapper = new IndexWrapper(tree);

		var expected = tree.Knn(10, 40, 40, maxDistance: 30);
		var actual = wrapper.Knn(10, 40, 40, maxDistance: 30);
		await Assert.That(actual).IsEquivalentTo(expected, CollectionOrdering.Matching);
	}

	[Test]
	public async Task ConcurrentQueriesReturnCorrectResults()
	{
		var rng = new Random(7);
		var points = Enumerable.Range(0, 5000)
			.Select(_ =>
			{
				var px = rng.NextDouble() * 1000;
				var py = rng.NextDouble() * 1000;
				return new Point(px, py, px, py);
			})
			.ToArray();
		var tree = new RBush<Point>();
		tree.BulkLoad(points);

		var failures = 0;
		_ = Parallel.For(0, 8, t =>
		{
			var local = new Random(t);
			for (var i = 0; i < 200; i++)
			{
				var qx = local.NextDouble() * 1000;
				var qy = local.NextDouble() * 1000;
				var expected = ReferenceKnn(tree, 10, qx, qy, maxDistance: null, predicate: null);
				var actual = tree.Knn(10, qx, qy);
				if (!expected.SequenceEqual(actual))
					_ = Interlocked.Increment(ref failures);
			}

		});

		await Assert.That(failures).IsEqualTo(0);
	}

	private sealed class IndexWrapper(RBush<Point> inner) : ISpatialIndex<Point>
	{
		public IReadOnlyList<Point> Search() => inner.Search();

		public IReadOnlyList<Point> Search(in Envelope boundingBox) => inner.Search(boundingBox);

		public IReadOnlyList<Point> SearchContained(in Envelope boundingBox) => inner.SearchContained(boundingBox);
	}

	[Test]
	public async Task ReturnsEmptyForEmptyTree() =>
		await Assert.That(new RBush<Point>().Knn(5, 0, 0)).IsEmpty();

	[Test]
	public async Task ReturnsStoredNodesAsItemsInsteadOfOpeningThem()
	{
		// RBush<ISpatialData> can store another tree's Root, which is a Node. Knn must
		// return it as an item, not return its children.
		var inner = new RBush<Point>();
		inner.BulkLoad([new Point(1, 1, 1, 1), new Point(2, 2, 2, 2), new Point(3, 3, 3, 3)]);

		var outer = new RBush<ISpatialData>();
		outer.Insert(inner.Root);

		var result = outer.Knn(5, 0, 0);

		await Assert.That(result.Count).IsEqualTo(1);
		await Assert.That(result[0]).IsSameReferenceAs(inner.Root);
	}

	[Test]
	public async Task SmallMaxDistanceWithNoLimitReturnsOnlyNearbyItems()
	{
		var tree = new RBush<Point>();
		tree.BulkLoad(s_points);

		var result = tree.Knn(0, 40, 40, maxDistance: 1);
		var expected = s_points
			.Where(p => p.DistanceTo(40, 40) <= 1)
			.OrderBy(p => p.DistanceTo(40, 40))
			.ToList();

		await Assert.That(result).IsEquivalentTo(expected, CollectionOrdering.Matching);
		await Assert.That(tree.Knn(0, 1000, 1000, maxDistance: 1)).IsEmpty();
	}
}
