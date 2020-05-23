# CoEnumerable
A demonstration of how to combine consumers of IEnumerables, aka CoEnumerables

Suppose you have a sequence of numbers:
````C#
var nums = Enumerable.Range(1, 5); // 1,2,3,4,5
````
Then `nums.Min()` computes the minimum, and `nums.Max()` computes the maximum. 
However if we want want both we end up running through `nums` twice.
We could of course write our own function `MinMax()` which keeps track of both 
the minimum and the maximum.  But let's assume what we want is more complicated
and we don't have the source code for the function.
Suppose also that that the sequence we are working with is expensive to produce (so
we don't want to run through it more than once) and large (so we can't store all the items
in memory).  Is there a way to still compute what we wanted?
