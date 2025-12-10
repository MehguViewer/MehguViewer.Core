# CI/CD Workflows

This document describes the GitHub Actions workflows for MehguViewer.Core.

## Overview

| Workflow | File | Triggers | Purpose |
|----------|------|----------|---------|
| **Continuous Integration** | `ci.yml` | Push to main/develop, PRs | Complete CI pipeline with tests, coverage, and deployment |
| **Build Artifacts** | `build-artifacts.yml` | Push to main/develop, PRs, tags | Multi-platform executable builds |
| **PR Checks** | `pr-checks.yml` | Pull Requests | Fast PR validation and metadata analysis |
| **Nightly Build** | `nightly.yml` | Daily at 2 AM UTC, Manual | Extended testing and dependency checks |

---

## 🏗️ Build Artifacts Workflow (`build-artifacts.yml`)

Builds self-contained single-file executables for all supported platforms.

### Supported Platforms

| Platform | Architecture | Artifact Name | Runner |
|----------|--------------|---------------|--------|
| Linux | x64 | `MehguViewer-linux-x64` | ubuntu-latest |
| Linux | ARM64 | `MehguViewer-linux-arm64` | ubuntu-latest |
| Windows | x64 | `MehguViewer-win-x64` | windows-latest |
| macOS | Intel (x64) | `MehguViewer-osx-x64` | macos-13 |
| macOS | Apple Silicon (ARM64) | `MehguViewer-osx-arm64` | macos-latest |

### Key Features

- ✅ **Single-file executables** - All dependencies bundled
- ✅ **Self-contained** - No .NET runtime required
- ✅ **Compression enabled** - Optimized file sizes
- ✅ **Blazor UI included** - wwwroot bundled with executable
- ✅ **30-day retention** - Download artifacts for a month
- ✅ **Error handling** - Validates executable and wwwroot presence

### Triggers

- Push to `main` or `develop` branches
- Pull requests to `main` or `develop`
- Tags matching `v*` (for releases)
- Manual workflow dispatch

### Downloading Artifacts

1. Navigate to [Actions → Build Artifacts](../../actions/workflows/build-artifacts.yml)
2. Click on a successful workflow run
3. Scroll to the **Artifacts** section
4. Download the artifact for your platform
5. Extract and run the executable

---

## 🔄 CI Workflow (`ci.yml`)

Comprehensive continuous integration pipeline with multiple stages.

### Jobs

#### 1. Build 🔨
- Restores NuGet packages with caching
- Builds entire solution in Release mode
- Uploads build artifacts for downstream jobs
- **Duration:** ~2-3 minutes

#### 2. Test 🧪
- Runs all tests with code coverage
- Generates Cobertura coverage reports
- Creates HTML coverage report using ReportGenerator
- Uploads test results (TRX format)
- Adds coverage summary to PR comments
- **Depends on:** Build
- **Duration:** ~3-5 minutes

#### 3. Code Quality ✨
- Validates code formatting with `dotnet format`
- Scans for vulnerable NuGet packages
- Uploads security report as artifact
- **Depends on:** Build
- **Duration:** ~1-2 minutes

#### 4. Docker Build & Push 🐳
- Builds multi-platform Docker images (amd64, arm64)
- Pushes to GitHub Container Registry (ghcr.io)
- Tags: `latest`, `{branch}`, `{branch}-{sha}`
- Uses layer caching for faster builds
- **Only runs:** Push to `main` branch
- **Depends on:** Test, Code Quality
- **Duration:** ~5-8 minutes

#### 5. Release 📦
- Creates release archives for all platforms
- Generates `.tar.gz` (Unix) and `.zip` (Windows)
- Uploads to GitHub Releases
- **Only runs:** Tag push matching `v*`
- **Depends on:** Test, Code Quality
- **Duration:** ~10-15 minutes

### Triggers

- Push to `main` or `develop` branches
- Pull requests to `main` or `develop`
- Changes to: `*.cs`, `*.csproj`, `*.sln`, `*.razor`, `Dockerfile`, workflows
- Manual workflow dispatch

### Artifacts Produced

- **build-artifacts** - Compiled binaries (1 day retention)
- **test-results** - TRX test result files (7 days retention)
- **coverage-reports** - Cobertura XML files (7 days retention)
- **coverage-html-report** - HTML coverage report (7 days retention)
- **security-report** - Vulnerability scan results (7 days retention)

### Status Badge

```markdown
![CI](https://github.com/MehguViewer/MehguViewer.Core/actions/workflows/ci.yml/badge.svg)
```

---

## ✅ PR Checks Workflow (`pr-checks.yml`)

Fast validation and analysis for pull requests.

### Jobs

