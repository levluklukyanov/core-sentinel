using CoreSentinel.Services;
using Xunit;

namespace CoreSentinel.Tests;

public class GameSentinelTests
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
    [InlineData("chrome", "Google - Chrome", false, false, "")]
    [InlineData("explorer", "", false, false, "")]
    public void ResolveGameIdentity_CorrectlyIdentifiesProcesses(
        string processName, string windowTitle, bool isRu, bool expectedIsGame, string expectedDisplayName)
    {
        var (isGame, displayName) = GameSentinelService.ResolveGameIdentity(processName, windowTitle, isRu);

        Assert.Equal(expectedIsGame, isGame);
        if (expectedIsGame)
        {
            Assert.Equal(expectedDisplayName, displayName);
        }
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
        bool actual = GameSentinelService.IsMinecraftLauncher(name, path, category);
        Assert.Equal(expectedIsLauncher, actual);
    }
}
