using System;
using System.Threading;
using System.Threading.Tasks;

using MusicStrmExtract.Online;

using Xunit;

namespace MusicStrmExtract.Tests
{
    public class RequestRateLimiterTests
    {
        [Fact]
        public async Task Acquire_HoldsExclusiveLeaseUntilDisposed()
        {
            var limiter = new RequestRateLimiter(TimeSpan.Zero, () => DateTime.UtcNow);

            using var first = await limiter.AcquireAsync(CancellationToken.None);
            var second = limiter.AcquireAsync(CancellationToken.None);
            await Task.Delay(50);
            Assert.False(second.IsCompleted);

            first.Dispose();

            await second;
        }
    }
}
