# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project shape

A Serilog sink that publishes log events to RabbitMQ via `RabbitMQ.Client` v7. The package targets `netstandard2.0`, `net8.0`, and `net10.0` (see [Directory.Build.props](Directory.Build.props) and [src/Serilog.Sinks.RabbitMQ/Serilog.Sinks.RabbitMQ.csproj](src/Serilog.Sinks.RabbitMQ/Serilog.Sinks.RabbitMQ.csproj)). Test projects target `net48;net8.0;net10.0`.

Solution file is the new `.slnx` format: [Serilog.Sinks.RabbitMQ.slnx](Serilog.Sinks.RabbitMQ.slnx).

## Build, format, test

```bash
# Build everything (all TFMs)
dotnet build

# Run the same formatting check CI runs — gates the PR build
dotnet format --no-restore --verify-no-changes --severity warn

# Unit tests only, single TFM (fastest dev loop)
dotnet test tests/Serilog.Sinks.RabbitMQ.Tests/Serilog.Sinks.RabbitMQ.Tests.csproj --framework net10.0

# Single test by name
dotnet test tests/Serilog.Sinks.RabbitMQ.Tests/Serilog.Sinks.RabbitMQ.Tests.csproj \
  --framework net10.0 --filter FullyQualifiedName~RabbitMQChannelPoolTests.WarmUp_RetriesAfterTransientFailure

# Integration tests (need brokers — see below)
docker compose up -d
dotnet test tests/Serilog.Sinks.RabbitMQ.Tests.Integration/Serilog.Sinks.RabbitMQ.Tests.Integration.csproj --framework net10.0

# Code coverage — ALWAYS run this locally before COMMITTING changes that add/modify
# src/. Codecov runs on CI and will flag missing patch coverage; catch it here first
# so you don't record a commit that needs a follow-up coverage fix.
dotnet test tests/Serilog.Sinks.RabbitMQ.Tests/Serilog.Sinks.RabbitMQ.Tests.csproj \
  --framework net10.0 -c Release \
  -p:CollectCoverage=true -p:CoverletOutputFormat=opencover \
  -p:CoverletOutput=./out/.coverage/
# Inspect out/.coverage/coverage.net10.0.opencover.xml for any SequencePoint/BranchPoint
# with vc="0" in methods you added or touched. Add tests until there are none.
```

Two RabbitMQ brokers come up via [docker-compose.yml](docker-compose.yml): `rabbitmq-plain` on 5672/6672 and `rabbitmq-cert` on 5671. Test fixtures wait for `rabbitmqctl status`.

`net48` tests are intentionally skipped on Linux CI — `coverlet.msbuild` 10.x emits IL Mono can't load. Windows CI still validates net48. Locally, run net48 only on Windows or via `--framework net8.0|net10.0`.

## Pre-commit checklist

Before `git commit`:

1. `dotnet build -c Release --no-restore` on all TFMs (catches net48 API mismatches that only surface at build time).
2. Full unit test suite on **net10.0 AND net8.0** (no `--filter`; parallel-class races sometimes only show up in the full run).
3. `dotnet format --no-restore --verify-no-changes --severity warn` — CI gate.
4. **Code coverage** on any method you added or modified in `src/` — see the coverlet command above. Zero uncovered lines/branches on new code.
5. For integration-test-touching changes: run integration tests on net10.0 against the docker-compose brokers.
6. **Update [CHANGELOG.md](CHANGELOG.md) and [README.md](README.md) whenever the change is user-visible** — new/changed/removed public API, behaviour changes, new configuration options, migration notes, or anything a consumer would need to read about before upgrading. Pure internals / test-only changes don't require updates, but default to updating when in doubt. For breaking changes, extend the `Migrating to X.Y.Z` section in README.

Skipping step 4 has repeatedly meant shipping a commit, watching Codecov flag it, then following up. Don't.

## Public API gate

`ApiApprovalTests` (in [tests/Serilog.Sinks.RabbitMQ.Tests/Approval/](tests/Serilog.Sinks.RabbitMQ.Tests/Approval/)) uses `PublicApiGenerator` to snapshot the public surface against [Serilog.Sinks.RabbitMQ.approved.txt](tests/Serilog.Sinks.RabbitMQ.Tests/Approval/Serilog.Sinks.RabbitMQ.approved.txt). When you intentionally change the public API:

1. Run the test — it writes a `.received.txt` next to the approved file.
2. `diff` the two and verify the change is what you intended.
3. `mv` `.received.txt` over `.approved.txt`.

Don't hand-edit `.approved.txt`.

## Architecture

