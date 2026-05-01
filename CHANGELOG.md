# Release Notes

## 7.0.0

### Added support for Serilog 3.1.1

Upgraded the Serilog package to fix the issue with the missing `RawFormatter`.

### Failure sink support

Failure sink(s) can be configured through appsettings.json or via code.

In this example the Console sink is used as a failure sink. If the RabbitMQ sink fails
to emit an event, it will write to the Console sink.

```json
  "Serilog": {
    "Using": [ "Serilog.Sinks.RabbitMQ", "Serilog.Sinks.Console" ],
    "MinimumLevel": "Debug",
    "WriteTo": [
      {
        "Name": "RabbitMQ",
        "Args": {
          "username": "serilog",
          "password": "serilog",
          "hostnames": [
            "localhost"
          ],
          "emitEventFailure": "WriteToSelfLog,WriteToFailureSink",
          "failureSinkConfiguration": [
            {
              "Name": "Console"
            }
          ]
        }
      }
    ]
  }
```

### Improved SSL support

The SSL support has been improved. Now all properties are used when creating the connection.

### Breaking changes (7.0.0)

- Renamed `LoggerConfigurationRabbitMqExtension` to `LoggerConfigurationRabbitMQExtensions`.
- Renamed `hostname` to `hostnames` in configuration.
- Default text formatter is `CompactJsonFormatter`. The `RawFormatter` was removed from Serilog.
- Requires `Serilog.Sinks.PeriodicBatching` `4.0`. This could break other sinks that inherit from
`PeriodicBatchingSink` from this package. This was obsolete code and it has been removed. See
[release notes](https://github.com/serilog/serilog-sinks-periodicbatching/releases/tag/v4.0.0)
for more information.

## 8.0.0

### Added support for RabbitMQ.Client 7.0.0

Add support for `RabbitMQ.Client` version `7.0.0`. Note that this is a breaking change.
The `RabbitMQ.Client` `7.0.0` is not compatible with the `RabbitMQ.Client` `6.x`.

### Added support for Serilog 4.2.0

Removed reference to `Serilog.Sinks.PeriodicBatching` and use `Serilog.Core.Sinks.Batching.BatchingSink` instead.

### Set message properties

Add support for setting `BasicProperties` before publishing a message. Properties can be set by creating a class implementing `ISendMessageEvents`.

 ```csharp
public void OnSetMessageProperties(LogEvent logEvent, IBasicProperties properties)
{
    // example of setting message headers based on log event properties
    logEvent.Properties.TryGetValue("messageType", out var messageType);
    properties.Headers = new Dictionary<string, object?>
    {
        { "messageType", messageType?.ToString() },
    };

    // example of setting correlation id based on log event properties
    if (logEvent.Properties.TryGetValue(LogProperties.CORRELATION_ID, out var correlationId))
    {
        properties.CorrelationId = correlationId.ToString();
    }
}
```

### Dynamic Routing Key

Moved the logic for determining the routing key logic to the `ISendMessageEvents`.
This allows for more flexibility when setting the routing key.

```csharp
public string OnGetRoutingKey(LogEvent logEvent, string defaultRoutingKey)
{
    // example of routing based on log level
    return logEvent.Level switch
    {
        LogEventLevel.Error => "error",
        _ => _defaultRoutingKey
    };
}
```

### Added support for .net 9

Add `net9.0` to the target frameworks.

### Breaking changes (8.0.0)

- Upgrade RabbitMQ.Client to `7.0.0`.
- Upgrade Serilog to `4.2.0`.
- Renamed `Period` to `BufferingTimeLimit`.
- Renamed `RouteKey` to `RoutingKey`
- Removed `RoutingFunc`
- Removed `.net7.0` target framework

## 9.0.0 [not published]

### Breaking change — failure sink removed in favour of `WriteTo.FallbackChain(...)`

The in-sink failure sink and the `EmitEventFailureHandling` enum have been
removed. Publish failures now always propagate so Serilog's `BatchingSink` can
route the original batch to its `ILoggingFailureListener` — this is what enables
`WriteTo.FallbackChain(...)` / `WriteTo.Fallible(...)` to compose with the
RabbitMQ sink. Diagnostics are written to `Serilog.Debugging.SelfLog` on every
failure (no opt-in flag required).

Removed public surface:

- `Serilog.Sinks.RabbitMQ.EmitEventFailureHandling` (enum)
- `RabbitMQSinkConfiguration.EmitEventFailure` (property)
- `WriteTo.RabbitMQ(..., Action<LoggerSinkConfiguration>? failureSinkConfiguration)` overloads
- The flat-overload `emitEventFailure` and `failureSinkConfiguration` parameters

Migration: replace `failureSinkConfiguration` with Serilog core's
`WriteTo.FallbackChain(...)` (or `WriteTo.Fallible(...)` for listener-based
reporting). For appsettings.json, remove the `emitEventFailure` and
`failureSinkConfiguration` keys and wrap the `RabbitMQ` block in `FallbackChain`
(the JSON binding ships with prerelease `Serilog.Settings.Configuration`). The
audit path (`AuditTo.RabbitMQ`) keeps its existing throw-on-failure semantics —
there is no `AuditTo.FallbackChain`/`AuditTo.Fallible` overload; wrap audit calls
in `try/catch` or register an `ILoggingFailureListener` via the new
`RabbitMQSink.SetFailureListener(...)` hook on a directly-constructed sink.

### Cleanup follow-ups from PR #310 architect review

Three issues filed during the PR #310 review, addressed together:

- **#314**: `GetAsync`'s rotation-race ODE handler (now in `ResolveUnhealthyToken`)
  used to throw `InvalidOperationException("Channel pool has been disposed.")`
  when the inner re-read also threw `ObjectDisposedException`, even if the pool
  wasn't actually disposed. The handler now checks `_disposed` first and only
  surfaces the disposal message when the flag is set; the (essentially-unreachable)
  back-to-back rotation case maps to the structured `PoolExhaustedException`
  instead.
- **#316**: The internal `TestingSetState` and `TestingInvokeHandleBrokenStateAsync`
  hooks are now `[Obsolete]`, so any accidental call from production code in
  `src/` fails the build via the project-wide `TreatWarningsAsErrors`. The test
  file suppresses CS0618 with a file-scope `#pragma`. The conditional-compilation
  suggestion from the original review (`[Conditional("DEBUG")]` / `#if DEBUG`)
  did not fit because tests run in `Release` on CI.
- **#317**: New `ProbeRecovery_WithChannelCountOne_RefillCohortOfZeroReachesOpenState`
  test pins the `ChannelCount = 1` edge case after the `_size - 1` refill fix —
  the cohort of zero never enters the `WarmUpAsync` for-loop and must still
  reach `Open` via the post-loop `Warming → Open` CAS.

### Gated cohort-completion `_consecutiveFailures` reset to authoritative cohorts

After moving the consecutive-failure reset from per-channel to per-cohort (#315),
a follow-up review found that opportunistic `Return`-driven refills (single
channel, `markOpenOnCompletion: false`) could still wipe failures another
concurrent cohort was actively accumulating. Two simultaneous broken-channel
returns where one refill succeeded and the other was mid-failure had the
success erase the failure count, delaying or hiding a legitimate breaker
trip. The reset is now gated on `markOpenOnCompletion == true`, so only
initial warm-up and post-probe refill — both authoritative for the state
machine — clear failure history.

### Fixed `_consecutiveFailures` per-channel reset masking sustained flapping

The warm-up loop previously reset `_consecutiveFailures` to zero on every successful
channel addition. A flaky broker that let one channel through per cohort therefore
silently kept clearing the counter mid-cohort and never reached `WarmUpMaxRetries`,
so the breaker never tripped — even with cumulative failures well past the
configured budget. The reset now happens on **cohort completion** (in `WarmUpAsync`,
after the inner loop), so failures accumulate across the cohort while still letting
a clean refill or initial warm-up clear stale failure history from a previous
outage. The previously-skipped reproducer test
`WarmUp_WithFlakyBrokerOneSuccessPerCohort_TripsBrokenAfterMaxRetriesCumulativeFailures`
is now passing. Tracked as [#315](https://github.com/ArieGato/serilog-sinks-rabbitmq/issues/315).

This is a behaviour change for users with `WarmUpMaxRetries` set: a sustained
flapping pattern that previously kept the breaker dormant will now correctly trip
it once the cumulative-failure threshold is reached.

### Wired `warmUpMaxRetries` through the public extension overloads

`WriteTo.RabbitMQ(...)` and `AuditTo.RabbitMQ(...)` now expose a `warmUpMaxRetries`
parameter (default `10`, `null` for unlimited). Previously the option was only
reachable by hand-constructing `RabbitMQClientConfiguration`, so the README and
CHANGELOG documentation of it didn't match the actual surface.

### Fixed misleading `PoolExhaustedException` message when retries are unlimited

When `WarmUpMaxRetries = null` the warm-up loop never trips Broken, but
`HandleBrokenStateAsync` could still surface `InvalidOperationException` on probe
paths (CAS-lost, probe-already-in-flight, or probe-failed). The interpolated
message previously read `"Channel pool exhausted after  consecutive warm-up
failures..."` (empty number). The probe-path message is now distinct and the
counted-retries variant is only emitted when a numeric retry budget exists.

### Fixed channel-pool concurrency bugs in the warm-up / circuit-breaker path

Three latent races in the bounded-warm-up and self-heal logic, surfaced by an
architect-level review of the new state machine:

- **`GetAsync` could leak `ObjectDisposedException`.** The probe-success path in
  `HandleBrokenStateAsync` rotates `_unhealthySignalCts` and disposes the
  previous instance. A concurrent `GetAsync` caller that loaded the old
  reference before the rotation could then call `.Token` on a disposed CTS,
  bypassing the structured `ChannelClosedException` / `OperationCanceledException`
  catch blocks and surfacing an unstructured failure to publish-path callers.
  The field is now `volatile` and `GetAsync` snapshots the reference once and
  tolerates `ObjectDisposedException` from the snapshot's `Token`, mapping the
  race to the structured disposal exception.
- **Probe recovery could leave the pool stuck in `Warming`.** After a successful
  probe, the refill task spawned `WarmUpAsync(_size, ...)` — but the probe had
  already written one channel into the bounded queue. The last `TryWrite`
  therefore overflowed, the orphan was disposed, `WarmUpSingleAsync` returned
  false, and `WarmUpAsync` exited before the `Warming → Open` CAS — leaving the
  pool stuck in `Warming` even though the broker had recovered. Refill now asks
  for `_size - 1` channels.
- **`RabbitMQConnectionFactory` lock-free fast path was racy.** The non-volatile
  read of `_connection` could observe a non-null reference whose construction
  was not yet fully published (release-store reordering), letting a concurrent
  caller invoke methods on a partially-initialised `IConnection`. The field is
  now `volatile`.

### Added support for .net 10

Add `net10.0` to the target frameworks.

### Replaced channel pool implementation

The dependency on `Microsoft.Extensions.ObjectPool` has been removed. The sink now uses an
internal channel pool that opens channels asynchronously and eagerly pre-opens the
configured number of channels in the background at startup.

The pool is fixed-size: when all channels are in use, additional publish calls wait for one
to be returned. Channels that close unexpectedly are disposed and replaced in the background
to keep the pool full.

### Async disposal throughout the sink

The internal `IRabbitMQChannel`, `IRabbitMQChannelPool`, `IRabbitMQClient`, and
`IRabbitMQConnectionFactory` now implement `IAsyncDisposable` so channel close operations
are properly awaited instead of fire-and-forget. `RabbitMQSink` remains `IDisposable`
(Serilog's lifecycle boundary is synchronous) and bridges once via an internal
sync-over-async helper at dispose time.

### Channel pool correctness

Two concurrency bugs in the channel pool were fixed:

- Concurrent warm-up tasks could redundantly declare the exchange. The declare is now
  guarded by a semaphore with a double-check so it runs exactly once across all channel
  creations.
- `GetAsync` could hand back a null reference if `DisposeAsync` ran between its semaphore
  wait and its bag dequeue. The pool now uses `System.Threading.Channels.Channel<T>` where
  dequeue and signalling are atomic by construction.

As a side-effect of the `Channel<T>` migration, `GetAsync` invoked on a disposed pool
throws `InvalidOperationException` instead of `OperationCanceledException`. Both types
are on an internal interface; no public API change.

### Fixed channel leak on exchange declare failure

`RabbitMQChannelPool.CreateChannelAsync` now closes the underlying `IChannel` if
`ExchangeDeclareAsync` throws. Previously, declare failures orphaned the channel against
the broker on each retry; repeated failures could accumulate up to the connection's
`channel_max` limit and then stop opening new channels entirely.

### Aligned failure handling with Serilog's `BatchingSink` model

`RabbitMQSink.EmitBatchAsync` no longer silently swallows publish failures.
Exceptions propagate to Serilog's `BatchingSink`, whose listener plumbing
observes the failure — this is what makes
`WriteTo.FallbackChain(s => s.RabbitMQ(...), s => s.File(...))` route
failed events to the fallback sink. Each failure is also written to
`Serilog.Debugging.SelfLog`.

`RabbitMQSink` also now implements `ISetLoggingFailureListener` — this is only
relevant for the audit path (`AuditTo.RabbitMQ(...)`) and direct-construct users.
`BatchingSink` does not forward `SetFailureListener` to its inner sink by design,
so the batched pipeline still routes via `BatchingSink`'s own listener.

### Fixed SSL `ServerName` leaking across hostnames

When `Hostnames` contained more than one entry and `SslOption` was enabled, every
`AmqpTcpEndpoint` was pointed at the **same** `SslOption` instance and the SNI
`ServerName` was mutated in place. The first hostname's value was baked in and reused
for every subsequent connection, causing SNI mismatches against brokers with
host-specific certificates. The connection factory now shallow-clones the
`SslOption` per endpoint and derives `ServerName` from the caller-provided value or
the endpoint's own hostname — never from a previously-mutated shared object. The
caller's `SslOption` is no longer mutated.

### Public `Validate()` on configuration classes

`RabbitMQClientConfiguration` and `RabbitMQSinkConfiguration` now expose a public
`Validate()` method. Callers who construct a configuration directly (for example to
hand to a custom `RabbitMQSink`) can use it to get the same safety net that
`WriteTo.RabbitMQ(...)` / `AuditTo.RabbitMQ(...)` have always applied internally.

Existing client-configuration checks (non-empty hostnames, non-empty username,
non-null password, valid port range) were moved verbatim — exception types and
messages are preserved. `RabbitMQClientConfiguration.Validate()` additionally
checks:

- `ChannelCount > 0` — **tightened constraint**: a zero or negative
  `ChannelCount` was previously silently substituted with the default of 64
  inside the channel pool; it is now rejected at configuration time. The
  defensive clamp in the pool constructor has been removed.

`RabbitMQSinkConfiguration.Validate()` is new and additionally checks:

- `TextFormatter` is non-null
- `BatchPostingLimit > 0`
- `BufferingTimeLimit >= TimeSpan.Zero`
- `QueueLimit > 0` when set — **tightened constraint**: a zero or negative
  `QueueLimit` previously passed through to Serilog's batching layer silently;
  it is now rejected at configuration time.

### Bounded warm-up retry with exponential backoff and circuit-breaker self-heal

`RabbitMQChannelPool` used to retry warm-up forever on a fixed 500 ms interval. Under a
sustained broker outage that meant constant wake-ups, `SelfLog` flooded with "Failed to
warm up RabbitMQ channel" entries, and callers blocked on `GetAsync` indefinitely because
the pool never filled.

The warm-up path now:

- **Backs off exponentially**: 500 ms → 1 s → 2 s → 4 s → 8 s → 16 s → 30 s (cap). The
  failure counter resets on any successful channel creation, so a single transient blip
  cannot combine with later failures to reach the cap.
- **Gives up after `RabbitMQClientConfiguration.WarmUpMaxRetries` consecutive failures**
  (default **10**). On exhaustion the pool transitions to a `Broken` state.
- **Fails fast from `GetAsync`** while broken — throws
  `InvalidOperationException("Channel pool exhausted after N consecutive warm-up failures; broker is unreachable.")`.
  That propagates through `RabbitMQClient.PublishAsync` → `RabbitMQSink.EmitBatchAsync` →
  Serilog's `BatchingSink`, which invokes its failure listener. If you wrap the sink with
  `WriteTo.FallbackChain(s => s.RabbitMQ(...), s => s.File(...))`, failed
  events automatically route to the fallback instead of piling up in the batching queue.
- **Self-heals after a 60 s cooldown**: the first `GetAsync` after the cooldown elapses
  claims a probe slot via CAS, attempts one warm-up, and on success transitions the pool
  back to `Warming` (with a background refill of the remaining channels). On probe failure
  the pool re-enters `Broken` with a fresh 60 s cooldown. Concurrent `GetAsync` callers
  during the probe see exhaustion — only one probe runs at a time.
- **Wakes in-flight waiters when the breaker trips**: there are two distinct windows
  to think about:
  - **Pre-trip backoff window** (the time spent retrying warm-up before the breaker
    flips to `Broken` — roughly ~2 minutes with the default `WarmUpMaxRetries = 10`).
    A `GetAsync` parked on the empty channel during this window used to stay hung for
    the rest of the outage. It now wakes the moment the breaker trips and throws the
    exhaustion exception, so `BatchingSink`'s flush loop is unblocked.
  - **Post-trip cooldown window** (the 60 s between trip and the next probe). New
    `GetAsync` calls during this window throw the exhaustion exception synchronously
    via the fail-fast path described above; nothing parks.

  Without the wake on trip, `BatchingSink`'s in-memory queue would grow indefinitely
  behind a hung `EmitBatchAsync`. With it, the hung call surfaces the exhaustion
  exception and subsequent batches drain through the failure listener / `Fallback`
  chain.

Set `WarmUpMaxRetries = null` to preserve the pre-9.0 behaviour of retrying indefinitely. The
backoff schedule itself is not configurable.

### Renamed `MaxChannels` to `ChannelCount`

`RabbitMQClientConfiguration.MaxChannels` has been renamed to `ChannelCount` to reflect that
it is the actual number of channels held in the pool, not an upper bound. The old
`MaxChannels` property remains as a `[Obsolete]` shim that forwards to `ChannelCount` and
will be removed in a future major version.

The `maxChannels` parameter on `WriteTo.RabbitMQ(...)` and `AuditTo.RabbitMQ(...)` has been
renamed to `channelCount`. Update appsettings JSON / `App.config` keys from `maxChannels` to
`channelCount`.

### Breaking changes (9.0.0)

- Removed `net6.0` and `.net9.0` target framework
- Removed dependency on `Microsoft.Extensions.ObjectPool`
- `WriteTo.RabbitMQ` / `AuditTo.RabbitMQ` parameter `maxChannels` renamed to `channelCount`
- `RabbitMQClientConfiguration.MaxChannels` is now `[Obsolete]`; use `ChannelCount`
- `RabbitMQSinkConfiguration.QueueLimit` must be greater than zero when set; zero or
  negative values now throw `ArgumentOutOfRangeException` at configuration time
- `RabbitMQClientConfiguration.ChannelCount` must be greater than zero; zero or
  negative values now throw `ArgumentOutOfRangeException` at configuration time
  (previously silently substituted with the default of 64)
- `RabbitMQClientConfiguration.Validate()` and `RabbitMQSinkConfiguration.Validate()` are
  invoked during sink construction. Misconfiguration that was previously tolerated until
  first publish (missing hostnames, blank credentials, etc.) now throws at startup.
- `RabbitMQSink.EmitBatchAsync` propagates publish exceptions so Serilog's
  `BatchingSink` listener observes them — required for `WriteTo.FallbackChain(...)` /
  `WriteTo.Fallible(...)` composition.
- The in-sink failure sink and `EmitEventFailureHandling` enum have been removed:
  `RabbitMQSinkConfiguration.EmitEventFailure`, the `failureSinkConfiguration`
  extension parameter, and the flat-overload `emitEventFailure` parameter are gone.
  Replace with Serilog core's `WriteTo.FallbackChain(...)` (or
  `WriteTo.Fallible(...)` for listener-based reporting).
- Warm-up now stops after `WarmUpMaxRetries` consecutive failures (default `10`) and a
  broken pool fails `GetAsync` fast instead of blocking waiters. Set
  `WarmUpMaxRetries = null` to opt back into unlimited retries.

### New public surface (9.0.0)

- `RabbitMQClientConfiguration.ChannelCount` (replaces `MaxChannels`)
- `RabbitMQClientConfiguration.WarmUpMaxRetries`
- `RabbitMQClientConfiguration.Validate()`
- `RabbitMQSinkConfiguration.Validate()`
- `RabbitMQSink.SetFailureListener(ILoggingFailureListener)` — `RabbitMQSink` now
  implements `Serilog.Core.ISetLoggingFailureListener`. Relevant for the audit path
  (`AuditTo.RabbitMQ`) and direct-construct users; the batched pipeline still routes
  via `BatchingSink`'s own listener (Serilog does not forward `SetFailureListener`
  to inner sinks).
- `WriteTo.RabbitMQ(client, sink)` and `WriteTo.RabbitMQ(configure)` overloads (the
  failure-sink-free counterparts to the removed `failureSinkConfiguration` overloads).
- `warmUpMaxRetries` parameter on the `WriteTo.RabbitMQ` / `AuditTo.RabbitMQ` flat
  overloads.
