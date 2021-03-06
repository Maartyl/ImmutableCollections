﻿using System;
using System.Collections.Generic;
using Maa.Common;

/*
The MIT License (MIT)

Copyright (c) 2015 Jules Jacobs

Copyright (c) 2018 Maartyl

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
*/

namespace Maa.Data {

  //represents immutable hash map
  // - abstract class, so I can define general operators on it
  public abstract class Map<TK, TV> {
    public abstract Map<TK, TV> Assoc(TK k, TV v);

    public abstract Map<TK, TV> Dissoc(TK k);

    public abstract Opt<TV> ValueAt(TK k);

    public virtual Map<TK, TV> Update<TCtx>(TK k, Func<TCtx, Opt<TV>, Opt<TV>> updater, TCtx ctx, IEqualityComparer<TV> eqComparerT) {
      //simple reference nonoptimized implementation
      var origOpt = ValueAt(k);
      return updater(ctx, origOpt).Match(v => {
        if (origOpt) {
          if (eqComparerT.Equals(v, origOpt.ValueOrDefault))
            return this; //already contains the same value
        }
        return Assoc(k, v);
      }, () => origOpt ? Dissoc(k) : this);
    }

    #region impls + defaults

    public Map<TK, TV> Update<TCtx>(TK k, Func<TCtx, Opt<TV>, Opt<TV>> updater, TCtx ctx) {
      return Update(k, updater, ctx, EqualityComparer<TV>.Default);
    }

    public Map<TK, TV> Update(TK k, Func<Opt<TV>, Opt<TV>> updater) {
      if (updater == null)
        throw new ArgumentNullException("updater");

      return Update(k, (f, o) => f(o), updater); //don't create another closure to wrap - use context for fn itself
    }

    #endregion impls + defaults
  }

  public struct HAMT<K, V> {

    //size of HAMT struct: 2x ptr,2x int
    TrieNode<TrieNode<TrieNode<TrieNode<TrieNode<TrieNode<TrieNode<Bucket>>>>>>> trie;

    #region API

    //TODO: wrapper CLASS with the ~same API, but mabe also counted etc. (counted would require changes to Assoc and Dissoc impls...)
    // - this is still a struct: not ideal for passing around...

    //short bracket notation for most common operations
    public Opt<V> this[K key] { get { return ValueAt(key); } }

    public HAMT<K, V> this[K key, V value] { get { return Assoc(key, value); } }

    public static HAMT<K, V> operator -(HAMT<K, V> m, K k) {
      return m.Dissoc(k);
    }

    #region update
    public HAMT<K, V> Update<TCtx>(K k, Func<TCtx, Opt<V>, Opt<V>> updater, TCtx ctx, IEqualityComparer<V> eqComparerT) {
      //simple reference nonoptimized implementation
      var origOpt = ValueAt(k);
      var self = this;
      return updater(ctx, origOpt).Match(v => {
        if (origOpt) {
          if (eqComparerT.Equals(v, origOpt.ValueOrDefault))
            return self; //already contains the same value
        }
        return self.Assoc(k, v);
      }, () => origOpt ? self.Dissoc(k) : self);
    }

    #region impls + defaults

    public HAMT<K, V> Update<TCtx>(K k, Func<TCtx, Opt<V>, Opt<V>> updater, TCtx ctx) {
      return Update(k, updater, ctx, EqualityComparer<V>.Default);
    }

    public HAMT<K, V> Update(K k, Func<Opt<V>, Opt<V>> updater) {
      if (updater == null)
        throw new ArgumentNullException("updater");

      return Update(k, (f, o) => f(o), updater); //don't create another closure to wrap - use context for fn itself
    }

    #endregion impls + defaults
    #endregion update

    public bool TryGetValue(K key, out V value) {
      return trie.TryGetValue(key, hashOf(key), out value);
    }

    public Opt<V> ValueAt(K key) {
      V v;
      return TryGetValue(key, out v) ? v.Some() : Opt.NoneAny;
    }

    public HAMT<K, V> Assoc(K key, V value) {
      var self = this;

      self.trie.UpdateAssoc(new KeyValuePair<K, V>(key, value), hashOf(key));
      return self;
    }

