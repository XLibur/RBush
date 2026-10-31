using System.Diagnostics.CodeAnalysis;

namespace RBush;

/// <summary>
/// An implementation of the R-tree data structure for 2-d spatial indexing.
/// </summary>
/// <typeparam name="T">The type of elements in the index.</typeparam>
public partial class RBush<T> : ISpatialDatabase<T>, ISpatialIndex<T> where T : ISpatialData
{
	private const int DefaultMaxEntries = 9;
	private const int MinimumMaxEntries = 4;
	private const int MinimumMinEntries = 2;
	private const double DefaultFillFactor = 0.4;

	private readonly IEqualityComparer<T> _comparer;
	private readonly int _maxEntries;
	private readonly int _minEntries;

	/// <summary>
	/// The root of the R-tree.
	/// </summary>
	public Node Root { get; private set; }

	/// <summary>
	/// The bounding box of all elements currently in the data structure.
	/// </summary>
	public ref readonly Envelope Envelope => ref Root.Envelope;

	/// <summary>
	/// Initializes a new instance of the <see cref="RBush{T}"/> that is
	/// empty and has the default tree width and default <see cref="IEqualityComparer{T}"/>.
	/// </summary>
	public RBush()
		: this(DefaultMaxEntries, EqualityComparer<T>.Default) { }

	/// <summary>
	/// Initializes a new instance of the <see cref="RBush{T}"/> that is
	/// empty and has a custom max number of elements per tree node
	/// and default <see cref="IEqualityComparer{T}"/>.
	/// </summary>
	/// <param name="maxEntries"></param>
	public RBush(int maxEntries)
		: this(maxEntries, EqualityComparer<T>.Default) { }

	/// <summary>
	/// Initializes a new instance of the <see cref="RBush{T}"/> that is
	/// empty and has a custom max number of elements per tree node
	/// and a custom <see cref="IEqualityComparer{T}"/>.
	/// </summary>
	/// <param name="maxEntries"></param>
	/// <param name="comparer"></param>
	public RBush(int maxEntries, IEqualityComparer<T> comparer)
	{
		ArgumentNullException.ThrowIfNull(comparer);

		_comparer = comparer;
		_maxEntries = Math.Max(MinimumMaxEntries, maxEntries);
		_minEntries = Math.Max(MinimumMinEntries, (int)Math.Ceiling(_maxEntries * DefaultFillFactor));

		Clear();
	}

	/// <summary>
	/// Gets the number of items currently stored in the <see cref="RBush{T}"/>
	/// </summary>
	public int Count { get; private set; }

	/// <summary>
	/// Removes all elements from the <see cref="RBush{T}"/>.
	/// </summary>
	[MemberNotNull(nameof(Root))]
	public void Clear()
	{
		Root = new Node([], 1);
		Count = 0;
	}

	/// <summary>
	/// Get all of the elements within the current <see cref="RBush{T}"/>.
	/// </summary>
	/// <returns>
	/// A list of every element contained in the <see cref="RBush{T}"/>.
	/// </returns>
	public IReadOnlyList<T> Search() =>
		GetAllChildren(new List<T>(Count), Root);

	/// <summary>
	/// Get all of the elements from this <see cref="RBush{T}"/>
	/// within the <paramref name="boundingBox"/> bounding box.
	/// </summary>
	/// <param name="boundingBox">The area for which to find elements.</param>
	/// <returns>
	/// A list of the points that are within the bounding box
	/// from this <see cref="RBush{T}"/>.
	/// </returns>
	public IReadOnlyList<T> Search(in Envelope boundingBox) =>
		DoSearch(boundingBox);

	/// <summary>
	/// Adds an object to the <see cref="RBush{T}"/>
	/// </summary>
	/// <param name="item">
	/// The object to be added to <see cref="RBush{T}"/>.
	/// </param>
	public void Insert(T item)
	{
		// Guard on `is null` first so value-type T is never boxed.
		if (item is null)
			ArgumentNullException.ThrowIfNull(item);

		Insert(item, Root.Height);
		Count++;
	}

	/// <summary>
	/// Adds all of the elements from the collection to the <see cref="RBush{T}"/>.
	/// </summary>
	/// <param name="items">
	/// A collection of items to add to the <see cref="RBush{T}"/>.
	/// </param>
	/// <remarks>
	/// For multiple items, this method is more performant than 
	/// adding items individually via <see cref="Insert(T)"/>.
	/// </remarks>
	public void BulkLoad(IEnumerable<T> items)
	{
		ArgumentNullException.ThrowIfNull(items);

		var data = items.ToArray();
		if (data.Length == 0) return;

		if (Root.IsLeaf &&
			Root.Items.Count + data.Length < _maxEntries)
		{
			foreach (var i in data)
				Insert(i);
			return;
		}

		if (data.Length < _minEntries)
		{
			foreach (var i in data)
				Insert(i);
			return;
		}

		var dataRoot = BuildTree(data);
		Count += data.Length;

		if (Root.Items.Count == 0)
		{
			Root = dataRoot;
		}
		else if (Root.Height == dataRoot.Height)
		{
			if (Root.Items.Count + dataRoot.Items.Count <= _maxEntries)
			{
				foreach (var isd in dataRoot.Items)
					Root.Add(isd);
			}
			else
			{
				SplitRoot(dataRoot);
			}
		}
		else
		{
			if (Root.Height < dataRoot.Height)
			{
				(Root, dataRoot) = (dataRoot, Root);
			}

			Insert(dataRoot, Root.Height - dataRoot.Height);
		}
	}

	/// <summary>
	/// Removes an object from the <see cref="RBush{T}"/>.
	/// </summary>
	/// <param name="item">
	/// The object to be removed from the <see cref="RBush{T}"/>.
	/// </param>
	/// <returns><see langword="bool" /> indicating whether the item was deleted.</returns>
	public bool Delete(T item)
	{
		// Guard on `is null` first so value-type T is never boxed.
		if (item is null)
			ArgumentNullException.ThrowIfNull(item);

		var deleted = DoDelete(Root, item);
		if (deleted)
			CondenseRoot();

		return deleted;
	}

	// Shrinks the tree when deletions leave the root with at most one child.
	private void CondenseRoot()
	{
		while (!Root.IsLeaf && Root.Items.Count <= 1)
		{
			if (Root.Items.Count == 0)
			{
				Clear();
				return;
			}

			Root = (Node)Root.Items[0];
		}
	}

	private bool DoDelete(Node node, T item)
	{
		if (!node.Envelope.Contains(item.Envelope))
			return false;

		if (node.IsLeaf)
		{
			// manual compaction (same result as RemoveAll) without allocating a closure
			var items = node.Items;
			var write = 0;
			for (var read = 0; read < items.Count; read++)
			{
				var current = items[read];
				if (!_comparer.Equals((T)current, item))
					items[write++] = current;
			}

			var cnt = items.Count - write;
			if (cnt == 0)
				return false;

			items.RemoveRange(write, cnt);

			Count -= cnt;
			node.ResetEnvelope();
			return true;

		}

		var flag = false;
		foreach (var n in node.Items)
		{
			flag |= DoDelete((Node)n, item);
		}

		if (flag)
		{
			// Drop children that became empty so they are neither kept in
			// memory nor visited by later inserts and searches.
			_ = node.Items.RemoveAll(static n => ((Node)n).Items.Count == 0);
			node.ResetEnvelope();
		}

		return flag;
	}
}
