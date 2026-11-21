# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

The solution lives in `src/` (`src/CoEnumerable.sln`, .NET 9, nullable enabled). Run commands from `src/`:

```sh
dotnet build                                   # build all projects
dotnet test                                    # run all tests (MSTest)
dotnet test --filter "FullyQualifiedName~CombineTracingTest"   # run a single test
dotnet run --project CoEnumerable.Demo -c Release              # demo + timing comparison
```

## What this library does

`CoEnumerable` evaluates two "coenumerables" (`Func<IEnumerable<TS>, T>`) over a single source `IEnumerable<TS>` such that the source is enumerated only once, nothing is buffered, the coenumerables are opaque, and the source is consumed only as far as the coenumerables need (if both stop early, so does the source). See `README.md` for the full rationale, preconditions, and limitations.

Public API (all in `src/CoEnumerable/Extensions.cs`):
- `TryCombine` → `(Result<T1>, Result<T2>)`; each result independently holds a value or an exception (`Result.cs`).
- `Combine` → `(T1, T2)`; throws `AggregateException` if either coenumerable throws. Built on the internal `TryCombine` overload with `isCancelable: true`.
- Exceptions thrown by the *source* itself bypass `Result` and propagate directly to the caller.

## Architecture of the Barrier-based implementation

This is the part that requires reading carefully; most past bugs were concurrency bugs here.

- Each coenumerable runs on its own `Task.Run`, receiving its own `BarrierEnumerable<TS>` wrapper. Both wrappers share the one real source enumerator.
- A `Barrier` with 2 participants coordinates them. Each `GetEnumerator()` iteration calls `SignalAndWait`; the barrier's **post-phase action** is the only place `source.MoveNext()` is called, and it writes the result into both wrappers' `moveNext` field (visibility guaranteed by the barrier's memory fence). So the source advances exactly once per phase.
- When a coenumerable finishes (early or not), its task calls `barrier.RemoveParticipant()` in a `finally`, letting the other continue alone. Coenumerables must call `GetEnumerator()` exactly once and dispose the enumerator (preconditions in README).
- Source exceptions surface as `BarrierPostPhaseException` (from either `SignalAndWait` or `RemoveParticipant`) and are wrapped in the private `SourceException`, then unwrapped and rethrown via `ExceptionDispatchInfo` in `TryCombine`.
- In cancelable mode (`Combine`), a throwing coenumerable cancels a shared `CancellationTokenSource`; the partner's `SignalAndWait(token)` then throws `OperationCanceledException`, converted to the private `CancelledByPartnerException` so the partner stops enumerating early.
- Disposal ordering matters: `barrier.Dispose()` happens in `finally` before the `using`-disposed source enumerator, so the post-phase action can never touch a disposed enumerator. Comments in the code explain these invariants (and the ReSharper suppressions) — preserve them when editing.
- For any change to the Barrier/cancellation/disposal logic, explain which invariant
  is affected before editing, and run the test suite several times
  (e.g. `for i in {1..20}; do dotnet test --no-build || break; done`) to catch flakiness.

`internal` overloads exposing `out int taskId1/taskId2` (and `isCancelable`) exist solely for tests; `InternalsVisibleTo("CoEnumerable.Tests")` is declared in `Extensions.cs`.

## Tests

- `CoEnumerable.Tests/TestExtensions.cs` provides `.Tracing(out StringBuilder sb)`, which wraps an enumerable and logs every `MoveNext`/`Current`/`Dispose`/`Reset` tagged with `Task.CurrentId`. Tests (e.g. `CombineTracingTest`) build regexes over this trace, using the task IDs from the internal `Combine` overload, to assert the source is enumerated once, minimally, and from which threads. `TestExtensionsTests.cs` tests the tracer itself.
- `.editorconfig` disables CA1707 (underscores in names) for `*Tests.cs`; test names follow `Method_Behavior_Condition`.

## Other projects

- `CoEnumerable.Demo` — console demo showing minimal enumeration and timing vs. naive double enumeration (expect ~20–50x slowdown; inherent to the no-buffering design).
- `Experiments` — scratch/prototype code (an alternative hand-off `Box<T>` approach), not referenced by the library.

## Tooling

- When the `rider` MCP server is available, prefer it for symbol search, find usages,
  renames/refactorings, and inspections over text search and manual edits.
- After any change, run `dotnet build` and `dotnet test` from `src/` and make sure
  both pass before considering the task done.