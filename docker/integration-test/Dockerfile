FROM mcr.microsoft.com/dotnet/sdk:8.0

WORKDIR /src

COPY . .

RUN dotnet restore tests/Serilog.Sinks.RabbitMQ.Tests.Integration/Serilog.Sinks.RabbitMQ.Tests.Integration.csproj

WORKDIR tests/Serilog.Sinks.RabbitMQ.Tests.Integration

ENTRYPOINT ["dotnet", "test"]
