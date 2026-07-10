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

using System.Diagnostics;
using System.IO;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Serilog;
using Serilog.Debugging;
using Serilog.Sinks.RabbitMQ;

const string Exchange = "LoadTestExchange";
const string Queue = "LoadTestQueue";
const string FailureSinkPath = "./log/failure.txt";
const string Host = "localhost";
const string User = "serilog";
const string Pass = "serilog";

var options = LoadTestOptions.Parse(args);
Console.WriteLine(options.Describe());

SelfLog.Enable(Console.Error);

await DeclareQueueAsync().ConfigureAwait(false);
await PurgeQueueAsync().ConfigureAwait(false);

if (File.Exists(FailureSinkPath))
{
    File.Delete(FailureSinkPath);
}

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.FallbackChain(
        primary => primary.RabbitMQ(
            (clientConfiguration, sinkConfiguration) =>
            {
                sinkConfiguration.BatchPostingLimit = options.BatchPostingLimit;
                sinkConfiguration.BufferingTimeLimit = TimeSpan.FromSeconds(2);
                sinkConfiguration.QueueLimit = options.QueueLimit;

                // Do NOT set RestrictedToMinimumLevel here — Serilog wraps the sink in a
                // RestrictedSink which doesn't forward ISetLoggingFailureListener, silently
                // breaking FallbackChain. Filter at the logger level above instead.
                sinkConfiguration.TextFormatter = new Serilog.Formatting.Json.JsonFormatter();

                clientConfiguration.AutoCreateExchange = true;
                clientConfiguration.ChannelCount = options.ChannelCount;
                clientConfiguration.DeliveryMode = RabbitMQDeliveryMode.NonDurable;
                clientConfiguration.Exchange = Exchange;
                clientConfiguration.ExchangeType = "direct";
                clientConfiguration.Hostnames = [Host];
                clientConfiguration.Username = User;
                clientConfiguration.Password = Pass;
            }),
        fallback => fallback.File(FailureSinkPath))
    .CreateLogger();

var produced = 0L;
var producedBytes = 0L;
var sizeMix = SizeMix.Default;
using var cts = new CancellationTokenSource(options.Duration);
var sw = Stopwatch.StartNew();

using var monitorCts = new CancellationTokenSource();
var monitorTask = Task.Run(() => MonitorQueueDepthAsync(monitorCts.Token));

var tasks = Enumerable.Range(0, options.Concurrency)
    .Select(workerId => Task.Run(() => ProduceAsync(workerId, cts.Token)))
    .ToArray();

try
{
    await Task.WhenAll(tasks).ConfigureAwait(false);
}
catch (OperationCanceledException)
{
    // expected when the duration timer fires
}

sw.Stop();
await monitorCts.CancelAsync().ConfigureAwait(false);
try
{
    await monitorTask.ConfigureAwait(false);
}
catch (OperationCanceledException)
{
    // expected
}

Console.WriteLine($"Producers stopped after {sw.Elapsed.TotalSeconds:F2}s. Flushing...");

var flushSw = Stopwatch.StartNew();
await Log.CloseAndFlushAsync().ConfigureAwait(false);
flushSw.Stop();
Console.WriteLine($"Flush completed in {flushSw.Elapsed.TotalSeconds:F2}s.");

// Give the broker a moment to settle before counting.
await Task.Delay(500).ConfigureAwait(false);

uint landed;
try
{
    landed = await CountQueueAsync().ConfigureAwait(false);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Could not count broker queue because the operation was canceled.");
    landed = 0;
}
catch (BrokerUnreachableException ex)
{
    Console.WriteLine($"Could not count broker queue (broker unreachable): {ex.GetType().Name}: {ex.Message}");
    landed = 0;
}
catch (AlreadyClosedException ex)
{
    Console.WriteLine($"Could not count broker queue (connection/channel closed): {ex.GetType().Name}: {ex.Message}");
    landed = 0;
}
catch (IOException ex)
{
    Console.WriteLine($"Could not count broker queue (I/O error): {ex.GetType().Name}: {ex.Message}");
    landed = 0;
}

var brokerRejected = CountFailureSinkLines(FailureSinkPath);

PrintSummary(produced, producedBytes, landed, brokerRejected, sw.Elapsed);

return;

