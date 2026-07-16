using FoileBrowser.Services;

namespace FoileBrowser.Tests;

[TestFixture]
public class LruCacheTests
{
    [Test]
    public void Get_Returns_Stored_Value()
    {
        var cache = new LruCache<string, long>(4);
        cache.Set("a", 10);

        Assert.That(cache.TryGet("a", out var value), Is.True);
        Assert.That(value, Is.EqualTo(10));
    }

    [Test]
    public void Miss_Returns_False()
    {
        var cache = new LruCache<string, long>(4);
        Assert.That(cache.TryGet("nope", out _), Is.False);
    }

    [Test]
    public void Evicts_Least_Recently_Used_When_Full()
    {
        var cache = new LruCache<string, int>(2);
        cache.Set("a", 1);
        cache.Set("b", 2);

        _ = cache.TryGet("a", out _); // touch "a" so "b" is now least-recently-used
        cache.Set("c", 3);            // over capacity → evict "b"

        Assert.That(cache.TryGet("a", out _), Is.True);
        Assert.That(cache.TryGet("c", out _), Is.True);
        Assert.That(cache.TryGet("b", out _), Is.False, "b was the LRU entry and should be evicted");
        Assert.That(cache.Count, Is.EqualTo(2));
    }

    [Test]
    public void Set_Updates_Existing_Without_Growing()
    {
        var cache = new LruCache<string, int>(2);
        cache.Set("a", 1);
        cache.Set("a", 99);

        Assert.That(cache.TryGet("a", out var v), Is.True);
        Assert.That(v, Is.EqualTo(99));
        Assert.That(cache.Count, Is.EqualTo(1));
    }

    [Test]
    public void Zero_Capacity_Is_Rejected()
        => Assert.Throws<ArgumentOutOfRangeException>(() => _ = new LruCache<string, int>(0));
}
