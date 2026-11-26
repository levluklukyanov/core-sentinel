using CoreSpectre.Services;
using Xunit;

namespace CoreSpectre.Tests;

public class GameBoostTests
{
    [Theory]
    [InlineData("cs2", "", false, true, "Counter-Strike 2")]
    [InlineData("dota2", "", false, true, "Dota 2")]
    [InlineData("javaw", "Minecraft 1.20.4", false, true, "Minecraft")]
    [InlineData("javaw", "Minecraft 1.20.4", true, true, "Майнкрафт")]
    [InlineData("cyberpunk2077", "", false, true, "Cyberpunk 2077")]
    [InlineData("gta5", "", false, true, "Grand Theft Auto V")]
    [InlineData("roblox", "", false, true, "Roblox")]
    [InlineData("terraria", "", false, true, "Terraria")]
    [InlineData("notepad", "Notes.txt", false, false, "")]
    [InlineData("notepad", "Unity Documentation", false, false, "")]
    [InlineData("javaw", "IntelliJ IDEA", false, false, "")]
    [InlineData("chrome", "Google - Chrome", false, false, "")]
    [InlineData("chrome", "Dota 2 - Twitch", false, false, "")]
    [InlineData("msedge", "Minecraft Wiki", false, false, "")]
    [InlineData("discord", "Counter-Strike 2 Voice", false, false, "")]
    [InlineData("explorer", "", false, false, "")]
    public void ResolveGameIdentity_CorrectlyIdentifiesProcesses(
        string processName, string windowTitle, bool isRu, bool expectedIsGame, string expectedDisplayName)
    {
        var (isGame, displayName) = GameBoostService.ResolveGameIdentity(processName, windowTitle, isRu);

        Assert.Equal(expectedIsGame, isGame);
        if (expectedIsGame)
        {
            Assert.Equal(expectedDisplayName, displayName);
        }
    }

    [Fact]
    public void ResolveGameIdentity_SupportsCustomGamesAndExclusions()
    {
        var custom = new[] { "valorant" };
        var excluded = new[] { "cs2" };

        var (isValGame, valName) = GameBoostService.ResolveGameIdentity("valorant", "VALORANT", false, custom, excluded);
        Assert.True(isValGame);
        Assert.Equal("VALORANT", valName);

        var (isCsGame, _) = GameBoostService.ResolveGameIdentity("cs2", "", false, custom, excluded);
        Assert.False(isCsGame);
    }

    [Theory]
    [InlineData("TLauncher", @"C:\Users\User\AppData\Roaming\.minecraft", "Game", true)]
    [InlineData("Minecraft", @"C:\Program Files\Minecraft Launcher", "Launcher", true)]
    [InlineData("PrismLauncher", @"C:\Tools\PrismLauncher\prismlauncher.exe", "Game", true)]
    [InlineData("Minecraft", @"C:\Games\Minecraft\Minecraft.exe", "Game", false)]
    [InlineData("Counter-Strike 2", @"D:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive\game\bin\win64\cs2.exe", "Steam", false)]
    public void IsMinecraftLauncher_ExcludesLaunchersFromInstalledGames(
        string name, string path, string category, bool expectedIsLauncher)
    {
        bool actual = GameBoostService.IsMinecraftLauncher(name, path, category);
        Assert.Equal(expectedIsLauncher, actual);
    }
}
