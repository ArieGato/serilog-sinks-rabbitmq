FROM microsoft/dotnet:2.1-runtime AS base
WORKDIR /app

FROM microsoft/dotnet:2.1-sdk AS build
WORKDIR /src
COPY . .

RUN dotnet restore tests/Serilog.Sinks.RabbitMQ.Tests.Console/Serilog.Sinks.RabbitMQ.Tests.Console.csproj

RUN dotnet build tests/Serilog.Sinks.RabbitMQ.Tests.Console/Serilog.Sinks.RabbitMQ.Tests.Console.csproj -c Debug -o /app

FROM build AS publish
RUN dotnet publish tests/Serilog.Sinks.RabbitMQ.Tests.Console/Serilog.Sinks.RabbitMQ.Tests.Console.csproj -c Debug -o /app

FROM base AS final
WORKDIR /app
COPY --from=publish /app .
ENTRYPOINT ["dotnet", "Serilog.Sinks.RabbitMQ.Tests.Console.dll"]