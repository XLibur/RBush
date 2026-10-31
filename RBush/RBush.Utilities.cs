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
	private List<T> DoSearch(in Envelope boundingBox)
	{
		if (!Root.Envelope.Intersects(boundingBox))
			return [];

		var intersections = new List<T>();
		var queue = new Queue<Node>();
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

		return intersections;
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
		var suffix = new Envelope[count + 1];
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
		foreach (var subData in Chunk(byX, subSortLength))
		{
			var byY = new ArraySegment<T>(subData.OrderBy(d => d.Envelope.MinY).ToArray());

			foreach (var nodeData in Chunk(byY, nodeSize))
			{
				children.Add(BuildNodes(nodeData, height - 1, _maxEntries));
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

	private static IEnumerable<ArraySegment<T>> Chunk(ArraySegment<T> values, int chunkSize)
	{
		var start = 0;
		while (start < values.Count)
		{
			var len = Math.Min(values.Count - start, chunkSize);
			yield return new ArraySegment<T>(values.Array!, values.Offset + start, len);
			start += chunkSize;
		}
	}
	#endregion

	// Indexed loop: avoids boxing List<T>'s enumerator on every ResetEnvelope.
	private static Envelope GetEnclosingEnvelope(List<ISpatialData> items)
	{
		var envelope = Envelope.EmptyBounds;
		for (var i = 0; i < items.Count; i++)
			envelope = envelope.Extend(items[i].Envelope);

		return envelope;
	}

	private static Envelope GetEnclosingEnvelope(IEnumerable<ISpatialData> items)
	{
		var envelope = Envelope.EmptyBounds;
		foreach (var data in items)
			envelope = envelope.Extend(data.Envelope);

		return envelope;
	}

	private static List<T> GetAllChildren(List<T> list, Node n)
	{
		if (n.IsLeaf)
		{
			list.AddRange(n.Items.Cast<T>());
		}
		else
		{
			foreach (var node in n.Items.Cast<Node>())
				_ = GetAllChildren(list, node);
		}

		return list;
	}

}
