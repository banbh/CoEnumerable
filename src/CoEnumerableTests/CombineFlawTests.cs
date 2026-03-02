using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CoEnumerable.Tests
{
    [TestClass]
    public class CombineFlawTests
    {
        private static readonly TimeSpan DeadlockTimeout = TimeSpan.FromSeconds(5);

        // -----------------------------------------------------------------------
        // Flaw 1: Deadlock when a coenumerable ignores its IEnumerable<S> argument.
        // Out of scope: violates the precondition that each coenumerable must call
        // GetEnumerator() exactly once.
        // -----------------------------------------------------------------------
        [TestMethod]
        [Ignore("Out of scope: a coenumerable that never calls GetEnumerator() violates the precondition " +
                "that each coenumerable enumerates its argument exactly once.")]
        public void Flaw1_Deadlock_WhenOneCoenumerableIgnoresItsArgument()
        {
            var nums = Enumerable.Range(1, 5);

            bool completed = false;
            var thread = new Thread(() =>
            {
                nums.Combine(
                    ns => 42,
                    ns => ns.Sum(),
                    (a, b) => (a, b));
                completed = true;
            });

            thread.IsBackground = true;
            thread.Start();
            bool finished = thread.Join(DeadlockTimeout);

            Assert.IsTrue(finished, "Combine deadlocked: the thread did not complete within the timeout.");
            Assert.IsTrue(completed);
        }

        // -----------------------------------------------------------------------
        // Flaw 1b: Deadlock when a coenumerable calls GetEnumerator() but never
        // calls MoveNext() — e.g. ns.Take(0).Sum().
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
                    ns => { using var e = ns.GetEnumerator(); return 99; },
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
        // Flaw 2: Thread leak when one coenumerable throws an exception.
        // The second coenumerable should run to completion even if the first throws.
        // We detect this using a ManualResetEventSlim set in the second coenumerable's
        // finally block — if it's never set, the coenumerable was leaked.
        // -----------------------------------------------------------------------
        [TestMethod]
        public void Flaw2_ThreadLeak_WhenOneCoenumerableThrows()
        {
            var nums = Enumerable.Range(1, 1000);
            var thread2Finished = new ManualResetEventSlim(false);

            bool exceptionPropagated = false;
            try
            {
                nums.Combine<int, int, int, (int, int)>(
                    ns => throw new InvalidOperationException("deliberate failure"),
                    ns =>
                    {
                        try   { return ns.Sum(); }
                        finally { thread2Finished.Set(); }
                    },
                    (a, b) => (a, b));
            }
            catch (Exception)
            {
                exceptionPropagated = true;
            }

            Assert.IsTrue(exceptionPropagated, "Expected exception was not propagated.");

            bool finished = thread2Finished.Wait(DeadlockTimeout);
            Assert.IsTrue(finished,
                "Thread 2 was leaked: it did not finish within the timeout after coenumerable 1 threw.");
        }

        // -----------------------------------------------------------------------
        // Sanity check: thread pool threads are always alive, confirming that
        // IsAlive is not a reliable signal for task completion.
        // -----------------------------------------------------------------------
        [TestMethod]
        public void ThreadPoolThreadsAreAlwaysAlive()
        {
            Thread capturedThread = null;
            var done = new ManualResetEventSlim(false);

            Task.Run(() =>
            {
                capturedThread = Thread.CurrentThread;
                done.Set();
            }).Wait();

            done.Wait();
            Thread.Sleep(500);

            Assert.IsTrue(capturedThread.IsAlive,
                "Thread pool threads are always alive even after their task completes.");
        }

        // -----------------------------------------------------------------------
        // Correctness baseline.
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
