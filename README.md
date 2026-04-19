# Serilog.Sinks.RabbitMQ

![License](https://img.shields.io/github/license/ArieGato/serilog-sinks-rabbitmq)
[![codecov](https://codecov.io/gh/ArieGato/serilog-sinks-rabbitmq/graph/badge.svg?token=4uZ4oaOQUE)](https://codecov.io/gh/ArieGato/serilog-sinks-rabbitmq)
[![Nuget](https://img.shields.io/nuget/dt/Serilog.Sinks.RabbitMQ)](https://www.nuget.org/packages/Serilog.Sinks.RabbitMQ)
[![Nuget](https://img.shields.io/nuget/v/Serilog.Sinks.RabbitMQ)](https://www.nuget.org/packages/Serilog.Sinks.RabbitMQ)

[![GitHub Release Date](https://img.shields.io/github/release-date/ArieGato/serilog-sinks-rabbitmq?label=released)](https://github.com/ArieGato/serilog-sinks-rabbitmq/releases)
[![GitHub commits since latest release (by date)](https://img.shields.io/github/commits-since/ArieGato/serilog-sinks-rabbitmq/latest?label=new+commits)](https://github.com/ArieGato/serilog-sinks-rabbitmq/commits/master)
![Size](https://img.shields.io/github/repo-size/ArieGato/serilog-sinks-rabbitmq)

[![GitHub contributors](https://img.shields.io/github/contributors/ArieGato/serilog-sinks-rabbitmq)](https://github.com/ArieGato/serilog-sinks-rabbitmq/graphs/contributors)
![Activity](https://img.shields.io/github/commit-activity/w/ArieGato/serilog-sinks-rabbitmq)
![Activity](https://img.shields.io/github/commit-activity/m/ArieGato/serilog-sinks-rabbitmq)
![Activity](https://img.shields.io/github/commit-activity/y/ArieGato/serilog-sinks-rabbitmq)

[![Run unit tests](https://github.com/ArieGato/serilog-sinks-rabbitmq/actions/workflows/tests.yml/badge.svg)](https://github.com/ArieGato/serilog-sinks-rabbitmq/actions/workflows/tests.yml)
[![Publish preview to GitHub registry](https://github.com/ArieGato/serilog-sinks-rabbitmq/actions/workflows/publish-prerelease.yml/badge.svg)](https://github.com/ArieGato/serilog-sinks-rabbitmq/actions/workflows/publish-prerelease.yml)
[![Publish release to Nuget registry](https://github.com/ArieGato/serilog-sinks-rabbitmq/actions/workflows/publish-release.yml/badge.svg)](https://github.com/ArieGato/serilog-sinks-rabbitmq/actions/workflows/publish-release.yml)
[![CodeQL analysis](https://github.com/ArieGato/serilog-sinks-rabbitmq/actions/workflows/codeql-analysis.yml/badge.svg)](https://github.com/ArieGato/serilog-sinks-rabbitmq/actions/workflows/codeql-analysis.yml)

> A [Serilog](https://serilog.net/) sink that publishes log events to RabbitMQ via the
> official [RabbitMQ.Client](https://www.nuget.org/packages/RabbitMQ.Client) library.

The aim of this sink is to expose `RabbitMQ.Client` functionality in an idiomatic Serilog
way without burying it behind extra abstractions. Expect plain RabbitMQ behaviour with a
slightly simpler surface.

## Table of contents

- [Features](#features)
- [Installation](#installation)
- [Quick start](#quick-start)
- [Configuration](#configuration)
  - [Programmatic](#programmatic)
  - [appsettings.json](#appsettingsjson)
  - [App.config](#appconfig)
- [Configuration reference](#configuration-reference)
  - [Connection](#connection)
  - [Exchange and routing](#exchange-and-routing)
  - [TLS / SSL](#tls--ssl)
  - [Channel pool](#channel-pool)
  - [Batching](#batching)
  - [Failure handling](#failure-handling)
- [Audit sink](#audit-sink)
- [Customising message properties and routing keys](#customising-message-properties-and-routing-keys)
- [Multi-host configuration](#multi-host-configuration)
- [Samples](#samples)
- [Compatibility matrix](#compatibility-matrix)
- [Migrating to 9.0.0](#migrating-to-900)
- [References](#references)
- [License](#license)

## Features

- Publishes log events through `RabbitMQ.Client` v7.
- Supports both `WriteTo` (batched) and `AuditTo` (synchronous, throws on failure) sinks.
- Eagerly opens a fixed pool of channels at startup; broken channels are replaced in the
  background.
- Optional automatic exchange declaration.
- TLS / SSL support, multi-host clusters, dynamic routing keys, custom message properties.
- Pluggable failure sinks for fan-out on emit errors.
- Targets `netstandard2.0`, `net8.0`, and `net10.0`.

## Installation

```bash
dotnet add package Serilog.Sinks.RabbitMQ
```

Or via the Package Manager Console:

```powershell
Install-Package Serilog.Sinks.RabbitMQ
```

## Quick start

```csharp
using Serilog;
using Serilog.Sinks.RabbitMQ;

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.RabbitMQ(
        hostnames: ["localhost"],
        username: "guest",
        password: "guest",
        exchange: "logs",
        exchangeType: "topic",
        autoCreateExchange: true)
    .CreateLogger();

Log.Information("Hello RabbitMQ");
Log.CloseAndFlush();
```

The sink eagerly opens 64 channels in the background at startup. See
[Channel pool](#channel-pool) to tune.

## Configuration

The sink can be configured from code, from `appsettings.json` (via
[Serilog.Settings.Configuration](https://github.com/serilog/serilog-settings-configuration)),
or from `App.config` (via
[Serilog.Settings.AppSettings](https://github.com/serilog/serilog-settings-appsettings)).
All approaches accept the same set of options — see [Configuration reference](#configuration-reference).

### Programmatic

The recommended way is the action-based overload, which gives strongly typed access to both
the client and the sink configuration:

```csharp
Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.RabbitMQ((client, sink) =>
    {
        client.Hostnames    = ["localhost"];
        client.Username     = "guest";
        client.Password     = "guest";
        client.Exchange     = "logs";
        client.ExchangeType = "topic";
        client.DeliveryMode = RabbitMQDeliveryMode.Durable;
        client.RoutingKey   = "log";
        client.ChannelCount = 32;

        sink.TextFormatter      = new Serilog.Formatting.Json.JsonFormatter();
        sink.BatchPostingLimit  = 100;
        sink.BufferingTimeLimit = TimeSpan.FromSeconds(2);
    })
    .CreateLogger();
```

A flat-parameter overload is also available for simple setups (see [Quick start](#quick-start)).

### appsettings.json

```json
{
  "Serilog": {
    "Using": [ "Serilog.Sinks.RabbitMQ" ],
    "MinimumLevel": "Information",
    "WriteTo": [
      {
        "Name": "RabbitMQ",
        "Args": {
          "clientConfiguration": {
            "hostnames": [ "localhost" ],
            "username": "guest",
            "password": "guest",
            "exchange": "logs",
            "exchangeType": "topic",
            "deliveryMode": "Durable",
            "routingKey": "log",
            "autoCreateExchange": true,
            "channelCount": 32
          },
          "sinkConfiguration": {
            "batchPostingLimit": 100,
            "bufferingTimeLimit": "00:00:02",
            "textFormatter": "Serilog.Formatting.Json.JsonFormatter, Serilog"
          }
        }
      }
    ]
  }
}
```

Keys are case-insensitive.

### App.config

```xml
<add key="serilog:using:RabbitMQ" value="Serilog.Sinks.RabbitMQ" />
<add key="serilog:write-to:RabbitMQ.hostnames" value="server1,server2" />
<add key="serilog:write-to:RabbitMQ.username" value="guest" />
<add key="serilog:write-to:RabbitMQ.password" value="guest" />
<add key="serilog:write-to:RabbitMQ.exchange" value="logs" />
<add key="serilog:write-to:RabbitMQ.batchPostingLimit" value="100" />
<add key="serilog:write-to:RabbitMQ.bufferingTimeLimit" value="00:00:02" />
```

## Configuration reference

### Connection

| Option | Type | Default | Description |
|---|---|---|---|
| `hostnames` | `string[]` | _required_ | One or more broker hostnames. See [Multi-host configuration](#multi-host-configuration). |
| `username` | `string` | _required_ | Authentication user. |
| `password` | `string` | _required_ | Authentication password. |
| `port` | `int` | `0` | Broker port. `0` defaults to the RabbitMQ.Client default (5672 / 5671). |
| `vHost` | `string` | `""` | Virtual host. |
| `heartbeat` | `ushort` | `0` (broker default) | Heartbeat interval in milliseconds. |
| `clientProvidedName` | `string?` | `null` | Connection name shown in the RabbitMQ Management UI. |

### Exchange and routing

| Option | Type | Default | Description |
|---|---|---|---|
| `exchange` | `string` | `""` | Target exchange name. |
| `exchangeType` | `string` | `"fanout"` | Exchange type (`direct`, `fanout`, `topic`, `headers`). |
| `deliveryMode` | `RabbitMQDeliveryMode` | `NonDurable` | Persistence of published messages. |
| `routingKey` | `string` | `""` | Default routing key. Can be overridden per event via `ISendMessageEvents`. |
| `autoCreateExchange` | `bool` | `false` | Declare the exchange on startup if it does not exist. |

### TLS / SSL

| Option | Type | Default | Description |
|---|---|---|---|
| `sslEnabled` | `bool` | `false` | Enable TLS for broker connections. |
| `sslServerName` | `string?` | first hostname | Server name used for certificate validation. |
| `sslVersion` | `SslProtocols` | `None` | TLS protocol version. |
| `sslAcceptablePolicyErrors` | `SslPolicyErrors` | `None` | Tolerated certificate validation errors. |
| `sslCheckCertificateRevocation` | `bool` | `false` | Check certificate revocation status. |

### Channel pool

| Option | Type | Default | Description |
|---|---|---|---|
| `channelCount` | `int` | `64` | Number of channels held in the pool. Channels are opened eagerly in the background at startup; broken channels are replaced automatically. When all channels are in use, additional publish calls await until one is returned. |

> **Deprecated:** `maxChannels` (parameter) and `MaxChannels` (property) are kept as
> `[Obsolete]` shims that forward to `channelCount` / `ChannelCount`. They will be removed in
> a future major version. See [Migrating to 9.0.0](#migrating-to-900).

### Batching

These options apply to `WriteTo.RabbitMQ` only. Audit sinks write each event synchronously
and ignore them.

| Option | Type | Default | Description |
|---|---|---|---|
| `batchPostingLimit` | `int` | `50` | Maximum events written per batch. |
| `bufferingTimeLimit` | `TimeSpan` | `2s` | Flush interval for partial batches. |
| `queueLimit` | `int?` | `null` | Maximum buffered events. `null` = unbounded. |

### Failure handling

| Option | Type | Default | Description |
|---|---|---|---|
| `emitEventFailure` | `EmitEventFailureHandling` | `WriteToSelfLog` | Combination of `Ignore`, `WriteToSelfLog`, `WriteToFailureSink`, `ThrowException`. See behaviour table below. |
| `failureSinkConfiguration` | `Action<LoggerSinkConfiguration>?` | `null` | Legacy sink(s) that receive events when the primary sink fails. Used together with `WriteToFailureSink`. Prefer `WriteTo.Fallback(...)` for new code. |
| `formatter` | `ITextFormatter?` | `CompactJsonFormatter` | Formatter used to render the event into the message body. |
| `levelSwitch` | `LogEventLevel` | `Verbose` | Minimum level for events emitted by the sink. |
| `sendMessageEvents` | `ISendMessageEvents?` | `null` | Hooks for customising message properties and routing keys (see below). |

`WriteTo.RabbitMQ` runs inside Serilog's `BatchingSink`. When a batch fails, the sink's behaviour depends on `emitEventFailure`:

| Flags | Behaviour |
|---|---|
| `Ignore` | Rethrow. BatchingSink's failure listener observes the exception (defaults to `SelfLog`). |
| `WriteToSelfLog` (default) | Log to `SelfLog`, then rethrow. |
| `ThrowException` | Rethrow (same as `Ignore`; kept for clarity). |
| `WriteToFailureSink` | Route events to the legacy failure sink; do **not** rethrow. |
| `WriteToFailureSink \| WriteToSelfLog` | Log and route to failure sink; do not rethrow. |
| `WriteToFailureSink \| ThrowException` | Route to failure sink **and** rethrow. |

#### Recommended: `WriteTo.Fallback(...)` (Serilog 4.1+)

For new code, use Serilog's native fallback chain instead of `failureSinkConfiguration`:

```csharp
Log.Logger = new LoggerConfiguration()
    .WriteTo.Fallback(
        primary => primary.RabbitMQ((client, sink) =>
        {
            client.Hostnames = ["localhost"];
            client.Username  = "guest";
            client.Password  = "guest";
            client.Exchange  = "logs";
        }),
        fallback => fallback.Console())
    .CreateLogger();
```

Leave `emitEventFailure` at its default — the fallback chain relies on the sink rethrowing.

#### Legacy: `failureSinkConfiguration`

```csharp
Log.Logger = new LoggerConfiguration()
    .WriteTo.RabbitMQ(
        configure: (client, sink) =>
        {
            client.Hostnames = ["localhost"];
            client.Username  = "guest";
            client.Password  = "guest";
            client.Exchange  = "logs";

            sink.EmitEventFailure = EmitEventFailureHandling.WriteToSelfLog
                                  | EmitEventFailureHandling.WriteToFailureSink;
        },
        failureSinkConfiguration: failure => failure.Console())
    .CreateLogger();
```

## Audit sink

A Serilog audit sink writes events that must succeed and surfaces exceptions to the caller.
Use `AuditTo.RabbitMQ` instead of `WriteTo.RabbitMQ`. Wrap audit logging calls in
`try/catch` to handle failures.

```csharp
Log.Logger = new LoggerConfiguration()
    .AuditTo.RabbitMQ((client, sink) =>
    {
        client.Hostnames    = ["localhost"];
        client.Username     = "guest";
        client.Password     = "guest";
        client.Exchange     = "audit";
        client.DeliveryMode = RabbitMQDeliveryMode.Durable;
    })
    .CreateLogger();
```

The audit sink supports the same options as the regular sink **except** the batching options
(`batchPostingLimit`, `bufferingTimeLimit`, `queueLimit`).

## Customising message properties and routing keys

Implement `ISendMessageEvents` to set per-event message properties or compute a routing key
from the log event.

```csharp
public class CustomMessageEvents : ISendMessageEvents
{
    public void OnSetMessageProperties(LogEvent logEvent, IBasicProperties properties)
    {
        properties.Headers = new Dictionary<string, object?>
        {
            ["log-level"] = logEvent.Level.ToString(),
        };

        if (logEvent.Properties.TryGetValue("CorrelationId", out var correlationId))
        {
            properties.CorrelationId = correlationId.ToString();
        }
    }

    public string OnGetRoutingKey(LogEvent logEvent, string defaultRoutingKey) =>
        logEvent.Level switch
        {
            LogEventLevel.Error => "error",
            _ => defaultRoutingKey,
        };
}
```

Wire it up via `RabbitMQClientConfiguration.SendMessageEvents`:

```csharp
client.SendMessageEvents = new CustomMessageEvents();
```

## Multi-host configuration

Pass multiple hostnames to connect to a RabbitMQ cluster. The client will attempt each in
turn until one succeeds.

```xml
<add key="serilog:using:RabbitMQ" value="Serilog.Sinks.RabbitMQ" />
<add key="serilog:write-to:RabbitMQ.hostnames" value="host1,host2,host3" />
<add key="serilog:write-to:RabbitMQ.username" value="guest" />
<add key="serilog:write-to:RabbitMQ.password" value="guest" />
```

## Samples

Three runnable sample projects live under [`samples/`](samples/):

| Sample | Configuration style | Target |
|---|---|---|
| [`NetFromCodeSample`](samples/NetFromCodeSample/) | Pure code, includes audit sink and failure sink | `net10.0` |
| [`NetAppsettingsJsonSample`](samples/NetAppsettingsJsonSample/) | `appsettings.json` via `Serilog.Settings.Configuration`, includes a custom `ISendMessageEvents` | `net10.0` |
| [`NetFrameworkAppSettingsConfigSample`](samples/NetFrameworkAppSettingsConfigSample/) | `App.config` via `Serilog.Settings.AppSettings` | `net48` |

A `docker-compose.yml` at the repo root brings up a RabbitMQ broker for running the samples
and integration tests.

## Compatibility matrix

| Serilog.Sinks.RabbitMQ | .NETStandard | .NETFramework | Serilog | RabbitMQ.Client |
|---|---|---|---|---|
| 2.0.0 | 1.6.0 | 4.5.1 | 2.3.0 | 4.* |
| 3.0.0 | 1.6.1 | 4.5.1 | 2.8.0 | 5.* |
| 6.0.0 | 2.0.0 | 4.7.2 | 2.8.0 | 6.* |
| 7.0.0 | 2.0.0 | — | 3.1.1 | 6.8.* |
| 8.0.0 | 2.0.0 | — | 4.2.0 | 7.0 |
| 9.0.0 | 2.0.0 | — | 4.3.x | 7.2.x |

## Migrating to 9.0.0

- **`MaxChannels` is now `ChannelCount`.** The property and the `maxChannels` parameter on
  `WriteTo.RabbitMQ` / `AuditTo.RabbitMQ` have been renamed to `ChannelCount` and
  `channelCount`. The property keeps an `[Obsolete]` shim so existing code compiles with a
  warning; the parameter is a hard rename — update `appsettings.json` / `App.config` keys
  from `maxChannels` to `channelCount`.
- **Channel pool semantics changed.** The pool is now fixed-size and pre-opens all channels
  in the background at startup. When all channels are in use, additional publish calls
  await until one is returned (previous behaviour grew the pool on demand). Broken channels
  are replaced automatically in the background.
- **Failure handling aligns with Serilog's `BatchingSink`.** `EmitBatchAsync` now rethrows
  by default so the BatchingSink's failure listener observes the error (it writes to
  `SelfLog` unless a listener is configured). Previously, failures were swallowed when
  `WriteToFailureSink` was not set. If you relied on the old silent behaviour, set
  `emitEventFailure` to `WriteToFailureSink` with a `failureSinkConfiguration`, or wire a
  `WriteTo.Fallback(...)` chain. `AuditTo.RabbitMQ` also notifies any configured
  `ILoggingFailureListener` before rethrowing.
- **`WriteTo.Fallback(...)` is now the recommended pattern.** Serilog 4.1's native fallback
  chain is preferred over `failureSinkConfiguration`. See the [Failure handling](#failure-handling)
  section.
- **`Validate()` on configuration objects.** `RabbitMQClientConfiguration` and
  `RabbitMQSinkConfiguration` now expose public `Validate()` methods and are invoked during
  sink construction. Misconfiguration (missing hostnames, invalid port, zero batch limit,
  etc.) throws at startup instead of failing at first publish.
- **`QueueLimit` validation.** `QueueLimit`, when set, must be greater than zero —
  `QueueLimit = 0` now throws `ArgumentOutOfRangeException` rather than silently creating a
  zero-capacity queue. Leave the property unset (`null`) for an unbounded queue.
- **`Microsoft.Extensions.ObjectPool` dependency removed.** No action needed unless your
  application referenced it transitively through this package.
- **Target frameworks:** `net6.0` and `net9.0` were removed. Supported targets are
  `netstandard2.0`, `net8.0`, and `net10.0`.

See [`CHANGELOG.md`](CHANGELOG.md) for the full release notes.

## References

- [Serilog](https://serilog.net/)
- [RabbitMQ .NET client](https://github.com/rabbitmq/rabbitmq-dotnet-client)
- [Serilog.Settings.Configuration](https://github.com/serilog/serilog-settings-configuration)
- [Serilog.Settings.AppSettings](https://github.com/serilog/serilog-settings-appsettings)
- [Logging in ASP.NET Core](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/logging)

## License

Apache-2.0 — see [LICENSE](LICENSE).
