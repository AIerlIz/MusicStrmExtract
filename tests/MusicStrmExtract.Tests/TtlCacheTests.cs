using System;

using MusicStrmExtract.Caching;

using Xunit;

namespace MusicStrmExtract.Tests
{
    public class TtlCacheTests
    {
        [Fact]
        public void Set_OverCapacity_EvictsOldestOnly()
        {
            var cache = new TtlCache<string>(TimeSpan.FromMinutes(30), 2);

            cache.Set("a", "1");
            cache.Set("b", "2");
            cache.Set("c", "3");

            Assert.False(cache.TryGet("a", out _));
            Assert.True(cache.TryGet("b", out var b));
            Assert.True(cache.TryGet("c", out var c));
            Assert.Equal("2", b);
            Assert.Equal("3", c);
            Assert.Equal(2, cache.Count);
        }

        [Fact]
        public void ExpiredEntry_IsRemovedOnTryGet()
        {
            var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var cache = new TtlCache<string>(TimeSpan.FromMinutes(30), 10, () => now);

            cache.Set("a", "1");
            now = now.AddMinutes(31);

            Assert.False(cache.TryGet("a", out _));
            Assert.Equal(0, cache.Count);
        }

        [Fact]
        public void Set_ExpiresOldPrefixBeforeAddingFreshEntry()
        {
            var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var cache = new TtlCache<string>(TimeSpan.FromMinutes(30), 10, () => now);

            cache.Set("old", "1");
            now = now.AddMinutes(31);
            cache.Set("fresh", "2");

            Assert.False(cache.TryGet("old", out _));
            Assert.True(cache.TryGet("fresh", out var fresh));
            Assert.Equal("2", fresh);
            Assert.Equal(1, cache.Count);
        }
    }
}
