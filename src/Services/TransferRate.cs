namespace FoileBrowser.Services;

/// <summary>
/// Turns a running byte count into the three numbers a transfer is judged by (PRD §6.3): what it is
/// doing right now, what it has averaged, and how much longer it has to go — plus the recent history
/// the progress window draws as a graph.
/// </summary>
/// <remarks>
/// <para>
/// Two speeds, because they answer different questions and disagree in exactly the interesting
/// cases. The current speed is measured over a short trailing window, so it drops the moment a
/// transfer hits a slow device or a wall of tiny files; the average is over the whole run, so it
/// keeps its head while the current figure swings. A single number would either be too jittery to
/// read or too slow to tell you anything had changed.
/// </para>
/// <para>
/// The clock is passed in rather than read here, so the rules are testable without waiting in real
/// time for them to happen.
/// </para>
/// </remarks>
public sealed class TransferRate
{
    /// <summary>How far back the "right now" speed looks.</summary>
    private const double WindowSeconds = 3.0;

    /// <summary>Minimum spacing between graph samples, so a fast transfer does not flood the ring.</summary>
    private const double SampleSeconds = 0.25;

    /// <summary>How many samples the graph keeps — at the spacing above, half a minute of history.</summary>
    public const int Capacity = 120;

    private readonly Queue<(double At, long Bytes)> _window = new();
    private readonly double[] _samples = new double[Capacity];

    private int _sampleCount;
    private int _sampleHead;
    private double _lastSampleAt = double.NegativeInfinity;
    private double _at;
    private long _bytes;

    /// <summary>Bytes per second over the last few seconds, or 0 before there is a span to divide by.</summary>
    public double Speed { get; private set; }

    /// <summary>Bytes per second since the first observation.</summary>
    public double Average => _at > 0 ? _bytes / _at : 0;

    /// <summary>The graph history, oldest first. Empty until the first sample lands.</summary>
    public IReadOnlyList<double> Samples
    {
        get
        {
            var result = new double[_sampleCount];
            for (var i = 0; i < _sampleCount; ++i)
                result[i] = _samples[(_sampleHead - _sampleCount + i + Capacity) % Capacity];

            return result;
        }
    }

    /// <summary>The largest sample held, so a graph can scale itself. 0 when there are none.</summary>
    public double Peak
    {
        get
        {
            var peak = 0.0;
            for (var i = 0; i < _sampleCount; ++i)
                peak = Math.Max(peak, _samples[(_sampleHead - _sampleCount + i + Capacity) % Capacity]);

            return peak;
        }
    }

    /// <summary>
    /// Records the running total at a moment. <paramref name="atSeconds"/> is time since the
    /// operation began; going backwards or repeating a moment is ignored rather than dividing by
    /// zero.
    /// </summary>
    public void Observe(long bytesDone, double atSeconds)
    {
        if (atSeconds < _at || bytesDone < _bytes)
            return;

        _at = atSeconds;
        _bytes = bytesDone;

        _window.Enqueue((atSeconds, bytesDone));

        // Keep one sample older than the window so the span covers it rather than stopping short.
        while (_window.Count > 2 && _window.Peek().At < atSeconds - WindowSeconds)
            _window.Dequeue();

        var oldest = _window.Peek();
        var span = atSeconds - oldest.At;
        if (span > 0)
            Speed = (bytesDone - oldest.Bytes) / span;

        if (atSeconds - _lastSampleAt < SampleSeconds)
            return;

        _lastSampleAt = atSeconds;
        _samples[_sampleHead] = Speed;
        _sampleHead = (_sampleHead + 1) % Capacity;
        _sampleCount = Math.Min(_sampleCount + 1, Capacity);
    }

    /// <summary>
    /// How long the remaining bytes should take. Uses the average rather than the current speed,
    /// because an estimate that jumps every time one large file gives way to a hundred small ones is
    /// not an estimate anyone can plan around. Null until there is a rate to divide by.
    /// </summary>
    public TimeSpan? EtaFor(long bytesRemaining)
    {
        if (bytesRemaining <= 0)
            return TimeSpan.Zero;

        var rate = Average;
        if (rate <= 0)
            return null;

        var seconds = bytesRemaining / rate;
        return seconds > TimeSpan.MaxValue.TotalSeconds ? null : TimeSpan.FromSeconds(seconds);
    }
}
