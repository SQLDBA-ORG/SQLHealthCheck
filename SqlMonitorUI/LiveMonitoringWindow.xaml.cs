using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Data.SqlClient;

namespace SqlMonitorUI
{
    public partial class LiveMonitoringWindow : Window
    {
        private readonly string _connectionString;
        private DispatcherTimer? _refreshTimer;
        private DispatcherTimer? _gcTimer;
        private bool _isRunning;
        private long _lastBatchReq, _lastTrans, _lastComp, _lastPhReads, _lastPhWrites;
        private DateTime _lastSampleTime = DateTime.MinValue;

        // Reduced history from 120 to 60 points
        private const int MaxHistoryPoints = 60;
        private readonly Dictionary<string, long[]> _waitHistory = new();
        private readonly Dictionary<string, int> _waitHistoryIndex = new();
        private readonly Dictionary<string, Color> _waitColors = new()
        {
            { "Locks", Color.FromRgb(220, 20, 60) }, { "Reads/Latches", Color.FromRgb(0, 120, 212) },
            { "Writes/I/O", Color.FromRgb(139, 0, 139) }, { "Network", Color.FromRgb(255, 140, 0) },
            { "Backup", Color.FromRgb(128, 128, 128) }, { "Memory", Color.FromRgb(16, 124, 16) },
            { "Parallelism", Color.FromRgb(107, 105, 214) }, { "Transaction Log", Color.FromRgb(216, 59, 1) }
        };

        // Cached brushes to avoid repeated allocations
        private readonly Dictionary<string, SolidColorBrush> _brushCache = new();
        private static readonly SolidColorBrush GridLineBrush = new SolidColorBrush(Color.FromRgb(230, 230, 230));
        private static readonly SolidColorBrush LabelBrush = new SolidColorBrush(Color.FromRgb(100, 100, 100));
        
        private Dictionary<string, long>? _baselineWaits;
        private Dictionary<string, long> _lastWaits = new();

        // Use array instead of dictionary for SPIDs (max 200)
        private readonly SpidHistory[] _spidHistories = new SpidHistory[500];
        private readonly HashSet<int> _activeSpidSet = new();

        // Reusable list for sessions
        private readonly List<SessionItem> _sessionsList = new(100);

        public LiveMonitoringWindow(string connectionString)
        {
            InitializeComponent();
            _connectionString = connectionString;
            
            // Initialize arrays instead of queues
            foreach (var key in _waitColors.Keys)
            {
                _waitHistory[key] = new long[MaxHistoryPoints];
                _waitHistoryIndex[key] = 0;
                _brushCache[key] = new SolidColorBrush(_waitColors[key]);
                _brushCache[key].Freeze(); // Freeze for performance
            }
            
            GridLineBrush.Freeze();
            LabelBrush.Freeze();
            
            BuildLegend();
            try { ServerNameText.Text = new SqlConnectionStringBuilder(connectionString).DataSource; } catch { ServerNameText.Text = "Unknown"; }
            
            // GC timer - more aggressive, every 15 seconds
            _gcTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _gcTimer.Tick += (s, e) => PerformMemoryCleanup();
            _gcTimer.Start();
            
            Loaded += (s, e) => StartMonitoring();
        }

        private void PerformMemoryCleanup()
        {
            // Clean up inactive SPIDs
            for (int i = 0; i < _spidHistories.Length; i++)
            {
                var h = _spidHistories[i];
                if (h != null && !h.IsActive && h.InactiveTicks > 20)
                {
                    _spidHistories[i] = null;
                }
            }
            
            // Force GC if over threshold
            var memUsed = GC.GetTotalMemory(false);
            if (memUsed > 150 * 1024 * 1024) // 150MB threshold
            {
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, true, true);
            }
            else if (memUsed > 80 * 1024 * 1024) // 80MB - light cleanup
            {
                GC.Collect(0, GCCollectionMode.Optimized, false);
            }
            
            // Update status with memory info
            StatusText.Text = $"Monitoring... (Mem: {memUsed / 1024 / 1024}MB)";
        }

