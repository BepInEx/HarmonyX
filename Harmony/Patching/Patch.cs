using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using MonoMod.Utils;

namespace HarmonyLib
{
    /// <summary>Serializable patch information</summary>
    public class PatchInfo
    {
        /// <summary>The prefixes</summary>
        public Patch[] prefixes;

        /// <summary>The postfixes</summary>
        public Patch[] postfixes;

        /// <summary>The transpilers</summary>
        public Patch[] transpilers;

        /// <summary>The finalizers</summary>
        public Patch[] finalizers;

        /// <summary>Default constructor</summary>
        public PatchInfo()
        {
            prefixes = new Patch[0];
            postfixes = new Patch[0];
            transpilers = new Patch[0];
            finalizers = new Patch[0];
        }

        private void AddPatch(ref Patch[] list, string owner, HarmonyMethod info)
        {
            if (info?.method == null) return;

            var priority = info.priority == -1 ? Priority.Normal : info.priority;
            var before = info.before ?? new string[0];
            var after = info.after ?? new string[0];

            AddPatch(ref list, info.method, owner, priority, before, after);
        }

        private void AddPatch(ref Patch[] list, MethodInfo patch, string owner, int priority, string[] before,
                              string[] after)
        {
            var l = list.ToList();
            l.Add(new Patch(patch, prefixes.Count() + 1, owner, priority, before, after));
            list = l.ToArray();
        }

        private void RemovePatch(ref Patch[] list, string owner)
        {
            if (owner == "*")
            {
                list = new Patch[0];
                return;
            }

            list = list.Where(patch => patch.owner != owner).ToArray();
        }

        /// <summary>Adds a prefix</summary>
        /// <param name="patch">The patch</param>
        /// <param name="owner">The owner (Harmony ID)</param>
        /// <param name="priority">The priority</param>
        /// <param name="before">The before parameter</param>
        /// <param name="after">The after parameter</param>
        ///
        public void AddPrefix(MethodInfo patch, string owner, int priority, string[] before, string[] after)
        {
            AddPatch(ref prefixes, patch, owner, priority, before, after);
        }

        /// <summary>
        /// Adds a prefix method.
        /// </summary>
        /// <param name="owner">ID of the owner instance</param>
        /// <param name="info">Method to add as prefix</param>
        public void AddPrefix(string owner, HarmonyMethod info)
        {
            AddPatch(ref prefixes, owner, info);
        }

        /// <summary>Removes a prefix</summary>
        /// <param name="owner">The owner or (*) for any</param>
        ///
        public void RemovePrefix(string owner)
        {
            RemovePatch(ref prefixes, owner);
        }

        /// <summary>Adds a postfix</summary>
        /// <param name="patch">The patch</param>
        /// <param name="owner">The owner (Harmony ID)</param>
        /// <param name="priority">The priority</param>
        /// <param name="before">The before parameter</param>
        /// <param name="after">The after parameter</param>
        ///
        public void AddPostfix(MethodInfo patch, string owner, int priority, string[] before, string[] after)
        {
            AddPatch(ref postfixes, patch, owner, priority, before, after);
        }

        /// <summary>
        /// Adds a postfix
        /// </summary>
        /// <param name="owner">The owner (Harmony ID)</param>
        /// <param name="info">Method to add as postfix</param>
        public void AddPostfix(string owner, HarmonyMethod info)
        {
            AddPatch(ref postfixes, owner, info);
        }

        /// <summary>Removes a postfix</summary>
        /// <param name="owner">The owner or (*) for any</param>
        ///
        public void RemovePostfix(string owner)
        {
            RemovePatch(ref postfixes, owner);
        }

        /// <summary>Adds a transpiler</summary>
        /// <param name="patch">The patch</param>
        /// <param name="owner">The owner (Harmony ID)</param>
        /// <param name="priority">The priority</param>
        /// <param name="before">The before parameter</param>
        /// <param name="after">The after parameter</param>
        ///
        public void AddTranspiler(MethodInfo patch, string owner, int priority, string[] before, string[] after)
        {
            AddPatch(ref transpilers, patch, owner, priority, before, after);
        }

        /// <summary>
        /// Adds a transpiler
        /// </summary>
        /// <param name="owner">The owner (Harmony ID)</param>
        /// <param name="info">The method to add as transpiler</param>
        public void AddTranspiler(string owner, HarmonyMethod info)
        {
            AddPatch(ref transpilers, owner, info);
        }

        /// <summary>Removes a transpiler</summary>
        /// <param name="owner">The owner or (*) for any</param>
        ///
        public void RemoveTranspiler(string owner)
        {
            RemovePatch(ref transpilers, owner);
        }

