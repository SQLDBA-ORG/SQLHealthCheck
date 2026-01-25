# 🚀 Simple Start Guide - No Visual Studio Required!

Can't get the solution to load in Visual Studio? No problem! Here's how to run everything from the command line.

## Prerequisites

1. **Install .NET 8 SDK** (if you haven't already)
   - Download: https://dotnet.microsoft.com/download/dotnet/8.0
   - Choose: ".NET 8.0 SDK" for your operating system
   - Install and restart your terminal

2. **Verify Installation**
   ```bash
   dotnet --version
   ```
   Should show: `8.0.x` or higher

## Method 1: Run the WPF UI (Recommended)

This is the visual dashboard app.

```bash
# Open Command Prompt or PowerShell
# Navigate to where you extracted the files
cd C:\Users\YourName\Downloads\SqlHealthCheck

# Navigate to the UI project
cd SqlMonitorUI

# Run it!
dotnet run
```

**That's it!** The app will launch and you'll see the monitoring dashboard.

## Method 2: Run the Console Demo

This is the text-based version for scripting.

```bash
# Navigate to the demo project
cd SqlCheckDemo

# Run with default connection
dotnet run

# Or run with a custom connection string
dotnet run "Server=YOUR_SERVER;Database=master;Integrated Security=true;TrustServerCertificate=true;"
```

## Method 3: Build Everything First

If you want to build once and run the .exe directly:

```bash
# Navigate to the main folder
cd C:\Users\YourName\Downloads\SqlHealthCheck

# Build everything
dotnet build

# Run the UI from the compiled output
.\SqlMonitorUI\bin\Debug\net8.0-windows\SqlMonitorUI.exe

# Or run the console app
.\SqlCheckDemo\bin\Debug\net8.0\SqlCheckDemo.exe
```

## Troubleshooting

### Error: "dotnet: command not found"
- Install .NET 8 SDK (link above)
- Restart your terminal after installing

### Error: "Could not find project"
- Make sure you're in the correct folder
- Run `dir` (Windows) or `ls` (Mac/Linux) to see files
- You should see folders like SqlMonitorUI, SqlCheckDemo, etc.

### Error: "The current .NET SDK does not support targeting .NET 8.0"
- Your .NET SDK is too old
- Install .NET 8 SDK (link above)

### Error: "Microsoft.Data.SqlClient not found"
- This is normal on first run
- Run `dotnet restore` first
- Then try `dotnet run` again

## Quick Reference

```bash
# Restore packages (run once after download)
dotnet restore

# Build all projects
dotnet build

# Run WPF UI
cd SqlMonitorUI
dotnet run

# Run Console
cd SqlCheckDemo  
dotnet run

# Clean build artifacts
dotnet clean
```

## For Developers

Want to modify the code?

**Option 1: VS Code** (Lightweight)
1. Install VS Code: https://code.visualstudio.com/
2. Install "C# Dev Kit" extension
3. Open the SqlHealthCheck folder
4. Press F5 to run

**Option 2: Visual Studio 2022** (Full IDE)
1. See TROUBLESHOOTING.md for solutions
2. Or just use `dotnet run` - it's simpler!

**Option 3: JetBrains Rider**
1. Open the SqlHealthCheck folder
2. It should auto-detect all projects
3. Press F5 to run

## Next Steps

1. **Run the app** using one of the methods above
2. **Enter your SQL Server connection string** in the UI
3. **Click "Run Checks"** to see your SQL Server health
4. **Customize checks** by editing the `sql-checks.json` file that gets created

## Need Help?

See **TROUBLESHOOTING.md** for detailed solutions to common problems.

Happy monitoring! 🎉
