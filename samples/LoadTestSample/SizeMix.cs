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

internal sealed record SizeBucket(string Name, int MinBytes, int MaxBytes, double Weight);

internal sealed class SizeMix
{
    public static readonly SizeMix Default = new(
    [
        new SizeBucket("small", 50, 200, 0.70),
        new SizeBucket("medium", 1_000, 2_000, 0.20),
        new SizeBucket("large", 10_000, 20_000, 0.08),
        new SizeBucket("xlarge", 80_000, 120_000, 0.02),
    ]);

    private readonly SizeBucket[] _buckets;
    private readonly double[] _cumulative;

    private SizeMix(SizeBucket[] buckets)
    {
        _buckets = buckets;
        _cumulative = new double[buckets.Length];
        var total = 0.0;
        for (var i = 0; i < buckets.Length; i++)
        {
            total += buckets[i].Weight;
            _cumulative[i] = total;
        }

        for (var i = 0; i < _cumulative.Length; i++)
        {
            _cumulative[i] /= total;
        }
    }

    public SizeBucket Pick(Random rng)
    {
        var r = rng.NextDouble();
        for (var i = 0; i < _cumulative.Length; i++)
        {
            if (r <= _cumulative[i])
            {
                return _buckets[i];
            }
        }

        return _buckets[^1];
    }
}
