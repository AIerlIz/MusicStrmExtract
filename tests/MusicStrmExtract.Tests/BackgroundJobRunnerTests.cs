using System.Collections.Generic;
using System.Threading;

using MusicStrmExtract.Ui;

using Xunit;

namespace MusicStrmExtract.Tests
{
    public class BackgroundJobRunnerTests
    {
        [Fact]
        public async Task TryStart_ReportsProgressAndFinalResult()
        {
            var messages = new List<string>();
            var runner = new BackgroundJobRunner(
                (progress, _) =>
                {
                    progress?.Report("working");
                    return Task.FromResult("done");
                },
                messages.Add,
                "操作失败");

            Assert.True(runner.TryStart("开始"));
            await runner.Completion!;

            Assert.Equal(new[] { "开始", "working", "done" }, messages);
        }

        [Fact]
        public async Task TryStart_RejectsConcurrentRunUntilPreviousCompletes()
        {
            var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var runner = new BackgroundJobRunner(
                (_, _) =>
                {
                    firstStarted.SetResult();
                    releaseFirst.Task.GetAwaiter().GetResult();
                    return Task.FromResult("done");
                },
                _ => { },
                "操作失败");

            Assert.True(runner.TryStart("开始"));
            await firstStarted.Task;
            Assert.False(runner.TryStart("再次开始"));

            releaseFirst.SetResult();
            await runner.Completion!;
            Assert.True(runner.TryStart("再次开始"));
        }
    }
}
