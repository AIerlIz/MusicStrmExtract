using System.Collections.Generic;
using System.Threading;

using MusicStrmExtract.Ui;

using Xunit;

namespace MusicStrmExtract.Tests
{
    public class RepairJobRunnerTests
    {
        [Fact]
        public async Task TryStart_ReportsProgressAndFinalResult()
        {
            var messages = new List<string>();
            var runner = new RepairJobRunner(
                (progress, _) =>
                {
                    progress?.Report("working");
                    return "done";
                },
                messages.Add);

            Assert.True(runner.TryStart());
            await runner.Completion!;

            Assert.Equal(new[] { "修复已开始，正在读取媒体库...", "working", "done" }, messages);
        }

        [Fact]
        public async Task TryStart_RejectsConcurrentRunUntilPreviousCompletes()
        {
            var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var runner = new RepairJobRunner(
                (_, _) =>
                {
                    firstStarted.SetResult();
                    releaseFirst.Task.GetAwaiter().GetResult();
                    return "done";
                },
                _ => { });

            Assert.True(runner.TryStart());
            await firstStarted.Task;
            Assert.False(runner.TryStart());

            releaseFirst.SetResult();
            await runner.Completion!;
            Assert.True(runner.TryStart());
        }
    }
}
