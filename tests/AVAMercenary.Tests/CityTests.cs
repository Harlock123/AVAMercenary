using AVAMercenary.Engine;

namespace AVAMercenary.Tests;

public class CityTests
{
    [Fact]
    public void The_same_seed_builds_the_same_city_and_another_seed_another()
    {
        var a = CityBook.Build(3);
        var b = CityBook.Build(3);
        var c = CityBook.Build(4);
        Assert.Equal(a.Buildings, b.Buildings);
        Assert.Equal(a.Towers, b.Towers);
        Assert.NotEqual(a.Buildings, c.Buildings);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    public void Ten_towers_stand_in_the_open_well_apart(int seed)
    {
        var c = CityBook.Build(seed);
        Assert.Equal(10, c.Towers.Count);
        foreach (var t in c.Towers)
        {
            Assert.Equal(0, c.Roof(t.X, t.Y));
            Assert.InRange(t.H, 110, 170);
            Assert.All(c.Towers.Where(o => o != t), o => Assert.True(Math.Abs(o.X - t.X) + Math.Abs(o.Y - t.Y) >= 1200));
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void The_pads_the_depots_and_the_crash_site_are_open_ground(int seed)
    {
        var c = CityBook.Build(seed);
        Assert.Equal(6, c.Depots.Count);
        foreach (var (x, y) in c.Depots) Assert.Equal(0, c.Roof(x, y));
        Assert.Equal(0, c.Roof(c.Palyar.X, c.Palyar.Y - 190));
        Assert.Equal(0, c.Roof(c.Mechanoid.X, c.Mechanoid.Y - 190));
        Assert.Equal(0, c.Roof(c.Spaceport.X, c.Spaceport.Y));
        Assert.Equal(0, c.Roof(c.Crash.X, c.Crash.Y));
    }

    [Fact]
    public void The_headquarters_stand_where_the_factions_live()
    {
        var c = CityBook.Build(1);
        Assert.Equal(240, c.Roof(c.Palyar.X, c.Palyar.Y));
        Assert.Equal(280, c.Roof(c.Mechanoid.X, c.Mechanoid.Y));
        Assert.Contains(c.Buildings, b => b.Shape == Shape.Headquarters && b.Owner == Faction.Palyar);
        Assert.Contains(c.Buildings, b => b.Shape == Shape.Headquarters && b.Owner == Faction.Mechanoid);
        Assert.Contains(c.Buildings, b => b.Shape == Shape.Hangar);
    }

    [Fact]
    public void Sectors_are_named_by_column_and_row()
    {
        Assert.Equal("00-00", CityDef.SectorName(10, 10));
        Assert.Equal("04-04", CityDef.SectorName(3600, 3600));
        Assert.Equal("08-08", CityDef.SectorName(Field.Size + 50, Field.Size + 50));
        var c = CityBook.Build(1);
        Assert.Equal("01-06", CityDef.SectorName(c.Palyar.X, c.Palyar.Y));
    }

    [Fact]
    public void Every_block_stands_inside_the_city_off_the_roads()
    {
        var c = CityBook.Build(2);
        foreach (var b in c.Buildings.Where(b => b.Shape == Shape.Block))
        {
            var (sx, sy) = CityDef.SectorOf(b.X, b.Y);
            Assert.True(b.X - b.W / 2 >= sx * Field.SectorSize + 60 && b.X + b.W / 2 <= (sx + 1) * Field.SectorSize - 60);
            Assert.True(b.Y - b.D / 2 >= sy * Field.SectorSize + 60 && b.Y + b.D / 2 <= (sy + 1) * Field.SectorSize - 60);
        }
    }
}
