using System;

namespace Maa.Data {
  public static class ArrayUtils {
    public static T[] CopyInsertAt<T>(this T[] xs, int i, T x) {
      var tmp = new T[xs.Length + 1];
      Array.Copy(xs, 0, tmp, 0, i);
      tmp[i] = x;
      Array.Copy(xs, i, tmp, i + 1, xs.Length - i);
      return tmp;
    }

    public static T[] CopyRemoveAt<T>(this T[] xs, int i) {
      var n = xs.Length - 1;
      var tmp = new T[n];
      Array.Copy(xs, 0, tmp, 0, i);
      Array.Copy(xs, i + 1, tmp, i, n - i);
      return tmp;
    }

    public static T[] CopySlice<T>(this T[] xs, int i, int len) {
      var tmp = new T[len];
      Array.Copy(xs, i, tmp, 0, len);
      return tmp;
    }

    public static T[] Copy<T>(this T[] xs) {
      return CopySlice(xs, 0, xs.Length);
    }

    public static T[] CopySetAt<T>(this T[] xs, int i, T x) {
      var ret = xs.Copy();
      ret[i] = x;
      return ret;
    }
  }
}
