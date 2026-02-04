# 🏢 Enterprise Roadmap for SQL Health Monitor

This document outlines the path to making SQL Health Monitor a truly enterprise-grade, charity-quality application that anyone can use and extend.

---

## ✅ Already Implemented (v26)

### Performance
- [x] **UI Virtualization** - All DataGrids and ListBoxes use `VirtualizingStackPanel.IsVirtualizing="True"`
- [x] **Async/Await** - All database operations are async (62+ usages)
- [x] **No Blocking Calls** - No `.Result` or `.Wait()` calls that block UI
- [x] **Object Pooling** - SPID boxes and brushes are pooled in LiveMonitoringWindow
- [x] **Frozen Brushes** - Static brushes are frozen for thread-safety and performance
- [x] **Connection Pooling** - Optimized SQL connection pool settings

### Memory Management
- [x] **Resource Cleanup** - Windows properly clean up on close
- [x] **Timer Disposal** - DispatcherTimers are stopped on window close
- [x] **Event Unsubscription** - Config events are unsubscribed
- [x] **Connection Pool Clearing** - SQL pools cleared on app exit
- [x] **Memory Monitoring** - Background timer monitors memory pressure

### Security
- [x] **Windows Authentication** - Full support for Integrated Security
- [x] **DPAPI Encryption** - Connection strings encrypted with Windows DPAPI
- [x] **No Password Storage** - Passwords not persisted to disk
- [x] **Connection Validation** - Security warnings for insecure settings

### Architecture (Partial)
- [x] **ObservableCollections** - Used for data binding
- [x] **ViewModels** - CheckResultViewModel, CategoryViewModel, ServerViewModel exist
- [x] **Services Layer** - CheckRunner, CheckRepository, SecurityService, etc.
- [x] **CommunityToolkit.Mvvm** - Package added for future use

---

## 🔄 Phase 1: Code Quality (Recommended Next Steps)

### 1.1 Extract ViewModels from Code-Behind
**Current State:** MainWindow.xaml.cs = 1,100 lines, LiveMonitoringWindow.xaml.cs = 2,347 lines

**Goal:** Move business logic to ViewModels, keeping code-behind under 200 lines

```
SqlMonitorUI/
├── ViewModels/
│   ├── MainWindowViewModel.cs      # Check results, filtering, running checks
│   ├── LiveMonitoringViewModel.cs  # Real-time metrics, sessions, blocking
│   ├── CheckManagerViewModel.cs    # Check editing logic
│   └── ConnectionViewModel.cs      # Server connection logic
├── Views/
│   ├── MainWindow.xaml             # Only UI, bindings to ViewModel
│   └── LiveMonitoringWindow.xaml
└── Services/                       # Already exists
```

**Benefits:**
- Testable without UI
- Multiple developers can work simultaneously
- Easier to maintain and debug

### 1.2 Use CommunityToolkit.Mvvm Attributes
Already added to csproj. Example refactor:

```csharp
// BEFORE (current)
public class CheckResultViewModel : INotifyPropertyChanged
{
    private string _checkName;
    public string CheckName 
    { 
        get => _checkName; 
        set { _checkName = value; OnPropertyChanged(); }
    }
    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

// AFTER (with toolkit)
public partial class CheckResultViewModel : ObservableObject
{
    [ObservableProperty]
    private string _checkName;
    
    [RelayCommand]
    private async Task RunCheckAsync() { /* logic */ }
}
```

### 1.3 Add XML Documentation
Add `///` documentation to all public classes and methods for IntelliSense:

```csharp
/// <summary>
/// Runs SQL Server health checks against one or more servers.
/// </summary>
/// <param name="connectionString">The SQL Server connection string</param>
/// <returns>A collection of check results</returns>
public async Task<List<CheckResult>> RunChecksAsync(string connectionString)
```

---

## 🔄 Phase 2: Testing & Quality Assurance

### 2.1 Add Unit Tests Project
```
SqlHealthMonitor.Tests/
├── Services/
│   ├── CheckRunnerTests.cs
│   ├── CheckRepositoryTests.cs
│   └── SecurityServiceTests.cs
├── ViewModels/
│   └── MainWindowViewModelTests.cs
└── Helpers/
    └── TestConnectionStrings.cs
```

### 2.2 Add Integration Tests
Test against actual SQL Server (LocalDB or Docker):
- Health check execution
- Multi-server scenarios
- Script execution (sp_Blitz, sp_triage)

### 2.3 Code Analysis
Already enabled in csproj:
```xml
<RunAnalyzers>true</RunAnalyzers>
<EnableNETAnalyzers>true</EnableNETAnalyzers>
```

Consider adding:
```xml
<AnalysisLevel>latest-recommended</AnalysisLevel>
```

---

## 🔄 Phase 3: User Experience Polish

### 3.1 Error Handling & User Feedback
- Add global exception handler
- Show friendly error messages (not stack traces)
- Log errors to file for troubleshooting

### 3.2 Accessibility (a11y)
- Ensure all controls have `AutomationProperties.Name`
- Support keyboard navigation
- Test with Windows Narrator

### 3.3 Localization Ready
- Extract all strings to resource files
- Support multiple languages (future)

### 3.4 Help & Onboarding
- Add tooltips to all buttons
- Create "Getting Started" wizard for first run
- Add F1 help integration

---

## 🔄 Phase 4: Advanced Features (Future)

### 4.1 Plugin Architecture
Allow users to add custom check modules:
```csharp
public interface ICheckModule
{
    string Name { get; }
    string Category { get; }
    Task<List<SqlCheck>> GetChecksAsync();
}
```

### 4.2 Scheduling & Automation
- Run checks on schedule
- Email alerts for critical findings
- Integration with Windows Task Scheduler

### 4.3 Historical Trending
- Store results in SQLite/SQL Server
- Show trends over time
- Compare before/after changes

### 4.4 Export Formats
- PDF reports with charts
- Excel with formatting
- Integration with Power BI

---

## 🚫 What NOT to Do (Over-Engineering)

For a charity DBA tool, these are **overkill**:

| Feature | Why Skip It |
|---------|-------------|
| **Prism Modules** | Too complex for single-developer maintenance |
| **Full DI Container** | Direct instantiation is fine for this scale |
| **OIDC/Entra ID** | DBAs already have SQL Server access |
| **RBAC** | Single-user desktop app doesn't need roles |
| **Microservices** | This is a desktop app, not a distributed system |
| **gRPC/SignalR** | No server component needed |

---

## 📊 Quality Metrics to Track

| Metric | Target | Current |
|--------|--------|---------|
| Code-behind lines per window | < 200 | ~1,700 avg |
| Unit test coverage | > 60% | 0% |
| XML documentation | > 80% | ~10% |
| Async I/O operations | 100% | ~95% |
| Memory leaks (profiler) | 0 | Unknown |
| Startup time | < 2s | ~1s ✓ |
| Check execution (10 checks) | < 5s | ~3s ✓ |

---

## 🎯 Recommended Priority Order

1. **Phase 1.1** - Extract ViewModels (biggest maintainability win)
2. **Phase 3.1** - Error handling (best user experience win)
3. **Phase 2.1** - Unit tests (prevents regressions)
4. **Phase 1.3** - XML documentation (helps contributors)
5. **Phase 3.4** - Help/tooltips (helps new users)

---

## 🤝 For Contributors

When contributing, please:
1. Follow existing code style
2. Add XML documentation to public members
3. Test manually before PR
4. Keep code-behind minimal
5. Use async/await for all I/O
6. Clean up resources (IDisposable, event handlers)

---

*This roadmap is a living document. Update as the project evolves.*
