namespace AVAMercenary.Engine;

/// <summary>
/// The skimmer's controls: left and right turn it, up drives it forward and down slows it (and backs it off);
/// climb and dive; fire; and use (take a job at a headquarters, pick up or put down a crate, buy passage).
/// </summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Fire = false, bool Use = false, int Climb = 0);

/// <summary>The screen, and the city: nine sectors by nine.</summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const int Sectors = 9;
    public const double SectorSize = 800;
    public const double Size = Sectors * SectorSize;
    public const double MaxAltitude = 420;
}

public enum Faction { Palyar, Mechanoid }

public enum Shape
{
    /// <summary>A plain block.</summary>
    Block,
    /// <summary>A headquarters: the Palyars' pyramid, or the Mechanoids' stepped tower.</summary>
    Headquarters,
    /// <summary>The spaceport's hangar.</summary>
    Hangar,
}

/// <summary>A building: a box (or a pyramid) standing on the ground, and how tall.</summary>
public readonly record struct BuildingDef(double X, double Y, double W, double D, double H, Shape Shape, Faction? Owner = null);

/// <summary>A Mechanoid relay tower: a lattice mast.</summary>
public readonly record struct TowerDef(double X, double Y, double H);

/// <summary>The city: its buildings, where the factions' headquarters and the spaceport are, and the relay towers.</summary>
public sealed class CityDef
{
    public string Name = "TARG CITY";
    public List<BuildingDef> Buildings = [];
    public List<TowerDef> Towers = [];
    public (double X, double Y) Palyar, Mechanoid, Spaceport, Crash;
    /// <summary>Where crates wait to be collected for the Mechanoids.</summary>
    public List<(double X, double Y)> Depots = [];
    public int ShipPrice = 20000;

    public static (int X, int Y) SectorOf(double x, double y) =>
        ((int)Math.Clamp(Math.Floor(x / Field.SectorSize), 0, Field.Sectors - 1), (int)Math.Clamp(Math.Floor(y / Field.SectorSize), 0, Field.Sectors - 1));

    public static string SectorName(double x, double y)
    {
        var (sx, sy) = SectorOf(x, y);
        return $"{sx:00}-{sy:00}";
    }

    /// <summary>The top of whatever stands at a point (0 for open ground).</summary>
    public double Roof(double x, double y)
    {
        double top = 0;
        foreach (var b in Buildings)
            if (Math.Abs(x - b.X) < b.W / 2 && Math.Abs(y - b.Y) < b.D / 2) top = Math.Max(top, b.H);
        return top;
    }
}

public static class CityBook
{
    public static CityDef Build(int seed = 1)
    {
        var d = new Dice(seed * 7919 + 13);
        var c = new CityDef();
        double S = Field.SectorSize;
        (double, double) Mid(int sx, int sy) => ((sx + 0.5) * S, (sy + 0.5) * S);
        c.Crash = Mid(4, 4);
        c.Palyar = Mid(1, 6);
        c.Mechanoid = Mid(6, 1);
        c.Spaceport = Mid(7, 7);
        c.Buildings.Add(new BuildingDef(c.Palyar.Item1, c.Palyar.Item2, 220, 220, 240, Shape.Headquarters, Faction.Palyar));
        c.Buildings.Add(new BuildingDef(c.Mechanoid.Item1, c.Mechanoid.Item2, 180, 180, 280, Shape.Headquarters, Faction.Mechanoid));
        c.Buildings.Add(new BuildingDef(c.Spaceport.Item1 + 180, c.Spaceport.Item2, 160, 240, 90, Shape.Hangar));
        // City blocks: in each sector a few buildings set back from the roads along its edges, leaving the middle
        // of the special sectors open.
        for (int sx = 0; sx < Field.Sectors; sx++)
        for (int sy = 0; sy < Field.Sectors; sy++)
        {
            var (mx, my) = Mid(sx, sy);
            bool special = (mx, my) == c.Crash || (mx, my) == c.Palyar || (mx, my) == c.Mechanoid || (mx, my) == c.Spaceport;
            int n = special ? 2 : 3 + d.Next(4);
            for (int k = 0; k < n; k++)
            {
                for (int tries = 0; tries < 20; tries++)
                {
                    double w = 60 + d.Next() * 120, dep = 60 + d.Next() * 120, h = 30 + d.Next() * (special ? 60 : 200);
                    double x = sx * S + 70 + w / 2 + d.Next() * (S - 140 - w), y = sy * S + 70 + dep / 2 + d.Next() * (S - 140 - dep);
                    if (special && Math.Abs(x - mx) < 320 && Math.Abs(y - my) < 320) continue;
                    if (c.Buildings.Any(b => Math.Abs(b.X - x) < (b.W + w) / 2 + 30 && Math.Abs(b.Y - y) < (b.D + dep) / 2 + 30)) continue;
                    c.Buildings.Add(new BuildingDef(x, y, w, dep, h, Shape.Block));
                    break;
                }
            }
        }
        // Relay towers, out in the open.
        while (c.Towers.Count < 10)
        {
            int sx = d.Next(Field.Sectors), sy = d.Next(Field.Sectors);
            double x = (sx + 0.15 + d.Next() * 0.7) * S, y = (sy + 0.15 + d.Next() * 0.7) * S;
            if (Math.Abs(x - c.Crash.Item1) + Math.Abs(y - c.Crash.Item2) < 900) continue;
            if (c.Roof(x, y) > 0 || c.Buildings.Any(b => Math.Abs(b.X - x) < b.W / 2 + 40 && Math.Abs(b.Y - y) < b.D / 2 + 40)) continue;
            if (c.Towers.Any(t => Math.Abs(t.X - x) + Math.Abs(t.Y - y) < 1200)) continue;
            c.Towers.Add(new TowerDef(x, y, 110 + d.Next() * 60));
        }
        // Depots where crates wait.
        while (c.Depots.Count < 6)
        {
            int sx = d.Next(Field.Sectors), sy = d.Next(Field.Sectors);
            double x = (sx + 0.5) * S + (d.Next() - 0.5) * 300, y = (sy + 0.5) * S + (d.Next() - 0.5) * 300;
            if (Math.Abs(x - c.Mechanoid.Item1) + Math.Abs(y - c.Mechanoid.Item2) < 1600) continue;
            if (c.Buildings.Any(b => Math.Abs(b.X - x) < b.W / 2 + 50 && Math.Abs(b.Y - y) < b.D / 2 + 50)) continue;
            if (c.Depots.Any(p => Math.Abs(p.X - x) + Math.Abs(p.Y - y) < 1000)) continue;
            c.Depots.Add((x, y));
        }
        return c;
    }
}
