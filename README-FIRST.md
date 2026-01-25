# 📦 SQL Health Check - Complete Package

## 🚀 Quick Start (3 Steps!)

### Step 1: Extract
Extract this ZIP file to a simple location:
- ✅ **Good**: `C:\Projects\SqlHealthCheck\`
- ✅ **Good**: `C:\Dev\SqlHealthCheck\`
- ❌ **Bad**: `C:\Users\YourName\Downloads\SqlHealthCheck (1)\`

### Step 2: Open Solution
Double-click: `SqlHealthCheck.sln`

### Step 3: Run
- In Visual Studio, right-click **SqlMonitorUI** → Set as Startup Project
- Press **F5**
- Done! 🎉

## 📁 What's Included

```
SqlHealthCheck/
├── SqlHealthCheck.sln          ← Open this in Visual Studio
├── SqlCheckLibrary/            ← Core check engine
├── SqlCheckDemo/               ← Console app
├── SqlMonitorUI/               ← WPF Dashboard (the good stuff!)
└── Documentation:
    ├── START-HERE.md           ← Command-line instructions
    ├── TROUBLESHOOTING.md      ← If Visual Studio won't load
    ├── README.md               ← Full documentation
    ├── QUICKSTART.md           ← Quick examples
    ├── USAGE.md                ← Advanced usage
    └── UI-WALKTHROUGH.md       ← UI tour
```

## ⚡ Alternative: Skip Visual Studio

Don't want to deal with Visual Studio? Just use the command line:

```bash
# Navigate to where you extracted
cd C:\Projects\SqlHealthCheck

# Run the WPF UI
cd SqlMonitorUI
dotnet run
```

## 📋 Requirements

- **Windows** (for WPF UI) or any OS (for console)
- **.NET 8 SDK**: https://dotnet.microsoft.com/download/dotnet/8.0
- **SQL Server** (to monitor)

## 🆘 Having Issues?

### "Solution won't load in Visual Studio"
→ Read **TROUBLESHOOTING.md**

### "dotnet command not found"
→ Install .NET 8 SDK from link above

### "Just want to run it quickly"
→ Read **START-HERE.md**

## 🎯 What This Does

Monitors your SQL Server with professional checks from:
- ✅ Brent Ozar's sp_blitz
- ✅ Microsoft SQL Tiger Team best practices

Features:
- 12 pre-configured health checks
- JSON-based (easy to customize)
- Beautiful WPF dashboard
- Or simple console output for automation

## 📖 Next Steps

1. **Extract** to `C:\Projects\SqlHealthCheck\`
2. **Open** `SqlHealthCheck.sln`
3. **Press F5** to run the dashboard
4. **Enter** your SQL Server connection string
5. **Click** "Run Checks"
6. **View** your SQL Server health!

## 🌳 Enjoy!

Now go plant those trees! 🌲🌳🌴

---

**Need Help?** Check the TROUBLESHOOTING.md file for solutions to common issues.
