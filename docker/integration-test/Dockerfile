FROM mono:latest
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    # prevent NuGetFallbackFolder from being recreated
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    #
    # some environment variables from official dotnet docker
    # https://github.com/dotnet/dotnet-docker/blob/master/2.2/sdk/stretch/amd64/Dockerfile
    # Configure web servers to bind to port 80 when present
    ASPNETCORE_URLS=http://+:80 \
    # Enable detection of running in a container
    DOTNET_RUNNING_IN_CONTAINER=true \
    # Enable correct mode for dotnet watch (only mode supported in a container)
    DOTNET_USE_POLLING_FILE_WATCHER=true \
    # Skip extraction of XML docs - generally not useful within an image/container - helps performance
    NUGET_XMLDOC_MODE=skip

RUN apt-get update \
    && apt-get install wget gpg -y \
    #
    # https://dotnet.microsoft.com/download/linux-package-manager/debian10/sdk-current
    && wget -qO- https://packages.microsoft.com/keys/microsoft.asc | gpg --dearmor > microsoft.asc.gpg \
    && mv microsoft.asc.gpg /etc/apt/trusted.gpg.d/ \
    && wget -q https://packages.microsoft.com/config/debian/10/prod.list \
    && mv prod.list /etc/apt/sources.list.d/microsoft-prod.list \
    && apt-get install apt-transport-https -y \
    && apt-get update \
    && apt-get install -y dotnet-sdk-8.0 -y \
	&& apt-get install -y dotnet-sdk-6.0 -y \
    && rm -rf /usr/share/dotnet/sdk/NuGetFallbackFolder \
    && rm -rf /tmp/*

WORKDIR /src

COPY . .

RUN dotnet restore

ENTRYPOINT ["dotnet", "test", "-c", "Release"]
