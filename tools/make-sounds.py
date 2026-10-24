#!/usr/bin/env python3
"""Synthesises every sound effect in Sounds/ from scratch.

    tools/make-sounds.py            (writes Sounds/*.wav)

The sounds are made from sine waves, noise and envelopes by this script, so
they belong to nobody and are released under CC0 like the rest of the audio.
Only the standard library is needed. The output is deterministic: running it
again gives the same files.

To add a sound, write a function that returns a list of samples between -1
and 1 and add it to SOUNDS. Names ending in a loop flag get a WAV loop point,
which Godot reads on import.
"""

import math
import os
import random
import struct
import sys

RATE = 44100
TAU = 2 * math.pi


def n(seconds):
    return int(seconds * RATE)


def silence(seconds):
    return [0.0] * n(seconds)


def env(i, total, attack, release):
    """Linear attack, exponential-ish release, in samples."""
    if i < attack:
        return i / attack
    left = total - i
    if left < release:
        return max(left / release, 0.0) ** 2
    return 1.0


def decay(t, rate):
    return math.exp(-t * rate)


def tone(freq, seconds, rate=6.0, shape="sine", attack=0.003):
    """One note; freq may be a function of time for sweeps."""
    out = []
    phase = 0.0
    total = n(seconds)
    for i in range(total):
        t = i / RATE
        f = freq(t) if callable(freq) else freq
        phase += TAU * f / RATE
        if shape == "sine":
            s = math.sin(phase)
        elif shape == "square":
            s = 1.0 if math.sin(phase) >= 0 else -1.0
        elif shape == "triangle":
            s = 2 / math.pi * math.asin(math.sin(phase))
        else:
            raise ValueError(shape)
        a = min(t / attack, 1.0) if attack else 1.0
        out.append(s * a * decay(t, rate) * env(i, total, 1, n(0.01)))
    return out


def bell(freq, seconds, rate=4.0):
    """A soft bell: a few inharmonic partials that die away at different speeds."""
    parts = [(1.0, 1.0, rate), (2.0, 0.45, rate * 1.6), (3.01, 0.25, rate * 2.4), (4.2, 0.12, rate * 3.5)]
    out = [0.0] * n(seconds)
    for mult, amp, r in parts:
        for i, s in enumerate(tone(freq * mult, seconds, r)):
            out[i] += s * amp
    return out


def noise(seconds, rng, cutoff=1.0):
    """White noise through a one-pole low-pass; cutoff 1 is unfiltered, smaller is darker."""
    out = []
    y = 0.0
    for _ in range(n(seconds)):
        y += cutoff * (rng.uniform(-1, 1) - y)
        out.append(y)
    return out


def mix(*tracks, at=None):
    """Adds tracks together; at gives each track's start time in seconds."""
    at = at or [0.0] * len(tracks)
    length = max(n(start) + len(track) for track, start in zip(tracks, at))
    out = [0.0] * length
    for track, start in zip(tracks, at):
        offset = n(start)
        for i, s in enumerate(track):
            out[offset + i] += s
    return out


def gain(track, g):
    return [s * g for s in track]


def shaped(track, rate, attack=0.002):
    return [s * min((i / RATE) / attack, 1.0) * decay(i / RATE, rate) for i, s in enumerate(track)]


def normalise(track, peak):
    top = max(abs(s) for s in track) or 1.0
    return [s * peak / top for s in track]


# --- The sounds -------------------------------------------------------------

def roll(rng):
    """A seamless loop of low rumble; the game changes its pitch and volume with speed."""
    seconds = 2.0
    total = n(seconds)
    # Two passes of filtering make it a deep, soft grind.
    raw = noise(seconds + 0.5, rng, 0.08)
    raw = noise_filter(raw, 0.15)
    raw = raw[n(0.5) - n(0.25):]
    # Crossfade the tail into the head so the loop has no click.
    fade = n(0.25)
    body = raw[:total]
    tail = raw[total:total + fade]
    for i in range(fade):
        w = i / fade
        body[i] = body[i] * w + tail[i] * (1 - w)
    # A slow wobble, like the ball's seams going round.
    hum = [0.35 * math.sin(TAU * 55 * i / RATE) * (0.6 + 0.4 * math.sin(TAU * 4 * i / RATE)) for i in range(total)]
    return [b + h for b, h in zip(body, hum)]


