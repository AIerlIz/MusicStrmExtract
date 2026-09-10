using System.Collections.Generic;

using MediaBrowser.Controller.Entities.Audio;

using MusicStrmExtract.Ui;

using Xunit;

namespace MusicStrmExtract.Tests
{
    public class RepairPlanBuilderTests
    {
        [Fact]
        public void Build_DeletesOnlyUnreferencedStaleAlbumAndQueuesRelatedStrm()
        {
            var staleReferenced = new MusicAlbum
            {
                Name = "Stale Referenced",
                Path = null,
                InternalId = 100
            };
            var staleUnreferenced = new MusicAlbum
            {
                Name = "Stale Unreferenced",
                Path = null,
                InternalId = 200
            };
            var audio = new Audio
            {
                Name = "Track",
                Path = @"C:\music\Album\01 - Track.strm",
                InternalAlbumId = staleReferenced.InternalId
            };

            var plan = RepairPlanBuilder.Build(
                [audio],
                [staleReferenced, staleUnreferenced]);

            Assert.Equal([staleUnreferenced], plan.AlbumsToDelete);
            Assert.Equal([audio], plan.StrmToRefresh);
        }

        [Fact]
        public void Build_QueuesMissingAlbumLinkWhenStrmHasReleaseMbid()
        {
            var audio = new Audio
            {
                Name = "Track",
                Path = @"C:\music\Album\01 - Track.strm"
            };
            audio.ProviderIds[PluginConstants.MusicBrainzAlbum] = "release-1";

            var plan = RepairPlanBuilder.Build([audio], []);

            Assert.Empty(plan.AlbumsToDelete);
            Assert.Equal([audio], plan.StrmToRefresh);
        }
    }
}
