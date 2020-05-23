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

            public IEnumerator<T> GetEnumerator()
            {
                try
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
                finally
                {
                    barrier.RemoveParticipant();
                }
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        public static T Combine<S, T1, T2, T>(this IEnumerable<S> source,
            Func<IEnumerable<S>, T1> coenumerable1,
            Func<IEnumerable<S>, T2> coenumerable2,
            Func<T1, T2, T> resultSelector,
            out int taskId1,
            out int taskId2)
        {
            using var ss = source.GetEnumerator();
            var enumerable1 = new BarrierEnumerable<S>(ss);
            var enumerable2 = new BarrierEnumerable<S>(ss);
            using var barrier = new Barrier(2, _ => enumerable1.MoveNext = enumerable2.MoveNext = ss.MoveNext());
            enumerable2.Barrier = enumerable1.Barrier = barrier;

            using var t1 = Task.Run(() => coenumerable1(enumerable1));
            taskId1 = t1.Id;

            using var t2 = Task.Run(() => coenumerable2(enumerable2));
            taskId2 = t2.Id;

            return resultSelector(t1.Result, t2.Result);
        }

        public static T Combine<S, T1, T2, T>(this IEnumerable<S> source, Func<IEnumerable<S>, T1> coenumerable1, Func<IEnumerable<S>, T2> coenumerable2, Func<T1, T2, T> resultSelector) =>
            Combine(source, coenumerable1, coenumerable2, resultSelector, out _, out _);
    }
}
