using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Sockets;
using System.Numerics;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security.Policy;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
//using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using static System.Net.Mime.MediaTypeNames;
//using static System.Windows.Forms.AxHost;
//using static System.Windows.Forms.VisualStyles.VisualStyleElement;
//using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace SqlMonitorUI
{
    public class CircularBuffer<T>
    {
        private readonly T[] _buffer;
        private int _head = 0;
        private int _count = 0;
        private readonly int _capacity;

        public CircularBuffer(int capacity)
        {
            _capacity = capacity;
            _buffer = new T[capacity];
        }

        public void Enqueue(T item)
        {
            _buffer[_head] = item;
            _head = (_head + 1) % _capacity;
            if (_count < _capacity) _count++;
        }

        public T Dequeue()
        {
            var result = _buffer[(_head - _count + _capacity) % _capacity];
            _count--;
            return result;
        }

        public int Count => _count;
    }
    public partial class LiveMonitoringWindow : Window
    {
        private readonly List<string> _connectionStrings;
        private string? _selectedConnectionString;
        private DispatcherTimer? _refreshTimer;
        private bool _isRunning;
        private long _lastBatchReq, _lastTrans, _lastComp, _lastReads, _lastWrites, _lastPoisonWaits, _lastPoisonWaitSerializable, _lastPoisonWaitCMEM;
        private DateTime _lastSampleTime = DateTime.MinValue;
        private int _tickCount = 0;
        private Boolean ShowSleepingSPIDs = true;
        private string _programFilter = "";
        private int TopSessions = 15;

        // Configuration - loaded from LiveMonitoring.config
        private readonly LiveMonitoringConfig _config;

        private const int MaxHistoryPoints = 120;
        private readonly Dictionary<string, Queue<long>> _waitHistory = new(); // Changed to long for values
        private readonly Dictionary<string, System.Windows.Media.Color> _waitColors = new()
        {
            { "Locks",  System.Windows.Media.Color.FromRgb(220, 20, 60) },
            { "Reads/Latches", System.Windows.Media.Color.FromRgb(0, 120, 212) },
            { "Writes/I/O",  System.Windows.Media.Color.FromRgb(139, 0, 139) },
            { "Network",  System.Windows.Media.Color.FromRgb(255, 140, 0) },
            { "Backup",  System.Windows.Media.Color.FromRgb(128, 128, 128) },
            { "Memory",  System.Windows.Media.Color.FromRgb(16, 124, 16) },
            { "Parallelism",  System.Windows.Media.Color.FromRgb(107, 105, 214) },
            { "Transaction Log",  System.Windows.Media.Color.FromRgb(216, 59, 1) },
            { "PoisonWaits",  System.Windows.Media.Color.FromRgb(255, 0, 0) },
            { "Poison Serializable Locking",  System.Windows.Media.Color.FromRgb(255, 50, 0) },
            { "Poison CMEMTHREAD and NUMA",  System.Windows.Media.Color.FromRgb(255, 50, 50) }

        };

        // ========== PERFORMANCE OPTIMIZATION: Object Pooling ==========
        // Pre-cached brushes to avoid creating new SolidColorBrush objects every frame
        private static readonly Dictionary<System.Windows.Media.Color, SolidColorBrush> _brushCache = new();
        private static SolidColorBrush GetCachedBrush(System.Windows.Media.Color color)
        {
            if (!_brushCache.TryGetValue(color, out var brush))
            {
                brush = new SolidColorBrush(color);
                brush.Freeze(); // Frozen brushes are thread-safe and more performant
                _brushCache[color] = brush;
            }
            return brush;
        }

        // Pre-cached common brushes
        private static readonly SolidColorBrush _redBrush = CreateFrozenBrush(System.Windows.Media.Colors.Red);
        private static readonly SolidColorBrush _blackBrush = CreateFrozenBrush(System.Windows.Media.Colors.Black);
        private static readonly SolidColorBrush _grayBrush = CreateFrozenBrush(System.Windows.Media.Colors.Gray);
        private static readonly SolidColorBrush _whiteBrush = CreateFrozenBrush(System.Windows.Media.Colors.White);
        private static readonly SolidColorBrush _blueBrush = CreateFrozenBrush(System.Windows.Media.Color.FromRgb(0, 120, 212));
        private static readonly SolidColorBrush _orangeBrush = CreateFrozenBrush(System.Windows.Media.Color.FromRgb(216, 59, 1));

        private static SolidColorBrush CreateFrozenBrush(System.Windows.Media.Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        // SPID Box object pool - reuse Border controls instead of recreating
        private readonly Queue<Border> _spidBoxPool = new();
        private readonly List<Border> _activeSpidBoxes = new();
        private const int MaxPoolSize = 200;

        // ========== PERFORMANCE OPTIMIZATION: Connection Pooling ==========
        // Use optimized connection string with explicit pool settings
        private string? _optimizedConnectionString;
        private string GetOptimizedConnectionString()
        {
            if (_optimizedConnectionString == null && !string.IsNullOrEmpty(_selectedConnectionString))
            {
                var builder = new SqlConnectionStringBuilder(_selectedConnectionString)
                {
                    Pooling = true,
                    MinPoolSize = 2,
                    MaxPoolSize = 10,
                    ConnectTimeout = 5,
                    CommandTimeout = 15,
                    ApplicationName = "SQLMonitorUI_LiveMonitor"
                };
                _optimizedConnectionString = builder.ConnectionString;
            }
            return _optimizedConnectionString ?? _selectedConnectionString ?? "";
        }

        // ========== PERFORMANCE OPTIMIZATION: Cancellation Support ==========
        private CancellationTokenSource? _refreshCts;

        // ========== PERFORMANCE OPTIMIZATION: Rate Limiting ==========
        private DateTime _lastRefreshComplete = DateTime.MinValue;
        private bool _refreshInProgress = false;
        private readonly object _refreshLock = new object();

        // Baseline wait stats for delta calculation
        private Dictionary<string, long>? _baselineWaits;
        private Dictionary<string, long> _lastWaits = new();

        private readonly Dictionary<int, SpidHistory> _spidHistories = new();
        private const int SparklinePoints = 30;

        // Current blocking information
        private List<BlockingInfo> _currentBlocking = new();

        // Store SPID box positions for drawing blocking lines
        private readonly Dictionary<int, System.Drawing.Point> _spidBoxPositions = new();
        private readonly Dictionary<int, Border> _spidBoxBorders = new();

        public LiveMonitoringWindow(List<string> connectionStrings)
        {
            InitializeComponent();
            _connectionStrings = connectionStrings;
            foreach (var key in _waitColors.Keys) _waitHistory[key] = new Queue<long>();
            BuildLegend();

            // Load configuration
            _config = LiveMonitoringConfig.Instance;
            _config.ConfigReloaded += Config_ConfigReloaded;

            // Test connections and populate server dropdown
            Loaded += async (s, e) => await TestAndPopulateServersAsync();
        }

        private void Config_ConfigReloaded(object? sender, EventArgs e)
        {
            // Config file changed - update on UI thread
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = "Configuration reloaded - queries updated";

                // Update timer interval if changed
                if (_refreshTimer != null && _refreshTimer.Interval.TotalMilliseconds != _config.RefreshIntervalMs)
                {
                    _refreshTimer.Interval = TimeSpan.FromMilliseconds(_config.RefreshIntervalMs);
                }
            });
        }

        private async System.Threading.Tasks.Task TestAndPopulateServersAsync()
        {
            StatusText.Text = "Testing server connections...";
            var serverItems = new List<ServerItem>();

            foreach (var connStr in _connectionStrings)
            {
                string serverName;
                try { serverName = new SqlConnectionStringBuilder(connStr).DataSource; }
                catch { serverName = "Unknown"; }

                var item = new ServerItem
                {
                    ConnectionString = connStr,
                    ServerName = serverName,
                    DisplayName = serverName + " (testing...)",
                    IsConnectable = false,
                    TextColor = System.Windows.Media.Brushes.Gray
                };
                serverItems.Add(item);
            }

            ServerCombo.ItemsSource = serverItems;

            // Test each connection asynchronously
            foreach (var item in serverItems)
            {
                var isConnectable = await TestConnectionAsync(item.ConnectionString);
                item.IsConnectable = isConnectable;
                item.DisplayName = isConnectable ? item.ServerName : item.ServerName + " (offline)";
                item.TextColor = isConnectable ? System.Windows.Media.Brushes.Black : System.Windows.Media.Brushes.Gray;
            }

            // Refresh the combo box
            ServerCombo.Items.Refresh();

            // Auto-select first connectable server (this will trigger ServerCombo_SelectionChanged which auto-starts monitoring)
            var firstConnectable = serverItems.FirstOrDefault(s => s.IsConnectable);
            if (firstConnectable != null)
            {
                ServerCombo.SelectedItem = firstConnectable;
                // Monitoring starts automatically via ServerCombo_SelectionChanged
            }
            else
            {
                StatusText.Text = "No servers available. Check connections.";
            }
        }

        private async System.Threading.Tasks.Task<bool> TestConnectionAsync(string connectionString)
        {
            return await System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var builder = new SqlConnectionStringBuilder(connectionString)
                    {
                        ConnectTimeout = 5
                    };
                    using var conn = new SqlConnection(builder.ConnectionString);
                    conn.Open();
                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        private void ServerCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ServerCombo.SelectedItem is ServerItem selectedServer)
            {
                if (!selectedServer.IsConnectable)
                {
                    ConnectionStatusText.Text = "Server offline";
                    StartStopButton.IsEnabled = false;
                    return;
                }

                // Stop current monitoring if running
                if (_isRunning) StopMonitoring();

                // Reset all state
                ResetMonitoringState();

                _selectedConnectionString = selectedServer.ConnectionString;
                _optimizedConnectionString = null; // Reset so it gets rebuilt with new connection string
                ConnectionStatusText.Text = "Connected";
                StartStopButton.IsEnabled = true;

                // Auto-start monitoring
                StartMonitoring();
            }
        }

        private void ResetMonitoringState()
        {
            _baselineWaits = null;
            _lastWaits.Clear();
            _lastBatchReq = 0;
            _lastTrans = 0;
            _lastComp = 0;
            _lastReads = 0;
            _lastWrites = 0;
            _lastPoisonWaits = 0;
            _lastPoisonWaitSerializable = 0;
            _lastPoisonWaitCMEM = 0;
            _lastSampleTime = DateTime.MinValue;
            _tickCount = 0;
            _spidHistories.Clear();
            _currentBlocking.Clear();
            foreach (var key in _waitColors.Keys)
            {
                _waitHistory[key].Clear();
            }

            // Clear object pools and caches
            _graphPolylines.Clear();
            _spidBoxPool.Clear();
            _activeSpidBoxes.Clear();
            _blockingLinePool.Clear();
            _activeBlockingLines.Clear();

            // Reset UI
            CpuText.Text = "--";
            BatchReqText.Text = "--";
            TransText.Text = "--";
            CompilationsText.Text = "--";
            ReadsText.Text = "--";
            WritesText.Text = "--";
            SessionsGrid.ItemsSource = null;
            WaitStatsGrid.ItemsSource = null;
            BlockingGrid.ItemsSource = null;
            TopQueriesGrid.ItemsSource = null;
            SpidBoxesCanvas.Children.Clear();
            WaitGraphCanvas.Children.Clear();
            YAxisCanvas.Children.Clear();
        }

        private void BuildLegend()
        {
            LegendPanel.Children.Clear();
            foreach (var kvp in _waitColors)
            {
                var sp = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 15, 5) };
                sp.Children.Add(new System.Windows.Shapes.Rectangle { Width = 14, Height = 14, Fill = new System.Windows.Media.SolidColorBrush(kvp.Value), Margin = new Thickness(0, 0, 5, 0), RadiusX = 2, RadiusY = 2 });
                sp.Children.Add(new TextBlock { Text = kvp.Key, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
                LegendPanel.Children.Add(sp);
            }
        }

        private void StartStopButton_Click(object sender, RoutedEventArgs e) { if (_isRunning) StopMonitoring(); else StartMonitoring(); }

        private void StartMonitoring()
        {
            if (string.IsNullOrEmpty(_selectedConnectionString)) return;

            _isRunning = true;
            StartStopButton.Content = "■ Stop";
            StartStopButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(216, 59, 1));
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(GetRefreshInterval()) };
            _refreshTimer.Tick += async (s, e) => await RefreshDataAsync();
            _refreshTimer.Start();
            _ = RefreshDataAsync();
            StatusText.Text = "Monitoring...";
            OptimizeDataGrid();
        }
        // In your code-behind, optimize DataGrid performance
        private void OptimizeDataGrid()
        {
            // Disable virtualization for better performance with smaller datasets
            BlockingGrid.EnableRowVirtualization = true;
            BlockingGrid.EnableColumnVirtualization = true;

            // Set appropriate row height
            BlockingGrid.RowHeight = 26;

            // Use DataGridTextColumn with proper formatting
            foreach (DataGridTextColumn column in BlockingGrid.Columns)
            {
                if (column.Width == DataGridLength.Auto)
                    column.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
            }
        }

        private void StopMonitoring()
        {
            _isRunning = false;
            _refreshCts?.Cancel(); // Cancel any pending refresh operations
            StartStopButton.Content = "▶ Start";
            StartStopButton.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 124, 16));
            _refreshTimer?.Stop(); _refreshTimer = null;
            StatusText.Text = "Stopped";
        }

        private int GetRefreshInterval() => RefreshIntervalCombo.SelectedIndex switch { 0 => 1, 1 => 5, 2 => 10, 3 => 30, _ => 5 };

        private void RefreshIntervalCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_refreshTimer != null)
                _refreshTimer.Interval = TimeSpan.FromSeconds(GetRefreshInterval());
        }

        private async System.Threading.Tasks.Task RefreshDataAsync()
        {
            if (string.IsNullOrEmpty(_selectedConnectionString)) return;

            // Rate limiting - skip if previous refresh still running
            lock (_refreshLock)
            {
                if (_refreshInProgress) return;
                _refreshInProgress = true;
            }

            try
            {
                _tickCount++;
                var shouldRefreshTopQueries = _config.Queries.TopQueries.Enabled &&
                    (_tickCount == 1 || _tickCount % _config.TopQueriesRefreshInterval == 0);
                var shouldRefreshServerDetails = _config.Queries.ServerDetails.Enabled &&
                    (_tickCount == 1 || _tickCount % _config.ServerDetailsRefreshInterval == 0);
                var shouldRefreshDriveLatency = _config.Queries.DriveLatency.Enabled &&
                    (_tickCount == 1 || _tickCount % _config.DriveLatencyRefreshInterval == 0);

                var connStr = GetOptimizedConnectionString();

                // Run all queries in parallel
                var metricsTask = _config.Queries.Metrics.Enabled
                    ? Task.Run(() => GetMetricsOptimized(connStr))
                    : Task.FromResult(new List<MetricsItem>());
                var sessionsTask = _config.Queries.Sessions.Enabled
                    ? Task.Run(() => GetSessionsOptimized(connStr))
                    : Task.FromResult(new List<SessionItem>());
                var blockingTask = _config.Queries.Blocking.Enabled
                    ? Task.Run(() => GetBlockingInfoOptimized(connStr))
                    : Task.FromResult(new List<BlockingInfo>());

                Task<List<TopQueryItem>>? topQueriesTask = shouldRefreshTopQueries
                    ? Task.Run(() => GetTopQueriesOptimized(connStr))
                    : null;
                Task<List<DriveLatencyItem>>? driveLatencyTask = shouldRefreshDriveLatency
                    ? Task.Run(() => GetDriveLatencyOptimized(connStr))
                    : null;
                Task<List<ServerDetailsItem>>? serverDetailsTask = shouldRefreshServerDetails
                    ? Task.Run(() => GetServerDetailsOptimized(connStr))
                    : null;

                // Wait for core queries
                await Task.WhenAll(metricsTask, sessionsTask, blockingTask);

                var metrics = await metricsTask;
                var sessions = await sessionsTask;
                var blocking = await blockingTask;

                // Wait for optional queries if they were started
                List<TopQueryItem>? topQueries = null;
                List<DriveLatencyItem>? driveLatency = null;
                List<ServerDetailsItem>? serverDetails = null;

                if (topQueriesTask != null)
                {
                    topQueries = await topQueriesTask;
                }
                if (driveLatencyTask != null)
                {
                    driveLatency = await driveLatencyTask;
                }
                if (serverDetailsTask != null)
                {
                    serverDetails = await serverDetailsTask;
                }

                // Update UI on dispatcher thread
                await Dispatcher.InvokeAsync(() =>
                {
                    UpdateMetrics(metrics);
                    UpdateSessions(sessions);
                    UpdateBlocking(blocking);

                    if (topQueries != null && topQueries.Count > 0)
                    {
                        UpdateTopQueries(topQueries);
                    }
                    if (driveLatency != null)
                        UpdateDriveLatency(driveLatency);
                    if (serverDetails != null)
                        UpdateGetServerDetails(serverDetails);

                    DrawWaitGraphOptimized();
                    UpdateSpidBoxesOptimized();

                    LastUpdateText.Text = $"Updated: {DateTime.Now:HH:mm:ss} (tick {_tickCount})";
                }, System.Windows.Threading.DispatcherPriority.Background);

                // Gentle GC only every 120 ticks
                if (_tickCount % 120 == 0)
                {
                    GC.Collect(0, GCCollectionMode.Optimized, false);
                }
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() => { StatusText.Text = $"Error: {ex.Message}"; });
            }
            finally
            {
                lock (_refreshLock)
                {
                    _refreshInProgress = false;
                }
            }
        }

        // ========== PERFORMANCE: Optimized connection creation ==========
        private SqlConnection CreateConnection()
        {
            var conn = new SqlConnection(GetOptimizedConnectionString());
            conn.Open();
            return conn;
        }

        // ========== Optimized query methods ==========
        private List<MetricsItem> GetMetricsOptimized(string connStr)
        {
            using var conn = new SqlConnection(connStr);
            conn.Open();
            return GetMetricsInternal(conn);
        }

        private List<SessionItem> GetSessionsOptimized(string connStr)
        {
            using var conn = new SqlConnection(connStr);
            conn.Open();
            return GetSessionsInternal(conn);
        }

        private List<BlockingInfo> GetBlockingInfoOptimized(string connStr)
        {
            using var conn = new SqlConnection(connStr);
            conn.Open();
            return GetBlockingInfoInternal(conn);
        }

        private List<TopQueryItem> GetTopQueriesOptimized(string connStr)
        {
            using var conn = new SqlConnection(connStr);
            conn.Open();
            return GetTopQueriesInternal(conn);
        }

        private List<DriveLatencyItem> GetDriveLatencyOptimized(string connStr)
        {
            using var conn = new SqlConnection(connStr);
            conn.Open();
            return GetDriveLatencyInternal(conn);
        }

        private List<ServerDetailsItem> GetServerDetailsOptimized(string connStr)
        {
            using var conn = new SqlConnection(connStr);
            conn.Open();
            return GetServerDetailsInternal(conn);
        }

        private List<MetricsItem> GetMetricsInternal(SqlConnection conn)
        {
            var results = new List<MetricsItem>();
            var q = _config.Queries.Metrics.Sql;
            using var cmd = new SqlCommand(q, conn) { CommandTimeout = _config.CommandTimeoutSeconds };
            try
            {
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    results.Add(new MetricsItem
                    {
                        CPU = reader["CPU"] != DBNull.Value ? Convert.ToInt32(reader["CPU"]) : 0,
                        BatchReq = reader["BatchReq"] != DBNull.Value ? Convert.ToInt64(reader["BatchReq"]) : 0,
                        Trans = reader["Trans"] != DBNull.Value ? Convert.ToInt64(reader["Trans"]) : 0,
                        SqlComp = reader["SqlComp"] != DBNull.Value ? Convert.ToInt64(reader["SqlComp"]) : 0,
                        PhReads = reader["PhReads"] != DBNull.Value ? Convert.ToInt64(reader["PhReads"]) : 0,
                        PhWrites = reader["PhWrites"] != DBNull.Value ? Convert.ToInt64(reader["PhWrites"]) : 0,
                        Locks = reader["Locks"] != DBNull.Value ? Convert.ToInt64(reader["Locks"]) : 0,
                        Reads = reader["Reads"] != DBNull.Value ? Convert.ToInt64(reader["Reads"]) : 0,
                        Writes = reader["Writes"] != DBNull.Value ? Convert.ToInt64(reader["Writes"]) : 0,
                        Network = reader["Network"] != DBNull.Value ? Convert.ToInt64(reader["Network"]) : 0,
                        Backup = reader["Backup"] != DBNull.Value ? Convert.ToInt64(reader["Backup"]) : 0,
                        Memory = reader["Memory"] != DBNull.Value ? Convert.ToInt64(reader["Memory"]) : 0,
                        Parallelism = reader["Parallelism"] != DBNull.Value ? Convert.ToInt64(reader["Parallelism"]) : 0,
                        TransactionLog = reader["TransactionLog"] != DBNull.Value ? Convert.ToInt64(reader["TransactionLog"]) : 0,
                        PoisonWaits = reader["PoisonWaits"] != DBNull.Value ? Convert.ToInt64(reader["PoisonWaits"]) : 0,
                        PoisonSerializableLocking = reader["Poison Serializable Locking"] != DBNull.Value ? Convert.ToInt64(reader["Poison Serializable Locking"]) : 0,
                        PoisonCMEMTHREAD = reader["Poison CMEMTHREAD and NUMA"] != DBNull.Value ? Convert.ToInt64(reader["Poison CMEMTHREAD and NUMA"]) : 0
                    });
                }
            }
            catch { /* Ignore empty metrics */ }
            return results;
        }


        private List<SessionItem> GetSessionsInternal(SqlConnection conn)
        {
            // Use query from config with placeholders replaced
            var q = _config.GetSessionsSql(
                TopSessions,
                ShowSleepingSPIDs,
                _programFilter);

            var sess = new List<SessionItem>();
            var timeout = _config.Queries.Sessions.TimeoutSeconds;

            using (var command = new SqlCommand(q, conn) { CommandTimeout = timeout })
            {
                try
                {
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var cpu = reader["Cpu"] != DBNull.Value ? Convert.ToInt32(reader["Cpu"]) : 0;
                            var io = reader["PhysicalIo"] != DBNull.Value ? Convert.ToInt32(reader["PhysicalIo"]) : 0;

                            sess.Add(new SessionItem
                            {
                                Spid = Convert.ToInt32(reader["Spid"]),
                                Database = reader["Database"]?.ToString() ?? "",
                                Status = reader["Status"]?.ToString() ?? "",
                                Cpu = cpu,
                                PhysicalIo = io,
                                Hostname = reader["Hostname"]?.ToString() ?? "",
                                ProgramName = reader["ProgramName"]?.ToString() ?? "",
                                LoginName = reader["LoginName"]?.ToString() ?? "",
                                Command = reader["Command"]?.ToString() ?? "",
                                text = reader["text"]?.ToString() ?? "",
                                Blocked = reader["blocked"]?.ToString() ?? "",
                                IdleSeconds = reader["IdleSeconds"] != DBNull.Value ? Convert.ToInt32(reader["IdleSeconds"]) : 0
                            });
                        }
                    }
                }
                catch { /* Ignore query errors */ }
            }

            return sess;
        }

        private List<DriveLatencyItem> GetDriveLatencyInternal(SqlConnection conn)
        {


            // Use query from config
            var q = _config.Queries.DriveLatency.Sql;
            var timeout = _config.Queries.DriveLatency.TimeoutSeconds;

            var results = new List<DriveLatencyItem>();
            using var cmd = new SqlCommand(q, conn) { CommandTimeout = timeout };

            try
            {
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    results.Add(new DriveLatencyItem
                    {
                        Drive = reader["Drive"]?.ToString() ?? "",
                        LatencyMs = reader["Latency(ms)"] != DBNull.Value ? Convert.ToInt32(reader["Latency(ms)"]) : 0,
                        GBPerDay = 0,
                        FreeSpace = "",
                        ReadLatencyMs = reader["ReadLatency(ms)"] != DBNull.Value ? Convert.ToInt32(reader["ReadLatency(ms)"]) : 0,
                        WriteLatencyMs = reader["WriteLatency(ms)"] != DBNull.Value ? Convert.ToInt32(reader["WriteLatency(ms)"]) : 0
                    });
                }
            }
            catch { /* Ignore query errors */ }
            return results;
        }

        private List<ServerDetailsItem> GetServerDetailsInternal(SqlConnection conn)
        {

            // Use query from config
            var q = _config.Queries.ServerDetails.Sql;
            var timeout = _config.Queries.ServerDetails.TimeoutSeconds;

            var results = new List<ServerDetailsItem>();
            using var cmd = new SqlCommand(q, conn) { CommandTimeout = timeout };


            try
            {
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    results.Add(new ServerDetailsItem
                    {
                        ServerName = reader["ServerName"]?.ToString() ?? "",
                        Edition = reader["Edition"]?.ToString() ?? "",
                        Sockets = reader["Sockets"] != DBNull.Value ? Convert.ToInt32(reader["Sockets"]) : 0,
                        VirtualCPUs = reader["VirtualCPUs"] != DBNull.Value ? Convert.ToInt32(reader["VirtualCPUs"]) : 0,
                        VMType = reader["VMType"]?.ToString() ?? "",
                        MemoryGB = reader["MemoryGB"] != DBNull.Value ? Convert.ToDouble(reader["MemoryGB"]) : 0,
                        SqlAllocated = reader["SqlAllocated"] != DBNull.Value ? Convert.ToDouble(reader["SqlAllocated"]) : 0,
                        UsedBySql = reader["UsedBySql"] != DBNull.Value ? Convert.ToDouble(reader["UsedBySql"]) : 0,
                        MemoryState = reader["MemoryState"]?.ToString() ?? "",
                        Version = reader["Version"]?.ToString()?.Split('\n')[0]?.Trim() ?? "",
                        BuildNr = reader["BuildNr"]?.ToString() ?? "",
                        OS = reader["OS"]?.ToString() ?? "",
                        HADR = reader["HADR"]?.ToString() == "Yes" ? 1 : 0,
                        SA = reader["SA"]?.ToString() == "Enabled" ? 1 : 0,
                        Level = reader["Level"]?.ToString() ?? ""
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ServerDetails query error: {ex.Message}");
            }
            return results;
        }

        private List<BlockingInfo> GetBlockingInfoInternal(SqlConnection conn)
        {
            // Use query from config
            var q = _config.Queries.Blocking.Sql;
            var timeout = _config.Queries.Blocking.TimeoutSeconds;

            var results = new List<BlockingInfo>();
            using var cmd = new SqlCommand(q, conn) { CommandTimeout = timeout };

            try
            {
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    results.Add(new BlockingInfo
                    {
                        LockType = reader["LockType"]?.ToString() ?? "",
                        DatabaseName = reader["DatabaseName"]?.ToString() ?? "",
                        WaitSpid = reader["WaitSpid"] != DBNull.Value ? Convert.ToInt32(reader["WaitSpid"]) : 0,
                        BlockerSpid = reader["BlockerSpid"] != DBNull.Value ? Convert.ToInt32(reader["BlockerSpid"]) : 0,
                        WaitDurationMs = reader["WaitDurationMs"] != DBNull.Value ? Convert.ToInt64(reader["WaitDurationMs"]) : 0,
                        WaitType = reader["WaitType"]?.ToString() ?? "",
                        WaitStatement = reader["WaitStatement"]?.ToString() ?? "",
                        BlockerStatement = reader["BlockerStatement"]?.ToString() ?? ""
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Blocking query error: {ex.Message}");
            }
            return results;
        }

        private List<TopQueryItem> GetTopQueriesInternal(SqlConnection conn)
        {
            // Use query from config
            var q = _config.Queries.TopQueries.Sql;
            var timeout = _config.Queries.TopQueries.TimeoutSeconds;

            var results = new List<TopQueryItem>();
            using var cmd = new SqlCommand(q, conn) { CommandTimeout = timeout };
            try
            {
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    results.Add(new TopQueryItem
                    {
                        Category = reader["Category"]?.ToString() ?? "",
                        Database = reader["DB"]?.ToString() ?? "",
                        TotalElapsedTimeS = reader["Total Elapsed Time in S"] != DBNull.Value ? Convert.ToDecimal(reader["Total Elapsed Time in S"]) : 0,
                        ExecutionCount = reader["Total Execution Count"] != DBNull.Value ? Convert.ToInt64(reader["Total Execution Count"]) : 0,
                        TotalCpuTimeS = reader["Total CPU Time in S"] != DBNull.Value ? Convert.ToDecimal(reader["Total CPU Time in S"]) : 0,
                        TotalLogicalReads = reader["Total Logical Reads"] != DBNull.Value ? Convert.ToInt64(reader["Total Logical Reads"]) : 0,
                        TotalLogicalWrites = reader["Total Logical Writes"] != DBNull.Value ? Convert.ToInt64(reader["Total Logical Writes"]) : 0,
                        QueryText = reader["Query"]?.ToString()?.Replace("\r", " ").Replace("\n", " ").Trim() ?? "",
                        PlanHandle = reader["Plan Handle"] != DBNull.Value ? (byte[])reader["Plan Handle"] : null,
                        PlanXml = "" // Fetch on-demand via Save Plan button to avoid performance hit
                    });
                }
            }
            catch { /* Ignore query errors */ }
            return results;
        }

        private void UpdateDriveLatency(List<DriveLatencyItem> items)
        {
            if (items.Count == 0) return;
            var r = items[0];
            var drv = r.Drive;
            var lat = r.LatencyMs;
            var wl = r.GBPerDay;
            var fs = r.FreeSpace;
            var rlat = r.ReadLatencyMs;
            var wlat = r.WriteLatencyMs;
            // C:\	2   6.5883  965.21GB    5   0
        }
        private void UpdateGetServerDetails(List<ServerDetailsItem> items)
        {
            if (items.Count == 0) return;
            var r = items[0];
            var sn = r.ServerName;
            var ed = r.Edition;
            var sckt = r.Sockets;
            var cpus = r.VirtualCPUs;
            var vm = r.VMType;
            var ram = r.MemoryGB;
            var all = r.SqlAllocated;
            var usd = r.UsedBySql;
            var ms = r.MemoryState;
            var vs = r.Version;
            var bld = r.BuildNr;
            var os = r.OS;
            var ha = r.HADR;
            var sa = r.SA;
            var lvl = r.Level;

            var licensingRetailUSD = 5434;  //$5,434/year ;
            var licensingRetailUSDServer = 0.00;
            //MSI Developer Edition(64 - bit)	1   22(Hypervisor)    32.25   0.55    0.5489  Available physical memory is high MSI Microsoft SQL Server 2022(RT   16.0.4230.2 Windows 10 Home 10.0 < X64 > (Bu  Developer Edition(64 - bit)  0   1   RTM
            //ed = "Standard";
            //0.ToString("N0");
            ServerNameText.Text = sn;
            EditionText.Text = ed;
            SocketsText.Text = sckt.ToString() + " : " + cpus.ToString();

            // Developer
            //Express
            //Web
            licensingRetailUSDServer = 0;

            switch (ed)
            {
                case "Business Intelligence":
                    licensingRetailUSDServer = Convert.ToDouble(licensingRetailUSD) * cpus / 2;
                    break;
                case "Standard":
                    licensingRetailUSDServer = Convert.ToDouble(licensingRetailUSD) * cpus / 2 / 3.832;
                    break;
                case "Enterprise":
                    licensingRetailUSDServer = Convert.ToDouble(licensingRetailUSD) * cpus / 2;
                    break;
                default:
                    licensingRetailUSDServer = 0;
                    break;
            }



            //switch (ed)
            //{
            //    case "Enterprise":
            //        licensingRetailUSDServer = licensingRetailUSD * cpus / 2;
            //        break;
            //    case "Standard":
            //        licensingRetailUSDServer = Convert.ToDouble(licensingRetailUSD) * cpus / 2 / 3.832;
            //        break;
            //    default:
            //        licensingRetailUSDServer = 0;
            //        break;
            //}
            LicensingText.Text = licensingRetailUSDServer.ToString("C0");


            MemoryGBText.Text = ram.ToString("N2");
            UsedbySQLGBText.Text = ((all / ram)).ToString("P2");
            VersionText.Text = vs.Replace("Microsoft SQL Server", "");
            BuildNrText.Text = bld;

            if (ha >= 1)
            {
                HADRText.Text = "Yes";
            }
            else
            {
                HADRText.Text = "No";
            }

        }
        private void UpdateMetrics(List<MetricsItem> items)
        {

            if (items.Count == 0) return;
            var r = items[0];


            var cpu = r.CPU;
            var now = DateTime.Now;
            var br = r.BatchReq;
            var tr = r.Trans;
            var sc = r.SqlComp;
            var reads = r.PhReads;
            var writes = r.PhWrites;



            var lcks = r.Locks;
            var rds = r.Reads;
            var wrts = r.Writes;
            var nw = r.Network;
            var bkp = r.Backup;
            var mm = r.Memory;
            var cx = r.Parallelism;
            var tlog = r.TransactionLog;
            var pw = r.PoisonWaits;
            var pws = r.PoisonSerializableLocking;
            var pwn = r.PoisonCMEMTHREAD;


            CpuText.Text = cpu.ToString();
            CpuText.Foreground = new SolidColorBrush(cpu > 80 ? System.Windows.Media.Color.FromRgb(216, 59, 1) : System.Windows.Media.Color.FromRgb(0, 120, 212));


            if (_lastSampleTime != DateTime.MinValue)
            {
                var el = (now - _lastSampleTime).TotalSeconds;
                if (el > 0)
                {
                    if (((br - _lastBatchReq) / el) <= 0)
                    {
                        BatchReqText.Text = 0.ToString("N0");
                    }
                    else
                        BatchReqText.Text = ((br - _lastBatchReq) / el).ToString("N0");
                    if (((tr - _lastTrans) / el) <= 0)
                    {
                        TransText.Text = 0.ToString("N0");
                    }
                    else
                        TransText.Text = ((tr - _lastTrans) / el).ToString("N0");

                    if (((sc - _lastComp) / el) <= 0)
                    {
                        CompilationsText.Text = 0.ToString("N0");
                    }
                    else
                        CompilationsText.Text = ((sc - _lastComp) / el).ToString("N0");
                    // Page Reads and Writes as deltas per second
                    if (((reads - _lastReads) / el) <= 0)
                    {
                        ReadsText.Text = 0.ToString("N0");
                    }
                    else
                        ReadsText.Text = ((reads - _lastReads) / el).ToString("N0");
                    if (((writes - _lastWrites) / el) <= 0)
                    {
                        WritesText.Text = 0.ToString("N0");
                    }
                    else
                        WritesText.Text = ((writes - _lastWrites) / el).ToString("N0");




                    if (((pw - _lastPoisonWaits) / el) <= 0)
                    {
                        PoisonWaitsText.Text = 0.ToString("N0");
                    }
                    else
                        PoisonWaitsText.Text = ((pw - _lastPoisonWaits) / el).ToString("N0");

                    if (((pws - _lastPoisonWaitSerializable) / el) <= 0)
                    {
                        PoisonWaitsSLText.Text = 0.ToString("N0");
                    }
                    else
                        PoisonWaitsSLText.Text = ((writes - _lastPoisonWaitSerializable) / el).ToString("N0");

                    if (((pwn - _lastPoisonWaitCMEM) / el) <= 0)
                    {
                        PoisonWaitsMemText.Text = 0.ToString("N0");
                    }
                    else
                        PoisonWaitsMemText.Text = ((pwn - _lastPoisonWaitCMEM) / el).ToString("N0");

                }
            }
            _lastBatchReq = br;
            _lastTrans = tr;
            _lastComp = sc;
            _lastReads = reads;
            _lastWrites = writes;
            _lastPoisonWaits = pw;
            _lastPoisonWaitSerializable = pws;
            _lastPoisonWaitCMEM = pwn;
            _lastSampleTime = now;

            // When building currentWaits, use the same canonical keys:
            var currentWaits = new Dictionary<string, long> {
                { "Locks", lcks },
                { "Reads/Latches", rds },
                { "Writes/I/O", wrts },
                { "Network", nw },
                { "Backup", bkp },
                { "Memory", mm },
                { "Parallelism", cx },
                { "Transaction Log", tlog },
                { "PoisonWaits", pw },
                { "Poison Serializable Locking", pws },
                { "Poison CMEMTHREAD and NUMA", pwn }
            };

            // Set baseline on first sample
            if (_baselineWaits == null)
            {
                _baselineWaits = new Dictionary<string, long>(currentWaits);
                _lastWaits = new Dictionary<string, long>(currentWaits);
            }

            // Calculate delta from last sample (for graph) and from baseline (for table)
            var deltaFromBaseline = new Dictionary<string, long>();
            var deltaFromLast = new Dictionary<string, long>();

            foreach (var key in currentWaits.Keys)
            {
                deltaFromBaseline[key] = currentWaits[key] - _baselineWaits[key];
                deltaFromLast[key] = currentWaits[key] - (_lastWaits.ContainsKey(key) ? _lastWaits[key] : currentWaits[key]);
            }

            _lastWaits = new Dictionary<string, long>(currentWaits);

            // Update table with delta from baseline
            var tot = deltaFromBaseline.Values.Sum();
            var ws = new List<WaitStatItem>();
            foreach (var kv in deltaFromBaseline)
            {
                var pct = tot > 0 ? (double)kv.Value / tot * 100 : 0;
                ws.Add(new WaitStatItem { WaitType = kv.Key, WaitTimeMs = kv.Value, Percentage = pct, Color = GetCachedBrush(_waitColors[kv.Key]) });
            }
            WaitStatsGrid.ItemsSource = ws.Where(w => w.WaitTimeMs > 0).OrderByDescending(w => w.WaitTimeMs).ToList();

            // Update graph with delta from last sample (actual ms values, not percentages)
            foreach (var kv in deltaFromLast)
            {
                var val = Math.Max(0, kv.Value);
                _waitHistory[kv.Key].Enqueue(val);
                while (_waitHistory[kv.Key].Count > MaxHistoryPoints) _waitHistory[kv.Key].Dequeue();
            }
        }

        private void DrawWaitGraph()
        {
            WaitGraphCanvas.Children.Clear();
            YAxisCanvas.Children.Clear();

            var w = WaitGraphCanvas.ActualWidth;
            var h = WaitGraphCanvas.ActualHeight;
            if (w <= 0 || h <= 0) return;

            var maxPts = _waitHistory.Values.Max(q => q.Count);
            if (maxPts < 2) return;

            // Calculate max value across all history + 10% headroom
            var allValues = _waitHistory.Values.SelectMany(q => q).ToList();
            var maxVal = allValues.Any() ? (double)allValues.Max() : 1;
            if (maxVal < 1) maxVal = 1;
            maxVal = maxVal * 1.1; // Add 10% headroom

            var xStep = w / (MaxHistoryPoints - 1);
            var yPadding = 10.0;
            var graphHeight = h - yPadding * 2;

            // Draw Y-axis labels and grid lines
            DrawYAxis(maxVal, h, w);

            // Draw data lines with anti-aliasing
            foreach (var kv in _waitHistory.Where(x => x.Value.Any(v => v > 0)))
            {
                var pts = kv.Value.ToArray();
                var off = MaxHistoryPoints - pts.Length;
                var pl = new Polyline
                {
                    Stroke = GetCachedBrush(_waitColors[kv.Key]),
                    StrokeThickness = 2,
                    StrokeLineJoin = PenLineJoin.Round
                };
                for (int i = 0; i < pts.Length; i++)
                {
                    var yVal = h - yPadding - ((double)pts[i] / maxVal * graphHeight);
                    yVal = Math.Max(yPadding, Math.Min(h - yPadding, yVal)); // Clamp to graph area
                    pl.Points.Add(new System.Windows.Point((off + i) * xStep, yVal));
                }
                WaitGraphCanvas.Children.Add(pl);
            }
        }

        private void DrawYAxis(double maxVal, double canvasHeight, double graphWidth)
        {
            var yAxisWidth = YAxisCanvas.ActualWidth > 0 ? YAxisCanvas.ActualWidth : 50;
            var yPadding = 10.0;
            var graphHeight = canvasHeight - yPadding * 2;

            if (graphHeight <= 0) return;

            // Determine nice tick values
            var tickCount = 5;
            var tickStep = maxVal / tickCount;

            // Round tick step to nice numbers
            if (tickStep > 0)
            {
                var magnitude = Math.Pow(10, Math.Floor(Math.Log10(tickStep)));
                var normalized = tickStep / magnitude;
                double niceStep;
                if (normalized <= 1) niceStep = 1;
                else if (normalized <= 2) niceStep = 2;
                else if (normalized <= 5) niceStep = 5;
                else niceStep = 10;
                tickStep = niceStep * magnitude;
            }
            else
            {
                tickStep = 1;
            }

            for (int i = 0; i <= tickCount; i++)
            {
                var value = i * tickStep;
                if (value > maxVal * 1.05) break;

                var yPos = canvasHeight - yPadding - (value / maxVal * graphHeight);
                if (yPos < 0 || yPos > canvasHeight) continue;

                // Y-axis label - format based on magnitude
                string labelText;
                if (value >= 1000000000) labelText = (value / 1000000000).ToString("F1") + "B";
                else if (value >= 1000000) labelText = (value / 1000000).ToString("F1") + "M";
                else if (value >= 1000) labelText = (value / 1000).ToString("F0") + "K";
                else labelText = value.ToString("F0");

                var label = new TextBlock
                {
                    Text = labelText,
                    FontSize = 9,
                    Foreground = System.Windows.Media.Brushes.Gray,
                    TextAlignment = TextAlignment.Right,
                    Width = yAxisWidth - 8
                };
                Canvas.SetLeft(label, 0);
                Canvas.SetTop(label, yPos - 7);
                YAxisCanvas.Children.Add(label);

                // Grid line on main canvas - dashed for non-zero values
                if (i > 0)
                {
                    var gridLine = new Line
                    {
                        X1 = 0,
                        Y1 = yPos,
                        X2 = graphWidth,
                        Y2 = yPos,
                        Stroke = System.Windows.Media.Brushes.LightGray,
                        StrokeThickness = 0.5,
                        StrokeDashArray = new DoubleCollection { 4, 4 }
                    };
                    WaitGraphCanvas.Children.Add(gridLine);
                }
                else
                {
                    // Solid baseline at 0
                    var baseLine = new Line
                    {
                        X1 = 0,
                        Y1 = yPos,
                        X2 = graphWidth,
                        Y2 = yPos,
                        Stroke = System.Windows.Media.Brushes.DarkGray,
                        StrokeThickness = 1
                    };
                    WaitGraphCanvas.Children.Add(baseLine);
                }
            }
        }

        // ========== PERFORMANCE: Optimized graph drawing with reused Polylines ==========
        private readonly Dictionary<string, Polyline> _graphPolylines = new();
        private readonly List<UIElement> _gridLinePool = new();

        private void DrawWaitGraphOptimized()
        {
            var w = WaitGraphCanvas.ActualWidth;
            var h = WaitGraphCanvas.ActualHeight;
            if (w <= 0 || h <= 0) return;

            var maxPts = _waitHistory.Values.Max(q => q.Count);
            if (maxPts < 2) return;

            // Calculate max value
            long maxVal = 1;
            foreach (var q in _waitHistory.Values)
            {
                foreach (var v in q)
                {
                    if (v > maxVal) maxVal = v;
                }
            }
            var maxValD = maxVal * 1.1;

            var xStep = w / (MaxHistoryPoints - 1);
            var yPadding = 10.0;
            var graphHeight = h - yPadding * 2;

            // Update or create polylines (reuse existing ones)
            foreach (var kv in _waitHistory)
            {
                if (!_graphPolylines.TryGetValue(kv.Key, out var pl))
                {
                    pl = new Polyline
                    {
                        Stroke = GetCachedBrush(_waitColors[kv.Key]),
                        StrokeThickness = 2,
                        StrokeLineJoin = PenLineJoin.Round
                    };
                    _graphPolylines[kv.Key] = pl;
                    WaitGraphCanvas.Children.Add(pl);
                }

                pl.Points.Clear();

                if (kv.Value.Any(v => v > 0))
                {
                    var pts = kv.Value.ToArray();
                    var off = MaxHistoryPoints - pts.Length;
                    for (int i = 0; i < pts.Length; i++)
                    {
                        var yVal = h - yPadding - ((double)pts[i] / maxValD * graphHeight);
                        yVal = Math.Max(yPadding, Math.Min(h - yPadding, yVal)); // Clamp to graph area
                        pl.Points.Add(new System.Windows.Point((off + i) * xStep, yVal));
                    }
                    pl.Visibility = Visibility.Visible;
                }
                else
                {
                    pl.Visibility = Visibility.Collapsed;
                }
            }

            // Update Y-axis less frequently (every 5 ticks)
            if (_tickCount % 5 == 0)
            {
                YAxisCanvas.Children.Clear();
                // Clear old grid lines first
                var toRemove = WaitGraphCanvas.Children.OfType<Line>().ToList();
                foreach (var line in toRemove)
                {
                    WaitGraphCanvas.Children.Remove(line);
                }
                DrawYAxis(maxValD, h, w);
            }
        }

        private void UpdateSessionsAfterChange()
        {
            using var conn = new SqlConnection(_selectedConnectionString);
            conn.Open();

            //DataTable? UpdateSessionsTable = null;

            var UpdateSessionsTable = GetSessionsInternal(conn);
            UpdateSessions(UpdateSessionsTable);
        }
        //private async void UpdateSessionsAfterChange()
        //{
        //    try
        //    {
        //        var sessions = await _sessionService.GetActiveSessionsAsync();
        //        Dispatcher.Invoke(() =>
        //        {
        //            SessionGrid.ItemsSource = sessions;
        //        });
        //    }
        //    catch (Exception ex)
        //    {
        //        // Handle error appropriately
        //        MessageBox.Show($"Error updating sessions: {ex.Message}");
        //    }
        //}
        private void UpdateSessions(List<SessionItem> sess)
        {
            //var sess = new List<SessionItem>() = dt;
            ; long totCpu = 0, totIo = 0;

            foreach (var s in sess)
            {
                var cpuPct = totCpu > 0 ? (double)s.Cpu / totCpu * 100 : 0;
                var ioPct = totIo > 0 ? (double)s.PhysicalIo / totIo * 100 : 0;
                if (!_spidHistories.ContainsKey(s.Spid)) _spidHistories[s.Spid] = new SpidHistory { Spid = s.Spid };
                _spidHistories[s.Spid].AddSample(cpuPct, ioPct);
                _spidHistories[s.Spid].Database = s.Database;
                _spidHistories[s.Spid].IsActive = true;
                _spidHistories[s.Spid].Hostname = s.Hostname;
                _spidHistories[s.Spid].ProgramName = s.ProgramName;
                _spidHistories[s.Spid].LoginName = s.LoginName;
                _spidHistories[s.Spid].Command = s.Command;
                _spidHistories[s.Spid].text = s.text;
                _spidHistories[s.Spid].Blocked = s.Blocked;
                _spidHistories[s.Spid].Status = s.Status;
                _spidHistories[s.Spid].IdleSeconds = s.IdleSeconds;
                _spidHistories[s.Spid].LastCpu = s.Cpu;
                _spidHistories[s.Spid].LastIo = s.PhysicalIo;
            }
            //net48 var active = sess.Select(x => x.Spid).ToHashSet();
            // To this:
            var active = new HashSet<int>(sess.Select(x => x.Spid));
            foreach (var h in _spidHistories.Values) if (!active.Contains(h.Spid)) { h.IsActive = false; h.AddSample(0, 0); }
            SessionsGrid.ItemsSource = sess;
            SessionCountText.Text = $" ({sess.Count})";
            //dt.Dispose();
        }

        private void UpdateBlocking(List<BlockingInfo> items)
        {
            _currentBlocking.Clear();
            _currentBlocking.AddRange(items);

            BlockingGrid.ItemsSource = _currentBlocking.OrderByDescending(b => b.WaitDurationMs).ToList();
            BlockingCountText.Text = $" ({_currentBlocking.Count})";
            BlockingAlert.Visibility = _currentBlocking.Any() ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateTopQueries(List<TopQueryItem> queries)
        {
            TopQueriesGrid.ItemsSource = queries;

        }
        private void ClearSpidBoxes()
        {
            SpidBoxesCanvas.Children.Clear();
        }

        // ========== PERFORMANCE: Optimized SPID boxes with object pooling ==========
        private void UpdateSpidBoxesOptimized()
        {
            if (SpidBoxesCanvas == null) return;

            // Return active boxes to pool
            foreach (var box in _activeSpidBoxes)
            {
                SpidBoxesCanvas.Children.Remove(box);
                if (_spidBoxPool.Count < MaxPoolSize)
                {
                    _spidBoxPool.Enqueue(box);
                }
            }
            _activeSpidBoxes.Clear();
            _spidBoxPositions.Clear();
            _spidBoxBorders.Clear();

            // Pre-calculate blocking sets (avoid multiple enumerations)
            var blockerSpids = new HashSet<int>();
            var waitSpids = new HashSet<int>();
            foreach (var b in _currentBlocking)
            {
                blockerSpids.Add(b.BlockerSpid);
                waitSpids.Add(b.WaitSpid);
            }

            // Filter and sort data (limit to prevent UI lag)
            var rel = GetFilteredSpidHistories(50); // Limit to 50 boxes max for performance

            if (!string.IsNullOrEmpty(_programFilter))
            {
                FilteredCountText.Text = $"Showing {rel.Count} sessions (filtered)";
            }
            else
            {
                FilteredCountText.Text = "";
            }

            const double boxWidth = 85;
            const double boxHeight = 55;
            const double margin = 8;
            var canvasWidth = SpidBoxesCanvas.ActualWidth > 0 ? SpidBoxesCanvas.ActualWidth : 500;
            var boxesPerRow = Math.Max(1, (int)((canvasWidth - margin) / (boxWidth + margin)));

            int index = 0;
            foreach (var sp in rel)
            {
                int row = index / boxesPerRow;
                int col = index % boxesPerRow;
                double x = margin + col * (boxWidth + margin);
                double y = margin + row * (boxHeight + margin);

                bool isBlocker = blockerSpids.Contains(sp.Spid);
                bool isWaiting = waitSpids.Contains(sp.Spid);

                var box = GetOrCreateSpidBox(sp, isBlocker, isWaiting, boxWidth);
                Canvas.SetLeft(box, x);
                Canvas.SetTop(box, y);

                if (!SpidBoxesCanvas.Children.Contains(box))
                {
                    SpidBoxesCanvas.Children.Add(box);
                }
                _activeSpidBoxes.Add(box);

                _spidBoxPositions[sp.Spid] = new System.Drawing.Point((int)(x + boxWidth / 2), (int)(y + boxHeight / 2));
                _spidBoxBorders[sp.Spid] = box;

                index++;
            }

            int totalRows = (rel.Count + boxesPerRow - 1) / boxesPerRow;
            SpidBoxesCanvas.Height = Math.Max(200, totalRows * (boxHeight + margin) + margin);

            DrawBlockingLinesOptimized(blockerSpids, waitSpids);
        }

        private List<SpidHistory> GetFilteredSpidHistories(int maxCount)
        {
            // Pre-filter to avoid sorting entire collection
            var candidates = new List<SpidHistory>(maxCount * 2);

            foreach (var h in _spidHistories.Values)
            {
                if (candidates.Count >= maxCount * 2) break;

                // Apply filters
                if (!string.IsNullOrEmpty(_programFilter))
                {
                    if (h.ProgramName == null ||
                        h.ProgramName.IndexOf(_programFilter, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                }

                if (SPIDFilter?.IsChecked == true)
                {
                    if (h.Status != null &&
                        h.Status.IndexOf("sleeping", StringComparison.OrdinalIgnoreCase) >= 0)
                        continue;
                }

                candidates.Add(h);
            }

            // Sort and take top N
            return candidates
                .OrderByDescending(h => h.IoHistory.LastOrDefault())
                .ThenByDescending(h => h.CpuHistory.LastOrDefault())
                .Take(maxCount)
                .ToList();
        }

        private Border GetOrCreateSpidBox(SpidHistory sp, bool isBlocker, bool isWaiting, double fixedWidth)
        {
            Border brd;
            if (_spidBoxPool.Count > 0)
            {
                brd = _spidBoxPool.Dequeue();
                UpdateSpidBoxContent(brd, sp, isBlocker, isWaiting, fixedWidth);
            }
            else
            {
                brd = CreateSpidBoxOptimized(sp, isBlocker, isWaiting, fixedWidth);
            }
            return brd;
        }

        private void UpdateSpidBoxContent(Border brd, SpidHistory sp, bool isBlocker, bool isWaiting, double fixedWidth)
        {
            // Update existing border properties
            var borderColor = isBlocker ? System.Windows.Media.Colors.Red :
                (sp.IsActive ? System.Windows.Media.Color.FromRgb(0, 120, 212) : System.Windows.Media.Color.FromRgb(200, 200, 200));
            brd.BorderBrush = GetCachedBrush(borderColor);
            brd.BorderThickness = new Thickness(isBlocker ? 3 : (sp.IsActive ? 2 : 1));
            brd.Background = GetCachedBrush(isBlocker ? System.Windows.Media.Color.FromRgb(255, 240, 240) :
                (sp.IsActive ? System.Windows.Media.Color.FromRgb(250, 250, 250) : System.Windows.Media.Color.FromRgb(240, 240, 240)));

            if (brd.Child is Grid g && g.Children.Count >= 3)
            {
                // Update header
                if (g.Children[0] is TextBlock hdr)
                {
                    var hdrText = $"SPID {sp.Spid}";
                    if (isBlocker) hdrText += " 🔒";
                    if (isWaiting) hdrText += " ⏳";
                    hdr.Text = hdrText;
                    hdr.Foreground = isBlocker ? _redBrush : (sp.IsActive ? _blackBrush : _grayBrush);
                }

                // Update CPU text
                if (g.Children[1] is TextBlock cpuText)
                {
                    cpuText.Text = $"CPU: {sp.CpuHistory.LastOrDefault():F1}%";
                }

                // Update I/O text
                if (g.Children[2] is TextBlock ioText)
                {
                    ioText.Text = $"I/O: {sp.IoHistory.LastOrDefault():F1}%";
                }
            }

            // Update tooltip with current data
            var tooltipText = BuildSpidTooltip(sp, isBlocker, isWaiting);
            brd.ToolTip = new System.Windows.Controls.ToolTip
            {
                Content = new TextBlock
                {
                    Text = tooltipText,
                    FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                    FontSize = 11
                },
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 45, 48)),
                Foreground = System.Windows.Media.Brushes.White,
                Padding = new Thickness(10),
                BorderThickness = new Thickness(0)
            };
        }

        private Border CreateSpidBoxOptimized(SpidHistory sp, bool isBlocker, bool isWaiting, double fixedWidth)
        {
            var borderColor = isBlocker ? System.Windows.Media.Colors.Red :
                (sp.IsActive ? System.Windows.Media.Color.FromRgb(0, 120, 212) : System.Windows.Media.Color.FromRgb(200, 200, 200));

            var brd = new Border
            {
                Width = fixedWidth,
                MinHeight = 50,
                Background = GetCachedBrush(isBlocker ? System.Windows.Media.Color.FromRgb(255, 240, 240) :
                    (sp.IsActive ? System.Windows.Media.Color.FromRgb(250, 250, 250) : System.Windows.Media.Color.FromRgb(240, 240, 240))),
                BorderBrush = GetCachedBrush(borderColor),
                BorderThickness = new Thickness(isBlocker ? 3 : (sp.IsActive ? 2 : 1)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 4, 6, 4)
            };

            var g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var hdrText = $"SPID {sp.Spid}";
            if (isBlocker) hdrText += " 🔒";
            if (isWaiting) hdrText += " ⏳";

            var hdr = new TextBlock
            {
                Text = hdrText,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = isBlocker ? _redBrush : (sp.IsActive ? _blackBrush : _grayBrush),
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };
            Grid.SetRow(hdr, 0);
            g.Children.Add(hdr);

            var cpuText = new TextBlock
            {
                Text = $"CPU: {sp.CpuHistory.LastOrDefault():F1}%",
                FontSize = 10,
                Foreground = _blueBrush,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0)
            };
            Grid.SetRow(cpuText, 1);
            g.Children.Add(cpuText);

            var ioText = new TextBlock
            {
                Text = $"I/O: {sp.IoHistory.LastOrDefault():F1}%",
                FontSize = 10,
                Foreground = _orangeBrush,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 1, 0, 0)
            };
            Grid.SetRow(ioText, 2);
            g.Children.Add(ioText);

            brd.Child = g;

            // Add tooltip with detailed SPID information
            var tooltipText = BuildSpidTooltip(sp, isBlocker, isWaiting);
            brd.ToolTip = new System.Windows.Controls.ToolTip
            {
                Content = new TextBlock
                {
                    Text = tooltipText,
                    FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                    FontSize = 11
                },
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 45, 48)),
                Foreground = System.Windows.Media.Brushes.White,
                Padding = new Thickness(10),
                BorderThickness = new Thickness(0)
            };

            return brd;
        }

        private readonly List<Line> _blockingLinePool = new();
        private readonly List<Line> _activeBlockingLines = new();

        private void DrawBlockingLinesOptimized(HashSet<int> blockerSpids, HashSet<int> waitSpids)
        {
            // Return lines to pool
            foreach (var line in _activeBlockingLines)
            {
                SpidBoxesCanvas.Children.Remove(line);
                _blockingLinePool.Add(line);
            }
            _activeBlockingLines.Clear();

            int lineIndex = 0;
            foreach (var blocking in _currentBlocking)
            {
                if (_spidBoxPositions.TryGetValue(blocking.WaitSpid, out var waitPos) &&
                    _spidBoxPositions.TryGetValue(blocking.BlockerSpid, out var blockerPos))
                {
                    Line line;
                    if (lineIndex < _blockingLinePool.Count)
                    {
                        line = _blockingLinePool[lineIndex];
                    }
                    else
                    {
                        line = new Line
                        {
                            Stroke = _redBrush,
                            StrokeThickness = 2,
                            StrokeDashArray = new DoubleCollection { 4, 2 }
                        };
                    }

                    line.X1 = waitPos.X;
                    line.Y1 = waitPos.Y;
                    line.X2 = blockerPos.X;
                    line.Y2 = blockerPos.Y;

                    SpidBoxesCanvas.Children.Insert(0, line);
                    _activeBlockingLines.Add(line);
                    lineIndex++;
                }
            }
        }

        private void UpdateSpidBoxes()
        {
            if (SpidBoxesCanvas == null) return; // avoid calling before UI is built


            SpidBoxesCanvas.Children.Clear();
            _spidBoxPositions.Clear();
            _spidBoxBorders.Clear();

            //net48 var blockerSpids = _currentBlocking.Select(b => b.BlockerSpid).ToHashSet();
            //net48 var waitSpids = _currentBlocking.Select(b => b.WaitSpid).ToHashSet();
            var blockerSpids = new HashSet<int>(_currentBlocking.Select(b => b.BlockerSpid));
            var waitSpids = new HashSet<int>(_currentBlocking.Select(b => b.WaitSpid));

            // Use virtualization or pagination for large datasets
            //var visibleData = spidData.Take(1000); // Limit to prevent UI lag 

            var allRelevant = _spidHistories.Values.Take(1000)
                //.Where(h => h.CpuHistory.Any(v => v > 0) || h.IoHistory.Any(v => v > 0))
                .OrderByDescending(h => h.IoHistory.Sum())
                //.OrderByDescending(h => h.Spid)
                .ThenByDescending(h => h.CpuHistory.Sum())
                .ToList();
            ClearSpidBoxes();
            // Apply program filter if specified
            var rel = allRelevant;
            if (!string.IsNullOrEmpty(_programFilter))
            {
                rel = allRelevant
                    .Where(h => h.ProgramName != null &&
                           h.ProgramName.IndexOf(_programFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();

                FilteredCountText.Text = $"Showing {rel.Count} of {allRelevant.Count} sessions (filtered by '{_programFilter}')";
            }
            else
            {
                FilteredCountText.Text = "";
            }


            if (SPIDFilter.IsChecked == true)
            {
                rel = allRelevant
                    .Where(h => h.Status != null &&
                          !(h.Status.IndexOf("sleeping", StringComparison.OrdinalIgnoreCase) >= 0))
                    .ToList();

                //FilteredCountText.Text = $"Showing {rel.Count} of {allRelevant.Count} sessions (filtered by '{_programFilter}')";
            }
            else
            {
                rel = allRelevant
                        .Where(h => h.Status != null)
                        .ToList();
            }





            const double boxWidth = 85;
            const double boxHeight = 55;
            const double margin = 8;
            var canvasWidth = SpidBoxesCanvas.ActualWidth > 0 ? SpidBoxesCanvas.ActualWidth : 500;
            var boxesPerRow = Math.Max(1, (int)((canvasWidth - margin) / (boxWidth + margin)));

            int index = 0;
            foreach (var sp in rel)
            {
                int row = index / boxesPerRow;
                int col = index % boxesPerRow;
                double x = margin + col * (boxWidth + margin);
                double y = margin + row * (boxHeight + margin);

                bool isBlocker = blockerSpids.Contains(sp.Spid);
                bool isWaiting = waitSpids.Contains(sp.Spid);

                var box = CreateSpidBox(sp, isBlocker, isWaiting, boxWidth);
                Canvas.SetLeft(box, x);
                Canvas.SetTop(box, y);

                SpidBoxesCanvas.Children.Add(box);

                _spidBoxPositions[sp.Spid] = new System.Drawing.Point((int)(x + (boxWidth / 2)), (int)(y + (boxHeight / 2)));
                _spidBoxBorders[sp.Spid] = box;

                index++;
            }

            int totalRows = (rel.Count + boxesPerRow - 1) / boxesPerRow;
            SpidBoxesCanvas.Height = Math.Max(200, totalRows * (boxHeight + margin) + margin);

            DrawBlockingLines();
        }

        private void DrawBlockingLines()
        {
            foreach (var blocking in _currentBlocking)
            {
                if (_spidBoxPositions.TryGetValue(blocking.WaitSpid, out var waitPos) &&
                    _spidBoxPositions.TryGetValue(blocking.BlockerSpid, out var blockerPos))
                {
                    var line = new Line
                    {
                        X1 = waitPos.X,
                        Y1 = waitPos.Y,
                        X2 = blockerPos.X,
                        Y2 = blockerPos.Y,
                        Stroke = System.Windows.Media.Brushes.Red,
                        StrokeThickness = 2,
                        StrokeDashArray = new DoubleCollection { 4, 2 }

                    };

                    SpidBoxesCanvas.Children.Insert(0, line);
                    //Float lien above boxes
                    System.Windows.Controls.Panel.SetZIndex(line, 1);

                }
            }
        }

        private Border CreateSpidBox(SpidHistory sp, bool isBlocker, bool isWaiting, double fixedWidth)
        {
            var borderColor = isBlocker ? System.Windows.Media.Colors.Red : (sp.IsActive ? System.Windows.Media.Color.FromRgb(0, 120, 212) : System.Windows.Media.Color.FromRgb(200, 200, 200));
            var borderThickness = isBlocker ? 3 : (sp.IsActive ? 2 : 1);

            var brd = new Border
            {
                Width = fixedWidth,
                MinHeight = 50,
                Background = new SolidColorBrush(isBlocker ? System.Windows.Media.Color.FromRgb(255, 240, 240) : (sp.IsActive ? System.Windows.Media.Color.FromRgb(250, 250, 250) : System.Windows.Media.Color.FromRgb(240, 240, 240))),
                BorderBrush = new SolidColorBrush(borderColor),
                BorderThickness = new Thickness(borderThickness),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0),
                Padding = new Thickness(6, 4, 6, 4)
            };

            var g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var hdrText = $"SPID {sp.Spid}";
            if (isBlocker) hdrText += " 🔒";
            if (isWaiting) hdrText += " ⏳";

            var hdr = new TextBlock
            {
                Text = hdrText,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(isBlocker ? System.Windows.Media.Colors.Red : (sp.IsActive ? System.Windows.Media.Colors.Black : System.Windows.Media.Colors.Gray)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };
            Grid.SetRow(hdr, 0);
            g.Children.Add(hdr);

            var lastCpuPct = sp.CpuHistory.LastOrDefault();
            var cpuText = new TextBlock
            {
                Text = $"CPU: {lastCpuPct:F1}%",
                FontSize = 10,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 120, 212)),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0)
            };
            Grid.SetRow(cpuText, 1);
            g.Children.Add(cpuText);

            var lastIoPct = sp.IoHistory.LastOrDefault();
            var ioText = new TextBlock
            {
                Text = $"I/O: {lastIoPct:F1}%",
                FontSize = 10,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(216, 59, 1)),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 1, 0, 0)
            };
            Grid.SetRow(ioText, 2);
            g.Children.Add(ioText);

            brd.Child = g;

            var tooltipText = BuildSpidTooltip(sp, isBlocker, isWaiting);
            brd.ToolTip = new System.Windows.Controls.ToolTip
            {
                Content = new TextBlock
                {
                    Text = tooltipText,
                    FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                    FontSize = 11
                },
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 45, 48)),
                Foreground = System.Windows.Media.Brushes.White,
                Padding = new Thickness(10),
                BorderThickness = new Thickness(0)
            };

            return brd;
        }

        // Current approach - string concatenation in loops
        //$"CPU %: {string.Join(" → ", sp.CpuHistory.Skip(Math.Max(0, sp.CpuHistory.Count - 5)).Select(v => v.ToString("F0")))"

        // Better approach - pre-calculate and reuse
        private string FormatHistory(IEnumerable<double> history)
        {
            var recent = history.Skip(Math.Max(0, history.Count() - 5)).ToArray();
            return string.Join(" → ", recent.Select(v => v.ToString("F0")));
        }
        private string BuildSpidTooltip(SpidHistory sp, bool isBlocker, bool isWaiting)
        {
            var lines = new List<string>
            {
                $"═══ SPID {sp.Spid} ═══",
                "",
                $"Database:    {(string.IsNullOrEmpty(sp.Database) ? "(none)" : sp.Database)}",
                $"Status:      {sp.Status}",
                $"Blocked by:  {sp.Blocked}",
                $"Command:     {sp.Command}",
                $"Login:       {sp.LoginName}",
                $"Host:        {sp.Hostname}",
                $"Program:     {(sp.ProgramName?.Length > 30 ? sp.ProgramName.Substring(0, 30) + "..." : sp.ProgramName)}",
                $"Query:       {sp.text}",
                "",
                $"CPU (total): {sp.LastCpu:N0}",
                $"I/O (total): {sp.LastIo:N0}",
                $"Idle:        {FormatIdleTime(sp.IdleSeconds)}",
                "",
                "─── Recent Activity ───",
                //$"CPU %: {string.Join(" → ", sp.CpuHistory.Skip(Math.Max(0, sp.CpuHistory.Count - 5)).Select(v => v.ToString("F0")))}",
                //$"I/O %: {string.Join(" → ", sp.IoHistory.Skip(Math.Max(0, sp.IoHistory.Count - 5)).Select(v => v.ToString("F0")))}"
                $"CPU %: {FormatHistory(sp.CpuHistory)}",
                $"I/O %: {FormatHistory(sp.IoHistory)}"
            };

            if (isBlocker)
            {
                lines.Add("");
                lines.Add("⚠ THIS SESSION IS BLOCKING OTHERS");
            }

            if (isWaiting)
            {
                lines.Add("");
                lines.Add("⏳ This session is WAITING (blocked)");
            }

            return string.Join("\n", lines);
        }

        private string FormatIdleTime(int seconds)
        {
            if (seconds < 60) return $"{seconds}s";
            if (seconds < 3600) return $"{seconds / 60}m {seconds % 60}s";
            return $"{seconds / 3600}h {(seconds % 3600) / 60}m";
        }

        // Program filter for Session Activity
        //private string _programFilter = "";

        private void ProgramFilterText_TextChanged(object sender, TextChangedEventArgs e)
        {
            _programFilter = ProgramFilterText.Text?.Trim() ?? "";

            UpdateSpidBoxes();
            UpdateSessionsAfterChange();

        }

        private void ClearProgramFilter_Click(object sender, RoutedEventArgs e)
        {
            ProgramFilterText.Text = "";
            _programFilter = "";
            UpdateSpidBoxes();
            UpdateSessionsAfterChange();
        }


        //ClearSPIDFilter

        private void ToggleSPIDFilter_Checked(object sender, RoutedEventArgs e)
        {
            SPIDFilter.Content = "Not Showing Sleeping SPIDs";
            ShowSleepingSPIDs = false;
            UpdateSpidBoxes();
            UpdateSessionsAfterChange();
        }

        private void ToggleSPIDFilter_Unchecked(object sender, RoutedEventArgs e)
        {
            SPIDFilter.Content = "Showing Sleeping SPIDs";
            ShowSleepingSPIDs = true;
            UpdateSpidBoxes();
            UpdateSessionsAfterChange();
        }

        private void LimitSessions(object sender, TextChangedEventArgs e)
        {
            TopSessions = Int32.Parse(LimitSessionsText.Text);
        }

        private void ToggleSPIDFilter_ThirdState(object sender, RoutedEventArgs e)
        {
            SPIDFilter.Content = "Sleeping SPIDs";
        }

        //ClearSPIDFilter Sleeping on Activ



        // View Query button handler
        private void ViewQueryButton_Click(object sender, RoutedEventArgs e)
        {
            if (TopQueriesGrid.SelectedItem is TopQueryItem selected)
            {
                var viewer = new CodeViewerWindow("Query Text", selected.QueryText);
                viewer.Owner = this;
                viewer.ShowDialog();
            }
            else
            {
                System.Windows.MessageBox.Show("Please select a query from the grid first.", "No Selection",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // View Blocking Query button handler
        private void ViewBlockingQueryButton_Click(object sender, RoutedEventArgs e)
        {
            if (BlockingGrid.SelectedItem is BlockingInfo selected)
            {
                var viewer = new CodeViewerWindow("Query Text", selected.WaitStatement);
                viewer.Owner = this;
                viewer.ShowDialog();
            }
            else
            {
                System.Windows.MessageBox.Show("Please select a query from the grid first.", "No Selection",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }


        // Save Plan button handler


        protected override void OnClosed(EventArgs e)
        {
            // Stop monitoring and clean up resources
            StopMonitoring();

            // Unsubscribe from config changes
            _config.ConfigReloaded -= Config_ConfigReloaded;

            // Dispose cancellation token source
            _refreshCts?.Cancel();
            _refreshCts?.Dispose();
            _refreshCts = null;

            // Clear collections to help GC
            _spidHistories.Clear();
            _waitHistory.Clear();
            _currentBlocking.Clear();
            _spidBoxPositions.Clear();
            _spidBoxBorders.Clear();
            _spidBoxPool.Clear();
            _activeSpidBoxes.Clear();
            _blockingLinePool.Clear();
            _activeBlockingLines.Clear();
            _graphPolylines.Clear();

            base.OnClosed(e);
        }
    }

    public class ServerItem
    {
        public string ConnectionString { get; set; } = "";
        public string ServerName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public bool IsConnectable { get; set; }
        public System.Windows.Media.Brush TextColor { get; set; } = System.Windows.Media.Brushes.Black;
    }

    public class WaitStatItem { public string WaitType { get; set; } = ""; public long WaitTimeMs { get; set; } public double Percentage { get; set; } public SolidColorBrush Color { get; set; } = new SolidColorBrush(Colors.Gray); }

    public class SessionItem
    {
        public int Spid { get; set; }
        public string Database { get; set; } = "";
        public string Status { get; set; } = "";
        public long Cpu { get; set; }
        public long PhysicalIo { get; set; }
        public string Hostname { get; set; } = "";
        public string ProgramName { get; set; } = "";
        public string LoginName { get; set; } = "";
        public string Command { get; set; } = "";
        public string text { get; set; } = "";
        public string Blocked { get; set; } = "";
        public int IdleSeconds { get; set; }
    }

    public class SpidHistory
    {
        public int Spid { get; set; }
        public string Database { get; set; } = "";
        public bool IsActive { get; set; }
        public Queue<double> CpuHistory { get; } = new();
        public Queue<double> IoHistory { get; } = new();

        public string Hostname { get; set; } = "";
        public string ProgramName { get; set; } = "";
        public string LoginName { get; set; } = "";
        public string Command { get; set; } = "";
        public string text { get; set; } = "";

        public string Blocked { get; set; } = "";
        public string Status { get; set; } = "";
        public int IdleSeconds { get; set; }
        public long LastCpu { get; set; }
        public long LastIo { get; set; }


        // Optimized version
        public void AddSample(double c, double i)
        {
            // Replace the following lines in SpidHistory.AddSample(double c, double i):
            // CpuHistory.TryDequeue(out _);
            // ...
            // IoHistory.TryDequeue(out _);

            // With the following code:
            if (CpuHistory.Count >= 30)
                CpuHistory.Dequeue();
            CpuHistory.Enqueue(c);

            if (IoHistory.Count >= 30)
                IoHistory.Dequeue();
            IoHistory.Enqueue(i);
        }

    }

    public class BlockingInfo
    {
        public string LockType { get; set; } = "";
        public string DatabaseName { get; set; } = "";
        public int WaitSpid { get; set; }
        public int BlockerSpid { get; set; }
        public long WaitDurationMs { get; set; }
        public string WaitType { get; set; } = "";
        public string WaitStatement { get; set; } = "";
        public string BlockerStatement { get; set; } = "";
    }

    public class TopQueryItem
    {
        public string Category { get; set; } = "";
        public string Database { get; set; } = "";
        public decimal TotalElapsedTimeS { get; set; }
        public long ExecutionCount { get; set; }
        public decimal TotalCpuTimeS { get; set; }
        public long TotalLogicalReads { get; set; }
        public long TotalLogicalWrites { get; set; }
        public string QueryText { get; set; } = "";
        public byte[]? PlanHandle { get; set; }
        public string PlanXml { get; set; } = "";
    }

    public class DriveLatencyItem
    {
        public string Drive { get; set; } = "";
        public int LatencyMs { get; set; }
        public double GBPerDay { get; set; }
        public string FreeSpace { get; set; } = "";
        public int ReadLatencyMs { get; set; }
        public int WriteLatencyMs { get; set; }
    }

    public class ServerDetailsItem
    {
        public string ServerName { get; set; } = "";
        public string Edition { get; set; } = "";
        public int Sockets { get; set; }
        public int VirtualCPUs { get; set; }
        public string VMType { get; set; } = "";
        public double MemoryGB { get; set; }
        public double SqlAllocated { get; set; }
        public double UsedBySql { get; set; }
        public string MemoryState { get; set; } = "";
        public string Version { get; set; } = "";
        public string BuildNr { get; set; } = "";
        public string OS { get; set; } = "";
        public int HADR { get; set; }
        public int SA { get; set; }
        public string Level { get; set; } = "";
    }

    public class MetricsItem
    {
        public int CPU { get; set; }
        public long BatchReq { get; set; }
        public long Trans { get; set; }
        public long SqlComp { get; set; }
        public long PhReads { get; set; }
        public long PhWrites { get; set; }
        public long Locks { get; set; }
        public long Reads { get; set; }
        public long Writes { get; set; }
        public long Network { get; set; }
        public long Backup { get; set; }
        public long Memory { get; set; }
        public long Parallelism { get; set; }
        public long TransactionLog { get; set; }
        public long PoisonWaits { get; set; }
        public long PoisonSerializableLocking { get; set; }
        public long PoisonCMEMTHREAD { get; set; }
    }

    // public class Line
    // {
    //     X1 = 0,
    //                 Y1 = yPos,
    //                 X2 = graphWidth,
    //                 Y2 = yPos,
    //                 Stroke = Brushes.LightGray,
    //                 StrokeThickness = 0.5,
    //                 StrokeDashArray
    //     public Geometry Geometry { get; set; }
    //     public Brush Stroke { get; set; }
    //     public int ZIndex { get; }
    //     public double StrokeThickness { get; set; }
    // }
}