using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;
#if !NETSTANDARD2_0
using System.Diagnostics.CodeAnalysis;
#endif

namespace StirlingLabs.Utilities.Collections;

// based on https://github.com/TwentyFourMinutes/BidirectionalDict/blob/master/src/BidirectionalDict/BidirectionalDict/ConcurrentBiDictionary.cs
// MIT License

/// <summary>
/// Represents a concurrent version of a bidirectional collection.
/// </summary>
/// <typeparam name="TLeft">The type of the first values in the dictionary.</typeparam>
/// <typeparam name="TRight">The type of the second values in the dictionary.</typeparam>
[PublicAPI]
public sealed class ConcurrentBijectiveSet<TLeft, TRight> : IEnumerable<(TLeft, TRight)>
  where TLeft : notnull
  where TRight : notnull
{
  private readonly ConcurrentDictionary<TLeft, TRight> _firstToSecond;
  private readonly ConcurrentDictionary<TRight, TLeft> _secondToFirst;

  ///	<summary>Tells if the inner Dictionaries of the <see cref="ConcurrentBijectiveSet{TLeft, TRight}" /> are still synced.</summary>
  /// <returns>The bool if the inner Dictionaries of the <see cref="ConcurrentBijectiveSet{TLeft, TRight}" /> are still synced..</returns>
  public bool IsSynced => Count == _secondToFirst.Count;

  ///	<summary>Gets the number of value pairs contained in the <see cref="ConcurrentBijectiveSet{TLeft, TRight}" />.</summary>
  /// <returns>The number of value pairs contained in the <see cref="ConcurrentBijectiveSet{TLeft, TRight}" />.</returns>
  public int Count => _firstToSecond.Count;

  /// <summary>Initializes a new instance of the <see cref="ConcurrentBijectiveSet{TLeft, TRight}" /> class that is empty, has the default initial capacity, and uses the default equality comparer for the key type.</summary>
  public ConcurrentBijectiveSet()
  {
    _firstToSecond = new();
    _secondToFirst = new();
  }

  /// <summary>Initializes a new instance of the <see cref="ConcurrentBijectiveSet{TLeft, TRight}" /> class that contains elements copied from the specified <see cref="IEnumerable{T}" /> and uses the default equality comparer for the key type.</summary>
  /// <param name="collection">The <see cref="IEnumerable{T}" /> whose elements are copied to the new <see cref="ConcurrentBijectiveSet{TLeft, TRight}" />.</param>
  public ConcurrentBijectiveSet(IEnumerable<KeyValuePair<TLeft, TRight>> collection)
  {
    if (collection is null) throw new ArgumentNullException(nameof(collection));
    var snapshot = collection as KeyValuePair<TLeft, TRight>[] ?? collection.ToArray();
    _firstToSecond = new(snapshot);
    _secondToFirst = new(snapshot.Select(kv => new KeyValuePair<TRight, TLeft>(kv.Value, kv.Key)));
  }

  /// <summary>Initializes a new instance of the <see cref="ConcurrentBijectiveSet{TLeft, TRight}" /> class that is empty, has the default initial capacity, and uses the specified <see cref="IEqualityComparer{T}" />.</summary>
  /// <param name="firstComparer">The <see cref="IEqualityComparer{TLeft}" /> implementation to use when comparing the first values, or <see langword="null" /> to use the default <see cref="IEqualityComparer{TLeft}" /> for the type of the key.</param>
  /// <param name="secondComparer">The <see cref="IEqualityComparer{TRight}" /> implementation to use when comparing the first values, or <see langword="null" /> to use the default <see cref="IEqualityComparer{TRight}" /> for the type of the key.</param>
  public ConcurrentBijectiveSet(IEqualityComparer<TLeft> firstComparer, IEqualityComparer<TRight> secondComparer)
  {
    if (firstComparer is null) throw new ArgumentNullException(nameof(firstComparer));
    if (secondComparer is null) throw new ArgumentNullException(nameof(secondComparer));
    _firstToSecond = new(firstComparer);
    _secondToFirst = new(secondComparer);
  }

  /// <summary>
  /// Tries to add new value pair to the <see cref="ConcurrentBijectiveSet{TLeft, TRight}"/>.
  /// </summary>
  /// <param name="first">The first value of the pair</param>
  /// <param name="second">The second value of the pair</param>
  /// <returns>Returns <see langword="true"/>, if the operation was successful, otherwise returns <see langword="false"/>.</returns>
  public bool TryAdd(TLeft first, TRight second)
  {
    if (!_firstToSecond.TryAdd(first, second))
      return false;

    if (_secondToFirst.TryAdd(second, first))
      return true;

    _firstToSecond.TryRemove(first, out var _);
    return false;

  }

  /// <summary>Uses the argument to add a value pair to the <see cref="ConcurrentBijectiveSet{TLeft, TRight}" /> if the first value does not already exist, or to update a value pair in the <see cref="ConcurrentBijectiveSet{TLeft, TRight}" /> if the first value already exists.</summary>
  /// <param name="first">The first value of the pair</param>
  /// <param name="second">The second value of the pair</param>
  /// <returns>The second value for first value. This will be either the existing second value for the first value if the first value is already in the dictionary, or the new second value if the first value was not in the dictionary.</returns>
  public TRight GetOrAdd(TLeft first, TRight second)
  {
    var tempSecond = _firstToSecond.GetOrAdd(first, second);

    if (tempSecond.Equals(second))
      _firstToSecond.GetOrAdd(first, second);

    return tempSecond;
  }

  /// <summary>Uses the argument to add a value pair to the <see cref="ConcurrentBijectiveSet{TLeft, TRight}" /> if the second value does not already exist, or to update a value pair in the <see cref="ConcurrentBijectiveSet{TLeft, TRight}" /> if the second value already exists.</summary>
  /// <param name="first">The first value of the pair</param>
  /// <param name="second">The second value of the pair</param>
  /// <returns>The first value for second value. This will be either the existing first value for the second value if the second value is already in the dictionary, or the new first value if the second value was not in the dictionary.</returns>
  public TLeft GetOrAdd(TRight second, TLeft first)
  {
    var tempFirst = _secondToFirst.GetOrAdd(second, first);

    if (tempFirst.Equals(first))
      _firstToSecond.GetOrAdd(first, second);

    return tempFirst;
  }

  /// <summary>
  /// Tries to add new value pair to the <see cref="ConcurrentBijectiveSet{TLeft, TRight}"/>. If any of the values already exists, it will be updated.
  /// </summary>
  /// <param name="first">The first value of the pair</param>
  /// <param name="second">The second value of the pair</param>
  public void AddOrUpdate(TLeft first, TRight second)
  {
    if (!_firstToSecond.TryAdd(first, second))
      _firstToSecond[first] = second;

    if (!_secondToFirst.TryAdd(second, first))
      _secondToFirst[second] = first;
  }

  /// <summary>
  /// Tries to remove a value pair from the <see cref="ConcurrentBijectiveSet{TLeft, TRight}"/>, by the first value of the pair.
  /// </summary>
  /// <param name="first">The first value of the pair</param>
  /// <param name="second">The second value of the pair</param>
  /// <returns>Returns <see langword="true"/>, if the operation was successful, otherwise returns <see langword="false"/>.</returns>

#if NETSTANDARD2_0
  public bool TryRemove(TLeft first, out TRight? second)
#else
  public bool TryRemove(TLeft first, [NotNullWhen(true)] out TRight? second)
#endif
  {
    if (!_firstToSecond.TryRemove(first, out second))
      return false;

    _secondToFirst.TryRemove(second, out var _);

    return true;
  }

  /// <summary>
  /// Tries to remove a value pair from the <see cref="ConcurrentBijectiveSet{TLeft, TRight}"/>, by the second value of the pair.
  /// </summary>
  /// <param name="second">The second value of the pair</param>
  /// <param name="first">The first value of the pair</param>
  /// <returns>Returns <see langword="true"/>, if the operation was successful, otherwise returns <see langword="false"/>.</returns>
#if NETSTANDARD2_0
  public bool TryRemove(TRight second, out TLeft? first)
#else
  public bool TryRemove(TRight second, [NotNullWhen(true)] out TLeft? first)
#endif
  {
    if (!_secondToFirst.TryRemove(second, out first!))
      return false;

    _firstToSecond.TryRemove(first, out var _);

    return true;
  }

  /// <summary>
  /// Tells if the <see cref="ConcurrentBijectiveSet{TLeft, TRight}"/> contains the first value of the value pair.
  /// </summary>
  /// <param name="first">The first value of the pair</param>
  /// <returns>Returns <see langword="true"/>, if the operation was successful, otherwise returns <see langword="false"/>.</returns>
  public bool Contains(TLeft first)
    => _firstToSecond.ContainsKey(first);

  /// <summary>
  /// Tells if the <see cref="ConcurrentBijectiveSet{TLeft, TRight}"/> contains the second value of the value pair.
  /// </summary>
  /// <param name="second">The second value of the pair</param>
  /// <returns>Returns <see langword="true"/>, if the operation was successful, otherwise returns <see langword="false"/>.</returns>
  public bool Contains(TRight second)
    => _secondToFirst.ContainsKey(second);

  /// <summary>
  /// Clears all value pairs in the <see cref="ConcurrentBijectiveSet{TLeft, TRight}"/>.
  /// </summary>
  public void Clear()
  {
    _secondToFirst.Clear();
    _firstToSecond.Clear();
  }

  /// <summary>
  /// Gets the element by the specified value of the value pair
  /// </summary>
  /// <param name="first">The first value of the pair</param>
  /// <returns>The second value of the pair</returns>
  public TRight this[TLeft first]
    => _firstToSecond[first];


  /// <summary>
  /// Gets the element by the specified value of the value pair
  /// </summary>
  /// <param name="second">The second value of the pair</param>
  /// <returns>The first value of the pair</returns>
  public TLeft this[TRight second]
    => _secondToFirst[second];


  /// <summary>
  /// Tries to get a value of the value pair from the <see cref="ConcurrentBijectiveSet{TLeft, TRight}"/>, by the first value of the pair.
  /// </summary>
  /// <param name="first">The first value of the pair</param>
  /// <param name="second">The first value of the pair</param>
  /// <returns>Returns <see langword="true"/>, if the operation was successful, otherwise returns <see langword="false"/>.</returns>
#if NETSTANDARD2_0
  public bool TryGet(TLeft first, out TRight second)
#else
  public bool TryGet(TLeft first, [NotNullWhen(true)] out TRight second)
#endif
    => _firstToSecond.TryGetValue(first, out second!);

  /// <summary>
  /// Tries to get a value of the value pair from the <see cref="ConcurrentBijectiveSet{TLeft, TRight}"/>, by the second value of the pair.
  /// </summary>
  /// <param name="first">The first value of the pair</param>
  /// <param name="second">The second value of the pair</param>
  /// <returns>Returns <see langword="true"/>, if the operation was successful, otherwise returns <see langword="false"/>.</returns>
#if NETSTANDARD2_0
  public bool TryGet(TRight second, out TLeft first)
#else
  public bool TryGet(TRight second, [NotNullWhen(true)] out TLeft first)
#endif
    => _secondToFirst.TryGetValue(second, out first!);

  /// <summary>
  /// Gets the <see cref="IEnumerator{T}"/> of the <see cref="ConcurrentBijectiveSet{TLeft, TRight}"/>.
  /// </summary>
  /// <returns>The <see cref="IEnumerator{T}"/> of the <see cref="ConcurrentBijectiveSet{TLeft, TRight}"/></returns>
  public IEnumerator<(TLeft, TRight)> GetEnumerator()
    => _firstToSecond
      .Select(kv => Unsafe.As<KeyValuePair<TLeft, TRight>, (TLeft, TRight)>(ref Unsafe.AsRef(kv)))
      .GetEnumerator();

  /// <summary>
  /// Gets the <see cref="IEnumerator"/> of the <see cref="ConcurrentBijectiveSet{TLeft, TRight}"/>.
  /// </summary>
  /// <returns>The <see cref="IEnumerator"/> of the <see cref="ConcurrentBijectiveSet{TLeft, TRight}"/></returns>
  IEnumerator IEnumerable.GetEnumerator()
    => _firstToSecond.GetEnumerator();
}
