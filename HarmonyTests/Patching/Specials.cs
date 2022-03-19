using HarmonyLib;
using HarmonyLibTests.Assets;
using HarmonyLibTests.Assets.Methods;
using NUnit.Framework;
using System;
using System.Linq;

namespace HarmonyLibTests.Patching
{
	[TestFixture, NonParallelizable]
	public class Specials : TestLogger
	{
		/* TODO - patching HttpWebRequest.GetResponse does not work
		 *
		[Test]
		public void Test_HttpWebRequestGetResponse()
		{
			Assert.Ignore("Someone patching HttpWebRequest does not work");

			var t_WebRequest = typeof(HttpWebRequest);
			Assert.NotNull(t_WebRequest);
			var original = AccessTools.DeclaredMethod(t_WebRequest, nameof(HttpWebRequest.GetResponse));
			Assert.NotNull(original);

			var t_HttpWebRequestPatches = typeof(HttpWebRequestPatches);
			var prefix = t_HttpWebRequestPatches.GetMethod("Prefix");
			Assert.NotNull(prefix);
			var postfix = t_HttpWebRequestPatches.GetMethod("Postfix");
			Assert.NotNull(postfix);

			var instance = new Harmony("test");
			Assert.NotNull(instance);
			_ = instance.Patch(original, new HarmonyMethod(prefix, debug: true), new HarmonyMethod(postfix, debug: true));

			HttpWebRequestPatches.ResetTest();
			var request = WebRequest.Create("http://google.com");
			Assert.AreEqual(request.GetType(), t_WebRequest);
			var response = request.GetResponse();
			Assert.NotNull(response);
			Assert.True(HttpWebRequestPatches.prefixCalled, "Prefix not called");
			Assert.True(HttpWebRequestPatches.postfixCalled, "Postfix not called");
		}
		*/

		[Test]
		public void Test_Wrap_Patch()
		{
			SafeWrapPatch.called = false;
			var instance = new Harmony("special-case-wrap-patch");
			Assert.NotNull(instance);

			instance.PatchAll(typeof(SafeWrapPatch));

			var testObject = new DeadEndCode();
			Assert.NotNull(testObject);
			Assert.DoesNotThrow(() => testObject.Method4());
			Assert.True(SafeWrapPatch.called);
		}

		[Test]
		public void Test_Type_Patch_Regression()
		{
			var instance = new Harmony("special-case-type-patch");
			Assert.NotNull(instance);

			var testObject = new MultiAttributePatchCall();
			Assert.NotNull(testObject);
			MultiAttributePatchCall.returnValue = true;
			Assert.True(testObject.GetValue());
			MultiAttributePatchCall.returnValue = false;
			Assert.False(testObject.GetValue());

			instance.PatchAll(typeof(TestMultiAttributePatch));

			MultiAttributePatchCall.returnValue = true;
			Assert.True(testObject.GetValue());
			MultiAttributePatchCall.returnValue = false;
			Assert.True(testObject.GetValue());
		}

		[Test]
		public void Test_Multiple_Attributes()
		{
			MultiAttributePatch.callCount = 0;
			var instance = new Harmony("special-case-multi-attribute");
			Assert.NotNull(instance);
			instance.PatchAll(typeof(MultiAttributePatch));

			var testObject = new DeadEndCode();
			Assert.NotNull(testObject);
			Assert.DoesNotThrow(() => testObject.Method(), "Test method 1 wasn't patched");
			Assert.DoesNotThrow(() => testObject.Method2(), "Test method 2 wasn't patched");
			Assert.AreEqual(2, MultiAttributePatch.callCount);
		}

		[Test]
		public void Test_Patch_Exception_Propagate()
		{
			var instance = new Harmony("special-case-exception-throw");
			Assert.NotNull(instance);

			var processor = instance.CreateClassProcessor(typeof(ErrorReportTestPatch));
			Assert.NotNull(processor);
			Assert.Throws<HarmonyException>(() => processor.Patch());
		}

		[Test]
		public void Test_MultiTarget_Class1()
		{
			MultiAttributePatchClass1.callCount = 0;
			var instance = new Harmony("special-case-multi-target-1");
			Assert.NotNull(instance);

			var processor = instance.CreateClassProcessor(typeof(MultiAttributePatchClass1));
			Assert.NotNull(processor);
			processor.Patch();

			var testObject = new DeadEndCode();
			Assert.NotNull(testObject);
			Assert.DoesNotThrow(() => testObject.Method(), "Test method 1 wasn't patched");
			Assert.DoesNotThrow(() => testObject.Method2(), "Test method 2 wasn't patched");
			Assert.AreEqual(2, MultiAttributePatchClass1.callCount);
		}

