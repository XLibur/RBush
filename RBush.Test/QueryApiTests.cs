using TUnit.Assertions.Enums;

namespace RBush.Test;

public class QueryApiTests
{
	// Mutable item with reference equality, so it can be moved.
	private sealed class Box(double minX, double minY, double maxX, double maxY) : ISpatialData
	{
		private Envelope _envelope = new(minX, minY, maxX, maxY);

		public ref readonly Envelope Envelope => ref _envelope;

		public void MoveTo(double x, double y) => _envelope = new Envelope(x, y, x + 1, y + 1);
	}

	private static List<Box> CreateBoxes(int count, int seed)
	{
		var random = new Random(seed);
		var boxes = new List<Box>(count);
		for (var i = 0; i < count; i++)
		{
			var x = random.NextDouble() * 1000;
			var y = random.NextDouble() * 1000;
			boxes.Add(new Box(x, y, x + (random.NextDouble() * 20), y + (random.NextDouble() * 20)));
		}

		return boxes;
	}

	private static Envelope[] CreateQueries(int seed)
	{
		var random = new Random(seed);
		var queries = new Envelope[40];
		for (var i = 0; i < queries.Length; i++)
		{
			var x = random.NextDouble() * 1000;
			var y = random.NextDouble() * 1000;
			var size = random.NextDouble() * 300;
			queries[i] = new Envelope(x, y, x + size, y + size);
		}

		return queries;
	}

	private static RBush<Box> BuildTree(List<Box> boxes, bool bulk)
	{
		var tree = new RBush<Box>();
		if (bulk)
		{
			tree.BulkLoad(boxes);
		}
		else
		{
			foreach (var b in boxes)
				tree.Insert(b);
		}

		return tree;
	}

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task SearchIntoListMatchesAllocatingSearchAndBruteForce(bool bulk)
	{
		var boxes = CreateBoxes(1500, seed: 1);
		var tree = BuildTree(boxes, bulk);

		foreach (var q in CreateQueries(seed: 2))
		{
			var expected = tree.Search(q);
			var brute = boxes.Where(b => b.Envelope.Intersects(q)).ToList();
			var results = new List<Box>();

			var appended = tree.Search(q, results);

			await Assert.That(appended).IsEqualTo(expected.Count);
			await Assert.That(results).IsEquivalentTo(expected, CollectionOrdering.Matching);
			await Assert.That(results.Count).IsEqualTo(brute.Count);
			await Assert.That(results.ToHashSet()).IsEquivalentTo(brute.ToHashSet());
		}
	}

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task SearchContainedIntoListMatchesAllocatingSearchAndBruteForce(bool bulk)
	{
		var boxes = CreateBoxes(1500, seed: 3);
		var tree = BuildTree(boxes, bulk);

		foreach (var q in CreateQueries(seed: 4))
		{
			var expected = tree.SearchContained(q);
			var brute = boxes.Where(b => q.Contains(b.Envelope)).ToList();
			var results = new List<Box>();

			var appended = tree.SearchContained(q, results);

			await Assert.That(appended).IsEqualTo(expected.Count);
			await Assert.That(results).IsEquivalentTo(expected, CollectionOrdering.Matching);
			await Assert.That(results.ToHashSet()).IsEquivalentTo(brute.ToHashSet());
		}
	}

	[Test]
	public async Task SearchIntoListAppendsWithoutClearing()
	{
		var boxes = CreateBoxes(200, seed: 5);
		var tree = BuildTree(boxes, bulk: true);
		var sentinel = new Box(-5, -5, -4, -4);
		var results = new List<Box> { sentinel };

		var appended = tree.Search(Envelope.InfiniteBounds, results);
		var appendedContained = tree.SearchContained(Envelope.InfiniteBounds, results);

		await Assert.That(appended).IsEqualTo(200);
		await Assert.That(appendedContained).IsEqualTo(200);
		await Assert.That(results.Count).IsEqualTo(401);
		await Assert.That(results[0]).IsSameReferenceAs(sentinel);
	}

	[Test]
	public async Task SearchIntoListOnEmptyTreeOrMissReturnsZero()
	{
		var tree = new RBush<Box>();
		var results = new List<Box>();
		await Assert.That(tree.Search(Envelope.InfiniteBounds, results)).IsEqualTo(0);
		await Assert.That(tree.SearchContained(Envelope.InfiniteBounds, results)).IsEqualTo(0);

		tree.BulkLoad(CreateBoxes(50, seed: 6));
		var far = new Envelope(5000, 5000, 6000, 6000);
		await Assert.That(tree.Search(far, results)).IsEqualTo(0);
		await Assert.That(results).IsEmpty();
	}

