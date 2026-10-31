namespace RBush.Test;

public class EnvelopeValidationTests
{
	private const double Nan = double.NaN;

	public static IEnumerable<(double MinX, double MinY, double MaxX, double MaxY)> InvalidEnvelopes()
	{
		yield return (Nan, 0, 1, 1);
		yield return (0, Nan, 1, 1);
		yield return (0, 0, Nan, 1);
		yield return (0, 0, 1, Nan);
		yield return (Nan, Nan, Nan, Nan);
		yield return (2, 0, 1, 1);
		yield return (0, 2, 1, 1);
	}

	private static List<Point> Valid(int count) =>
		Enumerable.Range(0, count).Select(i => new Point(i, i, i + 1, i + 1)).ToList();

	[Test]
	[MethodDataSource(nameof(InvalidEnvelopes))]
	public async Task InsertRejectsInvalidEnvelope(double minX, double minY, double maxX, double maxY)
	{
		var tree = new RBush<Point>();
		tree.BulkLoad(Valid(5));
		var bad = new Point(minX, minY, maxX, maxY);

		var ex = Assert.Throws<ArgumentException>(() => tree.Insert(bad));

		await Assert.That(ex.ParamName).IsEqualTo("item");
		await Assert.That(tree.Count).IsEqualTo(5);
	}

	[Test]
	[MethodDataSource(nameof(InvalidEnvelopes))]
	public async Task BulkLoadRejectsInvalidEnvelopeAndNamesIndex(double minX, double minY, double maxX, double maxY)
	{
		var tree = new RBush<Point>();
		tree.BulkLoad(Valid(30));
		var before = tree.Search().Count;

		var batch = Valid(100);
		batch[57] = new Point(minX, minY, maxX, maxY);

		var ex = Assert.Throws<ArgumentException>(() => tree.BulkLoad(batch));

		await Assert.That(ex.ParamName).IsEqualTo("items");
		await Assert.That(ex.Message).Contains("index 57");
		await Assert.That(tree.Count).IsEqualTo(30);
		await Assert.That(tree.Search().Count).IsEqualTo(before);
	}

	[Test]
	public async Task BulkLoadSmallBatchWithBadLastItemLeavesTreeUnchanged()
	{
		var tree = new RBush<Point>();
		var batch = Valid(3);
		batch[2] = new Point(Nan, 0, 1, 1);

		var ex = Assert.Throws<ArgumentException>(() => tree.BulkLoad(batch));

		await Assert.That(ex.Message).Contains("index 2");
		await Assert.That(tree.Count).IsEqualTo(0);
		await Assert.That(tree.Search().Count).IsEqualTo(0);
	}

	[Test]
	public async Task BulkLoadRejectsNullItem()
	{
		var tree = new RBush<Point>();
		var batch = Valid(20);
		batch[4] = null!;

		Assert.Throws<ArgumentNullException>(() => tree.BulkLoad(batch));

		await Assert.That(tree.Count).IsEqualTo(0);
	}

	[Test]
	public async Task PointsAndInfiniteEnvelopesAreAccepted()
	{
		var tree = new RBush<Point>();
		var items = new List<Point>
		{
			new(1, 1, 1, 1),
			new(double.NegativeInfinity, double.NegativeInfinity, double.PositiveInfinity, double.PositiveInfinity),
			new(0, 0, double.PositiveInfinity, 5),
		};

		tree.Insert(items[0]);
		tree.BulkLoad(items.Skip(1));
		tree.BulkLoad(Valid(40));

		await Assert.That(tree.Count).IsEqualTo(43);
		await Assert.That(tree.Search(new Envelope(1, 1, 1, 1)).Count).IsGreaterThanOrEqualTo(2);
	}
}
