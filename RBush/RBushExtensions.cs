using System.Runtime.InteropServices;

namespace RBush;

/// <summary>
/// Extension methods for the <see cref="RBush{T}"/> object.
/// </summary>
public static class RBushExtensions
{
	[StructLayout(LayoutKind.Auto)]
	private readonly record struct Candidate<T>(T Item, double Distance, int Index);

	// Orders by distance, then by position in the search result, which is the
	// same (stable) order the previous OrderBy-based implementation produced.
	private struct CandidateComparer<T> : IComparer<Candidate<T>>
	{
		public readonly int Compare(Candidate<T> x, Candidate<T> y)
		{
			var c = x.Distance.CompareTo(y.Distance);
			return c != 0 ? c : x.Index.CompareTo(y.Index);
		}
	}

	/// <summary>
	/// Get the <paramref name="k"/> nearest neighbors to a specific point.
	/// </summary>
	/// <typeparam name="T">The type of elements in the index.</typeparam>
	/// <param name="tree">An index of points.</param>
	/// <param name="k">The number of points to retrieve.</param>
	/// <param name="x">The x-coordinate of the center point.</param>
	/// <param name="y">The y-coordinate of the center point.</param>
	/// <param name="maxDistance">The maximum distance of points to be considered "near"; optional.</param>
	/// <param name="predicate">A function to test each element for a condition; optional.</param>
	/// <remarks>
	/// Results are ordered nearest first; elements at equal distance appear in the order
	/// <c>Search()</c> returns them. <paramref name="predicate"/> is only called for elements
	/// that could be part of the result, possibly in a different order than the tree stores them.
	/// </remarks>
	/// <returns>The list of up to <paramref name="k"/> elements nearest to the given point.</returns>
	public static IReadOnlyList<T> Knn<T>(
		this ISpatialIndex<T> tree,
		int k,
		double x,
		double y,
		double? maxDistance = null,
		Func<T, bool>? predicate = null)
		where T : ISpatialData
	{
		ArgumentNullException.ThrowIfNull(tree);

		// Fast path: a best-first walk of the tree visits roughly log n + k nodes
		// instead of every item in the window.
		if (tree is RBush<T> rbush && rbush.KnnSearch(k, x, y, maxDistance, predicate) is { } nearest)
			return nearest;

		var items = maxDistance == null
			? tree.Search()
			: tree.Search(
				new Envelope(
					MinX: x - maxDistance.Value,
					MinY: y - maxDistance.Value,
					MaxX: x + maxDistance.Value,
					MaxY: y + maxDistance.Value));

		return SelectNearest(items, k, x, y, maxDistance, predicate);
	}

	// Scans the items once, keeping the nearest k (or all of them when k <= 0), and
	// returns them nearest first. Equal distances keep their order in `items`.
	private static List<T> SelectNearest<T>(
		IReadOnlyList<T> items,
		int k,
		double x,
		double y,
		double? maxDistance,
		Func<T, bool>? predicate)
		where T : ISpatialData
	{
		var bounded = k > 0;
		var capacity = bounded ? Math.Min(k, items.Count) : items.Count;
		if (capacity == 0)
			return [];

		// bounded: a max-heap holding the best `capacity` candidates so far, so the
		// worst of them is always at index 0. unbounded (k <= 0): collect everything.
		var buffer = new Candidate<T>[capacity];
		var size = 0;

		for (var index = 0; index < items.Count; index++)
		{
			var item = items[index];
			var distance = item.Envelope.DistanceTo(x, y);

			if (maxDistance.HasValue && !(distance <= maxDistance.Value))
				continue;

			// Candidates arrive in increasing index order, so a candidate that ties
			// the current worst on distance loses to it (stable ordering).
			if (bounded && size == capacity && distance.CompareTo(buffer[0].Distance) >= 0)
				continue;

			if (predicate != null && !predicate(item))
				continue;

			var candidate = new Candidate<T>(item, distance, index);
			if (!bounded)
			{
				buffer[size++] = candidate;
			}
			else if (size < capacity)
			{
				buffer[size] = candidate;
				SiftUp(buffer, size);
				size++;
			}
			else
			{
				buffer[0] = candidate;
				SiftDown(buffer, size);
			}
		}

		buffer.AsSpan(0, size).Sort(new CandidateComparer<T>());

		var result = new List<T>(size);
		for (var i = 0; i < size; i++)
			result.Add(buffer[i].Item);
		return result;
	}

	private static bool IsWorse<T>(in Candidate<T> a, in Candidate<T> b)
	{
		var c = a.Distance.CompareTo(b.Distance);
		return c > 0 || (c == 0 && a.Index > b.Index);
	}

	private static void SiftUp<T>(Candidate<T>[] heap, int i)
	{
		while (i > 0)
		{
			var parent = (i - 1) / 2;
			if (!IsWorse(in heap[i], in heap[parent]))
				break;

			(heap[i], heap[parent]) = (heap[parent], heap[i]);
			i = parent;
		}
	}

	private static void SiftDown<T>(Candidate<T>[] heap, int size)
	{
		var i = 0;
		while (true)
		{
			var left = (2 * i) + 1;
			if (left >= size)
				return;

			var worst = left;
			var right = left + 1;
			if (right < size && IsWorse(in heap[right], in heap[left]))
				worst = right;

			if (!IsWorse(in heap[worst], in heap[i]))
				return;

			(heap[i], heap[worst]) = (heap[worst], heap[i]);
			i = worst;
		}
	}

	/// <summary>
	/// Calculates the distance from the borders of an <see cref="Envelope"/>
	/// to a given point.
	/// </summary>
	/// <param name="envelope">The <see cref="Envelope"/> from which to find the distance</param>
	/// <param name="x">The x-coordinate of the given point</param>
	/// <param name="y">The y-coordinate of the given point</param>
	/// <returns>The calculated Euclidean shortest distance from the <paramref name="envelope"/> to a given point.</returns>
	public static double DistanceTo(this in Envelope envelope, double x, double y)
	{
		var dX = AxisDistance(x, envelope.MinX, envelope.MaxX);
		var dY = AxisDistance(y, envelope.MinY, envelope.MaxY);
		return Math.Sqrt((dX * dX) + (dY * dY));

		static double AxisDistance(double p, double min, double max) =>
		   p < min ? min - p :
		   p > max ? p - max :
		   0;
	}
}
