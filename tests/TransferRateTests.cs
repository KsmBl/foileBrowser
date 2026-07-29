using FoileBrowser.Services;

namespace FoileBrowser.Tests;

/// <summary>
/// The three numbers a transfer is judged by (PRD §6.3). The clock is passed in, so these are the
/// actual rules rather than a guess about how long a test took to run.
/// </summary>
[TestFixture]
public class TransferRateTests
{
    private const long MiB = 1024 * 1024;

    /// <summary>Feeds a steady rate for a number of seconds, sampling ten times a second.</summary>
    private static TransferRate Steady(long bytesPerSecond, double seconds, TransferRate? into = null)
    {
        var rate = into ?? new TransferRate();
        var start = rate.Samples.Count == 0 ? 0.0 : double.NaN;
        _ = start;

        for (var t = 0.1; t <= seconds + 1e-9; t += 0.1)
            rate.Observe((long)(bytesPerSecond * t), t);

        return rate;
    }

    [Test]
    public void Nothing_Observed_Yet_Claims_Nothing()
    {
        var rate = new TransferRate();

        Assert.Multiple(() =>
        {
            Assert.That(rate.Speed, Is.Zero);
            Assert.That(rate.Average, Is.Zero);
            Assert.That(rate.EtaFor(1000), Is.Null, "no rate to divide by yet");
            Assert.That(rate.Samples, Is.Empty);
        });
    }

    [Test]
    public void A_Steady_Transfer_Reports_That_Rate_Both_Ways()
    {
        var rate = Steady(10 * MiB, seconds: 5);

        Assert.Multiple(() =>
        {
            Assert.That(rate.Speed, Is.EqualTo(10.0 * MiB).Within(0.02 * 10 * MiB));
            Assert.That(rate.Average, Is.EqualTo(10.0 * MiB).Within(0.02 * 10 * MiB));
        });
    }

    [Test]
    public void The_Current_Speed_Follows_A_Slowdown_While_The_Average_Holds_Its_Head()
    {
        // The case the two numbers exist for: ten seconds fast, then it falls off a cliff. The
        // current figure has to notice; the average has to not panic.
        var rate = new TransferRate();
        for (var t = 0.1; t <= 10.0 + 1e-9; t += 0.1)
            rate.Observe((long)(100 * MiB * t), t);

        var fastAverage = rate.Average;
        var carried = 100 * MiB * 10;

        // Four more seconds at a tenth of the speed.
        for (var t = 10.1; t <= 14.0 + 1e-9; t += 0.1)
            rate.Observe((long)(carried + (10 * MiB * (t - 10))), t);

        Assert.Multiple(() =>
        {
            Assert.That(rate.Speed, Is.EqualTo(10.0 * MiB).Within(0.1 * 10 * MiB), "the window has moved on");
            Assert.That(rate.Average, Is.LessThan(fastAverage), "the average came down");
            Assert.That(rate.Average, Is.GreaterThan(50.0 * MiB), "but nowhere near the current rate");
        });
    }

    [Test]
    public void The_Estimate_Divides_What_Is_Left_By_The_Average()
    {
        var rate = Steady(1 * MiB, seconds: 4);

        // 8 MiB left at ~1 MiB/s.
        var eta = rate.EtaFor(8 * MiB);

        Assert.That(eta, Is.Not.Null);
        Assert.That(eta!.Value.TotalSeconds, Is.EqualTo(8).Within(0.5));
    }

    [Test]
    public void Nothing_Left_To_Move_Is_No_Time_At_All()
    {
        var rate = Steady(1 * MiB, seconds: 2);

        Assert.Multiple(() =>
        {
            Assert.That(rate.EtaFor(0), Is.EqualTo(TimeSpan.Zero));
            Assert.That(rate.EtaFor(-1), Is.EqualTo(TimeSpan.Zero), "a total that under-counted");
        });
    }

    [Test]
    public void The_Graph_History_Is_Bounded_And_Keeps_The_Recent_End()
    {
        // Far more samples than the ring holds; it must not grow, and what it keeps is the tail.
        var rate = new TransferRate();
        for (var t = 0.25; t <= 200.0; t += 0.25)
            rate.Observe((long)(5 * MiB * t), t);

        Assert.Multiple(() =>
        {
            Assert.That(rate.Samples, Has.Count.EqualTo(TransferRate.Capacity));
            Assert.That(rate.Peak, Is.GreaterThan(0));
            Assert.That(rate.Samples[^1], Is.EqualTo(5.0 * MiB).Within(0.05 * 5 * MiB));
        });
    }

    [Test]
    public void Samples_Are_Spaced_Out_However_Often_The_Engine_Reports()
    {
        // The copy engine reports per block, which can be thousands of times a second. The graph is
        // a picture of time, not of how chatty the engine is.
        var rate = new TransferRate();
        for (var i = 1; i <= 10_000; ++i)
            rate.Observe(i * 1024, i * 0.0001); // one second of very frequent reports

        Assert.That(rate.Samples.Count, Is.LessThanOrEqualTo(6), "about four a second, not ten thousand");
    }

    [Test]
    public void A_Clock_That_Goes_Backwards_Is_Ignored_Rather_Than_Dividing_By_Nothing()
    {
        var rate = Steady(1 * MiB, seconds: 2);
        var before = rate.Average;

        rate.Observe(1, 0.5); // stale report arriving late

        Assert.That(rate.Average, Is.EqualTo(before));
    }
}