    public HAMT<K, V> Dissoc(K key) {
      var self = this;

      var h = hashOf(key);
      var replace = self.trie.UpdateDissoc(key, h);
      if (replace) { //only top node can contain single entery - tried to replace itself, but no higher level
        self = Empty;
        self.trie.UpdateAssoc(replace.ValueOrDefault, h);
      }

      return self;
    }

    internal void DebugInspectPrint() {
      trie.DebugInspectPrint();
    }

    public IEnumerable<KeyValuePair<K, V>> Enumerate() {
      return trie.Enumerate();
    }

    public HAMT<K, V> MapValues(Func<KeyValuePair<K, V>, V> mapper) {
      var self = this;
      self.trie.UpdateMapValues(mapper);
      return self;
    }

    ///struct: same as new
    public static readonly HAMT<K, V> Empty = new HAMT<K, V>();

    #endregion API

    #region implementation

    static readonly Func<K, K, bool> eq = EqualityComparer<K>.Default.Equals;

    static int hashOf(K v) {
      // Analysis disable once CompareNonConstrainedGenericWithNull
      return v == null ? 0 : v.GetHashCode();
    }

    static KeyValuePair<K, V>[] mapValueArr(Func<KeyValuePair<K, V>, V> mapper, KeyValuePair<K, V>[] kvs) {
      //assumes valid args (not null etc.)

      var len = kvs.Length;
      var mapped = new KeyValuePair<K, V>[len];
      for (int i = 0; i < len; i++) {
        mapped[i] = new KeyValuePair<K, V>(kvs[i].Key, mapper(kvs[i]));
      }
      return mapped;
    }

    interface IHashTrieNode {
      bool TryGetValue(K key, int hash, out V value);

      void UpdateAssoc(KeyValuePair<K, V> kv, int hash);

      //returns: none -> keep child; KV -> replace child with entry (only 1 entry in subtree left)
      // - if KV: you must replace: cannot assume it's updated to only hold one
      Opt<KeyValuePair<K, V>> UpdateDissoc(K k, int hash);

      void Init2(KeyValuePair<K, V> kv1, int hash1, KeyValuePair<K, V> kv2, int hash2);

      IEnumerable<KeyValuePair<K, V>> Enumerate();

      //will not change shape of tree or any keys
      // - only maps values - costs single copy of entire tree (many assocs would be much more expensive)
      void UpdateMapValues(Func<KeyValuePair<K, V>, V> mapper);

      //TrieNode<TrieNode<TrieNode<TrieNode<TrieNode<TrieNode<TrieNode<Bucket>>>>>>> trie;
      // - originally: top started with 0, then each additional added 5
      // - if I put 35 to Bucket: then each in static ctor subtracts 5 -> all will know their depth.
      // - this should just return the static field (that coulnd't be directly part of interface...)
      int GetNodeDepth();

      void DebugInspectPrint();
    }

    struct TrieNode<Child> : IHashTrieNode where Child : struct, IHashTrieNode {
      /*, new()*/
      int childrenbitmap;
      int entriesbitmap;

      Child[] children;
      KeyValuePair<K, V>[] entries;

      public bool TryGetValue(K key, int hash, out V value) {
        int bit = ComputeBit(hash);
        if ((bit & childrenbitmap) != 0) {
          return children[ComputeIndex(bit, childrenbitmap)].TryGetValue(key, hash, out value);
        } else if ((bit & entriesbitmap) != 0) {
          var kv = entries[ComputeIndex(bit, entriesbitmap)];
          if (eq(kv.Key, key)) {
            value = kv.Value;
            return true;
          }
        }
        value = default(V);
        return false;
      }

