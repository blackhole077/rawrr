# RawrrAssembly Executables

This directory is a placeholder for documentation. **Executables are NOT stored in the repository** to keep the repo size manageable (each executable is ~120 MB).

## Where to Download

Pre-built executables are available as **GitHub Release Assets**. Download them from the latest release:

**[→ Go to Releases](https://github.com/blackhole077/rawrr/releases)**

### Direct Download URLs (Latest Release)

Replace `<version>` with the release tag (e.g., `v1.0.0`):

```text
https://github.com/blackhole077/rawrr/releases/download/<version>/rawrr-linux-x64
https://github.com/blackhole077/rawrr/releases/download/<version>/rawrr-win-x64.exe
https://github.com/blackhole077/rawrr/releases/download/<version>/rawrr-osx-x64
```

### Download Commands

#### Linux

```bash
wget https://github.com/blackhole077/rawrr/releases/download/<version>/rawrr-linux-x64
chmod +x rawrr-linux-x64
```

#### Windows

```powershell
Invoke-WebRequest -Uri "https://github.com/blackhole077/rawrr/releases/download/<version>/rawrr-win-x64.exe" -OutFile "rawrr-win-x64.exe"
```

#### macOS

```bash
curl -LO https://github.com/blackhole077/rawrr/releases/download/<version>/rawrr-osx-x64
chmod +x rawrr-osx-x64
```

## Automatic Build Process

When changes are pushed to the `devel` branch that affect `inst/rawrrassembly/`:

1. **Build Workflow** (`.github/workflows/build_rawrrassembly.yml`):
   - Automatically builds executables for Linux, Windows, and macOS
   - Stores them as GitHub Actions artifacts (available for 90 days)
   - Does NOT commit them to the repository

2. **Release Workflow** (`.github/workflows/cd_rawrrassembly.yml`):
   - Triggered when you publish a GitHub release
   - Builds fresh executables
   - Verifies them on all three platforms
   - Attaches them to the release as downloadable assets

## For Development/Testing

If you need the latest development builds (not yet released):

1. Go to [Actions → Build RawrrAssembly on Devel](https://github.com/blackhole077/rawrr/actions/workflows/build_rawrrassembly.yml)
2. Click on the most recent successful workflow run
3. Download the `rawrr-executables` artifact (available for 90 days)

## Usage

These executables wrap the ThermoFisher RawFileReader library and can be used to read Thermo RAW mass spectrometry data files.

Example:

```bash
# Make executable (Linux/macOS only)
chmod +x rawrr-linux-x64

# Run it
./rawrr-linux-x64 path/to/sample.raw index
```

## Why Not Store in Git?

- Each executable is ~120 MB
- GitHub has a 100 MB file size limit (would need Git LFS)
- Rebuilding on each push would bloat repository history
- Release assets provide unlimited storage for binaries
