using AVAMercenary.Engine;

namespace AVAMercenary.Tests;

/// <summary>How the computer mercenary gets on (set MERC_PROBE to run it, for tuning).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_mercenary_does()
    {
        if (Environment.GetEnvironmentVariable("MERC_PROBE") is not { } mode) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int seed = 1; seed <= 5; seed++)
        {
            var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "merc-probe-" + Guid.NewGuid().ToString("N"))), seed) { Silent = true };
            g.Go();
            g.SetLives(30);
            int t = 0, lost = 0;
            var last = g.Phase;
            var doing = new Dictionary<string, int>();
            var causes = new Dictionary<string, int>();
            for (; t < 60 * 60 * (mode == "lazy" ? 5 : 25) && g.State == GameState.Playing && g.Phase != Phase.Escaped; t++)
            {
                var c = mode == "lazy" ? new Controls(0, -1, true) : g.Pilot.Decide(g);
                g.Tick(c);
                if (g.Phase == Phase.Dying && last != Phase.Dying) { lost++; causes[g.World.Cause] = causes.GetValueOrDefault(g.World.Cause) + 1; }
                last = g.Phase;
                doing[g.Pilot.Doing] = doing.GetValueOrDefault(g.Pilot.Doing) + 1;
            }
            var w = g.World;
            output.WriteLine($"seed {seed}: {(g.Phase == Phase.Escaped ? "ESCAPED" : "stuck")} after {t / 60}s, {lost} lost ({string.Join(", ", causes.Select(c => $"{c.Key} {c.Value}"))}), {w.JobsDone} jobs, credits {w.Credits}, standing {w.Standing[0]}/{w.Standing[1]}; {string.Join(", ", doing.OrderByDescending(d => d.Value).Take(6).Select(d => $"{d.Key} {d.Value / 60}s"))}");
            foreach (var l in w.Log) output.WriteLine("    " + l);
        }
        output.WriteLine($"total {sw.ElapsedMilliseconds} ms");
    }
}
