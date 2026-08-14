# Mobile Adjust Game Quality

Three-tier mobile graphics quality for Unity URP — Low, Balanced, High. One button
generates everything; at runtime it picks a tier from the device and holds the frame
budget by adapting render scale.

![Screenshot placeholder](Documentation~/screenshot.png)

---

## Requirements

- Unity 2022.3 LTS or newer
- URP 14 or newer — installed automatically as a package dependency
- URP must be the active render pipeline in **Project Settings > Graphics**

No other dependencies.

---

## Installation

Install through Unity's Package Manager using the Git URL.

1. Open your project in Unity.
2. Go to **Window → Package Manager**.
3. Click the **+** button in the top-left and choose **Add package from git URL…**
4. Paste the URL below and click **Add**:

```
https://github.com/eliranshaya/Unity-MobileAdjustGameQuality.git?path=/Packages/com.eliranshaya.mobileadjustgamequality
```

To lock to a specific version, append the tag:

```
https://github.com/eliranshaya/Unity-MobileAdjustGameQuality.git?path=/Packages/com.eliranshaya.mobileadjustgamequality#v2.4.0
```

---

## Quick start

1. `Tools > Mobile Quality > Setup`
2. Press **Setup**
3. Drag `Assets/MobileAdjustGameQuality/Generated/MobileQualityManager.prefab` into your
   first scene

That is the whole setup.

Note that the generated assets land in **your** `Assets/` folder, not inside the package.
A package installed from a Git URL lives in `Library/PackageCache` and is read-only, so
anything Setup writes has to live in the project.

---

## What Setup actually does

| Step | Result |
| --- | --- |
| 1 | Checks URP is installed and is the active pipeline. Aborts with a message if not. |
| 2 | Creates `Assets/MobileAdjustGameQuality/Generated/` |
| 3 | 3 `UniversalRendererData` assets — `MAQ-Low-Renderer`, `MAQ-Balanced-Renderer`, `MAQ-High-Renderer` |
| 4 | 3 `UniversalRenderPipelineAsset` assets — `MAQ-URP-Low`, `MAQ-URP-Balanced`, `MAQ-URP-High`, each linked to its renderer |
| 5 | Writes 3 quality levels, each pointing at its URP asset |
| 6 | Applies the recommended mobile Player Settings (each one individually toggleable) |
| 7 | Builds `MobileQualityManager.prefab` — one component — with the real quality level indices baked in |

Running Setup again updates in place — it never duplicates. The button relabels itself to
**Update Existing** once it finds generated assets.

### Quality levels: replace or append

Chosen by the **Replace all quality levels** checkbox.

**Replace (default).** Every existing quality level is deleted and the three MAQ levels are
written at indices **0, 1 and 2**. Tier index and quality level index become the same
number, which is the whole point — there is nothing to look up and nothing to get wrong.
Setup names every level it is about to delete in a confirmation dialog first, and if a
per-platform default quality index no longer exists afterwards it is clamped and reported.

**Append.** Turn the checkbox off and the three levels are added after your own, which are
left exactly as they are. The prefab gets whatever indices the new levels landed on.

### Player Settings it touches

All on by default, each individually toggleable in a foldout, all Android unless noted:

Optimized Frame Pacing · Color Space Linear (project-wide) · Graphics APIs Vulkan then
GLES3 · Multithreaded Rendering · IL2CPP · ARM64 · ASTC texture compression · Static +
Dynamic Batching.

### Remove Everything

Deletes the six generated assets, the prefab and the three MAQ quality levels — and nothing
else. Your Player Settings, Graphics Settings and any non-MAQ quality levels stay as they
are. The confirmation dialog lists every item by name first.

---

## The three tiers

