using Avalonia.Input;

namespace AVAMercenary.Engine;

/// <summary>
/// Keyboard and gamepad boiled down to the controls: left and right, up and down (up the screen is -1), climb and
/// dive, fire and use.
/// </summary>
public sealed class InputHandler : IDisposable
{
    private readonly List<Key> _held = [];
    private readonly ControllerManager _pad = new(0);

    public void OnKeyDown(Key key)
    {
        _held.Remove(key);
        _held.Add(key);
    }

    public void OnKeyUp(Key key) => _held.Remove(key);
    public bool Has(Key key) => _held.Contains(key);
    private bool Any(params Key[] keys) => keys.Any(_held.Contains);

    public void Update()
    {
        _pad.Update();
    }

    /// <summary>
    /// Left and right to turn; up to fly forward and down to slow (and back off); Q to climb and A to dive; Space,
    /// Z or Ctrl to fire; X or E to use. On a gamepad: the stick, the shoulder buttons to climb and dive, A to fire,
    /// B to use.
    /// </summary>
    public Controls Player
    {
        get
        {
            var pad = _pad;
            int dx = (Any(Key.Right, Key.NumPad6) ? 1 : 0) - (Any(Key.Left, Key.NumPad4) ? 1 : 0);
            if (dx == 0) dx = pad.DPadLeft || pad.LeftStickX < -0.4f ? -1 : pad.DPadRight || pad.LeftStickX > 0.4f ? 1 : 0;
            // Up (forward) is -1.
            int dy = (Any(Key.Down, Key.NumPad2) ? 1 : 0) - (Any(Key.Up, Key.NumPad8) ? 1 : 0);
            if (dy == 0) dy = pad.DPadUp || pad.LeftStickY < -0.4f ? -1 : pad.DPadDown || pad.LeftStickY > 0.4f ? 1 : 0;
            int climb = (Any(Key.Q, Key.PageUp, Key.W) || pad.RightBumper ? 1 : 0) - (Any(Key.A, Key.PageDown, Key.S) || pad.LeftBumper ? 1 : 0);
            bool fire = Any(Key.Space, Key.Z, Key.LeftCtrl, Key.RightCtrl) || pad.ButtonA || pad.RightTrigger > 0.5f;
            bool use = Any(Key.X, Key.E) || pad.ButtonB || pad.ButtonX;
            return new Controls(dx, dy, fire, use, climb);
        }
    }

    /// <summary>Enter or 1.</summary>
    public bool Start => Any(Key.Enter, Key.D1, Key.NumPad1) || _pad.ButtonStart;

    public bool PadUp => _pad.DPadUp || _pad.LeftStickY < -0.5f;
    public bool PadDown => _pad.DPadDown || _pad.LeftStickY > 0.5f;
    public bool PadLeft => _pad.DPadLeft || _pad.LeftStickX < -0.5f;
    public bool PadRight => _pad.DPadRight || _pad.LeftStickX > 0.5f;
    public bool PadAccept => _pad.ButtonA || _pad.ButtonX || _pad.ButtonB;
    public bool PadDone => _pad.ButtonStart;
    public bool PadPause => _pad.ButtonStart;
    public bool PadBack => _pad.ButtonBack;

    public void Dispose() => _pad.Dispose();
}
