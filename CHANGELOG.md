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
messages are preserved. `RabbitMQSinkConfiguration.Validate()` is new and
additionally checks:

- `TextFormatter` is non-null
- `BatchPostingLimit > 0`
- `BufferingTimeLimit >= TimeSpan.Zero`
- `QueueLimit > 0` when set — **tightened constraint**: a zero or negative
  `QueueLimit` previously passed through to Serilog's batching layer silently;
  it is now rejected at configuration time.

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
