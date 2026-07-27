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
https://github.com/eliranshaya/Unity-MobileAdjustGameQuality.git?path=/Packages/com.eliranshaya.mobileadjustgamequality#v1.0.0
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
| 7 | Builds `MobileQualityManager.prefab` with the real quality level indices baked in |

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
| Render scale | 0.75 | 0.85 | 1.00 |
| Target FPS | 30 | 60 | 60 |
| Render scale range | 0.60 – 0.75 | 0.70 – 0.85 | 0.80 – 1.00 |
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
QualityTierManager.Instance.SetTier(1, isPlayerChoice: true);
```

Stop the adaptive controller from changing anything while the player is paying attention:

```csharp
using MobileAdjustGameQuality;

var adaptive = QualityTierManager.Instance.GetComponent<AdaptiveQualityController>();

void OnCombatStart() => adaptive.AllowAdjustments = false;

// Measurement never stopped, so a change that came due mid-fight is applied now
// rather than restarting its timer from zero.
void OnCombatEnd() => adaptive.AllowAdjustments = true;
```

React to a tier change in gameplay code:

```csharp
QualityTierManager.Instance.OnTierChanged += tier =>
{
    vfxRoot.SetActive(tier >= QualityTier.Balanced);
};
```

Read what the controller is seeing:

```csharp
float p95    = adaptive.LastP95Ms;                            // 95th percentile frame time
float budget = QualityTierManager.Instance.FrameBudgetMs;     // 16.67 at 60 FPS
float scale  = QualityTierManager.Instance.CurrentRenderScale;
```

Import the **Menu and tier-reactive content** sample from the Package Manager for two
complete examples.

---

## How it works

**Static device tiering at boot.** `DeviceTierDetector` scores the device additively from
RAM, GPU model (Apple / Adreno / Mali / Xclipse / PowerVR, parsed out of the driver string),
core count, screen resolution, graphics API and shader level. Score ≥ 5 is High, ≥ 2 is
Balanced, otherwise Low. The tier switch happens once during `Awake`, because switching the
URP asset forces a pipeline rebuild the player sees as a hitch. After that the tier only
changes if the player changes it.

**Dynamic render scale at runtime.** `AdaptiveQualityController` samples unscaled frame time
into a ring buffer and evaluates twice a second, after a 3-second warmup that also restarts
on every tier change. Decisions use the 95th percentile in **milliseconds**, never averaged
FPS — a 60 FPS average with a 45 ms spike every second reads as smooth to an average and as
stuttering to a person, and milliseconds are linear in the thing being controlled.

- p95 above budget × 1.08 for 4 sustained seconds → render scale down 0.05, then 20 s cooldown
- p95 below budget × 0.75 for 12 sustained seconds → render scale up 0.05, then 30 s cooldown

The step-up window is three times the step-down window on purpose: a wrong step down costs
sharpness, a wrong step up costs frames.

**Thermal awareness.** `ThermalMonitor` polls Android's `PowerManager` every 5 seconds —
`getCurrentThermalStatus()` on API 29+, and `getThermalHeadroom(10)` on API 30+, which is a
forecast where 1.0 means throttling now. Every JNI call is wrapped in try/catch because
several OEM ROMs throw, and one failure disables thermal input rather than retrying forever.

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
Android, iOS and Standalone. With it on you get diagnostics through `QualityDevLog` and an
on-screen overlay — toggle with **F1**, or four taps in the top-right corner — showing tier,
budget, average and p95 frame time, render scale, thermal state and GPU name, plus three
buttons to switch tier live.

With the define off all of that is compiled out. The overlay class does not exist, and the
log calls are removed together with their argument expressions, so the interpolated strings
in the twice-a-second evaluation loop are never built.

Wire `DeveloperDefineToggle.AssertStrippedForRelease()` into your build pipeline to make a
release build fail if the define was left on.

---

## License

MIT, © Eliran Shaya. See [LICENSE.md](LICENSE.md).