		[Test]
		public void Test_MultiTarget_Class2()
		{
			MultiAttributePatchClass2.callCount = 0;
			var instance = new Harmony("special-case-multi-target-2");
			Assert.NotNull(instance);

			var processor = instance.CreateClassProcessor(typeof(MultiAttributePatchClass2));
			Assert.NotNull(processor);
			processor.Patch();

			var testObject = new DeadEndCode();
			Assert.NotNull(testObject);
			Assert.DoesNotThrow(() => testObject.Method(), "Test method 1 wasn't patched");
			Assert.DoesNotThrow(() => testObject.Method2(), "Test method 2 wasn't patched");
			Assert.AreEqual(2, MultiAttributePatchClass2.callCount);
		}

		[Test]
		public void Test_Multiple_Attributes_Partial()
		{
			var instance = new Harmony("special-case-multi-attribute-partial");
			Assert.NotNull(instance);
			instance.PatchAll(typeof(TypeTargetedPatch));

			var testObject = new DeadEndCode();
			Assert.NotNull(testObject);
			Assert.DoesNotThrow(() => testObject.Method3(), "Test method wasn't patched");
		}

		[Test]
		public void Test_Enumerator_Patch()
		{
			Assert.Null(EnumeratorPatch.patchTarget);
			Assert.AreEqual(0, EnumeratorPatch.runTimes);

			var instance = new Harmony("special-case-enumerator-movenext");
			Assert.NotNull(instance);
			instance.PatchAll(typeof(EnumeratorPatch));

			Assert.IsNotNull(EnumeratorPatch.patchTarget);
			Assert.AreEqual("MoveNext", EnumeratorPatch.patchTarget.Name);

			var testObject = new EnumeratorCode();
			Assert.AreEqual(new []{ 1, 2, 3, 4, 5 }, testObject.NumberEnumerator().ToArray());
			Assert.AreEqual(6, EnumeratorPatch.runTimes);
		}

		[Test]
		public void Test_Multiple_Attributes_Overload()
		{
			OverloadedCodePatch.callCount = 0;
			var instance = new Harmony("special-case-overload");
			Assert.NotNull(instance);
			instance.PatchAll(typeof(OverloadedCodePatch));

			var testObject1 = new OverloadedCode.Class1();
			var testObject2 = new OverloadedCode.Class2();
			Assert.NotNull(testObject1);
			Assert.NotNull(testObject2);
			Assert.DoesNotThrow(() => testObject1.Method(), "Method() wasn't patched");
			Assert.DoesNotThrow(() => testObject2.Method("test"), "Method(string) wasn't patched");
			Assert.AreEqual(2, OverloadedCodePatch.callCount);
		}

		[Test]
		public void Test_Patch_With_Module_Call()
		{
			var testMethod = ModuleLevelCall.CreateTestMethod();
			Assert.AreEqual(0, testMethod());

			var instance = new Harmony("special-case-module-call");
			Assert.NotNull(instance);
			var postfix = AccessTools.Method(typeof(ModuleLevelCall), nameof(ModuleLevelCall.Postfix));
			Assert.NotNull(postfix);

			instance.Patch(testMethod.Method, postfix: new HarmonyMethod(postfix));
			Assert.AreEqual(1, testMethod());
		}

		[Test]
		public void Test_Patch_ConcreteClass()
		{
			var instance = new Harmony("special-case-1");
			Assert.NotNull(instance, "instance");
			var processor = instance.CreateClassProcessor(typeof(ConcreteClass_Patch));
			Assert.NotNull(processor, "processor");

			var someStruct1 = new ConcreteClass().Method("test", new AnotherStruct());
			Assert.True(someStruct1.accepted, "someStruct1.accepted");

			TestTools.Log($"Patching ConcreteClass_Patch start");
			var replacements = processor.Patch();
			Assert.NotNull(replacements, "replacements");
			Assert.AreEqual(1, replacements.Count);
			TestTools.Log($"Patching ConcreteClass_Patch done");

			TestTools.Log($"Running patched ConcreteClass_Patch start");
			var someStruct2 = new ConcreteClass().Method("test", new AnotherStruct());
			Assert.True(someStruct2.accepted, "someStruct2.accepted");
			TestTools.Log($"Running patched ConcreteClass_Patch done");
		}

