using System;
using System.Diagnostics;
using Maa.Data;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace ImmutableCollections {
  public class Benchmark {
    public static void Time(string name, Action f) {
      //if (name == "Collections.ImmutableList") return;
      //if (name == "FixedVector") return;
      //if (name == "Collections.ImmutableDictionary") return;
//      if(name != "MergeVector")
//        return;
      f(); // warmup: let the CLR genererate code for generics, get caches hot, etc.
      GC.GetTotalMemory(true);
      var watch = Stopwatch.StartNew();
      for(int i = 0; i < 10; i++) {
        f();
      }
      watch.Stop();
      Console.WriteLine("{0}: {1} ms", name, watch.ElapsedMilliseconds);
    }

    public static void Memory(string name, Action f) {
      var initial = GC.GetTotalMemory(true);
      f();
      var final = GC.GetTotalMemory(true);
      Console.WriteLine("{0}: {1} bytes", name, final - initial);
    }
  }

  public class MainClass {
    public static void Main() {



      //Console.WriteLine("start"); ;

      //for (int j = 0; j < 10; j++)
      //{
      //    var hamt = HashMaps.HAMT<int, int>.Empty;
      //    for (int i = 0; i < 1000000; i++) hamt = hamt.Set(i, i);

      //    int v = 0;
      //    for (int i = 0; i < 1000000; i++) hamt.TryGetValue(i, out v);

      //    Console.WriteLine(v);
      //}

      //Console.WriteLine("done");

      //Console.Write(true);
      /*
            Console.WriteLine("=== STACKS ===");
            Stacks.Benchmarks.Run();

            Console.WriteLine("=== QUEUES ===");
            Queues.Benchmarks.Run();
            */
      Console.WriteLine("=== VECTORS ===");
      Vectors.Benchmarks.Run();
            
           
      Console.WriteLine("=== SORTEDMAPS ===");
      SortedMaps.Benchmarks.Run();
      Console.Read();
            



      //HashMaps.Test.Run();

      /*
            var v = Vectors.ResizeVector<int>.Empty;

            for (int i = 0; i < 100000; i++)
            {
                v = v.Add(i);
            }

            Console.WriteLine(v.Lookup(5000));

            v = v.Set(5000, 42);

            Console.WriteLine(v.Lookup(5000));
            */


      Console.Read();
    }
  }

  public class MainMaa {
    static void simpleHAMT() {
      
      var time = DateTime.Now;
      //Maa.Data.HamtTest.Run();
      Maa.Data.HamtTest.Run2();
      HamtTest.Dissoc(1);
      Console.WriteLine((DateTime.Now - time).TotalMilliseconds);

      Console.WriteLine("-------------------------------------");

      time = DateTime.Now;
      //Maa.Data.HamtTest.Run();
      //Maa.Data.HamtTest.Run2();
      //HamtTest.Dissoc(1);
      HamtTest.Inspect();
      Console.WriteLine((DateTime.Now - time).TotalMilliseconds);
    }

    public static void Main() {
      //simpleHAMT(); 

      perfHAMT();

      Console.WriteLine("round 2");

      perfHAMT();

      //MainClass.Main(); - broken bench
    }

    static void perfHAMT(){
      //var h = default(HAMT<int,int>);
      //var d = new Dictionary<int,int>();

      const int offset = int.MinValue;
      const int step = 1101033;
      const int size = 1000*step;
      const int times = 100;
      const int timesGet = times * 10;

      Benchmark.Time("DICT assoc", () => {
        for (int j = 0; j < times; j++) {
          var dict = new Dictionary<int,int>();
          for (int i = offset; i < offset+size; i+=step) {
            dict[i] =i;
          }
        }
      });
      Benchmark.Time("HAMT assoc", () => {
        for (int j = 0; j < times; j++) {
          var hamt = default(HAMT<int,int>);
          for (int i = offset; i < offset+size; i+=step) {
            hamt = hamt[i,i];
          }
        }
      });
      Benchmark.Time("IMUD assoc", () => {
        for (int j = 0; j < times; j++) {
          var imud = ImmutableDictionary<int, int>.Empty;
          for (int i = offset; i < offset+size; i+=step) {
            imud = imud.SetItem(i, i);
          }
        }
      });

      var dictFull = new Dictionary<int,int>();
      var hamtFull = default(HAMT<int,int>);
      var imudFull = ImmutableDictionary<int, int>.Empty;
      for(int i = offset; i < offset+size; i+=step) {
        dictFull[i] =i;
        hamtFull = hamtFull[i,i];
        imudFull = imudFull.SetItem(i, i);
      }

      Benchmark.Time("DICT get", () => {
        for (int j = 0; j < timesGet; j++) {
          for (int i = offset; i < offset+size; i+=step) {
            int x;
            dictFull.TryGetValue(i, out x);
          }
        }
      });
      Benchmark.Time("HAMT get", () => {
        for (int j = 0; j < timesGet; j++) {
          for (int i = offset; i < offset+size; i+=step) {
            int x;
            hamtFull.TryGetValue(i, out x);
          }
        }
      });
      Benchmark.Time("IMUD get", () => {
        for (int j = 0; j < timesGet; j++) {
          for (int i = offset; i < offset+size; i+=step) {
            int x;
            imudFull.TryGetValue(i, out x);
          }
        }
      });
    }
    
  }
}
