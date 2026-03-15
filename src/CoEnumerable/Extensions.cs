using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("CoEnumerable.Tests")]

namespace CoEnumerable;

public static class Extensions
{
    private class BarrierEnumerable<T>(IEnumerator<T> enumerator) : IEnumerable<T>
    {
        private Barrier? barrier;
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
                try
                {
                    // barrier is always set immediately after construction in TryCombine,
                    // before GetEnumerator() is called, so ! is safe here.
                    barrier!.SignalAndWait();
                }
                catch (BarrierPostPhaseException e)
                {
                    throw new SourceException(e.InnerException!);
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
            
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class SourceException(Exception inner) : Exception(null, inner);
        
    internal static (Result<T1>, Result<T2>) TryCombine<TS, T1, T2>(
        this IEnumerable<TS> source,
        Func<IEnumerable<TS>, T1> coenumerable1,
        Func<IEnumerable<TS>, T2> coenumerable2,
        out int taskId1,
        out int taskId2)
    {
        using var ss = source.GetEnumerator();

        var enumerable1 = new BarrierEnumerable<TS>(ss);
        var enumerable2 = new BarrierEnumerable<TS>(ss);

        // ReSharper disable once AccessToDisposedClosure
        // ss is disposed via 'using' only after barrier.Dispose() is called,
        // which guarantees the post-phase action (which captures ss) can never
        // fire after ss is disposed.
        var barrier = new Barrier(2, _ => enumerable1.MoveNext = enumerable2.MoveNext = ss.MoveNext());
        enumerable1.Barrier = enumerable2.Barrier = barrier;

        using var t1 = Task.Run(() =>
        {
            try
            {
                return Result<T1>.Ok(coenumerable1(enumerable1));
            }
            catch (Exception e) when (e is not SourceException)
            {
                return Result<T1>.Fail(e);
            }
            finally
            {
                RemoveParticipantOrThrowSourceException();
            }
        });
        taskId1 = t1.Id;

        using var t2 = Task.Run(() =>
        {
            try
            {
                return Result<T2>.Ok(coenumerable2(enumerable2));
            }
            catch (Exception e) when (e is not SourceException)
            {
                return Result<T2>.Fail(e);
            }
            finally
            {
                RemoveParticipantOrThrowSourceException();
            }
        });
        taskId2 = t2.Id;

        try
        {
            Task.WhenAll(t1, t2).Wait();
        }
        catch (AggregateException ae)
        {
            var sourceExceptions = ae.InnerExceptions
                .OfType<SourceException>()
                .Select(se => se.InnerException!)
                .ToList();
    
            if (sourceExceptions.Count > 0)
                ExceptionDispatchInfo.Capture(sourceExceptions[0]).Throw();
    
            // Should not reach here
            Debug.Assert(false, "Unexpected exception in AggregateException");
            throw;
        }
        finally
        {
            barrier.Dispose();
        }

        return (t1.Result, t2.Result);
        
        void RemoveParticipantOrThrowSourceException()
        {
            // Note: if this throws, any in-flight exception from the coenumerable will be lost.
            // This is safe because coenumerable exceptions are always captured into Results
            // before this finally block runs, so there is never an in-flight exception here.
            try
            {
                // barrier is disposed only after both tasks complete (via Task.WhenAll),
                // so it is guaranteed to be alive when RemoveParticipantOrThrowSourceException is called.
                // ReSharper disable once AccessToDisposedClosure
                barrier.RemoveParticipant();
            }
            catch (BarrierPostPhaseException e)
            {
                throw new SourceException(e.InnerException!);
            }
        }
    }
    
    
        
    public static (Result<T1>, Result<T2>) TryCombine<TS, T1, T2>(
        this IEnumerable<TS> source,
        Func<IEnumerable<TS>, T1> coenumerable1,
        Func<IEnumerable<TS>, T2> coenumerable2) =>
        source.TryCombine(coenumerable1, coenumerable2, out _, out _);
        

    
    internal static T Combine<TS, T1, T2, T>(this IEnumerable<TS> source,
        Func<IEnumerable<TS>, T1> coenumerable1,
        Func<IEnumerable<TS>, T2> coenumerable2,
        Func<T1, T2, T> resultSelector,
        out int taskId1,
        out int taskId2)
    {
        var (r1, r2) = source.TryCombine(coenumerable1, coenumerable2, out taskId1, out taskId2);

        var exceptions = new[] { r1.IsSuccess ? null : r1.Error, r2.IsSuccess ? null : r2.Error }
            .OfType<Exception>()
            .ToList();

        return exceptions.Count == 0 
            ? resultSelector(r1.Value, r2.Value) 
            : throw new AggregateException(exceptions);
    }
    
    public static (T1, T2) Combine<TS, T1, T2>(this IEnumerable<TS> source,
        Func<IEnumerable<TS>, T1> coenumerable1,
        Func<IEnumerable<TS>, T2> coenumerable2) =>
        source.Combine(coenumerable1, coenumerable2, (x, y) => (x, y), out _, out _);
}
