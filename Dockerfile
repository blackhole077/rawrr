# Dockerfile for rawrr package with ThermoFisher RawFileReader support
# Based on Bioconductor devel branch with .NET 8.0 SDK
#
# This Dockerfile provides a complete environment for:
# - Building and running the rawrr R package
# - Compiling C# code using .NET 8.0 SDK
# - Accessing ThermoFisher RawFileReader assemblies
# - Running R CMD build and R CMD check
#
# Build command:
#   docker build -t rawrr:latest .
#
# Run command:
#   docker run -it --rm -v $(pwd):/workspace rawrr:latest

FROM bioconductor/bioconductor_docker:devel

LABEL maintainer="rawrr developers"
LABEL description="Docker image for rawrr package with .NET 8.0 SDK and ThermoFisher RawFileReader support"
LABEL version="1.0"

# Update package lists and install system dependencies
RUN apt-get update && apt-get install -y \
    # .NET 8.0 SDK for compiling rawrr.cs
    dotnet-sdk-8.0 \
    # Build tools
    build-essential \
    # Additional utilities
    wget \
    curl \
    git \
    vim \
    # Clean up to reduce image size
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*

# Install required R packages from Bioconductor and CRAN
RUN R -q -e "BiocManager::install(c( \
    'BiocStyle', \
    'ExperimentHub', \
    'knitr', \
    'protViz', \
    'rmarkdown', \
    'tartare', \
    'testthat', \
    'Spectra' \
), ask = FALSE, update = TRUE)"

# Set environment variables for .NET
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
ENV DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

# Create working directory
WORKDIR /workspace

# Verify .NET installation
RUN dotnet --version

# Set default command to bash
CMD ["/bin/bash"]

# Usage examples:
# 
# Build the Docker image:
#   docker build -t rawrr:latest .
#
# Run interactively with current directory mounted:
#   docker run -it --rm -v $(pwd):/workspace rawrr:latest
#
# Build rawrr package inside container:
#   docker run -it --rm -v $(pwd):/workspace rawrr:latest R CMD build /workspace
#
# Check rawrr package inside container:
#   docker run -it --rm -v $(pwd):/workspace rawrr:latest R CMD check /workspace/rawrr_*.tar.gz
#
# Compile rawrr.exe from source:
#   docker run -it --rm -v $(pwd):/workspace rawrr:latest bash -c \
#     "cd /workspace/inst/rawrrassembly && ./build.sh"