def noise_filter(track, cutoff):
    out = []
    y = 0.0
    for s in track:
        y += cutoff * (s - y)
        out.append(y)
    return out


def impact(rng):
    thump = tone(lambda t: 140 - 80 * min(t / 0.12, 1), 0.3, rate=18)
    click = shaped(noise(0.05, rng, 0.35), 90)
    return mix(thump, gain(click, 0.5))


def fall(rng):
    whistle = tone(lambda t: 900 * (0.25 ** (t / 0.9)), 0.95, rate=1.2, shape="sine", attack=0.02)
    vibrato = [s * (0.85 + 0.15 * math.sin(TAU * 7 * i / RATE)) for i, s in enumerate(whistle)]
    return vibrato


def respawn(rng):
    sweep = tone(lambda t: 300 + 900 * min(t / 0.25, 1), 0.3, rate=5, shape="triangle", attack=0.01)
    sparkle = mix(bell(1568, 0.4, 7), bell(2093, 0.4, 7), at=[0.18, 0.26])
    return mix(gain(sweep, 0.6), gain(sparkle, 0.5))


def checkpoint(rng):
    return mix(bell(1318.5, 0.6, 5), bell(1975.5, 0.8, 4), at=[0.0, 0.11])


def goal(rng):
    notes = [523.25, 659.25, 783.99, 1046.5]
    parts = [gain(tone(f, 0.25, rate=7, shape="triangle"), 0.7) for f in notes[:3]]
    final = mix(tone(1046.5, 1.2, rate=2.2, shape="triangle"), gain(tone(1318.5, 1.2, rate=2.6, shape="triangle"), 0.5),
                gain(bell(2093, 1.2, 3), 0.4))
    return mix(*parts, final, at=[0.0, 0.1, 0.2, 0.32])


def countdown(rng):
    return tone(660, 0.18, rate=10, shape="square", attack=0.002)


def go(rng):
    return mix(tone(990, 0.45, rate=4, shape="square"), gain(tone(1980, 0.45, rate=6), 0.3))


def timer_warning(rng):
    return tone(1500, 0.07, rate=40, shape="triangle", attack=0.001)


def time_up(rng):
    a = tone(220, 0.35, rate=1.5, shape="square", attack=0.005)
    b = tone(165, 0.7, rate=1.5, shape="square", attack=0.005)
    return mix(a, b, at=[0.0, 0.3])


def hazard_hit(rng):
    whack = shaped(noise(0.18, rng, 0.5), 30)
    body = tone(lambda t: 260 - 120 * min(t / 0.1, 1), 0.2, rate=22, shape="triangle")
    return mix(whack, gain(body, 0.8))


def chaser_hit(rng):
    partials = [(523, 1.0), (1311, 0.6), (2079, 0.4), (3170, 0.25)]
    clang = mix(*[gain(tone(f, 0.6, rate=7 + i * 3), a) for i, (f, a) in enumerate(partials)])
    return mix(clang, gain(shaped(noise(0.03, rng, 0.9), 120), 0.6))


def chaser_alert(rng):
    return mix(tone(700, 0.08, rate=20, shape="square"), tone(1050, 0.12, rate=14, shape="square"), at=[0.0, 0.09])


def menu_move(rng):
    return tone(1800, 0.035, rate=80, attack=0.001)


def menu_click(rng):
    return mix(tone(1200, 0.06, rate=50, shape="triangle", attack=0.001), gain(shaped(noise(0.015, rng, 0.8), 200), 0.4))


# Sounds for the newer course pieces, ready to call from their scripts.

def bumper(rng):
    boing = tone(lambda t: 180 + 260 * math.exp(-t * 18) + 25 * math.sin(TAU * 28 * t), 0.4, rate=7)
    return mix(boing, gain(shaped(noise(0.02, rng, 0.9), 150), 0.4))


