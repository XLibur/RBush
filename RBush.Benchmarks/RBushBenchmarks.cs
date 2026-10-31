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

	[Benchmark]
	public int Delete_Class()
	{
		var tree = new RBush<PointC>();
		tree.BulkLoad(_classPoints);
		var deleted = 0;
		for (var i = 0; i < _classPoints.Length; i += 2)
			if (tree.Delete(_classPoints[i]))
				deleted++;
		return deleted;
	}
}
