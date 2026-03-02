using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("CoEnumerableTests")]

namespace CoEnumerable
{
    public static class Extensions
    {
        private class BarrierEnumerable<T>(IEnumerator<T> enumerator) : IEnumerable<T>
        {
            private Barrier barrier;
            // moveNext is written by the post-phase action and read by Inner() on another thread.
            // This is safe because Barrier.SignalAndWait() provides a full memory barrier,
            // guaranteeing that the write is visible to all threads before they proceed.
            private bool moveNext;
            private readonly Func<T> src = () => enumerator.Current;

            public Barrier Barrier
            {
                set => barrier = value;
            }

            public bool MoveNext
            {
                set => moveNext = value;
            }

            public IEnumerator<T> GetEnumerator()
            {
                while (true)
                {
                    barrier.SignalAndWait();

                    if (moveNext)
                    {
                        yield return src();
                    }
                    else
                    {
                        yield break;
                    }
                }
            }
            
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        internal static T Combine<S, T1, T2, T>(this IEnumerable<S> source,
            Func<IEnumerable<S>, T1> coenumerable1,
            Func<IEnumerable<S>, T2> coenumerable2,
            Func<T1, T2, T> resultSelector,
            out int taskId1,
            out int taskId2)
        {
            using var ss = source.GetEnumerator();

            var enumerable1 = new BarrierEnumerable<S>(ss);
            var enumerable2 = new BarrierEnumerable<S>(ss);

            // Not using 'using' here — barrier is disposed manually after WhenAll
            // to ensure it is not disposed while threads are still using it.
            var barrier = new Barrier(2, _ => enumerable1.MoveNext = enumerable2.MoveNext = ss.MoveNext());
            enumerable1.Barrier = enumerable2.Barrier = barrier;

            using var t1 = Task.Run(() =>
            {
                try   { return coenumerable1(enumerable1); }
                finally
                {
                    try { barrier.RemoveParticipant(); }
                    catch (InvalidOperationException) { }
                }
            });
            taskId1 = t1.Id;

            using var t2 = Task.Run(() =>
            {
                try   { return coenumerable2(enumerable2); }
                finally
                {
                    try { barrier.RemoveParticipant(); }
                    catch (InvalidOperationException) { }
                }
            });
            taskId2 = t2.Id;

            try
            {
                // Wait for both tasks to complete before propagating any exception,
                // ensuring neither task is leaked if one coenumerable throws.
                Task.WhenAll(t1, t2).Wait();
            }
            finally
            {
                barrier.Dispose();
            }

            return resultSelector(t1.Result, t2.Result);
        }

        public static T Combine<S, T1, T2, T>(this IEnumerable<S> source, Func<IEnumerable<S>, T1> coenumerable1, Func<IEnumerable<S>, T2> coenumerable2, Func<T1, T2, T> resultSelector) =>
            Combine(source, coenumerable1, coenumerable2, resultSelector, out _, out _);
    }
}
