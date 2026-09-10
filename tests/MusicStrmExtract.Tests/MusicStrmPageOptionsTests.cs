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
            Assert.Equal("MusicBrainz", options.MusicBrainzCaption.Caption);
            Assert.Equal("维护", options.MaintenanceCaption.Caption);
            Assert.Contains("刷新相关", options.RepairButton.ConfirmationPrompt, System.StringComparison.Ordinal);
        }
    }
}
