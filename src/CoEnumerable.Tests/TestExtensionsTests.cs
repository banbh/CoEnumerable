using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace CoEnumerable.Tests;

/// <summary>
/// These tests check that <c>Tracing</c> behaves as expected.
/// The full method is
/// <code>public static IEnumerable&lt;T&gt; Tracing&lt;T&gt;(
///     this IEnumerable&lt;T&gt; ts,
///     out StringBuilder stringBuilder)</code>
/// </summary>
[TestClass]
public class TestExtensionsTests
{
    [TestMethod]
    public void Tracing_Simple_Case()
    {
        var n = Enumerable.Range(42, 3).Tracing(out var builder).Sum();
        AreEqual(42 + 43 + 44, n);
        var i = Task.CurrentId;
        AreEqual($"{i}:, {i}:True {i}:42 {i}:True {i}:43 {i}:True {i}:44 {i}:False {i}:; ", builder.ToString());
    }

    private static Func<IEnumerable<int>, int> ConsumeThenThrow(int n) =>
        ns =>
        {
            _ = ns.Take(n).Count();
            throw new InvalidOperationException($"n={n}");
        };

    private static Func<IEnumerable<int>, int> ConsumeThenThrow2(int n) =>
        ns => ns
            .Select(k => k >= n ? throw new InvalidOperationException($"n={n}") : k)
            .Sum();

    private static int WeirdCoenumerable(IEnumerable<int> xs)
    {
        var xe = xs.GetEnumerator();
        xe.MoveNext(); // skip one
        xe.MoveNext();
        var x = xe.Current;
        xe.Dispose();
        return x + xe.Current; // access twice and after dispose
    }

    [TestMethod]
    public void Bar()
    {
        const int n = 3;
        StringBuilder sb = new();
        var e = Throws<InvalidOperationException>(() => ConsumeThenThrow(n)(Enumerable.Range(1, 10).Tracing(out sb)));
        AreEqual($"n={n}", e.Message);
        var i = Task.CurrentId;
        AreEqual($"{i}:, {i}:True {i}:True {i}:True {i}:; ", sb.ToString());
    }
    
    [TestMethod]
    public void Bar2()
    {
        const int n = 3;
        StringBuilder sb = new();
        var e = Throws<InvalidOperationException>(() => ConsumeThenThrow2(n)(Enumerable.Range(1, 10).Tracing(out sb)));
        AreEqual($"n={n}", e.Message);
        var i = Task.CurrentId;
        AreEqual($"{i}:, {i}:True {i}:1 {i}:True {i}:2 {i}:True {i}:3 {i}:; ", sb.ToString());
    }
    
    [TestMethod]
    public void Test_WeirdCoenumerable()
    {
        var nums = Enumerable.Range(1, 10);
        // ReSharper disable PossibleMultipleEnumeration
        var x = WeirdCoenumerable(nums.Tracing(out var sb));
        var k = nums.Skip(1).First();
        // ReSharper restore PossibleMultipleEnumeration
        AreEqual(k + k, x);
        var t  = Task.CurrentId;
        // Check a weird access pattern, which is not even legal since we access current twice (odd)
        // and access after we dispose (illegal). Regardless, Tracing traces what happens.
        AreEqual($"{t}:, {t}:True {t}:True {t}:{k} {t}:; {t}:{k} ", sb.ToString());
    }
}