      public void UpdateAssoc(KeyValuePair<K, V> kv, int hash) {
        int bit = ComputeBit(hash);
        if ((bit & childrenbitmap) != 0) {
          children = children.Copy();
          children[ComputeIndex(bit, childrenbitmap)].UpdateAssoc(kv, hash);
        } else if ((bit & entriesbitmap) != 0) {
          int i = ComputeIndex(bit, entriesbitmap);
          var ent = entries[i];
          if (eq(ent.Key, kv.Key)) {
            entries = entries.CopySetAt(i, kv);
          } else {
            int j = 0;
            if (children == null)
              children = new Child[1];
            else {
              j = ComputeIndex(bit, childrenbitmap);
              children = children.CopyInsertAt(j, new Child());
            }
            children[j].Init2(kv, hash, ent, hashOf(ent.Key));
            entries = entries.CopyRemoveAt(i);
            childrenbitmap |= bit;
            entriesbitmap &= ~bit;
          }
        } else {
          entriesbitmap |= bit; //why is this the same as setting after ComputeIndex?
          // - must match index I'm looking for, so...
          // OOH! it deoes NOT matter: only computes from bits LOWER than bit, so if bit set or not doesn't matter...
          // - whow, that's ... pretty cool, actually...
          if (entries == null)
            entries = new[] { kv };
          else
            entries = entries.CopyInsertAt(ComputeIndex(bit, entriesbitmap), kv);
          //entriesbitmap |= bit;
        }
      }

      public Opt<KeyValuePair<K, V>> UpdateDissoc(K key, int hash) {
        int bit = ComputeBit(hash);
        if ((bit & childrenbitmap) != 0) {
          var i = ComputeIndex(bit, childrenbitmap);
          var tmp = children[i];
          var replace = tmp.UpdateDissoc(key, hash);
          if (replace) {
            if (bit == childrenbitmap && entriesbitmap == 0)
              return replace; //was sole child and no entries: replace myself
            else {
              //collapse of child into entry

              if (bit == childrenbitmap) {
                childrenbitmap = 0;
                children = null;
              } else {
                childrenbitmap &= ~bit;
                children = children.CopyRemoveAt(i);
              }

              entriesbitmap |= bit;
              entries = entries.CopyInsertAt(ComputeIndex(bit, entriesbitmap), replace.ValueOrDefault);
              return Opt.NoneAny; //keep me
            }
          } else {
            //keep tmp as child - did not ask to be replaced
            children = children.CopySetAt(i, tmp);
            return Opt.NoneAny; //keep me
          }
        } else if ((bit & entriesbitmap) != 0) {
          var i = ComputeIndex(bit, entriesbitmap);
          var kv = entries[i];
          if (eq(kv.Key, key)) {
            if (entries.Length > 2) { //common case: multiple entries, just remove
              entriesbitmap &= ~bit;
              entries = entries.CopyRemoveAt(i);
              return Opt.NoneAny; //keep me
            } else { //sole entry will remain
              if (childrenbitmap == 0) { //no children - replace myself
                //I know there cannot be more than 2 children
                //it's impossible to have no children and only single entry
                // -> must be 2
                //; replace myself with other

                return entries[i == 0 ? 1 : 0];
              } else {
                if (entriesbitmap == bit) { //must have been a single entry, but I still have children
                  entriesbitmap = 0;
                  entries = null;
                  return Opt.NoneAny; //keep me
                } else { //has children and there were multiple entries (2) - keep the other
                  entriesbitmap &= ~bit;
                  entries = entries.CopyRemoveAt(i);
                  return Opt.NoneAny; //keep me
                }
              }
            }
          } else {
            //found hash, but not matching key: cannot be collision (would be in bucket) so key not present
            //nothing to remove -> nothing to do; stay as I am
            return Opt.NoneAny; //keep me
          }
        } else { //hash not found in either
          //nothing to remove -> nothing to do; stay as I am
          return Opt.NoneAny; //keep me
        }
      }

      public void Init2(KeyValuePair<K, V> kv1, int hash1, KeyValuePair<K, V> kv2, int hash2) {
        var bit1 = ComputeBit(hash1);
        var bit2 = ComputeBit(hash2);
        if (bit1 == bit2) {
          childrenbitmap = bit1;
          children = new Child[1];
          children[0].Init2(kv1, hash1, kv2, hash2);
        } else {
          entriesbitmap = bit1 | bit2;
          //if didn't use uint: if last bit set (negative int): should be /after/ the other (but I'm using <): would flip order
          //it took me a good while to find this bug...
          entries = unchecked((uint)bit1) < unchecked((uint)bit2) ? new[] { kv1, kv2 } : new[] { kv2, kv1 };
        }
      }

