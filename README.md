<div align="center">
  <picture>
    <img alt="MehguViewer Logo" src="Public/thumbnail.png" width="400">
  </picture>
</div>

# <picture><img alt="MehguViewer Logo" src="Public/logo-light.png" height="32"></picture> MehguViewer.Core <picture><img alt="MehguViewer Logo" src="Public/logo-dark.png" height="32"></picture>

> **The Reference Implementation of the MehguViewer Core Node.**

[![CI](https://github.com/MehguViewer/MehguViewer.Core/actions/workflows/ci.yml/badge.svg)](https://github.com/MehguViewer/MehguViewer.Core/actions/workflows/ci.yml)
[![Build Artifacts](https://github.com/MehguViewer/MehguViewer.Core/actions/workflows/build-artifacts.yml/badge.svg)](https://github.com/MehguViewer/MehguViewer.Core/actions/workflows/build-artifacts.yml)
[![License](https://img.shields.io/github/license/MehguViewer/MehguViewer.Core?style=flat-square)](LICENSE)

**MehguViewer.Core** is the high-performance, self-hostable server component of the MehguViewer Ecosystem. It handles content management, media streaming, and user progress tracking.

---

## 📦 Download

Pre-built executables are available from GitHub Actions:

1. Go to [Actions → Build Artifacts](https://github.com/MehguViewer/MehguViewer.Core/actions/workflows/build-artifacts.yml)
2. Select the latest successful run
3. Download the artifact for your platform:
   - `MehguViewer-linux-x64` - Linux x64
   - `MehguViewer-linux-arm64` - Linux ARM64 (Raspberry Pi, etc.)
   - `MehguViewer-win-x64` - Windows x64
   - `MehguViewer-osx-x64` - macOS Intel
   - `MehguViewer-osx-arm64` - macOS Apple Silicon

---

### **Architecture**

This repository implements the **Core API** defined in [MehguViewer.Proto](https://github.com/MehguViewer/MehguViewer.Proto).

| Component | Tech Stack | Description |
| :--- | :--- | :--- |
| **Server** | .NET 9 (Native AOT) | High-performance REST API. Handles metadata, file serving, and logic. |
| **Client** | Blazor WebAssembly | Web-based Admin Dashboard for managing the node. Served by the backend. |

---

### **Key Features**

- 🚀 **Native AOT**: Compiled to native code for instant startup and low memory footprint.
- 🖥️ **Integrated Admin Panel**: Built-in Blazor WebAssembly dashboard (MudBlazor) for easy management.
- 🔒 **Stateless Auth**: Validates JWTs from the Auth Server using cached JWKS.
- 📦 **Universal Asset Handling**: Manages Manga, Anime, and Novels with URN-based addressing.
- ⚡ **Dual Mode Delivery**: Supports both secure Proxy Mode and direct CDN redirection.

---

### **Development**

#### **Prerequisites**
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- Docker (optional, for containerized builds)

#### **Project Structure**

The workspace is organized as a Visual Studio Solution (`MehguViewer.sln`):

- `MehguViewer.Core.csproj`: The Core API server (ASP.NET Core Minimal APIs).
- `Client/`: The Admin Dashboard (Blazor WebAssembly).
- `Tests/`: Integration tests using xUnit and WebApplicationFactory.

#### **Building and Running**

You can build and run the entire solution using the .NET CLI from the root directory.

```bash
# Restore dependencies
dotnet restore

# Run the Server (serves the Client automatically)
dotnet run
```

Or use **Hot Reload** for development:

```bash
dotnet watch run
```

Access the application at:
- **Admin Panel**: `http://localhost:5273/`
- **API**: `http://localhost:5273/api/v1/...`

#### **Testing**

Run the integration tests to verify API functionality and Client integration.

```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test /p:CollectCoverage=true

# Run specific test category
dotnet test --filter "Category=Smoke"
```

See [Tests/README.md](Tests/README.md) for detailed testing documentation.

---

### **CI/CD**

This project uses GitHub Actions for continuous integration:

| Workflow | Trigger | Description |
|----------|---------|-------------|
| [CI](.github/workflows/ci.yml) | Push/PR | Build, test, code quality, Docker |
| [Build Artifacts](.github/workflows/build-artifacts.yml) | Push | Build executables for all platforms |
| [PR Checks](.github/workflows/pr-checks.yml) | PR | Quick validation and conventional commits |
| [Nightly](.github/workflows/nightly.yml) | Daily 2AM UTC | Integration tests with PostgreSQL |

See [.github/workflows/README.md](.github/workflows/README.md) for detailed CI/CD documentation.

---

### **Docker Support**

MehguViewer.Core is designed to run in a container. The Dockerfile builds the application using Native AOT for maximum performance.

```bash
# Build the image
docker build -t mehguviewer-core .

# Run the container
docker run -p 8080:8080 mehguviewer-core
```

---

<div align="center">
  <sub>MehguViewer.Core &copy; 2025</sub>
</div>
