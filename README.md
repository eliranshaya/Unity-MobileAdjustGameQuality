# Unity-MobileAdjustGameQuality

Three-tier mobile graphics quality for Unity URP — Low, Balanced, High. One button
generates the URP assets, renderers, quality levels, Player Settings and a manager prefab.
At runtime it picks a tier from the device and holds the frame budget by adapting render
scale from p95 frame time, with thermal awareness.

![Screenshot placeholder](Documentation~/screenshot.png)

This repository is a full Unity project used to develop and test the package. The package
itself is `Packages/com.eliranshaya.mobileadjustgamequality/`.

---

## Requirements

- Unity 2022.3 LTS or newer
- URP 14 or newer — installed automatically as a package dependency
- URP must be the active render pipeline in **Project Settings > Graphics**

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
https://github.com/eliranshaya/Unity-MobileAdjustGameQuality.git?path=/Packages/com.eliranshaya.mobileadjustgamequality#v1.0.1
```

---

## Quick start

1. `Tools > Mobile Quality > Setup`
2. Press **Setup**
3. Drag `Assets/MobileAdjustGameQuality/Generated/MobileQualityManager.prefab` into your
   first scene

The generated assets land in your `Assets/` folder, not inside the package — a package
installed from Git is read-only, so anything Setup writes has to live in the project.

Setup generates 3 URP assets, 3 renderer assets, writes 3 quality levels, applies the
recommended mobile Player Settings and builds the prefab. Running it again updates in place
instead of duplicating.

Quality levels are written in one of two modes, chosen by the **Replace all quality levels**
checkbox:

- **Replace** (default) — every existing quality level is deleted and MAQ Low / Balanced /
  High are written at indices **0, 1, 2**, each pointing at its generated URP asset. Setup
  names every level it is about to delete in a confirmation dialog first.
- **Append** — the three levels are added after your own, which are left untouched.

---

## The three tiers

| | Low | Balanced | High |
| --- | --- | --- | --- |
| Render scale | 0.75 | 0.85 | 1.00 |
| Target FPS | 30 | 60 | 60 |
| Main light shadows | off | 512 | 1024 |
| Additional lights | 1, per-vertex | 2, per-vertex | 4, per-pixel |
| Additional light shadows | off | off | 512 |
| Soft shadows | off | off | on |
| Shadow distance | 20 | 25 | 40 |
| Cascades | 1 | 1 | 2 |
| Post-processing | off | off | on |
| LOD bias | 0.7 | 1.0 | 1.5 |

---

## API

```csharp
using MobileAdjustGameQuality;

// 0 Low, 1 Balanced, 2 High. isPlayerChoice makes the selection stick across launches.
QualityTierManager.Instance.SetTier(1, isPlayerChoice: true);
```

```csharp
// Hold every adaptive change until a natural break.
var adaptive = QualityTierManager.Instance.GetComponent<AdaptiveQualityController>();
adaptive.AllowAdjustments = false;   // entering combat
adaptive.AllowAdjustments = true;    // combat over
```

---

## How it works

**Static device tiering at boot.** `DeviceTierDetector` scores RAM, GPU model, core count,
screen resolution, graphics API and shader level, and maps the score onto a tier. The switch
happens once in `Awake`, because changing the URP asset forces a pipeline rebuild the player
sees as a hitch.

**Dynamic render scale at runtime.** `AdaptiveQualityController` measures unscaled frame time
and evaluates twice a second, deciding on the 95th percentile in milliseconds rather than
averaged FPS. Four seconds sustained over budget steps the render scale down by 0.05; twelve
seconds sustained under budget steps it up, each followed by a cooldown. Render scale is the
only setting moved at runtime.

**Thermal awareness.** When Android reports severe throttling, the render scale is pinned to
the tier minimum first, and the frame cap is only dropped if that was not enough — halving
the frame rate is the most noticeable change a game can make, so it is the last resort.

---

## Notes and gotchas

**In the Editor, changing `renderScale` at runtime writes to the URP asset on disk.**
After a play session the generated URP assets may hold whatever scale the controller settled
on. Verify the baseline values before building, or re-run Setup to rewrite them. This does
not happen in a player build.

**Switching the URP asset causes a pipeline rebuild hitch.** That is why tier switches happen
once at boot and only `renderScale` moves at runtime.

**Device detection is a heuristic and is wrong on roughly 20% of devices.** Ship telemetry
keyed on `SystemInfo.deviceModel` plus measured p95 frame time, and build a server-side
override table. The detector is the cold-start guess, not the answer.

---

Full documentation:
[`Packages/com.eliranshaya.mobileadjustgamequality/README.md`](Packages/com.eliranshaya.mobileadjustgamequality/README.md)

## License

MIT, © Eliran Shaya. See [LICENSE](LICENSE).
