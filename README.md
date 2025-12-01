<div align="center">
  <picture>
    <img alt="MehguViewer Logo" src="public/thumbnail.png" width="400">
  </picture>
</div>

# <picture><img alt="MehguViewer Logo" src="public/logo-light.png" height="32"></picture> MehguViewer.Core <picture><img alt="MehguViewer Logo" src="public/logo-dark.png" height="32"></picture>

> **The Reference Implementation of the MehguViewer Core Node.**

[![CI](https://img.shields.io/github/actions/workflow/status/MehguViewer/MehguViewer.Core/ci.yml?style=flat-square&label=Build)](https://github.com/MehguViewer/MehguViewer.Core/actions)
[![License](https://img.shields.io/github/license/MehguViewer/MehguViewer.Core?style=flat-square)](LICENSE)

**MehguViewer.Core** is the high-performance, self-hostable server component of the MehguViewer Ecosystem. It handles content management, media streaming, and user progress tracking.

---

### **Architecture**

This repository implements the **Core API** defined in [MehguViewer.Proto](https://github.com/MehguViewer/MehguViewer.Proto).

| Component | Tech Stack | Description |
| :--- | :--- | :--- |
| **Backend** | .NET 9 (Native AOT) | High-performance REST API. Handles metadata, file serving, and logic. |
| **Panel** | (Planned) | Web-based Admin Dashboard for managing the node. |

---

### **Key Features**

- 🚀 **Native AOT**: Compiled to native code for instant startup and low memory footprint.
- 🔒 **Stateless Auth**: Validates JWTs from the Auth Server using cached JWKS.
- 📦 **Universal Asset Handling**: Manages Manga, Anime, and Novels with URN-based addressing.
- ⚡ **Dual Mode Delivery**: Supports both secure Proxy Mode and direct CDN redirection.

---

### **Development**

#### **Prerequisites**
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- Docker (optional, for containerized builds)

#### **Backend**

The backend is located in the `Backend/` directory.

```bash
# Restore dependencies
cd Backend
dotnet restore

# Run in development mode
dotnet run

# Build Native AOT Release
dotnet publish -c Release -r linux-x64
```

#### **Panel**

*Coming Soon*

---

<div align="center">
  <sub>MehguViewer.Core &copy; 2025</sub>
</div>