		[Test, NonParallelizable]
		public void Test_Patch_Returning_Structs([Values(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20)] int n, [Values("I", "S")] string type)
		{
			var name = $"{type}M{n:D2}";

			var patchClass = typeof(ReturningStructs_Patch);
			Assert.NotNull(patchClass);

			var prefix = SymbolExtensions.GetMethodInfo(() => ReturningStructs_Patch.Prefix(null));
			Assert.NotNull(prefix);

			var instance = new Harmony("returning-structs");
			Assert.NotNull(instance);

			var cls = AccessTools.TypeByName($"HarmonyLibTests.Assets.Methods.ReturningStructs_{type}{n:D2}");
			Assert.NotNull(cls, "type");
			var method = AccessTools.DeclaredMethod(cls, name);
			Assert.NotNull(method, "method");

			TestTools.Log($"Test_Returning_Structs: patching {name} start");
			try
			{
				var replacement = instance.Patch(method, new HarmonyMethod(prefix));
				Assert.NotNull(replacement, "replacement");
			}
			catch (Exception ex)
			{
				TestTools.Log($"Test_Returning_Structs: patching {name} exception: {ex}");
			}
			TestTools.Log($"Test_Returning_Structs: patching {name} done");

			var clsInstance = Activator.CreateInstance(cls);
			try
			{
				TestTools.Log($"Test_Returning_Structs: running patched {name}");

				var original = AccessTools.DeclaredMethod(cls, name);
				Assert.NotNull(original, $"{name}: original");
				var result = original.Invoke(type == "S" ? null : clsInstance, new object[] { "test" });
				Assert.NotNull(result, $"{name}: result");
				Assert.AreEqual($"St{n:D2}", result.GetType().Name);

				TestTools.Log($"Test_Returning_Structs: running patched {name} done");
			}
			catch (Exception ex)
			{
				TestTools.Log($"Test_Returning_Structs: running {name} exception: {ex}");
			}
		}

		[Test]
		public void Test_PatchException()
		{
			var patchClass = typeof(DeadEndCode_Patch1);
			Assert.NotNull(patchClass);

			var instance = new Harmony("test");
			Assert.NotNull(instance);
			var patcher = instance.CreateClassProcessor(patchClass);
			Assert.NotNull(patcher);

			Exception exception = null;
			try
			{
				Assert.NotNull(patcher.Patch());
			}
			catch (Exception ex)
			{
				exception = ex;
			}
			Assert.NotNull(exception);
		}

		[Test]
		public void Test_PatchExceptionWithCleanup1()
		{
			if (AccessTools.IsMonoRuntime is false)
				return; // Assert.Ignore("Only mono allows for detailed IL exceptions. Test ignored.");

			var patchClass = typeof(DeadEndCode_Patch2);
			Assert.NotNull(patchClass);

			DeadEndCode_Patch2.original = null;
			DeadEndCode_Patch2.exception = null;

			var instance = new Harmony("test");
			Assert.NotNull(instance, "Harmony instance");
			var patcher = instance.CreateClassProcessor(patchClass);
			Assert.NotNull(patcher, "Patch processor");
			try
			{
				_ = patcher.Patch();
				Assert.Fail("Patch should throw exception");
			}
			catch (Exception)
			{
			}

			Assert.AreSame(typeof(DeadEndCode).GetMethod("Method"), DeadEndCode_Patch2.original, "Patch should save original method");
			Assert.NotNull(DeadEndCode_Patch2.exception, "Patch should save exception");

			var harmonyException = DeadEndCode_Patch2.exception as HarmonyException;
			Assert.NotNull(harmonyException, $"Exception should be a HarmonyException (is: {DeadEndCode_Patch2.exception.GetType()}");

			var instructions = harmonyException.GetInstructions();
			Assert.NotNull(instructions, "HarmonyException should have instructions");
			Assert.AreEqual(12, instructions.Count);

			var errorIndex = harmonyException.GetErrorIndex();
			Assert.AreEqual(10, errorIndex);

			var errorOffset = harmonyException.GetErrorOffset();
			Assert.AreEqual(50, errorOffset);
		}

		[Test]
		public void Test_PatchExceptionWithCleanup2()
		{
			if (AccessTools.IsMonoRuntime is false)
				return; // Assert.Ignore("Only mono allows for detailed IL exceptions. Test ignored.");

			var patchClass = typeof(DeadEndCode_Patch3);
			Assert.NotNull(patchClass);

			var instance = new Harmony("test");
			Assert.NotNull(instance, "Harmony instance");
			var patcher = instance.CreateClassProcessor(patchClass);
			Assert.NotNull(patcher, "Patch processor");
			try
			{
				_ = patcher.Patch();
			}
			catch (HarmonyException ex)
			{
				Assert.NotNull(ex.InnerException);
				Assert.IsInstanceOf(typeof(ArgumentException), ex.InnerException);
				Assert.AreEqual("Test", ex.InnerException.Message);
				return;
			}
			Assert.Fail("Patch should throw HarmonyException");
		}

