#!/bin/bash
# Build script for rawrr .NET assembly
# 
# This script builds cross-platform executables for the rawrr .NET application
# that wraps the ThermoFisher RawFileReader library.
#
# Usage: build.sh <project_folder> <output_folder>
#
# Arguments:
#   project_folder  - Path to the directory containing rawrr.csproj and rawrr.cs
#                     Can be relative or absolute. Must contain a .csproj file.
#                     Example: rawrr/inst/rawrrassembly
#
#   output_folder   - Path where the compiled executables will be placed
#                     Can be relative or absolute. Will be created if it doesn't exist.
#                     Example: output/
#
# Output:
#   Creates three platform-specific executables in the output folder:
#     - rawrr-linux-x64      (Linux)
#     - rawrr-osx-x64        (macOS)
#     - rawrr-win-x64.exe    (Windows)
#
# Requirements:
#   - .NET SDK 8.0 or later
#   - git
#   - Internet connection (for first run to clone dependencies)
#
# Environment:
#   - Uses /tmp/build-dir for dependency caching (RawFileReader repo)
#   - Uses /tmp/build as temporary workspace for compilation
#   - Reuses cached dependencies on subsequent runs
#
# Example:
#   cd ~/repos
#   rawrr/inst/rawrrassembly/build.sh rawrr/inst/rawrrassembly/ output/
#
set -euxo pipefail

if [ "$#" -ne 2 ]; then
    echo "Usage: $0 <project_folder> <output_folder>"
    echo ""
    echo "Arguments:"
    echo "  project_folder  - Directory containing the .csproj file"
    echo "  output_folder   - Directory where executables will be placed"
    echo ""
    echo "Example:"
    echo "  $0 rawrr/inst/rawrrassembly/ output/"
    exit 1
fi
# Convert to absolute paths to avoid issues after changing directories
project_folder="$(cd "$1" && pwd)"
output_folder="$(cd "$(dirname "$2")" && pwd)/$(basename "$2")"
work_folder="/tmp/build"

# Validate that project folder contains a .csproj file
if ! ls "$project_folder"/*.csproj 1> /dev/null 2>&1; then
    echo "ERROR: No .csproj file found in $project_folder"
    echo "Please ensure you're pointing to the correct project directory."
    exit 1
fi

echo "=== Build Configuration ==="
echo "Project folder: $project_folder"
echo "Output folder: $output_folder"
echo "Work folder: $work_folder"
echo "=========================="

# debug: 
#apt-get update && apt-get upgrade -y && apt-get install -y zip tree

# Checkout the deps
echo "Creating and moving to /tmp/build-dir..."
mkdir -p /tmp/build-dir && cd /tmp/build-dir
if [ -d "RawFileReader" ]; then
    echo "RawFileReader already exists, pulling latest changes..."
    cd RawFileReader
    git pull origin main || echo "Warning: Could not pull latest changes, using existing version"
    cd ..
else
    echo "Cloning RawFileReader..."
    git clone --depth=1 https://github.com/thermofisherlsms/RawFileReader.git
fi

# Add NuGet source if not already present
nuget_source="$PWD/RawFileReader/Libs/NetCore/Net8/"
echo "Checking NuGet source: $nuget_source"
if ! dotnet nuget list source | grep -q "$nuget_source"; then
    dotnet nuget add source "$nuget_source"
else
    echo "NuGet source already configured, skipping..."
fi

# Perform the release
echo "Cleaning and preparing work folder: $work_folder..."
rm -rf "$work_folder"
mkdir -p "$work_folder"

echo "Copying project files from $project_folder to $work_folder..."
# Only copy the necessary files, excluding subdirectories and git files
cp "$project_folder"/*.cs "$work_folder"/ 2>/dev/null || true
cp "$project_folder"/*.csproj "$work_folder"/ 2>/dev/null || true
cp "$project_folder"/*.txt "$work_folder"/ 2>/dev/null || true
cp "$project_folder"/*.md "$work_folder"/ 2>/dev/null || true

echo "Moving to work folder: $work_folder"
cd "$work_folder"
runtimes="linux-x64 osx-x64 win-x64"
echo "Building for runtimes: $runtimes"
for runtime in $runtimes; do
    echo "Publishing for $runtime..."
    dotnet publish --runtime "$runtime" -c Release
done

# debug:
#tree bin/Release/net8.0
echo "Creating output folder: $output_folder"
mkdir -p "$output_folder"

# Export the result
echo "Copying executables to output folder..."
for runtime in $runtimes; do
    source_path="bin/Release/net8.0/$runtime/publish"
    suffix=""
    if [[ "$runtime" == win-* ]]; then
        suffix=".exe"
    fi
    echo "  $runtime: $source_path/rawrr$suffix -> $output_folder/rawrr-$runtime$suffix"
    cp "$source_path/rawrr$suffix" "$output_folder/rawrr-$runtime$suffix"
done

echo "=== Build completed successfully ==="
echo "Output files in: $output_folder"