def teleport(rng):
    return tone(lambda t: 400 + 1400 * (t / 0.6) + 200 * math.sin(TAU * 30 * t), 0.6, rate=3, shape="triangle", attack=0.02)


def gate(rng):
    return mix(tone(320, 0.12, rate=25, shape="square"), gain(shaped(noise(0.06, rng, 0.4), 60), 0.6))


def conveyor(rng):
    """A seamless loop: a machine hum with a clatter at 8 Hz."""
    seconds = 1.0
    total = n(seconds)
    hum = [0.5 * math.sin(TAU * 90 * i / RATE) + 0.25 * math.sin(TAU * 180 * i / RATE) for i in range(total)]
    clatter = noise_filter(noise(seconds, rng, 0.6), 0.5)
    tick = [c * (math.exp(-((i / RATE) % 0.125) * 60)) for i, c in enumerate(clatter)]
    return [h + 0.6 * k for h, k in zip(hum, tick)]


def ice(rng):
    """A seamless loop of a thin, glassy hiss, for sliding on ice."""
    seconds = 2.0
    total = n(seconds)
    raw = noise(seconds + 0.2, rng, 0.9)
    hp = []
    prev = 0.0
    for s in raw:
        hp.append(s - prev)
        prev = s
    body = hp[:total]
    fade = n(0.2)
    for i in range(fade):
        w = i / fade
        body[i] = body[i] * w + hp[total + i] * (1 - w)
    return body


# name: (function, peak level, loops)
SOUNDS = {
    "roll": (roll, 0.6, True),
    "impact": (impact, 0.9, False),
    "fall": (fall, 0.7, False),
    "respawn": (respawn, 0.7, False),
    "checkpoint": (checkpoint, 0.7, False),
    "goal": (goal, 0.8, False),
    "countdown": (countdown, 0.45, False),
    "go": (go, 0.5, False),
    "timer_warning": (timer_warning, 0.5, False),
    "time_up": (time_up, 0.5, False),
    "hazard_hit": (hazard_hit, 0.85, False),
    "chaser_hit": (chaser_hit, 0.7, False),
    "chaser_alert": (chaser_alert, 0.4, False),
    "menu_move": (menu_move, 0.35, False),
    "menu_click": (menu_click, 0.5, False),
    "bumper": (bumper, 0.85, False),
    "teleport": (teleport, 0.6, False),
    "gate": (gate, 0.6, False),
    "conveyor": (conveyor, 0.4, True),
    "ice": (ice, 0.3, True),
}


def write_wav(path, samples, loops):
    frames = b"".join(struct.pack("<h", max(-32767, min(32767, int(s * 32767)))) for s in samples)
    chunks = [b"fmt " + struct.pack("<IHHIIHH", 16, 1, 1, RATE, RATE * 2, 2, 16),
              b"data" + struct.pack("<I", len(frames)) + frames]
    if loops:
        # smpl chunk with one forward loop over the whole sound.
        smpl = struct.pack("<9I", 0, 0, int(1e9 / RATE), 60, 0, 0, 0, 1, 0)
        smpl += struct.pack("<6I", 0, 0, 0, len(samples) - 1, 0, 0)
        chunks.append(b"smpl" + struct.pack("<I", len(smpl)) + smpl)
    body = b"WAVE" + b"".join(chunks)
    with open(path, "wb") as f:
        f.write(b"RIFF" + struct.pack("<I", len(body)) + body)


def main():
    root = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Sounds")
    os.makedirs(root, exist_ok=True)
    only = set(sys.argv[1:])
    for name, (make, peak, loops) in SOUNDS.items():
        if only and name not in only:
            continue
        rng = random.Random(name)
        samples = normalise(make(rng), peak)
        write_wav(os.path.join(root, name + ".wav"), samples, loops)
        print(f"{name}.wav  {len(samples) / RATE:.2f}s{'  (loop)' if loops else ''}")


if __name__ == "__main__":
    main()
