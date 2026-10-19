using Vigil.Core;

namespace Vigil.Tests;

public class BackupWifiTests
{
    private const string Home = "Maple Street WiFi";
    private const string Phone = "Sam’s iPhone 11";
    private const string Friend = "iPhone (2)";
    private static readonly DateTime T0 = new(2026, 10, 17, 2, 0, 0);
    private static readonly string[] Backups = { Phone };

    [Fact]
    public void Picks_saved_networks_that_look_like_phones()
    {
        var saved = new[] { "Linksys-5G", Phone, "CafeGuest", "Pixel 8", "Galaxy S24 hotspot", "Unit 12" };
        Assert.Equal(new[] { Phone, "Pixel 8", "Galaxy S24 hotspot" }, BackupWifiPolicy.Backups(saved, ""));
        Assert.Equal(new[] { "CafeGuest" }, BackupWifiPolicy.Backups(saved, "CafeGuest"));
        // A chosen network that isn't saved anymore falls back to the automatic pick.
        Assert.Equal(new[] { Phone, "Pixel 8", "Galaxy S24 hotspot" }, BackupWifiPolicy.Backups(saved, "Gone"));
    }

    [Fact]
    public void Matches_the_network_lists_numbered_names_to_saved_profiles()
    {
        var saved = new[] { "Maple Street WiFi", Phone, "iPhone (2)" };
        Assert.Equal("Maple Street WiFi", BackupWifiPolicy.Resolve("Maple Street WiFi 2", saved));
        Assert.Equal(Phone, BackupWifiPolicy.Resolve(Phone, saved));
        Assert.True(BackupWifiPolicy.Same("Sam’s iPhone 11 2", Phone));
        Assert.False(BackupWifiPolicy.Same("iPhone 12", "iPhone 11"));
        Assert.Null(BackupWifiPolicy.Resolve("Somewhere Else", saved));
    }

    [Fact]
    public void Waits_out_a_blip_then_joins_the_hotspot()
    {
        var p = new BackupWifiPolicy();
        p.Step(true, true, Home, Backups, T0);
        Assert.Equal(WifiStep.None, p.Step(true, false, null, Backups, T0.AddSeconds(10)));
        Assert.Equal(WifiStep.None, p.Step(true, false, null, Backups, T0.AddSeconds(25)));
        Assert.Equal(new WifiStep(WifiAction.Join, Phone), p.Step(true, false, null, Backups, T0.AddSeconds(31)));
        Assert.Equal(Home, p.Home);
    }

    [Fact]
    public void Does_nothing_when_Claude_isnt_working()
    {
        var p = new BackupWifiPolicy();
        Assert.Equal(WifiStep.None, p.Step(false, false, null, Backups, T0));
        Assert.Equal(WifiStep.None, p.Step(false, false, null, Backups, T0.AddMinutes(5)));
    }

    [Fact]
    public void Goes_back_home_once_the_work_is_done()
    {
        var p = OnHotspot();
        Assert.Equal(WifiStep.None, p.Step(true, true, Phone, Backups, T0.AddSeconds(40)));
        Assert.Equal(new WifiStep(WifiAction.Return, Home), p.Step(false, true, Phone, Backups, T0.AddMinutes(30)));
        Assert.True(p.Returning);
        // Made it home.
        Assert.Equal(WifiStep.None, p.Step(false, true, Home, Backups, T0.AddMinutes(30.2)));
        Assert.False(p.Returning);
        Assert.Null(p.Joined);
    }

    [Fact]
    public void If_home_isnt_in_range_it_rejoins_the_hotspot_and_waits_before_trying_again()
    {
        var p = OnHotspot();
        var back = T0.AddMinutes(30);
        Assert.Equal(WifiAction.Return, p.Step(false, true, Phone, Backups, back).Action);
        // Home didn't connect: offline.
        Assert.Equal(WifiStep.None, p.Step(false, false, null, Backups, back.AddSeconds(10)));
        Assert.Equal(new WifiStep(WifiAction.Join, Phone), p.Step(false, false, null, Backups, back.AddSeconds(21)));
        // Back on the hotspot; no new trip home for a while.
        Assert.Equal(WifiStep.None, p.Step(false, true, Phone, Backups, back.AddMinutes(5)));
        Assert.Equal(new WifiStep(WifiAction.Return, Home), p.Step(false, true, Phone, Backups, back.AddMinutes(16)));
    }

    [Fact]
    public void Tries_each_saved_hotspot_then_says_so_once()
    {
        var p = new BackupWifiPolicy();
        var two = new[] { Phone, Friend };
        p.Step(true, false, null, two, T0);
        Assert.Equal(new WifiStep(WifiAction.Join, Phone), p.Step(true, false, null, two, T0.AddSeconds(21)));
        Assert.Equal(new WifiStep(WifiAction.Join, Friend), p.Step(true, false, null, two, T0.AddSeconds(82)));
        Assert.Equal(new WifiStep(WifiAction.NotInRange, Phone), p.Step(true, false, null, two, T0.AddSeconds(143)));
        // Keeps trying quietly.
        Assert.Equal(new WifiStep(WifiAction.Join, Phone), p.Step(true, false, null, two, T0.AddSeconds(204)));
    }

    [Fact]
    public void Lets_go_if_you_switch_networks_yourself()
    {
        var p = OnHotspot();
        p.Step(true, true, "CafeGuest", Backups, T0.AddMinutes(2));
        Assert.Null(p.Joined);
        Assert.Equal(WifiStep.None, p.Step(false, true, "CafeGuest", Backups, T0.AddMinutes(30)));
    }

    private static BackupWifiPolicy OnHotspot()
    {
        var p = new BackupWifiPolicy();
        p.Step(true, true, Home, Backups, T0);
        p.Step(true, false, null, Backups, T0.AddSeconds(1));
        Assert.Equal(WifiAction.Join, p.Step(true, false, null, Backups, T0.AddSeconds(30)).Action);
        return p;
    }
}