Two extension methods are the public entry points: `WriteTo.RabbitMQ(...)` (batched, `IBatchedLogEventSink`) and `AuditTo.RabbitMQ(...)` (synchronous, throws on failure). Both live in [LoggerConfigurationRabbitMQExtensions.cs](src/Serilog.Sinks.RabbitMQ/LoggerConfigurationRabbitMQExtensions.cs).

The sink ([RabbitMQSink.cs](src/Serilog.Sinks.RabbitMQ/Sinks/RabbitMQ/RabbitMQSink.cs)) implements **both** `IBatchedLogEventSink` and `ILogEventSink`. The sync `Emit` path bridges through [AsyncHelpers.RunSync](src/Serilog.Sinks.RabbitMQ/Sinks/RabbitMQ/AsyncHelpers.cs) — this is the only place sync-over-async is acceptable, because it's the entry point Serilog gives us when an audit sink emits synchronously. Do not introduce new `RunSync` calls deeper in the pipeline.

`RabbitMQClient` ([RabbitMQClient.cs](src/Serilog.Sinks.RabbitMQ/Sinks/RabbitMQ/RabbitMQClient.cs)) owns two collaborators:
- `IRabbitMQConnectionFactory` — single, lazily-created `IConnection`, gated by a `SemaphoreSlim`.
- `IRabbitMQChannelPool` — fixed-size pool of `IRabbitMQChannel` instances.

### Channel pool semantics ([RabbitMQChannelPool.cs](src/Serilog.Sinks.RabbitMQ/Sinks/RabbitMQ/RabbitMQChannelPool.cs))

- Pool size is fixed at `RabbitMQClientConfiguration.ChannelCount` (default 64). It does **not** grow on demand.
- All channels are pre-opened in the background at construction (`Task.Run(WarmUpAsync)`).
- `GetAsync` awaits a `SemaphoreSlim` — callers wait when every channel is in use rather than spawning a new one.
- `Return` discards channels whose `IsOpen` flipped to false and triggers a one-shot background re-warmup so the pool stays full.
- `Dispose` is idempotent (`Interlocked.Exchange` guard) and cancels in-flight warm-up before draining and disposing retained channels.

`MaxChannels` is an `[Obsolete]` forwarding shim to `ChannelCount`. Use `ChannelCount` everywhere; don't introduce new references to `MaxChannels`.

### Internal vs public surface

Almost everything except `RabbitMQSink`, `RabbitMQClientConfiguration`, `RabbitMQSinkConfiguration`, `LoggerConfigurationRabbitMQExtensions`, `ISendMessageEvents`, and the two enums is `internal`. The test assemblies have `InternalsVisibleTo` so substitutes can target the internal interfaces (`IRabbitMQChannel`, `IRabbitMQChannelPool`, `IRabbitMQConnectionFactory`).

## Conventions to know before editing

- **Central package management** is on. Add new packages to [Directory.Packages.props](Directory.Packages.props), not the individual csprojs. Multi-targeted packages stay single-versioned across TFMs (we removed the per-TFM `Microsoft.Extensions.ObjectPool` indirection in 9.0.0).
- **`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`** is set globally. Test projects suppress `CS1591;SA1600` so doc-comment warnings don't apply to tests, but they apply to `src/`.
- **StyleCop** runs with `documentInternalElements: false` (see [stylecop.json](stylecop.json)) — internal members do not need XML docs in general, but interface implementations exposed via `InternalsVisibleTo` are still flagged. Use `/// <inheritdoc />` rather than re-documenting the interface.
- **CodeQL `cs/catch-of-all-exceptions`** is configured as `note` severity. The one accepted suppression is on `RabbitMQChannelPool.WarmUpAsync` — broad catch is intentional so transient broker errors don't take the sink down. New broad catches need a similar justification.
- **Naming**: private fields use `_camelCase`; constants are `UPPER_CASE` (see [.editorconfig](.editorconfig)); async methods must end in `Async`.
- **`coverlet.msbuild`** must stay on a version compatible with Mono if/when net48 testing returns to Linux. Currently pinned at 10.x and Linux net48 is excluded in [.github/workflows/tests.yml](.github/workflows/tests.yml).
- **Per CONTRIBUTING.md**: every PR must reference an issue and target `master`.

## Useful pointers

- Three runnable end-to-end samples live under [samples/](samples/): code-only (`NetFromCodeSample`), `appsettings.json` (`NetAppsettingsJsonSample`, includes a custom `ISendMessageEvents`), and `App.config` for .NET Framework (`NetFrameworkAppSettingsConfigSample`). The two non-Framework samples talk to the docker-compose broker.
- TLS / SSL test certificates and how they were generated: [docker/rabbitmq/README.md](docker/rabbitmq/README.md). Self-signed, **not for production**.
- Release notes and migration guidance live in [CHANGELOG.md](CHANGELOG.md) and [README.md](README.md#migrating-to-900) — both should be updated for any breaking change.
