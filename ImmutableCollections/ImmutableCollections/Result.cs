using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;

namespace Maa.Common {

  /// <summary>
  /// Opt = None | Some(T)
  /// </summary>
  /// <typeparam name="T">Some(T)</typeparam>
  public struct Opt<T> {
    //... I don't like the name Option... It's too long and unintuitive for me; Maybe<T>? R<T>? ===> Opt<T> won
    //heavily inspired by https://github.com/nlkl/Optional & more ; even though I think of it as Maybe, Nothing and Just are... urgh names
    //either Some(T) or None(); None() === default(Opt<T>)
    public readonly bool HasValue; //false by default
    private readonly T value;
    private Opt(T value) {
      this.HasValue = true;
      this.value = value;
    }
    //--- ctors
    public static Opt<T> Some(T val) { return new Opt<T>(val); }
    public static Opt<T> None { get { return new Opt<T>(); } }

    //--- access & methods
    public bool IsNone { get { return !HasValue; } }
    public T ValueOrDefault { get { return value; } }
    public T ValueOr(T dflt) { return HasValue ? value : dflt; }
    public T ValueOr(Func<T> dflt) {
      if (null == dflt) throw new ArgumentNullException("dflt");
      return HasValue ? value : dflt();
    }
    public TRet Match<TRet>(Func<T, TRet> some, Func<TRet> none) {
      //ugh... I forgot I don't even have named arguments... oh well...
      // - well, I /am/ using a 10 years old compiler... It's a miracle it works at all... and hell that I need it.
      if (some == null) throw new ArgumentNullException("some");
      if (none == null) throw new ArgumentNullException("none");
      return HasValue ? some(value) : none();
    }
    public Opt<T> Match(Action<T> some, Action none) {
      if (some == null) throw new ArgumentNullException("some");
      if (none == null) throw new ArgumentNullException("none");
      if (HasValue) some(value); else none();
      return this;
    }
    public Opt<TRet> Map<TRet>(Func<T, TRet> mapper) {
      if (mapper == null) throw new ArgumentNullException("mapper");
      return HasValue ? Opt<TRet>.Some(mapper(value)) : Opt.NoneAny;
    }
    public Opt<TRet> Bind<TRet>(Func<T, Opt<TRet>> step) {
      if (step == null) throw new ArgumentNullException("step");
      return HasValue ? step(value) : Opt.NoneAny;
    }

    public Opt<TRet> CastOrNone<TRet>() {
      return Bind(v => (v is TRet) ? ((TRet)(object)v).Some() : Opt.NoneAny);
    }

    public override bool Equals(object obj) {
      if (obj is Opt<T>) {
        var opt2 = (Opt<T>)obj;
        if (HasValue != opt2.HasValue) return false;
        return !HasValue ? false : EqualityComparer<T>.Default.Equals(value, opt2.value);
      } else return false;
    }
    public override int GetHashCode() {
      return HasValue ? EqualityComparer<T>.Default.GetHashCode(value) : -1;
    }
    public override string ToString() {
      if (IsNone) return "None";
      return string.Format("Some({0})", (((object)value) ?? "null").ToString()); //need to box to check ((T:class) != null)
    }

    public IEnumerable<T> ToEnumerable() { if (HasValue) yield return value; }

    //---operators
    public static implicit operator Opt<T>(T val) {
      return Some(val);
    }
    public static implicit operator Opt<T>(Opt.NoneCastImplicit none) {
      return None;
    }
    public static T operator |(Opt<T> opt, T dflt) {
      return opt.ValueOr(dflt);
    }
    public static Opt<T> operator |(Opt<T> opt, Opt<T> Else) {
      return opt.HasValue ? opt : Else;
    }
    public static Opt<T> operator &(Opt<T> opt, Func<T, T> mapper) {
      return opt.Map(mapper);
    }

    public static bool operator true(Opt<T> opt) {
      return opt.HasValue;
    }
    public static bool operator false(Opt<T> opt) {
      return opt.IsNone;
    }
    /// <summary>
    /// Some(T) -> None
    /// None -> Some(default(T))
    /// -- flips HasValue; mostly to be used in IF statements
    /// </summary>
    /// <param name="opt"></param>
    /// <returns></returns>
    public static Opt<T> operator !(Opt<T> opt) {
      return opt.HasValue ? None : Some(default(T));
    }
  }
  public static class Opt {
    public struct NoneCastImplicit { }
    public static readonly NoneCastImplicit NoneAny = default(NoneCastImplicit);

    public static Opt<T> Some<T>(this T val) { return Opt<T>.Some(val); }
    public static Opt<T> None<T>() { return NoneAny; }
    public static Opt<T> None<T>(this T typeHint) { return NoneAny; }

    public static Opt<T> SomeNotNull<T>(this T val) where T : class {
      return val == null ? NoneAny : val.Some();
    }
    public static Opt<T> NotNull<T>(this Opt<T> opt) where T : class {
      return opt.Bind<T>(SomeNotNull);
    }

