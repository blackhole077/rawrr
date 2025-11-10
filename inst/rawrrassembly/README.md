# rawrr.exe Command Line Usage

`rawrr.exe` is a command-line tool for extracting information from Thermo RAW files. This document provides usage instructions, available options, and practical examples.

## Table of Contents

- [Overview](#overview)
- [Installation](#installation)
- [Command Line Options](#command-line-options)
  - [Index](#index)
  - [Chromatograms](#chromatograms)
  - [Scan](#scan)
  - [Metadata](#metadata)
  - [Help](#help)
- [Examples](#examples)
- [Building from Source](#building-from-source)
- [Troubleshooting](#troubleshooting)

---

## Overview

`rawrr.exe` enables extraction of scan indices, chromatograms, scan data, and metadata from Thermo RAW files for downstream analysis.

## Installation

Download the appropriate binary for your operating system, or build from source (see [Building from Source](#building-from-source)).

## Command Line Options

### Index

List all scan indices in a RAW file.

```sh
rawrr.exe <rawfile> index
```

- `<rawfile>`: Path to the Thermo RAW file.

### Chromatograms

Extract chromatogram data from a RAW file.

```sh
rawrr.exe <rawfile> chromatogram [<scan filter>] [<output file>]
```

- `<rawfile>`: Path to the Thermo RAW file.
- `<scan filter>`: (Optional) Filter string to select specific scans (e.g., `"ms"` or `"ms2"`).
- `<output file>`: (Optional) Path to save the extracted chromatogram data (e.g., CSV or TXT).

If `<scan filter>` and `<output file>` are omitted, all chromatograms are extracted and printed to stdout.

### Scan

Extract scan data from a RAW file.

```sh
rawrr.exe <rawfile> scan <scan number> [<output file>]
```

- `<rawfile>`: Path to the Thermo RAW file.
- `<scan number>`: Scan number to extract.
- `<output file>`: (Optional) Path to save the scan data.

### Metadata

Display metadata information about a RAW file.

```sh
rawrr.exe <rawfile> metadata
```

- `<rawfile>`: Path to the Thermo RAW file.

### Help

Display usage information.

```sh
rawrr.exe help
```

- No arguments required.

## Examples

### List Scan Indices

```sh
rawrr.exe sample.raw index
```

### Extract All Chromatograms

```sh
rawrr.exe sample.raw chromatogram
```

### Extract MS2 Chromatograms to File

```sh
rawrr.exe sample.raw chromatogram "ms2" ms2_chromatograms.csv
```

### Extract a Specific Scan

```sh
rawrr.exe sample.raw scan 1234 scan_1234.txt
```

### Show RAW File Metadata

```sh
rawrr.exe sample.raw metadata
```

## Building from Source

To build `rawrr.exe` for different platforms, use the following commands:

```sh
dotnet publish rawrr-dotnet.csproj --os osx -a x64 --output /Users/cp/Library/Caches/org.R-project.R/R/rawrr/rawrrassembly/osx-x64
dotnet publish rawrr-dotnet.csproj --os win -a x64 --output /Users/cp/Library/Caches/org.R-project.R/R/rawrr/rawrrassembly/win-x64
dotnet publish rawrr-dotnet.csproj --os linux -a x64 --output /Users/cp/Library/Caches/org.R-project.R/R/rawrr/rawrrassembly/linux-x64
```

## Troubleshooting

- Ensure the correct .NET runtime is installed for your platform.
- For permission errors, check file paths and access rights.
- For RAW file compatibility issues, verify the file format and version.

---

For more details, see the project documentation or contact the maintainers.
