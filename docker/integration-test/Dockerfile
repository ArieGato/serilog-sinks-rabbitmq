FROM microsoft/dotnet:2.2-sdk

WORKDIR /src

COPY . .

RUN dotnet restore tests/Serilog.Sinks.RabbitMQ.Tests.Integration/Serilog.Sinks.RabbitMQ.Tests.Integration.csproj

WORKDIR tests/Serilog.Sinks.RabbitMQ.Tests.Integration

ENTRYPOINT ["dotnet", "test"]
