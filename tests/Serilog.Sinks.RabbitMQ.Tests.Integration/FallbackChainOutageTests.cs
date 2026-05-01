// Copyright 2015-2024 Serilog Contributors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

#if !NETFRAMEWORK

using Serilog.Debugging;
using Testcontainers.RabbitMq;
using Xunit.Abstractions;

namespace Serilog.Sinks.RabbitMQ.Tests.Integration;

/// <summary>
/// Pins the contract that publish failures from <see cref="RabbitMQSink"/> are observed by
/// the surrounding <c>BatchingSink</c> listener and routed to a <c>WriteTo.FallbackChain(...)</c>
/// fallback sink. Uses Testcontainers so the broker is owned by the test (not docker compose) —
/// this lets us deterministically stop the container mid-run.
/// </summary>
public sealed class FallbackChainOutageTests : IAsyncLifetime
{
    private const string Username = "guest";
    private const string Password = "guest";

    private readonly RabbitMqContainer _container = new RabbitMqBuilder("rabbitmq:4.2.2")
        .WithUsername(Username)
        .WithPassword(Password)
        .Build();

    private readonly ITestOutputHelper _output;

    public FallbackChainOutageTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    [Fact]
    public async Task BrokerOutage_RoutesPostFailureBatchesToFallbackFile()
    {
        var fallbackPath = Path.Combine(Path.GetTempPath(), $"fallback-{Guid.NewGuid():N}.txt");
        var selfLog = new StringBuilder();
        SelfLog.Enable(s => selfLog.AppendLine(s));
        try
        {
            // Configure the channel pool to fail fast: WarmUpMaxRetries=1 means the pool
            // transitions to Broken on the first sustained warm-up failure (instead of the
            // default 10-retry exponential backoff that would stretch the test out beyond
            // the CI timeout).
            // Note: do NOT set RestrictedToMinimumLevel on the primary sink config —
            // Serilog wraps the sink in a RestrictedSink which does not forward
            // ISetLoggingFailureListener, breaking the FallbackChain wiring.
            // Apply level filtering at the logger level instead.
            using var logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.FallbackChain(
                    primary => primary.RabbitMQ((c, s) =>
                    {
                        c.Hostnames = ["localhost"];
                        c.Port = _container.GetMappedPublicPort(5672);
                        c.Username = Username;
                        c.Password = Password;
                        c.Exchange = "test-exchange";
                        c.ExchangeType = "fanout";
                        c.AutoCreateExchange = true;
                        c.ChannelCount = 2;
                        c.WarmUpMaxRetries = 1;
                        s.BatchPostingLimit = 5;
                        s.BufferingTimeLimit = TimeSpan.FromMilliseconds(200);
                    }),
                    fallback => fallback.File(fallbackPath))
                .CreateLogger();

            // Drive a few events while the broker is healthy. They should land at the broker,
            // not the fallback. We do not assert on broker queue contents here — that's the
            // responsibility of WriteToRabbitMQSinkTests; this test only cares that the
            // FallbackChain wrapper observes the rethrow when the broker dies.
            for (var i = 0; i < 5; i++)
            {
                logger.Information("pre-outage event {Index}", i);
            }

            // Allow the warm-up batch to flush before killing the broker, so we know the
            // fallback file's contents come from POST-outage batches only.
            await Task.Delay(750);

            await _container.StopAsync();

            // Drive enough events that BatchPostingLimit (=5) triggers multiple batches.
            // Each batch's PublishAsync now fails (broker socket dead); EmitBatchAsync
            // rethrows; BatchingSink hands the batch to its failure listener; the listener
            // (set by WriteTo.FallbackChain) re-emits to the File fallback.
            const int postOutageEventCount = 25;
            for (var i = 0; i < postOutageEventCount; i++)
            {
                logger.Information("post-outage event {Index}", i);
            }

            // Force flush — drains the BatchingSink queue, runs the failing publishes,
            // routes everything to the fallback before disposal. Disposing the logger is
            // sync-bridged into the async disposal path; the BatchingSink waits for its
            // pump to drain before returning, so any mid-flight batches are emitted (and
            // their failures observed by the FallbackChain listener) inside Dispose.
            logger.Dispose();

            // Allow the File sink to flush its writer before we read.
            await Task.Delay(2000);

            _output.WriteLine("SelfLog output:\n" + selfLog);

            System.IO.File.Exists(fallbackPath).ShouldBeTrue($"fallback file '{fallbackPath}' must exist after broker outage");

            var lines = await System.IO.File.ReadAllLinesAsync(fallbackPath);
            _output.WriteLine($"fallback file line count: {lines.Length}");
            foreach (var line in lines.Take(5))
            {
                _output.WriteLine("  " + line);
            }

            lines.Length.ShouldBeGreaterThan(0, "fallback file must contain at least one event after broker outage");

            var postOutageHits = lines.Count(l => l.Contains("post-outage event"));
            postOutageHits.ShouldBeGreaterThan(0, "expected post-outage events in the fallback file");
        }
        finally
        {
            SelfLog.Disable();
            if (System.IO.File.Exists(fallbackPath))
            {
                System.IO.File.Delete(fallbackPath);
            }
        }
    }
}

#endif
