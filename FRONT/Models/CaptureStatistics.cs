namespace DigitalBrain.UI.Models;

/// <summary>
/// Acumula muestras válidas y descartadas de una sesión, en total y por canal.
/// Es seguro llamarla desde el hilo de lectura y consultarla desde la interfaz.
/// </summary>
public sealed class CaptureStatistics
{
    private readonly object _lock = new();
    private readonly long[] _unsaturatedPerChannel = new long[4];
    private long _total;
    private long _valid;

    public void Register(SignalSample sample)
    {
        lock (_lock)
        {
            _total++;
            if (!SignalValidity.IsSaturated(sample.Channel1)) _unsaturatedPerChannel[0]++;
            if (!SignalValidity.IsSaturated(sample.Channel2)) _unsaturatedPerChannel[1]++;
            if (!SignalValidity.IsSaturated(sample.Channel3)) _unsaturatedPerChannel[2]++;
            if (!SignalValidity.IsSaturated(sample.Channel4)) _unsaturatedPerChannel[3]++;
            if (!sample.IsSaturated) _valid++;
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _total = 0;
            _valid = 0;
            Array.Clear(_unsaturatedPerChannel);
        }
    }

    public CaptureSnapshot Snapshot()
    {
        lock (_lock)
        {
            var ratios = _unsaturatedPerChannel
                .Select(count => _total == 0 ? (double?)null : (double)count / _total)
                .ToArray();
            return new CaptureSnapshot(_total, _valid, _total - _valid, ratios);
        }
    }
}

/// <param name="ChannelValidRatios">Fracción de muestras sin saturar por canal; <c>null</c> si aún no hay muestras.</param>
public sealed record CaptureSnapshot(long Total, long Valid, long Discarded, IReadOnlyList<double?> ChannelValidRatios)
{
    public static CaptureSnapshot Empty { get; } = new(0, 0, 0, [null, null, null, null]);
    public int Windows => AcquisitionParameters.WindowCount(Valid);
}
