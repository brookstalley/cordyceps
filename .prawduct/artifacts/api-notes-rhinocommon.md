# API Notes — RhinoCommon threading surface (verify-api, Chunk 01)

Verified against `RhinoCommon 8.0.23304.9001` (net48 ref assembly) via
`System.Reflection.MetadataLoadContext` reflection on `Rhino.RhinoApp`, 2026-06-21.

## Findings

- **`void RhinoApp.InvokeAndWait(Action action)`** — the ONLY overload. No timeout
  parameter, no `CancellationToken`. The UI-thread invocation **cannot be natively
  bounded or cancelled.** A wedged UI thread (e.g. an infinite-loop script component)
  blocks `InvokeAndWait` indefinitely, and there is no supported way to abort the
  running UI work.
- **`bool RhinoApp.InvokeRequired`** (property) — EXISTS. `true` when the caller is NOT
  on the main/UI thread (an invoke is required). Used as the re-entrancy guard:
  `!InvokeRequired` ⇒ already on the UI thread ⇒ run the action inline (no re-marshal).
- **`void RhinoApp.InvokeOnUiThread(Delegate method, object[] args)`** — fire-and-forget
  async post; no return, no wait. (Used elsewhere for `RefreshComponent`/`ScheduleRecompute`.)
- **`void RhinoApp.Wait()`** — parameterless message-pump; no timeout. Not useful for a
  bounded wait.

## Design consequence

Because `InvokeAndWait` can't be timed out, the timeout is applied to the **document
mutex acquire**, not the UI work:

- A bounded `SemaphoreSlim.Wait(timeout)` (pure .NET) means that when one operation holds
  the document lock too long, **other** requests fail fast with a structured
  `DocumentBusyException` instead of blocking forever. The server stays responsive
  (returns errors) rather than becoming a black hole for every request.
- **Re-entrancy guard:** if `!RhinoApp.InvokeRequired`, run the action inline WITHOUT
  acquiring the lock — on the UI thread we already have exclusive UI access (single
  thread), and acquiring the lock there could deadlock against a worker thread blocked in
  `InvokeAndWait`.
- **Residual (documented, not fixed here):** a genuinely wedged UI thread still holds the
  lock until Rhino is restarted; the holder's own call cannot be aborted. The win is that
  every *other* request now fails fast and clearly instead of hanging. Forcible abort of
  UI work is not supported by RhinoCommon.

The timeout/release logic is host-free (`SemaphoreSlim` only) and is extracted into
`Core/DocumentLock` so it can be unit-tested in CI; only the `InvokeRequired`/
`InvokeAndWait` marshaling stays host-coupled and is verified live in Rhino.
