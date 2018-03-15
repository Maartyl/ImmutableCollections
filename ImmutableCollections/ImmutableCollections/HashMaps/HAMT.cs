using System;
using System.Collections.Generic;

namespace ImmutableCollections.HashMaps {
  struct HAMT<K, V> {
    static readonly Func<K, K, bool> eq = EqualityComparer<K>.Default.Equals;

    // TODO:
    //   optimize copying
    //   remove depth parameter
    //   try inlining bit calculations
    interface IHashMap {
      bool TryGetValue(K key, int hash, int depth, out V value);

      void UpdateAssoc(K key, int hash, int depth, V value);

      void Init2(K key1, V val1, int hash1, K key2, V val2, int hash2, int depth);
    }

    struct TrieNode<Child> : IHashMap where Child : IHashMap, new() {
      int childrenbitmap;
      int entriesbitmap;

      Child[] children;
      KeyValuePair<K, V>[] entries;

      public bool TryGetValue(K key, int hash, int depth, out V value) {
        int bit = ComputeBit(hash, depth);
        if((bit & childrenbitmap) != 0) {
          return children[ComputeIndex(bit, childrenbitmap)].TryGetValue(key, hash, depth + 5, out value);
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

      public void UpdateAssoc(K key, int hash, int depth, V value) {
        int bit = ComputeBit(hash, depth);
        if((bit & childrenbitmap) != 0) {
          //speed is not as important as corectness <- readability

          //var tmp = new Child[children.Length];
          //Array.Copy(children, tmp, children.Length);
          children = children.Copy(); //maa: remove 'inlining' - use ArrayUtils
          children[ComputeIndex(bit, childrenbitmap)].UpdateAssoc(key, hash, depth + 5, value);
        } else if((bit & entriesbitmap) != 0) {
          int i = ComputeIndex(bit, entriesbitmap);
          if(eq(entries[i].Key, key)) {
            //orig // - YUP: I tested it: I noticed right: it was wrong - did mtate original map
            //entries[i] = new KeyValuePair<K,V>(key, value);

            //maa -- update copied structure; but not the original array
            entries = entries.CopyAndSet(i, new KeyValuePair<K,V>(key, value));
          } else {
            int j = 0;
            if(children == null)
              children = new Child[1];
            else {
              j = ComputeIndex(bit, childrenbitmap);
              children = children.Insert(j, new Child());
            }
            children[j].Init2(key, value, hash, entries[i].Key, entries[i].Value, entries[i].Key.GetHashCode(), depth + 5);
            entries = entries.Remove(i);
            childrenbitmap = childrenbitmap | bit;
            entriesbitmap = entriesbitmap & ~bit;
          }
        } else {
          if(entries == null)
            entries = new[] { new KeyValuePair<K, V>(key, value) };
          else
            entries = entries.Insert(ComputeIndex(bit, entriesbitmap), new KeyValuePair<K, V>(key, value));
          entriesbitmap = entriesbitmap | bit;
        }
      }

      public bool UpdateDissoc(K key, int hash, int depth){
        //TODO: return value: keep subtree?
        // - actually: should return Opt<KV> - (None -> keep subtree) (KV -> replace subtree with KV)
        // - first things first: move to Common and make it depend on Opt etc...
        return false;
      }

      public void Init2(K key1, V val1, int hash1, K key2, V val2, int hash2, int depth) {
        var bit1 = ComputeBit(hash1, depth);
        var bit2 = ComputeBit(hash2, depth);
        if(bit1 == bit2) {
          childrenbitmap = bit1;
          children = new Child[1];
          //children[0].Init2(key1, val2, hash1, key2, val2, hash2, depth + 5); -- whow, I thought it would be hard to find the error!
          children[0].Init2(key1, val1, hash1, key2, val2, hash2, depth + 5);
        } else {
          entriesbitmap = bit1 | bit2;
          if(bit1 < bit2)
            entries = new[] { new KeyValuePair<K, V>(key1, val1), new KeyValuePair<K, V>(key2, val2) };
          else
            entries = new[] { new KeyValuePair<K, V>(key2, val2), new KeyValuePair<K, V>(key1, val1) };
        }
      }

      static int ComputeBit(int hash, int depth) {
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
      KeyValuePair<K, V>[] entries;

      //static KeyValuePair<K, V>[] emptyentries = new KeyValuePair<K, V>[0];

      public void Init2(K key1, V val1, int hash1, K key2, V val2, int hash2, int depth) {
        entries = new[] { new KeyValuePair<K, V>(key1, val1), new KeyValuePair<K, V>(key2, val2) };
      }

      public bool TryGetValue(K key, int hash, int depth, out V value) {
        foreach(var kv in entries) {
          if(eq(kv.Key, key)) {
            value = kv.Value;
            return true;
          }
        }
        value = default(V);
        return false;
      }

      public void Update(K key, int hash, int depth, V value) {
        // inefficient: makes double copy
        var list = new List<KeyValuePair<K, V>>();
        foreach(var kv in entries) {
          if(eq(kv.Key, key)) {
            list.Add(new KeyValuePair<K, V>(key, value));
          } else {
            list.Add(kv);
          }
        }
        entries = list.ToArray();
      }


    }

    public struct TrieMap {
      TrieNode<TrieNode<TrieNode<TrieNode<TrieNode<TrieNode<TrieNode<Bucket>>>>>>> trie;

      public bool TryGetValue(K key, out V value) {
        return trie.TryGetValue(key, key.GetHashCode(), 0, out value);
      }

      public TrieMap Assoc(K key, V value) {
        var self = this;
        self.trie.UpdateAssoc(key, key.GetHashCode(), 0, value);
        return self;
      }
    }

    public static TrieMap Empty = new TrieMap();
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

    public  static void Run2(){
      var h = HAMT<int, int>.Empty;
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

    }
  }
}
