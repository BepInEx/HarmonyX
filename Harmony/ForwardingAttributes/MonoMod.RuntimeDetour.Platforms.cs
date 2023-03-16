#if NETFRAMEWORK
using System.Runtime.CompilerServices;

[assembly: TypeForwardedTo(typeof(MonoMod.RuntimeDetour.Platforms.DetourRuntimeILPlatform))]
[assembly: TypeForwardedTo(typeof(MonoMod.RuntimeDetour.Platforms.DetourRuntimeNETCore30Platform))]
[assembly: TypeForwardedTo(typeof(MonoMod.RuntimeDetour.Platforms.DetourRuntimeNETCorePlatform))]
[assembly: TypeForwardedTo(typeof(MonoMod.RuntimeDetour.Platforms.DetourRuntimeNETPlatform))]
#endif
