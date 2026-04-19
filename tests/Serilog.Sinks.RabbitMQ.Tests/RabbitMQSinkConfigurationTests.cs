namespace Serilog.Sinks.RabbitMQ.Tests;

public class RabbitMQSinkConfigurationTests
{
    private static RabbitMQSinkConfiguration ValidSample() => new()
    {
        BatchPostingLimit = 50,
        BufferingTimeLimit = TimeSpan.FromSeconds(2),
    };

    [Fact]
    public void Validate_DoesNotThrow_WhenConfigurationIsValid()
    {
        var sut = ValidSample();

        Should.NotThrow(sut.Validate);
    }

    [Fact]
    public void Validate_Throws_WhenTextFormatterIsNull()
    {
        var sut = ValidSample();
        sut.TextFormatter = null!;

        Should.Throw<ArgumentException>(sut.Validate).ParamName.ShouldBe("TextFormatter");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_Throws_WhenBatchPostingLimitIsNotPositive(int value)
    {
        var sut = ValidSample();
        sut.BatchPostingLimit = value;

        Should.Throw<ArgumentOutOfRangeException>(sut.Validate).ParamName.ShouldBe("BatchPostingLimit");
    }

    [Fact]
    public void Validate_Throws_WhenBufferingTimeLimitIsNegative()
    {
        var sut = ValidSample();
        sut.BufferingTimeLimit = TimeSpan.FromSeconds(-1);

        Should.Throw<ArgumentOutOfRangeException>(sut.Validate).ParamName.ShouldBe("BufferingTimeLimit");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_Throws_WhenQueueLimitIsNotPositive(int value)
    {
        var sut = ValidSample();
        sut.QueueLimit = value;

        Should.Throw<ArgumentOutOfRangeException>(sut.Validate).ParamName.ShouldBe("QueueLimit");
    }

    [Fact]
    public void Validate_AcceptsNullQueueLimit_AsUnsetSentinel()
    {
        var sut = ValidSample();
        sut.QueueLimit = null;

        Should.NotThrow(sut.Validate);
    }

    [Fact]
    public void Validate_IsIdempotent_WhenCalledRepeatedlyOnValidConfiguration()
    {
        var sut = ValidSample();

        Should.NotThrow(sut.Validate);
        Should.NotThrow(sut.Validate);
    }
}