| | Low | Balanced | High |
| --- | --- | --- | --- |
| Render scale at boot | 0.65 | 0.75 | 0.85 |
| Target FPS | 30 | 60 | 60 |
| Render scale range | 0.60 – 0.70 | 0.70 – 0.80 | 0.80 – 0.90 |
| Main light shadows | off | 512 | 1024 |
| Additional lights | 1, per-vertex | 2, per-vertex | 4, per-pixel |
| Additional light shadows | off | off | 512 |
| Soft shadows | off | off | on |
| Shadow distance | 20 | 25 | 40 |
| Cascades | 1 | 1 | 2 |
| Post-processing | off | off | on |
| Reflection probe blending | off | off | on |
| LOD bias | 0.7 | 1.0 | 1.5 |
| Texture mipmap limit | half | full | full |
| Thermal fallback FPS | — | 30 | 30 |

HDR and MSAA are off on every tier: on a phone-sized screen both cost bandwidth out of
proportion to what they give back.

---

## API

Switch tier from a settings menu:

```csharp
using MobileAdjustGameQuality;

// 0 Low, 1 Balanced, 2 High.
// isPlayerChoice: true makes it sticky, so device detection will not override it
// on the next launch.
MobileQualityManager.Instance.SetTier(1, isPlayerChoice: true);
```

Stop the adaptive controller from changing anything while the player is paying attention:

```csharp
using MobileAdjustGameQuality;

var quality = MobileQualityManager.Instance;

void OnCombatStart() => quality.AllowAdjustments = false;

// Measurement never stopped, so a change that came due mid-fight is applied now
// rather than restarting its timer from zero.
void OnCombatEnd() => quality.AllowAdjustments = true;
```

React to a tier change in gameplay code:

```csharp
MobileQualityManager.Instance.OnTierChanged += tier =>
{
    vfxRoot.SetActive(tier >= QualityTier.Balanced);
};
```

Read what the loop is seeing:

```csharp
float p95     = quality.LastP95Ms;              // 95th percentile frame time
float budget  = quality.FrameBudgetMs;          // 16.67 at 60 FPS
float scale   = quality.CurrentRenderScale;
bool  hot     = quality.IsThermalThrottled;   // severe: pinned to minimum
bool  warm    = quality.IsDeviceWarm;         // OS says do not raise quality
bool  shed    = quality.IsSheddingHeat;       // stepping down because of heat
bool  locked  = quality.IsRenderScaleLocked;    // oscillation guard engaged
```

Import the **Menu and tier-reactive content** sample from the Package Manager for two
complete examples.

---

## Structure

One MonoBehaviour, `MobileQualityManager`. Everything else is a plain class it owns:

| Class | Kind | Job |
| --- | --- | --- |
| `MobileQualityManager` | MonoBehaviour | The only component. Owns the lifetime and the coroutines. |
| `FrameTimeSampler` | plain | Fixed ring buffer, p95 and mean, zero allocation. |
| `AdaptiveScaler` | plain | Render scale policy, including the oscillation guard. |
| `TierArbiter` | plain | When the tier itself should move, and the promotion budget. |
| `TierHistory` | plain | What the last sessions settled on, persisted in PlayerPrefs. |
| `ThermalSensor` | plain, `IDisposable` | Android JNI and the iOS plugin call, both wrapped in try/catch. |
| `TierSettings` | serializable | Per-tier data. |
| `AdaptiveSettings` | serializable | Render scale tuning, one inspector foldout. |
| `TierAdaptationSettings` | serializable | Tier change thresholds and session memory. |
| `OverlaySettings` | serializable | Debug HUD position, fields and styling. |
| `MobileQualityThermalNative.mm` | native, iOS | Six lines bridging `NSProcessInfo.thermalState`. |
| `MobileQualityThermalIOSPostProcess` | editor, iOS only | Verifies the `.mm` reached the Xcode project. |

Three components would mean three execution orders, three lifetimes and three things that
have to agree with each other. None of the three needed a transform or its own `Update`.

### Cost per frame

