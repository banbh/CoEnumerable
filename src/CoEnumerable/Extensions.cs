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
    
    private sealed class CancelledByPartnerException : Exception;
    
    private class BarrierEnumerable<T>(IEnumerator<T> enumerator, CancellationToken token) : IEnumerable<T>
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
                    barrier!.SignalAndWait(token);
                }
                catch (BarrierPostPhaseException e)
                {
                    throw new SourceException(e.InnerException!);
                }
                catch (OperationCanceledException)
                {
                    throw new CancelledByPartnerException();
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
        bool isCancelable,
        out int taskId1,
        out int taskId2)
    {
        using var ss = source.GetEnumerator();

        CancellationTokenSource cts = new();
        var token = isCancelable ? cts.Token : CancellationToken.None;
        // var token = cts.Token;
        BarrierEnumerable<TS> enumerable1 = new(ss, token);
        BarrierEnumerable<TS> enumerable2 = new(ss, token);

        // ReSharper disable once AccessToDisposedClosure
        // ss is disposed via 'using' only after barrier.Dispose() is called,
        // which guarantees the post-phase action (which captures ss) can never
        // fire after ss is disposed.
        var barrier = new Barrier(2, _ => enumerable1.MoveNext = enumerable2.MoveNext = ss.MoveNext());
        enumerable1.Barrier = enumerable2.Barrier = barrier;
        
        using var t1 = RunCoenumerable(coenumerable1, enumerable1);
        taskId1 = t1.Id;
        using var t2 = RunCoenumerable(coenumerable2, enumerable2);
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
            
            if (!ae.InnerExceptions.All(e => e is CancelledByPartnerException))
            {
                // Should not reach here
                Debug.Assert(false, "Unexpected exception in AggregateException");
                throw;
            }
    
            if (ae.InnerExceptions.All(e => e is CancelledByPartnerException))
            {
                var r1 = t1.IsCompletedSuccessfully ? t1.Result : default;
                var r2 = t2.IsCompletedSuccessfully ? t2.Result : default;
                return (r1, r2);
            }
            // Debug.Assert(false, "Unexpected exception in AggregateException");
            // throw;
        }
        finally
        {
            barrier.Dispose();
        }

        return (t1.Result, t2.Result);

        Task<Result<T>> RunCoenumerable<T>(Func<IEnumerable<TS>, T> coenumerable, BarrierEnumerable<TS> enumerable) =>
            Task.Run(() =>
            {
                try
                {
                    return Result<T>.Ok(coenumerable(enumerable));
                }
                catch (Exception e) when (e is not SourceException and not CancelledByPartnerException)
                {
                    cts.Cancel();
                    return Result<T>.Fail(e);
                }
                finally
                {
                    RemoveParticipantOrThrowSourceException();
                }
            });

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
        source.TryCombine(coenumerable1, coenumerable2, false, out _, out _);
    
    internal static (T1,T2) Combine<TS, T1, T2>(this IEnumerable<TS> source,
        Func<IEnumerable<TS>, T1> coenumerable1,
        Func<IEnumerable<TS>, T2> coenumerable2,
        out int taskId1,
        out int taskId2)
    {
        var (r1, r2) = source.TryCombine(coenumerable1, coenumerable2, true, out taskId1, out taskId2);

        var exceptions = new[] { r1.IsSuccess ? null : r1.Error, r2.IsSuccess ? null : r2.Error }
            .OfType<Exception>()
            .ToList();

        return exceptions.Count == 0 
            ? (r1.Value, r2.Value) 
            : throw new AggregateException(exceptions);
    }
    
    public static (T1, T2) Combine<TS, T1, T2>(this IEnumerable<TS> source,
        Func<IEnumerable<TS>, T1> coenumerable1,
        Func<IEnumerable<TS>, T2> coenumerable2) =>
        source.Combine(coenumerable1, coenumerable2, out _, out _);
}
