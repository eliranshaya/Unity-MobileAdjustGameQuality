# Mobile Adjust Game Quality

One button generates a mobile-tuned URP setup. One prefab keeps the frame budget on the
device by moving along a quality ladder, driven by frame times and by how hot the phone is.

---

## Install

**Window → Package Manager → + → Add package from git URL:**

```
https://github.com/eliranshaya/Unity-MobileAdjustGameQuality.git?path=/Packages/com.eliranshaya.mobileadjustgamequality
```

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
| 1 | 0.85 | on | 1024 | 60 |
| **2** | **0.80** | on | 512 | 60 |
| 3 | 0.70 | on | 512 | 60 |
| 4 | 0.80 | **off** | — | 60 |
| 5 | 0.65 | off | — | 60 |
| 6 | 0.85 | on | 512 | **30** |
| 7 | 0.65 | off | — | 30 |

Rung 2 is where a device with no history starts. Every rung is editable on the prefab.

Three things are worth pointing out:

**Shadows are all or nothing.** Shadow *distance* is not a rung, and deliberately so:
shortening it leaves near objects with shadows and objects a metre behind them without, and
that boundary reads as a bug rather than as lower quality. Distance is one authored value on
the profile; the ladder only switches shadows on or off.

**Rung 4 raises the render scale while switching shadows off.** Losing the shadow pass frees
more than the extra pixels cost, so it looks better *and* runs better than rung 3.

**Rung 6 gives up 60 fps and hands quality back.** A 30 fps budget is twice as long, so half
the frame rate buys a much better looking frame. Frame rate is last because it is the most
noticeable change a game can make, and shadows come before it because they are the only rung
that helps a CPU-bound frame — render scale moves pixels, and a frame limited by draw calls
does not care how many pixels it is drawing.

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

So going down is driven by either. Coming back up needs the device to be cool **and** holding
its target.

| Trigger | Condition | Then |
| --- | --- | --- |
| Missing target | frame time > budget × 1.08 for 3 s | down one rung |
| **Wrong frame rate** | frame time > budget × 1.35 for 3 s | **jump straight to the first 30 fps rung** |
| Heat | device reports warm | down one rung, every 30 s |
| Severe heat | OS says it is throttling | down one rung immediately |
| Recovered | cool **and** frame time ≤ budget × 1.05 for 20 s | up one rung |

The wrong-frame-rate jump is the one that matters most in practice. 1.35 is roughly 44 fps
against a 60 target — at that point the device is not short of its target, it is on the wrong
target, and walking down one rung at a time would spend twenty seconds overheating to reach a
conclusion already visible in the numbers. A stable 30 looks and feels better than an unstable
44, and generates far less heat.

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

Temperature is polled every **30 seconds**. It moves on the scale of minutes, and on Android
asking faster than once a second returns NaN.

---

## Memory

The rung a device settles on is written to `PlayerPrefs` after it has held for 10 seconds, and
the next launch starts there. A rung that held on real hardware is a measurement; anything
decided at startup is a guess.

The ladder's length is stored alongside it, so editing the ladder discards a remembered rung
rather than applying it to the wrong row.

`Tools > Mobile Quality > Forget Remembered Step` clears it.

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

Warm is status ≥ 1 or headroom ≥ 0.85 — early on purpose, because raising quality on a warming
device is the change most likely to be undone seconds later. Severe is status ≥ 3 or headroom
≥ 1.0.

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

---

## The overlay

Two gates. The `MAQ_DEVELOPER_MODE` define decides whether the overlay **exists**; the
**Show** checkbox decides whether it is **drawn**. A single developer build can go to testers
with the HUD off, and a release build carries none of it — the class does not exist there.

The default is ten lines:

```
MOBILE QUALITY                              X
PERFORMANCE
FPS         60 / 60
Frame       16.9 p75 · 17.8 p95 · 92.2 worst
Budget      16.7 ms   ok
ADAPTATION
Step        2 of 0-7  (0 = best) · saved
Applied     0.80 · shadows 512 · 60fps
State       steady · Holding
THERMAL
Thermal     cool · None · headroom 0.42
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

int   step   = quality.CurrentStepIndex;
float p75    = quality.Stats.P75Ms;
float budget = quality.CurrentStep.FrameBudgetMs;
bool  warm   = quality.Thermal.IsWarm;

// Hold everything still during a cutscene.
quality.AllowAdjustments = false;

// Force a rung.
quality.ApplyStep(0);

quality.OnStepChanged += index => Debug.Log($"now on rung {index}");
```

---

## Notes

**In the editor, moving the ladder writes to the URP asset on disk.** After a play session
`MAQ-URP.asset` holds whatever the ladder settled on. Re-run Setup to rewrite it. This does
not happen in a player build.

**The editor is a poor place to judge any of this.** The Game view renders a phone resolution
on a desktop panel, alongside the inspector and the console, with the editor's own stalls in
the frame times. The p75 and the 500 ms stall rejection make the ladder robust to that noise,
but the numbers still say nothing about a phone. An Android build answers in a minute.

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
