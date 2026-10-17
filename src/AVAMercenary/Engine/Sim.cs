namespace AVAMercenary.Engine;

/// <summary>
/// The city, step by step: the skimmer flying among the buildings, its bolts, jobs taken and done for the
/// Palyars (bring down the Mechanoids' relay towers) and the Mechanoids (fetch crates home), each faction
/// turning against the mercenary who works for the other, their drones, and passage off the planet.
/// </summary>
public static class Sim
{
    private const double Dt = Field.Step;
    public const double BoltSpeed = 640, HqReach = 280, HqLow = 90, CrateReach = 45, CrateLow = 30, PortReach = 140, PortLow = 50;
    public const int TowerReward = 2500, CrateReward = 2000, StartCredits = 500;
    public const double DroneEvery = 13, DroneSpeed = 130, Recharge = 1.0;

    public static void Start(World w, CityDef c, int seed)
    {
        w.City = c;
        w.Dice = new Dice(seed);
        w.Towers = c.Towers.Select(t => new Tower { Def = t }).ToList();
        w.Drones.Clear();
        w.Bolts.Clear();
        w.Flashes.Clear();
        w.Credits = StartCredits;
        w.Job = null;
        w.CrateAt = -1;
        w.Standing = [0, 0];
        w.JobsDone = 0;
        w.Clock = 0;
        w.NextDrone = DroneEvery;
        w.Escaped = false;
        w.Log.Clear();
        w.Say("CRASH-LANDED ON TARG. PASSAGE OFF-WORLD COSTS " + c.ShipPrice + " CREDITS.");
        w.Say("THE PALYARS AND THE MECHANOIDS BOTH HAVE WORK. MIND WHOSE.");
        Respawn(w);
    }

    /// <summary>A fresh skimmer at the crash site. The job in hand still stands, but a crate aboard is lost: another waits at a depot.</summary>
    public static void Respawn(World w)
    {
        var (x, y) = w.City.Crash;
        w.Ship = new Skimmer { X = x, Y = y, Alt = 4, Heading = 0 };
        w.Drones.Clear();
        w.Bolts.RemoveAll(b => !b.Mine);
        if (w.Job?.Kind == JobKind.Crate && w.CrateAt < 0) w.CrateAt = w.Dice.Next(w.City.Depots.Count);
    }

    public static (double X, double Y) Ahead(double heading) => (Math.Sin(heading), Math.Cos(heading));

    public static double Wrap(double a)
    {
        while (a > Math.PI) a -= 2 * Math.PI;
        while (a < -Math.PI) a += 2 * Math.PI;
        return a;
    }

