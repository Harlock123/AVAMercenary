using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAMercenary.Engine;

namespace AVAMercenary.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAMercenary/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Cyan = 0xFF6AE8FF, Orange = 0xFFFF9A30;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);


    private bool InGame => Engine.State is GameState.Attract or GameState.Playing or GameState.Paused;

    // The view out of the skimmer, above the instrument panel.
    private const double ViewH = 560, Focal = 600, Near = 4, Far = 3600, HorizonY = 250;
    private static readonly (double X, double Y, double Z) Light = Norm(0.45, -0.55, 0.7);
    private const uint SkyTop = 0xFF0C1438, SkyLow = 0xFF5A78B0, Haze = 0xFF7A8EB8, Earth = 0xFF5E6A3E, Road = 0xFF3C3E46;
    private static readonly uint[] Walls = [0xFFB0B8C8, 0xFFC8A878, 0xFF88A0B8, 0xFFA8B890, 0xFFC09090, 0xFF9890B8];

    private World? _titleWorld;

    private readonly record struct Cam(double X, double Y, double Z, double Sin, double Cos, double RSin, double RCos);
    private readonly record struct V3(double S, double U, double F);

    private sealed class Item
    {
        public double Depth;
        public Point[] Pts = [];
        public uint Fill, Edge;
        public double Width = 1, R;
        public int Kind; // 0 polygon, 1 line, 2 ring
    }

    private readonly List<Item> _items = [];
    private readonly List<Item> _ground = [];

    private static (double X, double Y, double Z) Norm(double x, double y, double z)
    {
        double l = Math.Sqrt(x * x + y * y + z * z);
        return (x / l, y / l, z / l);
    }

    private static uint Mix(uint a, uint b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        uint Ch(int sh) => (uint)Math.Round(((a >> sh) & 0xFF) * (1 - t) + ((b >> sh) & 0xFF) * t);
        return 0xFF000000 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }

    private static uint Shade(uint c, double k) => Mix(0xFF000000, c, k);

    private static double Fog(double d) => Math.Clamp((d - 1700) / (Far - 1700), 0, 0.92);

    private static V3 ToCam(in Cam c, double x, double y, double z)
    {
        double dx = x - c.X, dy = y - c.Y;
        return new V3(dx * c.Cos - dy * c.Sin, z - c.Z, dx * c.Sin + dy * c.Cos);
    }

    private static Point Screen(in Cam c, V3 v)
    {
        double px = v.S / v.F * Focal, py = -v.U / v.F * Focal;
        return new Point(Field.Width / 2 + px * c.RCos - py * c.RSin, HorizonY + px * c.RSin + py * c.RCos);
    }

    private static Point[]? Project(in Cam c, List<V3> poly)
    {
        // Clip against the near plane, then onto the screen.
        var outp = new List<V3>(poly.Count + 2);
        for (int i = 0; i < poly.Count; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % poly.Count];
            bool ina = a.F >= Near, inb = b.F >= Near;
            if (ina) outp.Add(a);
            if (ina != inb)
            {
                double t = (Near - a.F) / (b.F - a.F);
                outp.Add(new V3(a.S + (b.S - a.S) * t, a.U + (b.U - a.U) * t, Near));
            }
        }
        if (outp.Count < 2) return null;
        var pts = new Point[outp.Count];
        for (int i = 0; i < pts.Length; i++) pts[i] = Screen(c, outp[i]);
        return pts;
    }

    private void Face(List<Item> into, in Cam c, double depth, uint fill, uint edge, params (double X, double Y, double Z)[] v)
    {
        var poly = new List<V3>(v.Length);
        foreach (var p in v) poly.Add(ToCam(c, p.X, p.Y, p.Z));
        if (Project(c, poly) is { Length: >= 3 } pts) into.Add(new Item { Depth = depth, Pts = pts, Fill = fill, Edge = edge });
    }

    private void Line(List<Item> into, in Cam c, double depth, uint colour, double width, (double X, double Y, double Z) a, (double X, double Y, double Z) b)
    {
        var va = ToCam(c, a.X, a.Y, a.Z);
        var vb = ToCam(c, b.X, b.Y, b.Z);
        if (va.F < Near && vb.F < Near) return;
        if (va.F < Near) va = Cut(vb, va);
        else if (vb.F < Near) vb = Cut(va, vb);
        into.Add(new Item { Depth = depth, Pts = [Screen(c, va), Screen(c, vb)], Edge = colour, Width = width, Kind = 1 });

        static V3 Cut(V3 p, V3 q)
        {
            double t = (Near - p.F) / (q.F - p.F);
            return new V3(p.S + (q.S - p.S) * t, p.U + (q.U - p.U) * t, Near);
        }
    }

    /// <summary>One face of a convex solid, if it faces the eye: lit by the sun, faded by the haze.</summary>
    private void Solid(in Cam c, (double X, double Y, double Z) centre, double depth, uint colour, params (double X, double Y, double Z)[] v)
    {
        var (a, b, d) = (v[0], v[1], v[2]);
        double ux = b.X - a.X, uy = b.Y - a.Y, uz = b.Z - a.Z, wx = d.X - a.X, wy = d.Y - a.Y, wz = d.Z - a.Z;
        var n = Norm(uy * wz - uz * wy, uz * wx - ux * wz, ux * wy - uy * wx);
        double mx = v.Average(p => p.X), my = v.Average(p => p.Y), mz = v.Average(p => p.Z);
        if (n.X * (mx - centre.X) + n.Y * (my - centre.Y) + n.Z * (mz - centre.Z) < 0) n = (-n.X, -n.Y, -n.Z);
        if (n.X * (c.X - a.X) + n.Y * (c.Y - a.Y) + n.Z * (c.Z - a.Z) <= 0) return;
        double lit = 0.5 + 0.5 * Math.Max(0, n.X * Light.X + n.Y * Light.Y + n.Z * Light.Z);
        uint fill = Mix(Shade(colour, lit), Haze, Fog(depth));
        Face(_items, c, depth, fill, Mix(Shade(colour, lit * 0.55), Haze, Fog(depth)), v);
    }

    private void Box(in Cam c, double depth, uint colour, double x, double y, double z0, double w, double d, double h)
    {
        double x0 = x - w / 2, x1 = x + w / 2, y0 = y - d / 2, y1 = y + d / 2, z1 = z0 + h;
        var m = (x, y, z0 + h / 2);
        Solid(c, m, depth, colour, (x1, y0, z0), (x1, y1, z0), (x1, y1, z1), (x1, y0, z1));
        Solid(c, m, depth, colour, (x0, y0, z0), (x0, y0, z1), (x0, y1, z1), (x0, y1, z0));
        Solid(c, m, depth, colour, (x0, y1, z0), (x0, y1, z1), (x1, y1, z1), (x1, y1, z0));
        Solid(c, m, depth, colour, (x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1));
        Solid(c, m, depth, colour, (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1));
    }

    private void Pyramid(in Cam c, double depth, uint colour, double x, double y, double z0, double w, double d, double h, double apexX = 0, double apexY = 0)
    {
        double x0 = x - w / 2, x1 = x + w / 2, y0 = y - d / 2, y1 = y + d / 2;
        var top = (x + apexX, y + apexY, z0 + h);
        var m = (x, y, z0 + h / 3);
        Solid(c, m, depth, colour, (x1, y0, z0), (x1, y1, z0), top);
        Solid(c, m, depth, colour, (x0, y1, z0), (x0, y0, z0), top);
        Solid(c, m, depth, colour, (x1, y1, z0), (x0, y1, z0), top);
        Solid(c, m, depth, colour, (x0, y0, z0), (x1, y0, z0), top);
    }

    private static Cam MakeCam(Skimmer s, double eye) =>
        new(s.X, s.Y, s.Alt + eye, Math.Sin(s.Heading), Math.Cos(s.Heading), Math.Sin(-s.Bank), Math.Cos(-s.Bank));

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF000000), new Rect(0, 0, Field.Width, Field.Height));
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            case GameState.GameOver: DrawGameOver(dc); break;
            default:
                var w = g.World;
                using (dc.PushClip(new Rect(0, 0, Field.Width, ViewH)))
                {
                    if (g.Phase == Phase.Escaped)
                    {
                        // Watching from the pad's edge as the ship lifts off.
                        var port = w.City.Spaceport;
                        var eye = new Skimmer { X = port.X - 70, Y = port.Y - 330, Alt = 30 + g.PhaseTime * 8, Heading = 0.2 };
                        DrawCity(dc, w, MakeCam(eye, 0), false, Math.Pow(Math.Max(0, g.PhaseTime - 0.4), 2) * 30);
                    }
                    else
                    {
                        DrawCity(dc, w, MakeCam(w.Ship, 6), true);
                        DrawSight(dc, w);
                    }
                    if (g.Phase == Phase.Dying)
                        dc.FillRectangle(Brush(Alpha(0xFFFF3020, Math.Max(0, 0.6 - g.PhaseTime * 0.3))), new Rect(0, 0, Field.Width, ViewH));
                    else if (w.Ship.Shields < 25 && (int)(_clock * 4) % 2 == 0)
                        dc.DrawRectangle(null, Pen(0xA0FF2020, 8), new Rect(4, 4, Field.Width - 8, ViewH - 8));
                }
                Text(dc, $"SCORE {g.Score:000000}", new Point(16, 22), 28, White);
                Text(dc, $"SKIMMERS {Math.Max(0, g.Lives)}", new Point(Field.Width - 16, 22), 28, White, right: true);
                DrawPanel(dc, w);
                if (g.Message is { } msg)
                {
                    double wdt = msg.Length * 17 + 40;
                    dc.FillRectangle(Brush(0xB0000000), new Rect(512 - wdt / 2, 150, wdt, 56), 6);
                    Text(dc, msg, new Point(512, 178), 38, g.Phase == Phase.Dying ? Red : Yellow, center: true);
                }
                break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(512, 300), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(512, 360), 26, White, center: true);
        }
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(512, 60), 22, Yellow, center: true);
    }

    /// <summary>Targ city from the skimmer: sky and ground, the roads, then everything standing, farthest first.</summary>
    private void DrawCity(DrawingContext dc, World w, Cam c, bool ship, double rise = 0)
    {
        var city = w.City;
        // Sky, and the ground up to the horizon (tilted with the skimmer's bank).
        dc.FillRectangle(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromUInt32(SkyTop), 0), new GradientStop(Color.FromUInt32(SkyLow), 0.55), new GradientStop(Color.FromUInt32(Haze), 1) },
        }, new Rect(0, 0, Field.Width, ViewH));
        Point Tilt(double x, double y) => new(Field.Width / 2 + x * c.RCos - y * c.RSin, HorizonY + x * c.RSin + y * c.RCos);
        Poly(dc, Mix(Earth, Haze, 0.55), null, 0, Tilt(-3000, 0), Tilt(3000, 0), Tilt(3000, 3000), Tilt(-3000, 3000));
        Poly(dc, Earth, null, 0, Tilt(-3000, 6), Tilt(3000, 6), Tilt(3000, 3000), Tilt(-3000, 3000));
        _items.Clear();
        _ground.Clear();
        // The roads along the sectors' edges, and the pads and depots.
        for (int k = 0; k <= Field.Sectors; k++)
        {
            double at = k * Field.SectorSize;
            double lo0 = Math.Max(0, c.Y - Far), hi0 = Math.Min(Field.Size, c.Y + Far), lo1 = Math.Max(0, c.X - Far), hi1 = Math.Min(Field.Size, c.X + Far);
            if (Math.Abs(at - c.X) < Far && lo0 < hi0) RoadStrip(c, at, lo0, hi0, true);
            if (Math.Abs(at - c.Y) < Far && lo1 < hi1) RoadStrip(c, at, lo1, hi1, false);
        }
        void Pad(double x, double y, double r, uint colour)
        {
            if (Sim.Dist(x, y, c.X, c.Y) > Far) return;
            Face(_ground, c, 0, Mix(colour, Earth, 0.35), colour, (x - r, y - r, 0), (x + r, y - r, 0), (x + r, y + r, 0), (x - r, y + r, 0));
        }
        Pad(city.Palyar.X, city.Palyar.Y - 190, 50, 0xFFE0C060);
        Pad(city.Mechanoid.X, city.Mechanoid.Y - 190, 50, 0xFF70A0E0);
        Pad(city.Spaceport.X, city.Spaceport.Y, 110, 0xFFD8D8E0);
        foreach (var d in city.Depots) Pad(d.X, d.Y, 35, 0xFFB08040);
        foreach (var it in _ground)
            if (it.Pts.Length >= 3) Poly(dc, it.Fill, it.Edge, 1.5, it.Pts);
        // Buildings.
        foreach (var b in city.Buildings)
        {
            double d = Sim.Dist(b.X, b.Y, c.X, c.Y);
            if (d > Far + 150) continue;
            switch (b.Shape)
            {
                case Shape.Headquarters when b.Owner == Faction.Palyar:
                    Pyramid(c, d, 0xFFE8C058, b.X, b.Y, 0, b.W, b.D, b.H);
                    break;
                case Shape.Headquarters:
                    Box(c, d + 0.2, 0xFF6890D0, b.X, b.Y, 0, b.W, b.D, b.H * 0.45);
                    Box(c, d + 0.1, 0xFF7AA0E0, b.X, b.Y, b.H * 0.45, b.W * 0.65, b.D * 0.65, b.H * 0.35);
                    Pyramid(c, d, 0xFFE05050, b.X, b.Y, b.H * 0.8, b.W * 0.4, b.D * 0.4, b.H * 0.2);
                    break;
                case Shape.Hangar:
                    Box(c, d, 0xFF9AA0A8, b.X, b.Y, 0, b.W, b.D, b.H);
                    break;
                default:
                    Box(c, d, Walls[Hash((int)b.X, (int)b.Y, 3) % Walls.Length], b.X, b.Y, 0, b.W, b.D, b.H);
                    break;
            }
        }
        // The Mechanoids' relay towers: lattice masts, or wreckage.
        foreach (var t in w.Towers)
        {
            var td = t.Def;
            double d = Sim.Dist(td.X, td.Y, c.X, c.Y);
            if (d > Far) continue;
            if (t.Down) { Pyramid(c, d, 0xFF6A4A3A, td.X, td.Y, 0, 50, 40, 14, 8, -5); continue; }
            uint col = Mix(0xFFFF7040, Haze, Fog(d));
            double b0 = 16, b1 = 4;
            (double, double, double) Leg(int i, double z)
            {
                double r = b0 + (b1 - b0) * z / td.H;
                return (td.X + (i is 0 or 3 ? -r : r), td.Y + (i < 2 ? -r : r), z);
            }
            for (int i = 0; i < 4; i++) Line(_items, c, d, col, 2, Leg(i, 0), Leg(i, td.H));
            for (double z = 0; z < td.H - 10; z += 28)
                for (int i = 0; i < 4; i++)
                {
                    Line(_items, c, d, col, 1, Leg(i, z), Leg((i + 1) % 4, z + 28));
                    Line(_items, c, d, col, 1, Leg(i, z + 28), Leg((i + 1) % 4, z + 28));
                }
            double spin = _clock * 1.6 + td.X;
            var tip = (td.X, td.Y, td.H + 4);
            Line(_items, c, d - 0.1, Mix(0xFFFFE060, Haze, Fog(d)), 3, (td.X - Math.Sin(spin) * 18, td.Y - Math.Cos(spin) * 18, td.H + 4), (td.X + Math.Sin(spin) * 18, td.Y + Math.Cos(spin) * 18, td.H + 4));
            Line(_items, c, d - 0.1, col, 2, tip, (td.X, td.Y, td.H + 20));
        }
        // The crate waiting at its depot; the ship waiting at the spaceport.
        if (w.Job?.Kind == JobKind.Crate && !w.Ship.Crate && w.CrateAt >= 0)
        {
            var (x, y) = city.Depots[w.CrateAt];
            Box(c, Sim.Dist(x, y, c.X, c.Y), 0xFFB07838, x, y, 0, 20, 20, 16);
        }
        {
            var (x, y) = city.Spaceport;
            double d = Sim.Dist(x, y, c.X, c.Y);
            if (d < Far + 100)
            {
                Box(c, d + 0.1, 0xFFDDE0F0, x, y - 10, 4 + rise, 34, 90, 22);
                Pyramid(c, d, 0xFFDDE0F0, x, y + 60, 4 + rise, 34, 50, 22, 0, 25);
                Box(c, d + 0.3, 0xFF505868, x, y - 10, 0, 60, 40, 6);
                if (rise > 0)
                {
                    // The drive's glare under it.
                    var v = ToCam(c, x, y - 10, rise + 2);
                    if (v.F > Near)
                        for (int i = 0; i < 3; i++)
                            _items.Add(new Item { Depth = d + 0.2, Pts = [Screen(c, v)], R = (8 + i * 7 + (int)(_clock * 20) % 3 * 2) / v.F * Focal, Kind = 2, Fill = Alpha(i == 0 ? 0xFFFFF0A0 : 0xFFFF9A30, 0.9 - i * 0.25) });
                }
            }
        }
        // Drones: spinning diamonds.
        foreach (var dr in w.Drones)
        {
            double d = Sim.Dist(dr.X, dr.Y, c.X, c.Y);
            double r = 16, a = _clock * 4 + dr.X;
            var top = (dr.X, dr.Y, dr.Z + r);
            var bot = (dr.X, dr.Y, dr.Z - r);
            var ring = Enumerable.Range(0, 4).Select(i => (dr.X + Math.Sin(a + i * Math.PI / 2) * r, dr.Y + Math.Cos(a + i * Math.PI / 2) * r, dr.Z)).ToArray();
            uint col = dr.Of == Faction.Palyar ? 0xFFF0B030 : 0xFFE040E0;
            var m = (dr.X, dr.Y, dr.Z);
            for (int i = 0; i < 4; i++)
            {
                Solid(c, m, d, col, ring[i], ring[(i + 1) % 4], top);
                Solid(c, m, d, col, ring[(i + 1) % 4], ring[i], bot);
            }
        }
        // Bolts and bursts.
        foreach (var b in w.Bolts)
        {
            double d = Sim.Dist(b.X, b.Y, c.X, c.Y);
            Line(_items, c, d, b.Mine ? 0xFFFFF080 : 0xFFFF5040, 3, (b.X, b.Y, b.Z), (b.X - b.Vx * 0.03, b.Y - b.Vy * 0.03, b.Z - b.Vz * 0.03));
        }
        foreach (var f in w.Flashes)
        {
            var v = ToCam(c, f.X, f.Y, f.Z);
            if (v.F < Near) continue;
            double r = (f.Big ? 70 : 18) * (0.3 + f.T * 1.4) / v.F * Focal;
            _items.Add(new Item { Depth = Math.Sqrt(v.F * v.F + v.S * v.S) - 1, Pts = [Screen(c, v)], R = r, Kind = 2, Fill = Alpha(f.Big ? 0xFFFFA040 : 0xFFFFF0A0, 1 - f.T / 0.8) });
        }
        foreach (var it in _items.OrderByDescending(i => i.Depth))
            switch (it.Kind)
            {
                case 0: Poly(dc, it.Fill, it.Edge, 1, it.Pts); break;
                case 1: dc.DrawLine(Pen(it.Edge, it.Width), it.Pts[0], it.Pts[1]); break;
                default:
                    dc.DrawEllipse(Brush(Alpha(it.Fill, ((it.Fill >> 24) & 0xFF) / 255.0 * 0.5)), Pen(it.Fill, 3), it.Pts[0], it.R, it.R);
                    break;
            }
        // The skimmer's nose, at the foot of the view.
        if (ship)
        {
            Poly(dc, 0xFF2A2E3A, 0xFF4A5060, 2, new Point(300, ViewH), new Point(440, ViewH - 46), new Point(584, ViewH - 46), new Point(724, ViewH));
            if (w.Ship.Crate) Poly(dc, 0xFFB07838, 0xFF603818, 2, new Point(480, ViewH - 46), new Point(490, ViewH - 70), new Point(534, ViewH - 70), new Point(544, ViewH - 46));
        }
    }

    private void RoadStrip(in Cam c, double at, double lo, double hi, bool northSouth)
    {
        const double half = 26;
        // Cut into lengths so the near-plane clip keeps the far end in place.
        for (double a = lo; a < hi; a += 400)
        {
            double b = Math.Min(hi, a + 400);
            if (northSouth)
                Face(_ground, c, 0, Mix(Road, Haze, Fog(Math.Abs((a + b) / 2 - c.Y))), 0, (at - half, a, 0), (at + half, a, 0), (at + half, b, 0), (at - half, b, 0));
            else
                Face(_ground, c, 0, Mix(Road, Haze, Fog(Math.Abs((a + b) / 2 - c.X))), 0, (a, at - half, 0), (b, at - half, 0), (b, at + half, 0), (a, at + half, 0));
        }
    }

    private void DrawSight(DrawingContext dc, World w)
    {
        if (w.Ship.Dead) return;
        uint sight = 0xC060FF60;
        double cy = HorizonY;
        dc.DrawLine(Pen(sight, 2), new Point(512 - 30, cy), new Point(512 - 10, cy));
        dc.DrawLine(Pen(sight, 2), new Point(512 + 10, cy), new Point(512 + 30, cy));
        dc.DrawLine(Pen(sight, 2), new Point(512, cy - 24), new Point(512, cy - 8));
        dc.DrawLine(Pen(sight, 2), new Point(512, cy + 8), new Point(512, cy + 24));
    }

    /// <summary>The instrument panel: position and flight, the compass, shields and credits, the job, the city map, and what the ship's computer has to say.</summary>
    private void DrawPanel(DrawingContext dc, World w)
    {
        var s = w.Ship;
        var city = w.City;
        double y0 = ViewH;
        dc.FillRectangle(Brush(0xFF1C1E26), new Rect(0, y0, Field.Width, Field.Height - y0));
        dc.FillRectangle(Brush(0xFF3A3E4A), new Rect(0, y0, Field.Width, 4));
        // Readouts.
        Text(dc, "LOC " + CityDef.SectorName(s.X, s.Y), new Point(20, y0 + 26), 30, Cyan);
        Text(dc, $"ALT {s.Alt,4:0}", new Point(20, y0 + 54), 30, Cyan);
        Text(dc, $"SPD {s.Speed,4:0}", new Point(20, y0 + 82), 30, Cyan);
        // Shields.
        Text(dc, "SHIELDS", new Point(170, y0 + 26), 24, Dim);
        double sh = Math.Clamp(s.Shields / Skimmer.Shields0, 0, 1);
        dc.FillRectangle(Brush(0xFF0A0C12), new Rect(170, y0 + 42, 110, 14));
        dc.FillRectangle(Brush(sh < 0.25 ? Red : sh < 0.5 ? Orange : 0xFF50E070), new Rect(170, y0 + 42, 110 * sh, 14));
        Text(dc, $"CREDITS {w.Credits}", new Point(170, y0 + 82), 26, Yellow);
        // The compass strip.
        double cx0 = 300, cx1 = 780, ccx = (cx0 + cx1) / 2;
        dc.FillRectangle(Brush(0xFF0A0C12), new Rect(cx0, y0 + 12, cx1 - cx0, 34));
        using (dc.PushClip(new Rect(cx0, y0 + 12, cx1 - cx0, 34)))
        {
            double deg = s.Heading * 180 / Math.PI;
            for (int a = -360; a <= 720; a += 15)
            {
                double x = ccx + (a - deg) * 2.2;
                if (x < cx0 - 20 || x > cx1 + 20) continue;
                int n = ((a % 360) + 360) % 360;
                string? lab = n switch { 0 => "N", 90 => "E", 180 => "S", 270 => "W", _ => null };
                if (lab != null) Text(dc, lab, new Point(x, y0 + 26), 24, White, center: true, shadow: false);
                else dc.DrawLine(Pen(Dim, 1), new Point(x, y0 + 44), new Point(x, y0 + (n % 45 == 0 ? 32 : 38)));
            }
            // Where the job's target lies.
            if (Target(w) is { } tg)
            {
                double b = Sim.Wrap(Sim.Bearing(s.X, s.Y, tg.X, tg.Y) - s.Heading) * 180 / Math.PI;
                double x = Math.Clamp(ccx + b * 2.2, cx0 + 6, cx1 - 6);
                Poly(dc, Yellow, null, 0, new Point(x, y0 + 40), new Point(x - 6, y0 + 46), new Point(x + 6, y0 + 46));
            }
        }
        dc.DrawLine(Pen(Yellow, 2), new Point(ccx, y0 + 10), new Point(ccx, y0 + 20));
        // The job, and who is friendly.
        Text(dc, JobText(w), new Point(cx0, y0 + 64), 24, White);
        string Feel(Faction f) => w.Standing[(int)f] < 0 ? "HOSTILE" : w.Standing[(int)f] > 0 ? "FRIENDLY" : "NEUTRAL";
        Text(dc, $"PALYARS {Feel(Faction.Palyar)}   MECHANOIDS {Feel(Faction.Mechanoid)}", new Point(cx0, y0 + 90), 22, Dim);
        // The computer's log.
        for (int i = 0; i < w.Log.Count && i < 3; i++)
        {
            int k = w.Log.Count - 1 - i;
            Text(dc, w.Log[k], new Point(20, y0 + 186 - i * 24), 22, i == 0 ? 0xFF90FF90 : 0xFF4A8A5A);
        }
        // The city map.
        double mx = 820, my = y0 + 12, cell = 20, size = cell * Field.Sectors;
        double Mx(double x) => mx + x / Field.Size * size;
        double My(double y) => my + size - y / Field.Size * size;
        dc.FillRectangle(Brush(0xFF0A1410), new Rect(mx, my, size, size));
        for (int k = 0; k <= Field.Sectors; k++)
        {
            dc.DrawLine(Pen(0xFF1E3A28, 1), new Point(mx + k * cell, my), new Point(mx + k * cell, my + size));
            dc.DrawLine(Pen(0xFF1E3A28, 1), new Point(mx, my + k * cell), new Point(mx + size, my + k * cell));
        }
        dc.FillRectangle(Brush(0xFFE0C060), new Rect(Mx(city.Palyar.X) - 5, My(city.Palyar.Y) - 5, 10, 10));
        dc.FillRectangle(Brush(0xFF70A0E0), new Rect(Mx(city.Mechanoid.X) - 5, My(city.Mechanoid.Y) - 5, 10, 10));
        dc.DrawEllipse(null, Pen(White, 2), new Point(Mx(city.Spaceport.X), My(city.Spaceport.Y)), 6, 6);
        foreach (var t in w.Towers)
            if (!t.Down) dc.FillEllipse(Mx(t.Def.X), My(t.Def.Y), 2.5, 0xFFFF7040);
        foreach (var d in w.Drones) dc.FillEllipse(Mx(d.X), My(d.Y), 3, 0xFFE040E0);
        if (Target(w) is { } at && (int)(_clock * 3) % 2 == 0) dc.DrawEllipse(null, Pen(Yellow, 2), new Point(Mx(at.X), My(at.Y)), 7, 7);
        var (fx, fy) = Sim.Ahead(s.Heading);
        double sx = Mx(s.X), sy = My(s.Y);
        Poly(dc, 0xFF80FF90, 0xFF0A1410, 1, new Point(sx + fx * 10, sy - fy * 10), new Point(sx - fx * 6 + fy * 6, sy + fy * 6 + fx * 6), new Point(sx - fx * 3, sy + fy * 3), new Point(sx - fx * 6 - fy * 6, sy + fy * 6 - fx * 6));
    }

    /// <summary>Where the job (or passage home) is.</summary>
    private static (double X, double Y)? Target(World w)
    {
        var c = w.City;
        if (w.Credits >= c.ShipPrice) return c.Spaceport;
        return w.Job switch
        {
            { Kind: JobKind.Tower } j => (w.Towers[j.Target].Def.X, w.Towers[j.Target].Def.Y),
            { Kind: JobKind.Crate } when w.Ship.Crate => c.Mechanoid,
            { Kind: JobKind.Crate } when w.CrateAt >= 0 => c.Depots[w.CrateAt],
            _ => null,
        };
    }

    private static string JobText(World w)
    {
        var c = w.City;
        if (w.Credits >= c.ShipPrice) return "PASSAGE PAID FOR: TO THE SPACEPORT " + CityDef.SectorName(c.Spaceport.X, c.Spaceport.Y);
        return w.Job switch
        {
            { Kind: JobKind.Tower } j => $"JOB: TOWER IN {CityDef.SectorName(w.Towers[j.Target].Def.X, w.Towers[j.Target].Def.Y)} ({j.Reward})",
            { Kind: JobKind.Crate } j when w.Ship.Crate => $"JOB: CRATE TO THE MECHANOIDS ({j.Reward})",
            { Kind: JobKind.Crate } j when w.CrateAt >= 0 => $"JOB: CRATE AT {CityDef.SectorName(c.Depots[w.CrateAt].X, c.Depots[w.CrateAt].Y)} ({j.Reward})",
            _ => "NO JOB: ASK AT A HEADQUARTERS",
        };
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        if (_titleWorld == null)
        {
            _titleWorld = new World();
            Sim.Start(_titleWorld, CityBook.Build(1), 1);
        }
        // A slow circuit over the city behind the title.
        var s = _titleWorld.Ship;
        double a = _clock * 0.05;
        s.X = 3600 + Math.Sin(a) * 1400;
        s.Y = 3600 + Math.Cos(a) * 1400;
        s.Heading = a + Math.PI / 2 + 0.35;
        s.Alt = 230;
        s.Bank = 0.08;
        using (dc.PushClip(new Rect(0, 0, Field.Width, Field.Height)))
            DrawCity(dc, _titleWorld, MakeCam(s, 0), false);
        dc.FillRectangle(Brush(0xFF1C1E26), new Rect(0, ViewH, Field.Width, Field.Height - ViewH));
        dc.FillRectangle(Brush(0xA0000000), new Rect(0, 30, Field.Width, 140));
        Text(dc, "AVA MERCENARY", new Point(cx, 88), 100, Yellow, center: true);
        Text(dc, "AFTER NOVAGEN'S 1985 MERCENARY: ESCAPE FROM TARG", new Point(cx, 148), 22, Cyan, center: true);
        dc.FillRectangle(Brush(0xB0000000), new Rect(0, 470, Field.Width, 298));
        Text(dc, "CRASH-LANDED IN TARG CITY, YOU NEED 20000 CREDITS FOR PASSAGE OFF THE PLANET.", new Point(cx, 492), 21, White, center: true);
        Text(dc, "THE PALYARS PAY TO BRING DOWN MECHANOID TOWERS; THE MECHANOIDS PAY FOR CRATES FETCHED HOME.", new Point(cx, 518), 21, White, center: true);
        Text(dc, "WORK FOR ONE AND THE OTHER SENDS ITS DRONES. THEN FLY TO THE SPACEPORT AND BUY YOUR WAY OUT.", new Point(cx, 544), 21, White, center: true);
        Text(dc, "PRESS ENTER TO START", new Point(cx, 600), 40, (int)(_clock * 2) % 2 == 0 ? Yellow : Orange, center: true);
        Text(dc, "ARROWS: TURN, FASTER/SLOWER   Q/A: CLIMB/DIVE   SPACE: FIRE   X: USE   P: PAUSE", new Point(cx, 660), 20, Dim, center: true);
        Text(dc, $"HIGH SCORE {Engine.HighScores.TopScore:000000}", new Point(cx, 710), 24, Cyan, center: true);
    }

    private void DrawGameOver(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        Text(dc, "GAME OVER", new Point(cx, 240), 110, Red, center: true);
        Text(dc, Engine.Cleared > 0 ? "YOU GOT OFF TARG" : "STRANDED ON TARG", new Point(cx, 330), 34, White, center: true);
        Text(dc, $"SCORE {Engine.Score}", new Point(cx, 420), 44, Yellow, center: true);
        Text(dc, $"JOBS DONE {Engine.World.JobsDone}", new Point(cx, 476), 30, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "THE BEST MERCENARIES", new Point(cx, 120), 64, 0xFFFFE040, center: true);
        Text(dc, "RANK NAME    SCORE  ESCAPED", new Point(cx, 190), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {(e.Stage > 0 ? "YES" : "NO"),6}", new Point(cx, 236 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 680), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}

internal static class DrawingExtensions
{
    public static void FillEllipse(this DrawingContext dc, double x, double y, double r, uint colour) =>
        dc.DrawEllipse(GameCanvas.Brush(colour), null, new Point(x, y), r, r);
    public static void FillRectangle(this DrawingContext dc, IBrush brush, Rect rect, double radius) => dc.FillRectangle(brush, rect, (float)radius);
}
