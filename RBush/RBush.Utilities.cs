using System.Buffers;

namespace RBush;

public partial class RBush<T>
{
	#region Sort Functions
	private static readonly IComparer<ISpatialData> s_compareMinX =
		Comparer<ISpatialData>.Create((x, y) => Comparer<double>.Default.Compare(x.Envelope.MinX, y.Envelope.MinX));
	private static readonly IComparer<ISpatialData> s_compareMinY =
		Comparer<ISpatialData>.Create((x, y) => Comparer<double>.Default.Compare(x.Envelope.MinY, y.Envelope.MinY));
	#endregion

	#region Search
	// One cached traversal queue. A search takes it with an atomic exchange and
	// puts it back when done; a concurrent search that finds it taken simply
	// allocates its own, so concurrent searches stay safe.
	private Queue<Node>? _searchQueue;

	// Receives search results. Add returns false to stop the traversal early.
	private interface IResultSink
	{
		bool Add(T item);
	}

	private struct ListSink(List<T> list) : IResultSink
	{
		public bool Add(T item)
		{
			list.Add(item);
			return true;
		}
	}

	private struct CountSink : IResultSink
	{
		public int Count { get; private set; }

		public bool Add(T item)
		{
			Count++;
			return true;
		}
	}

	private struct FirstSink : IResultSink
	{
		public bool Found { get; private set; }

		public bool Add(T item)
		{
			Found = true;
			return false;
		}
	}

	private List<T> DoSearch(in Envelope boundingBox)
	{
		var intersections = new List<T>();
		var sink = new ListSink(intersections);
		DoSearch(boundingBox, ref sink);
		return intersections;
	}

	private List<T> DoSearchContained(in Envelope boundingBox)
	{
		var contained = new List<T>();
		var sink = new ListSink(contained);
		DoSearchContained(boundingBox, ref sink);
		return contained;
	}

	// The one traversal behind every intersection query. TSink is a struct so the
	// JIT specializes (and inlines) the sink calls for each query kind.
	private void DoSearch<TSink>(in Envelope boundingBox, ref TSink sink)
		where TSink : struct, IResultSink
	{
		if (!Root.Envelope.Intersects(boundingBox))
			return;

		var queue = Interlocked.Exchange(ref _searchQueue, value: null) ?? new Queue<Node>();
		queue.Enqueue(Root);
		var more = true;

		while (more && queue.Count != 0)
		{
			var item = queue.Dequeue();

			if (item.IsLeaf)
			{
				foreach (var i in item.Items)
				{
					if (i.Envelope.Intersects(boundingBox) && !sink.Add((T)i))
					{
						more = false;
						break;
					}
				}
			}
			else
			{
				foreach (var i in item.Items)
				{
					if (i.Envelope.Intersects(boundingBox))
						queue.Enqueue((Node)i);
				}
			}
		}

		// hand the queue back for the next search (cleared if we stopped early)
		queue.Clear();
		_searchQueue = queue;
	}

	// Inner nodes are pruned with Intersects, not Contains: a node's envelope is the
	// union of its children, so it can be larger than the box while some of its
	// children are still inside it. Contains is only the test for the items.
	// When a node lies wholly inside the box, all of its items do, so they are
	// added without testing each one.
	private void DoSearchContained<TSink>(in Envelope boundingBox, ref TSink sink)
		where TSink : struct, IResultSink
	{
		if (!Root.Envelope.Intersects(boundingBox))
			return;

		var queue = Interlocked.Exchange(ref _searchQueue, value: null) ?? new Queue<Node>();
		queue.Enqueue(Root);
		var more = true;

		while (more && queue.Count != 0)
		{
			var node = queue.Dequeue();

			if (boundingBox.Contains(node.Envelope))
			{
				more = VisitAll(node, ref sink);
			}
			else if (node.IsLeaf)
			{
				foreach (var i in node.Items)
				{
					if (boundingBox.Contains(i.Envelope) && !sink.Add((T)i))
					{
						more = false;
						break;
					}
				}
			}
			else
			{
				foreach (var i in node.Items)
				{
					if (i.Envelope.Intersects(boundingBox))
						queue.Enqueue((Node)i);
				}
			}
		}

		queue.Clear();
		_searchQueue = queue;
	}

	private static bool VisitAll<TSink>(Node node, ref TSink sink)
		where TSink : struct, IResultSink
	{
		var items = node.Items;
		for (var i = 0; i < items.Count; i++)
		{
			var more = node.IsLeaf
				? sink.Add((T)items[i])
				: VisitAll((Node)items[i], ref sink);
			if (!more)
				return false;
		}

		return true;
	}

