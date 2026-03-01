using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CoEnumerable.Tests
{
    [TestClass]
    public class CombineFlawTests
    {
        // A timeout we use to detect deadlocks. If a test hangs longer than this,
        // we treat it as a deadlock rather than waiting forever.
        private static readonly TimeSpan DeadlockTimeout = TimeSpan.FromSeconds(5);

        // -----------------------------------------------------------------------
        // Flaw 1: Deadlock when a coenumerable ignores its IEnumerable<S> argument.
        //
        // If one coenumerable never calls MoveNext() on the proxy enumerable,
        // it never calls Barrier.SignalAndWait(). The other coenumerable blocks
        // in SignalAndWait() forever, and RemoveParticipant() is never called
        // because the ignoring coenumerable's iterator is never enumerated
        // (so its finally block never runs).
        //
        // Expected: Combine completes and returns (42, 15).
        // Actual:   Combine deadlocks.
        // -----------------------------------------------------------------------
        [TestMethod]
        [Timeout(6000)] // MSTest timeout in ms; test fails if it doesn't complete
        public void Flaw1_Deadlock_WhenOneCoenumerableIgnoresItsArgument()
        {
            var nums = Enumerable.Range(1, 5);

            // Run Combine on a separate thread so we can impose a timeout.
            int result1 = 0;
            int result2 = 0;
            bool completed = false;

            var thread = new Thread(() =>
            {
                (result1, result2) = nums.Combine(
                    ns => 42,           // ignores ns entirely — never calls MoveNext
                    ns => ns.Sum(),     // enumerates fully
                    (a, b) => (a, b));
                completed = true;
            });

            thread.IsBackground = true;
            thread.Start();
            bool finished = thread.Join(DeadlockTimeout);

            Assert.IsTrue(finished, "Combine deadlocked: the thread did not complete within the timeout.");
            Assert.IsTrue(completed);
            Assert.AreEqual(42, result1);
            Assert.AreEqual(15, result2);
        }

        // -----------------------------------------------------------------------
        // Flaw 1b: Deadlock when a coenumerable calls GetEnumerator() but never
        // calls MoveNext().
        //
        // The coenumerable receives the IEnumerable<S>, even calls GetEnumerator(),
        // but then discards the enumerator without iterating. Same deadlock.
        // -----------------------------------------------------------------------
        [TestMethod]
        [Timeout(6000)]
        public void Flaw1b_Deadlock_WhenOneCoenumerableCallsGetEnumeratorButNotMoveNext()
        {
            var nums = Enumerable.Range(1, 5);

            bool completed = false;

            var thread = new Thread(() =>
            {
                nums.Combine(
                    ns => { var e = ns.GetEnumerator(); return 99; }, // GetEnumerator but no MoveNext
                    ns => ns.Sum(),
                    (a, b) => (a, b));
                completed = true;
            });

            thread.IsBackground = true;
            thread.Start();
            bool finished = thread.Join(DeadlockTimeout);

            Assert.IsTrue(finished, "Combine deadlocked: GetEnumerator without MoveNext hung the other coenumerable.");
            Assert.IsTrue(completed);
        }

        // -----------------------------------------------------------------------
        // Flaw 2: Thread leak (and effectively a deadlock from the caller's
        // perspective) when one coenumerable throws an exception.
        //
        // If coenumerable1 throws, t1.Result re-throws on the calling thread.
        // t2.Result is never awaited. Thread 2 is blocked in SignalAndWait()
        // waiting for Thread 1 to arrive — but Thread 1 has already exited.
        // Thread 2 is leaked and blocks forever.
        //
        // We verify this by checking that after Combine throws, the second
        // thread is still alive (i.e., leaked/blocked).
        // -----------------------------------------------------------------------
        [TestMethod]
        public void Flaw2_ThreadLeak_WhenOneCoenumerableThrows()
        {
            var nums = Enumerable.Range(1, 1000);

            Thread capturedThread = null;
            var threadStarted = new ManualResetEventSlim(false);

            bool exceptionPropagated = false;
            try
            {
                nums.Combine<int, int, int, object>(
                    ns => throw new InvalidOperationException("deliberate failure"),
                    ns =>
                    {
                        capturedThread = Thread.CurrentThread;
                        threadStarted.Set();
                        return ns.Sum(); // will block in SignalAndWait waiting for thread 1
                    },
                    (a, b) => (a, b));
            }
            catch (Exception)
            {
                exceptionPropagated = true;
            }

            Assert.IsTrue(exceptionPropagated, "Expected exception was not propagated.");

            // Give the leaked thread a moment to reveal itself as blocked.
            threadStarted.Wait(DeadlockTimeout);
            Thread.Sleep(500);

            // The thread should still be alive and blocked, not completed.
            if (capturedThread != null)
            {
                Assert.IsFalse(
                    capturedThread.IsAlive,
                    "Thread 2 was leaked: it is still blocked after coenumerable 1 threw.");
            }
            else
            {
                // If the thread never even started, the exception was thrown before
                // Task.Run had a chance to start t2 — that's a different race but
                // still means t2 was never properly cleaned up.
                Assert.Inconclusive("Thread 2 never started; exception was thrown too early to observe the leak.");
            }
        }

        // -----------------------------------------------------------------------
        // Correctness baseline: verify the implementation works in the normal case,
        // so we know failures in the above tests are genuine flaws and not
        // environment issues.
        // -----------------------------------------------------------------------
        [TestMethod]
        public void Baseline_CorrectResultsInNormalCase()
        {
            var nums = Enumerable.Range(1, 2_000_000);
            (var x, var y) = nums.Combine(
                ns => ns.Any(n => n == 3),
                ns => ns.Take(2).Sum(),
                (a, b) => (a, b));
            Assert.IsTrue(x);
            Assert.AreEqual(3, y);
        }
    }
}