	[Test]
	public async Task SearchIntoListRejectsNullList()
	{
		var tree = new RBush<Box>();
		var ex = Assert.Throws<ArgumentNullException>(() => tree.Search(Envelope.InfiniteBounds, null!));
		var ex2 = Assert.Throws<ArgumentNullException>(() => tree.SearchContained(Envelope.InfiniteBounds, null!));
		await Assert.That(ex.ParamName).IsEqualTo("results");
		await Assert.That(ex2.ParamName).IsEqualTo("results");
	}

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task SearchCountAndAnyMatchSearch(bool bulk)
	{
		var boxes = CreateBoxes(1500, seed: 7);
		var tree = BuildTree(boxes, bulk);

		foreach (var q in CreateQueries(seed: 8))
		{
			var expected = boxes.Count(b => b.Envelope.Intersects(q));
			await Assert.That(tree.SearchCount(q)).IsEqualTo(expected);
			await Assert.That(tree.Any(q)).IsEqualTo(expected > 0);
		}

		var far = new Envelope(5000, 5000, 6000, 6000);
		await Assert.That(tree.SearchCount(far)).IsEqualTo(0);
		await Assert.That(tree.Any(far)).IsFalse();
		await Assert.That(new RBush<Box>().Any(Envelope.InfiniteBounds)).IsFalse();
		await Assert.That(tree.SearchCount(Envelope.InfiniteBounds)).IsEqualTo(boxes.Count);
	}

	[Test]
	public async Task AnyStopsEarlyAndLeavesTreeSearchable()
	{
		var boxes = CreateBoxes(1000, seed: 9);
		var tree = BuildTree(boxes, bulk: true);

		// An early exit leaves the cached queue non-empty unless it is cleared.
		for (var i = 0; i < 5; i++)
			await Assert.That(tree.Any(Envelope.InfiniteBounds)).IsTrue();

		await Assert.That(tree.Search(Envelope.InfiniteBounds).Count).IsEqualTo(1000);
		await Assert.That(tree.SearchCount(Envelope.InfiniteBounds)).IsEqualTo(1000);
	}

	[Test]
	public async Task ContainsFindsItemsAndNotOthers()
	{
		var boxes = CreateBoxes(500, seed: 10);
		var tree = BuildTree(boxes, bulk: false);

		foreach (var b in boxes)
			await Assert.That(tree.Contains(b)).IsTrue();

		// same envelope, different identity: not equal under the default comparer
		var twin = new Box(boxes[0].Envelope.MinX, boxes[0].Envelope.MinY, boxes[0].Envelope.MaxX, boxes[0].Envelope.MaxY);
		await Assert.That(tree.Contains(twin)).IsFalse();
		await Assert.That(tree.Contains(new Box(-100, -100, -99, -99))).IsFalse();
		await Assert.That(new RBush<Box>().Contains(twin)).IsFalse();
	}

	[Test]
	public async Task ContainsIsFalseAfterDeleteAndDoesNotRemove()
	{
		var boxes = CreateBoxes(100, seed: 11);
		var tree = BuildTree(boxes, bulk: true);

		await Assert.That(tree.Contains(boxes[5])).IsTrue();
		await Assert.That(tree.Count).IsEqualTo(100);

		await Assert.That(tree.Delete(boxes[5])).IsTrue();
		await Assert.That(tree.Contains(boxes[5])).IsFalse();
		await Assert.That(tree.Contains(boxes[6])).IsTrue();
		await Assert.That(tree.Count).IsEqualTo(99);
	}

	[Test]
	public async Task ContainsRejectsNullItem()
	{
		var tree = new RBush<Box>();
		var ex = Assert.Throws<ArgumentNullException>(() => tree.Contains(null!));
		await Assert.That(ex.ParamName).IsEqualTo("item");
	}

