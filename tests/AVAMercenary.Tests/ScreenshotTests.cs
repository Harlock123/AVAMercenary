using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAMercenary.Engine;
using AVAMercenary.Views;
using Controls = AVAMercenary.Engine.Controls;

namespace AVAMercenary.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set MERC_SHOTS to a folder to save
/// them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 1)
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avamercenary-ui-" + Guid.NewGuid().ToString("N"))), seed) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("MERC_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAMercenary-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer mercenary play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            g.Tick(g.Pilot.Decide(g));
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }

    private static void Play(string name, Func<GameEngine, bool> moment, int seconds = 400, Action<GameEngine>? setup = null)
    {
        var (w, c, g) = Open();
        g.Go();
        g.SetLives(20);
        g.Pilot.Reset();
        setup?.Invoke(g);
        Assert.True(PlayUntil(g, c, moment, seconds), $"never saw the moment for {name}");
        Shot(w, c, name);
    }

    private static bool Ahead(GameEngine g, double x, double y, double lo, double hi, double off = 0.3)
    {
        var s = g.World.Ship;
        double d = Sim.Dist(s.X, s.Y, x, y);
        return d > lo && d < hi && Math.Abs(Sim.Wrap(Sim.Bearing(s.X, s.Y, x, y) - s.Heading)) < off;
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("ACE", 31250, 1);
        g.HighScores.Insert("AVA", 7400, 0);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 300 && g.State != GameState.HighScores; i++) g.Idle(1);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void Over_the_city() => Play("city", g => g.World.Clock > 14 && g.World.Ship.Alt > 300 && g.World.Ship.Speed > 90);

    [AvaloniaFact]
    public void The_Palyar_pyramid() => Play("palyars", g => g.World.Job == null && Ahead(g, g.World.City.Palyar.X, g.World.City.Palyar.Y, 450, 900, 0.25));

    [AvaloniaFact]
    public void A_tower_under_fire() => Play("tower", g => g.World.Job is { Kind: JobKind.Tower } j && g.World.Bolts.Count(b => b.Mine) >= 2
        && Ahead(g, g.World.Towers[j.Target].Def.X, g.World.Towers[j.Target].Def.Y, 150, 500, 0.1));

    [AvaloniaFact]
    public void A_drone() => Play("drone", g => g.World.Drones.Any(d => Ahead(g, d.X, d.Y, 120, 400, 0.25)) && g.World.Bolts.Count >= 1, 600);

    [AvaloniaFact]
    public void A_crate_to_fetch() => Play("crate", g => g.World.Job is { Kind: JobKind.Crate } && g.World.CrateAt >= 0
        && g.World.Ship.Alt < 90 && Ahead(g, g.World.City.Depots[g.World.CrateAt].X, g.World.City.Depots[g.World.CrateAt].Y, 50, 260, 0.3), 600,
        g => g.World.Standing[(int)Faction.Palyar] = -1);

    [AvaloniaFact]
    public void Escaped()
    {
        var (w, c, g) = Open();
        g.Go();
        var world = g.World;
        world.Credits = world.City.ShipPrice;
        var port = world.City.Spaceport;
        (world.Ship.X, world.Ship.Y, world.Ship.Alt, world.Ship.Heading) = (port.X - 20, port.Y - 110, 10, 0.15);
        g.Run(1);
        g.Run(1, new Controls(Use: true));
        for (int i = 0; i < 140; i++) { g.Tick(default); if (i % 3 == 0) c.Advance(0.05); }
        Assert.Equal(Phase.Escaped, g.Phase);
        Shot(w, c, "escaped");
    }

    [AvaloniaFact]
    public void Game_over()
    {
        var (w, c, g) = Open();
        g.Go();
        g.SetLives(1);
        g.World.Credits += 6000;
        g.Run(1);
        g.World.Ship.Shields = 0;
        for (int i = 0; i < 600 && !(g.State == GameState.GameOver && g.StateTime > 1); i++) { g.Tick(default); if (i % 3 == 0) c.Advance(0.05); }
        Shot(w, c, "game-over");
    }
}
