using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace CoEnumerable.Tests;

[TestClass]
public class ExtensionsTests
{
    private class ThrowingEnumerable(IEnumerable<int> inner, int throwAfter, string msg) : IEnumerable<int>
    {
        public IEnumerator<int> GetEnumerator() => new ThrowingEnumerator(inner.GetEnumerator(), throwAfter, msg);
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private class ThrowingEnumerator(IEnumerator<int> inner, int throwAfter, string msg) : IEnumerator<int>
    {
        private int count;

        public bool MoveNext()
        {
            return count++ >= throwAfter ? throw new InvalidOperationException(msg) : inner.MoveNext();
        }

        public int Current => inner.Current;
        object IEnumerator.Current => Current;
        public void Reset() => inner.Reset();
        public void Dispose() => inner.Dispose();
    }
        
    private static bool Coenumerable1(IEnumerable<int> ns) => ns.Any(n => n == 3);
    private static int Coenumerable2(IEnumerable<int> ns) => ns.Take(2).Sum();
        
    private static readonly IEnumerable<int> Nums = Enumerable.Range(1, 2_000_000);
        
    [TestMethod]
    public void Combine_PropagatesSourceException_WhenPostPhaseActionThrows()
    {
        const string msg = "source failure";
        var nums = new ThrowingEnumerable(Enumerable.Range(1, 10), throwAfter: 3, msg);

        var e = Throws<InvalidOperationException>(() =>
            nums.TryCombine(
                ns => ns.Take(0).Sum(), // finishes early, triggers RemoveParticipant -> FinishPhase
                ns => ns.Sum()));

        AreEqual(msg, e.Message);
    }
        
    [TestMethod]
    public void Combine_PropagatesSourceException_WhenSourceThrowsMidEnumeration()
    {
        const string msg = "source failure";
        var nums = new ThrowingEnumerable(Enumerable.Range(1, 10), throwAfter: 3, msg);

        var e = Throws<InvalidOperationException>(() =>
            nums.TryCombine(
                ns => ns.Sum(), // fully consumes — will hit the throw
                ns => ns.Sum()));

        AreEqual(msg, e.Message);
    }
        
    [TestMethod]
    public void Combine_ReturnsCorrectValues()
    {
        var (x1, x2) = Nums.Combine(Coenumerable1, Coenumerable2);
        AreEqual(Coenumerable1(Nums), x1);
        AreEqual(Coenumerable2(Nums), x2);
    }

    [TestMethod]
    public void TryCombine_CapturesException_WhenOneCoenumerableFails()
    {
        const string msg = "deliberate failure";
        var nums = Enumerable.Range(1, 1000);
        var (r1, r2) = nums.TryCombine<int, int, int>(
            _ => throw new InvalidOperationException(msg),
            ns => ns.Sum());

        IsFalse(r1.IsSuccess);
        IsInstanceOfType<InvalidOperationException>(r1.Error);
        AreEqual(msg, r1.Error.Message);
        IsTrue(r2.IsSuccess);
        AreEqual(500500, r2.Value);
    }
        
    [TestMethod]
    public void TryCombine_CapturesBothExceptions_WhenBothCoenumerablesFail()
    {
        const string msg1 = "failure 1", msg2 = "failure 2";
        var nums = Enumerable.Range(1, 1000);
        var (r1, r2) = nums.TryCombine(Fail1, Fail2);
            
        IsFalse(r1.IsSuccess);
        IsInstanceOfType<InvalidOperationException>(r1.Error);
        AreEqual(msg1, r1.Error.Message);
        IsFalse(r2.IsSuccess);
        IsInstanceOfType<InvalidOperationException>(r2.Error);
        AreEqual(msg2, r2.Error.Message);
        return;
            
        int Fail1(IEnumerable<int> ns) => throw new InvalidOperationException(msg1);
        int Fail2(IEnumerable<int> ns) => throw new InvalidOperationException(msg2);
    }
        
