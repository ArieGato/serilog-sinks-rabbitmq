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

namespace Serilog.Sinks.RabbitMQ;

/// <summary>
/// Pool of <see cref="IRabbitMQChannel"/> instances.
/// </summary>
internal interface IRabbitMQChannelPool : IAsyncDisposable
{
    /// <summary>
    /// Rents a channel from the pool, awaiting one if all channels are currently in use.
    /// </summary>
    /// <param name="cancellationToken">Token that aborts the wait.</param>
    /// <returns>A channel ready for publishing.</returns>
    ValueTask<IRabbitMQChannel> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a previously rented channel to the pool. Broken channels are disposed
    /// asynchronously and the pool refills in the background.
    /// </summary>
    /// <param name="channel">The channel to return.</param>
    /// <returns>A <see cref="ValueTask"/> that completes once any inline disposal finishes.</returns>
    ValueTask ReturnAsync(IRabbitMQChannel channel);
}
