# Base stage for building
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Install Native AOT dependencies (clang, zlib)
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
       clang zlib1g-dev

# Copy project files first to cache restore
COPY ["MehguViewer.sln", "./"]
COPY ["Backend/MehguViewer.Core.csproj", "Backend/"]
COPY ["Panel/Panel.csproj", "Panel/"]
COPY ["Tests/Tests.csproj", "Tests/"]

# Restore dependencies
RUN dotnet restore "Backend/MehguViewer.Core.csproj"

# Copy the rest of the source code
COPY . .

# Publish the application
WORKDIR "/src/Backend"
RUN dotnet publish "MehguViewer.Core.csproj" -c Release -r linux-x64 /p:PublishAot=true -o /app/publish

# Final stage - minimal runtime
FROM mcr.microsoft.com/dotnet/runtime-deps:9.0
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["./MehguViewer.Core"]
