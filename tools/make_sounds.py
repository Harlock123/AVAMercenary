#!/usr/bin/env python3
"""Synthesises AVAMercenary's sound effects and tunes into ../src/AVAMercenary/Sounds as 16-bit mono WAV.

Nothing is sampled from any game: every effect is built here from square, triangle and noise voices, and the
tunes are new compositions: a cool, steady theme for flying the city and a fanfare for the title. The skimmer's
computer speaks through the formant voice in speech.py. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

from speech import say


RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAMercenary", "Sounds")
RNG = random.Random(1985)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out


def computer(words):
    voice = say(words, rate=RATE, pitch=104, speed=0.95, size=1.05)
    hum = say(words, rate=RATE, pitch=156, speed=0.95, size=1.05, seed=11)
    return mix(voice, [s * 0.3 for s in hum])


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # The skimmer.
    write("Fire.wav", mix(tone(0.16, 2200, 500, "square", attack=0.002, release=0.8, gain=0.5), tone(0.1, 900, 300, "saw", release=1.0, gain=0.3)), 0.15)
    write("Hit.wav", mix(tone(0.08, 2600, 1200, "square", attack=0.001, release=1.4, gain=0.4), tone(0.07, 3000, 800, "noise", release=1.4, gain=0.4)), 0.18)
    write("TowerDown.wav", cat(mix(tone(0.3, 1400, 300, "saw", attack=0.002, release=0.8, gain=0.6), tone(0.25, 2400, 900, "noise", release=0.8, gain=0.5)),
                               mix(tone(1.2, 2000, 60, "noise", release=0.5), tone(1.0, 130, 30, "sine", release=0.6, gain=0.9))), 0.5)
    write("Hurt.wav", mix(tone(0.14, 700, 180, "square", attack=0.001, release=1.0, gain=0.6), tone(0.12, 2500, 500, "noise", release=1.2, gain=0.6)), 0.3)
    write("Explode.wav", cat(mix(tone(1.3, 2200, 50, "noise", attack=0.001, release=0.5), tone(1.1, 140, 25, "sine", release=0.6, gain=0.9)),
                             tone(0.8, 600, 80, "saw", vibrato=0.2, vib_rate=10, release=0.6, gain=0.3)), 0.55)
    write("Bump.wav", mix(tone(0.18, 160, 60, "square", attack=0.001, release=1.0, gain=0.7), tone(0.12, 1200, 300, "noise", release=1.2, gain=0.5)), 0.32)
    # The drones.
    write("Drone.wav", tone(1.1, 220, 880, "saw", attack=0.15, vibrato=0.04, vib_rate=22, release=0.5, gain=0.4), 0.22)
    write("DroneFire.wav", cat(*[cat(tone(0.04, 1400, 500, "square", attack=0.001, release=1.0, gain=0.5), silence(0.03)) for _ in range(2)]), 0.16)
    write("DroneDown.wav", cat(tone(0.35, 1600, 200, "saw", release=0.5, gain=0.6), mix(tone(0.4, 2000, 100, "noise", release=0.8), tone(0.3, 160, 50, "sine", release=0.8))), 0.36)
    # Business.
    write("Pickup.wav", cat(*[tone(0.06, hz(n), kind="tri", release=0.3) for n in ["C5", "G5", "C6"]]), 0.26)
    write("Radio.wav", cat(tone(0.18, 2600, 2600, "noise", attack=0.01, release=0.4, gain=0.25), *[tone(0.05, hz(n), kind="square", release=0.4, gain=0.4) for n in ["E6", "B5", "E6"]]), 0.18)
    write("Paid.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["E5", "G#5", "B5", "E6", "B5", "E6"]]), 0.3)
    write("Deny.wav", cat(tone(0.14, 220, kind="square", release=0.3, gain=0.5), silence(0.04), tone(0.22, 165, kind="square", release=0.4, gain=0.5)), 0.22)
    write("Launch.wav", cat(tone(0.5, 80, 140, "saw", attack=0.1, vibrato=0.05, vib_rate=30, release=0.2, gain=0.6),
                            mix(tone(2.4, 140, 900, "saw", vibrato=0.05, vib_rate=30, release=0.5, gain=0.5), tone(2.4, 3000, 1200, "noise", release=0.5, gain=0.4))), 0.42)
    # The computer.
    write("SayWelcome.wav", computer("W EH1 L K AH M , T UW , T AA1 R G !"), 0.5)
    write("SayPaid.wav", cat(*[tone(0.05, 1300, kind="square", release=0.2, gain=0.4) for _ in range(2)], silence(0.06), computer("K R EH1 D IH T S , P EY1 D !")), 0.45)

    # Flying the city (an original composition): a steady, cool loop over a ticking pulse.
    e = 0.5
    lead = [("E5", 1), ("B4", e), ("E5", e), ("G5", 1.5), ("F#5", e), ("E5", 1), ("D5", 1), ("B4", 2),
            ("C5", 1), ("E5", e), ("G5", e), ("A5", 1.5), ("G5", e), ("F#5", 1), ("D#5", 1), ("E5", 2)]
    bass = [("E3", e), ("E3", e), ("B2", e), ("E3", e)] * 4 + [("C3", e), ("C3", e), ("G2", e), ("C3", e), ("B2", e), ("B2", e), ("F#2", e), ("B2", e)] * 2
    theme = tune(112, lead * 2, bass * 2, kind="tri")
    beat = 60.0 / 112
    tick = cat(*[cat(tone(beat / 8, 5000, 3000, "noise", attack=0.001, release=2.0, gain=0.25), silence(beat * 3 / 8)) for _ in range(len(theme) * 2 // int(RATE * beat) + 2)])
    write("Music.wav", mix(theme, tick[: len(theme)]), 0.24)
    fanfare = [("E5", 0.5), ("E5", 0.5), ("B5", 1.5), ("A5", 0.5), ("G5", 0.5), ("F#5", 0.5), ("E5", 1), ("B4", 1),
               ("E5", 0.5), ("G5", 0.5), ("B5", 0.5), ("E6", 2), ("D6", 0.5), ("B5", 1), ("E6", 3)]
    fbass = [("E3", 2), ("B2", 2), ("E3", 2), ("C3", 2), ("G2", 1), ("B2", 2), ("E3", 3)]
    write("TitleTune.wav", cat(tune(116, fanfare, fbass, kind="square"), silence(0.8)), 0.34)
    write("Victory.wav", tune(140, [("E5", 0.5), ("G#5", 0.5), ("B5", 0.5), ("E6", 1.5), ("D#6", 0.5), ("E6", 0.5), ("F#6", 0.5), ("G#6", 3)],
                              [("E3", 1), ("B2", 1), ("C#3", 1), ("A2", 1), ("B2", 1), ("E3", 2.5)], kind="pulse"), 0.4)
    write("GameOver.wav", tune(66, [("B4", 1), ("G4", 1), ("F#4", 1), ("D#4", 1), ("E4", 4)], [("E3", 2), ("C3", 2), ("B2", 2), ("E2", 2)], kind="tri"), 0.42)


if __name__ == "__main__":
    main()
