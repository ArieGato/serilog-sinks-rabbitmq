if(Test-Path .\artifacts) { Remove-Item .\artifacts -Force -Recurse }

dotnet restore .\src\Serilog.Sinks.RabbitMQ\Serilog.Sinks.RabbitMQ.csproj
dotnet build .\src\Serilog.Sinks.RabbitMQ\Serilog.Sinks.RabbitMQ.csproj

echo "RUNNING dotnet pack .\src\Serilog.Sinks.RabbitMQ\Serilog.Sinks.RabbitMQ.csproj -c Release -o .\artifacts"
dotnet pack .\src\Serilog.Sinks.RabbitMQ\Serilog.Sinks.RabbitMQ.csproj -c Release -o .\artifacts
