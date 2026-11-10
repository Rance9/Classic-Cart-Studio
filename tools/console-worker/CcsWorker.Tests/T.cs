// Classic Cart Studio console transport worker tests. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
using System;
using System.Collections.Generic;
using System.Linq;

namespace CcsWorker.Tests
{
    sealed class SkipException : Exception { public SkipException(string m) : base(m) { } }

    static class T
    {
        public static readonly List<Tuple<string, string, Action>> All = new List<Tuple<string, string, Action>>();
        public static void Add(string cat, string name, Action a) => All.Add(Tuple.Create(cat, name, a));
        public static void True(bool c, string what) { if (!c) throw new Exception(what); }
        public static void Eq<V>(V want, V got, string what) { if (!EqualityComparer<V>.Default.Equals(want, got)) throw new Exception(what + ": expected <" + want + "> got <" + got + ">"); }
        public static E Throws<E>(Action a, string what) where E : Exception
        {
            try { a(); } catch (E e) { return e; } catch (Exception e) { throw new Exception(what + ": expected " + typeof(E).Name + ", got " + e.GetType().Name + ": " + e.Message); }
            throw new Exception(what + ": expected " + typeof(E).Name + ", nothing was thrown");
        }
        public static string Code(Action a, string what) => Throws<Ops.OpException>(a, what).Code;
        public static bool Same(byte[] a, byte[] b) => a.Length == b.Length && a.SequenceEqual(b);
    }
}
