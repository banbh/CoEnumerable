using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace CoEnumerableTests
{
    static class TestExtensions
    {
        public class TracingEnumerator<T> : IEnumerator<T>
        {
            private readonly object locker = new object();
            private readonly IEnumerator<T> enumerator;
            private readonly StringBuilder sb;

            public TracingEnumerator(IEnumerator<T> enumerator, StringBuilder sb)
            {
                this.enumerator = enumerator;
                this.sb = sb;
                Append(',');
            }

            private S Append<S>(S s)
            {
                var s1 = $"{Task.CurrentId}:{s} ";
                lock (locker)
                {
                    sb.Append(s1);
                }
                return s;
            }

            public T Current => Append(enumerator.Current);

            object IEnumerator.Current => Current;

            public void Dispose()
            {
                Append(';');
                enumerator.Dispose();
            }

            public bool MoveNext()
            {
                return Append(enumerator.MoveNext());
            }

            public void Reset()
            {
                Append('!');
                enumerator.Reset();
            }
        }

        public class TracingEnumerable<T> : IEnumerable<T>
        {
            private readonly IEnumerable<T> ts;
            private readonly StringBuilder stringBuilder;

            public TracingEnumerable(IEnumerable<T> ts, StringBuilder stringBuilder)
            {
                this.ts = ts;
                this.stringBuilder = stringBuilder;
            }

            public IEnumerator<T> GetEnumerator()
            {
                return new TracingEnumerator<T>(ts.GetEnumerator(), stringBuilder);
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        public static IEnumerable<T> Tracing<T>(this IEnumerable<T> ts, out StringBuilder stringBuilder)
        {
            stringBuilder = new StringBuilder();
            return new TracingEnumerable<T>(ts, stringBuilder);
        }
    }
}
