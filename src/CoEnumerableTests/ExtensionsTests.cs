using CoEnumerableTests;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CoEnumerable.Tests
{
    [TestClass()]
    public class ExtensionsTests
    {
        [TestMethod()]
        public void CombineTest()
        {
            var nums = Enumerable.Range(1, 2_000_000);
            (var x, var y) = nums.Combine(ns => ns.Any(n => n == 3), ns => ns.Take(2).Sum(), (x, y) => (x, y));
            Assert.IsTrue(x);
            Assert.AreEqual(3, y);
        }

        [TestMethod()]
        public void CombineTracingTest()
        {
            var nums = Enumerable.Range(1, 2_000_000);
            (var x, var y) = nums.Tracing(out var sb).Combine(
                ns => ns.Any(n => n == 3),
                ns => ns.Take(2).Sum(),
                (x, y) => (x, y),
                out int tid1,
                out int tid2);
            Assert.IsTrue(x);
            Assert.AreEqual(3, y);

            // The problem with tracking the precise activities triggered by Combine(...) is that the threads
            // associated with the two coenumerables may various orders.  Below we painfully create a regex
            // to allow for this variation.  We define some local functions to make it a bit easier.
            var pat = new StringBuilder(@$"^{Task.CurrentId}:, ");
            // below we define functions that add to the pattern corresponding to various events
            void a<S, T>(S s, T t) => pat.Append($"{s}:{t} "); // data
            void mn(bool b) => a($"({tid1}|{tid2})", b); // MoveNext() by some thread
            void c(int i) { pat.Append("("); a(tid1, i); a(tid2, i); pat.Append("|"); a(tid2, i); a(tid1, i); pat.Append(")"); }  // Current by both threads
            void c1(int i) => a(tid1, i); // Current by just thread 1
            using (var enumerator = nums.GetEnumerator())
                for (int i = 0; i < 3; i++)
                {
                    mn(enumerator.MoveNext());
                    if (i < 2) c(enumerator.Current); else c1(enumerator.Current);
                }
            pat.Append($"{Task.CurrentId}:; $");
            Assert.IsTrue(Regex.IsMatch(sb.ToString(), pat.ToString()));
        }
    }
}