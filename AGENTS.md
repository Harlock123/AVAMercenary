# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
what the tests check; this file covers how the code is put together and the rules that keep it working.

## What this is

A first-person flight through a wireframe 3D city in the style of Novagen's 1985 game Mercenary: Escape from Targ,
in C# on .NET 10 and Avalonia 12. One self-contained executable per platform (Windows, macOS and Linux, x64 and
ARM). The city is drawn in code, and the sounds and the spoken lines are synthesised by `tools/make_sounds.py`.

## Commands

```bash
dotnet build AVAMercenary.slnx                 # the SDK version is in global.json (10.0.400 or later)
dotnet test AVAMercenary.slnx                  # every test runs headless: no window opens
dotnet run --project src/AVAMercenary          # play it; --no-audio for silence
MERC_SHOTS=$PWD/Images dotnet test tests/AVAMercenary.Tests --filter ScreenshotTests   # refresh the README's pictures
MERC_PROBE=1 dotnet test tests/AVAMercenary.Tests --filter PilotProbe   # how the computer mercenary gets on, for tuning
python3 tools/make_sounds.py                   # rebuild src/AVAMercenary/Sounds (standard-library Python only)
```

`MERC_PROBE=lazy` flies straight ahead and fires. Without `MERC_PROBE` the probe test does nothing.

`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference types and implicit usings for every
project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVAMercenary/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, demonstration, play, high scores) and the fixed-step loop.
  - `Sim.cs` steps the city (`Sim.Start`, `Sim.Step`): the skimmer, its bolts, the jobs for the Palyars and the
    Mechanoids, the factions' drones, and passage off the planet.
  - `City.cs` has `Controls`, the screen and city measures (`Field`), the factions, buildings and relay towers,
    and `CityBook`, which builds the city from a seed.
  - `World.cs` has the seeded `Dice`, the skimmer, jobs, towers, drones and bolts, and the copyable `World`.
  - `Pilot.cs` is the computer mercenary, used by the demonstration and the tests.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepads) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVAMercenary/Views/`: `GameCanvas` (split over two partial files) draws the fixed 1024x768 screen and runs
  the 60 Hz timer.
- `src/AVAMercenary/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `tests/AVAMercenary.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`: city, gameplay and screenshot tests, and
  the pilot probe.
- `tools/`: `make_sounds.py` and the formant voice `speech.py` it uses.

## Rules that keep it working

- **Fixed steps.** `GameEngine.Update` runs the simulation in fixed 1/60 s steps (`Field.Step`), independent of
  the frame rate, so tests play deterministically from a seed. Draw random numbers from the world's seeded `Dice`
  (`w.Dice`), never from `Random.Shared`.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the world and draws it.
- **Input goes through `InputHandler`.** `Controls` carries `dy` with -1 for up (keys and stick alike,
  `LeftStickY < -0.4` is up), and up drives the skimmer forward. Climb and dive are a separate axis. Keep keys and
  stick agreeing when adding controls.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder (`AVAMercenary`); tests load
  the table from a fresh folder under the system temp path and run with `Silent = true`.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls, the test list) to match.
- New sounds and spoken lines go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push. It then publishes self-contained single-file builds for
win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as workflow artifacts.
Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a proper release.
