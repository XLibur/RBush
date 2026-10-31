using System.Numerics;
using System.Runtime.InteropServices;

namespace RBush;

public partial class RBush<T>
{
	// Heaps larger than this are not kept for reuse, so one huge query does not
	// pin memory for the life of the tree.
	private const int MaxRetainedHeapSize = 4096;

	// An entry in the best-first queue: a node or an item, with the distance from
	// the query point to its envelope and its position in depth-first order.
	[StructLayout(LayoutKind.Auto)]
	private readonly record struct KnnEntry(double Distance, ulong Path, ISpatialData Data);

	// Reused by Knn queries. A query takes it with Interlocked.Exchange and puts it
	// back when done; a concurrent query that finds it taken allocates its own.
	private KnnEntry[]? _knnHeap;

	/// <summary>
	/// Finds the nearest items to a point by walking the tree best-first.
	/// </summary>
	/// <returns>
	/// The items nearest first (equal distances in <c>Search()</c> order), or
	/// <see langword="null"/> when the tree is too deep to encode a position,
	/// in which case the caller must use another strategy.
	/// </returns>
	internal List<T>? KnnSearch(int k, double x, double y, double? maxDistance, Func<T, bool>? predicate)
	{
		if (Root.Items.Count == 0)
			return [];

		// Each level of the tree gets `bits` bits of a 64-bit position key, holding the
		// child index + 1. Comparing keys then equals comparing positions in a
		// depth-first walk (a parent sorts before its children), which is the
		// order Search() returns items in.
		var bits = BitOperations.Log2((uint)_maxEntries + 1) + 1;
		var rootHeight = Root.Height;
		if (bits * rootHeight > 64)
			return null;

		var heap = Interlocked.Exchange(ref _knnHeap, value: null) ?? new KnnEntry[32];
		var size = 0;
		var result = new List<T>(k > 0 ? Math.Min(k, Count) : Count);

		Push(ref heap, ref size, new KnnEntry(Root.Envelope.DistanceTo(x, y), 0, Root));

		while (size > 0 && (k <= 0 || result.Count < k))
		{
			var entry = Pop(heap, ref size);
			if (entry.Data is not Node node)
			{
				var item = (T)entry.Data;
				if (predicate == null || predicate(item))
					result.Add(item);
				continue;
			}

			var shift = 64 - (bits * (rootHeight - node.Height + 1));
			var items = node.Items;
			for (var i = 0; i < items.Count; i++)
			{
				var child = items[i];
				var distance = child.Envelope.DistanceTo(x, y);
				if (maxDistance.HasValue && !(distance <= maxDistance.Value))
					continue;

				var path = entry.Path | ((ulong)(i + 1) << shift);
				Push(ref heap, ref size, new KnnEntry(distance, path, child));
			}
		}

		Array.Clear(heap, 0, size);
		if (heap.Length <= MaxRetainedHeapSize)
			_knnHeap = heap;
		return result;
	}

	private static bool Precedes(in KnnEntry a, in KnnEntry b)
	{
		var c = a.Distance.CompareTo(b.Distance);
		return c < 0 || (c == 0 && a.Path < b.Path);
	}

	private static void Push(ref KnnEntry[] heap, ref int size, in KnnEntry entry)
	{
		if (size == heap.Length)
			Array.Resize(ref heap, size * 2);

		var i = size++;
		while (i > 0)
		{
			var parent = (i - 1) / 2;
			if (!Precedes(in entry, in heap[parent]))
				break;

			heap[i] = heap[parent];
			i = parent;
		}

		heap[i] = entry;
	}

	private static KnnEntry Pop(KnnEntry[] heap, ref int size)
	{
		var top = heap[0];
		var last = heap[--size];
		heap[size] = default;
		if (size == 0)
			return top;

		var i = 0;
		while (true)
		{
			var child = (2 * i) + 1;
			if (child >= size)
				break;

			if (child + 1 < size && Precedes(in heap[child + 1], in heap[child]))
				child++;

			if (!Precedes(in heap[child], in last))
				break;

			heap[i] = heap[child];
			i = child;
		}

		heap[i] = last;
		return top;
	}
}
