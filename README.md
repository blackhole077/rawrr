[![Project Status: Active – The project has reached a stable, usable state and is being actively developed.](https://www.repostatus.org/badges/latest/active.svg)](https://www.repostatus.org/#active)

![rawrrHexSticker](rawrr_logo.png)

# rawrr (Fork)

This repository is a fork of [fgcz/rawrr](https://github.com/fgcz/rawrr). The fork introduces several changes and improvements:

- **Refactored codebase** for improved maintainability and modularity.
- **Enhanced support for additional Thermo Fisher Scientific Orbitrap data formats.**
- **Updated dependencies** and build scripts for compatibility with newer R and .NET versions.
- **New command-line executables** for streamlined data access and processing.
- **Expanded documentation** and usage examples.

## Building and Using Executables

Instructions for building and using the provided executables are available in [`bin/README.md`](bin/README.md). In summary:

1. **Build the executables** using the provided scripts or Makefile.
2. **Run the tools** from the `bin/` directory, following usage examples in the documentation.
3. **Ensure required dependencies** (R, .NET, RawFileReader) are installed as described.

## Package Overview

rawrr provides access to proprietary Thermo Fisher Scientific Orbitrap instrument data as a stand-alone R package or as a backend for the Bioconductor [Spectra](https://bioconductor.org/packages/Spectra/) package. It wraps the functionality of the [RawFileReader](https://github.com/thermofisherlsms/RawFileReader) .NET assembly. Test files are available via the [tartare](https://bioconductor.org/packages/tartare/) ExperimentData package.

## Installation

To avoid package conflicts with the original `rawrr` package, please consult [this document](bin/README.md) to download the executable associated with
this version of `rawrr`. Afterwards, please replace your existing `rawrr` executable with the downloaded package.
