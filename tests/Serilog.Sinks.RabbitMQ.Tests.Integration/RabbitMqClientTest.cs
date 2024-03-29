// Copyright 2015-2022 Serilog Contributors
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

namespace Serilog.Sinks.RabbitMQ.Tests.Integration;

/// <summary>
/// Tests for <see cref="RabbitMQClient" />.
/// </summary>
[Collection("Sequential")]
public sealed class RabbitMQClientTest : IClassFixture<RabbitMQFixture>
{
    private readonly RabbitMQFixture _rabbitMQFixture;

    public RabbitMQClientTest(RabbitMQFixture rabbitMQFixture)
    {
        _rabbitMQFixture = rabbitMQFixture;
    }

    /// <summary>
    /// Consumer should receive a message after calling Publish.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>.
    [Fact]
    public async Task Publish_SingleMessage_ConsumerReceivesMessage()
    {
        await _rabbitMQFixture.InitializeAsync();

        string message = Guid.NewGuid().ToString();

        using var consumingChannel = await _rabbitMQFixture.GetConsumingModelAsync();
        var consumer = new EventingBasicConsumer(consumingChannel);
        var eventRaised = await Assert.RaisesAsync<BasicDeliverEventArgs>(
            h => consumer.Received += h,
            h => consumer.Received -= h,
            () =>
            {
                consumingChannel.BasicConsume(RabbitMQFixture.SerilogSinkQueueName, autoAck: true, consumer);
                _rabbitMQFixture.Publish(message);

                // Wait for consumer to receive the message.
                return Task.Delay(50);
            });

        string receivedMessage = Encoding.UTF8.GetString(eventRaised.Arguments.Body.ToArray());
        receivedMessage.ShouldBe(message);

        consumingChannel.Close();
    }

    /// <summary>
    /// Consumer should receive a message after calling Publish.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>.
    [Fact]
    public async Task Publish_BulkMessages_ConsumerReceivesMessage()
    {
        await _rabbitMQFixture.InitializeAsync();

        string message = Guid.NewGuid().ToString();

        using var consumingChannel = await _rabbitMQFixture.GetConsumingModelAsync();
        var consumer = new EventingBasicConsumer(consumingChannel);
        var eventRaised = await Assert.RaisesAsync<BasicDeliverEventArgs>(
            h => consumer.Received += h,
            h => consumer.Received -= h,
            () =>
            {
                // start consuming queue
                consumingChannel.BasicConsume(RabbitMQFixture.SerilogSinkQueueName, autoAck: true, consumer);

                for (int i = 0; i < 100; i++)
                {
                    _rabbitMQFixture.Publish(message);
                }

                // Wait for consumer to receive the message.
                return Task.Delay(1000);
            });

        string receivedMessage = Encoding.UTF8.GetString(eventRaised.Arguments.Body.ToArray());
        receivedMessage.ShouldBe(message);

        consumingChannel.Close();
    }

    [Fact]
    public async Task AutoCreateExchange_WhenTrue_ThenShouldCreateExchange()
    {
        var rabbitMQClientConfiguration = new RabbitMQClientConfiguration
        {
            Port = 5672,
            DeliveryMode = RabbitMQDeliveryMode.Durable,
            Exchange = "auto-created-exchange-name",
            Username = RabbitMQFixture.UserName,
            Password = RabbitMQFixture.Password,
            ExchangeType = "topic",
            Hostnames = [RabbitMQFixture.SslCertHostName],
            AutoCreateExchange = true,
        };

        using var rabbitMQClient = new RabbitMQClient(rabbitMQClientConfiguration);
        rabbitMQClient.Publish(Encoding.UTF8.GetBytes("a message"));

        //// wait for message sent
        // await Task.Delay(1000);
        using var consumingChannel = await _rabbitMQFixture.GetConsumingModelAsync();

        try
        {
            // should not throw
            consumingChannel.ExchangeDeclarePassive("auto-created-exchange-name");
        }
        finally
        {
            consumingChannel.ExchangeDelete("auto-created-exchange-name");
        }

        consumingChannel.Close();
        rabbitMQClient.Close();
    }

    /// <summary>
    /// Consumer should receive a message after calling Publish.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>.
    [Fact]
    public async Task Publish_ParallelMessages_AllMessagesArePublished()
    {
        var config = new RabbitMQClientConfiguration
        {
            Port = 5672,
            DeliveryMode = RabbitMQDeliveryMode.Durable,
            Exchange = "parallel-message-exchange",
            ExchangeType = RabbitMQFixture.SerilogSinkExchangeType,
            AutoCreateExchange = true,
            Username = RabbitMQFixture.UserName,
            Password = RabbitMQFixture.Password,
            Hostnames = [RabbitMQFixture.SslCertHostName],
        };
        using var rabbitMQClient = new RabbitMQClient(config);

        string message = Guid.NewGuid().ToString();

        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 10 };

        Parallel.For(0, 10, parallelOptions, (_, _) =>
        {
            for (int i = 0; i < 1000; i++)
            {
                rabbitMQClient.Publish(Encoding.UTF8.GetBytes(message));
            }
        });

        // Add some delay to ensure all messages are published before the exchange is deleted
        await Task.Delay(1000);

        rabbitMQClient.Close();
    }
}
