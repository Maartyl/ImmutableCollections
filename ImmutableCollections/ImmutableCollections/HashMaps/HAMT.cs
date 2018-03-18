using System;
using System.Collections.Generic;
using Maa.Common;

namespace Maa.Data {

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
    interface IHashTrieNode {
      bool TryGetValue(K key, int hash, out V value);

      void UpdateAssoc(KeyValuePair<K,V> kv, int hash);

      //returns: none -> keep child; KV -> replace child with entry (only 1 entry in subtree left)
      // - if KV: you must replace: cannot assume it's updated to only hold one
      Opt<KeyValuePair<K,V>> UpdateDissoc(K k, int hash);

      void Init2(KeyValuePair<K,V> kv1, int hash1, KeyValuePair<K,V> kv2, int hash2);

      IEnumerable<KeyValuePair<K,V>> Enumerate();


      //TrieNode<TrieNode<TrieNode<TrieNode<TrieNode<TrieNode<TrieNode<Bucket>>>>>>> trie;
      // - originally: top started with 0, then each additional added 5
      // - if I put 35 to Bucket: then each in static ctor subtracts 5 -> all will know their depth.
      // - this should just return the static field (that coulnd't be directly part of interface...)
      int GetNodeDepth();

      void DebugInspectPrint();
    }

