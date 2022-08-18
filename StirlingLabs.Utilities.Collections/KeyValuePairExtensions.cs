using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace StirlingLabs.Utilities.Collections
{
    public static class KeyValuePairExtensions
    {
#if NETSTANDARD2_0
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Deconstruct<TKey, TValue>(ref this KeyValuePair<TKey, TValue> kvp, out TKey key, out TValue value)
        {
            key = kvp.Key;
            value = kvp.Value;
        }
#endif
    }
}
