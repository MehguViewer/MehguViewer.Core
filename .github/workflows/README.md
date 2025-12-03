# CI/CD Workflows

This document describes the GitHub Actions workflows for MehguViewer.Core.

## Workflow Files

| Workflow | File | Triggers | Purpose |
|----------|------|----------|---------|
| Continuous Integration | `ci.yml` | Push, PR | Main CI pipeline |
| Build Artifacts | `build-artifacts.yml` | Push | Build & upload executables |
| PR Checks | `pr-checks.yml` | Pull Request | Quick PR validation |
| Nightly Build | `nightly.yml` | Schedule (2 AM UTC) | Extended testing |

## Build Artifacts Workflow (`build-artifacts.yml`)

Builds single-file executables for all platforms on every push and uploads them as artifacts.

### Platforms

| Artifact Name | Platform | Architecture |
|---------------|----------|--------------|
| `MehguViewer-linux-x64` | Linux | x64 |
| `MehguViewer-linux-arm64` | Linux | ARM64 |
| `MehguViewer-win-x64` | Windows | x64 |
| `MehguViewer-osx-x64` | macOS | Intel x64 |
| `MehguViewer-osx-arm64` | macOS | Apple Silicon |

### Features

- **Single-file**: All dependencies bundled into one executable
- **Self-contained**: No .NET runtime required on target machine
- **Compressed**: Smaller file size with compression enabled
- **30-day retention**: Artifacts available for download for 30 days

### Download Artifacts

1. Go to the [Actions tab](../../actions/workflows/build-artifacts.yml)
2. Click on a successful workflow run
3. Scroll to "Artifacts" section
4. Download the artifact for your platform

## CI Workflow (`ci.yml`)

The main CI workflow runs on every push to `main`/`develop` branches and on PRs.

### Jobs

#### 1. Build
- Restores NuGet packages
- Builds the solution in Release mode
- Caches NuGet packages for faster builds

#### 2. Test
- Runs all tests with code coverage
- Generates Cobertura coverage report
- Uploads coverage to Codecov
- Requires: `build`

#### 3. Code Quality
- Runs `dotnet format` to check formatting
- Reports any formatting issues
- Requires: `build`

#### 4. Docker
- Builds Docker image
- Pushes to GitHub Container Registry (GHCR)
- Tags: `latest`, `sha-{commit}`, `{branch}`
- Only runs on push to `main`
- Requires: `test`, `code-quality`

#### 5. Release
- Creates multi-platform release builds
- Platforms: `win-x64`, `linux-x64`, `osx-x64`, `osx-arm64`
- Generates self-contained publishable artifacts
- Only runs on tags (`v*`)
- Requires: `test`

### Status Badge

```markdown
![CI](https://github.com/YOUR-ORG/mehguviewer-core/actions/workflows/ci.yml/badge.svg)
```

## PR Checks Workflow (`pr-checks.yml`)

Quick validation for pull requests to catch issues early.

### Jobs

#### 1. Quick Validate
- Fast build validation (2-minute timeout)
- Runs smoke tests only
- Provides quick feedback on PRs

#### 2. PR Size Check
- Warns if PR is too large (>500 lines changed)
- Encourages smaller, focused PRs

#### 3. Conventional Commits
- Validates PR title follows conventional commit format
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
