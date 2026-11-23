# CoreSentinel

[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011%20(x64)-0078D6)](https://github.com/levluklukyanov/core-sentinel)
[![Framework](https://img.shields.io/badge/.NET-10.0%20WPF-512BD4)](https://dotnet.microsoft.com/)
[![Language](https://img.shields.io/badge/C%23-13.0-239120)](https://docs.microsoft.com/dotnet/csharp/)
[![Release](https://img.shields.io/badge/Release-v1.0.0-success)](https://github.com/levluklukyanov/core-sentinel/releases)

High-performance Windows system utility, real-time hardware telemetry monitor, and gaming resource manager built with C# 13 and .NET 10 (WPF).

---

## Overview

CoreSentinel provides real-time system monitoring, dual-engine memory and CPU optimization, game process prioritization, and disk maintenance within a single, lightweight desktop interface designed with engineering minimalism.

## Features

### Hardware Telemetry
- Real-time tracking of CPU load, RAM allocation, and storage drives via WMI and Win32 Native APIs.
- Detailed operation log with timestamps and horizontal navigation.

### Resource Optimization
- Memory purge: working set trimming and system standby cache flushing.
- CPU balancing: execution queue normalization and background process deprioritization.

### Game Sentinel
- Automatic detection of active game sessions with instant execution priority boost.
- Storage-wide game library scanner with duplicate exclusion and Minecraft launcher filtering.

### Storage Management
- Partition diagnostics and batch disk cleanup for system temp files, delivery optimization cache, and DISM image resets.

### Floating Telemetry HUD
- Low-overhead, semi-transparent overlay widget for on-screen monitoring during gaming sessions and compute workloads.

### Interface & Ergonomics
- Clean Dark and Light themes adhering to Swiss modernist and engineering design principles.
- Tab Vault system to hide, restore, and reorder navigation tabs.
- Bundled system fonts: Golos Text, Inter, JetBrains Mono, Manrope, Onest.
- Persistent user configuration saved across application sessions.

---

## Installation

1. Navigate to the [Releases](https://github.com/levluklukyanov/core-sentinel/releases) page.
2. Download the standalone executable `CoreSentinel.exe`.
3. Launch the application.

> **Note on executable size (~65 MB):**  
> The application is packaged as a Self-Contained Single-File binary. The complete .NET 10 runtime, native WPF dependencies, and typography assets are embedded directly into the executable. It runs out of the box on clean Windows 10/11 installations without requiring prior installation of the .NET SDK or Runtime packages.

---

## Building from Source

### Prerequisites
- Windows 10 / 11 (x64)
- [.NET 10.0 SDK](https://dotnet.microsoft.com/)

### Build Commands

```bash
# Clone the repository
git clone https://github.com/levluklukyanov/core-sentinel.git
cd core-sentinel

# Build the solution in Release configuration
dotnet build CoreSentinel.sln -c Release

# Publish standalone single-file binary
dotnet publish CoreSentinel.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish
```