    public static double Bearing(double fx, double fy, double tx, double ty) => Math.Atan2(tx - fx, ty - fy);
    public static double Dist(double ax, double ay, double bx, double by) => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));

    public static void Step(World w, Controls c)
    {
        w.Clock += Dt;
        foreach (var f in w.Flashes) f.T += Dt;
        w.Flashes.RemoveAll(f => f.T > 0.8);
        var s = w.Ship;
        if (!s.Dead && !w.Escaped) Fly(w, s, c);
        s.UsePrev = c.Use;
        Drones(w);
        Bolts(w);
        if (!s.Dead && s.Shields <= 0)
        {
            s.Shields = 0;
            s.Dead = true;
            if (w.Cause == "") w.Cause = "SHOT DOWN";
            w.Flashes.Add(new Flash(s.X, s.Y, s.Alt, true));
            w.Heard.Add(Sfx.Explode);
        }
    }

    private static void Fly(World w, Skimmer s, Controls c)
    {
        s.FireTimer -= Dt;
        if (s.Shields > 0) s.Shields = Math.Min(Skimmer.Shields0, s.Shields + Recharge * Dt);
        s.Heading = Wrap(s.Heading + c.Dx * Skimmer.Turn * Dt);
        s.Bank += (c.Dx * 0.3 - s.Bank) * Math.Min(1, Dt * 5);
        if (c.Dy < 0) s.Speed = Math.Min(Skimmer.MaxSpeed, s.Speed + Skimmer.Accel * Dt);
        else if (c.Dy > 0) s.Speed = Math.Max(-30, s.Speed - Skimmer.Accel * 1.5 * Dt);
        else s.Speed -= s.Speed * 0.4 * Dt;
        double want = c.Climb * Skimmer.Climb;
        s.Vz += (want - s.Vz) * Math.Min(1, Dt * 4);
        var (ax, ay) = Ahead(s.Heading);
        double nx = Math.Clamp(s.X + ax * s.Speed * Dt, 20, Field.Size - 20), ny = Math.Clamp(s.Y + ay * s.Speed * Dt, 20, Field.Size - 20);
        double nz = Math.Clamp(s.Alt + s.Vz * Dt, 2, Field.MaxAltitude);
        // Into a building: stopped (and shaken if it was fast).
        double roof = w.City.Roof(nx, ny);
        if (nz < roof + 3)
        {
            if (s.Alt >= roof + 3 && w.City.Roof(s.X, s.Y) == roof) nz = roof + 3;
            else
            {
                if (Math.Abs(s.Speed) > 40) { s.Shields -= 6; w.Cause = "FLEW INTO A BUILDING"; w.Heard.Add(Sfx.Bump); }
                s.Speed = 0;
                nx = s.X;
                ny = s.Y;
                nz = Math.Max(nz, w.City.Roof(s.X, s.Y) + 3);
            }
        }
        s.X = nx;
        s.Y = ny;
        s.Alt = nz;
        if (c.Fire && s.FireTimer <= 0)
        {
            s.FireTimer = 0.28;
            Shoot(w, s);
        }
        if (c.Use && !s.UsePrev) Use(w, s);
    }

    /// <summary>The things worth shooting at: towers still standing, and drones.</summary>
    public static IEnumerable<(double X, double Y, double Z)> Targets(World w)
    {
        foreach (var t in w.Towers) if (!t.Down) yield return (t.Def.X, t.Def.Y, t.Def.H * 0.6);
        foreach (var d in w.Drones) yield return (d.X, d.Y, d.Z);
    }

    /// <summary>A bolt straight ahead, tipped up or down onto whatever is near the line of fire.</summary>
    private static void Shoot(World w, Skimmer s)
    {
        var (ax, ay) = Ahead(s.Heading);
        double v = BoltSpeed + Math.Max(0, s.Speed), vz = 0, best = 0.14;
        foreach (var (x, y, z) in Targets(w))
        {
            double d = Dist(s.X, s.Y, x, y);
            if (d > 1000 || d < 10) continue;
            double off = Math.Abs(Wrap(Bearing(s.X, s.Y, x, y) - s.Heading));
            if (off < best) { best = off; vz = (z - s.Alt) / (d / v); }
        }
        w.Bolts.Add(new Bolt { X = s.X + ax * 6, Y = s.Y + ay * 6, Z = s.Alt - 1, Vx = ax * v, Vy = ay * v, Vz = vz, Life = 1.6, Mine = true });
        w.Heard.Add(Sfx.Fire);
    }

    private static void Use(World w, Skimmer s)
    {
        var c = w.City;
        // Passage off-world.
        if (Dist(s.X, s.Y, c.Spaceport.X, c.Spaceport.Y) < PortReach && s.Alt < PortLow)
        {
            if (w.Credits >= c.ShipPrice)
            {
                w.Credits -= c.ShipPrice;
                w.Escaped = true;
                w.Say("PASSAGE BOOKED. GOODBYE, TARG!");
                w.Heard.Add(Sfx.Launch);
            }
            else { w.Say($"THE PORTMASTER WANTS {c.ShipPrice} CREDITS. YOU HAVE {w.Credits}."); w.Heard.Add(Sfx.Deny); }
            return;
        }
        // A crate to pick up (or put down).
        if (w.CrateAt >= 0 && !s.Crate && Dist(s.X, s.Y, c.Depots[w.CrateAt].X, c.Depots[w.CrateAt].Y) < CrateReach && s.Alt < CrateLow)
        {
            s.Crate = true;
            w.CrateAt = -1;
            w.Say("CRATE ABOARD. TAKE IT TO THE MECHANOIDS.");
            w.Heard.Add(Sfx.Pickup);
            return;
        }
        foreach (var f in new[] { Faction.Palyar, Faction.Mechanoid })
        {
            var hq = f == Faction.Palyar ? c.Palyar : c.Mechanoid;
            if (Dist(s.X, s.Y, hq.X, hq.Y) >= HqReach || s.Alt >= HqLow) continue;
            string who = f == Faction.Palyar ? "PALYARS" : "MECHANOIDS";
            if (w.Job is { } job)
            {
                if (job.For == f && job.Kind == JobKind.Crate && s.Crate)
                {
                    s.Crate = false;
                    Done(w, job);
                }
                else w.Say(job.For == f ? $"{who}: THE JOB ISN'T DONE YET." : $"{who}: FINISH YOUR WORK FOR THE OTHERS FIRST.");
                w.Heard.Add(Sfx.Radio);
                return;
            }
            if (w.Hostile(f)) { w.Say($"{who}: WE DON'T DEAL WITH TRAITORS."); w.Heard.Add(Sfx.Deny); return; }
            if (f == Faction.Palyar)
            {
                var standing = Enumerable.Range(0, w.Towers.Count).Where(i => !w.Towers[i].Down).ToList();
                if (standing.Count == 0) { w.Say("PALYARS: NO WORK - EVERY TOWER IS DOWN."); return; }
                int t = standing.OrderBy(i => Dist(hq.X, hq.Y, w.Towers[i].Def.X, w.Towers[i].Def.Y) + w.Dice.Next() * 1500).First();
                w.Job = new Job(f, JobKind.Tower, t, TowerReward + 200 * w.JobsDone);
                w.Say($"PALYARS: BRING DOWN THE RELAY TOWER IN SECTOR {CityDef.SectorName(w.Towers[t].Def.X, w.Towers[t].Def.Y)}. {w.Job.Reward} CREDITS.");
            }
            else
            {
                int depot = w.Dice.Next(c.Depots.Count);
                w.CrateAt = depot;
                w.Job = new Job(f, JobKind.Crate, depot, CrateReward + 200 * w.JobsDone);
                w.Say($"MECHANOIDS: FETCH THE CRATE FROM SECTOR {CityDef.SectorName(c.Depots[depot].X, c.Depots[depot].Y)}. {w.Job.Reward} CREDITS.");
            }
            w.Heard.Add(Sfx.Radio);
            return;
        }
        w.Heard.Add(Sfx.Deny);
    }

    /// <summary>A job done: paid, and the faction it was against (the other one) the angrier for it.</summary>
    private static void Done(World w, Job job)
    {
        w.Credits += job.Reward;
        w.JobsDone++;
        w.Job = null;
        var other = job.For == Faction.Palyar ? Faction.Mechanoid : Faction.Palyar;
        w.Standing[(int)job.For]++;
        w.Standing[(int)other] -= 2;
        string who = job.For == Faction.Palyar ? "PALYARS" : "MECHANOIDS";
        w.Say($"{who}: WELL DONE. {job.Reward} CREDITS PAID. ({w.Credits} IN ALL)");
        if (w.Hostile(other)) w.Say($"THE {(other == Faction.Palyar ? "PALYARS" : "MECHANOIDS")} HAVE TURNED AGAINST YOU.");
        w.Heard.Add(Sfx.Paid);
    }

    private static void Drones(World w)
    {
        var s = w.Ship;
        var c = w.City;
        if (!s.Dead && w.Clock >= w.NextDrone)
        {
            w.NextDrone = w.Clock + DroneEvery;
            foreach (var f in new[] { Faction.Palyar, Faction.Mechanoid })
            {
                if (!w.Hostile(f) || w.Drones.Count >= 3) continue;
                var hq = f == Faction.Palyar ? c.Palyar : c.Mechanoid;
                // From whichever is nearer: the faction's headquarters, or out of the sky ahead.
                double a = w.Dice.Next() * Math.PI * 2;
                (double x, double y) = Dist(hq.X, hq.Y, s.X, s.Y) < 2000 ? hq : (s.X + Math.Sin(a) * 1500, s.Y + Math.Cos(a) * 1500);
                w.Drones.Add(new Drone { Of = f, X = Math.Clamp(x, 0, Field.Size), Y = Math.Clamp(y, 0, Field.Size), Z = 300, Cool = 2 });
                w.Heard.Add(Sfx.Drone);
            }
        }
        foreach (var d in w.Drones)
        {
            d.Cool -= Dt;
            d.Spin += Dt * 3;
            double dist = Dist(d.X, d.Y, s.X, s.Y);
            double want = Bearing(d.X, d.Y, s.X, s.Y) + (dist < 250 ? Math.PI / 2 : 0);
            d.Heading = Wrap(d.Heading + Math.Clamp(Wrap(want - d.Heading), -1.4 * Dt, 1.4 * Dt));
            var (ax, ay) = Ahead(d.Heading);
            d.X += ax * DroneSpeed * Dt;
            d.Y += ay * DroneSpeed * Dt;
            d.Z += (Math.Max(c.Roof(d.X, d.Y) + 40, s.Alt + 25) - d.Z) * Math.Min(1, Dt);
            if (!s.Dead && d.Cool <= 0 && dist < 520)
            {
                d.Cool = 1.7;
                double t = Math.Sqrt(dist * dist + (s.Alt - d.Z) * (s.Alt - d.Z)) / 300;
                var (sx, sy) = Ahead(s.Heading);
                double tx = s.X + sx * s.Speed * t + (w.Dice.Next() - 0.5) * 20, ty = s.Y + sy * s.Speed * t + (w.Dice.Next() - 0.5) * 20;
                w.Bolts.Add(new Bolt { X = d.X, Y = d.Y, Z = d.Z, Vx = (tx - d.X) / t, Vy = (ty - d.Y) / t, Vz = (s.Alt - d.Z) / t, Life = t + 0.5 });
                w.Heard.Add(Sfx.DroneFire);
            }
        }
    }

    private static void Bolts(World w)
    {
        var s = w.Ship;
        foreach (var b in w.Bolts)
        {
            b.X += b.Vx * Dt;
            b.Y += b.Vy * Dt;
            b.Z += b.Vz * Dt;
            b.Life -= Dt;
            if (b.Life <= 0) continue;
            if (b.Z < w.City.Roof(b.X, b.Y) || b.Z < 0)
            {
                b.Life = 0;
                w.Flashes.Add(new Flash(b.X, b.Y, Math.Max(0, b.Z), false));
                continue;
            }
            if (b.Mine)
            {
                foreach (var t in w.Towers)
                    if (!t.Down && Dist(b.X, b.Y, t.Def.X, t.Def.Y) < 16 && b.Z < t.Def.H)
                    {
                        b.Life = 0;
                        t.Life -= 10;
                        w.Flashes.Add(new Flash(b.X, b.Y, b.Z, t.Down));
                        w.Heard.Add(t.Down ? Sfx.TowerDown : Sfx.Hit);
                        if (!t.Down) break;
                        int index = w.Towers.IndexOf(t);
                        if (w.Job is { Kind: JobKind.Tower } job && job.Target == index) Done(w, job);
                        else
                        {
                            w.Standing[(int)Faction.Mechanoid]--;
                            w.Say("MECHANOIDS: THAT WAS OUR TOWER.");
                        }
                        break;
                    }
                foreach (var d in w.Drones)
                    if (Dist(b.X, b.Y, d.X, d.Y) < 16 && Math.Abs(b.Z - d.Z) < 10)
                    {
                        b.Life = 0;
                        d.Life -= 6;
                        w.Flashes.Add(new Flash(d.X, d.Y, d.Z, d.Life <= 0));
                        w.Heard.Add(d.Life <= 0 ? Sfx.DroneDown : Sfx.Hit);
                    }
                w.Drones.RemoveAll(d => d.Life <= 0);
            }
            else if (!s.Dead && Dist(b.X, b.Y, s.X, s.Y) < 12 && Math.Abs(b.Z - s.Alt) < 8)
            {
                b.Life = 0;
                s.Shields -= 8;
                w.Cause = "SHOT DOWN BY A DRONE";
                w.Flashes.Add(new Flash(s.X, s.Y, s.Alt, false));
                w.Heard.Add(Sfx.Hurt);
            }
        }
        w.Bolts.RemoveAll(b => b.Life <= 0);
    }
}
