using BenchmarkDotNet.Attributes;

namespace RBush.Benchmarks;

[MemoryDiagnoser]
public class RBushBenchmarks
{
	[Params(10_000, 100_000)]
	public int N { get; set; }

	private PointC[] _classPoints = [];
	private PointS[] _structPoints = [];
	private RBush<PointC> _classTree = new();
	private RBush<PointS> _structTree = new();
	private Envelope[] _queries = [];

	[GlobalSetup]
	public void Setup()
	{
		var rng = new Random(42);
		_classPoints = new PointC[N];
		_structPoints = new PointS[N];
		for (var i = 0; i < N; i++)
		{
			var x = rng.NextDouble() * 1000;
			var y = rng.NextDouble() * 1000;
			_classPoints[i] = new PointC(x, y);
			_structPoints[i] = new PointS(_classPoints[i]);
		}

		_queries = new Envelope[1000];
		for (var i = 0; i < _queries.Length; i++)
		{
			var x = rng.NextDouble() * 950;
			var y = rng.NextDouble() * 950;
			_queries[i] = new Envelope(x, y, x + 20, y + 20);
		}

		_classTree = new RBush<PointC>();
		_classTree.BulkLoad(_classPoints);
		_structTree = new RBush<PointS>();
		_structTree.BulkLoad(_structPoints);
	}

	[Benchmark]
	public int Insert_Class()
	{
		var tree = new RBush<PointC>();
		foreach (var p in _classPoints)
			tree.Insert(p);
		return tree.Count;
	}

	[Benchmark]
	public int Insert_Struct()
	{
		var tree = new RBush<PointS>();
		foreach (var p in _structPoints)
			tree.Insert(p);
		return tree.Count;
	}

	[Benchmark]
	public int BulkLoad_Class()
	{
		var tree = new RBush<PointC>();
		tree.BulkLoad(_classPoints);
		return tree.Count;
	}

	[Benchmark]
	public int Search_Class()
	{
		var total = 0;
		foreach (var q in _queries)
			total += _classTree.Search(q).Count;
		return total;
	}

	[Benchmark]
	public int Search_Struct()
	{
		var total = 0;
		foreach (var q in _queries)
			total += _structTree.Search(q).Count;
		return total;
	}

	private RBush<PointC> _deleteTree = new();

	[IterationSetup(Target = nameof(DeleteOnly_Class))]
	public void SetupDelete()
	{
		_deleteTree = new RBush<PointC>();
		_deleteTree.BulkLoad(_classPoints);
	}

	// Measures only the deletes; the tree is rebuilt outside the timed region.
	[Benchmark]
	public int DeleteOnly_Class()
	{
		var deleted = 0;
		for (var i = 0; i < _classPoints.Length; i += 2)
			if (_deleteTree.Delete(_classPoints[i]))
				deleted++;
		return deleted;
	}

	// Inserts into an already loaded tree: exercises FindCoveringArea and ResetEnvelope.
	[Benchmark]
	public int InsertIntoLoaded_Class()
	{
		var tree = new RBush<PointC>();
		tree.BulkLoad(_classPoints);
		for (var i = 0; i < 1000; i++)
			tree.Insert(_extraPoints[i]);
		return tree.Count;
	}

	private readonly PointC[] _extraPoints = CreateExtra();

	private static PointC[] CreateExtra()
	{
		var rng = new Random(99);
		var pts = new PointC[1000];
		for (var i = 0; i < pts.Length; i++)
			pts[i] = new PointC(rng.NextDouble() * 1000, rng.NextDouble() * 1000);
		return pts;
	}
}
