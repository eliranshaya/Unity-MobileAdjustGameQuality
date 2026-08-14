// MobileQualityThermalNative.mm
//
// Native iOS side of com.eliranshaya.mobileadjustgamequality.
//
// Unity does not surface NSProcessInfo.thermalState to managed code, and there is no other
// way to ask iOS how hot it is. These few lines are the whole gap.
//
// The exported symbol is prefixed _MAQ_ on purpose. A generic name like _GetThermalState
// collides at link time with any other package bridging the same API, and a duplicate-symbol
// error in Xcode is exactly the failure this package should not be causing in somebody
// else's project.
//
// Foundation is always linked into UnityFramework, so this needs no framework added to the
// Xcode project - MobileQualityThermalIOSPostProcess only verifies the file reached it.

#import <Foundation/Foundation.h>

// Returns NSProcessInfoThermalState:
//   0 nominal   - no thermal pressure
//   1 fair      - slightly elevated; the system may start taking action
//   2 serious   - performance is being reduced; reduce work now
//   3 critical  - the device needs to cool down immediately
//
// Returns -1 when the API is unavailable, which ThermalSensor reads as Unknown. thermalState
// is iOS 11+, below every deployment target URP 14 supports, but the check costs nothing and
// keeps the contract explicit.
extern "C" int MAQ_GetThermalState()
{
    if (@available(iOS 11.0, tvOS 11.0, *))
    {
        return (int)[[NSProcessInfo processInfo] thermalState];
    }

    return -1;
}
