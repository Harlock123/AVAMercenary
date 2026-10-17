namespace AVAMercenary.Engine;

/// <summary>A small random number generator whose state can be copied.</summary>
public struct Dice
{
    private ulong _s;
    public Dice(int seed) => _s = (ulong)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;

    public double Next()
    {
        _s ^= _s << 13;
        _s ^= _s >> 7;
        _s ^= _s << 17;
        return (_s >> 11) * (1.0 / (1UL << 53));
    }

    public int Next(int n) => (int)(Next() * n);
}

/// <summary>The skimmer: where it is, how high, which way it heads (0 is north, turning clockwise), how fast.</summary>
public sealed class Skimmer
{
    public const double MaxSpeed = 110, Accel = 70, Turn = 1.5, Climb = 50, Shields0 = 100;
    public double X, Y, Alt = 4, Heading, Speed, Vz, Bank;
    public double Shields = Shields0, FireTimer;
    public bool Dead, UsePrev;
    /// <summary>Carrying a crate for the Mechanoids.</summary>
    public bool Crate;
    public Skimmer Copy() => (Skimmer)MemberwiseClone();
}

public enum JobKind { None, Tower, Crate }

/// <summary>A job taken from a faction: a tower to bring down, or a crate to fetch home; and the reward.</summary>
public sealed record Job(Faction For, JobKind Kind, int Target, int Reward);

public sealed class Tower
{
    public TowerDef Def;
    public double Life = 30;
    public bool Down => Life <= 0;
    public Tower Copy() => (Tower)MemberwiseClone();
}

/// <summary>A drone of a faction that has turned against the mercenary.</summary>
public sealed class Drone
{
    public Faction Of;
    public double X, Y, Z, Heading, Cool, Life = 12, Spin;
    public Drone Copy() => (Drone)MemberwiseClone();
}

public sealed class Bolt
{
    public double X, Y, Z, Vx, Vy, Vz, Life;
    public bool Mine;
    public Bolt Copy() => (Bolt)MemberwiseClone();
}

public sealed record Flash(double X, double Y, double Z, bool Big)
{
    public double T;
}

/// <summary>Everything in the city that moves or changes, kept together so it can be copied whole.</summary>
public sealed class World
{
    public CityDef City = null!;
    public Skimmer Ship = new();
    public List<Tower> Towers = [];
    public List<Drone> Drones = [];
    public List<Bolt> Bolts = [];
    public List<Flash> Flashes = [];
    public List<Sfx> Heard = [];
    public Dice Dice = new(1);
    public int Credits;
    public Job? Job;
    /// <summary>The crate waiting for the current crate job (which depot), or -1.</summary>
    public int CrateAt = -1;
    /// <summary>How each faction feels about the mercenary: below zero, its drones hunt him.</summary>
    public int[] Standing = [0, 0];
    public int JobsDone;
    public double Clock, NextDrone;
    public bool Escaped;
    public string Cause = "";
    /// <summary>The last few things the ship's computer said, newest last.</summary>
    public List<string> Log = [];

    public bool Hostile(Faction f) => Standing[(int)f] < 0;

    public void Say(string text)
    {
        Log.Add(text);
        if (Log.Count > 4) Log.RemoveAt(0);
    }

    public World Copy() => new()
    {
        City = City, Ship = Ship.Copy(), Towers = Towers.Select(t => t.Copy()).ToList(), Drones = Drones.Select(d => d.Copy()).ToList(),
        Bolts = Bolts.Select(b => b.Copy()).ToList(), Dice = Dice, Credits = Credits, Job = Job, CrateAt = CrateAt, Standing = [.. Standing],
        JobsDone = JobsDone, Clock = Clock, NextDrone = NextDrone, Escaped = Escaped, Cause = Cause, Log = [.. Log],
    };
}
