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

internal sealed record LoadTestOptions(
    TimeSpan Duration,
    int Concurrency,
    int RatePerSecondPerTask,
    int BatchPostingLimit,
    int ChannelCount,
    int? QueueLimit)
{
    public static LoadTestOptions Parse(string[] args)
    {
        var duration = TimeSpan.FromSeconds(GetInt(args, "--duration", 10));
        var concurrency = GetInt(args, "--concurrency", Math.Max(4, Environment.ProcessorCount));
        var rate = GetInt(args, "--rate", 0);
        var batch = GetInt(args, "--batch", 200);
        var channels = GetInt(args, "--channels", 64);
        var queueLimit = GetInt(args, "--queue-limit", -1);
        int? queueLimitOpt = queueLimit > 0 ? queueLimit : null;
        return new LoadTestOptions(duration, concurrency, rate, batch, channels, queueLimitOpt);
    }

    public string Describe() =>
        $"Load test: duration={Duration.TotalSeconds:F0}s, concurrency={Concurrency}, " +
        $"ratePerTask={(RatePerSecondPerTask == 0 ? "unbounded" : RatePerSecondPerTask + "/s")}, " +
        $"batchPostingLimit={BatchPostingLimit}, channelCount={ChannelCount}, " +
        $"queueLimit={(QueueLimit.HasValue ? QueueLimit.Value.ToString() : "default")}";

    private static int GetInt(string[] args, string name, int defaultValue)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.Ordinal)
                && int.TryParse(args[i + 1], out var v))
            {
                return v;
            }
        }

        return defaultValue;
    }
}
