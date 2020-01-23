using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib.Tools;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;

namespace HarmonyLib.Internal.Patching
{
    internal abstract class MethodPatcher
    {
        protected MethodPatcher(MethodBase original)
        {
            Original = original;
        }

        public MethodBase Original { get; }

        public abstract void Apply();

        public static MethodPatcher Create(MethodBase original)
        {
            if (original.GetMethodBody() == null)
                return new NativeMethodPatcher(original);
            return new ILMethodPatcher(original);
        }
    }

    internal class ILMethodPatcher : MethodPatcher
    {
        private static readonly MethodInfo IsAppliedSetter =
            AccessTools.PropertySetter(typeof(ILHook), nameof(ILHook.IsApplied));

        private static readonly Action<ILHook, bool> SetIsApplied =
            (Action<ILHook, bool>) IsAppliedSetter.CreateDelegate<Action<ILHook, bool>>();

        private ILHook ilHook;

        public ILMethodPatcher(MethodBase original) : base(original)
        {
        }

        public override void Apply()
        {
            if (ilHook == null)
                ilHook = new ILHook(Original, Manipulator, new ILHookConfig
                {
                    ManualApply = true
                });

            // Reset IsApplied to force MonoMod to reapply the ILHook without removing it
            SetIsApplied(ilHook, false);
            ilHook.Apply();
        }

        private void Manipulator(ILContext ctx)
        {
            HarmonyManipulator.Manipulate(Original, Original.GetPatchInfo(), ctx);
        }
    }

    internal class NativeMethodPatcher : MethodPatcher
    {
        private static readonly bool IsRunningOnDotNetCore = Type.GetType("System.Runtime.Loader.AssemblyLoadContext") != null;

        private static readonly Dictionary<int, Delegate> TrampolineCache = new Dictionary<int, Delegate>();

        private static readonly MethodInfo GetTrampolineMethod =
            AccessTools.Method(typeof(NativeMethodPatcher), nameof(GetTrampoline));

        private string[] _argTypeNames;
        private Type[] _argTypes;

        private DynamicMethodDefinition _dmd;
        private MethodInfo _invokeTrampolineMethod;
        private NativeDetour _nativeDetour;
        private Type _returnType;
        private Type _trampolineDelegateType;

        public NativeMethodPatcher(MethodBase original) : base(original)
        {
            Init();
        }

        public override void Apply()
        {
            // The process to patch native methods is as follows:
            // 1. Create a managed proxy method that calls NativeDetour's trampoline (we need to cache it
            //    because we don't know the trampoline method when generating the DMD).
            // 2. Pass the proxy to the normal Harmony manipulator to apply prefixes, postfixes, transpilers, etc.
            // 3. NativeDetour the method to the managed proxy
            // 4. Cache the NativeDetour's trampoline (technically we wouldn't need to, this is just a workaround
            //    for MonoMod's API.

            if (IsRunningOnDotNetCore)
                Logger.Log(Logger.LogChannel.Warn, () => $"Patch target {Original.GetID()} is marked as extern. " +
                                                         "Extern methods may not be patched because of inlining behaviour of coreclr (refer to https://github.com/dotnet/coreclr/pull/8263)." +
                                                         "If you need to patch externs, consider using pure NativeDetour instead.");


            var prevDmd = _dmd;
            _nativeDetour?.Dispose();

            _dmd = GenerateManagedOriginal();
            var ctx = new ILContext(_dmd.Definition);

            HarmonyManipulator.Manipulate(Original, Original.GetPatchInfo(), ctx);

            var target = _dmd.Generate();

            _nativeDetour = new NativeDetour(Original, target, new NativeDetourConfig
            {
                ManualApply = true
            });

            lock (TrampolineCache)
            {
                if (prevDmd != null)
                    TrampolineCache.Remove(prevDmd.GetHashCode());

                TrampolineCache[_dmd.GetHashCode()] = CreateDelegate(_trampolineDelegateType, _nativeDetour.GenerateTrampoline(_invokeTrampolineMethod));
            }

            _nativeDetour.Apply();
        }

        private Delegate CreateDelegate(Type delegateType, MethodBase mb)
        {
            if (mb is DynamicMethod dm)
                return dm.CreateDelegate(delegateType);

            return Delegate.CreateDelegate(delegateType, mb as MethodInfo ?? throw new InvalidCastException($"Unexpected method type: {mb.GetType()}"));
        }

        private static Delegate GetTrampoline(int hash)
        {
            lock (TrampolineCache)
            {
                return TrampolineCache[hash];
            }
        }

        private void Init()
        {
            var orig = Original;

            var args = orig.GetParameters();
            var offs = orig.IsStatic ? 0 : 1;
            _argTypes = new Type[args.Length + offs];
            _argTypeNames = new string[args.Length + offs];
            _returnType = (orig as MethodInfo)?.ReturnType;

            if (!orig.IsStatic)
            {
                _argTypes[0] = orig.GetThisParamType();
                _argTypeNames[0] = "this";
            }

            for (var i = 0; i < args.Length; i++)
            {
                _argTypes[i + offs] = args[i].ParameterType;
                _argTypeNames[i + offs] = args[i].Name;
            }

            _trampolineDelegateType = DelegateTypeFactory.instance.CreateDelegateType(_returnType, _argTypes);
            _invokeTrampolineMethod = AccessTools.Method(_trampolineDelegateType, "Invoke");
        }

        private DynamicMethodDefinition GenerateManagedOriginal()
        {
            // Here we generate the "managed" version of the native method
            // It simply calls the trampoline generated by MonoMod
            // As a result, we can pass the managed original to HarmonyManipulator like a normal method

            var orig = Original;

            var dmd = new DynamicMethodDefinition($"NativeDetour<{orig.GetID(simple: true)}>", _returnType, _argTypes);
            dmd.Definition.Name += $"?{dmd.GetHashCode()}";

            var def = dmd.Definition;
            for (var i = 0; i < _argTypeNames.Length; i++)
                def.Parameters[i].Name = _argTypeNames[i];

            var il = dmd.GetILGenerator();

            il.Emit(OpCodes.Ldc_I4, dmd.GetHashCode());
            il.Emit(OpCodes.Call, GetTrampolineMethod);
            for (var i = 0; i < _argTypes.Length; i++)
                il.Emit(OpCodes.Ldarg, i);
            il.Emit(OpCodes.Call, _invokeTrampolineMethod);
            il.Emit(OpCodes.Ret);

            return dmd;
        }
    }
}