#### 1. Quick Validation ⚡
- Fast Debug build (10-minute timeout)
- Runs unit tests only (excludes Integration/E2E tests)
- Provides rapid feedback within minutes
- **Filter:** `Category!=Integration&Category!=E2E`

#### 2. PR Metadata 📊
- Analyzes PR size and complexity
- Automatically adds size labels:
  - `size/XS`: < 50 lines changed
  - `size/S`: 50-200 lines changed
  - `size/M`: 200-500 lines changed
  - `size/L`: 500-1000 lines changed
  - `size/XL`: > 1000 lines changed
- Warns on large PRs (> 500 lines)
- Creates detailed summary with statistics

#### 3. Commit Messages 📝
- Validates commit message format
- Uses commitlint with conventional commits
- Checks against `.commitlintrc.json` configuration
- Allowed types: `feat`, `fix`, `docs`, `style`, `refactor`, `perf`, `test`, `build`, `ci`, `chore`, `revert`
- Continues on error (warnings only)

#### 4. File Changes Analysis 🔍
- Detects which parts of codebase changed
- Categorizes changes:
  - **Core Backend** - `MehguViewer.Core/**/*.cs`
  - **UI Components** - `MehguViewer.Core.UI/**/*.razor`
  - **Shared Library** - `MehguViewer.Core.Shared/**/*.cs`
  - **Tests** - `Tests/**/*.cs`
  - **Workflows** - `.github/workflows/**`
  - **Docker** - `Dockerfile`
- Creates visual summary table

### Triggers

- Pull request events: `opened`, `synchronize`, `reopened`, `edited`
- Targets: `main` or `develop` branches

### Configuration

Commit message validation is configured in `.commitlintrc.json`:

```json
{
  "extends": ["@commitlint/config-conventional"],
  "rules": {
    "type-enum": [2, "always", ["feat", "fix", "docs", ...]],
    "header-max-length": [2, "always", 100]
  }
}
```

---

## 🌙 Nightly Build Workflow (`nightly.yml`)

Extended testing and maintenance checks run daily.

### Jobs

#### 1. Integration Tests 🔗
- Runs complete test suite including integration tests
- Uses PostgreSQL 16 service container
- Full code coverage collection
- Generates detailed test reports
- **Environment:**
  - PostgreSQL: `postgres://testuser:testpass@localhost:5432/mehgutest`
- **Duration:** ~10-15 minutes

#### 2. Cross-Platform Builds 🌐
- Tests build and publish on actual target platforms
- Verifies executables are created correctly
- Validates wwwroot directory presence
- **Platforms:**
  - Ubuntu (linux-x64)
  - Windows (win-x64)
  - macOS (osx-arm64)
- **Duration:** ~15-20 minutes per platform

#### 3. Dependency Check 📦
- Lists all outdated NuGet packages
- Scans for known vulnerabilities
- Checks transitive dependencies
- Generates markdown report
- **Retention:** 30 days
- **Duration:** ~2-3 minutes

#### 4. Health Check 🏥
- Starts application in Release mode
- Tests health endpoint (`/.well-known/mehgu-node`)
- Validates startup within 30 seconds
- Clean shutdown verification
- **Duration:** ~1-2 minutes
- **Continue on error:** Yes

#### 5. Summary 📋
- Aggregates all job results
- Creates visual summary table
- Reports overall status
- **Always runs:** Even if previous jobs fail

### Triggers

- **Scheduled:** Daily at 2:00 AM UTC (`0 2 * * *`)
- **Manual:** workflow_dispatch

### Artifacts Produced

- **integration-test-results** - Full test results with coverage (7 days)
- **dependency-report** - Outdated and vulnerable packages (30 days)

### Notifications

The nightly build creates a comprehensive summary showing:
- ✅ Success / ❌ Failure / ⏭️ Skipped status for each job
- Timestamp of execution
- Overall health status

---

## 🔧 Configuration

### Environment Variables

All workflows use consistent environment variables:

```yaml
env:
  DOTNET_VERSION: '9.0.x'
  DOTNET_SKIP_FIRST_TIME_EXPERIENCE: true
  DOTNET_CLI_TELEMETRY_OPTOUT: true
  NUGET_PACKAGES: ${{ github.workspace }}/.nuget/packages
```

### Caching Strategy

NuGet packages are cached to speed up builds:

```yaml
- name: Cache NuGet packages
  uses: actions/cache@v4
  with:
    path: ${{ env.NUGET_PACKAGES }}
    key: ${{ runner.os }}-nuget-${{ hashFiles('**/*.csproj') }}
```

### Permissions

Workflows use minimal required permissions:

| Workflow | Permissions |
|----------|-------------|
| `ci.yml` (docker job) | `contents: read`, `packages: write` |
| `ci.yml` (release job) | `contents: write` |
| Others | Default (read-only) |

