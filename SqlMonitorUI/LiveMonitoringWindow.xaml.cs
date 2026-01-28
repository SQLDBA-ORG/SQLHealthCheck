using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace SqlMonitorUI
{
    public partial class LiveMonitoringWindow : Window
    {
        private readonly List<string> _connectionStrings;
        private string? _selectedConnectionString;
        private DispatcherTimer? _refreshTimer;
        private bool _isRunning;
        private long _lastBatchReq, _lastTrans, _lastComp, _lastReads, _lastWrites;
        private DateTime _lastSampleTime = DateTime.MinValue;
        private int _tickCount = 0;

        private const int MaxHistoryPoints = 120;
        private readonly Dictionary<string, Queue<long>> _waitHistory = new(); // Changed to long for values
        private readonly Dictionary<string, Color> _waitColors = new()
        {
            { "Locks", Color.FromRgb(220, 20, 60) }, { "Reads/Latches", Color.FromRgb(0, 120, 212) },
            { "Writes/I/O", Color.FromRgb(139, 0, 139) }, { "Network", Color.FromRgb(255, 140, 0) },
            { "Backup", Color.FromRgb(128, 128, 128) }, { "Memory", Color.FromRgb(16, 124, 16) },
            { "Parallelism", Color.FromRgb(107, 105, 214) }, { "Transaction Log", Color.FromRgb(216, 59, 1) }
        };

        // Baseline wait stats for delta calculation
        private Dictionary<string, long>? _baselineWaits;
        private Dictionary<string, long> _lastWaits = new();

        private readonly Dictionary<int, SpidHistory> _spidHistories = new();
        private const int SparklinePoints = 30;

        // Current blocking information
        private List<BlockingInfo> _currentBlocking = new();

        // Store SPID box positions for drawing blocking lines
        private readonly Dictionary<int, Point> _spidBoxPositions = new();
        private readonly Dictionary<int, Border> _spidBoxBorders = new();

        public LiveMonitoringWindow(List<string> connectionStrings)
        {
            InitializeComponent();
            _connectionStrings = connectionStrings;
            foreach (var key in _waitColors.Keys) _waitHistory[key] = new Queue<long>();
            BuildLegend();

            // Test connections and populate server dropdown
            Loaded += async (s, e) => await TestAndPopulateServersAsync();
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
                    TextColor = Brushes.Gray
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
                item.TextColor = isConnectable ? Brushes.Black : Brushes.Gray;
            }

            // Refresh the combo box
            ServerCombo.Items.Refresh();

            // Auto-select first connectable server
            var firstConnectable = serverItems.FirstOrDefault(s => s.IsConnectable);
            if (firstConnectable != null)
            {
                ServerCombo.SelectedItem = firstConnectable;
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
            _lastSampleTime = DateTime.MinValue;
            _tickCount = 0;
            _spidHistories.Clear();
            _currentBlocking.Clear();
            foreach (var key in _waitColors.Keys)
            {
                _waitHistory[key].Clear();
            }

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
                var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 15, 5) };
                sp.Children.Add(new Rectangle { Width = 14, Height = 14, Fill = new SolidColorBrush(kvp.Value), Margin = new Thickness(0, 0, 5, 0), RadiusX = 2, RadiusY = 2 });
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
            StartStopButton.Background = new SolidColorBrush(Color.FromRgb(216, 59, 1));
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(GetRefreshInterval()) };
            _refreshTimer.Tick += async (s, e) => await RefreshDataAsync();
            _refreshTimer.Start();
            _ = RefreshDataAsync();
            StatusText.Text = "Monitoring...";
        }

        private void StopMonitoring()
        {
            _isRunning = false;
            StartStopButton.Content = "▶ Start";
            StartStopButton.Background = new SolidColorBrush(Color.FromRgb(16, 124, 16));
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

            try
            {
                _tickCount++;
                var shouldRefreshTopQueries = _tickCount % 30 == 0 || _tickCount == 1;

                await System.Threading.Tasks.Task.Run(() =>
                {
                    using var conn = new SqlConnection(_selectedConnectionString);
                    conn.Open();
                    var metrics = GetMetrics(conn);
                    var sessions = GetSessions(conn);
                    var blocking = GetBlockingInfo(conn);
                    DataTable? topQueries = null;
                    if (shouldRefreshTopQueries)
                        topQueries = GetTopQueries(conn);

                    Dispatcher.Invoke(() =>
                    {
                        UpdateMetrics(metrics);
                        UpdateSessions(sessions);
                        UpdateBlocking(blocking);
                        DrawWaitGraph();
                        UpdateSpidBoxes();
                        if (topQueries != null)
                            UpdateTopQueries(topQueries);
                        LastUpdateText.Text = $"Updated: {DateTime.Now:HH:mm:ss} (tick {_tickCount})";
                    });
                });
            }
            catch (Exception ex) { Dispatcher.Invoke(() => { StatusText.Text = $"Error: {ex.Message}"; }); }
        }

        private DataTable GetMetrics(SqlConnection conn)
        {
            const string q = @"DECLARE @BR BIGINT, @SC BIGINT, @TR BIGINT, @cpu INT
SELECT @BR=ISNULL(SUM(CONVERT(BIGINT,cntr_value)),0) FROM sys.dm_os_performance_counters WHERE LOWER(object_name) LIKE '%sql statistics%' AND LOWER(counter_name)='batch requests/sec'
SELECT @SC=ISNULL(SUM(CONVERT(BIGINT,cntr_value)),0) FROM sys.dm_os_performance_counters WHERE LOWER(object_name) LIKE '%sql statistics%' AND LOWER(counter_name)='sql compilations/sec'
SELECT @TR=ISNULL(SUM(CONVERT(BIGINT,cntr_value)),0) FROM sys.dm_os_performance_counters WHERE LOWER(object_name) LIKE '%databases%' AND LOWER(counter_name)='transactions/sec' AND LOWER(instance_name)<>'_total'
SELECT TOP 1 @cpu=CONVERT(XML,record).value('(./Record/SchedulerMonitorEvent/SystemHealth/ProcessUtilization)[1]','int') FROM sys.dm_os_ring_buffers WHERE ring_buffer_type='RING_BUFFER_SCHEDULER_MONITOR' ORDER BY timestamp DESC
SELECT ISNULL(@cpu,0) AS CPU,
SUM(CONVERT(BIGINT,CASE WHEN wait_type LIKE 'LCK%' THEN wait_time_ms-signal_wait_time_ms ELSE 0 END)) AS Locks,
SUM(CONVERT(BIGINT,CASE WHEN wait_type LIKE 'LATCH%' OR wait_type LIKE 'PAGELATCH%' OR wait_type LIKE 'PAGEIOLATCH%' THEN wait_time_ms-signal_wait_time_ms ELSE 0 END)) AS Reads,
SUM(CONVERT(BIGINT,CASE WHEN wait_type LIKE '%IO_COMPLETION%' OR wait_type='WRITELOG' THEN wait_time_ms-signal_wait_time_ms ELSE 0 END)) AS Writes,
SUM(CONVERT(BIGINT,CASE WHEN wait_type IN('NETWORKIO','OLEDB','ASYNC_NETWORK_IO') THEN wait_time_ms-signal_wait_time_ms ELSE 0 END)) AS Network,
SUM(CONVERT(BIGINT,CASE WHEN wait_type LIKE 'BACKUP%' THEN wait_time_ms-signal_wait_time_ms ELSE 0 END)) AS [Backup],
SUM(CONVERT(BIGINT,CASE WHEN wait_type='CMEMTHREAD' OR wait_type LIKE 'RESOURCE_SEMAPHORE%' THEN wait_time_ms-signal_wait_time_ms ELSE 0 END)) AS Memory,
SUM(CONVERT(BIGINT,CASE WHEN wait_type IN('CXPACKET','EXCHANGE') THEN wait_time_ms-signal_wait_time_ms ELSE 0 END)) AS Parallelism,
SUM(CONVERT(BIGINT,CASE WHEN wait_type IN('LOGBUFFER','LOGMGR','WRITELOG') THEN wait_time_ms-signal_wait_time_ms ELSE 0 END)) AS TransactionLog,
@@TOTAL_READ AS PhReads,@@TOTAL_WRITE AS PhWrites,@BR AS BatchReq,@SC AS SqlComp,@TR AS Trans FROM sys.dm_os_wait_stats";
            using var cmd = new SqlCommand(q, conn) { CommandTimeout = 30 };
            var dt = new DataTable(); new SqlDataAdapter(cmd).Fill(dt); return dt;
        }

        private DataTable GetSessions(SqlConnection conn)
        {
            const string q = @"SELECT 
                s.spid AS Spid,
                DB_NAME(s.dbid) AS [Database],
                status AS Status,
                CAST(cpu AS BIGINT) AS Cpu,
                CAST(physical_io AS BIGINT) AS PhysicalIo,
                hostname AS Hostname,
                program_name AS ProgramName,
                loginame AS LoginName,
                cmd AS Command,
                DATEDIFF(SECOND, last_batch, GETDATE()) AS IdleSeconds
                , SomeText.text
                , s.blocked
            FROM sys.sysprocesses s
            --CROSS APPLY sys.dm_exec_sql_text(sql_handle) e
            LEFT OUTER JOIN
            (
            SELECT spid AS Spid, e.text
            FROM sys.sysprocesses s
            CROSS APPLY sys.dm_exec_sql_text(sql_handle) e
            ) SomeText ON SomeText.spid = s.spid

            WHERE s.spid>50 AND program_name NOT LIKE '%SQLMonitorUI%' 
            AND (hostname <> '' AND program_name <> '')
     
            ORDER BY cpu DESC";
            using var cmd = new SqlCommand(q, conn) { CommandTimeout = 30 };
            var dt = new DataTable(); new SqlDataAdapter(cmd).Fill(dt); return dt;
        }

        private DataTable GetBlockingInfo(SqlConnection conn)
        {
            const string q = @"SELECT 
                t1.resource_type AS lock_type,
                DB_NAME(resource_database_id) AS database_name,
                t1.resource_associated_entity_id AS blk_object,
                t1.request_mode AS lock_req,
                t1.request_session_id AS wait_sid,
                t2.wait_duration_ms AS wait_time,
                t2.wait_type AS wait_type,
                (SELECT text FROM sys.dm_exec_requests r
                    CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) 
                    WHERE r.session_id = t1.request_session_id) AS wait_batch,
                (SELECT SUBSTRING(qt.text, r.statement_start_offset/2, 
                    (CASE WHEN r.statement_end_offset = -1 
                    THEN LEN(CONVERT(NVARCHAR(MAX), qt.text)) * 2 
                    ELSE r.statement_end_offset END - r.statement_start_offset)/2) 
                    FROM sys.dm_exec_requests r
                    CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) qt
                    WHERE r.session_id = t1.request_session_id) AS wait_stmt,
                (SELECT text FROM sys.sysprocesses p
                    CROSS APPLY sys.dm_exec_sql_text(p.sql_handle) 
                    WHERE p.spid = t2.blocking_session_id) AS block_stmt,
                t2.blocking_session_id AS blocker_sid
            FROM sys.dm_tran_locks t1
            INNER JOIN sys.dm_os_waiting_tasks t2 ON t1.lock_owner_address = t2.resource_address";

            using var cmd = new SqlCommand(q, conn) { CommandTimeout = 30 };
            var dt = new DataTable();
            try { new SqlDataAdapter(cmd).Fill(dt); } catch { /* Ignore blocking query errors */ }
            return dt;
        }

        private DataTable GetTopQueries(SqlConnection conn)
        {
            const string q = @"SELECT
                TMP.Category,
                DB_NAME(st.dbid) [DB],
                TMP.[Total Elapsed Time in S],
                TMP.[Total Execution Count],
                TMP.[Total CPU Time in S],
                TMP.[Total Logical Reads],
                TMP.[Total Logical Writes],
                TMP.[Total CLR Time],
                TMP.[Number of Statements],
                TMP.[Last Execution Time],
                TMP.[Plan Handle],
                st.text AS [Query],
                qp.query_plan AS [Plan]

            FROM (
                SELECT * FROM (
                    SELECT TOP 10 'worst I/O' [Category],
                        CAST(SUM(s.total_elapsed_time) / 1000000.0 AS DECIMAL(20, 2)) AS [Total Elapsed Time in S],
                        SUM(s.execution_count) AS [Total Execution Count],
                        CAST(SUM(s.total_worker_time) / 1000000.0 AS DECIMAL(20, 2)) AS [Total CPU Time in S],
                        SUM(s.total_logical_reads) AS [Total Logical Reads],
                        SUM(s.total_logical_writes) AS [Total Logical Writes],
                        SUM(s.total_clr_time) AS [Total CLR Time],
                        COUNT(1) AS [Number of Statements],
                        MAX(s.last_execution_time) AS [Last Execution Time],
                        s.plan_handle AS [Plan Handle]
                    FROM sys.dm_exec_query_stats s
                    GROUP BY s.plan_handle ORDER BY SUM(s.total_logical_reads + s.total_logical_writes) DESC
                ) TT
                --UNION ALL
                --SELECT * FROM (
                --    SELECT TOP 10 'worst CPU' [Category],
                --        CAST(SUM(s.total_elapsed_time) / 1000000.0 AS DECIMAL(20, 2)) AS [Total Elapsed Time in S],
                --        SUM(s.execution_count) AS [Total Execution Count],
                --        CAST(SUM(s.total_worker_time) / 1000000.0 AS DECIMAL(20, 2)) AS [Total CPU Time in S],
                --        SUM(s.total_logical_reads) AS [Total Logical Reads],
                --        SUM(s.total_logical_writes) AS [Total Logical Writes],
                --        SUM(s.total_clr_time) AS [Total CLR Time],
                --        COUNT(1) AS [Number of Statements],
                --        MAX(s.last_execution_time) AS [Last Execution Time],
                --        s.plan_handle AS [Plan Handle]
                --    FROM sys.dm_exec_query_stats s
                --    GROUP BY s.plan_handle ORDER BY SUM(s.total_worker_time) DESC
                --) T
            ) TMP
            OUTER APPLY sys.dm_exec_sql_text(TMP.[Plan Handle]) AS st
            OUTER APPLY
	                sys.dm_exec_query_plan(TMP.[Plan Handle]) AS qp
            ORDER BY Category, [Total Logical Reads] DESC";

            using var cmd = new SqlCommand(q, conn) { CommandTimeout = 60 };
            var dt = new DataTable();
            try { new SqlDataAdapter(cmd).Fill(dt); } catch { /* Ignore query errors */ }
            return dt;
        }

        private void UpdateMetrics(DataTable dt)
        {
            if (dt.Rows.Count == 0) return;
            var r = dt.Rows[0];
            var cpu = Convert.ToInt32(r["CPU"]);
            CpuText.Text = cpu.ToString();
            CpuText.Foreground = new SolidColorBrush(cpu > 80 ? Color.FromRgb(216, 59, 1) : Color.FromRgb(0, 120, 212));

            var now = DateTime.Now;
            var br = Convert.ToInt64(r["BatchReq"]);
            var tr = Convert.ToInt64(r["Trans"]);
            var sc = Convert.ToInt64(r["SqlComp"]);
            var reads = Convert.ToInt64(r["PhReads"]);
            var writes = Convert.ToInt64(r["PhWrites"]);

            if (_lastSampleTime != DateTime.MinValue)
            {
                var el = (now - _lastSampleTime).TotalSeconds;
                if (el > 0)
                {
                    BatchReqText.Text = ((br - _lastBatchReq) / el).ToString("N0");
                    TransText.Text = ((tr - _lastTrans) / el).ToString("N0");
                    CompilationsText.Text = ((sc - _lastComp) / el).ToString("N0");
                    // Page Reads and Writes as deltas per second
                    ReadsText.Text = ((reads - _lastReads) / el).ToString("N0");
                    WritesText.Text = ((writes - _lastWrites) / el).ToString("N0");
                }
            }
            _lastBatchReq = br;
            _lastTrans = tr;
            _lastComp = sc;
            _lastReads = reads;
            _lastWrites = writes;
            _lastSampleTime = now;

            // Current absolute wait values
            var currentWaits = new Dictionary<string, long>{
                {"Locks",Convert.ToInt64(r["Locks"])},
                {"Reads/Latches",Convert.ToInt64(r["Reads"])},
                {"Writes/I/O",Convert.ToInt64(r["Writes"])},
                {"Network",Convert.ToInt64(r["Network"])},
                {"Backup",Convert.ToInt64(r["Backup"])},
                {"Memory",Convert.ToInt64(r["Memory"])},
                {"Parallelism",Convert.ToInt64(r["Parallelism"])},
                {"Transaction Log",Convert.ToInt64(r["TransactionLog"])}
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
                ws.Add(new WaitStatItem { WaitType = kv.Key, WaitTimeMs = kv.Value, Percentage = pct, Color = new SolidColorBrush(_waitColors[kv.Key]) });
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
            var yPadding = 5.0;
            var graphHeight = h - yPadding * 2;

            // Draw Y-axis labels and grid lines
            DrawYAxis(maxVal, h, w);

            // Draw data lines
            foreach (var kv in _waitHistory.Where(x => x.Value.Any(v => v > 0)))
            {
                var pts = kv.Value.ToArray();
                var off = MaxHistoryPoints - pts.Length;
                var pl = new Polyline { Stroke = new SolidColorBrush(_waitColors[kv.Key]), StrokeThickness = 1.5 };
                for (int i = 0; i < pts.Length; i++)
                {
                    var yVal = h - yPadding - ((double)pts[i] / maxVal * graphHeight);
                    pl.Points.Add(new Point((off + i) * xStep, yVal));
                }
                WaitGraphCanvas.Children.Add(pl);
            }
        }

        private void DrawYAxis(double maxVal, double canvasHeight, double graphWidth)
        {
            var yAxisWidth = YAxisCanvas.ActualWidth > 0 ? YAxisCanvas.ActualWidth : 50;
            var yPadding = 5.0;
            var graphHeight = canvasHeight - yPadding * 2;

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

                // Y-axis label - format based on magnitude
                string labelText;
                if (value >= 1000000) labelText = (value / 1000000).ToString("F1") + "M";
                else if (value >= 1000) labelText = (value / 1000).ToString("F1") + "K";
                else labelText = value.ToString("F0");

                var label = new TextBlock
                {
                    Text = labelText,
                    FontSize = 9,
                    Foreground = Brushes.Gray,
                    TextAlignment = TextAlignment.Right,
                    Width = yAxisWidth - 5
                };
                Canvas.SetRight(label, 2);
                Canvas.SetTop(label, yPos - 7);
                YAxisCanvas.Children.Add(label);

                // Grid line on main canvas
                var gridLine = new Line
                {
                    X1 = 0,
                    Y1 = yPos,
                    X2 = graphWidth,
                    Y2 = yPos,
                    Stroke = Brushes.LightGray,
                    StrokeThickness = 0.5,
                    StrokeDashArray = new DoubleCollection { 2, 2 }
                };
                WaitGraphCanvas.Children.Add(gridLine);
            }
        }

        private void UpdateSessions(DataTable dt)
        {
            var sess = new List<SessionItem>(); long totCpu = 0, totIo = 0;
            foreach (DataRow r in dt.Rows)
            {
                var cpu = Convert.ToInt64(r["Cpu"]); var io = Convert.ToInt64(r["PhysicalIo"]);
                totCpu += cpu; totIo += io;
                sess.Add(new SessionItem
                {
                    Spid = Convert.ToInt32(r["Spid"]),
                    Database = r["Database"]?.ToString() ?? "",
                    Status = r["Status"]?.ToString() ?? "",
                    Cpu = cpu,
                    PhysicalIo = io,
                    Hostname = r["Hostname"]?.ToString() ?? "",
                    ProgramName = r["ProgramName"]?.ToString() ?? "",
                    LoginName = r["LoginName"]?.ToString() ?? "",
                    Command = r["Command"]?.ToString() ?? "",
                    text = r["text"]?.ToString() ?? "",
                    Blocked = r["blocked"]?.ToString() ?? "",
                    IdleSeconds = r["IdleSeconds"] != DBNull.Value ? Convert.ToInt32(r["IdleSeconds"]) : 0
                });
            }
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
            var active = sess.Select(x => x.Spid).ToHashSet();
            foreach (var h in _spidHistories.Values) if (!active.Contains(h.Spid)) { h.IsActive = false; h.AddSample(0, 0); }
            SessionsGrid.ItemsSource = sess;
            SessionCountText.Text = $" ({sess.Count})";
        }

        private void UpdateBlocking(DataTable dt)
        {
            _currentBlocking.Clear();

            foreach (DataRow r in dt.Rows)
            {
                _currentBlocking.Add(new BlockingInfo
                {
                    LockType = r["lock_type"]?.ToString() ?? "",
                    DatabaseName = r["database_name"]?.ToString() ?? "",
                    WaitSpid = r["wait_sid"] != DBNull.Value ? Convert.ToInt32(r["wait_sid"]) : 0,
                    BlockerSpid = r["blocker_sid"] != DBNull.Value ? Convert.ToInt32(r["blocker_sid"]) : 0,
                    WaitDurationMs = r["wait_time"] != DBNull.Value ? Convert.ToInt64(r["wait_time"]) : 0,
                    WaitType = r["wait_type"]?.ToString() ?? "",
                    WaitStatement = r["wait_stmt"]?.ToString() ?? r["wait_batch"]?.ToString() ?? "",
                    BlockerStatement = r["block_stmt"]?.ToString() ?? ""

                });
            }

            BlockingGrid.ItemsSource = _currentBlocking.OrderByDescending(b => b.WaitDurationMs).ToList();
            BlockingCountText.Text = $" ({_currentBlocking.Count})";
            BlockingAlert.Visibility = _currentBlocking.Any() ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateTopQueries(DataTable dt)
        {
            var queries = new List<TopQueryItem>();

            foreach (DataRow r in dt.Rows)
            {
                queries.Add(new TopQueryItem
                {
                    Category = r["Category"]?.ToString() ?? "",
                    Database = r["DB"]?.ToString() ?? "",
                    TotalElapsedTimeS = r["Total Elapsed Time in S"] != DBNull.Value ? Convert.ToDecimal(r["Total Elapsed Time in S"]) : 0,
                    ExecutionCount = r["Total Execution Count"] != DBNull.Value ? Convert.ToInt64(r["Total Execution Count"]) : 0,
                    TotalCpuTimeS = r["Total CPU Time in S"] != DBNull.Value ? Convert.ToDecimal(r["Total CPU Time in S"]) : 0,
                    TotalLogicalReads = r["Total Logical Reads"] != DBNull.Value ? Convert.ToInt64(r["Total Logical Reads"]) : 0,
                    TotalLogicalWrites = r["Total Logical Writes"] != DBNull.Value ? Convert.ToInt64(r["Total Logical Writes"]) : 0,
                    QueryText = r["Query"]?.ToString()?.Replace("\r", " ").Replace("\n", " ").Trim() ?? "",
                    PlanHandle = r["Plan Handle"] != DBNull.Value ? (byte[])r["Plan Handle"] : null
                });
            }

            TopQueriesGrid.ItemsSource = queries;
        }

        private void UpdateSpidBoxes()
        {
            if (SpidBoxesCanvas == null) return; // avoid calling before UI is built


            SpidBoxesCanvas.Children.Clear();
            _spidBoxPositions.Clear();
            _spidBoxBorders.Clear();

            var blockerSpids = _currentBlocking.Select(b => b.BlockerSpid).ToHashSet();
            var waitSpids = _currentBlocking.Select(b => b.WaitSpid).ToHashSet();

            var allRelevant = _spidHistories.Values
                //.Where(h => h.CpuHistory.Any(v => v > 0) || h.IoHistory.Any(v => v > 0))
                .OrderByDescending(h => h.IoHistory.Sum())
                //.OrderByDescending(h => h.Spid)
                .ThenByDescending(h => h.CpuHistory.Sum())
                .ToList();

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


            if (SPIDFilter.IsChecked == true )  
            {
                rel = allRelevant
                    .Where(h => h.Status != null &&
                          !( h.Status.IndexOf("sleeping", StringComparison.OrdinalIgnoreCase) >= 0))
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

                _spidBoxPositions[sp.Spid] = new Point(x + boxWidth / 2, y + boxHeight / 2);
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
                        Stroke = Brushes.Red,
                        StrokeThickness = 2,
                        StrokeDashArray = new DoubleCollection { 4, 2 }

                    };

                    SpidBoxesCanvas.Children.Insert(0, line);
                    //Float lien above boxes
                    Panel.SetZIndex(line, 1);


                    // Draw Arrows, maybe too much for now
                   //var angle = Math.Atan2(blockerPos.Y - waitPos.Y, blockerPos.X - waitPos.X);
                   //var arrowLength = 10;
                   //var arrowAngle = Math.PI / 6;
                   //
                   //var arrowX = blockerPos.X - 30 * Math.Cos(angle);
                   //var arrowY = blockerPos.Y - 30 * Math.Sin(angle);
                   //
                   //var arrow = new Polygon
                   //{
                   //    Fill = Brushes.Red,
                   //    Points = new PointCollection
                   //    {
                   //        new Point(arrowX, arrowY),
                   //        new Point(arrowX - arrowLength * Math.Cos(angle - arrowAngle), arrowY - arrowLength * Math.Sin(angle - arrowAngle)),
                   //        new Point(arrowX - arrowLength * Math.Cos(angle + arrowAngle), arrowY - arrowLength * Math.Sin(angle + arrowAngle))
                   //    }
                   //    
                   //};
                   //SpidBoxesCanvas.Children.Insert(0, arrow);
                   //Panel.SetZIndex(arrow, 1);

                }
            }
        }

        private Border CreateSpidBox(SpidHistory sp, bool isBlocker, bool isWaiting, double fixedWidth)
        {
            var borderColor = isBlocker ? Colors.Red : (sp.IsActive ? Color.FromRgb(0, 120, 212) : Color.FromRgb(200, 200, 200));
            var borderThickness = isBlocker ? 3 : (sp.IsActive ? 2 : 1);

            var brd = new Border
            {
                Width = fixedWidth,
                MinHeight = 50,
                Background = new SolidColorBrush(isBlocker ? Color.FromRgb(255, 240, 240) : (sp.IsActive ? Color.FromRgb(250, 250, 250) : Color.FromRgb(240, 240, 240))),
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
                Foreground = new SolidColorBrush(isBlocker ? Colors.Red : (sp.IsActive ? Colors.Black : Colors.Gray)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetRow(hdr, 0);
            g.Children.Add(hdr);

            var lastCpuPct = sp.CpuHistory.LastOrDefault();
            var cpuText = new TextBlock
            {
                Text = $"CPU: {lastCpuPct:F1}%",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 120, 212)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0)
            };
            Grid.SetRow(cpuText, 1);
            g.Children.Add(cpuText);

            var lastIoPct = sp.IoHistory.LastOrDefault();
            var ioText = new TextBlock
            {
                Text = $"I/O: {lastIoPct:F1}%",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(216, 59, 1)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 1, 0, 0)
            };
            Grid.SetRow(ioText, 2);
            g.Children.Add(ioText);

            brd.Child = g;

            var tooltipText = BuildSpidTooltip(sp, isBlocker, isWaiting);
            brd.ToolTip = new ToolTip
            {
                Content = new TextBlock
                {
                    Text = tooltipText,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 11
                },
                Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)),
                Foreground = Brushes.White,
                Padding = new Thickness(10),
                BorderThickness = new Thickness(0)
            };

            return brd;
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
                $"CPU %: {string.Join(" → ", sp.CpuHistory.Skip(Math.Max(0, sp.CpuHistory.Count - 5)).Select(v => v.ToString("F0")))}",
                $"I/O %: {string.Join(" → ", sp.IoHistory.Skip(Math.Max(0, sp.IoHistory.Count - 5)).Select(v => v.ToString("F0")))}"
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
        private string _programFilter = "";
        
        private void ProgramFilterText_TextChanged(object sender, TextChangedEventArgs e)
        {
            _programFilter = ProgramFilterText.Text?.Trim() ?? "";
            UpdateSpidBoxes();
        }

        private void ClearProgramFilter_Click(object sender, RoutedEventArgs e)
        {
            ProgramFilterText.Text = "";
            _programFilter = "";
            UpdateSpidBoxes();
        }


        //ClearSPIDFilter
        
            private void ToggleSPIDFilter_Checked(object sender, RoutedEventArgs e)
        {
            SPIDFilter.Content = "Not Showing Sleeping SPIDs";
            UpdateSpidBoxes();
        }

        private void ToggleSPIDFilter_Unchecked(object sender, RoutedEventArgs e)
        {
            SPIDFilter.Content = "Showing Sleeping SPIDs";
            UpdateSpidBoxes();
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
                MessageBox.Show("Please select a query from the grid first.", "No Selection", 
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
                MessageBox.Show("Please select a query from the grid first.", "No Selection",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        

        // Save Plan button handler


        protected override void OnClosed(EventArgs e) { StopMonitoring(); base.OnClosed(e); }
    }

    public class ServerItem
    {
        public string ConnectionString { get; set; } = "";
        public string ServerName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public bool IsConnectable { get; set; }
        public Brush TextColor { get; set; } = Brushes.Black;
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

        public void AddSample(double c, double i)
        {
            CpuHistory.Enqueue(c);
            IoHistory.Enqueue(i);
            while (CpuHistory.Count > 30) CpuHistory.Dequeue();
            while (IoHistory.Count > 30) IoHistory.Dequeue();
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
   //     public int ZIndex { get; set; }
   //     public double StrokeThickness { get; set; }
   // }
}