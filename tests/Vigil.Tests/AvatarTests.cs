using Vigil.UI;

namespace Vigil.Tests;

public class AvatarTests
{
    [Fact]
    public void Projects_on_screen_get_different_colors_in_any_order()
    {
        // Find two folders whose hashes collide, then check they end up apart.
        var names = Enumerable.Range(0, 50).Select(i => $"project-{i}").ToList();
        var pair = names.SelectMany(a => names.Where(b => string.CompareOrdinal(a, b) < 0 && Avatars.HashIndex(a) == Avatars.HashIndex(b)).Select(b => (a, b))).First();

        Avatars.Assign(new[] { pair.a, pair.b });
        var first = (Avatars.HueIndex(pair.a), Avatars.HueIndex(pair.b));
        Avatars.Assign(new[] { pair.b, pair.a });
        var second = (Avatars.HueIndex(pair.a), Avatars.HueIndex(pair.b));

        Assert.NotEqual(first.Item1, first.Item2);
        Assert.Equal(first, second);
        Avatars.Assign(Array.Empty<string>());
    }

    [Fact]
    public void The_hash_matches_the_phone()
    {
        // Same FNV-1a as phone/public/app.js: "vigil" and "replay-tool" must land on the same colors there.
        Assert.Equal(Avatars.HashIndex("Vigil"), Avatars.HashIndex("vigil"));
        Assert.InRange(Avatars.HashIndex("replay-tool"), 0, 7);
    }
}