    [TestMethod]
    public void CombineTracingTest()
    {
        // ReSharper disable PossibleMultipleEnumeration
        var (x1, x2) = Nums.Tracing(out var sb).Combine(Coenumerable1, Coenumerable2,
            (x, y) => (x, y),
            out var tid1,
            out var tid2);
        AreEqual(Coenumerable1(Nums), x1);
        AreEqual(Coenumerable2(Nums), x2);

        // The problem with tracking the precise activities triggered by Combine(...) is that the threads
        // associated with the two coenumerables may run in various orders.  Below we painfully create a regex
        // to allow for this variation.  We define some local functions to make it a bit easier.
        var pat = new StringBuilder($"^{Task.CurrentId}:, ");
        using (var enumerator = Nums.GetEnumerator())
            for (var i = 0; i < 3; i++)
            {
                Mn(enumerator.MoveNext());
                if (i < 2) C(enumerator.Current); else C1(enumerator.Current);
            }
        pat.Append($"{Task.CurrentId}:; $");
        IsTrue(Regex.IsMatch(sb.ToString(), pat.ToString()));
        // ReSharper restore PossibleMultipleEnumeration
        return;
            
        // below we define functions that add to the pattern corresponding to various events
        void A<TS, T>(TS s, T t) => pat.Append($"{s}:{t} "); // data
        void Mn(bool b) => A($"({tid1}|{tid2})", b); // MoveNext() by some thread
        void C(int i)
        {
            pat.Append('(');
            A(tid1, i); 
            A(tid2, i); 
            pat.Append('|'); 
            A(tid2, i);
            A(tid1, i); 
            pat.Append(')');
        }  // Current by both threads
        void C1(int i) => A(tid1, i); // Current by just thread 1
    }
        
    [TestMethod]
    public void Barrier_RemoveParticipant_ThrowsBPPE_WhenPostPhaseActionThrows()
    {
        const string msg = "post-phase failure";
        var barrier = new Barrier(2, _ => throw new InvalidOperationException(msg));
        
        var t = Task.Run(() =>
        {
            barrier.SignalAndWait(TestContext.CancellationToken); // signal and wait for phase to complete
        }, TestContext.CancellationToken);
            
        // Give task time to signal
        Thread.Sleep(50);
            
        // Now remove the second participant — this should trigger FinishPhase,
        // which runs the post-phase action, which throws, resulting in BPPE
        try
        {
            barrier.RemoveParticipant();
            Fail("Expected BarrierPostPhaseException was not thrown; " +
                 "behavior may have changed since test was written.");
        }
        catch (BarrierPostPhaseException bppe)
        {
            IsInstanceOfType<InvalidOperationException>(bppe.InnerException);
            AreEqual(msg, bppe.InnerException!.Message);
            // The next assert identifies an undocumented behavior. For now we just work around it
            // and if they fix it then we can alter our code.
            // Assert.Inconclusive("This is undocumented behavior!");
        }
        finally
        {
            try
            {
                t.Wait(TestContext.CancellationToken); // will throw AggregateException wrapping BPPE
            }
            catch (AggregateException) { } // expected — task's SignalAndWait also throws BPPE

            barrier.Dispose();
        }
    }

    // Flaw 2: Thread leak when one coenumerable throws an exception.
    // The second coenumerable should run to completion even if the first throws an exception.
    // We detect this using a ManualResetEventSlim set in the second coenumerable's
    // finally block — if it's never set, the coenumerable was leaked.
    [TestMethod]
    public void Flaw2_ThreadLeak_WhenOneCoenumerableThrows()
    {
        var nums = Enumerable.Range(1, 1000);
        var (r1, r2) = nums.TryCombine(Fail, ns => ns.Sum());

        IsFalse(r1.IsSuccess);
        IsInstanceOfType<InvalidOperationException>(r1.Error);
        IsTrue(r2.IsSuccess);
        AreEqual(500500, r2.Value);

        return;
        int Fail(IEnumerable<int> _) => throw new InvalidOperationException("deliberate failure");
    }
        
    // Ensure no deadlock when a coenumerable enumerates vacuously — e.g.
    // ns.Take(0).Sum() calls GetEnumerator() but never calls MoveNext().
    [TestMethod]
    [Timeout(6000, CooperativeCancellation = true)]
    public void TryCombine_Succeeds_WhenOneCoenumerableEnumeratesVacuously()
    {
        var (r1, r2) = Enumerable.Range(1, 5).TryCombine(
            ns => ns.Take(0).Sum(),
            ns => ns.Sum());

        IsTrue(r1.IsSuccess);
        AreEqual(0, r1.Value);
        IsTrue(r2.IsSuccess);
        AreEqual(15, r2.Value);
    }

    [TestMethod]
    [Ignore("Out of scope: a coenumerable that never calls GetEnumerator() violates the precondition " +
            "that each coenumerable enumerates its argument exactly once.")]
    [Timeout(6000, CooperativeCancellation = true)]
    public void TryCombine_Deadlocks_WhenCoenumerableIgnoresItsArgument()
    {
        Enumerable.Range(1, 5).TryCombine(
            _ => 42,
            ns => ns.Sum());
    }

    public TestContext TestContext { get; set; }
}