        /// <summary>Adds a finalizer</summary>
        /// <param name="patch">The patch</param>
        /// <param name="owner">The owner (Harmony ID)</param>
        /// <param name="priority">The priority</param>
        /// <param name="before">The before parameter</param>
        /// <param name="after">The after parameter</param>
        ///
        public void AddFinalizer(MethodInfo patch, string owner, int priority, string[] before, string[] after)
        {
            AddPatch(ref finalizers, patch, owner, priority, before, after);
        }

        /// <summary>
        /// Adds a finalizer.
        /// </summary>
        /// <param name="owner">The owner (Harmony ID)</param>
        /// <param name="info">Method to add as finalizer</param>
        public void AddFinalizer(string owner, HarmonyMethod info)
        {
            AddPatch(ref finalizers, owner, info);
        }

        /// <summary>Removes a finalizer</summary>
        /// <param name="owner">The owner or (*) for any</param>
        ///
        public void RemoveFinalizer(string owner)
        {
            RemovePatch(ref finalizers, owner);
        }

        /// <summary>Removes a patch</summary>
        /// <param name="patch">The patch method</param>
        ///
        public void RemovePatch(MethodInfo patch)
        {
            lock (this)
            {
                prefixes = prefixes.Where(p => p.patch != patch).ToArray();
                postfixes = postfixes.Where(p => p.patch != patch).ToArray();
                transpilers = transpilers.Where(p => p.patch != patch).ToArray();
                finalizers = finalizers.Where(p => p.patch != patch).ToArray();
            }
        }
    }

    /// <summary>A serializable patch</summary>
    public class Patch : IComparable
    {
        /// <summary>Zero-based index</summary>
        public readonly int index;

        /// <summary>The owner (Harmony ID)</summary>
        public readonly string owner;

        /// <summary>The priority</summary>
        public readonly int priority;

        /// <summary>The before</summary>
        public readonly string[] before;

        /// <summary>The after</summary>
        public readonly string[] after;

        /// <summary>The patch method</summary>
        public MethodInfo patch;

        public MethodInfo PatchMethod
        {
            get => patch;
            set => patch = value;
        }

        /// <summary>Creates a patch</summary>
        /// <param name="patch">The patch</param>
        /// <param name="index">Zero-based index</param>
        /// <param name="owner">The owner (Harmony ID)</param>
        /// <param name="priority">The priority</param>
        /// <param name="before">The before parameter</param>
        /// <param name="after">The after parameter</param>
        ///
        public Patch(MethodInfo patch, int index, string owner, int priority, string[] before, string[] after)
        {
            if (patch is DynamicMethod)
                throw new ArgumentException(
                    $"Cannot directly reference dynamic method \"{patch.GetID()}\" in Harmony. Use a factory method instead that will return the dynamic method.", nameof(patch));

            this.index = index;
            this.owner = owner;
            this.priority = priority;
            this.before = before;
            this.after = after;
            this.patch = patch;
        }

        /// <summary>Gets the patch method</summary>
        /// <param name="original">The original method</param>
        /// <returns>The patch method</returns>
        ///
        public MethodInfo GetMethod(MethodBase original)
        {
            if (patch.ReturnType != typeof(DynamicMethod) && patch.ReturnType != typeof(MethodInfo)) return patch;
            if (patch.IsStatic == false) return patch;
            var parameters = patch.GetParameters();
            if (parameters.Count() != 1) return patch;
            if (parameters[0].ParameterType != typeof(MethodBase)) return patch;

            // we have a DynamicMethod factory, let's use it
            return patch.Invoke(null, new object[] {original}) as MethodInfo;
        }

        /// <summary>Determines whether patches are equal</summary>
        /// <param name="obj">The other patch</param>
        /// <returns>true if equal</returns>
        ///
        public override bool Equals(object obj)
        {
            return obj is Patch patch1 && patch == patch1.patch;
        }

        /// <summary>Determines how patches sort</summary>
        /// <param name="obj">The other patch</param>
        /// <returns>integer to define sort order (-1, 0, 1)</returns>
        ///
        public int CompareTo(object obj)
        {
            if (!(obj is Patch other))
                return 0;

            if (other.priority != priority)
                return -priority.CompareTo(other.priority);

            return index.CompareTo(other.index);
        }

        /// <summary>Hash function</summary>
        /// <returns>A hash code</returns>
        ///
        public override int GetHashCode()
        {
            return patch.GetHashCode();
        }
    }
}