      public IEnumerable<KeyValuePair<K, V>> Enumerate() {
        if (entries != null)
          foreach (var e in entries)
            yield return e;

        if (children != null) //there's surely a faster way to do this, but this one is fine for now
          foreach (var c in children)
            foreach (var e in c.Enumerate())
              yield return e;
      }

      public void UpdateMapValues(Func<KeyValuePair<K, V>, V> mapper) {
        if (entries != null)
          entries = mapValueArr(mapper, entries);
        if (children != null) {
          children = children.Copy();
          for (int i = 0; i < children.Length; i++) {
            children[i].UpdateMapValues(mapper);
          }
        }
      }

      public int GetNodeDepth() {
        return depth;
      }

      //see GetNodeDepth in interface for explanation
      static readonly int depth = default(Child).GetNodeDepth() - 5;

      static int ComputeBit(int hash) {
        return 1 << ((hash >> depth) & 0x01F);
      }

      static int ComputeIndex(int bit, int bitmap) {
        return BitCount(bitmap & (bit - 1));
      }

      // blatantly stolen
      static int BitCount(int x) {
        x = x - ((x >> 1) & 0x55555555);                    // reuse input as temporary
        x = (x & 0x33333333) + ((x >> 2) & 0x33333333);     // temp
        x = ((x + (x >> 4) & 0xF0F0F0F) * 0x1010101) >> 24; // count
        return x;
      }

      #region debug

      static void dbgLn(string s) {
        if (s == null)
          s = "";

        var indent = new string(' ', depth / 5);

        Console.Write(indent);
        Console.WriteLine(s.Replace("\n", "\n" + indent));
      }

      public void DebugInspectPrint() {
        if (entriesbitmap != 0) {
          dbgLn("entries " + dbgHash(entriesbitmap));
        } else {
          dbgLn("entries none");
        }

        if (entries != null)
          foreach (var e in entries)
            dbgLn("." + dbgHash(hashOf(e.Key)) + " ; " + e.Key);

        if (childrenbitmap != 0)
          dbgLn("children " + dbgHash(childrenbitmap));
        else
          dbgLn("children none");

        if (children != null)
          for (int i = 0; i < 32; i++) {
            var bit = (1 << i);
            if ((bit & childrenbitmap) != 0) {
              dbgLn("child " + binI5(i));
              children[ComputeIndex(bit, childrenbitmap)].DebugInspectPrint();
            }
          }
      }

      #endregion debug
    }

    struct Bucket : IHashTrieNode {
      public int GetNodeDepth() {
        return 35; //see interface for explanation -- allows each node type to determine it's depth.
      }

      KeyValuePair<K, V>[] entries;

      public void Init2(KeyValuePair<K, V> kv1, int hash1, KeyValuePair<K, V> kv2, int hash2) {
        entries = new[] { kv1, kv2 };
      }

      public bool TryGetValue(K key, int hash, out V value) {
        foreach (var kv in entries) {
          if (eq(kv.Key, key)) {
            value = kv.Value;
            return true;
          }
        }
        value = default(V);
        return false;
      }

      public void UpdateAssoc(KeyValuePair<K, V> kvNew, int hash) {
        if (entries == null) { //not possible inside tree; only when Bucket used for small maps
          entries = new[] { kvNew };
          return;
        }

        var indexToReplace = findIndex(kvNew.Key);

        entries = indexToReplace < 0
          ? entries.CopyInsertAt(0, kvNew) //add ;; also to start: doesn't matter and last inserted might be most likely to be searched for
          : entries.CopySetAt(indexToReplace, kvNew);
      }

      public Opt<KeyValuePair<K, V>> UpdateDissoc(K k, int hash) {
        if (entries == null)
          return Opt.NoneAny; //not possible inside tree; only when Bucket used for small maps

        var indexToRemove = findIndex(k);

        if (indexToRemove < 0)
          return Opt.NoneAny; //not found: keep me as I am

        if (entries.Length == 2)
          return entries[indexToRemove == 0 ? 1 : 0]; //return other than removed to replace self with

        if (entries.Length == 1) { //not possible inside tree; only when Bucket used for small maps
          entries = null; //was single: removed to null
          return Opt.NoneAny;
        }

        entries = entries.CopyRemoveAt(indexToRemove);
        return Opt.NoneAny;
      }