`Update` does exactly one thing — write one float into the ring buffer. That cannot become
a coroutine: a p95 is only meaningful if every frame is measured, and a coroutine resuming
each frame costs an iterator `MoveNext` plus a scheduler visit, strictly more than the
method call it would replace.

Everything that does not need per-frame resolution is a coroutine instead — the thermal
poll every 5 s and the decision tick every 0.5 s. Their `WaitForSecondsRealtime` instances
are constructed once and reused, so the steady-state GC allocation of the whole package is
**zero bytes**. `WaitForSecondsRealtime` rather than `WaitForSeconds` because both loops
have to keep running while the game sits at `Time.timeScale = 0` in a pause menu.

---

## How it works

**Automatic tier changes.** The tier is not fixed after startup. When render scale has run
out of room, the tier itself moves:

| Direction | Condition | Rationed? |
| --- | --- | --- |
| **Down** | Render scale pinned at the tier minimum **and** p95 over budget for 30 s | Never. If the device cannot hold the tier, nothing is gained by staying. |
| **Up** | Render scale at the tier maximum, p95 under budget × 0.6, device cool, for 60 s | Yes — 3 upward changes per session, and only while the device is not marked settled. |

Every tier change rebuilds the pipeline and the player sees a hitch, which is why raising is
a bet and lowering is not. A promotion is watched for 2 minutes: if the tier falls again in
that window, the promotion was wrong, upward moves stop for the session, and the device is
marked **settled** — no future session pays a hitch to learn the same thing again.

**Session memory.** The tier each of the last 5 sessions settled on is persisted, and the
most recent one decides where the next launch starts. Device detection only runs on the very
first launch; after that the system starts from a measurement rather than a guess. A device
that settled on Low starts on Low, and because promotion needs 60 seconds of proven headroom
it will not even be attempted unless something really changed.

The record is written when the session starts and rewritten on every tier change, not saved
on quit — mobile apps are very often killed without `OnApplicationQuit` ever running.

A tier the system picked for itself is never marked as a player choice, so a real settings
menu still overrides everything.

**Static device tiering at boot.** `DeviceTierDetector` scores the device additively from
RAM, GPU model (Apple / Adreno / Mali / Xclipse / PowerVR, parsed out of the driver string),
core count, screen resolution, graphics API and shader level. Score ≥ 5 is High, ≥ 2 is
Balanced, otherwise Low. The tier switch happens once during `Awake`, because switching the
URP asset forces a pipeline rebuild the player sees as a hitch. After that the tier only
changes if the player changes it.

**Dynamic render scale at runtime.** Frame times go into a ring buffer and a coroutine
evaluates twice a second, after a 3-second warmup that also restarts on every tier change.
Decisions use the 95th percentile in **milliseconds**, never averaged FPS — a 60 FPS average
with a 45 ms spike every second reads as smooth to an average and as stuttering to a person,
and milliseconds are linear in the thing being controlled.

- p95 above budget × 1.08 for 4 sustained seconds → render scale down 0.05, then 20 s cooldown
- p95 below budget × 0.75 for 12 sustained seconds → render scale up 0.05, then 30 s cooldown

The step-up window is three times the step-down window on purpose: a wrong step down costs
sharpness, a wrong step up costs frames.

**The oscillation guard.** Some devices sit exactly on the boundary: they hold the budget at
0.85, run out of headroom, drop to 0.80, look fine, climb back to 0.85, and repeat. Every
one of those moves reallocates render targets, so a device that never settles pays a cost
forever and gets nothing for it.

`AdaptiveScaler` counts completed down-then-up cycles. Two consecutive cycles and the
**next** upward step is refused rather than applied and undone — the scale stays on the
lower value that already held the budget — and upward steps are locked for 5 minutes. If
the pattern comes back after the lock expires, the lock becomes permanent for the session.
Downward steps are never blocked by any of this: protecting the frame rate outranks
everything else. A step in the same direction twice in a row resets the count, because that
is a device settling somewhere, not a device wobbling.

