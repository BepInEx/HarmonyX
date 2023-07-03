using Mono.Cecil;
using MonoMod.Cil;
using MonoMod.Core.Platforms;
using MonoMod.Utils;
using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace HarmonyLib.Public.Patching;

/// <inheritdoc/>
public class NativeMethodPatcher : MethodPatcher
{
	private PlatformTriple.NativeDetour? _hook;
	private Delegate _replacementDelegate;

	private readonly Type _returnType;
	private readonly Type[] _parameterTypes;
	private readonly Type _altEntryDelegateType;
	private readonly DataScope<DynamicReferenceCell> _altEntryDelegateStore;
	private readonly MethodInfo _altEntryDelegateInvoke;

	/// <inheritdoc/>
	public NativeMethodPatcher(MethodBase original) : base(original)
	{
		var originalParameters = Original.GetParameters();

		var offset = Original.IsStatic ? 0 : 1;

		_parameterTypes = new Type[originalParameters.Length + offset];

		if (!Original.IsStatic)
		{
			_parameterTypes[0] = Original.GetThisParamType();
		}

		for (int i = 0; i < originalParameters.Length; i++)
		{
			_parameterTypes[i + offset] = originalParameters[i].ParameterType;
		}

		_returnType = Original is MethodInfo { ReturnType: var ret } ? ret : typeof(void);

		_altEntryDelegateType = DelegateTypeFactory.instance.CreateDelegateType(_returnType, _parameterTypes);
		_altEntryDelegateInvoke = _altEntryDelegateType.GetMethod("Invoke");

		_altEntryDelegateStore = DynamicReferenceManager.AllocReference(default(Delegate), out _);
	}

	/// <inheritdoc/>
	public override DynamicMethodDefinition PrepareOriginal()
	{
		var dmd = new DynamicMethodDefinition(
			$"NativeHookProxy<{Original.DeclaringType.FullName}:{Original.Name}>",
			_returnType,
			_parameterTypes
		);

		var il = new ILContext(dmd.Definition);

		var c = new ILCursor(il);

		c.EmitLoadReference(_altEntryDelegateStore.Data);
		for (int i = 0; i < _parameterTypes.Length; i++)
		{
			c.EmitLdarg(i);
		}
		c.EmitCallvirt(_altEntryDelegateInvoke);
		c.EmitRet();

		return dmd;
	}

	/// <inheritdoc/>
	public override MethodBase DetourTo(MethodBase replacement)
	{
		_replacementDelegate = replacement.CreateDelegate(_altEntryDelegateType);
		var replacementDelegatePtr = Marshal.GetFunctionPointerForDelegate(_replacementDelegate);
		if (_hook is { } hook)
		{
			hook.Simple.ChangeTarget(replacementDelegatePtr);
		}
		else
		{
			_hook = PlatformTriple.Current.CreateNativeDetour(PlatformTriple.Current.GetNativeMethodBody(Original), replacementDelegatePtr);
			DynamicReferenceManager.SetValue(_altEntryDelegateStore.Data, Marshal.GetDelegateForFunctionPointer(_hook.Value.AltEntry, _altEntryDelegateType));
		}
		return replacement;
	}

	/// <inheritdoc/>
	public override DynamicMethodDefinition CopyOriginal()
	{
		return null;
	}

	public static void TryResolve(object _, PatchManager.PatcherResolverEventArgs args)
	{
		if (args.Original.GetMethodBody() == null)
			args.MethodPatcher = new NativeMethodPatcher(args.Original);
	}
}
