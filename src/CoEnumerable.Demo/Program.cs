using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace CoEnumerable.Demo
{
    static class Program
    {
        private static IEnumerable<T> Trace<T>(this IEnumerable<T> ts, string msg = "")
        {
            Console.Write(msg);
            try
            {
                foreach (T t in ts)
                {
                    Console.Write($" {t}");
                    yield return t;
                }
            }
            finally
            {
                Console.WriteLine('.');
            }
        }
        
        static void Main()
        {
            var nums = Enumerable.Range(1, 2_000_000);

            // Check that CoEnumerable.Combine(...) behaves as expected
            // ReSharper disable PossibleMultipleEnumeration
            var (x, y) = nums.Trace("nums:").Combine(ns => ns.Any(n => n == 3), ns => ns.Take(2).Sum());
            // Note that Trace(...) outputs "nums: 1 2 3." indicating that the sequence is (a) iterated only once, and (b) only as far as needed
            Console.WriteLine($"Are any three? {x}.  Sum of first two: {y}");

            // Carry out some timings
            var stopWatch = Stopwatch.StartNew();
            var minMax1 = (nums.Min(), nums.Max()); // iterates through `nums` twice
            stopWatch.Stop();
            Console.WriteLine($"Old-fashioned way: (min, max)={minMax1} (in {stopWatch.ElapsedMilliseconds}ms)");

            stopWatch = Stopwatch.StartNew();
            var minMax2 = nums.Combine(Enumerable.Min, Enumerable.Max); // iterates through `nums` only once
            stopWatch.Stop();
            Console.WriteLine($"Using CoEnumerable: (min, max)={minMax2} (in {stopWatch.ElapsedMilliseconds}ms)"); // takes 10x - 20x as long as the old-fashioned way

            Console.WriteLine($"Did we get the right answer? {minMax1 == minMax2}");
            // ReSharper restore PossibleMultipleEnumeration
        }
    }
}