**Thermal awareness.** `ThermalSensor` polls Android's `PowerManager` every 5 seconds —
`getCurrentThermalStatus()` on API 29+, and `getThermalHeadroom(10)` on API 30+. Every JNI
call is wrapped in try/catch because several OEM ROMs throw, and one failure disables
thermal input rather than retrying forever.

There is **no temperature in degrees** anywhere in this, and there cannot be — neither OS
exposes one to apps, because the temperature at which one phone throttles is meaningless on
another. What you get instead is the OS's own verdict.

### The heat response has three bands

| Band | Android | iOS | What happens |
| --- | --- | --- | --- |
| **cool** | status 0 and headroom < 0.85 | nominal | Normal frame-time adaptation, up and down. |
| **warm** | status ≥ 1, or headroom ≥ 0.85 | fair | **Render scale steps down every 10 s** until it reaches the tier minimum — regardless of frame times. Upward steps blocked. |
| **severe** | status ≥ 3, or headroom ≥ 1.0 | serious / critical | Render scale **pinned straight to the tier minimum**. If p95 is still over budget, the frame cap drops to `thermalFallbackFrameRate` — last, because halving the frame rate is the most noticeable change a game can make. |

The warm band is the one that matters most, and it is deliberately not conditional on frame
times. Waiting for p95 to go over budget means waiting for the OS to have already throttled
the chip — at which point the frames are gone and there is nothing left to do but react.
Shedding a step of render scale while there is still headroom is what keeps a session from
getting there at all.

Cooling down unwinds in reverse: frame cap first, then the pin releases, then the render
scale climbs back in 0.05 steps. Going from 0.60 back to 0.85 takes roughly three and a half
minutes on purpose — fast recovery is how you end up warm again a minute later.

### Signal reference

| Signal | Value | Meaning | Band |
| --- | --- | --- | --- |
| `getCurrentThermalStatus()` | 0 NONE | no throttling | cool |
| | 1 LIGHT | throttling started | warm |
| | 2 MODERATE | frame times drifting | warm |
| | 3 SEVERE | actively clocking down | severe |
| | 4 CRITICAL | OS out of options | severe |
| `getThermalHeadroom(10)` | < 0.85 | headroom left | cool |
| | ≥ 0.85 | throttling forecast | warm |
| | ≥ 1.00 | throttling now | severe |
| `NSProcessInfo.thermalState` | 0 nominal | no pressure | cool |
| | 1 fair | slightly elevated | warm |
| | 2 serious | reduce work now | severe |
| | 3 critical | cool down immediately | severe |

Headroom is the most useful of the three because it is a **forecast normalised to the
device's own throttling point** — 1.0 means "the temperature at which *this* phone
throttles", so 0.85 reads as "85% of the way to your limit" on any handset without a lookup
table. It also moves *before* the status does, which is what buys the loop time to act on
its own terms.

### Platform support

| | Status | Forecast | How |
| --- | --- | --- | --- |
| **Android** | ✔ API 29+ | ✔ API 30+ | `PowerManager` over JNI. No permission, no plugin, works in a plain build. |
| **iOS** | ✔ iOS 11+ | ✘ Apple exposes none | `NSProcessInfo.thermalState` through the bundled `MobileQualityThermalNative.mm`. |
| **Everything else** | ✘ | ✘ | `Unknown`, and adaptation runs from frame times alone. |

The iOS plugin ships inside the package at `Runtime/Plugins/iOS/`, with its `.meta` pinned
so Unity compiles it into UnityFramework. `MobileQualityThermalIOSPostProcess` then runs on
every iOS build, checks the `.mm` actually reached the generated Xcode project, and adds it
to UnityFramework's Compile Sources if a stale package cache dropped it. Both outcomes are
logged — a silently missing plugin looks exactly like a working one until you notice thermal
never reports anything.

