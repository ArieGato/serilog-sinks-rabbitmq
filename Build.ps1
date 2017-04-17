if(Test-Path .\artifacts) { Remove-Item .\artifacts -Force -Recurse }

dotnet restore .\src\Serilog.Sinks.RabbitMQ\Serilog.Sinks.RabbitMQ.csproj
dotnet build .\src\Serilog.Sinks.RabbitMQ\Serilog.Sinks.RabbitMQ.csproj

$revision = @{ $true = $env:APPVEYOR_BUILD_NUMBER; $false = 1 }[$env:APPVEYOR_BUILD_NUMBER -ne $NULL];
$revision = "{0:D4}" -f [convert]::ToInt32($revision, 10)

echo "RUNNING dotnet pack .\src\Serilog.Sinks.RabbitMQ\Serilog.Sinks.RabbitMQ.csproj -c Release -o .\artifacts --version-suffix=$revision"
dotnet pack .\src\Serilog.Sinks.RabbitMQ\Serilog.Sinks.RabbitMQ.csproj -c Release -o .\artifacts --version-suffix=$revision

dir
