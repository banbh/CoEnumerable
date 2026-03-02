using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CoEnumerable
{
    public static class Extensions
    {
        private class BarrierEnumerable<T> : IEnumerable<T>
        {
            private Barrier barrier;
            private bool moveNext;
            private CancellationToken cancellationToken;
            private readonly Func<T> src;

            public BarrierEnumerable(IEnumerator<T> enumerator)
            {
                src = () => enumerator.Current;
            }

            public Barrier Barrier
            {
                set => barrier = value;
            }

            public bool MoveNext
            {
                set => moveNext = value;
            }

            public CancellationToken CancellationToken
            {
                set => cancellationToken = value;
            }

            private IEnumerator<T> Inner()
            {
                while (true)
                {
                    try
                    {
                        barrier.SignalAndWait(cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        yield break;
                    }

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

            public IEnumerator<T> GetEnumerator() => new DisposingEnumerator(Inner());

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            private class DisposingEnumerator : IEnumerator<T>
            {
                private readonly IEnumerator<T> _inner;

                public DisposingEnumerator(IEnumerator<T> inner)
                {
                    _inner = inner;
                }

                public T Current => _inner.Current;
                object IEnumerator.Current => Current;
                public bool MoveNext() => _inner.MoveNext();
                public void Reset() => _inner.Reset();
                public void Dispose() => _inner.Dispose();
            }
        }

        public static T Combine<S, T1, T2, T>(this IEnumerable<S> source,
            Func<IEnumerable<S>, T1> coenumerable1,
            Func<IEnumerable<S>, T2> coenumerable2,
            Func<T1, T2, T> resultSelector,
            out int taskId1,
            out int taskId2)
        {
            using var ss = source.GetEnumerator();
            using var cts = new CancellationTokenSource();

            var enumerable1 = new BarrierEnumerable<S>(ss);
            var enumerable2 = new BarrierEnumerable<S>(ss);

            // Not using 'using' here — barrier is disposed manually after WhenAll
            // to ensure it is not disposed while threads are still using it.
            var barrier = new Barrier(2, _ => enumerable1.MoveNext = enumerable2.MoveNext = ss.MoveNext());
            enumerable1.Barrier = enumerable2.Barrier = barrier;
            enumerable1.CancellationToken = enumerable2.CancellationToken = cts.Token;

            using var t1 = Task.Run(() =>
            {
                bool faulted = false;
                try   { return coenumerable1(enumerable1); }
                catch { faulted = true; throw; }
                finally
                {
                    if (faulted) cts.Cancel();
                    try { barrier.RemoveParticipant(); }
                    catch (InvalidOperationException) { }
                }
            });
            taskId1 = t1.Id;

            using var t2 = Task.Run(() =>
            {
                bool faulted = false;
                try   { return coenumerable2(enumerable2); }
                catch { faulted = true; throw; }
                finally
                {
                    if (faulted) cts.Cancel();
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
