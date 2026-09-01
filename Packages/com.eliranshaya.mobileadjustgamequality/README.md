# Mobile Adjust Game Quality

One button generates a mobile-tuned URP setup. One prefab keeps the frame budget on the
device by moving along a quality ladder, driven by frame times and by how hot the phone is.

---

## Install

**Window → Package Manager → + → Add package from git URL:**

```
https://github.com/eliranshaya/Unity-MobileAdjustGameQuality.git?path=/Packages/com.eliranshaya.mobileadjustgamequality#v3.3.0
```

Leaving the `#v…` off tracks the newest commit on `main` instead. Pin the tag for anything you
ship — and note that Package Manager resolves a git URL exactly once either way, recording the
commit in `Packages/packages-lock.json`, so **updating means editing the tag in
`Packages/manifest.json`**, not pressing anything in the UI.
[All releases](https://github.com/eliranshaya/Unity-MobileAdjustGameQuality/tags).

### Upgrading to 3.3 from an earlier version

**Re-run `Tools > Mobile Quality > Setup`.** Several adaptation defaults changed, and Unity
keeps whatever an existing prefab already has serialized — most importantly the thermal poll
interval, which is 5 seconds now and 30 in a prefab built by 3.2. Setup rebuilds the prefab
from scratch, so one press is enough. The alternative is editing those fields by hand.

The rung remembered by 3.2 is discarded automatically on first run: it recorded where the last
session stopped rather than what the device proved, so it means something different now.

Requires Unity 2022.3 LTS or newer and URP 14+, which installs automatically. URP must be the
active pipeline in **Project Settings → Graphics**.

---

## Use

1. `Tools > Mobile Quality > Setup`
2. Press **Setup**
3. Drag `Assets/MobileAdjustGameQuality/Generated/MobileQualityManager.prefab` into your
   first scene

Generated assets land in **your** `Assets/`, not in the package — a package installed from
Git is read-only.

Setup creates `MAQ-Renderer.asset` and `MAQ-URP.asset`, **replaces every quality level** with
a single `MAQ Mobile`, applies the recommended mobile Player Settings, builds the prefab, and
turns on the `MAQ_DEVELOPER_MODE` define. It names every quality level it is about to delete
before you press anything.

---

## The ladder

Quality is one number: which rung of a ladder is applied. Moving a rung applies its whole row
at once.

| # | Render scale | Shadows | Res | FPS |
| --- | --- | --- | --- | --- |
| 0 | 1.00 | on | 1024 | 60 |
| 1 | 0.90 | on | 1024 | 60 |
| **2** | **0.80** | on | 512 | 60 |
| 3 | 0.70 | on | 512 | 60 |
| 4 | 0.70 | **off** | — | 60 |
| 5 | 0.65 | off | — | 60 |
| 6 | 0.60 | off | — | 60 |
| 7 | 0.60 | off | — | **30** |
| 8 | 0.55 | off | — | 30 |

Rung 2 is where a device with no history starts. Every rung is editable on the prefab.

Three things are worth pointing out:

**Shadows are all or nothing.** Shadow *distance* is not a rung, and deliberately so:
shortening it leaves near objects with shadows and objects a metre behind them without, and
that boundary reads as a bug rather than as lower quality. Distance is one authored value on
the profile; the ladder only switches shadows on or off.

**The ladder only descends.** Every rung is cheaper than the one above it and looks it — no
rung trades one setting for another, and nothing is handed back on the way down. So a rung
index is a single honest measure of how hard the device is working, and a device that drops
one rung sees one small change rather than a rearrangement.

**Frame rate is last.** Rungs 0-6 spend resolution first and then the shadow pass, all while
holding 60. Only when nothing cheaper is left does rung 7 halve the target, because 30 fps is
the most noticeable change a game can make. Shadows come before it because they are the only
rung that helps a CPU-bound frame — render scale moves pixels, and a frame limited by draw
calls does not care how many pixels it is drawing.

---

## How it decides

Two signals, answering different questions.

**Frame time** answers *is the device failing to reach its target?* It can answer that
honestly, because missing a target really does show up in wall-clock frame time. It **cannot**
answer *does the device have room to spare*: `Application.targetFrameRate` makes the engine
sleep until the frame is due, so a device holding 60 comfortably and a device only just
holding 60 both report 16.67 ms.

**Temperature** answers *is the device in trouble?* with no blind spot, on both platforms,
capped or not.

So going down is driven by either — but the two are **recorded separately**, and that is the
part that matters.

The applied rung is a sum of two numbers:

```
applied rung  =  perf rung  +  thermal penalty
```

**Perf rung** is what this device is believed to be capable of. It moves only on frame times,
and only while the device is cool enough for those frame times to mean anything. It is kept.

**Thermal penalty** is a temporary charge on top, added because the device is hot and removed
again when it is not. It is a lease, not a verdict, and it expires on its own.

Keeping them apart is the whole point. When heat and performance wrote to the same number, a
phone that got warm once walked to the bottom of the ladder and stayed there for the rest of
the session — and for every session after it, because the rung it was stranded on was then
remembered as the rung it belonged on. Nothing recorded that the descent had been thermal, so
nothing could undo it.

| Trigger | Condition | Then |
| --- | --- | --- |
| Missing target, cool | frame time > budget × 1.08 for 3 s | down one rung, charged to the **perf rung** |
| Missing target, warm | the same, but not cool | down one rung, charged to the **penalty** |
| **Wrong frame rate** | frame time > budget × 1.35 for 3 s | **jump straight to the first 30 fps rung** |
| Heat | device in the **Shed** band | penalty +1, every 30 s |
| Urgent heat | device in the **Urgent** band | penalty +1, as fast as the ladder allows |
| **Cooling** | device **fully cool**, penalty outstanding | penalty −1, every 10 s |
| Recovered | fully cool, no penalty, frame time ≤ budget × 1.05 for 20 s | perf rung up one |

A warm device charges its drops to the penalty because the frames may be the device or may be
the throttling, and there is no way to tell those apart from inside the game. A cool one has no
such ambiguity, so what it measures is kept.

Coming back up is not symmetric either, and deliberately so. Climbing the **perf rung** needs
real evidence: cool, inside budget, for twenty seconds. Handing back a **penalty** needs no
evidence at all beyond the heat being gone — nothing was measured to take those rungs away, and
at a capped rung the frame times could not justify returning them anyway, because the cap pins
them to the budget however much room the device has.

The wrong-frame-rate jump is the one that matters most in practice. 1.35 is roughly 44 fps
against a 60 target — at that point the device is not short of its target, it is on the wrong
target, and walking down one rung at a time would spend twenty seconds overheating to reach a
conclusion already visible in the numbers. A stable 30 looks and feels better than an unstable
44, and generates far less heat.

It lands on the **first** 30 fps rung below the current one — rung 7 in the shipping ladder,
which is also the ladder's cheapest 60 fps rung with the frame rate halved. That is a large
single step from the top. If you want a gentler landing, insert a 30 fps rung with shadows
still on above it; the governor jumps to whichever it finds first.

### The frame rate floor

Once a device has **proven** a 60 fps rung — see [Memory](#memory) — the ladder stops at the
last rung that still runs at 60 rather than descending into the 30 fps rungs below it. On the
shipping ladder that is rung 6. A phone that demonstrated it can hold 60 should not quietly end
up at 30 because it spent an afternoon in a warm room.

The floor is derived from the ladder, not written down as an index: it is the last rung whose
target frame rate matches the one the device proved. A device that has only ever proven a 30 fps
rung floors at the last rung, which is no floor at all.

This is the one guard in the package that can refuse a downward step, so it has exactly one
exception: from the **Urgent** band upward it is ignored. At that point the OS is clocking the
chip down itself, holding 60 in the settings does not hold 60 on screen, and refusing to
descend protects nothing. Turn the whole thing off with `Protect Proven Frame Rate`.

### Why p75 and not p95

Decisions are made on the **75th percentile** frame time. With vsync a frame time is quantised
to multiples of the refresh interval — at a 60 fps target on a 120 Hz panel a frame is either
16.7 ms or 25 ms, with nothing in between. A device that is coping perfectly still lands a few
frames on 25 ms, which drags p95 over any sensible threshold and makes it look like it is
failing. p75 rides out that noise while still catching a device that is genuinely slow for a
quarter of its frames.

Frames longer than **500 ms** are discarded entirely. Nothing renders at two frames per second
for one frame and then recovers — that is an asset load, a garbage collection or a domain
reload, and letting one into a two-second window skews every statistic in it.

### The hysteresis band

The miss threshold is 1.08 and the holding threshold is 1.05, and the gap between them is
where the ladder does nothing. Do not set the holding threshold near 1.0: a capped device sits
a few percent above its budget from quantisation alone, so 1.02 would leave a device that is
coping perfectly stuck — never bad enough to drop, never good enough to climb.

---

## Two phases

| | Window | Check | Down after | Up after |
| --- | --- | --- | --- | --- |
| **Grace** | first 3 s | nothing is judged | — | — |
| **Calibration** | next 30 s | every frame | 2.5 s | 4 s |
| **Steady** | after that | every 0.25 s | 3 s | 20 s |

The grace window exists because the first frames of a session are shader compilation and scene
loading, not gameplay. Without it every device drops several rungs before it has drawn a real
frame. Nothing is judged until the two-second measurement window is also full.

Note what actually controls how fast calibration converges: the **window lengths**, not the
check rate. A 2.5-second sustain window cannot fire more than once every 2.5 seconds however
often it is asked.

The thermal **status** is polled every **5 seconds**. It used to be 30, which was longer than
the twenty-second window a device has to stay cool for before the ladder climbs — so a single
stale reading could block a climb for the whole window it covered.

The **headroom forecast** is paced separately, inside `ThermalSensor`, at no more than one call
every 15 seconds. That is not a tunable: Android's documentation says *"Don't call the
GetThermalHeadroom() API too frequently. If you do so, the API returns NaN. You shouldn't call
it more than once every 10 seconds."* A setting somebody could turn down to five would not poll
faster, it would silently stop returning a forecast at all, so the platform's contract is
enforced in code instead.

---

## Memory

What is stored is the **best rung this device has earned**, not the rung the last session
happened to stop on. That one word was a real bug: a session that ended hot ended at the bottom
of the ladder, so the bottom of the ladder is what got written, and the next launch started
there on a cold phone with no way to climb out. One warm evening permanently downgraded the
device.

A rung counts as **proven** when the device has spent `Proven After Seconds` — 120 by default —
on it while:

- settled (not in the window just after a change), and
- inside its frame budget, and
- **cool**, and
- with **no thermal penalty outstanding**.

That last condition is what makes it a measurement rather than a coincidence. With a penalty
applied the game is running easier than the rung being judged, so holding it proves nothing.
And because heat can never contribute, a hot session cannot lower the stored value.

The time is **accumulated, not continuous**. A player who pauses, loads a level or takes a call
has not stopped proving anything about their phone.

Only improvements are written. A session that could not reach last month's best has not
disproved it — it may simply have been a hot afternoon or a heavier level. The cost of a
high-water mark is that a device whose game has since grown heavier starts a rung or two too
high and pays a short calibration for it; that is half a minute of adjustment against a
permanent downgrade, so it is the right way round.

The ladder's length is stored alongside it, so editing the ladder discards a remembered rung
rather than applying it to the wrong row. A value written by a version older than 3.3 is
discarded outright, because it means something different.

`Tools > Mobile Quality > Forget Remembered Step` clears it.

---

## Which rung the game opens on

In descending order of how much the answer is worth:

| # | Source | When |
| --- | --- | --- |
| 1 | **The proven rung** | this device has earned one, in this session or an earlier one |
| 2 | **The device profile** | it has not, and `Use Device Profiler` is on |
| 3 | **`Start Step`** | the profiler is off |

A measurement always wins. Below that, the device's own hardware is a far better guess than one
fixed number for a market that runs from a 4 GB budget phone to current flagship silicon.

`DeviceProfiler` scores the GPU (`SystemInfo.graphicsDeviceName`, 60 %) against memory (40 %),
then multiplies by how much the panel's resolution changes the workload — a 1440p screen is 1.8×
the pixels of a 1080p one, which for a package whose main lever is render scale matters as much
as the chip does. The score maps to a fraction of the ladder, so editing the ladder does not
silently change what "start a flagship high" means.

Memory is scored on **two different scales**, because the number means two different things.
Apple ships far less of it for the same class of device — an iPhone 15 Pro has 8 GB where an
Android flagship of the same year has 12 to 16 — so an iPhone 13, which is a 4 GB A15, scored as
a budget Android phone and started three rungs lower than it deserved.

It is a score rather than a chain of special cases because a score degrades gracefully when one
signal cannot be read, and can be re-tuned by moving a number.

**An unrecognised GPU scores as mid-range, never as low.** New parts ship every year and the
table cannot know about them, so the failure mode has to be *a new flagship starts one rung
lower than it deserved for thirty seconds* rather than *every new flagship launches on the worst
rung on the ladder*.

This is only ever a guess, and it is treated as one — calibration runs straight afterwards
either way, so a wrong guess costs a rough half-minute rather than a bad session.

---

## Thermal

There is **no temperature in degrees** on either platform, and there cannot be — the
temperature at which one phone throttles is meaningless on another. What you get is the OS's
own verdict.

| | API | Gives |
| --- | --- | --- |
| **Android** | `PowerManager.getCurrentThermalStatus` (API 29+) and `getThermalHeadroom(10)` (API 30+) | status 0-6, plus a 0..1+ forecast where 1.0 means throttling now |
| **iOS** | `NSProcessInfo.thermalState` via a bundled `.mm` | nominal / fair / serious / critical |
| **Editor, desktop** | none | the ladder runs on frame times alone |

### Three bands, not two

Those scales have a wide middle, and collapsing it into one "warm" flag was wrong. Android's
`LIGHT` means *throttling has started and the user cannot tell* — it is the state most phones
sit in after a few minutes of play. Treating it as a reason to give quality back walks a
perfectly healthy device to the bottom of the ladder and leaves it there for the session.

The right response to `LIGHT` is to stop asking for more, not to start giving things up.

| Band | Android status | Headroom | iOS | Response |
| --- | --- | --- | --- | --- |
| **Cool** | `NONE` | < 0.85 | nominal | normal adaptation |
| **Hold** | `LIGHT` | ≥ 0.85 | fair | stop climbing. Nothing else. |
| **Shed** | `MODERATE` | ≥ 0.95 | — | one rung every 30 s, ignoring frame times |
| **Urgent** | `SEVERE`+ | ≥ 1.0 | serious, critical | one rung per evaluation |

Every threshold — the status *and* the headroom for each band — is a field on the prefab.

### The forecast outranks the status

Where a headroom forecast exists, **it decides alone**. The two signals are not equals, and
treating them as equals is what stranded a cooled phone on the bottom rung: a device reporting
`LIGHT` with a forecast of 0.51 is not throttled, it is a status that has not caught up, and
under an either-signal rule that stale bit vetoed every attempt to climb back.

Android says as much itself: *"If GetThermalHeadroom() returns a high value and
GetCurrentThermalStatus() still returns THERMAL_STATUS_NONE, the status is likely not updated
... we recommend using getThermalHeadroom instead."* It is stale in both directions.

One exception. `SEVERE` and above is not a forecast — it is the OS reporting that it is clocking
the chip down right now — and that is never ignored however comfortable the prediction looks.

On iOS and on Android 10 and older there is no forecast at all, and the status decides on its
own.

`Shed` is where Google's own guidance says to start reducing, and it is deliberately before
the player feels anything: waiting for frames to suffer means waiting for the OS to have
already throttled the chip.

Two things about `Urgent` worth knowing. Its interval defaults to zero, meaning every
evaluation, which takes the ladder from the top rung to the bottom in about two seconds — the
right answer for a device the OS is already clocking down, but also the most abrupt change the
game can make. `Heat > Urgent Step Seconds` turns that jump into a slide.

And **iOS has no `MODERATE`**. Apple exposes four states, `serious` maps to `SEVERE` here, so
on iPhone the band after `fair` is `Urgent` with nothing in between. If the two-second descent
is too abrupt for your game, iPhone is where you will see it first.

Every Android JNI call is wrapped in try/catch: several OEM ROMs throw, and one failure
disables thermal input rather than retrying forever.

### iOS builds work

`MobileQualityThermalNative.mm` ships inside the package with its `.meta` pinned to a fixed
GUID, marked iOS-only. `MobileQualityThermalIOSPostProcess` runs on every iOS build, verifies
the file reached the generated Xcode project, and adds it to **UnityFramework** — not the app
target, which is the usual mistake — if a stale package cache dropped it. Both outcomes are
logged, because a silently missing plugin looks exactly like a working one.

No framework is linked: the plugin only touches `NSProcessInfo`, and Foundation is always
linked. You never open Xcode.

Setup's iOS Player Settings are a short list next to the Android one, and deliberately so: iOS
decides most of it for you — the scripting backend is always IL2CPP, the architecture is always
ARM64, the texture format comes from the platform defaults. What is left is Metal pinned rather
than left on Auto, multithreaded rendering, and batching. Batching is the one that matters:
`m_BuildTargetBatching` is keyed per platform under the name **`iPhone`**, so the Android entry
does nothing at all for an iOS build.

---

## The overlay

Two gates. The `MAQ_DEVELOPER_MODE` define decides whether the overlay **exists**; the
**Show** checkbox decides whether it is **drawn**. A single developer build can go to testers
with the HUD off, and a release build carries none of it — the class does not exist there.

The default is twelve lines:

```
MOBILE QUALITY                              X
PERFORMANCE
FPS         60 / 60
Frame       16.9 p75 · 17.8 p95 · 92.2 worst
Budget      16.7 ms   ok
ADAPTATION
Step        2 of 0-8  (0 = best)
Applied     0.80 · shadows 512 · 60fps
Ladder      perf 2 · floor 6
Proven      2
State       steady · Holding
THERMAL
Thermal     cool · None · headroom 0.42
```

`Ladder` is the line to read when something looks wrong. It splits the applied rung into its
two parts, because a rung lost to heat reads very differently from a rung lost to frame times
and only one of them is going to come back on its own:

```
Ladder      perf 2 + 6 heat · floor 6
```

`Fields` also offers **Quality**, **URP**, **Device**, **Screen**, **Memory** and **Adaptive
Performance** for when you are chasing something specific. Anchor is any corner, edge centre
or dead centre; **F1** or four taps in the top-right corner toggle it, **X** closes it, the
**MAQ** button reopens it.

---

## API

```csharp
using MobileAdjustGameQuality;

var quality = MobileQualityManager.Instance;

int      step   = quality.CurrentStepIndex;
float    p75    = quality.Stats.P75Ms;
float    budget = quality.CurrentStep.FrameBudgetMs;
HeatBand heat   = quality.Heat;          // Cool / Hold / Shed / Urgent

// Hold everything still during a cutscene.
quality.AllowAdjustments = false;

// Force a rung.
quality.ApplyStep(0);

quality.OnStepChanged += index => Debug.Log($"now on rung {index}");
```

---

## Notes

**In the editor the ladder is pinned to rung 0** and does not move — `Adaptation > Best Step
In Editor`, on by default. A desktop running a mobile game has performance to spare, so all
adaptation achieves in Play mode is making the Game view look worse than the build will, over
a scene you are trying to look at. Frame times and temperature are still measured and still
shown in the overlay; only the decision is suppressed, and the remembered rung is neither read
nor written. Turn it off for the few minutes you want to watch the ladder work.

The check is inside `#if UNITY_EDITOR`, so a prefab saved with it ticked cannot pin a shipped
game to rung 0.

**The editor is a poor place to judge any of this** even with the pin off. The Game view
renders a phone resolution on a desktop panel, alongside the inspector and the console, with
the editor's own stalls in the frame times. The p75 and the 500 ms stall rejection make the
ladder robust to that noise, but the numbers still say nothing about a phone. An Android build
answers in a minute.

**Moving the ladder in the editor writes to the URP asset on disk.** With the pin on this no
longer happens by itself, but if you turn it off, `MAQ-URP.asset` will hold whatever the
ladder settled on after a play session. Re-run Setup to rewrite it. Player builds are
unaffected either way.

**Adaptive Performance is optional.** If Unity's package is installed, the overlay's Adaptive
Performance group reports its thermal data, bottleneck and real CPU/GPU frame times. Nothing
here depends on it.

---

## Developer mode

`Tools > Mobile Quality > Developer Mode` toggles **`MAQ_DEVELOPER_MODE`** on Android, iOS and
Standalone. The `MAQ_` prefix is not decoration: scripting defines are global to the project,
so a generic name collides with any other package that reached for the same word, and the
symptom is one package silently enabling another's debug code.

With the define off the overlay class does not exist and log calls are removed together with
their argument expressions, so nothing allocates. Wire
`DeveloperDefineToggle.AssertStrippedForRelease()` into your build pipeline to fail a release
build that was left with it on.

---

## License

MIT, © Eliran Shaya. See [LICENSE.md](LICENSE.md).