	[Test]
	public async Task UpdateMovesItemToNewLocationOnly()
	{
		var boxes = CreateBoxes(400, seed: 12);
		var tree = BuildTree(boxes, bulk: true);
		var item = boxes[0];
		var old = item.Envelope;

		item.MoveTo(5000, 5000);
		var moved = tree.Update(item, old);

		await Assert.That(moved).IsTrue();
		await Assert.That(tree.Count).IsEqualTo(400);
		await Assert.That(tree.Search(new Envelope(4999, 4999, 5002, 5002))).Contains(item);
		await Assert.That(tree.Search(old)).DoesNotContain(item);
		await Assert.That(tree.Contains(item)).IsTrue();
		await Assert.That(tree.Search().Count).IsEqualTo(400);
	}

	[Test]
	public async Task UpdateKeepsTreeConsistentAfterManyMoves()
	{
		var boxes = CreateBoxes(600, seed: 13);
		var tree = BuildTree(boxes, bulk: false);
		var random = new Random(14);

		foreach (var b in boxes)
		{
			var old = b.Envelope;
			b.MoveTo(random.NextDouble() * 1000, random.NextDouble() * 1000);
			await Assert.That(tree.Update(b, old)).IsTrue();
		}

		await Assert.That(tree.Count).IsEqualTo(600);
		foreach (var q in CreateQueries(seed: 15))
		{
			var expected = boxes.Where(b => b.Envelope.Intersects(q)).ToHashSet();
			await Assert.That(tree.Search(q).ToHashSet()).IsEquivalentTo(expected);
		}
	}

	[Test]
	public async Task UpdateWithWrongOldEnvelopeReturnsFalseAndLeavesTreeUnchanged()
	{
		var boxes = CreateBoxes(300, seed: 16);
		var tree = BuildTree(boxes, bulk: true);
		var item = boxes[3];
		var before = tree.Search().ToHashSet();
		var originalEnvelope = item.Envelope;
		var wrong = new Envelope(-50, -50, -40, -40);

		var moved = tree.Update(item, wrong);

		await Assert.That(moved).IsFalse();
		await Assert.That(tree.Count).IsEqualTo(300);
		await Assert.That(tree.Search().ToHashSet()).IsEquivalentTo(before);
		await Assert.That(tree.Search(originalEnvelope)).Contains(item);

		await Assert.That(new RBush<Box>().Update(item, originalEnvelope)).IsFalse();
	}

	[Test]
	public async Task UpdateOfLastItemCondensesAndReinserts()
	{
		var tree = new RBush<Box>();
		var item = new Box(1, 1, 2, 2);
		tree.Insert(item);
		var old = item.Envelope;

		item.MoveTo(50, 50);

		await Assert.That(tree.Update(item, old)).IsTrue();
		await Assert.That(tree.Count).IsEqualTo(1);
		await Assert.That(tree.Search(old).Count).IsEqualTo(0);
		await Assert.That(tree.Search(item.Envelope).Count).IsEqualTo(1);
	}

	[Test]
	public async Task UpdateRejectsNullItem()
	{
		var tree = new RBush<Box>();
		var ex = Assert.Throws<ArgumentNullException>(() => tree.Update(null!, Envelope.InfiniteBounds));
		await Assert.That(ex.ParamName).IsEqualTo("item");
	}

	[Test]
	public async Task ConcurrentNewSearchOverloadsReturnConsistentResults()
	{
		var boxes = CreateBoxes(2000, seed: 17);
		var tree = BuildTree(boxes, bulk: true);
		var queries = CreateQueries(seed: 18);
		var expected = queries.Select(q => tree.Search(q).Count).ToArray();
		var expectedContained = queries.Select(q => tree.SearchContained(q).Count).ToArray();
		var expectedAny = expected.Select(c => c > 0).ToArray();

		var failures = 0;
		_ = Parallel.For(0, 16, _ =>
		{
			var results = new List<Box>();
			for (var round = 0; round < 100; round++)
			{
				for (var i = 0; i < queries.Length; i++)
				{
					results.Clear();
					if (tree.Search(queries[i], results) != expected[i] || results.Count != expected[i])
						_ = Interlocked.Increment(ref failures);
					results.Clear();
					if (tree.SearchContained(queries[i], results) != expectedContained[i])
						_ = Interlocked.Increment(ref failures);
					if (tree.SearchCount(queries[i]) != expected[i])
						_ = Interlocked.Increment(ref failures);
					if (tree.Any(queries[i]) != expectedAny[i])
						_ = Interlocked.Increment(ref failures);
				}
			}
		});

		await Assert.That(failures).IsEqualTo(0);
	}
}
