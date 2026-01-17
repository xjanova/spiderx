# Contributing to SpiderX

Thank you for your interest in contributing to SpiderX! This document provides guidelines and instructions for contributing.

## Table of Contents

- [Code of Conduct](#code-of-conduct)
- [Getting Started](#getting-started)
- [Development Setup](#development-setup)
- [Making Changes](#making-changes)
- [Coding Standards](#coding-standards)
- [Testing](#testing)
- [Submitting Changes](#submitting-changes)
- [Release Process](#release-process)

## Code of Conduct

By participating in this project, you agree to abide by our [Code of Conduct](CODE_OF_CONDUCT.md). Please read it before contributing.

## Getting Started

### Prerequisites

- .NET 8.0 SDK or later
- Visual Studio 2022 / VS Code / JetBrains Rider
- Git

### Fork and Clone

1. Fork the repository on GitHub
2. Clone your fork locally:
   ```bash
   git clone https://github.com/YOUR_USERNAME/spiderx.git
   cd spiderx
   ```
3. Add the upstream remote:
   ```bash
   git remote add upstream https://github.com/ORIGINAL_OWNER/spiderx.git
   ```

## Development Setup

### Install Dependencies

```bash
# Restore NuGet packages
dotnet restore

# Install MAUI workload (for app development)
dotnet workload install maui
```

### Build

```bash
# Build all projects
dotnet build

# Build in Release mode
dotnet build -c Release
```

### Run Tests

```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

## Making Changes

### Branch Naming

Use descriptive branch names:

| Type | Format | Example |
|------|--------|---------|
| Feature | `feature/description` | `feature/video-calls` |
| Bug fix | `fix/description` | `fix/nat-traversal` |
| Docs | `docs/description` | `docs/api-reference` |
| Refactor | `refactor/description` | `refactor/transport-layer` |

### Workflow

1. **Sync with upstream:**
   ```bash
   git fetch upstream
   git checkout main
   git merge upstream/main
   ```

2. **Create a branch:**
   ```bash
   git checkout -b feature/your-feature
   ```

3. **Make your changes** following our [coding standards](#coding-standards)

4. **Test your changes:**
   ```bash
   dotnet build
   dotnet test
   dotnet format --verify-no-changes
   ```

5. **Commit your changes:**
   ```bash
   git add .
   git commit -m "feat: add your feature description"
   ```

6. **Push and create PR:**
   ```bash
   git push origin feature/your-feature
   ```

## Coding Standards

### C# Style

We follow the [Microsoft C# Coding Conventions](https://docs.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions) with some additions:

```csharp
// Use file-scoped namespaces
namespace SpiderX.Core;

// Use expression bodies for simple members
public bool IsConnected => _connection?.IsConnected ?? false;

// Use primary constructors where appropriate
public class Peer(SpiderId id, IConnection connection)
{
    public SpiderId Id { get; } = id;
}

// Use pattern matching
if (message is ChatMessage chat)
{
    await HandleChatAsync(chat);
}

// Prefer async/await for I/O operations
public async Task SendAsync(byte[] data)
{
    await _stream.WriteAsync(data);
}
```

### Naming Conventions

| Type | Convention | Example |
|------|------------|---------|
| Class | PascalCase | `SpiderNode` |
| Interface | IPascalCase | `ITransport` |
| Method | PascalCase | `SendAsync` |
| Property | PascalCase | `IsConnected` |
| Private field | _camelCase | `_connections` |
| Parameter | camelCase | `peerId` |
| Constant | PascalCase | `MaxPeers` |

### Documentation

- Add XML documentation to all public APIs
- Include examples in documentation where helpful
- Keep comments up-to-date with code changes

```csharp
/// <summary>
/// Sends a message to a peer.
/// </summary>
/// <param name="peerId">The recipient's identifier.</param>
/// <param name="message">The message to send.</param>
/// <returns>A task representing the async operation.</returns>
/// <exception cref="PeerNotFoundException">Thrown when peer is not connected.</exception>
public async Task SendAsync(SpiderId peerId, Message message)
```

### Security Guidelines

- **Never** log private keys or decrypted content
- **Always** verify signatures before processing
- **Always** validate external input
- Use `CryptographicOperations.FixedTimeEquals()` for comparisons
- Dispose `KeyPair` and other crypto objects properly

## Testing

### Test Requirements

- All new features must have tests
- Bug fixes should include regression tests
- Aim for >80% code coverage on new code

### Test Structure

```csharp
[Fact]
public void MethodName_StateUnderTest_ExpectedBehavior()
{
    // Arrange
    var sut = new SystemUnderTest();

    // Act
    var result = sut.DoSomething();

    // Assert
    result.Should().BeTrue();
}
```

### Running Tests

```bash
# Run all tests
dotnet test

# Run specific test class
dotnet test --filter "FullyQualifiedName~SpiderIdTests"

# Run with verbosity
dotnet test -v detailed
```

## Submitting Changes

### Commit Messages

Follow [Conventional Commits](https://www.conventionalcommits.org/):

```
<type>[optional scope]: <description>

[optional body]

[optional footer(s)]
```

**Types:**
- `feat`: New feature
- `fix`: Bug fix
- `docs`: Documentation
- `style`: Formatting
- `refactor`: Code restructuring
- `test`: Adding tests
- `chore`: Maintenance

**Examples:**
```
feat(voice): add noise cancellation

Implement noise cancellation using RNNoise library.
- Add RNNoise wrapper
- Integrate with VoiceService
- Add configuration option

Closes #123
```

### Pull Request Process

1. **Ensure CI passes** - All tests and checks must pass
2. **Update documentation** - If your change affects the API or behavior
3. **Add changelog entry** - For user-facing changes
4. **Request review** - Tag relevant reviewers
5. **Address feedback** - Respond to review comments
6. **Squash if needed** - Keep commit history clean

### PR Checklist

- [ ] Code follows project style guidelines
- [ ] Tests added/updated
- [ ] Documentation updated
- [ ] No new warnings
- [ ] CI passes

## Release Process

### Versioning

We use [Semantic Versioning](https://semver.org/):

- **MAJOR**: Breaking changes
- **MINOR**: New features (backward compatible)
- **PATCH**: Bug fixes (backward compatible)

### Creating a Release

1. **Update version** using the Version Bump workflow:
   - Go to Actions → Version Bump
   - Select bump type (major/minor/patch)
   - Run workflow

2. **Review changelog** in the generated release

3. **Publish release** (automated after version bump)

### Pre-releases

For pre-releases, use version bump with prerelease identifier:
- `1.0.0-alpha`
- `1.0.0-beta`
- `1.0.0-rc.1`

## Getting Help

- **Questions**: Open a [Question issue](.github/ISSUE_TEMPLATE/question.yml)
- **Bugs**: Open a [Bug report](.github/ISSUE_TEMPLATE/bug_report.yml)
- **Features**: Open a [Feature request](.github/ISSUE_TEMPLATE/feature_request.yml)
- **Discussion**: Use GitHub Discussions (if enabled)

## Recognition

Contributors are recognized in:
- Release notes
- README.md contributors section
- GitHub contributors page

Thank you for contributing to SpiderX! 🕷️