    public static Opt<T> ToOption<T>(this Nullable<T> n) where T : struct {
      return n.HasValue ? n.Value.Some() : NoneAny;
    }
    public static Opt<T> SomeWhen<T>(this T val, Func<T, bool> pred) {
      if (pred == null) throw new ArgumentNullException("pred");
      return pred(val) ? val.Some() : NoneAny;
    }
    public static Opt<T> NoneWhen<T>(this T val, Func<T, bool> pred) {
      if (pred == null) throw new ArgumentNullException("pred");
      return pred(val) ? NoneAny : val.Some();
    }
  }
  public static class R {
    #region Tuple
    public static R<T1, T2> Of<T1, T2>(T1 v1, T2 v2) { return new R<T1, T2>(v1, v2); }
    public static R<T1, T2, T3> Of<T1, T2, T3>(T1 v1, T2 v2, T3 v3) { return new R<T1, T2, T3>(v1, v2, v3); }

    public static R<T1, T2> RPack<T1, T2>(this T1 v1, T2 v2) { return Of(v1, v2); }
    public static R<T1, T2, T3> RPack<T1, T2, T3>(this T1 v1, T2 v2, T3 v3) { return Of(v1, v2, v3); }

    public static TR Unpack<T1, T2, TR>(this R<T1, T2> r, Func<T1, T2, TR> f) {
      return f(r.V1, r.V2);
    }
    public static TR Unpack<T1, T2, T3, TR>(this R<T1, T2, T3> r, Func<T1, T2, T3, TR> f) {
      return f(r.V1, r.V2, r.V3);
    }

    public static TR Unpack<T1, T2, TR>(this Opt<R<T1, T2>> rOpt, Func<T1, T2, TR> some, Func<TR> none) {
      return rOpt.Match(r => r.Unpack(some), none);
    }
    public static TR Unpack<T1, T2, T3, TR>(this Opt<R<T1, T2, T3>> rOpt, Func<T1, T2, T3, TR> some, Func<TR> none) {
      return rOpt.Match(r => r.Unpack(some), none);
    }

//    public static Nil Unpack<T1, T2>(this R<T1, T2> r, Action<T1, T2> f) {
//      return r.Unpack(f.AsF());
//    }
//    public static Nil Unpack<T1, T2, T3>(this R<T1, T2, T3> r, Action<T1, T2, T3> f) {
//      return r.Unpack(f.AsF());
//    }
    #endregion

    #region Result == Either<T, Exception>
    public static TV Unwrap<TV, TE>(this Result<TV, TE> r, Func<TE, Exception> thrower) {
      if (r.IsOk)
        return r.Value;
      else throw thrower(r.Error);
    }
    public static TV Unwrap<TV, TE>(this Result<TV, TE> r) where TE : Exception {
      return r.Unwrap(x => x);
    }
    #endregion
  }

  //Trampoline; + Lazy with no cashing -- allows to 'simulate' tail call optimization
  public struct Tail<T> {
    private readonly T value;
    private readonly Func<Tail<T>> continuation;
    private Tail(T value, Func<Tail<T>> continuation) {
      this.value = value;
      this.continuation = continuation;
    }
    public T Value { get { return ValueTail.value; } }
    public Tail<T> ValueTail {
      get {
        var lazy = this;
        while (lazy.continuation != null)
          lazy = lazy.continuation();
        return lazy;
      }
    }
    public static implicit operator Tail<T>(T value){
      return new Tail<T>(value, null);
    }
    public static Tail<T> Of(Func<Tail<T>> continuation) {
      return new Tail<T>(default(T), continuation);
    }
  }
  public static class Tail {
    public static Tail<T> Of<T>(T val) {
      return val;
    }
    public static Tail<T> Of<T>(Func<T> cont) {
      return Of(()=>cont());
    }
    public static Tail<T> Of<T>(Func<Tail<T>> cont) {
      return Tail<T>.Of(cont);
    }
  }

  public struct Result<TV, TE> {
    private readonly bool ok;
    private readonly TV val;
    private readonly TE err;
    public Result (TV v) {
      ok = true;
      val = v;
      err = default(TE);
    }
    ///caught exceptions should be wrapped (in ex.Wrap())
    ///.Unwrap only unwraps Result; not the stored wrapped exception
    public Result(TE e) {
      ok = false;
      val = default(TV);
      err = e;
    }

    public bool IsOk { get { return ok; } }
    public TV Value { get { return val; } } //maybe: throw if undefined?
    public TE Error { get { return err; } } //maybe: throw if undefined?

    public static implicit operator Result<TV, TE>(TV v) {
      return new Result<TV, TE>(v);
    }
    public static implicit operator Result<TV, TE>(TE e) {
      return new Result<TV, TE>(e);
    }
  }

  //T already commonly used for generic types
  ///(mainly meant to) pack 2 return values; (tuple)
  /// it's a simple immutable _structure_ - beware of boxing & lots of copying
  public struct R<T1, T2> {
    public readonly T1 V1;
    public readonly T2 V2;
    public R(T1 v1, T2 v2) { V1 = v1; V2 = v2; }
  }
  ///(mainly meant to) pack 3 return values; (tuple)
  /// it's a simple immutable _structure_ - beware of boxing & lots of copying
  public struct R<T1, T2, T3> {
    public readonly T1 V1;
    public readonly T2 V2;
    public readonly T3 V3;
    public R(T1 v1, T2 v2, T3 v3) { V1 = v1; V2 = v2; V3 = v3; }
  }

  //R<5 fields> should no longer be a struct;;; 4 should still be ok
}