---

## 🚀 Best Practices

### For Contributors

1. **Commit Messages**: Follow conventional commit format
   - ✅ `feat: add new endpoint for user management`
   - ✅ `fix: resolve race condition in job service`
   - ❌ `update code` (too vague)

2. **PR Size**: Keep PRs focused and reasonably sized
   - Aim for < 500 lines changed
   - Break large features into multiple PRs

3. **Tests**: Ensure tests pass locally before pushing
   ```bash
   dotnet test --filter "Category!=Integration"
   ```

4. **Code Formatting**: Run formatter before committing
   ```bash
   dotnet format
   ```

### For Maintainers

1. **Artifact Retention**: Adjust retention days based on storage needs
2. **Runner Costs**: Monitor GitHub Actions minutes usage
3. **Security**: Regularly review vulnerability reports from nightly builds
4. **Dependencies**: Update outdated packages quarterly (check nightly reports)

---

## 🐛 Troubleshooting

### Common Issues

#### Workflow Fails on `dotnet workload restore`
- **Cause:** Missing .NET workloads in project
- **Solution:** Ensure all required workloads are specified in project files

#### Docker Build Fails
- **Cause:** Layer cache corruption or authentication issues
- **Solution:** Re-run workflow or check GITHUB_TOKEN permissions

#### Tests Timeout
- **Cause:** Integration tests waiting for services
- **Solution:** Check service container health status, increase timeout

#### Artifact Upload Fails
- **Cause:** File path patterns don't match any files
- **Solution:** Verify file paths exist, check build output structure

---

## 📚 Additional Resources

- [GitHub Actions Documentation](https://docs.github.com/en/actions)
- [.NET CLI Reference](https://learn.microsoft.com/en-us/dotnet/core/tools/)
- [Conventional Commits](https://www.conventionalcommits.org/)
- [Docker Multi-Platform Builds](https://docs.docker.com/build/building/multi-platform/)

---

**Last Updated:** December 2025  
**Maintained By:** MehguViewer Core Team
- Patterns: `feat:`, `fix:`, `docs:`, `chore:`, etc.

## Nightly Workflow (`nightly.yml`)

Comprehensive testing on a schedule, runs daily at 2 AM UTC.

### Jobs

#### 1. Integration Tests
- Spins up real PostgreSQL via Docker
- Runs full integration test suite
- Tests actual database interactions
- Matrix: Ubuntu, macOS, Windows

#### 2. Cross-Platform Build
- Validates builds on all platforms
- Matrix: Ubuntu, macOS, Windows
- Ensures cross-platform compatibility

#### 3. Dependency Check
- Reports outdated NuGet packages
- Helps maintain up-to-date dependencies

### Manual Trigger

The nightly workflow can be triggered manually:

```bash
gh workflow run nightly.yml
```

## Environment Variables & Secrets

### Required Secrets

| Secret | Description | Used In |
|--------|-------------|---------|
| `GITHUB_TOKEN` | Auto-provided GitHub token | All workflows |
| `CODECOV_TOKEN` | Codecov upload token | CI workflow |

### Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `DOTNET_VERSION` | `9.0.x` | .NET SDK version |
| `CONFIGURATION` | `Release` | Build configuration |

## Caching

Workflows use caching for:
- NuGet packages (`~/.nuget/packages`)
- Build artifacts between jobs

Cache key pattern: `nuget-{os}-{hash(packages.lock.json)}`

## Docker Image

Images are published to GitHub Container Registry:

```bash
# Pull latest
docker pull ghcr.io/YOUR-ORG/mehguviewer-core:latest

# Pull specific commit
docker pull ghcr.io/YOUR-ORG/mehguviewer-core:sha-abc1234
```

### Build Locally

```bash
docker build -t mehguviewer-core:local .
docker run -p 6230:6230 mehguviewer-core:local
```

## Coverage Reports

Coverage reports are:
1. Generated using Coverlet
2. Uploaded to Codecov
3. Available as workflow artifacts

View coverage at: https://codecov.io/gh/YOUR-ORG/mehguviewer-core

## Troubleshooting

### Build Failures

1. Check NuGet package restore
2. Verify .NET SDK version matches
3. Review build logs for specific errors

### Test Failures

1. Run tests locally first
2. Check for environment-specific issues
3. Review test logs in workflow output

### Docker Build Issues

1. Verify Dockerfile syntax
2. Check base image availability
3. Review multi-stage build steps

## Local Development

Run the same checks locally before pushing:

```bash
# Build
dotnet build MehguViewer.sln --configuration Release

# Test with coverage
dotnet test Tests/Tests.csproj /p:CollectCoverage=true

# Format check
dotnet format --verify-no-changes

# Docker build
docker build -t test .
```