      int findIndex(K k) {
        //ASSUMPTION: entries != null

        var len = entries.Length;
        for (int i = 0; i < len; i++)
          if (eq(entries[i].Key, k))
            return i;

        return -1;
      }

      public IEnumerable<KeyValuePair<K, V>> Enumerate() {
        return entries ?? System.Linq.Enumerable.Empty<KeyValuePair<K, V>>();
      }

      public void UpdateMapValues(Func<KeyValuePair<K, V>, V> mapper) {
        if (entries != null)
          entries = mapValueArr(mapper, entries);
      }

      public void DebugInspectPrint() {
        var len = 0;
        if (entries != null)
          len = entries.Length;

        dbgLn("bucket #" + len);

        foreach (var e in entries)
          dbgLn("" + e.Key);
      }

      static void dbgLn(string s) {
        if (s == null)
          s = "";

        var indent = new string(' ', 35 / 5);

        Console.Write(indent);
        Console.WriteLine(s.Replace("\n", "\n" + indent));
      }
    }

    static string dbgHash(int h) {
      var bin = Convert.ToString(h, 2);
      return new string('0', 32 - bin.Length) + bin + " " + h;
    }

    static string binI5(int h) {
      var bin = Convert.ToString(h, 2);
      if (bin.Length >= 5)
        return bin;
      return new string('0', 5 - bin.Length) + bin;
    }

    static HAMT() {
      //point of this is partially for safety, but mainly to assure it gets evaluated before anything else:
      // - thus initialization not checked during actual algorithm
      // -- I don't think it would, but adding this does not hurt

      if (Empty.trie.GetNodeDepth() != 0)
        throw new InvalidProgramException("TrieMap top level NodeDepth must be 0; is: " + Empty.trie.GetNodeDepth());
    }

    #endregion implementation
  }

  public struct HamtTest {
    public static void Run() {
      var h = HAMT<int, int>.Empty;

      const int size = 40; //100000
      // const int offset = 939997; //9999999 works fine again... ;; 999999 many broken
      const int offset = 941000; //9999999 works fine again... ;; 999999 many broken
      // 939999 -- only 2 swapped;; other is: 940031
      //11100101011111011111
      //11100101011111111111

      for (int i = (offset + 0); i < (offset + 10 * size); i++) {
        h = h.Assoc(i, i);

        int v;
        if (!h.TryGetValue(i, out v)) {
          Console.WriteLine("E1: {0} has no value", i);

          h.DebugInspectPrint();

          var hx = h.Assoc(i, i);

          Console.WriteLine("--------------e: Second of {0}------------", i);

          hx.DebugInspectPrint();

          if (hx.TryGetValue(i, out v)) {
            Console.WriteLine("E1: {0} after second assoc: {1}", i, v);
          } else {
            Console.WriteLine("E1: {0} has no value even after second Assoc", i);
          }

          Console.WriteLine("E1: {0} has no value", i);
          return; //debug

          //continue;
        }
      }

      //      h.DebugInspectPrint();
      //      return;

      var _ = h[5, 8][6, 7][5].ValueOrDefault;

      for (int i = (offset + 0); i < (offset + 5 * size); i++) {
        h = h.Assoc(i, -i);
      }

      for (int i = (offset + 0); i < (offset + 10 * size); i++) {
        int v;
        if (!h.TryGetValue(i, out v)) {
          Console.WriteLine("Error: {0} has no value", i);

          if (h.Assoc(i, i).TryGetValue(i, out v)) {
            Console.WriteLine("Error: {0} after second assoc: {1}", i, v);
          } else {
            Console.WriteLine("Error: {0} has no value even after second Assoc", i);
          }
          continue;
        }
        if ((i < (offset + 5 * size) && v != -i) || (i >= (offset + 5 * size) && v != i))
          Console.WriteLine("Error: {0} returned {1}", i, v);
      }

      for (int i = (offset + 1 + 10 * size); i < (offset + 20 * size); i++) {
        int v;
        if (h.TryGetValue(i, out v))
          Console.WriteLine("Error: {0} shouldn't be present; is: {1}", i, v);
      }

      Console.WriteLine("End");
    }

