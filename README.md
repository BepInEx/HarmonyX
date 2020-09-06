<p align="center">
	<img src="./logo.png" alt="HarmonyX Logo" height="128" />
</p>

<p align="center">
	<a href="https://www.nuget.org/packages/HarmonyX/">
		<img src="https://img.shields.io/nuget/dt/HarmonyX?label=NuGet&style=for-the-badge" alt="NuGet" />
	</a>
</p>

***

<p align="center">
	A library for patching, replacing and decorating .NET and Mono methods during runtime. Now powered by MonoMod!
</p>

***

> **NOTE**
> The library is still WIP, see [current roadmap](https://github.com/BepInEx/HarmonyX/issues/2) for more details.  
> Note that this is a **fork** of Harmony 2 and not original version!

## About

**HarmonyX** is a fork of [Harmony 2](https://github.com/pardeike/Harmony) that specializes on support for games and game modding frameworks.

HarmonyX is being developed primarily for use in game frameworks alongside MonoMod. The main target usage of HarmonyX is [BepInEx](https://github.com/BepInEx/BepInEx) and Unity.

Important aspects of HarmonyX include:

* Better runtime support: .NET Standard 2, .NET Core 2, Mono shipped with some Unity games
* Better platform and OS support: x86, x64, ARM
* Active developer support
* Patching feature parity with Harmony
* New patch types with power of MonoMod: [support for native method patching](https://github.com/BepInEx/HarmonyX/wiki/Valid-patch-targets#native-methods-marked-extern)
* Fixes, changes and optimizations for game modding

HarmonyX is powered by [MonoMod](https://github.com/MonoMod) and its runtime patching tools.

## Documentation

At the moment the basic documentation is available at [original Harmony docs](http://pardeike.github.io/Harmony).