        private void BuildLegend()
        {
            LegendPanel.Children.Clear();
            foreach (var kvp in _waitColors)
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 15, 5) };
                var brush = _brushCache[kvp.Key];
                sp.Children.Add(new Rectangle { Width = 14, Height = 14, Fill = brush, Margin = new Thickness(0, 0, 5, 0), RadiusX = 2, RadiusY = 2 });
                sp.Children.Add(new TextBlock { Text = kvp.Key, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
                LegendPanel.Children.Add(sp);
            }
        }

        private void StartStopButton_Click(object sender, RoutedEventArgs e) { if (_isRunning) StopMonitoring(); else StartMonitoring(); }

        private void StartMonitoring()
        {
            _isRunning = true;
            _baselineWaits = null;
            _lastWaits.Clear();
            _lastPhReads = 0; _lastPhWrites = 0;
            
            // Clear history arrays
            foreach (var key in _waitHistory.Keys)
            {
                Array.Clear(_waitHistory[key], 0, MaxHistoryPoints);
                _waitHistoryIndex[key] = 0;
            }
            
            StartStopButton.Content = "Stop";
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
            StartStopButton.Content = "Start";
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
            try
            {
                DataTable? metrics = null;
                DataTable? sessions = null;
                
                await System.Threading.Tasks.Task.Run(() =>
                {
                    using var conn = new SqlConnection(_connectionString);
                    conn.Open();
                    metrics = GetMetrics(conn);
                    sessions = GetSessions(conn);
                });
                
                if (metrics != null && sessions != null)
                {
                    UpdateMetrics(metrics);
                    UpdateSessions(sessions);
                    DrawWaitGraph();
                    UpdateSpidBoxes();
                    LastUpdateText.Text = $"Updated: {DateTime.Now:HH:mm:ss}";
                    
                    // Dispose DataTables immediately
                    metrics.Dispose();
                    sessions.Dispose();
                }
            }
            catch (Exception ex) { StatusText.Text = $"Error: {ex.Message}"; }
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
            var dt = new DataTable(); 
            using (var adapter = new SqlDataAdapter(cmd)) { adapter.Fill(dt); }
            return dt;
        }

        private DataTable GetSessions(SqlConnection conn)
        {
            const string q = "SELECT spid AS Spid,DB_NAME(dbid) AS [Database],status AS Status,CAST(cpu AS BIGINT) AS Cpu,CAST(physical_io AS BIGINT) AS PhysicalIo,hostname AS Hostname,program_name AS ProgramName FROM sys.sysprocesses WHERE spid>50 AND program_name NOT LIKE '%SQLMonitorUI%' ORDER BY cpu DESC";
            using var cmd = new SqlCommand(q, conn) { CommandTimeout = 30 };
            var dt = new DataTable(); 
            using (var adapter = new SqlDataAdapter(cmd)) { adapter.Fill(dt); }
            return dt;
        }

        private void UpdateMetrics(DataTable dt)
        {
            if (dt.Rows.Count == 0) return;
            var r = dt.Rows[0];
            var cpu = Convert.ToInt32(r["CPU"]);
            CpuText.Text = cpu.ToString();
            CpuText.Foreground = cpu > 80 ? Brushes.OrangeRed : Brushes.DodgerBlue;

            var now = DateTime.Now;
            var br = Convert.ToInt64(r["BatchReq"]); var tr = Convert.ToInt64(r["Trans"]); var sc = Convert.ToInt64(r["SqlComp"]);
            var phReads = Convert.ToInt64(r["PhReads"]); var phWrites = Convert.ToInt64(r["PhWrites"]);
            
            if (_lastSampleTime != DateTime.MinValue)
            {
                var el = (now - _lastSampleTime).TotalSeconds;
                if (el > 0) 
                { 
                    BatchReqText.Text = ((br-_lastBatchReq)/el).ToString("N0"); 
                    TransText.Text = ((tr-_lastTrans)/el).ToString("N0"); 
                    CompilationsText.Text = ((sc-_lastComp)/el).ToString("N0");
                    ReadsText.Text = (phReads - _lastPhReads).ToString("N0");
                    WritesText.Text = (phWrites - _lastPhWrites).ToString("N0");
                }
            }
            _lastBatchReq = br; _lastTrans = tr; _lastComp = sc; 
            _lastPhReads = phReads; _lastPhWrites = phWrites;
            _lastSampleTime = now;

            var currentWaits = new Dictionary<string,long>(8){
                {"Locks",Convert.ToInt64(r["Locks"])},{"Reads/Latches",Convert.ToInt64(r["Reads"])},
                {"Writes/I/O",Convert.ToInt64(r["Writes"])},{"Network",Convert.ToInt64(r["Network"])},
                {"Backup",Convert.ToInt64(r["Backup"])},{"Memory",Convert.ToInt64(r["Memory"])},
                {"Parallelism",Convert.ToInt64(r["Parallelism"])},{"Transaction Log",Convert.ToInt64(r["TransactionLog"])}
            };

            if (_baselineWaits == null)
            {
                _baselineWaits = new Dictionary<string, long>(currentWaits);
                _lastWaits = new Dictionary<string, long>(currentWaits);
            }

            var ws = new List<WaitStatItem>(8);
            long tot = 0;
            
            foreach (var key in currentWaits.Keys)
            {
                var delta = currentWaits[key] - _baselineWaits[key];
                tot += delta;
                
                // Store delta in circular buffer
                var deltaFromLast = currentWaits[key] - (_lastWaits.ContainsKey(key) ? _lastWaits[key] : currentWaits[key]);
                var arr = _waitHistory[key];
                var idx = _waitHistoryIndex[key];
                arr[idx] = Math.Max(0, deltaFromLast);
                _waitHistoryIndex[key] = (idx + 1) % MaxHistoryPoints;
                
                if (delta > 0)
                    ws.Add(new WaitStatItem{WaitType=key,WaitTimeMs=delta,Color=_brushCache[key]});
            }
            
            _lastWaits = currentWaits;
            
            // Calculate percentages
            foreach (var w in ws) w.Percentage = tot > 0 ? (double)w.WaitTimeMs/tot*100 : 0;
            
            WaitStatsGrid.ItemsSource = ws.OrderByDescending(w=>w.WaitTimeMs).ToList();
        }

        private void DrawWaitGraph()
        {
            WaitGraphCanvas.Children.Clear();
            var w = WaitGraphCanvas.ActualWidth; var h = WaitGraphCanvas.ActualHeight;
            if (w <= 0 || h <= 0) return;

            // Find max value
            long maxVal = 1;
            foreach (var arr in _waitHistory.Values)
                foreach (var v in arr) if (v > maxVal) maxVal = v;

            var graphLeft = 55.0;
            var graphWidth = w - graphLeft - 5;
            var graphHeight = h - 20;
            var xStep = graphWidth / (MaxHistoryPoints - 1);

            // Y axis labels (simplified - only 3 labels)
            for (int i = 0; i <= 2; i++)
            {
                var yVal = maxVal * (2 - i) / 2;
                var yPos = i * graphHeight / 2;
                var label = new TextBlock { Text = FormatMs(yVal), FontSize = 9, Foreground = LabelBrush };
                Canvas.SetLeft(label, 2); Canvas.SetTop(label, yPos - 6);
                WaitGraphCanvas.Children.Add(label);
                var gridLine = new Line { X1 = graphLeft, Y1 = yPos, X2 = w - 5, Y2 = yPos, Stroke = GridLineBrush, StrokeThickness = 1 };
                WaitGraphCanvas.Children.Add(gridLine);
            }

            // Draw lines using simple polylines (faster than Bezier)
            foreach (var kv in _waitHistory)
            {
                var arr = kv.Value;
                var startIdx = _waitHistoryIndex[kv.Key];
                var hasData = false;
                for (int i = 0; i < MaxHistoryPoints && !hasData; i++) if (arr[i] > 0) hasData = true;
                if (!hasData) continue;

                var pl = new Polyline { Stroke = _brushCache[kv.Key], StrokeThickness = 1.5 };
                for (int i = 0; i < MaxHistoryPoints; i++)
                {
                    var dataIdx = (startIdx + i) % MaxHistoryPoints;
                    var x = graphLeft + i * xStep;
                    var y = graphHeight - (arr[dataIdx] / (double)maxVal * graphHeight);
                    pl.Points.Add(new Point(x, Math.Max(0, y)));
                }
                WaitGraphCanvas.Children.Add(pl);
            }
        }

        private string FormatMs(long ms)
        {
            if (ms >= 1000000) return $"{ms/1000000}M";
            if (ms >= 1000) return $"{ms/1000}K";
            return ms.ToString();
        }

        private void UpdateSessions(DataTable dt)
        {
            _sessionsList.Clear();
            _activeSpidSet.Clear();
            long totCpu = 0, totIo = 0;
            
            foreach (DataRow r in dt.Rows)
            {
                var spid = Convert.ToInt32(r["Spid"]);
                var cpu = Convert.ToInt64(r["Cpu"]); 
                var io = Convert.ToInt64(r["PhysicalIo"]);
                totCpu += cpu; totIo += io;
                _activeSpidSet.Add(spid);
                _sessionsList.Add(new SessionItem{Spid=spid,Database=r["Database"]?.ToString()??"",Status=r["Status"]?.ToString()??"",Cpu=cpu,PhysicalIo=io,Hostname=r["Hostname"]?.ToString()??"",ProgramName=r["ProgramName"]?.ToString()??""});
            }
            
            // Update SPID histories
            for (int i = 0; i < _spidHistories.Length; i++)
            {
                var h = _spidHistories[i];
                if (h != null && !_activeSpidSet.Contains(h.Spid))
                {
                    h.IsActive = false;
                    h.InactiveTicks++;
                }
            }
            
            foreach (var s in _sessionsList)
            {
                var idx = s.Spid % _spidHistories.Length;
                var h = _spidHistories[idx];
                if (h == null || h.Spid != s.Spid)
                {
                    _spidHistories[idx] = h = new SpidHistory { Spid = s.Spid };
                }
                
                var cpuDelta = s.Cpu - h.LastCpu;
                var ioDelta = s.PhysicalIo - h.LastIo;
                if (cpuDelta > 0) h.AddCpuSample(cpuDelta);
                if (ioDelta > 0) h.AddIoSample(ioDelta);
                h.LastCpu = s.Cpu;
                h.LastIo = s.PhysicalIo;
                h.IsActive = true;
                h.InactiveTicks = 0;
            }
            
            SessionsGrid.ItemsSource = null;
            SessionsGrid.ItemsSource = _sessionsList;
            SessionCountText.Text = $" ({_sessionsList.Count})";
        }

        private void UpdateSpidBoxes()
        {
            SpidBoxesPanel.Children.Clear();
            
            // Collect and sort active SPIDs
            var activeList = new List<SpidHistory>(50);
            for (int i = 0; i < _spidHistories.Length; i++)
            {
                var h = _spidHistories[i];
                if (h != null && (h.IsActive || h.InactiveTicks <= 20))
                    activeList.Add(h);
            }
            
            activeList.Sort((a, b) => {
                if (a.IsActive != b.IsActive) return b.IsActive.CompareTo(a.IsActive);
                var ioCmp = b.IoMovingAvg.CompareTo(a.IoMovingAvg);
                return ioCmp != 0 ? ioCmp : b.CpuMovingAvg.CompareTo(a.CpuMovingAvg);
            });
            
            // Limit to 30 boxes max
            var count = Math.Min(activeList.Count, 30);
            for (int i = 0; i < count; i++)
                SpidBoxesPanel.Children.Add(CreateSpidBox(activeList[i]));
        }

        private static readonly SolidColorBrush ActiveBg = new SolidColorBrush(Color.FromRgb(250,250,250));
        private static readonly SolidColorBrush InactiveBg = new SolidColorBrush(Color.FromRgb(230,230,230));
        private static readonly SolidColorBrush ActiveBorder = new SolidColorBrush(Color.FromRgb(0,120,212));
        private static readonly SolidColorBrush InactiveBorder = new SolidColorBrush(Color.FromRgb(180,180,180));
        private static readonly SolidColorBrush ActiveText = new SolidColorBrush(Colors.Black);
        private static readonly SolidColorBrush InactiveText = new SolidColorBrush(Color.FromRgb(150,150,150));
        private static readonly SolidColorBrush IoColor = new SolidColorBrush(Color.FromRgb(216,59,1));
        private static readonly SolidColorBrush CpuColor = new SolidColorBrush(Color.FromRgb(0,120,212));
        private static readonly SolidColorBrush GreyColor = new SolidColorBrush(Color.FromRgb(180,180,180));

        static LiveMonitoringWindow()
        {
            ActiveBg.Freeze(); InactiveBg.Freeze(); ActiveBorder.Freeze(); InactiveBorder.Freeze();
            ActiveText.Freeze(); InactiveText.Freeze(); IoColor.Freeze(); CpuColor.Freeze(); GreyColor.Freeze();
        }

        private Border CreateSpidBox(SpidHistory sp)
        {
            var isGrey = !sp.IsActive || sp.InactiveTicks > 20;
            var brd = new Border{
                Width=90, Height=55,
                Background = isGrey ? InactiveBg : ActiveBg,
                BorderBrush = isGrey ? InactiveBorder : ActiveBorder,
                BorderThickness=new Thickness(1),
                CornerRadius=new CornerRadius(4),
                Margin=new Thickness(3),
                Padding=new Thickness(5)
            };
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(new TextBlock{Text=$"SPID {sp.Spid}",FontSize=11,FontWeight=FontWeights.SemiBold,HorizontalAlignment=HorizontalAlignment.Center,Foreground=isGrey?InactiveText:ActiveText});
            stack.Children.Add(new TextBlock{Text=$"I/O: {sp.IoMovingAvg:N0}",FontSize=9,Foreground=isGrey?GreyColor:IoColor,HorizontalAlignment=HorizontalAlignment.Center});
            stack.Children.Add(new TextBlock{Text=$"CPU: {sp.CpuMovingAvg:N0}",FontSize=9,Foreground=isGrey?GreyColor:CpuColor,HorizontalAlignment=HorizontalAlignment.Center});
            brd.Child = stack;
            return brd;
        }

        protected override void OnClosed(EventArgs e) 
        { 
            StopMonitoring(); 
            _gcTimer?.Stop();
            _gcTimer = null;
            Array.Clear(_spidHistories, 0, _spidHistories.Length);
            _activeSpidSet.Clear();
            _sessionsList.Clear();
            foreach (var arr in _waitHistory.Values) Array.Clear(arr, 0, arr.Length);
            _baselineWaits?.Clear();
            _lastWaits.Clear();
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            base.OnClosed(e); 
        }
    }

    public class WaitStatItem { public string WaitType { get; set; } = ""; public long WaitTimeMs { get; set; } public double Percentage { get; set; } public SolidColorBrush Color { get; set; } = Brushes.Gray; }
    public class SessionItem { public int Spid { get; set; } public string Database { get; set; } = ""; public string Status { get; set; } = ""; public long Cpu { get; set; } public long PhysicalIo { get; set; } public string Hostname { get; set; } = ""; public string ProgramName { get; set; } = ""; }
    public class SpidHistory 
    { 
        public int Spid; 
        public bool IsActive; 
        public long LastCpu, LastIo;
        public int InactiveTicks;
        private readonly long[] _cpuSamples = new long[5];
        private readonly long[] _ioSamples = new long[5];
        private int _cpuIdx, _ioIdx, _cpuCount, _ioCount;
        
        public void AddCpuSample(long v) { _cpuSamples[_cpuIdx] = v; _cpuIdx = (_cpuIdx + 1) % 5; if (_cpuCount < 5) _cpuCount++; }
        public void AddIoSample(long v) { _ioSamples[_ioIdx] = v; _ioIdx = (_ioIdx + 1) % 5; if (_ioCount < 5) _ioCount++; }
        
        public double CpuMovingAvg { get { if (_cpuCount == 0) return 0; long sum = 0; for (int i = 0; i < _cpuCount; i++) sum += _cpuSamples[i]; return sum / (double)_cpuCount; } }
        public double IoMovingAvg { get { if (_ioCount == 0) return 0; long sum = 0; for (int i = 0; i < _ioCount; i++) sum += _ioSamples[i]; return sum / (double)_ioCount; } }
    }
}
