using Avalonia.Input;
using AVAMercenary.Engine;

namespace AVAMercenary.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avamercenary-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>The city, quiet: nobody hostile, so no drones.</summary>
    private static World City(bool empty = false)
    {
        var c = CityBook.Build(1);
        if (empty) c.Buildings.RemoveAll(b => b.Shape == Shape.Block);
        var w = new World();
        Sim.Start(w, c, 1);
        return w;
    }

    private static void Run(World w, int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Sim.Step(w, c);
    }

    private static void Put(World w, double x, double y, double alt, double heading = 0)
    {
        var s = w.Ship;
        (s.X, s.Y, s.Alt, s.Heading, s.Speed, s.Vz) = (x, y, alt, heading, 0, 0);
    }

    /// <summary>One press of Use (and its release).</summary>
    private static void Use(World w)
    {
        Run(w, 1, new Controls(Use: true));
        Run(w, 1);
    }

    // ───────────────────────────── flying ─────────────────────────────

    [Fact]
    public void It_flies_the_way_it_heads_and_turns_and_climbs_and_dives()
    {
        var w = City(empty: true);
        var s = w.Ship;
        double y = s.Y;
        Run(w, 120, new Controls(Dy: -1));
        Assert.True(s.Y > y + 60, "forward is north at heading 0");
        Assert.True(s.Speed > 60);
        Run(w, 60, new Controls(Climb: 1));
        Assert.True(s.Alt > 30, "climbs");
        double alt = s.Alt;
        Run(w, 30, new Controls(Climb: -1));
        Assert.True(s.Alt < alt, "dives");
        Run(w, 30, new Controls(Dx: 1));
        Assert.True(s.Heading > 0.4, "right turns clockwise");
    }

    [Fact]
    public void Down_slows_it_and_backs_it_off_and_it_drifts_to_a_stop()
    {
        var w = City(empty: true);
        Run(w, 240, new Controls(Dy: -1));
        Assert.Equal(Skimmer.MaxSpeed, w.Ship.Speed, 3);
        Run(w, 240, new Controls(Dy: 1));
        Assert.Equal(-30, w.Ship.Speed, 3);
        Run(w, 600);
        Assert.InRange(w.Ship.Speed, -1, 1);
    }

    [Fact]
    public void Flying_into_a_building_stops_it_and_hurts_it_at_speed()
    {
        var w = City();
        var b = w.City.Buildings.First(b => b.Shape == Shape.Block && b.H > 100);
        Put(w, b.X, b.Y - b.D / 2 - 30, 20);
        w.Ship.Speed = 100;
        Run(w, 60, new Controls(Dy: -1));
        Assert.True(w.Ship.Y < b.Y - b.D / 2, "not through the wall");
        Assert.True(w.Ship.Shields < Skimmer.Shields0 - 4);
        Assert.Equal("FLEW INTO A BUILDING", w.Cause);
    }

    [Fact]
    public void It_can_sit_on_a_roof()
    {
        var w = City();
        var b = w.City.Buildings.First(b => b.Shape == Shape.Block);
        Put(w, b.X, b.Y, b.H + 30);
        Run(w, 120, new Controls(Climb: -1));
        Assert.InRange(w.Ship.Alt, b.H + 2, b.H + 5);
    }

    [Fact]
    public void Shields_build_back_up_slowly()
    {
        var w = City(empty: true);
        w.Ship.Shields = 50;
        Run(w, 120);
        Assert.Equal(52, w.Ship.Shields, 1);
    }

    // ───────────────────────────── jobs ─────────────────────────────

    [Fact]
    public void The_Palyars_give_a_tower_to_bring_down_but_only_to_a_skimmer_landed_at_their_door()
    {
        var w = City();
        var p = w.City.Palyar;
        Put(w, p.X, p.Y - 190, 200);
        Use(w);
        Assert.Null(w.Job);
        Put(w, p.X, p.Y - 190, 20);
        Use(w);
        Assert.Equal(new { For = Faction.Palyar, Kind = JobKind.Tower }, new { w.Job!.For, w.Job.Kind });
        Assert.StartsWith("PALYARS: BRING DOWN THE RELAY TOWER IN SECTOR", w.Log[^1]);
    }

    [Fact]
    public void Bringing_down_the_tower_pays_and_turns_the_Mechanoids_against_you()
    {
        var w = City(empty: true);
        var p = w.City.Palyar;
        Put(w, p.X, p.Y - 190, 20);
        Use(w);
        var t = w.Towers[w.Job!.Target].Def;
        int reward = w.Job.Reward;
        Put(w, t.X, t.Y - 200, 60);
        Run(w, 60 * 4, new Controls(Fire: true));
        Assert.True(w.Towers[w.Job?.Target ?? 0].Down || w.Job == null);
        Assert.Null(w.Job);
        Assert.Equal(Sim.StartCredits + reward, w.Credits);
        Assert.Equal(1, w.Standing[(int)Faction.Palyar]);
        Assert.True(w.Hostile(Faction.Mechanoid));
        Assert.Equal(1, w.JobsDone);
    }

    [Fact]
    public void A_tower_brought_down_for_nobody_angers_the_Mechanoids()
    {
        var w = City(empty: true);
        var t = w.Towers[0].Def;
        Put(w, t.X, t.Y - 200, 60);
        Run(w, 60 * 4, new Controls(Fire: true));
        Assert.True(w.Towers[0].Down);
        Assert.Equal(-1, w.Standing[(int)Faction.Mechanoid]);
        Assert.Equal(Sim.StartCredits, w.Credits);
    }

    [Fact]
    public void The_Mechanoids_want_a_crate_fetched_home()
    {
        var w = City();
        var m = w.City.Mechanoid;
        Put(w, m.X, m.Y - 190, 20);
        Use(w);
        Assert.Equal(JobKind.Crate, w.Job!.Kind);
        var depot = w.City.Depots[w.CrateAt];
        Put(w, depot.X + 10, depot.Y, 10);
        Use(w);
        Assert.True(w.Ship.Crate);
        Assert.Equal(-1, w.CrateAt);
        Put(w, m.X, m.Y - 190, 20);
        Use(w);
        Assert.False(w.Ship.Crate);
        Assert.Null(w.Job);
        Assert.True(w.Credits > Sim.StartCredits);
        Assert.True(w.Hostile(Faction.Palyar));
    }

    [Fact]
    public void One_job_at_a_time_and_no_work_for_a_traitor()
    {
        var w = City();
        var p = w.City.Palyar;
        var m = w.City.Mechanoid;
        Put(w, p.X, p.Y - 190, 20);
        Use(w);
        var job = w.Job;
        Put(w, m.X, m.Y - 190, 20);
        Use(w);
        Assert.Same(job, w.Job);
        Assert.Equal("MECHANOIDS: FINISH YOUR WORK FOR THE OTHERS FIRST.", w.Log[^1]);
        w.Job = null;
        w.Standing[(int)Faction.Mechanoid] = -1;
        Use(w);
        Assert.Null(w.Job);
        Assert.Equal("MECHANOIDS: WE DON'T DEAL WITH TRAITORS.", w.Log[^1]);
    }

    [Fact]
    public void Passage_off_Targ_is_sold_only_for_the_full_price()
    {
        var w = City();
        var port = w.City.Spaceport;
        Put(w, port.X, port.Y, 10);
        Use(w);
        Assert.False(w.Escaped);
        Assert.StartsWith("THE PORTMASTER WANTS", w.Log[^1]);
        w.Credits = w.City.ShipPrice + 100;
        Use(w);
        Assert.True(w.Escaped);
        Assert.Equal(100, w.Credits);
    }

    // ───────────────────────────── drones and bolts ─────────────────────────────

    [Fact]
    public void A_hostile_faction_sends_drones_that_shoot()
    {
        var w = City(empty: true);
        w.Standing[(int)Faction.Mechanoid] = -2;
        w.NextDrone = 0;
        Run(w, 1);
        Assert.Single(w.Drones);
        Run(w, 60 * 25);
        Assert.True(w.Ship.Shields < Skimmer.Shields0 || w.Ship.Dead);
        Assert.InRange(w.Drones.Count, 1, 3);
    }

    [Fact]
    public void Friendly_factions_send_no_drones()
    {
        var w = City(empty: true);
        w.NextDrone = 0;
        Run(w, 60 * 30);
        Assert.Empty(w.Drones);
    }

    [Fact]
    public void A_drone_is_shot_down()
    {
        var w = City(empty: true);
        Put(w, 3600, 3600, 100);
        w.Drones.Add(new Drone { Of = Faction.Mechanoid, X = 3600, Y = 3780, Z = 100, Heading = Math.PI, Cool = 99 });
        for (int i = 0; i < 60 * 3 && w.Drones.Count > 0; i++)
        {
            var d = w.Drones[0];
            w.Ship.Heading = Sim.Bearing(w.Ship.X, w.Ship.Y, d.X, d.Y);
            d.Cool = 99;
            Sim.Step(w, new Controls(Fire: true));
        }
        Assert.Empty(w.Drones);
    }

    [Fact]
    public void Bolts_stop_at_walls()
    {
        var w = City();
        var b = w.City.Buildings.First(b => b.Shape == Shape.Block && b.H > 80);
        Put(w, b.X, b.Y - b.D / 2 - 100, 30);
        Run(w, 1, new Controls(Fire: true));
        Assert.Single(w.Bolts);
        Run(w, 30);
        Assert.Empty(w.Bolts);
        Assert.Contains(w.Flashes, f => Math.Abs(f.Y - (b.Y - b.D / 2)) < 20);
    }

    // ───────────────────────────── the game ─────────────────────────────

    private static GameEngine Engine() => new(HighScoreTable.Load(TempFolder())) { Silent = true };

    [Fact]
    public void Every_credit_earned_scores_and_escaping_adds_a_time_bonus()
    {
        var g = Engine();
        g.Go();
        var w = g.World;
        w.Credits = w.City.ShipPrice;
        g.Run(1);
        Assert.Equal(w.City.ShipPrice - Sim.StartCredits, g.Score);
        Put(w, w.City.Spaceport.X, w.City.Spaceport.Y, 10);
        g.Run(1, new Controls(Use: true));
        g.Run(1);
        Assert.Equal(Phase.Escaped, g.Phase);
        int bonus = (GameEngine.ParTime - (int)w.Clock) * GameEngine.TimePoints;
        Assert.Equal(w.City.ShipPrice - Sim.StartCredits + bonus, g.Score);
        Assert.Equal(1, g.Cleared);
        g.Run((int)(60 * GameEngine.EscapedSeconds) + 2);
        Assert.Equal(GameState.GameOver, g.State);
    }

    [Fact]
    public void A_skimmer_lost_and_the_next_waits_at_the_crash_site_with_the_crate_gone_back_to_a_depot()
    {
        var g = Engine();
        g.Go();
        var w = g.World;
        w.Job = new Job(Faction.Mechanoid, JobKind.Crate, 0, 2000);
        w.Ship.Crate = true;
        w.Ship.Shields = 0;
        g.Run(2);
        Assert.Equal(Phase.Dying, g.Phase);
        g.Run((int)(60 * GameEngine.DyingSeconds) + 2);
        Assert.Equal(GameEngine.Lives0 - 1, g.Lives);
        Assert.False(w.Ship.Dead);
        Assert.False(w.Ship.Crate);
        Assert.InRange(w.CrateAt, 0, w.City.Depots.Count - 1);
        Assert.Equal(w.City.Crash, (w.Ship.X, w.Ship.Y));
    }

    [Fact]
    public void The_last_skimmer_lost_is_game_over_then_the_high_scores()
    {
        var g = Engine();
        g.Go();
        g.SetLives(1);
        g.World.Credits += 9000;
        g.Run(1);
        g.World.Ship.Shields = 0;
        g.Run((int)(60 * GameEngine.DyingSeconds) + 5);
        Assert.Equal(GameState.GameOver, g.State);
        g.Run(60 * 5);
        Assert.Equal(GameState.EnterInitials, g.State);
        foreach (char ch in "ACE") g.Initials!.Type(ch);
        g.Initials!.Finish();
        g.Run(1);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal("ACE", g.HighScores.Entries[g.NewEntryIndex].Initials);
        Assert.Equal(0, g.HighScores.Entries[g.NewEntryIndex].Stage);
    }

    [Fact]
    public void The_title_gives_way_to_the_demonstration_and_enter_starts_a_game()
    {
        var g = Engine();
        g.Idle((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        g.Idle(60 * 8);
        Assert.NotEqual(g.World.City.Crash, (g.World.Ship.X, g.World.Ship.Y));
        g.Tick(default, start: true);
        Assert.Equal(GameState.Playing, g.State);
        Assert.Equal(GameEngine.Lives0, g.Lives);
    }

    [Fact]
    public void Paused_nothing_moves()
    {
        var g = Engine();
        g.Go();
        g.Run(30, new Controls(Dy: -1));
        g.Pause();
        double y = g.World.Ship.Y;
        g.Run(120, new Controls(Dy: -1));
        Assert.Equal(y, g.World.Ship.Y);
        g.Resume();
        g.Run(10, new Controls(Dy: -1));
        Assert.NotEqual(y, g.World.Ship.Y);
    }

    [Fact]
    public void Shields_low_is_called_out()
    {
        var g = Engine();
        g.Go();
        g.World.Ship.Shields = 20;
        g.Run(2);
        Assert.Equal("SHIELDS LOW", g.Message);
    }

    [Fact]
    public void Getting_paid_is_announced()
    {
        var g = Engine();
        var heard = new List<Sfx>();
        g.Silent = false;
        g.Sound += heard.Add;
        g.Go();
        g.World.Credits += 2500;
        g.Run(2);
        Assert.Contains(Sfx.SayPaid, heard);
    }

    [Fact]
    public void The_keys_fly_it_and_fire_and_use()
    {
        using var input = new InputHandler();
        input.OnKeyDown(Key.Up);
        Assert.Equal(-1, input.Player.Dy);
        // Up through to the skimmer: it flies forward.
        var w = City(empty: true);
        double y = w.Ship.Y;
        Run(w, 60, input.Player);
        Assert.True(w.Ship.Y > y + 20);
        input.OnKeyUp(Key.Up);
        input.OnKeyDown(Key.Down);
        Assert.Equal(1, input.Player.Dy);
        input.OnKeyUp(Key.Down);
        input.OnKeyDown(Key.Left);
        Assert.Equal(-1, input.Player.Dx);
        input.OnKeyUp(Key.Left);
        foreach (var (key, check) in new (Key, Func<Controls, bool>)[]
                 {
                     (Key.Space, c => c.Fire), (Key.X, c => c.Use), (Key.Q, c => c.Climb == 1), (Key.A, c => c.Climb == -1),
                     (Key.PageUp, c => c.Climb == 1), (Key.PageDown, c => c.Climb == -1),
                 })
        {
            input.OnKeyDown(key);
            Assert.True(check(input.Player), key.ToString());
            input.OnKeyUp(key);
        }
    }
}