    struct TrieNode<Child> : IHashTrieNode where Child : IHashTrieNode, new() {
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
            //orig // - YUP: I tested it: I noticed right: it was wrong - did mutate original map
            //entries[i] = new KeyValuePair<K,V>(key, value);
            entries = entries.CopySetAt(i, kv);
          } else {
            int j = 0;
            if(children == null)
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
          entriesbitmap |= bit; //why is this no different? what is correct? ...
          // - must match index I'm looking for, so...
          // OOH! it deoes NOT matter: only computes from bits LOWER than bit, so if bit set or not doesn't matter...
          // - whow, that's ... pretty cool, actually...
          if(entries == null)
            entries = new[] { kv };
          else
            entries = entries.CopyInsertAt(ComputeIndex(bit, entriesbitmap), kv);
          //entriesbitmap |= bit;
        }
      }

      public Opt<KeyValuePair<K,V>> UpdateDissoc(K key, int hash) {
        int bit = ComputeBit(hash);
        if((bit & childrenbitmap) != 0) {
          var i = ComputeIndex(bit, childrenbitmap);
          var tmp = children[i];
          var replace = tmp.UpdateDissoc(key, hash);
          if(replace) {
            if(bit == childrenbitmap && entriesbitmap == 0)
              return replace; //was sole child and no entries: replace myself
            else {
              //collapse of child into entry

              if(bit == childrenbitmap) {
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
            //keep tmp as child
            children = children.CopySetAt(i, tmp);
            return Opt.NoneAny; //keep me
          }
        } else if((bit & entriesbitmap) != 0) {
          var i = ComputeIndex(bit, entriesbitmap);
          var kv = entries[i];
          if(eq(kv.Key, key)) {
            if(bit != entriesbitmap) { //common case: multiple entries, just remove
              entriesbitmap &= ~bit;
              entries = entries.CopyRemoveAt(i);
              return Opt.NoneAny; //keep me
            } else { //is sole entry
              if(childrenbitmap == 0) { //no children
                return kv; //replace myself
              } else {
                entriesbitmap = 0;
                entries = null;
                return Opt.NoneAny; //keep me
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

      public void Init2(KeyValuePair<K,V> kv1, int hash1, KeyValuePair<K,V> kv2, int hash2) {
        var bit1 = ComputeBit(hash1);
        var bit2 = ComputeBit(hash2);
        if(bit1 == bit2) {
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

      public IEnumerable<KeyValuePair<K,V>> Enumerate() {
        if(entries != null)
          foreach(var e in entries)
            yield return e;
  
        if(children != null) //there's surely a faster way to do this, but this one is fine for now
          foreach(var c in children)
            foreach(var e in c.Enumerate())
              yield return e;
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
        if(s == null)
          s = "";
        
        var indent = new string(' ', depth / 5);

        Console.Write(indent);
        Console.WriteLine(s.Replace("\n", "\n" + indent));
      }

      public void DebugInspectPrint() {
        if(entriesbitmap != 0) {
          dbgLn("entries " + dbgHash(entriesbitmap));
        } else {
          dbgLn("entries none");
        }

        if(entries != null)
          foreach(var e in entries)
            dbgLn("." + dbgHash(hashOf(e.Key)) + " ; " + e.Key);

        if(childrenbitmap != 0)
          dbgLn("children " + dbgHash(childrenbitmap));
        else
          dbgLn("children none");

        if(children != null)
          for(int i = 0; i < 32; i++) {
            var bit = (1 << i);
            if((bit & childrenbitmap) != 0) {
              dbgLn("child " + binI5(i));
              children[ComputeIndex(bit, childrenbitmap)].DebugInspectPrint();
            }
          }
      }

      #endregion
    }

    struct Bucket : IHashTrieNode {
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
        if(entries == null) { //not possible inside tree; only when Bucket used for small maps
          entries = new []{ kvNew };
          return;
        }
          
        var indexToReplace = findIndex(kvNew.Key);

        entries = indexToReplace < 0 
          ? entries.CopyInsertAt(0, kvNew) //add ;; also to start: doesn't matter and last inserted might be most likely to be searched for
          : entries.CopySetAt(indexToReplace, kvNew);
      }

      public Opt<KeyValuePair<K,V>> UpdateDissoc(K k, int hash) {
        if(entries == null)
          return Opt.NoneAny; //not possible inside tree; only when Bucket used for small maps

        var indexToRemove = findIndex(k);

        if(indexToRemove < 0)
          return Opt.NoneAny; //not found: keep me as I am

        if(entries.Length == 2)
          return entries[indexToRemove == 0 ? 1 : 0]; //return other than removed to replace self with

        if(entries.Length == 1) { //not possible inside tree; only when Bucket used for small maps
          entries = null; //was single: removed to null
          return Opt.NoneAny;
        }

        entries = entries.CopyRemoveAt(indexToRemove);
        return Opt.NoneAny;
      }

      int findIndex(K k) {
        //ASSUMPTION: entries != null

        var len = entries.Length;
        for(int i = 0; i < len; i++)
          if(eq(entries[i].Key, k))
            return i;

        return -1;
      }

      public IEnumerable<KeyValuePair<K,V>> Enumerate() {
        return entries ?? System.Linq.Enumerable.Empty<KeyValuePair<K,V>>();
      }

      public void DebugInspectPrint() {
        var len = 0;
        if(entries != null)
          len = entries.Length;
        
        dbgLn("bucket #" + len); 

        foreach(var e in entries)
          dbgLn("" + e.Key);
      }

      static void dbgLn(string s) {
        if(s == null)
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
      if(bin.Length >= 5)
        return bin;
      return new string('0', 5 - bin.Length) + bin;
    }

    static HAMT() {
      //point of this is partially for safety, but mainly to assure it gets evaluated before anything else:
      // - thus initialization not checked during actual algorithm
      // -- I don't think it would, but adding this does not hurt
      if(Empty.RootNodeDepth != 0)
        throw new InvalidProgramException("TrieMap top level NodeDepth must be 0; is: " + Empty.RootNodeDepth);
    }

    public struct TrieMap {
      internal int RootNodeDepth{ get { return trie.GetNodeDepth(); } }

      TrieNode<TrieNode<TrieNode<TrieNode<TrieNode<TrieNode<TrieNode<Bucket>>>>>>> trie;

      public bool TryGetValue(K key, out V value) {
        return trie.TryGetValue(key, hashOf(key), out value);
      }

      public Opt<V> ValueAt(K key) {
        V v;
        return TryGetValue(key, out v) ? v.Some() : Opt.NoneAny;
      }

      public TrieMap Assoc(K key, V value) {
        var self = this;

        self.trie.UpdateAssoc(new KeyValuePair<K, V>(key, value), hashOf(key));
        return self;
      }

      public TrieMap Dissoc(K key) {
        var self = this;

        var h = hashOf(key);
        var replace = self.trie.UpdateDissoc(key, h);
        if(replace) { //only top node can contain single entery - tried to replace itself, but no higher level
          self = Empty;
          self.trie.UpdateAssoc(replace.ValueOrDefault, h);
        }

        return self;
      }

      public void DebugInspectPrint() {
        trie.DebugInspectPrint();
      }
    }

    public static readonly TrieMap Empty = new TrieMap();
  }

  public struct Test {
    public static void Run() {
      var h = HAMT<int, int>.Empty;


      const int size = 40; //100000
      // const int offset = 939997; //9999999 works fine again... ;; 999999 many broken
      const int offset = 941000; //9999999 works fine again... ;; 999999 many broken
      // 939999 -- only 2 swapped;; other is: 940031
      //11100101011111011111
      //11100101011111111111

      for(int i = (offset + 0); i < (offset + 10 * size); i++) {
        h = h.Assoc(i, i);

        int v;
        if(!h.TryGetValue(i, out v)) {
          Console.WriteLine("E1: {0} has no value", i);

          h.DebugInspectPrint();

          var hx = h.Assoc(i, i);

          Console.WriteLine("--------------e: Second of {0}------------", i);

          hx.DebugInspectPrint();

          if(hx.TryGetValue(i, out v)) {
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

      for(int i = (offset + 0); i < (offset + 5 * size); i++) {
        h = h.Assoc(i, -i);
      }

      for(int i = (offset + 0); i < (offset + 10 * size); i++) {
        int v;
        if(!h.TryGetValue(i, out v)) {
          Console.WriteLine("Error: {0} has no value", i);

          if(h.Assoc(i, i).TryGetValue(i, out v)) {
            Console.WriteLine("Error: {0} after second assoc: {1}", i, v);
          } else {
            Console.WriteLine("Error: {0} has no value even after second Assoc", i);
          }
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
  }
}
