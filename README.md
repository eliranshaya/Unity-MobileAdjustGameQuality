# Mobile Adjust Game Quality

**One button generates a mobile-tuned URP setup. One prefab keeps the frame budget on the
device.**

Quality is not a menu the player never opens and not a guess made from the device model. It is
one number — which rung of a nine-rung ladder is applied — and the device itself decides it,
from its own frame times and from how hot it is.

[![Latest release](https://img.shields.io/github/v/tag/eliranshaya/Unity-MobileAdjustGameQuality?label=latest&sort=semver&color=2ea44f)](https://github.com/eliranshaya/Unity-MobileAdjustGameQuality/tags)
[![Unity 2022.3+](https://img.shields.io/badge/unity-2022.3%2B-black)](https://unity.com)
[![URP 14+](https://img.shields.io/badge/URP-14%2B-black)](https://docs.unity3d.com/Manual/urp/urp-introduction.html)
[![License MIT](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

Unity 2022.3 LTS or newer · URP 14+ · Android and iOS · no dependencies.

---

## Install

**Window → Package Manager → `+` → Add package from git URL**, then paste:

```
https://github.com/eliranshaya/Unity-MobileAdjustGameQuality.git?path=/Packages/com.eliranshaya.mobileadjustgamequality#v3.3.0
```

That is the current release — the green badge above always shows the newest tag, and swapping
the `#v…` at the end is the only thing that changes between versions.

<details>
<summary>Other ways to add it</summary>

**Always the newest commit**, by leaving the tag off:

```
https://github.com/eliranshaya/Unity-MobileAdjustGameQuality.git?path=/Packages/com.eliranshaya.mobileadjustgamequality
```

Convenient, but it is whatever is on `main` at the moment you add it, which may be ahead of
the last release. Pin the tag for anything you ship.

**By hand**, in `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.eliranshaya.mobileadjustgamequality": "https://github.com/eliranshaya/Unity-MobileAdjustGameQuality.git?path=/Packages/com.eliranshaya.mobileadjustgamequality#v3.3.0"
  }
}
```
</details>

The `?path=` part is required — the package lives in a subfolder of this repository, which is
a full Unity project used to develop and test it.

**Before you start:** URP must be the active pipeline in **Project Settings → Graphics**. The
Setup window checks and tells you if it is not.

### Updating to a newer version

Package Manager will **not** pick up a new version on its own, and this catches everyone. A
git URL is resolved once and the resolved commit is written into `Packages/packages-lock.json`,
so the project keeps using it until you say otherwise — even with no tag in the URL.

Edit the `#v…` at the end of the URL in `Packages/manifest.json` and save. Unity re-resolves on
focus.

If the URL has no tag, there is nothing to edit: delete the package's entry from
`Packages/packages-lock.json`, or remove and re-add it in Package Manager.

Then re-run `Tools > Mobile Quality > Setup` if the release notes ask for it. New settings
fields do not need it — Unity gives them their defaults on your existing prefab.

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
Step        0 of 0-8  (0 = best)
Applied     1.00 · shadows 1024 · 60fps
Ladder      perf 0 · floor 6
Proven      0
State       steady · Holding
Thermal     cool · None · headroom 0.42
```

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

- **Down** when frame times miss the target, or when the OS reports the device hot enough to
  act on — in three bands, so the most common thermal state on a phone stops the climb
  without starting a descent
- **Straight to 30 fps** when the device is not short of its target but on the wrong one —
  a stable 30 beats an unstable 44, and runs far cooler
- **Up** on frame times only when the device is cool **and** holding, and much more slowly
  than it goes down
- **Back up quickly** when the descent was thermal. Rungs given up to heat are a lease, not a
  verdict: they are tracked separately and handed back as soon as the device cools, without
  waiting for the frame times to justify anything

Shadows are all or nothing. Shortening shadow distance instead would leave near objects
shadowed and objects a metre behind them not, and that boundary reads as a bug rather than as
lower quality.

The best rung a device has **earned** is remembered — cool, inside budget, no thermal penalty —
so the next launch starts from a measurement instead of a guess. A hot session can never lower
it. And once a phone has proven it can hold 60 fps, the ladder stops at the last 60 fps rung
rather than descending into the 30 fps ones, unless the OS is actively throttling.

A device with no history yet gets a starting rung from its own GPU, memory and screen
resolution rather than from one fixed number for the whole market.

Thermal comes from `PowerManager` on Android and `NSProcessInfo.thermalState` on iOS, through
a bundled plugin that a post-process build step verifies reached the Xcode project. You never
open Xcode.

In the editor the ladder is pinned to rung 0, because a desktop has nothing useful to tell it.

---

## Documentation

The full reference — every threshold, why p75 and not p95, the two startup phases, the
overlay, the API, and the reasoning behind each decision — is in the package README:

**[📖 Full documentation](Packages/com.eliranshaya.mobileadjustgamequality/README.md)**

Every release is a tag: **[all versions](https://github.com/eliranshaya/Unity-MobileAdjustGameQuality/tags)**.
Tags are never moved — a new version always gets a new tag, because UPM caches a resolved git
package by tag and would keep serving the old copy.

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
