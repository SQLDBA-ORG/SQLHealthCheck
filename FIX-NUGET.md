# 🔧 FIX: "Unable to find package Microsoft.Data.SqlClient"

## The Problem
Visual Studio is only looking at offline packages and can't find the Microsoft.Data.SqlClient package from NuGet.org.

## ✅ Quick Fix #1: Enable NuGet.org in Visual Studio

1. In Visual Studio, go to **Tools → Options**
2. Navigate to **NuGet Package Manager → Package Sources**
3. Make sure you see an entry like this:
   - Name: `nuget.org`
   - Source: `https://api.nuget.org/v3/index.json`
4. **Check the box** next to it to enable it
5. Click **OK**
6. Right-click your solution → **Restore NuGet Packages**

## ✅ Quick Fix #2: Use the Command Line

This bypasses Visual Studio's package sources entirely:

```bash
# Open Command Prompt or PowerShell
# Navigate to your extracted folder
cd C:\Projects\SqlHealthCheck

# Restore packages from NuGet.org
dotnet restore

# Now open in Visual Studio
start SqlHealthCheck.sln
```

## ✅ Quick Fix #3: Use the nuget.config File

I've included a `nuget.config` file in the project. Visual Studio should automatically use it.

If it doesn't work:
1. Close Visual Studio
2. Make sure `nuget.config` is in the same folder as `SqlHealthCheck.sln`
3. Open Visual Studio again
4. Right-click solution → Restore NuGet Packages

## ✅ Quick Fix #4: Manual Package Restore

```bash
# Open Command Prompt in the project folder
cd C:\Projects\SqlHealthCheck

# Clear package cache (sometimes it's corrupted)
dotnet nuget locals all --clear

# Restore packages
dotnet restore

# Build
dotnet build
```

## ✅ Quick Fix #5: Add Package Source Manually

If Tools → Options doesn't have nuget.org:

1. In Visual Studio: **Tools → Options → NuGet Package Manager → Package Sources**
2. Click the **+ (plus)** button
3. Enter:
   - **Name**: `nuget.org`
   - **Source**: `https://api.nuget.org/v3/index.json`
4. Click **Update**
5. Make sure the checkbox next to it is **checked**
6. Click **OK**
7. Right-click solution → **Restore NuGet Packages**

## 🎯 Recommended: Just Use Command Line

Honestly, the easiest way:

```bash
# Navigate to the project
cd C:\Projects\SqlHealthCheck

# Restore and build
dotnet restore
dotnet build

# Run the UI
cd SqlMonitorUI
dotnet run
```

No Visual Studio package manager issues to deal with! 🎉

## Verify It Worked

After running `dotnet restore`, you should see:

```
Determining projects to restore...
Restored C:\Projects\SqlHealthCheck\SqlCheckLibrary\SqlCheckLibrary.csproj
Restored C:\Projects\SqlHealthCheck\SqlCheckDemo\SqlCheckDemo.csproj
Restored C:\Projects\SqlHealthCheck\SqlMonitorUI\SqlMonitorUI.csproj
```

## Still Having Issues?

### Check Your Internet Connection
NuGet.org requires internet access. If you're behind a corporate firewall:

1. Ask your IT for the corporate NuGet feed URL
2. Add it as a package source in Visual Studio
3. Or ask them to whitelist `nuget.org`

### Check Proxy Settings
If you're behind a proxy:

Edit `nuget.config` to add:
```xml
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <config>
    <add key="http_proxy" value="http://your-proxy:8080" />
  </config>
</configuration>
```

### Use a Different Package
If all else fails and you can't access NuGet.org, you could:
1. Download Microsoft.Data.SqlClient manually from NuGet.org on another computer
2. Copy the `.nupkg` file to your machine
3. Add a local package source pointing to that folder

But really, `dotnet restore` from command line is your best bet!

## Quick Reference

```bash
# Clear cache and restore
dotnet nuget locals all --clear
dotnet restore

# Build everything
dotnet build

# Run UI directly
cd SqlMonitorUI
dotnet run

# Run console app
cd SqlCheckDemo
dotnet run
```

That's it! Let me know if you still have issues. 🚀
