<div align="center">
  <picture>
    <img alt="MehguViewer Logo" src="Public/thumbnail.png" width="400">
  </picture>
</div>

# <picture><img alt="MehguViewer Logo" src="Public/logo-light.png" height="32"></picture> MehguViewer.Core <picture><img alt="MehguViewer Logo" src="Public/logo-dark.png" height="32"></picture>

> **The Reference Implementation of the MehguViewer Core Node.**

[![CI](https://img.shields.io/github/actions/workflow/status/MehguViewer/MehguViewer.Core/release.yml?style=flat-square&label=Build)](https://github.com/MehguViewer/MehguViewer.Core/actions)
[![License](https://img.shields.io/github/license/MehguViewer/MehguViewer.Core?style=flat-square)](LICENSE)

**MehguViewer.Core** is the high-performance, self-hostable server component of the MehguViewer Ecosystem. It handles content management, media streaming, and user progress tracking.

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
dotnet test
```

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
