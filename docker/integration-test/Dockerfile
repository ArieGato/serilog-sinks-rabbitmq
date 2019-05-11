FROM microsoft/dotnet:2.2-sdk AS base
WORKDIR /app

FROM microsoft/dotnet:2.2-sdk AS build
WORKDIR /src
COPY . .

RUN dotnet restore tests/Serilog.Sinks.RabbitMQ.Tests.Integration/Serilog.Sinks.RabbitMQ.Tests.Integration.csproj

RUN dotnet build tests/Serilog.Sinks.RabbitMQ.Tests.Integration/Serilog.Sinks.RabbitMQ.Tests.Integration.csproj -c Debug -o /app

FROM build AS publish
RUN dotnet publish tests/Serilog.Sinks.RabbitMQ.Tests.Integration/Serilog.Sinks.RabbitMQ.Tests.Integration.csproj -c Debug -o /app

FROM base AS final
WORKDIR /app
COPY --from=publish /app .
ENTRYPOINT ["/bin/bash", "-c", "dotnet vstest Serilog.Sinks.RabbitMQ.Tests.Integration.dll"]
