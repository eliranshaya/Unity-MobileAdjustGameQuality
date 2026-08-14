# Unity-MobileAdjustGameQuality

One button generates a mobile-tuned URP setup. One prefab keeps the frame budget on the
device by moving along a quality ladder, driven by frame times and by how hot the phone is.

This repository is a full Unity project used to develop and test the package. The package
itself is `Packages/com.eliranshaya.mobileadjustgamequality/`.

---

## Install

**Window → Package Manager → + → Add package from git URL:**

```
https://github.com/eliranshaya/Unity-MobileAdjustGameQuality.git?path=/Packages/com.eliranshaya.mobileadjustgamequality
```

Unity 2022.3 LTS or newer, URP 14+ (installed automatically), URP active in
**Project Settings → Graphics**.

---

## Use

1. `Tools > Mobile Quality > Setup`
2. Press **Setup**
3. Drag `Assets/MobileAdjustGameQuality/Generated/MobileQualityManager.prefab` into your
   first scene

Setup creates one URP asset and one renderer, **replaces every quality level** with a single
`MAQ Mobile`, applies the recommended mobile Player Settings, and builds the prefab.

---

## What it does at runtime

Quality is one number: which rung of an eight-rung ladder is applied. Render scale, shadows
on/off, shadowmap resolution and the frame cap all move together.

- **Down** when frame times miss the target, or when the OS reports the device warm
- **Straight to 30 fps** when frame times are so far over that the device is on the wrong
  target entirely — a stable 30 beats an unstable 44, and runs much cooler
- **Up** when the device is cool *and* holding its target

Shadows are all or nothing: shortening shadow distance would leave near objects shadowed and
objects behind them not, which reads as a bug rather than as lower quality.

The rung a device settles on is remembered, so the next launch starts there instead of
rediscovering it.

Thermal comes from `PowerManager` on Android and `NSProcessInfo.thermalState` on iOS, through
a bundled plugin that a post-process build step verifies reached the Xcode project.

---

Full documentation:
[`Packages/com.eliranshaya.mobileadjustgamequality/README.md`](Packages/com.eliranshaya.mobileadjustgamequality/README.md)

## License

MIT, © Eliran Shaya. See [LICENSE](LICENSE).
