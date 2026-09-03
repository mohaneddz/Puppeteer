using Puppeteer.Core;
namespace Puppeteer.Core.Tests;
public sealed class IconRankingTests
{
 [Fact] public void SquareNamedIconOutranksBanner(){var icon=IconCandidateRanker.Score("project/assets/icon.png",256,256);var banner=IconCandidateRanker.Score("project/assets/screenshot.png",1200,400);Assert.True(icon>banner);}
 [Fact] public void TauriIconFolderReceivesPriority()=>Assert.True(IconCandidateRanker.Score("project/src-tauri/icons/app.png",128,128)>IconCandidateRanker.Score("project/misc/app.png",128,128));
}
