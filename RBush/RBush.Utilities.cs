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

	private List<T> DoSearch(in Envelope boundingBox)
	{
		if (!Root.Envelope.Intersects(boundingBox))
			return [];

		var intersections = new List<T>();
		var queue = Interlocked.Exchange(ref _searchQueue, value: null) ?? new Queue<Node>();
		queue.Enqueue(Root);

		while (queue.Count != 0)
		{
			var item = queue.Dequeue();

			if (item.IsLeaf)
			{
				foreach (var i in item.Items)
				{
					if (i.Envelope.Intersects(boundingBox))
						intersections.Add((T)i);
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

		// queue is empty here; hand it back for the next search
		_searchQueue = queue;
		return intersections;
	}

	// Inner nodes are pruned with Intersects, not Contains: a node's envelope is the
	// union of its children, so it can be larger than the box while some of its
	// children are still inside it. Contains is only the test for the items.
	// When a node lies wholly inside the box, all of its items do, so they are
	// added without testing each one.
	private List<T> DoSearchContained(in Envelope boundingBox)
	{
		var contained = new List<T>();
		if (!Root.Envelope.Intersects(boundingBox))
			return contained;

		var queue = Interlocked.Exchange(ref _searchQueue, value: null) ?? new Queue<Node>();
		queue.Enqueue(Root);

		while (queue.Count != 0)
		{
			var node = queue.Dequeue();

			if (boundingBox.Contains(node.Envelope))
			{
				_ = GetAllChildren(contained, node);
			}
			else if (node.IsLeaf)
			{
				foreach (var i in node.Items)
				{
					if (boundingBox.Contains(i.Envelope))
						contained.Add((T)i);
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

		_searchQueue = queue;
		return contained;
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
	// data is a private copy made by BulkLoad, so it can be reordered in place.
	private Node BuildTree(T[] data)
	{
		var treeHeight = GetDepth(data.Length);
		var rootMaxEntries = (int)Math.Ceiling(data.Length / Math.Pow(_maxEntries, treeHeight - 1));
		return BuildNodes(data, 0, data.Length, treeHeight, rootMaxEntries);
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

	// Builds the subtree for data[start..start+count). Instead of sorting, the range is
	// partially ordered in place (OMT with multi-select): only the slice boundaries are
	// put in order, the contents of each slice are left unordered.
	private Node BuildNodes(T[] data, int start, int count, int height, int maxEntries)
	{
		if (count <= maxEntries)
		{
			return height == 1
				? new Node(ToItemList(data, start, count), height)
				: new Node(
					[
						BuildNodes(data, start, count, height - 1, _maxEntries),
					],
					height);
		}

		var nodeSize = (count + (maxEntries - 1)) / maxEntries;
		var subSortLength = nodeSize * (int)Math.Ceiling(Math.Sqrt(maxEntries));
		var end = start + count;

		MultiSelect(data, start, end - 1, subSortLength, byX: true);

		var children = new List<ISpatialData>(maxEntries);
		for (var subStart = start; subStart < end; subStart += subSortLength)
		{
			var subCount = Math.Min(subSortLength, end - subStart);
			MultiSelect(data, subStart, subStart + subCount - 1, nodeSize, byX: false);

			for (var nodeStart = subStart; nodeStart < subStart + subCount; nodeStart += nodeSize)
			{
				var nodeCount = Math.Min(nodeSize, subStart + subCount - nodeStart);
				children.Add(BuildNodes(data, nodeStart, nodeCount, height - 1, _maxEntries));
			}
		}

		return new Node(children, height);
	}

	private static List<ISpatialData> ToItemList(T[] array, int start, int count)
	{
		var list = new List<ISpatialData>(count);
		for (var i = 0; i < count; i++)
			list.Add(array[start + i]);
		return list;
	}

	#region MultiSelect
	private static double Key(T item, bool byX) =>
		byX ? item.Envelope.MinX : item.Envelope.MinY;

	// Reorders data[left..right] so that every multiple-of-n offset from left is a
	// boundary: all items before it have a key <= and all items after a key >=.
	// Uses an explicit stack, so no recursion depth issues.
	private static void MultiSelect(T[] data, int left, int right, int n, bool byX)
	{
		var stack = new Stack<(int Left, int Right)>();
		stack.Push((left, right));

		while (stack.Count > 0)
		{
			(left, right) = stack.Pop();
			if (right - left <= n)
				continue;

			var mid = left + (int)Math.Ceiling((right - left) / (double)n / 2) * n;
			Select(data, mid, left, right, byX);

			stack.Push((left, mid));
			stack.Push((mid, right));
		}
	}

	// Quickselect with a three-way partition (robust against many equal keys):
	// afterwards data[k] holds the item that a full sort would put there.
	private static void Select(T[] data, int k, int left, int right, bool byX)
	{
		while (right > left)
		{
			var pivot = MedianOfThree(data, left, right, byX);
			var lt = left;
			var gt = right;
			var i = left;
			while (i <= gt)
			{
				var c = Key(data[i], byX).CompareTo(pivot);
				if (c < 0)
				{
					(data[lt], data[i]) = (data[i], data[lt]);
					lt++;
					i++;
				}
				else if (c > 0)
				{
					(data[i], data[gt]) = (data[gt], data[i]);
					gt--;
				}
				else
				{
					i++;
				}
			}

			if (k < lt)
				right = lt - 1;
			else if (k > gt)
				left = gt + 1;
			else
				return;
		}
	}

	private static double MedianOfThree(T[] data, int left, int right, bool byX)
	{
		var a = Key(data[left], byX);
		var b = Key(data[left + ((right - left) >> 1)], byX);
		var c = Key(data[right], byX);
		if (a.CompareTo(b) > 0) (a, b) = (b, a);
		if (b.CompareTo(c) > 0) b = c;
		return a.CompareTo(b) > 0 ? a : b;
	}
	#endregion
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