async Task ProduceAsync(int workerId, CancellationToken ct)
{
    var rng = new Random(unchecked((Environment.TickCount * 17) + workerId));
    var perTaskRateLimitNs = options.RatePerSecondPerTask > 0
        ? (long)(1_000_000_000.0 / options.RatePerSecondPerTask)
        : 0L;
    var nextTick = Stopwatch.GetTimestamp();
    var nsPerStopwatchTick = 1_000_000_000.0 / Stopwatch.Frequency;

    while (!ct.IsCancellationRequested)
    {
        var size = sizeMix.Pick(rng);
        var payload = BuildPayload(size, rng);
        Log.Information(
            "Worker {WorkerId} bucket {Bucket} payload {Payload}",
            workerId,
            size.Name,
            payload);
        Interlocked.Increment(ref produced);
        Interlocked.Add(ref producedBytes, payload.Length);

        if (perTaskRateLimitNs > 0)
        {
            nextTick += (long)(perTaskRateLimitNs / nsPerStopwatchTick);
            var now = Stopwatch.GetTimestamp();
            if (nextTick > now)
            {
                var waitMs = (int)(((nextTick - now) * nsPerStopwatchTick) / 1_000_000.0);
                if (waitMs > 0)
                {
                    try
                    {
                        await Task.Delay(waitMs, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }
        }
    }
}

static string BuildPayload(SizeBucket bucket, Random rng)
{
    var length = rng.Next(bucket.MinBytes, bucket.MaxBytes + 1);
    return new string('x', length);
}

async Task DeclareQueueAsync()
{
    var factory = new ConnectionFactory { HostName = Host, UserName = User, Password = Pass };
    await using var connection = await factory.CreateConnectionAsync().ConfigureAwait(false);
    await using var channel = await connection.CreateChannelAsync().ConfigureAwait(false);
    await channel.ExchangeDeclareAsync(Exchange, "direct", durable: false, autoDelete: false)
        .ConfigureAwait(false);
    await channel.QueueDeclareAsync(Queue, durable: false, exclusive: false, autoDelete: false)
        .ConfigureAwait(false);
    await channel.QueueBindAsync(Queue, Exchange, routingKey: string.Empty).ConfigureAwait(false);
}

async Task PurgeQueueAsync()
{
    var factory = new ConnectionFactory { HostName = Host, UserName = User, Password = Pass };
    await using var connection = await factory.CreateConnectionAsync().ConfigureAwait(false);
    await using var channel = await connection.CreateChannelAsync().ConfigureAwait(false);
    await channel.QueuePurgeAsync(Queue).ConfigureAwait(false);
}

async Task MonitorQueueDepthAsync(CancellationToken ct)
{
    var factory = new ConnectionFactory { HostName = Host, UserName = User, Password = Pass };
    await using var connection = await factory.CreateConnectionAsync(ct).ConfigureAwait(false);
    await using var channel = await connection.CreateChannelAsync(cancellationToken: ct).ConfigureAwait(false);

    Console.WriteLine("t(s)  produced  landed   dProd/s  dLand/s");
    var prevProduced = 0L;
    var prevLanded = 0u;
    var second = 0;
    while (!ct.IsCancellationRequested)
    {
        try
        {
            await Task.Delay(1000, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        second++;
        var prodNow = Interlocked.Read(ref produced);
        uint landedNow;
        try
        {
            var ok = await channel.QueueDeclarePassiveAsync(Queue, ct).ConfigureAwait(false);
            landedNow = ok.MessageCount;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (RabbitMQ.Client.Exceptions.RabbitMQClientException ex)
        {
            Console.WriteLine($"{second,4}  {prodNow,8:N0}      ?    {prodNow - prevProduced,7:N0}     ?    (broker unreachable: {ex.GetType().Name})");
            prevProduced = prodNow;
            continue;
        }

        Console.WriteLine(
            $"{second,4}  {prodNow,8:N0}  {landedNow,7:N0}  {prodNow - prevProduced,7:N0}  {(long)landedNow - prevLanded,7:N0}");
        prevProduced = prodNow;
        prevLanded = landedNow;
    }
}

async Task<uint> CountQueueAsync()
{
    var factory = new ConnectionFactory { HostName = Host, UserName = User, Password = Pass };
    await using var connection = await factory.CreateConnectionAsync().ConfigureAwait(false);
    await using var channel = await connection.CreateChannelAsync().ConfigureAwait(false);
    var ok = await channel.QueueDeclarePassiveAsync(Queue).ConfigureAwait(false);
    return ok.MessageCount;
}

static void PrintSummary(long produced, long producedBytes, uint landed, long brokerRejected, TimeSpan elapsed)
{
    var seconds = Math.Max(elapsed.TotalSeconds, 0.0001);
    var queueDropped = Math.Max(0, produced - landed - brokerRejected);
    Console.WriteLine();
    Console.WriteLine("===== Load test summary =====");
    Console.WriteLine($"Duration              : {elapsed.TotalSeconds:F2}s");
    Console.WriteLine($"Produced events       : {produced:N0}");
    Console.WriteLine($"Produced payload      : {producedBytes / (1024.0 * 1024.0):F2} MiB");
    Console.WriteLine($"Producer rate         : {produced / seconds:N0} events/s");
    Console.WriteLine($"Producer rate (MiB)   : {producedBytes / (1024.0 * 1024.0) / seconds:F2} MiB/s");
    Console.WriteLine();
    Console.WriteLine($"Landed in broker queue: {landed:N0} (sustained sink throughput ~ {landed / seconds:N0} events/s)");
    Console.WriteLine($"Broker-rejected       : {brokerRejected:N0} (entries in failure.txt)");
    Console.WriteLine($"Dropped at batch queue: {queueDropped:N0} (Serilog batching queue overflow — silent drops)");
    Console.WriteLine();
    Console.WriteLine("Tune: lower --rate, raise --queue-limit, raise --batch, or raise --channels");
    Console.WriteLine("until 'Dropped at batch queue' is 0; that's the sink's sustained capacity.");
}

static long CountFailureSinkLines(string path)
{
    if (!File.Exists(path))
    {
        return 0;
    }

    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    using var reader = new StreamReader(stream);
    long lines = 0;
    while (reader.ReadLine() is not null)
    {
        lines++;
    }

    return lines;
}
