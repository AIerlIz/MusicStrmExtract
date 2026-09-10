using MusicStrmExtract.Ui;

using Xunit;

namespace MusicStrmExtract.Tests
{
    public class MusicStrmPageOptionsTests
    {
        [Fact]
        public void Constructor_UsesLegacyAlbumRelationRepairCommand()
        {
            var options = new MusicStrmPageOptions();

            Assert.Equal("RepairLegacyAlbumRelations", MusicStrmPageOptions.RepairCommand);
            Assert.Equal("修复旧库专辑关系", options.RepairButton.Caption);
            Assert.Equal(MusicStrmPageOptions.RepairCommand, options.RepairButton.CommandId);
            Assert.Contains("无路径", options.RepairButton.ConfirmationPrompt, System.StringComparison.Ordinal);
            Assert.Equal(MusicStrmPageOptions.ClearCacheCommand, options.ClearCacheButton.CommandId);
            Assert.Equal(MusicStrmPageOptions.CheckSourceCommand, options.CheckSourceButton.CommandId);
            Assert.Equal(MusicStrmPageOptions.RefreshDiagnosticsCommand, options.RefreshDiagnosticsButton.CommandId);
        }
    }
}
