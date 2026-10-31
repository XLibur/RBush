namespace RBush.Test;

public class BulkLoadTests
{
	private static readonly IEqualityComparer<Point> s_byReference = ReferenceEqualityComparer.Instance;

	private static Point[] MakePoints(int count, int seed, int gridSize)
	{
		var rng = new Random(seed);
		var points = new Point[count];
		for (var i = 0; i < count; i++)
		{
			// gridSize > 0 gives many duplicate coordinates; 0 gives distinct values
			var x = gridSize > 0 ? rng.Next(gridSize) : rng.NextDouble() * 1000;
			var y = gridSize > 0 ? rng.Next(gridSize) : rng.NextDouble() * 1000;
			var w = gridSize > 0 ? rng.Next(3) : rng.NextDouble() * 5;
			var h = gridSize > 0 ? rng.Next(3) : rng.NextDouble() * 5;
			points[i] = new Point(x, y, x + w, y + h);
		}

		return points;
	}

	// Returns the number of nodes, and checks every structural invariant.
	private static int Validate(RBush<Point>.Node node, int maxEntries, List<Point> items)
	{
		var nodes = 1;
		var expected = Envelope.EmptyBounds;
		foreach (var child in node.Children)
			expected = expected.Extend(child.Envelope);
		if (!node.Envelope.Equals(expected))
			throw new InvalidOperationException("node envelope does not enclose its children exactly");
		if (node.Children.Count > maxEntries)
			throw new InvalidOperationException("node over capacity");

		foreach (var child in node.Children)
		{
			if (node.IsLeaf)
			{
				items.Add((Point)child);
			}
			else
			{
				var childNode = (RBush<Point>.Node)child;
				if (childNode.Height != node.Height - 1)
					throw new InvalidOperationException("inconsistent height");
				nodes += Validate(childNode, maxEntries, items);
			}
		}

		return nodes;
	}

	private static readonly int[] s_maxEntriesCases = [4, 9, 16];

	[Test]
	public async Task BulkLoadMatchesBruteForce()
	{
		var rng = new Random(7);
		foreach (var maxEntries in s_maxEntriesCases)
		{
			int[] sizes =
			[
				0, 1, 2, maxEntries - 1, maxEntries, maxEntries + 1, (2 * maxEntries) + 3,
				maxEntries * maxEntries, (maxEntries * maxEntries) + 1, maxEntries * maxEntries * maxEntries,
				1000, 10_000,
			];
			foreach (var size in sizes)
			{
				foreach (var grid in new[] { 0, 10, 1 })
				{
					var points = MakePoints(size, size + maxEntries + grid, grid);
					var snapshot = points.ToArray();
					var tree = new RBush<Point>(maxEntries);
					tree.BulkLoad(points);

					// the caller's array is untouched
					await Assert.That(points.SequenceEqual(snapshot, s_byReference)).IsTrue();
					await Assert.That(tree.Count).IsEqualTo(size);

					var items = new List<Point>();
					if (size > 0)
						_ = Validate(tree.Root, maxEntries, items);
					await Assert.That(items.Count).IsEqualTo(size);
					await Assert.That(items.ToHashSet(s_byReference).Count).IsEqualTo(size);

					for (var q = 0; q < 25; q++)
					{
						var x = rng.NextDouble() * (grid > 0 ? grid : 1000);
						var y = rng.NextDouble() * (grid > 0 ? grid : 1000);
						var box = new Envelope(x, y, x + (rng.NextDouble() * 100), y + (rng.NextDouble() * 100));
						var found = tree.Search(box).ToHashSet(s_byReference);
						var expected = points.Where(p => p.Envelope.Intersects(box)).ToHashSet(s_byReference);
						await Assert.That(found.SetEquals(expected)).IsTrue();
					}
				}
			}
		}
	}

	[Test]
	public async Task BulkLoadAllEqualAndSortedInputs()
	{
		var same = Enumerable.Range(0, 5000).Select(_ => new Point(1, 1, 2, 2)).ToArray();
		var sorted = Enumerable.Range(0, 5000).Select(i => new Point(i, i, i, i)).ToArray();
		var reversed = sorted.AsEnumerable().Reverse().ToArray();
		foreach (var data in new[] { same, sorted, reversed })
		{
			var tree = new RBush<Point>();
			tree.BulkLoad(data);
			var items = new List<Point>();
			_ = Validate(tree.Root, 9, items);
			await Assert.That(items.Count).IsEqualTo(data.Length);
			await Assert.That(tree.Search().Count).IsEqualTo(data.Length);
		}
	}
}