	private int DoCount(in Envelope boundingBox)
	{
		var sink = new CountSink();
		DoSearch(boundingBox, ref sink);
		return sink.Count;
	}

	private bool DoAny(in Envelope boundingBox)
	{
		var sink = new FirstSink();
		DoSearch(boundingBox, ref sink);
		return sink.Found;
	}

	// Descends only into nodes whose envelope contains the item's envelope (as Delete does).
	private bool DoContains(Node node, T item)
	{
		if (!node.Envelope.Contains(item.Envelope))
			return false;

		var items = node.Items;
		for (var i = 0; i < items.Count; i++)
		{
			if (node.IsLeaf)
			{
				if (_comparer.Equals((T)items[i], item))
					return true;
			}
			else if (DoContains((Node)items[i], item))
			{
				return true;
			}
		}

		return false;
	}
	#endregion

	#region Insert
	// Reused across inserts to avoid allocating a path list per insert.
	// Insert is not safe for concurrent use, so a single buffer is fine.
	private readonly List<Node> _path = [];

	private List<Node> FindCoveringArea(in Envelope area, int depth)
	{
		var path = _path;
		path.Clear();
		var node = Root;

		while (true)
		{
			path.Add(node);
			if (node.IsLeaf || path.Count == depth) return path;

			var next = node.Items[0];
			var nextArea = next.Envelope.Extend(area).Area;

			foreach (var i in node.Items)
			{
				var newArea = i.Envelope.Extend(area).Area;
				if (newArea > nextArea)
					continue;

				if (newArea == nextArea
					&& i.Envelope.Area >= next.Envelope.Area)
				{
					continue;
				}

				next = i;
				nextArea = newArea;
			}

			node = (next as Node)!;
		}
	}

	private void Insert(ISpatialData data, int depth)
	{
		var path = FindCoveringArea(data.Envelope, depth);

		var insertNode = path[^1];
		insertNode.Add(data);

		while (--depth >= 0)
		{
			if (path[depth].Items.Count > _maxEntries)
			{
				var newNode = SplitNode(path[depth]);
				if (depth == 0)
					SplitRoot(newNode);
				else
					path[depth - 1].Add(newNode);
			}
			else
			{
				path[depth].ResetEnvelope();
			}
		}

		// don't keep nodes alive through the reusable buffer
		path.Clear();
	}

	#region SplitNode
	private void SplitRoot(Node newNode) =>
		Root = new Node([Root, newNode], Root.Height + 1);

	private Node SplitNode(Node node)
	{
		SortChildren(node);

		var splitPoint = GetBestSplitIndex(node.Items);
		var newChildren = node.Items.GetRange(splitPoint, node.Items.Count - splitPoint);
		node.RemoveRange(splitPoint, node.Items.Count - splitPoint);
		return new Node(newChildren, node.Height);
	}

	#region SortChildren
	private void SortChildren(Node node)
	{
		node.Items.Sort(s_compareMinX);
		var splitsByX = GetPotentialSplitMargins(node.Items);
		node.Items.Sort(s_compareMinY);
		var splitsByY = GetPotentialSplitMargins(node.Items);

		if (splitsByX < splitsByY)
			node.Items.Sort(s_compareMinX);
	}

	private double GetPotentialSplitMargins(List<ISpatialData> children) =>
		GetPotentialEnclosingMargins(children, reverse: false) +
		GetPotentialEnclosingMargins(children, reverse: true);

	// When reverse is true, walks the list back to front, so no reversed copy is needed.
	private double GetPotentialEnclosingMargins(List<ISpatialData> children, bool reverse)
	{
		var count = children.Count;
		var envelope = Envelope.EmptyBounds;
		var i = 0;
		for (; i < _minEntries; i++)
		{
			envelope = envelope.Extend(children[reverse ? count - 1 - i : i].Envelope);
		}

		var totalMargin = envelope.Margin;
		for (; i < count - _minEntries; i++)
		{
			envelope = envelope.Extend(children[reverse ? count - 1 - i : i].Envelope);
			totalMargin += envelope.Margin;
		}

		return totalMargin;
	}
	#endregion

