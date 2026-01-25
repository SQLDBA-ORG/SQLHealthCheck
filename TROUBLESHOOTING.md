# 🔧 Troubleshooting Guide - "Projects Not Found"

## Problem: Visual Studio shows "project unavailable" or "unable to find project"

This happens when Visual Studio looks for the project files but can't find them. Here are solutions:

## ✅ Solution 1: Extract to a Simple Path (RECOMMENDED)

Visual Studio sometimes has issues with long paths or special characters.

1. **Extract the entire folder** from your downloads
2. **Move it to a simple location** like:
   - `C:\Projects\SqlHealthCheck\`
   - `C:\Dev\SqlHealthCheck\`
   - `D:\SqlHealthCheck\`

3. **Open the .sln file** from that location

### Example:
```
GOOD:
C:\Projects\SqlHealthCheck\SqlHealthCheck.sln

BAD:
C:\Users\YourName\Downloads\SqlHealthCheck (1)\SqlHealthCheck.sln
```

## ✅ Solution 2: Manually Add Projects

If moving doesn't work:

1. Open Visual Studio
2. Create a **New Blank Solution** named "SqlHealthCheck"
3. Right-click solution → **Add → Existing Project**
4. Navigate and add these 3 projects:
   - `SqlCheckLibrary\SqlCheckLibrary.csproj`
   - `SqlCheckDemo\SqlCheckDemo.csproj`
   - `SqlMonitorUI\SqlMonitorUI.csproj`

## ✅ Solution 3: Use Command Line to Build

Skip Visual Studio entirely and use the command line:

```bash
# Navigate to the folder
cd C:\Projects\SqlHealthCheck

# Restore packages
dotnet restore

# Build everything
dotnet build

# Run the UI
cd SqlMonitorUI
dotnet run
```

## ✅ Solution 4: Rebuild Solution File

Delete `SqlHealthCheck.sln` and recreate it:

```bash
# Navigate to the project folder
cd C:\Projects\SqlHealthCheck

# Create new solution
dotnet new sln -n SqlHealthCheck

# Add all projects
dotnet sln add SqlCheckLibrary\SqlCheckLibrary.csproj
dotnet sln add SqlCheckDemo\SqlCheckDemo.csproj
dotnet sln add SqlMonitorUI\SqlMonitorUI.csproj

# Open in Visual Studio
start SqlHealthCheck.sln
```

## ✅ Solution 5: Open Individual Projects

Instead of opening the .sln, open each project directly:

1. **File → Open → Project/Solution**
2. Navigate to `SqlMonitorUI\SqlMonitorUI.csproj`
3. Open it directly
4. Press F5 to run

Visual Studio will automatically restore dependencies from SqlCheckLibrary.

## Common Issues & Fixes

### Issue: "SDK not found" or "Target framework not found"

**Fix:** Install .NET 8 SDK
- Download from: https://dotnet.microsoft.com/download/dotnet/8.0
- Install ".NET 8.0 SDK" (not just runtime)
- Restart Visual Studio

### Issue: "The project file could not be loaded"

**Fix:** Check file permissions
1. Right-click the folder → Properties
2. Security tab → make sure your user has "Read & Execute"
3. Unblock the files if they came from a download:
   - Right-click .sln file → Properties
   - Check "Unblock" at the bottom
   - Click Apply

### Issue: "Microsoft.Data.SqlClient not found"

**Fix:** Restore NuGet packages
1. In Visual Studio: Tools → NuGet Package Manager → Package Manager Console
2. Run: `dotnet restore`
3. Or right-click solution → Restore NuGet Packages

### Issue: "The current .NET SDK does not support targeting .NET 8.0"

**Fix:** Update your .NET SDK
```bash
# Check your current version
dotnet --version

# If less than 8.0, download and install .NET 8 SDK
```

## Verify Your Setup

Run these commands to check everything is ready:

```bash
# Check .NET version (should be 8.0.x or higher)
dotnet --version

# List SDKs (should include 8.0.x)
dotnet --list-sdks

# Navigate to project folder
cd C:\Projects\SqlHealthCheck

# Build to test
dotnet build
```

Expected output:
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

## Quick Test - No Visual Studio Needed

Want to just see if it works?

```bash
cd SqlMonitorUI
dotnet run
```

This should launch the WPF app directly!

## Still Having Issues?

### Option A: Use the Console Version
The console version has fewer dependencies:

```bash
cd SqlCheckDemo
dotnet run
```

### Option B: Check File Structure
Your folder should look like this:

```
SqlHealthCheck/
├── SqlHealthCheck.sln
├── SqlCheckLibrary/
│   ├── SqlCheckLibrary.csproj
│   ├── Models/
│   │   ├── SqlCheck.cs
│   │   └── CheckResult.cs
│   └── Services/
│       ├── CheckRunner.cs
│       └── CheckRepository.cs
├── SqlCheckDemo/
│   ├── SqlCheckDemo.csproj
│   └── Program.cs
└── SqlMonitorUI/
    ├── SqlMonitorUI.csproj
    ├── App.xaml
    ├── App.xaml.cs
    ├── MainWindow.xaml
    └── MainWindow.xaml.cs
```

### Option C: Start Fresh
If all else fails:

1. Extract the files to `C:\Temp\SqlHealthCheck`
2. Open a command prompt in that folder
3. Run:
```bash
dotnet build
cd SqlMonitorUI
dotnet run
```

## Windows-Specific Issues

### Long Path Names
Windows has a 260 character path limit. If your path is too long:

1. Enable long paths:
   - Run `gpedit.msc`
   - Computer Config → Administrative Templates → System → Filesystem
   - Enable "Enable Win32 long paths"

2. Or just move to a shorter path like `C:\Dev\`

### Antivirus Blocking
Some antivirus software blocks .exe files in downloaded folders:

1. Add an exception for your dev folder
2. Or build from a different location

## Need More Help?

If none of these work, please provide:
1. Your .NET version: `dotnet --version`
2. Your Visual Studio version
3. The exact error message you see
4. Screenshot of the error would help!

## The Lazy Developer Solution 😎

Too much hassle? Just use VS Code:

1. Install VS Code
2. Install C# Dev Kit extension
3. Open the folder
4. Press F5

Done! 🎉
