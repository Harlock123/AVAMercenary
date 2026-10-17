namespace AVAMercenary.Engine;

/// <summary>
/// The computer mercenary, for the demonstration and the tests. With the price of passage saved it flies to the
/// spaceport; otherwise it takes a job (the Palyars' first, the Mechanoids' if the Palyars won't deal with it)
/// and does it: up over the rooftops, across the city, down where the work is. It brings towers down from a
/// little way off, and turns to shoot any drone that comes for it.
/// </summary>
public sealed class Pilot
{
    public const double Cruise = 330;
    private bool _usePrev;

    public string Doing { get; private set; } = "";

    public void Reset()
    {
        _usePrev = false;
        Doing = "";
    }

    public Controls Decide(GameEngine g) => g.Phase != Phase.Play ? default : Decide(g.World);

    public Controls Decide(World w)
    {
        var s = w.Ship;
        var c = w.City;
        if (s.Dead || w.Escaped) return default;
        // A drone close by: round to face it, firing.
        var drone = w.Drones.OrderBy(d => Sim.Dist(d.X, d.Y, s.X, s.Y)).FirstOrDefault();
        if (drone != null && Sim.Dist(drone.X, drone.Y, s.X, s.Y) < 600)
        {
            Doing = "fighting a drone";
            double e = Sim.Wrap(Sim.Bearing(s.X, s.Y, drone.X, drone.Y) - s.Heading);
            int climb = s.Alt < drone.Z - 15 ? 1 : s.Alt > drone.Z + 15 ? -1 : 0;
            if (s.Alt < c.Roof(s.X, s.Y) + 60 || s.Alt < 220) climb = 1;
            return new Controls(Turn(e), Math.Abs(e) < 0.6 ? -1 : 0, Math.Abs(e) < 0.12, false, climb);
        }
        // Where to, and what to do there.
        (double X, double Y) to;
        double low;
        string what;
        bool shoot = false;
        if (w.Credits >= c.ShipPrice) { to = c.Spaceport; low = 20; what = "to the spaceport"; }
        else if (w.Job is { Kind: JobKind.Tower } tj)
        {
            var t = w.Towers[tj.Target];
            to = (t.Def.X, t.Def.Y);
            low = 200;
            what = "after a tower";
            shoot = true;
        }
        else if (w.Job is { Kind: JobKind.Crate } cj)
        {
            if (s.Crate) { to = c.Mechanoid; low = 40; what = "taking the crate home"; to = (to.X, to.Y - 190); }
            else { to = c.Depots[w.CrateAt >= 0 ? w.CrateAt : 0]; low = 12; what = "after a crate"; }
        }
        else
        {
            var f = w.Hostile(Faction.Palyar) ? Faction.Mechanoid : Faction.Palyar;
            var hq = f == Faction.Palyar ? c.Palyar : c.Mechanoid;
            to = (hq.X, hq.Y - 190);
            low = 40;
            what = f == Faction.Palyar ? "to the Palyars for work" : "to the Mechanoids for work";
        }
        Doing = what;
        double dist = Sim.Dist(s.X, s.Y, to.X, to.Y);
        double err = Sim.Wrap(Sim.Bearing(s.X, s.Y, to.X, to.Y) - s.Heading);
        if (shoot)
        {
            // Stand off, face the tower, fire: from high enough to see it over anything in between.
            var tw = w.Towers[w.Job!.Target].Def;
            double over = Highest(c, s.X, s.Y, tw.X, tw.Y);
            double fireAlt = Math.Max(Math.Max(low, tw.H + 40), over + 40);
            int dy = dist > 380 ? (Math.Abs(err) < 0.5 ? -1 : 0) : dist < 220 ? 1 : 0;
            int climb = s.Alt < fireAlt - 10 ? 1 : s.Alt > fireAlt + 20 && dist < 900 ? -1 : 0;
            if (dist > 900) climb = s.Alt < Cruise - 10 ? 1 : 0;
            if (dy == -1 && s.Speed > 80 && dist < 600) dy = 0;
            if (Blocked(c, s)) { dy = 0; climb = 1; }
            return new Controls(Turn(err), dy, dist < 800 && Math.Abs(err) < 0.1 && s.Alt > over + 20, false, climb);
        }
        // Over the rooftops until close; then down onto the spot, slowly.
        bool near = dist < 160;
        double alt = near ? low : Cruise;
        int up = s.Alt < alt - 8 ? 1 : s.Alt > alt + 8 && near ? -1 : 0;
        if (!near && s.Alt < Cruise - 30) up = 1;
        double want = near ? Math.Min(dist * 0.8, 60) : Skimmer.MaxSpeed;
        // Too low to cross the city yet: climb first.
        if (!near && s.Alt < 290) want = Math.Min(want, 20);
        int fwd = Math.Abs(err) > 0.7 ? (s.Speed > 20 ? 1 : 0) : s.Speed < want - 5 ? -1 : s.Speed > want + 8 ? 1 : 0;
        // Something tall just ahead: up over it before going on.
        if (Blocked(c, s)) { fwd = s.Speed > 5 ? 1 : 0; up = 1; }
        bool use = dist < (w.Job?.Kind == JobKind.Crate && !s.Crate ? Sim.CrateReach - 10 : 60) && s.Alt < low + 6 && Math.Abs(s.Speed) < 25;
        if (use && _usePrev) use = false;
        _usePrev = use;
        return new Controls(Turn(err), fwd, Shootable(w, s), use, up);
    }

    private static int Turn(double err) => Math.Abs(err) < 0.04 ? 0 : Math.Sign(err);

    /// <summary>The tallest roof on the line between two points.</summary>
    private static double Highest(CityDef c, double ax, double ay, double bx, double by)
    {
        double top = 0;
        for (int i = 1; i < 20; i++) top = Math.Max(top, c.Roof(ax + (bx - ax) * i / 20, ay + (by - ay) * i / 20));
        return top;
    }

    /// <summary>A roof just ahead, as high as the skimmer flies.</summary>
    private static bool Blocked(CityDef c, Skimmer s)
    {
        var (ax, ay) = Sim.Ahead(s.Heading);
        for (double d = 10; d <= 60 + Math.Max(0, s.Speed); d += 15)
            if (c.Roof(s.X + ax * d, s.Y + ay * d) > s.Alt - 8) return true;
        return false;
    }

    private static bool Shootable(World w, Skimmer s) =>
        w.Drones.Any(d => Sim.Dist(d.X, d.Y, s.X, s.Y) < 700 && Math.Abs(Sim.Wrap(Sim.Bearing(s.X, s.Y, d.X, d.Y) - s.Heading)) < 0.12);
}
