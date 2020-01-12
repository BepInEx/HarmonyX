<p align="center">
	<img src="./logo.png" alt="HarmonyX Logo" height="128" />
</p>

![Nuget](https://img.shields.io/nuget/dt/HarmonyX?label=NuGet&style=for-the-badge)

***

<p align="center">
	A library for patching, replacing and decorating .NET and Mono methods during runtime. Now powered by MonoMod!
</p>

***

> **NOTE**
> The library is still WIP, see [current roadmap](https://github.com/BepInEx/HarmonyX/issues/2) for more details.

## About

**HarmonyX** is a fork of [Harmony 2](https://github.com/pardeike/Harmony) that specializes on support for games and game modding frameworks.

Important aspects of HarmonyX include:

* Better runtime support: .NET Standard 2, .NET Core 2, Mono shipped with some Unity games
* Better platform and OS support: x86, x64, ARM
* Active developer support
* Patching feature parity with Harmony
* New patch types with power of MonoMod: patches for native methods, IL manipulators (WIP, see above)
* Fixes, changes and optimizations for game modding

HarmonyX is powered by [MonoMod](https://github.com/MonoMod) and its runtime patching tools.

## Documentation

At the moment the basic documentation is available at [original Harmony docs](http://pardeike.github.io/Harmony).