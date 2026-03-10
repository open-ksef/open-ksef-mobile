# Contributing to OpenKSeF Mobile

Thank you for your interest in contributing to OpenKSeF Mobile! This guide will help you get started.

## Getting started

1. **Fork** the repository and clone your fork
2. Set up the dev environment (see [README.md](README.md#development-setup))
3. Create a feature branch from `main`
4. Make your changes
5. Run tests to verify nothing is broken
6. Submit a pull request

## Development setup

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) with MAUI workloads
- [Android Studio](https://developer.android.com/studio) (for Android emulator)
- [Xcode](https://developer.apple.com/xcode/) (macOS, for iOS simulator)
- A GitHub PAT with `read:packages` scope (for CIRFMF NuGet feed -- see README)

### Install MAUI workloads

```bash
dotnet workload install maui-android
dotnet workload install maui-ios   # macOS only
```

### Build

```bash
dotnet build src/OpenKSeF.Mobile.slnx
```

### Running tests

```bash
# Unit tests (no device required)
dotnet test src/OpenKSeF.Mobile.Tests/OpenKSeF.Mobile.Tests.csproj

# Android E2E tests (requires backend stack + emulator + Appium)
# First: start the backend (see open-ksef backend repo)
./scripts/setup-android-e2e.ps1
./scripts/run-mobile-e2e.ps1
```

## Code style

- **.NET:** Follow standard C# conventions. Use `dotnet format` before committing.
- **Commits:** Write clear, concise commit messages. Use imperative mood ("Add feature" not "Added feature").

## Pull request process

1. Ensure all tests pass
2. Update documentation if your change affects public APIs or configuration
3. Keep PRs focused -- one feature or fix per PR
4. Fill in the PR template describing your changes and how to test them
5. **Sign the CLA** -- the bot will comment on your first PR with instructions
6. A maintainer will review your PR and may request changes

## Reporting bugs

Use [GitHub Issues](../../issues) with the **Bug report** template. Include:

- Steps to reproduce
- Expected vs actual behavior
- Environment details (OS, .NET version, Android/iOS version)

## Requesting features

Use [GitHub Issues](../../issues) with the **Feature request** template. Describe:

- The problem you're trying to solve
- Your proposed solution
- Any alternatives you've considered

## Security vulnerabilities

**Do NOT open a public issue for security vulnerabilities.** See [SECURITY.md](SECURITY.md) for responsible disclosure instructions.

## Contributor License Agreement (CLA)

Before your pull request can be merged, you must sign the
[Contributor License Agreement](CLA.md). This is a one-time step per
contributor.

When you open a PR, the CLA bot will post a comment with signing instructions.
You sign by replying to the comment with a specific phrase. Your signature is
recorded in this repository.

The CLA grants the copyright holder the right to dual-license your
contributions (open source + commercial), while you retain full copyright
ownership of your work.

## License

By contributing, you agree that your contributions will be licensed under the
[Elastic License 2.0 (ELv2)](LICENSE). The copyright holder reserves the right
to offer OpenKSeF under alternative commercial licensing terms. See [CLA.md](CLA.md).
