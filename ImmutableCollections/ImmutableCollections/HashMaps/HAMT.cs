using System;
using System.Collections.Generic;
using Maa.Common;

namespace ImmutableCollections.HashMaps {

  //represents immutable hash map
  // - abstract class, so I can define general operators on it
  public abstract class Map<TK,TV> {
    public abstract Map<TK,TV> Assoc(TK k, TV v);

    public abstract Map<TK,TV> Dissoc(TK k);

    public abstract Opt<TV> ValueAt(TK k);

    public virtual Map<TK,TV> Update<TCtx>(TK k, Func<TCtx, Opt<TV>, Opt<TV>> updater, TCtx ctx, IEqualityComparer<TV> eqComparerT) {
      //simple reference nonoptimized implementation
      var origOpt = ValueAt(k);
      return updater(ctx, origOpt).Match(v => {
        if(origOpt) {
          if(eqComparerT.Equals(v, origOpt.ValueOrDefault))
            return this; //already contains the same value
        }
        return Assoc(k, v);
      }, () => origOpt ? Dissoc(k) : this);
    }



    #region impls + defaults

    public Map<TK,TV> Update<TCtx>(TK k, Func<TCtx, Opt<TV>, Opt<TV>> updater, TCtx ctx) {
      return Update(k, updater, ctx, EqualityComparer<TV>.Default);
    }

    public Map<TK,TV> Update(TK k, Func<Opt<TV>, Opt<TV>> updater) {
      if(updater == null)
        throw new ArgumentNullException("updater");
      
      return Update(k, (f, o) => f(o), updater); //don't create another closure to wrap - use context for fn itself
    }

    #endregion


  }

  struct HAMT<K, V> {
    static readonly Func<K, K, bool> eq = EqualityComparer<K>.Default.Equals;

    static int hashOf(K v) {
      return v == null ? 0 : v.GetHashCode();
    }

    // TODO:
    //   optimize copying
    //   remove depth parameter
    //   try inlining bit calculations
    interface IHashMap {
      bool TryGetValue(K key, int hash, out V value);

      void UpdateAssoc(KeyValuePair<K,V> kv, int hash);

      void Init2(KeyValuePair<K,V> kv1, int hash1, KeyValuePair<K,V> kv2, int hash2);


      //TrieNode<TrieNode<TrieNode<TrieNode<TrieNode<TrieNode<TrieNode<Bucket>>>>>>> trie;
      // - originally: top started with 0, then each additional added 5
      // - if I put 35 to Bucket: then each in static ctor subtracts 5 -> all will know their depth.
      // - this should just return the static field (that coulnd't be directly part of interface...)
      int GetNodeDepth();
    }

    struct TrieNode<Child> : IHashMap where Child : IHashMap, new() {
      int childrenbitmap;
      int entriesbitmap;

      Child[] children;
      KeyValuePair<K, V>[] entries;

      public bool TryGetValue(K key, int hash, out V value) {
        int bit = ComputeBit(hash);
        if((bit & childrenbitmap) != 0) {
          return children[ComputeIndex(bit, childrenbitmap)].TryGetValue(key, hash, out value);
        } else if((bit & entriesbitmap) != 0) {
          var kv = entries[ComputeIndex(bit, entriesbitmap)];
          if(eq(kv.Key, key)) {
            value = kv.Value;
            return true;
          }
        }
        value = default(V);
        return false;
      }

      public void UpdateAssoc(KeyValuePair<K,V> kv, int hash) {
        int bit = ComputeBit(hash);
        if((bit & childrenbitmap) != 0) {
          //speed is not as important as corectness <- readability

          //var tmp = new Child[children.Length];
          //Array.Copy(children, tmp, children.Length);
          children = children.Copy(); //maa: remove 'inlining' - use ArrayUtils
          children[ComputeIndex(bit, childrenbitmap)].UpdateAssoc(kv, hash);
        } else if((bit & entriesbitmap) != 0) {
          int i = ComputeIndex(bit, entriesbitmap);
          var ent = entries[i];
          if(eq(ent.Key, kv.Key)) {
            //orig // - YUP: I tested it: I noticed right: it was wrong - did mtate original map
            //entries[i] = new KeyValuePair<K,V>(key, value);

            //maa -- update copied structure; but not the original array
            entries = entries.CopyAndSet(i, kv);
          } else {
            int j = 0;
            if(children == null)
              children = new Child[1];
            else {
              j = ComputeIndex(bit, childrenbitmap);
              children = children.Insert(j, new Child());
            }
            children[j].Init2(kv, hash, ent, hashOf(ent.Key));
            entries = entries.Remove(i);
            childrenbitmap = childrenbitmap | bit;
            entriesbitmap = entriesbitmap & ~bit;
          }
        } else {
          if(entries == null)
            entries = new[] { kv };
          else
            entries = entries.Insert(ComputeIndex(bit, entriesbitmap), kv);
          entriesbitmap = entriesbitmap | bit;
        }
      }