		[Test]
		public void Test_PatchExceptionWithCleanup3()
		{
			if (AccessTools.IsMonoRuntime is false)
				return; // Assert.Ignore("Only mono allows for detailed IL exceptions. Test ignored.");

			var patchClass = typeof(DeadEndCode_Patch4);
			Assert.NotNull(patchClass);

			var instance = new Harmony("test");
			Assert.NotNull(instance, "Harmony instance");
			var patcher = instance.CreateClassProcessor(patchClass);
			Assert.NotNull(patcher, "Patch processor");
			_ = patcher.Patch();
		}

		[Test]
		public void Test_PatchExternalMethod()
		{
			var patchClass = typeof(ExternalMethod_Patch);
			Assert.NotNull(patchClass);

			var instance = new Harmony("test");
			Assert.NotNull(instance, "Harmony instance");
			var patcher = instance.CreateClassProcessor(patchClass);
			Assert.NotNull(patcher, "Patch processor");
			_ = patcher.Patch();
		}

		[Test]
		public void Test_PatchEventHandler()
		{
			Console.WriteLine($"### EventHandlerTestClass TEST");

			var patchClass = typeof(EventHandlerTestClass_Patch);
			Assert.NotNull(patchClass);

			var instance = new Harmony("test");
			Assert.NotNull(instance, "Harmony instance");
			var patcher = instance.CreateClassProcessor(patchClass);
			Assert.NotNull(patcher, "Patch processor");
			var patched = patcher.Patch();
			Assert.AreEqual(1, patched.Count);
			Assert.NotNull(patched[0]);

			Console.WriteLine($"### EventHandlerTestClass BEFORE");
			new EventHandlerTestClass().Run();
			Console.WriteLine($"### EventHandlerTestClass AFTER");
		}

		[Test]
		public void Test_PatchMarshalledClass()
		{
			Console.WriteLine($"### MarshalledTestClass TEST");

			var patchClass = typeof(MarshalledTestClass_Patch);
			Assert.NotNull(patchClass);

			var instance = new Harmony("test");
			Assert.NotNull(instance, "Harmony instance");
			var patcher = instance.CreateClassProcessor(patchClass);
			Assert.NotNull(patcher, "Patch processor");
			var patched = patcher.Patch();
			Assert.AreEqual(1, patched.Count);
			Assert.NotNull(patched[0]);

			Console.WriteLine($"### MarshalledTestClass BEFORE");
			new MarshalledTestClass().Run();
			Console.WriteLine($"### MarshalledTestClass AFTER");
		}

		[Test]
		public void Test_MarshalledWithEventHandler1()
		{
			Console.WriteLine($"### MarshalledWithEventHandlerTest1 TEST");

			var patchClass = typeof(MarshalledWithEventHandlerTest1Class_Patch);
			Assert.NotNull(patchClass);

			var instance = new Harmony("test");
			Assert.NotNull(instance, "Harmony instance");
			var patcher = instance.CreateClassProcessor(patchClass);
			Assert.NotNull(patcher, "Patch processor");
			var patched = patcher.Patch();
			Assert.AreEqual(1, patched.Count);
			Assert.NotNull(patched[0]);

			Console.WriteLine($"### MarshalledWithEventHandlerTest1 BEFORE");
			new MarshalledWithEventHandlerTest1Class().Run();
			Console.WriteLine($"### MarshalledWithEventHandlerTest1 AFTER");
		}

		[Test]
#if !NETCOREAPP3_0 || NETCOREAPP3_1 || NET5_0
		[Ignore("Crashes on x86:<=net48")]
#endif
		public void Test_MarshalledWithEventHandler2()
		{
			Console.WriteLine($"### MarshalledWithEventHandlerTest2 TEST");

			var patchClass = typeof(MarshalledWithEventHandlerTest2Class_Patch);
			Assert.NotNull(patchClass);

			var instance = new Harmony("test");
			Assert.NotNull(instance, "Harmony instance");
			var patcher = instance.CreateClassProcessor(patchClass);
			Assert.NotNull(patcher, "Patch processor");
			var patched = patcher.Patch();
			Assert.AreEqual(1, patched.Count);
			Assert.NotNull(patched[0]);

			Console.WriteLine($"### MarshalledWithEventHandlerTest2 BEFORE");
			new MarshalledWithEventHandlerTest2Class().Run();
			Console.WriteLine($"### MarshalledWithEventHandlerTest2 AFTER");
		}
	}
}