No framework is linked, because the plugin only touches `NSProcessInfo` and Foundation is
always linked into UnityFramework. You never need to open Xcode.

When the device goes severe the response is ordered, and the order is the point: **first**
the render scale is pinned to the tier minimum; **only** if that is still not enough does the
frame cap drop to `thermalFallbackFrameRate`. Halving the frame rate is the most noticeable
change a game can make, so it is the last resort. Cooling down unwinds in reverse — frame
rate back first, then the render scale is released to climb again. While the device is merely
warm, quality is held but never raised.

---

## Notes and gotchas

**In the Editor, changing `renderScale` at runtime writes to the URP asset on disk.**
The adaptive controller does exactly that, so after a play session your generated URP assets
may hold whatever scale it settled on. Check the baseline values before you build, or re-run
Setup to rewrite them. This does not happen in a player build, where the asset is read-only.

**Switching the URP asset causes a pipeline rebuild hitch.** That is why tier switches happen
once at boot and only `renderScale` moves at runtime. If you expose a quality dropdown, expect
a visible stall when the player uses it — putting it behind a pause menu rather than in the
HUD is the usual answer.

**Device detection is a heuristic and is wrong on roughly 20% of devices.** GPU name strings
are inconsistent between OEMs and drivers, RAM does not predict sustained throughput, and
nothing at boot can measure thermal behaviour. Ship telemetry keyed on
`SystemInfo.deviceModel` together with measured p95 frame time, and build a server-side
override table for the models you have seen. Treat the detector as the cold-start guess for
models you have not.

---

## Developer mode

`Tools > Mobile Quality > Developer Mode` toggles the `DEVELOPER_MODE` scripting define on
Android, iOS and Standalone. With it on you get diagnostics through `QualityDevLog` and the
on-screen overlay.

### The overlay

Two gates, deliberately. The define decides whether the overlay **exists**; the **Show**
checkbox under *Overlay* on the prefab decides whether it is **drawn**. So you can hand a
single developer build to testers and still turn the HUD off, and a release build carries
none of it either way.

| Setting | What it does |
| --- | --- |
| **Show** | Master switch. Off means the component is never even added — no `OnGUI` call at all. |
| **Anchor** | Nine positions: any corner, any edge centre, or dead centre. The panel measures itself and pins accordingly. |
| **Fields** | Multi-select dropdown, 12 readouts: tier, target FPS, frame budget, current FPS, average ms, p95 ms, render scale, thermal, heat band, oscillation lock, adjustments gate, device. Tick only what you are chasing. |
| **Show Tier Buttons** | The three live tier-switch buttons. |
| **Show Toggle Button** | The small **MAQ** button drawn at the anchor while the panel is closed. Turn it off for a completely clean screen. |
| **Open On Start** | Whether the panel starts open or collapsed to the toggle button. |
| **Scale** | Multiplies the DPI-derived font size, 0.5x to 3x. |
| **Background Opacity** | Panel darkness. |
| **Margin** | Distance from the screen edge. |

Opening and closing at runtime: tap the **MAQ** button to open, the **X** in the panel
header to close. **F1** toggles either way, and four taps in the top-right corner do the
same on a device where you turned the toggle button off. Taps that land on the panel itself
never count towards the corner gesture, so anchoring to the top right does not make the
overlay's own buttons unclickable. The lines are rebuilt once per frame on the IMGUI layout pass and reused for
the repaint, so the interpolated strings are not built twice.

With the define off all of that is compiled out. The overlay class does not exist, and the
log calls are removed together with their argument expressions, so the interpolated strings
in the twice-a-second evaluation loop are never built.

Wire `DeveloperDefineToggle.AssertStrippedForRelease()` into your build pipeline to make a
release build fail if the define was left on.

---

## License

MIT, © Eliran Shaya. See [LICENSE.md](LICENSE.md).
