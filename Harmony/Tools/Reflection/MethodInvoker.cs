using System.Reflection;
using System.Reflection.Emit;
using MonoMod.Utils;

namespace HarmonyLib
{
    /// <summary>A delegate to invoke a method</summary>
    /// <param name="target">The instance</param>
    /// <param name="parameters">The method parameters</param>
    /// <returns>The method result</returns>
    ///
    public delegate object FastInvokeHandler(object target, params object[] parameters);

    /// <summary>A helper class to invoke method with delegates</summary>
    public class MethodInvoker
    {
        private static readonly MethodInvoker Instance = new MethodInvoker();
        private readonly bool directBoxValueAccess;

        /// <summary>Creates a MethodInvoker that can create a fast invocation handler</summary>
        /// <param name="directBoxValueAccess">
        /// <para>
        /// This option controls how value types passed by reference (e.g. ref int, out my_struct) are handled in the arguments array
        /// passed to the fast invocation handler.
        /// Since the arguments array is an object array, any value types contained within it are actually references to a boxed value object.
        /// Like any other object, there can be other references to such boxed value objects, other than the reference within the arguments array.
        /// <example>For example,
        /// <code>
        /// var val = 5;
        /// var box = (object)val;
        /// var arr = new object[] { box };
        /// handler(arr); // for a method with parameter signature: ref/out/in int
        /// </code>
        /// </example>
        /// </para>
        /// <para>
        /// If <c>directBoxValueAccess</c> is <c>true</c>, the boxed value object is accessed (and potentially updated) directly when the handler is called,
        /// such that all references to the boxed object reflect the potentially updated value.
        /// In the above example, if the method associated with the handler updates the passed (boxed) value to 10, both <c>box</c> and <c>arr[0]</c>
        /// now reflect the value 10. Note that the original <c>val</c> is not updated, since boxing always copies the value into the new boxed value object.
        /// </para>
        /// <para>
        /// If <c>directBoxValueAccess</c> is <c>false</c> (default), the boxed value object in the arguments array is replaced with a "reboxed" value object,
        /// such that potential updates to the value are reflected only in the arguments array.
        /// In the above example, if the method associated with the handler updates the passed (boxed) value to 10, only <c>arr[0]</c> now reflects the value 10.
        /// </para>
        /// </param>
        public MethodInvoker(bool directBoxValueAccess = false)
        {
            this.directBoxValueAccess = directBoxValueAccess;
        }

        /// <summary>Creates a fast invocation handler from a method and a module</summary>
        /// <param name="methodInfo">The method to invoke</param>
        /// <param name="module">The module context</param>
        /// <returns>The fast invocation handler</returns>
        public FastInvokeHandler Handler(MethodInfo methodInfo, Module module)
        {
            var result = methodInfo.GetFastDelegate(directBoxValueAccess);
            return (target, parameters) => result(target, parameters);
        }

        /// <summary>Creates a fast invocation handler from a method and a module</summary>
        /// <param name="methodInfo">The method to invoke</param>
        /// <param name="module">The module context</param>
        /// <returns>The fast invocation handler</returns>
        ///
        public static FastInvokeHandler GetHandler(DynamicMethod methodInfo, Module module)
        {
            return Instance.Handler(methodInfo, module);
        }

        /// <summary>Creates a fast invocation handler from a method and a module</summary>
        /// <param name="methodInfo">The method to invoke</param>
        /// <returns>The fast invocation handler</returns>
        ///
        public static FastInvokeHandler GetHandler(MethodInfo methodInfo)
        {
            return Instance.Handler(methodInfo, methodInfo.DeclaringType?.Module);
        }
    }
}