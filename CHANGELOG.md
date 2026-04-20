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

### Breaking changes

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

### Breaking changes

- Upgrade RabbitMQ.Client to `7.0.0`.
- Upgrade Serilog to `4.2.0`.
- Renamed `Period` to `BufferingTimeLimit`.
- Renamed `RouteKey` to `RoutingKey`
- Removed `RoutingFunc`
- Removed `.net7.0` target framework

## 9.0.0 [not published]

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

`RabbitMQSink.EmitBatchAsync` no longer silently swallows publish failures by default.
Exceptions now propagate to Serilog's `BatchingSink`, whose listener plumbing
observes the failure — this is what makes `WriteTo.Fallback(s => s.RabbitMQ(...), fallback: s => s.File(...))`
route failed events to the fallback sink.

The `EmitEventFailureHandling` flags still work and compose as follows:

| Flags set                                | Behaviour                                                                 |
|------------------------------------------|---------------------------------------------------------------------------|
| *(default: `Ignore`)*                    | Rethrow. `BatchingSink` catches, notifies its listener. **(CHANGED — was silent swallow.)** |
| `WriteToSelfLog`                         | Log to `SelfLog`, then rethrow. **(CHANGED — was log-and-swallow.)**      |
| `WriteToFailureSink` *(legacy routing)*  | Emit events to the configured failure sink, **do not rethrow** (unchanged). |
| `WriteToFailureSink | WriteToSelfLog`    | Log + emit, **do not rethrow** (unchanged).                               |
| `ThrowException`                         | Rethrow (unchanged).                                                      |
| `WriteToFailureSink | ThrowException`    | Emit events to the failure sink **and** rethrow (unchanged).              |

**Behaviour change to flag:** users on the `Ignore` default who did not wire
`failureSinkConfiguration` previously saw silent failures. Publish errors now
surface via `BatchingSink`'s default listener (`SelfLog`), or via the listener set
by a `WriteTo.Fallback(...)` wrapper. Consider this an observability upgrade —
silent logging failures are almost always undesirable. To preserve the old
silent behaviour you can set `emitEventFailure = WriteToFailureSink` and wire
`failureSinkConfiguration` to a no-op sink.

`RabbitMQSink` also now implements `ISetLoggingFailureListener` — this is only
relevant for the audit path (`AuditTo.RabbitMQ(...)`) and direct-construct users.
`BatchingSink` does not forward `SetFailureListener` to its inner sink by design,
so the batched pipeline still routes via `BatchingSink`'s own listener as
described above.

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
  `WriteTo.Fallback(primary: s => s.RabbitMQ(...), fallback: s => s.File(...))`, failed
  events automatically route to the fallback instead of piling up in the batching queue.
- **Self-heals after a 60 s cooldown**: the first `GetAsync` after the cooldown elapses
  claims a probe slot via CAS, attempts one warm-up, and on success transitions the pool
  back to `Warming` (with a background refill of the remaining channels). On probe failure
  the pool re-enters `Broken` with a fresh 60 s cooldown. Concurrent `GetAsync` callers
  during the probe see exhaustion — only one probe runs at a time.
- **Wakes in-flight waiters on exhaustion**: a `GetAsync` call that parked on the empty
  channel during the cumulative warm-up backoff (the time spent in the retry loop before
  the breaker trips — roughly ~2 minutes with the default `WarmUpMaxRetries = 10` and
  the backoff schedule above, and shorter/longer when `WarmUpMaxRetries` is tuned) now
  wakes as soon as the breaker trips, instead of staying hung for the rest of the
  outage. Without this, `BatchingSink`'s flush loop would sit behind the hung
  `EmitBatchAsync` and the in-memory event queue would grow indefinitely. With it, the
  hung call surfaces the exhaustion exception and subsequent batches drain through the
  failure listener / `Fallback` chain. The separate 60 s cooldown window (after the
  breaker has tripped, before the next probe) is handled by the fail-fast path above.

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

### Breaking changes

- Removed `net6.0` and `.net9.0` target framework
- Removed dependency on `Microsoft.Extensions.ObjectPool`
- `WriteTo.RabbitMQ` / `AuditTo.RabbitMQ` parameter `maxChannels` renamed to `channelCount`
- `RabbitMQClientConfiguration.MaxChannels` is now `[Obsolete]`; use `ChannelCount`
- `RabbitMQSinkConfiguration.QueueLimit` must be greater than zero when set; zero or
  negative values now throw `ArgumentOutOfRangeException` at configuration time
- `RabbitMQClientConfiguration.ChannelCount` must be greater than zero; zero or
  negative values now throw `ArgumentOutOfRangeException` at configuration time
  (previously silently substituted with the default of 64)
- `RabbitMQSink.EmitBatchAsync` propagates publish exceptions by default instead of
  silently swallowing them. Use `EmitEventFailureHandling.WriteToFailureSink` to keep
  legacy catch-and-route behaviour.
- Warm-up now stops after `WarmUpMaxRetries` consecutive failures (default `10`) and a
  broken pool fails `GetAsync` fast instead of blocking waiters. Set
  `WarmUpMaxRetries = null` to opt back into unlimited retries.
