# Platform-Specific Testing Strategy

This document outlines our comprehensive approach to testing TrashMail Panda across different platforms, including Docker-based containerized testing and platform-specific limitations.

## Overview

TrashMail Panda uses platform-specific security implementations:
- **Windows**: DPAPI (Data Protection API)
- **macOS**: Keychain Services  
- **Linux**: libsecret with gnome-keyring

Our testing strategy ensures each platform's security implementation works correctly while providing fallback options for cross-platform development.

## Testing Approaches

### 1. Docker-Based Testing (Linux + Cross-Platform)

#### ✅ Linux Platform Tests
```bash
./scripts/run-linux-tests.sh
```

**Features:**
- Full libsecret + gnome-keyring environment
- Headless X11 setup for CI environments
- Containerized Ubuntu environment matching CI

**Use Cases:**
- Verify libsecret integration works correctly
- Test Linux-specific error handling
- Ensure proper fallback behavior

#### ✅ Cross-Platform Tests  
```bash
./scripts/run-cross-platform-tests.sh
```

**Features:**
- Platform-agnostic unit tests
- Integration tests that don't require platform-specific APIs
- Comprehensive coverage reporting

**Use Cases:**
- Core business logic testing
- Cross-platform compatibility validation
- CI/CD pipeline integration

### 2. Windows Container Testing

#### ⚠️ Windows Platform Tests (Limited)
```powershell
.\scripts\run-windows-tests.ps1
```

**Requirements:**
- Windows 10/11 or Windows Server 2016+
- Docker Desktop with Windows containers enabled
- Must run on Windows host machine

**Limitations:**
- Windows containers can **only** run on Windows hosts
- Requires Docker Desktop Windows container mode
- Not available on Linux/macOS development machines

**Use Cases:**
- Windows-specific DPAPI testing
- Windows CI/CD runners
- Windows developer local testing

### 3. macOS Testing (Native Only)

#### ❌ macOS Container Testing (Not Possible)
macOS containers are **not supported** due to:
- Apple's Software License Agreement restrictions
- No official Docker macOS base images
- Technical limitations of macOS containerization

#### ✅ Native macOS Testing
```bash
./scripts/run-macos-tests.sh
```

**Requirements:**
- Must run on actual macOS hardware
- macOS 10.15+ (Catalina or later)
- Keychain Services available

**Use Cases:**
- macOS developer local testing
- GitHub Actions macOS runners
- Keychain Services integration validation

## VS Code Integration

### Available Tasks

| Task | Command | Description |
|------|---------|-------------|
| 🐳 Multi-Platform Docker Tests | `Cmd+Shift+P` → "Tasks: Run Task" | Run cross-platform tests in Docker |
| 🐧 Linux Platform Tests | `Cmd+Shift+P` → "Tasks: Run Task" | Run Linux-specific libsecret tests |
| 🪟 Windows Platform Tests | `Cmd+Shift+P` → "Tasks: Run Task" | Run Windows DPAPI tests (Windows only) |
| 🔄 Full Platform Test Suite | `Cmd+Shift+P` → "Tasks: Run Task" | Run all available platform tests |
| 🏗️ Build Docker Test Images | `Cmd+Shift+P` → "Tasks: Run Task" | Pre-build test containers |

### Recommended Workflow

#### For All Developers:
1. **🐳 Multi-Platform Docker Tests** - Always works, tests core functionality
2. **🐧 Linux Platform Tests** - Tests Linux security implementation

#### For Windows Developers:
3. **🪟 Windows Platform Tests** - Test DPAPI integration locally

#### For macOS Developers:
3. **Native macOS Testing** - Use `./scripts/run-macos-tests.sh`

## CI/CD Pipeline Integration

### GitHub Actions Strategy

```yaml
# Current setup supports all platforms
strategy:
  matrix:
    include:
      - os: ubuntu-latest    # Native Linux + Docker Linux tests
      - os: windows-latest   # Native Windows + Docker Windows tests  
      - os: macos-latest     # Native macOS tests only
```

### Docker Integration in CI

#### Linux Runner (ubuntu-latest):
- ✅ Docker Linux containers
- ✅ Native Linux testing
- ✅ Cross-platform validation

#### Windows Runner (windows-latest):
- ✅ Docker Windows containers (when enabled)
- ✅ Native Windows DPAPI testing
- ✅ Cross-platform validation

#### macOS Runner (macos-latest):
- ❌ No Docker macOS containers
- ✅ Native macOS Keychain testing
- ✅ Cross-platform validation

## Alternative Solutions for macOS Testing

Since macOS containers aren't possible, developers on Linux/Windows can:

### 1. Mock-Based Testing
```csharp
// Example: Mock macOS Keychain for cross-platform testing
public class MockMacOSKeychain : IKeychainService
{
    // Implementation that simulates macOS behavior
}
```

### 2. GitHub Actions Integration
```yaml
jobs:
  macos-tests:
    runs-on: macos-latest
    steps:
      - name: Run macOS Platform Tests
        run: ./scripts/run-macos-tests.sh
```

### 3. Remote Testing Services
- Use cloud-based macOS instances
- GitHub Actions provides free macOS runners
- CI/CD pipeline automatically tests all platforms

## Quick Start Guide

### Setup Docker Environment
```bash
# Build all test containers
docker-compose -f docker-compose.tests.yml build

# Run comprehensive cross-platform tests
./scripts/run-cross-platform-tests.sh

# Run Linux-specific tests
./scripts/run-linux-tests.sh
```

### VS Code Integration
1. Open Command Palette (`Cmd+Shift+P`)
2. Type "Tasks: Run Task"
3. Select desired test task
4. View results in integrated terminal

### Local Development Workflow
```bash
# 1. Quick cross-platform validation
./scripts/run-cross-platform-tests.sh

# 2. Platform-specific testing
./scripts/run-linux-tests.sh        # Linux (works everywhere via Docker)
./scripts/run-windows-tests.ps1     # Windows (Windows hosts only)
./scripts/run-macos-tests.sh        # macOS (macOS hosts only)

# 3. Full CI/CD validation
dotnet test --configuration Release  # Standard .NET testing
```

## Troubleshooting

### Docker Issues
```bash
# Check Docker availability
docker --version

# Rebuild test images
docker-compose -f docker-compose.tests.yml build --no-cache

# Clean up test environment
docker-compose -f docker-compose.tests.yml down --rmi local --volumes
```

### Platform-Specific Issues

#### Linux/libsecret:
- Ensure Docker has proper permissions
- Check gnome-keyring setup in container

#### Windows/DPAPI:
- Verify Windows containers enabled in Docker Desktop
- Check Windows container mode: `docker system info`

#### macOS/Keychain:
- Run on actual macOS hardware only
- Ensure Keychain Services are accessible

## Summary

Our platform testing strategy provides:

✅ **Comprehensive Coverage**: All platforms tested via appropriate methods  
✅ **Developer Flexibility**: Docker enables cross-platform development  
✅ **CI/CD Integration**: Automated testing across all target platforms  
✅ **Fallback Options**: Graceful handling when platform APIs unavailable  

**Limitations:**
- macOS containers not possible (technical + legal restrictions)
- Windows containers require Windows hosts
- Some platform-specific tests require native hardware

This approach ensures robust cross-platform functionality while acknowledging platform-specific constraints.