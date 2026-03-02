# Introduction
Suppose you have a sequence of numbers:
```C#
var nums = Enumerable.Range(1, 5); // 1,2,3,4,5
```
Then `nums.Min()` computes their minimum, and `nums.Max()` computes their maximum.
However, if we want both the minimum and the maximum we end up running through `nums` twice.
We could, of course, write our own function `MinMax()` which keeps track of both
the minimum and the maximum. But let's assume what we want to compute two, more complicated,
functions of `nums` and that we don't have the source code for the functions.
Suppose, also, that the sequence we are working with is expensive to produce (so
we don't want to run through it more than once) and large (so we can't store all the items
in memory). Is there a way to still compute what we wanted?
This repo shows how one can go about doing this.

This repo was inspired by the following question on SO:
[Consuming an IEnumerable multiple times in one pass](https://stackoverflow.com/questions/60963484).

# Combining CoEnumerables
Let's call an object with type `IEnumerable<S>` an *enumerable*, and an object
with type `Func<IEnumerable<S>, T>` a *coenumerable* (since it is
[dual](https://en.wikipedia.org/wiki/Dual_(category_theory)) to an enumerable).
Each time we call `GetEnumerator()` on an enumerable we say that we are *enumerating* it.
If we call `MoveNext()` on the resulting `IEnumerator<S>` until it returns `false` then
we say we enumerated *fully*, otherwise we enumerated *partially*.

We can now state precisely what we described informally in the Introduction.
We want a procedure to evaluate two coenumerables on a given enumerable so that
* the enumerable is only enumerated once,
* we do not store the items of the enumerable simultaneously in memory,
* we do not require access to the source code of the coenumerables, and
* if both coenumerables enumerate the enumerable partially, then so too does the procedure.

The `Combine` extension method in the `CoEnumerable` project in this repo does exactly this.
It is implemented using a
[Barrier](https://docs.microsoft.com/en-us/dotnet/api/system.threading.barrier)
(not to be confused with a
[MemoryBarrier](https://docs.microsoft.com/en-us/dotnet/api/system.threading.thread.memorybarrier)):
each coenumerable runs on its own task, and the barrier ensures that the source is advanced
exactly once per phase, with both tasks receiving each item before the next is pulled.

# Preconditions
For `Combine` to work correctly, each coenumerable must:
* Call `GetEnumerator()` exactly once on the enumerable it receives.
* `Dispose()` the enumerator it obtains — either explicitly or implicitly via `foreach` or LINQ.
  This is standard .NET practice and is satisfied automatically by all LINQ operators.

A coenumerable that calls `GetEnumerator()` but never calls `MoveNext()` (e.g. `ns => ns.Take(0).Sum()`)
is fully supported. A coenumerable that never calls `GetEnumerator()` at all violates the first
precondition and will cause a deadlock.

# Comments and Limitations
* `Combine` is significantly slower than running two functions independently over the same sequence,
  because it requires two thread synchronisations per item. `CoEnumerable.Demo` illustrates this —
  expect a 20x–50x slowdown compared to the naive approach. This is an inherent cost of the
  no-buffering constraint: any meaningful speedup would require relaxing it to allow a small
  fixed-size buffer.
* If one coenumerable throws an exception, the other is allowed to run to completion before
  the exception is propagated to the caller.
* The `CoEnumerableTests` project documents the preconditions and verifies correct behavior,
  including edge cases such as partial enumeration, vacuous enumeration, and exceptions.
* Could this be implemented using [ReactiveX](http://reactivex.io/)? It is certainly possible
  to satisfy the first three requirements using ReactiveX. Whether the fourth requirement —
  that partial enumeration by both coenumerables implies partial enumeration of the source —
  can also be satisfied is an open question.

# Acknowledgements
The initial Barrier-based design was mine, however [Claude](https://claude.ai)
(claude.ai, `claude-sonnet-4-6`)
discovered some important bugs, wrote tests for them, 
and fixed the implementation.
