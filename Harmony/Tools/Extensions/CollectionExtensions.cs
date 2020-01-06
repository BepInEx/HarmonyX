using System;
using System.Collections.Generic;
using System.Linq;

namespace HarmonyLib
{
    /// <summary>General extensions for collections</summary>
    public static class CollectionExtensions
    {
        /// <summary>A simple way to execute code for every element in a collection</summary>
        /// <typeparam name="T">The inner type of the collection</typeparam>
        /// <param name="sequence">The collection</param>
        /// <param name="action">The action to execute</param>
        ///
        public static void Do<T>(this IEnumerable<T> sequence, Action<T> action)
        {
            if (sequence == null) return;
            var enumerator = sequence.GetEnumerator();
            while (enumerator.MoveNext()) action(enumerator.Current);
        }

        /// <summary>A simple way to execute code for elements in a collection matching a condition</summary>
        /// <typeparam name="T">The inner type of the collection</typeparam>
        /// <param name="sequence">The collection</param>
        /// <param name="condition">The predicate</param>
        /// <param name="action">The action to execute</param>
        ///
        public static void DoIf<T>(this IEnumerable<T> sequence, Func<T, bool> condition, Action<T> action)
        {
            sequence.Where(condition).Do(action);
        }

        /// <summary>A helper to add an item to a collection</summary>
        /// <typeparam name="T">The inner type of the collection</typeparam>
        /// <param name="sequence">The collection</param>
        /// <param name="item">The item to add</param>
        /// <returns>The collection containing the item</returns>
        /// 
        /// Note: this was called 'Add' before but that led to unwanted side effect
        ///       See https://github.com/pardeike/Harmony/issues/147
        ///
        public static IEnumerable<T> AddItem<T>(this IEnumerable<T> sequence, T item)
        {
            return (sequence ?? Enumerable.Empty<T>()).Concat(new[] {item});
        }

        /// <summary>A helper to add an item to an array</summary>
        /// <typeparam name="T">The inner type of the collection</typeparam>
        /// <param name="sequence">The array</param>
        /// <param name="item">The item to add</param>
        /// <returns>The array containing the item</returns>
        ///
        public static T[] AddToArray<T>(this T[] sequence, T item)
        {
            return AddItem(sequence, item).ToArray();
        }

        /// <summary>A helper to add items to an array</summary>
        /// <typeparam name="T">The inner type of the collection</typeparam>
        /// <param name="sequence">The array</param>
        /// <param name="items">The items to add</param>
        /// <returns>The array containing the items</returns>
        ///
        public static T[] AddRangeToArray<T>(this T[] sequence, T[] items)
        {
            return (sequence ?? Enumerable.Empty<T>()).Concat(items).ToArray();
        }
    }
}