      public bool UpdateDissoc(K key, int hash, int depth) {
        //TODO: return value: keep subtree?
        // - actually: should return Opt<KV> - (None -> keep subtree) (KV -> replace subtree with KV)
        // - first things first: move to Common and make it depend on Opt etc...
        return false;
      }

      public void Init2(KeyValuePair<K,V> kv1, int hash1, KeyValuePair<K,V> kv2, int hash2) {
        var bit1 = ComputeBit(hash1);
        var bit2 = ComputeBit(hash2);
        if(bit1 == bit2) {
          childrenbitmap = bit1;
          children = new Child[1];
          children[0].Init2(kv1, hash1, kv2, hash2);
        } else {
          entriesbitmap = bit1 | bit2;
          entries = bit1 < bit2 ? new[] { kv1, kv2 } : new[] { kv2, kv1 };
        }
      }

      public int GetNodeDepth() {
        return depth;
      }

      static readonly int depth = default(Child).GetNodeDepth() - 5; //see GetNodeDepth in interface for explanation

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
    }

    struct Bucket : IHashMap {
      public int GetNodeDepth() {
        return 35; //see interface for explanation -- allows each node type to determine it's depth.
      }

      KeyValuePair<K, V>[] entries;

      public void Init2(KeyValuePair<K,V> kv1, int hash1, KeyValuePair<K,V> kv2, int hash2) {
        entries = new[] { kv1, kv2 };
      }

      public bool TryGetValue(K key, int hash, out V value) {
        foreach(var kv in entries) {
          if(eq(kv.Key, key)) {
            value = kv.Value;
            return true;
          }
        }
        value = default(V);
        return false;
      }

      public void UpdateAssoc(KeyValuePair<K, V> kvNew, int hash) {
        if(entries == null) {
          entries = new []{ kvNew };
          return;
        }

        var len = entries.Length;
        var indexToReplace = -1;
        for(int i = 0; i < len; i++) {
          if(eq(entries[i].Key, kvNew.Key)) {
            indexToReplace = i;
            break;
          }
        }

        entries = indexToReplace < 0 
          ? entries.Insert(0, kvNew) //add ;; also to start: doesn't matter and last inserted might be most likely to be searched for
          : entries.CopyAndSet(indexToReplace, kvNew);
      }
    }

    public struct TrieMap {
      TrieNode<TrieNode<TrieNode<TrieNode<TrieNode<TrieNode<TrieNode<Bucket>>>>>>> trie;

      public bool TryGetValue(K key, out V value) {
        return trie.TryGetValue(key, hashOf(key), out value);
      }

      public TrieMap Assoc(K key, V value) {
        var self = this;

        self.trie.UpdateAssoc(new KeyValuePair<K, V>(key, value), hashOf(key));
        return self;
      }


    }

    public static readonly TrieMap Empty = new TrieMap();
  }

  public struct Test {
    public static void Run() {
      var h = HAMT<int, int>.Empty;


      const int size = 4; //100000
      const int offset = 939997; //9999999 works fine again... ;; 999999 many broken
      // 939999 -- only 2 swapped;; other is: 940031
      //11100101011111011111
      //11100101011111111111

      for(int i = (offset + 0); i < (offset + 10 * size); i++)
        h = h.Assoc(i, i);

      for(int i = (offset + 0); i < (offset + 5 * size); i++) {
        h = h.Assoc(i, -i);
      }

      for(int i = (offset + 0); i < (offset + 10 * size); i++) {
        int v;
        if(!h.TryGetValue(i, out v)) {
          Console.WriteLine("Error: {0} has no value", i);
//
//          if(h.Assoc(i, i).TryGetValue(i, out v)) {
//            Console.WriteLine("Error: {0} after second assoc: {1}", i, v);
//          } else {
//            Console.WriteLine("Error: {0} has no value even after second Assoc", i);
//          }
          continue;
        }
        if((i < (offset + 5 * size) && v != -i) || (i >= (offset + 5 * size) && v != i))
          Console.WriteLine("Error: {0} returned {1}", i, v);
      }

      for(int i = (offset + 1 + 10 * size); i < (offset + 20 * size); i++) {
        int v;
        if(h.TryGetValue(i, out v))
          Console.WriteLine("Error: {0} shouldn't be present; is: {1}", i, v);
      }

      Console.WriteLine("End");
    }

    public  static void Run2() {
      var h = HAMT<int, int>.Empty;

      var last = 31;
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
  }
}
