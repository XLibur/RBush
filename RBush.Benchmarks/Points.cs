#pragma warning disable CA1815, MA0048
namespace RBush.Benchmarks;

public sealed class PointC(double x, double y) : ISpatialData
{
	private readonly Envelope _envelope = new(x, y, x, y);
	public ref readonly Envelope Envelope => ref _envelope;
}

// A struct cannot return a ref to its own field (CS8170), so a struct T
// has to point at storage elsewhere. This wrapper is enough to measure the
// boxing cost of storing struct items in List<ISpatialData>.
public readonly struct PointS(PointC inner) : ISpatialData
{
	public ref readonly Envelope Envelope => ref inner.Envelope;
}
