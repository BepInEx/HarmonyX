using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

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

        /// <summary>Adds a prefix</summary>
        /// <param name="patch">The patch</param>
        /// <param name="owner">The owner (Harmony ID)</param>
        /// <param name="priority">The priority</param>
        /// <param name="before">The before parameter</param>
        /// <param name="after">The after parameter</param>
        ///
        public void AddPrefix(MethodInfo patch, string owner, int priority, string[] before, string[] after)
        {
            var l = prefixes.ToList();
            l.Add(new Patch(patch, prefixes.Count() + 1, owner, priority, before, after));
            prefixes = l.ToArray();
        }

        /// <summary>Removes a prefix</summary>
        /// <param name="owner">The owner or (*) for any</param>
        ///
        public void RemovePrefix(string owner)
        {
            if (owner == "*")
            {
                prefixes = new Patch[0];
                return;
            }

            prefixes = prefixes.Where(patch => patch.owner != owner).ToArray();
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
            var l = postfixes.ToList();
            l.Add(new Patch(patch, postfixes.Count() + 1, owner, priority, before, after));
            postfixes = l.ToArray();
        }

        /// <summary>Removes a postfix</summary>
        /// <param name="owner">The owner or (*) for any</param>
        ///
        public void RemovePostfix(string owner)
        {
            if (owner == "*")
            {
                postfixes = new Patch[0];
                return;
            }

            postfixes = postfixes.Where(patch => patch.owner != owner).ToArray();
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
            var l = transpilers.ToList();
            l.Add(new Patch(patch, transpilers.Count() + 1, owner, priority, before, after));
            transpilers = l.ToArray();
        }

        /// <summary>Removes a transpiler</summary>
        /// <param name="owner">The owner or (*) for any</param>
        ///
        public void RemoveTranspiler(string owner)
        {
            if (owner == "*")
            {
                transpilers = new Patch[0];
                return;
            }

            transpilers = transpilers.Where(patch => patch.owner != owner).ToArray();
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
            var l = finalizers.ToList();
            l.Add(new Patch(patch, finalizers.Count() + 1, owner, priority, before, after));
            finalizers = l.ToArray();
        }

        /// <summary>Removes a finalizer</summary>
        /// <param name="owner">The owner or (*) for any</param>
        ///
        public void RemoveFinalizer(string owner)
        {
            if (owner == "*")
            {
                finalizers = new Patch[0];
                return;
            }

            finalizers = finalizers.Where(patch => patch.owner != owner).ToArray();
        }

        /// <summary>Removes a patch</summary>
        /// <param name="patch">The patch method</param>
        ///
        public void RemovePatch(MethodInfo patch)
        {
            prefixes = prefixes.Where(p => p.patch != patch).ToArray();
            postfixes = postfixes.Where(p => p.patch != patch).ToArray();
            transpilers = transpilers.Where(p => p.patch != patch).ToArray();
            finalizers = finalizers.Where(p => p.patch != patch).ToArray();
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
        public readonly MethodInfo patch;

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
                throw new Exception("Cannot directly reference dynamic method \"" + patch.FullDescription() +
                                    "\" in Harmony. Use a factory method instead that will return the dynamic method.");

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
            if (patch.ReturnType != typeof(DynamicMethod)) return patch;
            if (patch.IsStatic == false) return patch;
            var parameters = patch.GetParameters();
            if (parameters.Count() != 1) return patch;
            if (parameters[0].ParameterType != typeof(MethodBase)) return patch;

            // we have a DynamicMethod factory, let's use it
            return patch.Invoke(null, new object[] {original}) as DynamicMethod;
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