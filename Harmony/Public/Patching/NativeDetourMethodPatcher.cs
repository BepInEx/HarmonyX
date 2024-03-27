using MonoMod.Cil;
using MonoMod.Core.Platforms;
using MonoMod.Utils;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace HarmonyLib.Public.Patching;

/// <summary>
///    A method patcher that uses <see cref="MonoMod.Core.Platforms.PlatformTriple.CreateNativeDetour" /> to patch
///    internal calls, methods marked with <see cref="DllImportAttribute" /> and any other managed method that
///    CLR managed-to-native trampolines for and which has no IL body defined.
/// </summary>
public class NativeDetourMethodPatcher : MethodPatcher
{
	private static readonly ConstructorInfo InvalidOperationExceptionCtor =
		typeof(InvalidOperationException).GetConstructor([typeof(string)]);

	private SimpleNativeDetour _detourOriginal;
	private SimpleNativeDetour _detourAltEntry;

	private IntPtr? _originalEntry;

	private MethodBase _replacement;

	private IDisposable _originalPin;
	private IDisposable _realReplacementPin;
	private IDisposable _altHandle;

	private readonly MethodSignature _staticSignature;

	private readonly MethodInfo _altEntryMethod;
	private readonly IDisposable _altEntryPin;

	/// <inheritdoc/>
	public NativeDetourMethodPatcher(MethodBase original) : base(original)
	{
		var staticSignature = new MethodSignature(Original, true);
		_staticSignature = staticSignature;

		var altEntryProxyMethod = GenerateAltEntryProxyStub(Original, staticSignature);
		_altEntryMethod = altEntryProxyMethod;
		_altEntryPin = PlatformTriple.Current.PinMethodIfNeeded(altEntryProxyMethod);
		PlatformTriple.Current.Compile(altEntryProxyMethod);
	}

	private static MethodInfo GenerateAltEntryProxyStub(MethodBase original, MethodSignature signature)
	{
		using var dmd = signature.CreateDmd(
			$"NativeDetourAltEntry<{original.DeclaringType?.FullName}:{original.Name}>"
			);

		var il = new ILContext(dmd.Definition);
		var c = new ILCursor(il);

		c.EmitLdstr($"{dmd.Definition.Name} should've been detoured!");
		c.EmitNewobj(InvalidOperationExceptionCtor);
		c.EmitThrow();

		return dmd.Generate();
	}

	/// <inheritdoc/>
	public override DynamicMethodDefinition PrepareOriginal() =>
		GenerateAltEntryProxyCaller($"NativeDetourProxy<{Original.DeclaringType?.FullName}:{Original.Name}>");

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

			// SimpleNativeDetour.ChangeTarget with alt entry is not safe currently, refer to docs
			_detourOriginal?.Dispose();
			_altHandle?.Dispose();

			_originalPin ??= triple.PinMethodIfNeeded(Original);
			// due to _originalPin, it shouldn't change
			var originalEntry = _originalEntry ??= triple.GetNativeMethodBody(Original);

			(_detourOriginal, var altEntry, _altHandle) = triple.CreateNativeDetour(
				originalEntry,
				realReplacementPtr
			);

			if (_detourAltEntry is { } detourAltEntry)
				detourAltEntry.ChangeTarget(altEntry);
			else
				_detourAltEntry = triple.CreateSimpleDetour(
					triple.GetNativeMethodBody(_altEntryMethod),
					altEntry
				);


			PatchManager.AddReplacementOriginal(Original, replacement);

			return replacement;
		}
	}

	/// <inheritdoc/>
	public override DynamicMethodDefinition CopyOriginal() =>
		GenerateAltEntryProxyCaller($"OrigWrapper<{Original.DeclaringType?.FullName}:{Original.Name}>");

	private DynamicMethodDefinition GenerateAltEntryProxyCaller(string name)
	{
		var dmd = _staticSignature.CreateDmd(name);

		var il = new ILContext(dmd.Definition);
		var c = new ILCursor(il);

		for (var i = 0; i < _staticSignature.ParameterCount; i++)
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