	private int GetBestSplitIndex(List<ISpatialData> children)
	{
		var count = children.Count;

		// suffix[i] encloses children[i..]; left is extended incrementally.
		// Candidates are visited in ascending order and only a strictly better
		// one replaces the current best, so ties keep the lowest index.
		// Small scratch buffer on the stack; fall back to the pool for unusually
		// large nodes (a custom, large maxEntries).
		const int StackLimit = 32;
		Envelope[]? rented = null;
		Span<Envelope> suffix = count + 1 <= StackLimit
			? stackalloc Envelope[StackLimit]
			: (rented = ArrayPool<Envelope>.Shared.Rent(count + 1));

		try
		{
			suffix[count] = Envelope.EmptyBounds;
			for (var i = count - 1; i >= _minEntries; i--)
				suffix[i] = suffix[i + 1].Extend(children[i].Envelope);

			var left = Envelope.EmptyBounds;
			for (var i = 0; i < _minEntries; i++)
				left = left.Extend(children[i].Envelope);

			var bestIndex = _minEntries;
			var bestOverlap = double.MaxValue;
			var bestArea = double.MaxValue;
			for (var i = _minEntries; i < count; i++)
			{
				var right = suffix[i];
				var overlap = left.Intersection(right).Area;
				var totalArea = left.Area + right.Area;
				if (overlap < bestOverlap || (overlap == bestOverlap && totalArea < bestArea))
				{
					bestIndex = i;
					bestOverlap = overlap;
					bestArea = totalArea;
				}

				left = left.Extend(children[i].Envelope);
			}

			return bestIndex;
		}
		finally
		{
			// Envelope holds no references, so there is nothing to clear.
			if (rented is not null)
				ArrayPool<Envelope>.Shared.Return(rented);
		}
	}
	#endregion
	#endregion

	#region BuildTree
	private Node BuildTree(T[] data)
	{
		var treeHeight = GetDepth(data.Length);
		var rootMaxEntries = (int)Math.Ceiling(data.Length / Math.Pow(_maxEntries, treeHeight - 1));
		return BuildNodes(new ArraySegment<T>(data), treeHeight, rootMaxEntries);
	}

	private int GetDepth(int numNodes)
	{
		// Smallest height h such that _maxEntries^h >= numNodes. Integer
		// arithmetic avoids floating-point error in Math.Log for exact powers.
		var height = 0;
		long capacity = 1;
		while (capacity < numNodes)
		{
			capacity *= _maxEntries;
			height++;
		}

		return height;
	}

	private Node BuildNodes(ArraySegment<T> data, int height, int maxEntries)
	{
		if (data.Count <= maxEntries)
		{
			return height == 1
				? new Node(ToItemList(data), height)
				: new Node(
					[
						BuildNodes(data, height - 1, _maxEntries),
					],
					height);
		}

		// after much testing, this is faster than using Array.Sort() on the provided array
		// in spite of the additional memory cost and copying. go figure!
		var byX = new ArraySegment<T>(data.OrderBy(i => i.Envelope.MinX).ToArray());

		var nodeSize = (data.Count + (maxEntries - 1)) / maxEntries;
		var subSortLength = nodeSize * (int)Math.Ceiling(Math.Sqrt(maxEntries));

		var children = new List<ISpatialData>(maxEntries);
		for (var subStart = 0; subStart < byX.Count; subStart += subSortLength)
		{
			var subData = Slice(byX, subStart, subSortLength);
			var byY = new ArraySegment<T>(subData.OrderBy(d => d.Envelope.MinY).ToArray());

			for (var nodeStart = 0; nodeStart < byY.Count; nodeStart += nodeSize)
			{
				children.Add(BuildNodes(Slice(byY, nodeStart, nodeSize), height - 1, _maxEntries));
			}
		}

		return new Node(children, height);
	}

	private static List<ISpatialData> ToItemList(ArraySegment<T> data)
	{
		var list = new List<ISpatialData>(data.Count);
		var array = data.Array!;
		for (var i = 0; i < data.Count; i++)
			list.Add(array[data.Offset + i]);
		return list;
	}

	// A view of at most chunkSize items starting at start; no copy.
	private static ArraySegment<T> Slice(ArraySegment<T> values, int start, int chunkSize) =>
		new(values.Array!, values.Offset + start, Math.Min(values.Count - start, chunkSize));
	#endregion

	// Indexed loop: avoids boxing List<T>'s enumerator on every ResetEnvelope.
	private static Envelope GetEnclosingEnvelope(List<ISpatialData> items)
	{
		var envelope = Envelope.EmptyBounds;
		for (var i = 0; i < items.Count; i++)
			envelope = envelope.Extend(items[i].Envelope);

		return envelope;
	}

	private static List<T> GetAllChildren(List<T> list, Node n)
	{
		// Indexed loops: Cast<T>() allocated an iterator and boxed an enumerator
		// for every node visited.
		var items = n.Items;
		if (n.IsLeaf)
		{
			for (var i = 0; i < items.Count; i++)
				list.Add((T)items[i]);
		}
		else
		{
			for (var i = 0; i < items.Count; i++)
				_ = GetAllChildren(list, (Node)items[i]);
		}

		return list;
	}

}
