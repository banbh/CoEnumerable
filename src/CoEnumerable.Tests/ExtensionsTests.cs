using System.Collections.Generic;
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
        private static bool Coenumerable1(IEnumerable<int> ns) => ns.Any(n => n == 3);
        private static int Coenumerable2(IEnumerable<int> ns) => ns.Take(2).Sum();
        
        private static readonly IEnumerable<int> Nums = Enumerable.Range(1, 2_000_000);
        
        [TestMethod()]
        public void CombineTest()
        {
            // ReSharper disable PossibleMultipleEnumeration
            var (x1, x2) = Nums.Combine(Coenumerable1, Coenumerable2);
            Assert.AreEqual(Coenumerable1(Nums), x1);
            Assert.AreEqual(Coenumerable2(Nums), x2);
            // ReSharper restore PossibleMultipleEnumeration
        }

        [TestMethod()]
        public void CombineTracingTest()
        {
            // ReSharper disable PossibleMultipleEnumeration
            var (x1, x2) = Nums.Tracing(out var sb).Combine(Coenumerable1, Coenumerable2,
                (x, y) => (x, y),
                out var tid1,
                out var tid2);
            Assert.AreEqual(Coenumerable1(Nums), x1);
            Assert.AreEqual(Coenumerable2(Nums), x2);

            // The problem with tracking the precise activities triggered by Combine(...) is that the threads
            // associated with the two coenumerables may run in various orders.  Below we painfully create a regex
            // to allow for this variation.  We define some local functions to make it a bit easier.
            var pat = new StringBuilder(@$"^{Task.CurrentId}:, ");
            using (var enumerator = Nums.GetEnumerator())
                for (var i = 0; i < 3; i++)
                {
                    Mn(enumerator.MoveNext());
                    if (i < 2) C(enumerator.Current); else C1(enumerator.Current);
                }
            pat.Append($"{Task.CurrentId}:; $");
            Assert.IsTrue(Regex.IsMatch(sb.ToString(), pat.ToString()));
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
    }
}