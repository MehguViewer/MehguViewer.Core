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

**MehguViewer.Core** is the high-performance, self-hostable server component of the MehguViewer Ecosystem. It handles content management, media streaming, and user progress tracking with an integrated web-based admin interface.

---

## 🚀 Quick Start

### Download & Run (Easiest Way)

1. **Download** the latest release for your platform from [GitHub Actions → Build Artifacts](https://github.com/MehguViewer/MehguViewer.Core/actions/workflows/build-artifacts.yml)
2. **Extract** the downloaded archive
3. **Run** the executable:
   ```bash
   # Linux/macOS
   ./MehguViewer.Core

   # Windows
   .\MehguViewer.Core.exe
   ```
4. **Open** your browser to `http://localhost:6230`

That's it! The application includes everything needed - no additional setup required.

---

## 📋 System Requirements

- **Operating System**: Linux, macOS, or Windows
- **Memory**: 512MB RAM minimum (1GB recommended)
- **Storage**: 100MB for application + storage for your content
- **Network**: Internet connection for initial setup (optional)

---

## 🏗️ Architecture

This repository implements the **Core API** defined in [MehguViewer.Proto](https://github.com/MehguViewer/MehguViewer.Proto).

| Component | Technology | Description |
|-----------|------------|-------------|
| **Backend API** | ASP.NET Core 9 (.NET 9) | High-performance REST API with Native AOT compilation |
| **Database** | Embedded PostgreSQL | Zero-configuration database (with memory fallback) |
| **Admin Interface** | Blazor WebAssembly | Integrated web dashboard for management |
| **Authentication** | JWT + JWKS | Stateless validation of tokens from Auth Server |
| **File Serving** | Native HTTP | Direct streaming of media assets |

---

## ✨ Key Features

- 🚀 **Instant Startup**: Native AOT compilation for sub-second startup times
- 💾 **Flexible Storage**: File-based JSON persistence with PostgreSQL support
- 🖥️ **Integrated Admin Panel**: Built-in Blazor WebAssembly dashboard
- 🔒 **Secure by Design**: Role-based access control (Admin, Uploader, User)
- 📚 **Universal Content**: Supports Manga, Anime, and Novels with URN addressing
- 🏷️ **Rich Taxonomy**: Authors, Scanlators, Groups, Tags, and Content Warnings
- 🌍 **Localization Support**: Multi-language series with localized versions
- ⚡ **Dual Delivery Modes**: Proxy mode for security, CDN mode for performance
- 🐳 **Container Ready**: Optimized Docker images with multi-stage builds
- 🔧 **Self-Contained**: Single executable with no external dependencies

---

## 🛠️ Development

### Prerequisites
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- Git

### Project Structure

```
MehguViewer.Core/
├── MehguViewer.Core.csproj          # Main ASP.NET Core application
├── Program.cs                       # Application entry point
├── appsettings.json                 # Configuration settings
├── Dockerfile                       # Container build definition
│
├── MehguViewer.Core.UI/             # Blazor WebAssembly admin interface
│   ├── Pages/                       # Routable pages (Series, Users, Settings, etc.)
│   ├── Components/                  # Reusable UI components and dialogs
│   ├── Layout/                      # Application layouts
│   ├── Services/                    # Client-side services (ApiService, Auth)
│   ├── wwwroot/                     # Static web assets
│   └── README.md                    # UI documentation
│
├── MehguViewer.Core.Shared/         # Shared domain models and DTOs
│   ├── Domain.cs                    # Public API models (Series, Units, etc.)
│   ├── AdminModels.cs               # Admin-specific models (Users, Jobs)
│   ├── Problem.cs                   # RFC 7807 error handling
│   └── README.md                    # Shared models documentation
│
├── Endpoints/                       # API endpoint definitions
│   ├── SeriesEndpoints.cs           # Series CRUD operations
│   ├── UserEndpoints.cs             # User management
│   ├── SystemEndpoints.cs           # System and taxonomy endpoints
│   └── ...                          # Additional endpoint groups
│
├── Services/                        # Business logic and background services
│   ├── FileBasedSeriesService.cs    # File-based series storage
│   ├── ImageProcessingService.cs    # Image variant generation
│   ├── AuthService.cs               # Authentication logic
│   └── ...                          # Additional services
│
├── Infrastructures/                 # Data access layer
│   ├── IRepository.cs               # Repository interface
│   ├── MemoryRepository.cs          # In-memory implementation
│   ├── PostgresRepository.cs        # PostgreSQL implementation
│   └── DynamicRepository.cs         # Dynamic repository selection
│
├── Middlewares/                     # Custom ASP.NET Core middleware
│   ├── JwtMiddleware.cs             # JWT validation
│   ├── RateLimitingMiddleware.cs    # Rate limiting
│   └── ServerTimingMiddleware.cs    # Performance tracking
│
├── Tests/                           # Comprehensive test suite
│   ├── Endpoints/                   # Endpoint tests
│   ├── Services/                    # Service tests
│   ├── Infrastructures/             # Repository tests
│   ├── Integrations/                # Integration tests
│   ├── Unit/                        # Unit tests
│   └── README.md                    # Testing documentation
│
├── Public/                          # Static assets and branding
│   ├── logo-light.png              # Light theme logo
│   ├── logo-dark.png               # Dark theme logo
│   └── thumbnail.png               # Application thumbnail
│
└── data/                            # Runtime data (auto-created)
    ├── series/                      # Series JSON files
    ├── covers/                      # Cover images
    └── users/                       # User data
```

**Documentation:**
- [MehguViewer.Core.UI Documentation](MehguViewer.Core.UI/README.md) - Blazor WebAssembly admin interface
- [MehguViewer.Core.Shared Documentation](MehguViewer.Core.Shared/README.md) - Shared domain models
- [Testing Documentation](Tests/README.md) - Test suite organization and usage

### Building & Running

```bash
# Clone the repository
git clone https://github.com/MehguViewer/MehguViewer.Core.git
cd MehguViewer.Core

# Restore dependencies
dotnet restore

# Run in development mode (with hot reload)
dotnet watch run

# Or run normally
dotnet run
```

The application will be available at:
- **Admin Dashboard**: `http://localhost:6230`
- **API Endpoints**: `http://localhost:6230/api/v1/...`

### Configuration

The application uses sensible defaults and can be configured through the web interface. For advanced scenarios, create an `appsettings.json` file:

```json
{
  "EmbeddedPostgres": {
    "Enabled": true,
    "FallbackToMemory": true
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  }
}
```

### Testing

```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test /p:CollectCoverage=true

# Run specific category
dotnet test --filter "Category=Integration"
```

---

## 🐳 Docker Deployment

Build and run with Docker:

```bash
# Build the optimized image
docker build -t mehguviewer-core .

# Run the container
docker run -p 6230:6230 \
  -v /path/to/your/content:/app/content \
  -v mehguviewer-data:/app/data \
  mehguviewer-core
```

### Docker Compose Example

```yaml
version: '3.8'
services:
  mehguviewer:
    image: mehguviewer-core
    ports:
      - "6230:6230"
    volumes:
      - ./content:/app/content
      - mehguviewer-data:/app/data
    restart: unless-stopped
```

---

## 🔧 API Reference

The Core API follows the OpenAPI specification defined in [MehguViewer.Proto](https://proto.mehguviewer.kazeo.xyz/specs/core/openapi.yaml).

### Key Endpoints

| Endpoint | Description |
|----------|-------------|
| `GET /api/v1/series` | List all series |
| `POST /api/v1/series` | Create a new series (Admin/Uploader) |
| `GET /api/v1/series/{id}` | Get series details |
| `PUT /api/v1/series/{id}` | Update series (Owner or Admin) |
| `DELETE /api/v1/series/{id}` | Delete series (Owner or Admin) |
| `GET /api/v1/system/taxonomy` | Get all authors, scanlators, groups, tags |
| `GET /api/v1/assets/{urn}` | Stream media assets |
| `GET /.well-known/mehgu-node` | Node metadata |

### Authorization

- **Admin**: Full access to all series and system settings
- **Uploader**: Can create series and edit/delete their own series
- **User**: Read-only access to series and personal library

---

## 📊 Monitoring & Logs

The application provides comprehensive logging and monitoring:

- **Application Logs**: Available in console output and through `/api/v1/system/logs`
- **Health Checks**: Built-in health endpoints for monitoring
- **Performance Metrics**: Server timing headers and response compression
- **Database Status**: Automatic fallback to memory storage if PostgreSQL fails

---

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch: `git checkout -b feature/your-feature`
3. Make your changes and add tests
4. Run tests: `dotnet test`
5. Submit a pull request

### Development Guidelines

- Follow the [MehguViewer.Proto](https://github.com/MehguViewer/MehguViewer.Proto) specifications
- Use URN-based identifiers for all resources
- Implement proper error handling with RFC 7807 Problem Details
- Add integration tests for new endpoints
- Update documentation for API changes

---

## 📄 License

This project is licensed under the terms specified in the [LICENSE](LICENSE) file.

---

## 🙋 Support

- **Documentation**: [MehguViewer.Proto](https://proto.mehguviewer.kazeo.xyz)
- **Issues**: [GitHub Issues](https://github.com/MehguViewer/MehguViewer.Core/issues)
- **Discussions**: [GitHub Discussions](https://github.com/MehguViewer/MehguViewer.Core/discussions)

---

<div align="center">
  <sub>Built with ❤️ using .NET 9 and Blazor WebAssembly</sub>
  <br>
  <sub>MehguViewer.Core &copy; 2025</sub>
</div>
