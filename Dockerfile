# Base stage for building
FROM mcr.microsoft.com/dotnet/sdk:10.0.101 AS build
WORKDIR /src

# Install dependencies (clang, zlib for Native AOT, python3 for WASM AOT)
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
       clang zlib1g-dev python3 \
    && rm -rf /var/lib/apt/lists/*

# Copy project files first to cache restore
COPY ["MehguViewer.sln", "./"]
COPY ["MehguViewer.Core.csproj", "./"]
COPY ["MehguViewer.Core.Shared/MehguViewer.Core.Shared.csproj", "MehguViewer.Core.Shared/"]
COPY ["MehguViewer.Core.UI/MehguViewer.Core.UI.csproj", "MehguViewer.Core.UI/"]
COPY ["Tests/Tests.csproj", "Tests/"]

# Install .NET workloads (required for Blazor WebAssembly)
RUN dotnet workload install wasm-tools --skip-sign-check

# Restore dependencies
RUN dotnet restore "MehguViewer.Core.csproj"

# Copy the rest of the source code
COPY . .

# Publish the application
WORKDIR "/src"
RUN dotnet publish "MehguViewer.Core.csproj" -c Release -r linux-x64 -o /app/publish

# Final stage - minimal runtime
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0.1
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["./MehguViewer.Core"]