    public static void Run2() {
      var h = HAMT<int, int>.Empty;

      const int last = 31;
      h = h.Assoc(last + (1 << 5), 42 + (1 << 5));
      // h = h.Assoc(42 + (2 << 5), 42 +( 2 << 5));
      h = h.Assoc(last + (3 << 5), 42 + (3 << 5));
      h = h.Assoc(last + (2 << 5), 42 + (2 << 5));

      h = h.Assoc(42, 465);
      var h2 = h.Assoc(42, 1);

      int x;
      int x2;
      h.TryGetValue(42, out x);
      h2.TryGetValue(42, out x2);
      Console.WriteLine("h: {0} h2: {1}", x, x2);

      h = h2.Assoc(940029, 42);
      h.TryGetValue(940029, out x);
      Console.WriteLine("940029: {0}", x);

      h.TryGetValue(last + (1 << 5), out x);
      Console.WriteLine("last + (1 << 5): {0}", x);

      h.TryGetValue(last + (2 << 5), out x);
      Console.WriteLine("last + (2 << 5): {0}", x);

      h.TryGetValue(last + (3 << 5), out x);
      Console.WriteLine("last + (3 << 5): {0}", x);
    }

    //TODO: run correctness tests
    // - run dissoc, assoc, enumerate, ...

    public static void Dissoc(int rep) {
      var r = new Random(78945611); //seed, so test is repeatable

      for (int i = 0; i < rep; i++) {
        Dissoc1(r.Next());
      }
    }

    public static void Dissoc1(int seed) {
      var h = default(HAMT<int, int>);
      var r = new Random(seed); //seed, so test is repeatable

      const int size = 4000; //100000
      // const int offset = 939997;
      int offset = unchecked(seed - size);
      // 939999

      for (int i = (offset + 0); i < (offset + 10 * size); i++) {
        h = h[i, i];
      }

      int delets = size * 2;
      while (delets-- > 0) {
        var n = offset + r.Next(size / 2); // always keep half keys: then check all present
        var opt = h[n];

        if (opt) {
          h = h - n;

          if (opt.ValueOrDefault != n) {
            Console.WriteLine("Error: {0} returned {1}", n, opt.ValueOrDefault);
          }

          opt = h[n];
          if (opt) {
            h.DebugInspectPrint();
            Console.WriteLine("E: {0} not removed after Dissoc; is: {1}", n, opt.ValueOrDefault);
            return;
          }
        }
      }

      Console.WriteLine("Dissoc part passed");

      //part that should stay unchanged
      for (int i = offset + (size / 2 + 1); i < offset + size; i++) {
        var opt = h[i];
        if (opt) {
          if (opt.ValueOrDefault != i)
            Console.WriteLine("Error: {0} returned {1}", i, opt.ValueOrDefault);
        } else {
          Console.WriteLine("Error: {0} not present after dissoc test", i);
        }
      }

      Console.WriteLine("End " + seed);

      //h.DebugInspectPrint();
    }

    public static void Inspect() {
      var h = default(HAMT<int, int>);
      var r = new Random(456773);

      const int start = int.MinValue;
      const int size = 500;
      for (int i = start; i < start + size; i++) {
        h = h[i, -i];
      }

      // h = (h - 0 - 33 - 34)[33,42];

      h = h
        //- -2147483617
        - -2147483585
        - -2147483553
        - -2147483521
        - -2147483489
        - -2147483457
        - -2147483425
        - -2147483393
        - -2147483361
        - -2147483329
        - -2147483297
        - -2147483265
        - -2147483233
        - -2147483201
        - -2147483169
        //        [(33 << 5) + 33, 1]
        //        [(33 << 10) + 33, 2]
        //        [(33 << 10) + 29, 2]
        //        [1,-1]
        //        [2,-1]
        //        [3,-1]
        //        [4,-1]

        ;

      h.DebugInspectPrint();
    }
  }
}