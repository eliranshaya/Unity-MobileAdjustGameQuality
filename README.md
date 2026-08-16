# Mobile Adjust Game Quality

**One button generates a mobile-tuned URP setup. One prefab keeps the frame budget on the
device.**

Quality is not a menu the player never opens and not a guess made from the device model. It is
one number — which rung of an eight-rung ladder is applied — and the device itself decides it,
from its own frame times and from how hot it is.

Unity 2022.3 LTS or newer · URP 14+ · Android and iOS · no dependencies.

---

## Install

**Window → Package Manager → `+` → Add package from git URL**, then paste:

```
https://github.com/eliranshaya/Unity-MobileAdjustGameQuality.git?path=/Packages/com.eliranshaya.mobileadjustgamequality
```

That tracks the default branch. To pin a version, add the tag:

```
https://github.com/eliranshaya/Unity-MobileAdjustGameQuality.git?path=/Packages/com.eliranshaya.mobileadjustgamequality#v3.2.0
```

<details>
<summary>Or add it to <code>Packages/manifest.json</code> by hand</summary>

```json
{
  "dependencies": {
    "com.eliranshaya.mobileadjustgamequality": "https://github.com/eliranshaya/Unity-MobileAdjustGameQuality.git?path=/Packages/com.eliranshaya.mobileadjustgamequality#v3.2.0"
  }
}
```
</details>

The `?path=` part is required — the package lives in a subfolder of this repository, which is
a full Unity project used to develop and test it.

**Before you start:** URP must be the active pipeline in **Project Settings → Graphics**. The
Setup window checks and tells you if it is not.

---

## Use

**1.** `Tools > Mobile Quality > Setup`

**2.** Press **Setup**. It generates, into *your* `Assets/`, never into the package:

- `MAQ-URP.asset` and `MAQ-Renderer.asset`, tuned for a phone
- a single quality level named **MAQ Mobile** — every existing level is replaced, and the
  window names each one it is about to delete before you press anything
- the recommended Player Settings for Android and iOS
- `MobileQualityManager.prefab`

**3.** Drag `Assets/MobileAdjustGameQuality/Generated/MobileQualityManager.prefab` into your
first scene.

That is all of it. Running Setup again updates in place.

---

## What happens at runtime

```
FPS         60 / 60
Frame       17.0 p75 · 17.5 p95 · 17.7 worst
Budget      16.7 ms   ok
Step        0 of 0-7  (0 = best) · saved
Applied     1.00 · shadows 1024 · 60fps
State       steady · Holding
Thermal     cool · None · headroom 0.42
```

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

- **Down** when frame times miss the target, or when the OS reports the device hot enough to
  act on — in three bands, so the most common thermal state on a phone stops the climb
  without starting a descent
- **Straight to 30 fps** when the device is not short of its target but on the wrong one —
  a stable 30 beats an unstable 44, and runs far cooler
- **Up** only when the device is cool **and** holding, and much more slowly than it goes down

Shadows are all or nothing. Shortening shadow distance instead would leave near objects
shadowed and objects a metre behind them not, and that boundary reads as a bug rather than as
lower quality.

The rung a device settles on is remembered, so the next launch starts from a measurement
instead of a guess.

Thermal comes from `PowerManager` on Android and `NSProcessInfo.thermalState` on iOS, through
a bundled plugin that a post-process build step verifies reached the Xcode project. You never
open Xcode.

In the editor the ladder is pinned to rung 0, because a desktop has nothing useful to tell it.

---

## Documentation

The full reference — every threshold, why p75 and not p95, the two startup phases, the
overlay, the API, and the reasoning behind each decision — is in the package README:

**[📖 Full documentation](Packages/com.eliranshaya.mobileadjustgamequality/README.md)**

---

## Repository layout

```
Unity-MobileAdjustGameQuality/          ← full Unity project, for developing and testing
└── Packages/
    └── com.eliranshaya.mobileadjustgamequality/   ← the package that gets installed
        ├── Runtime/
        ├── Editor/
        └── Samples~/
```

---

## License

MIT © Eliran Shaya. See [LICENSE](LICENSE).
