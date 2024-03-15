using MonoMod.Cil;
using MonoMod.Core.Platforms;
using MonoMod.Utils;
using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace HarmonyLib.Public.Patching;

/// <summary>
///    A method patcher that uses <see cref="MonoMod.Core.Platforms.PlatformTriple.CreateNativeDetour" /> to patch
///    internal calls, methods marked with <see cref="DllImportAttribute" /> and any other managed method that
///    CLR managed-to-native trampolines for and which has no IL body defined.
/// </summary>
public class NativeDetourMethodPatcher : MethodPatcher
{
	private static readonly ConstructorInfo NotImplementedExceptionCtor =
		typeof(NotImplementedException).GetConstructor([]);

	private SimpleNativeDetour _detourOriginal;
	private SimpleNativeDetour _detourAltEntry;

	private MethodBase _replacement;

	private IDisposable _originalPin;
	private IDisposable _realReplacementPin;
	private IDisposable _altHandle;

	private readonly Type _returnType;
	private readonly Type[] _parameterTypes;

	private readonly MethodInfo _altEntryMethod;
	private readonly IDisposable _altEntryPin;

	/// <inheritdoc/>
	public NativeDetourMethodPatcher(MethodBase original) : base(original)
	{
		var originalParameters = Original.GetParameters();

		var offset = Original.IsStatic ? 0 : 1;

		_parameterTypes = new Type[originalParameters.Length + offset];

		if (!Original.IsStatic)
			_parameterTypes[0] = Original.GetThisParamType();

		for (var i = 0; i < originalParameters.Length; i++)
			_parameterTypes[i + offset] = originalParameters[i].ParameterType;

		_returnType = Original is MethodInfo { ReturnType: var ret } ? ret : typeof(void);

		var altEntryProxyMethod = GenerateAltEntryProxyStub(Original, _returnType, _parameterTypes);
		_altEntryMethod = altEntryProxyMethod;
		_altEntryPin = PlatformTriple.Current.PinMethodIfNeeded(altEntryProxyMethod);
		PlatformTriple.Current.Compile(altEntryProxyMethod);
	}

	private static MethodInfo GenerateAltEntryProxyStub(MethodBase original, Type returnType, Type[] parameterTypes)
	{
		var dmd = new DynamicMethodDefinition(
			$"NativeDetourAltEntry<{original.DeclaringType?.FullName}:{original.Name}>",
			returnType,
			parameterTypes
		);

		var il = new ILContext(dmd.Definition);
		var c = new ILCursor(il);

		c.EmitNewobj(NotImplementedExceptionCtor);
		c.EmitThrow();

		var method = dmd.Generate();
		return method;
	}

	/// <inheritdoc/>
	public override DynamicMethodDefinition PrepareOriginal()
	{
		var dmd = new DynamicMethodDefinition(
			$"NativeDetourProxy<{Original.DeclaringType?.FullName}:{Original.Name}>",
			_returnType,
			_parameterTypes
		);

		var il = new ILContext(dmd.Definition);
		var c = new ILCursor(il);

		for (var i = 0; i < _parameterTypes.Length; i++)
			c.EmitLdarg(i);
		c.EmitCall(_altEntryMethod);
		c.EmitRet();

		return dmd;
	}

	/// <inheritdoc/>
	public override MethodBase DetourTo(MethodBase replacement)
	{
		var triple = PlatformTriple.Current;

		replacement = triple.GetIdentifiable(replacement);
		if (replacement == _replacement)
			return replacement;

		lock(_altEntryMethod)
		{
			if (replacement == _replacement)
				return replacement;

			_replacement = replacement;

			var realReplacement = triple.GetRealDetourTarget(Original, replacement);
			_realReplacementPin?.Dispose();
			_realReplacementPin = triple.PinMethodIfNeeded(realReplacement);

			var realReplacementPtr = triple.Runtime.GetMethodHandle(realReplacement).GetFunctionPointer();

			if (_detourOriginal is { } detourOriginal)
			{
				detourOriginal.ChangeTarget(realReplacementPtr);
			}
			else
			{
				_originalPin = triple.PinMethodIfNeeded(Original);

				(_detourOriginal, var altEntry, _altHandle) = triple.CreateNativeDetour(
					triple.GetNativeMethodBody(Original),
					realReplacementPtr
				);
				_detourAltEntry = triple.CreateSimpleDetour(
					triple.GetNativeMethodBody(_altEntryMethod),
					altEntry
				);
			}

			return replacement;
		}
	}

	/// <inheritdoc/>
	public override DynamicMethodDefinition CopyOriginal()
	{
		if (Original is not MethodInfo original)
			return null;

		var dmd = new DynamicMethodDefinition(
			$"OrigWrapper<{original.DeclaringType?.FullName}:{original.Name}>",
			_returnType,
			_parameterTypes
		);

		var il = new ILContext(dmd.Definition);
		var c = new ILCursor(il);

		for (var i = 0; i < _parameterTypes.Length; i++)
			c.EmitLdarg(i);
		c.EmitCall(_altEntryMethod);
		c.EmitRet();

		return dmd;
	}

	/// <summary>
	/// A handler for <see cref="PatchManager.ResolvePatcher"/> that checks if a method doesn't have a body
	/// (e.g. it's icall or marked with <see cref="DllImportAttribute"/>) and thus can be patched with
	/// <see cref="MonoMod.Core.Platforms.PlatformTriple.CreateNativeDetour"/>.
	/// </summary>
	/// <param name="sender">Not used</param>
	/// <param name="args">Patch resolver arguments</param>
	///
	public static void TryResolve(object sender, PatchManager.PatcherResolverEventArgs args)
	{
		// original cannot be executed without alt entry
		if(!PlatformTriple.Current.SupportedFeatures.Has(ArchitectureFeature.CreateAltEntryPoint))
			return;

		if (args.Original.GetMethodBody() == null)
			args.MethodPatcher = new NativeDetourMethodPatcher(args.Original);
	}
}
