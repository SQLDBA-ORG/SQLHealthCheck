using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Numerics;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security.Policy;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using static System.Windows.Forms.AxHost;
//using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace SqlMonitorUI
{
    public partial class LiveMonitoringWindow : Window
    {
        private readonly List<string> _connectionStrings;
        private string? _selectedConnectionString;
        private DispatcherTimer? _refreshTimer;
        private bool _isRunning;
        private long _lastBatchReq, _lastTrans, _lastComp, _lastReads, _lastWrites,_lastPoisonWaits,_lastPoisonWaitSerializable, _lastPoisonWaitCMEM ;
        private DateTime _lastSampleTime = DateTime.MinValue;
        private int _tickCount = 0;
        private int TopSessions = 50;
        private Boolean ShowSleepingSPIDs = true;
        private string _programFilter = "";

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
        }

        private void StopMonitoring()
        {
            _isRunning = false;
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

            try
            {
                _tickCount++;
                var shouldRefreshTopQueries = _tickCount % 30 == 0 || _tickCount == 1;

                await System.Threading.Tasks.Task.Run(() =>
                {
                    using var conn = new SqlConnection(_selectedConnectionString);
                    conn.Open();

                    DataTable? topQueries = null;
                    //DataTable? GetDriveLatencyTable = null;
                    //DataTable? GetServerDetailsTable = null;
                    if (shouldRefreshTopQueries)
                    {
                        //ResetMonitoringState();
                        topQueries = GetTopQueries(conn);
                        var GetDriveLatencyTable = GetDriveLatency(conn);
                        var GetServerDetailsTable = GetServerDetails(conn);

                        UpdateDriveLatency(GetDriveLatencyTable);
                        UpdateGetServerDetails(GetServerDetailsTable);
                        //Let's also do some Garbage Collection every 30 ticks
                        GC.Collect(GC.MaxGeneration, GCCollectionMode.Default, true, true);
                        GC.WaitForPendingFinalizers();
                    }

                    Dispatcher.Invoke(() =>
                    {
                        //DataTable? GetMetricsTable = null;
                        //DataTable? UpdateMetricsTable = null;
                        //DataTable? GetBlockingInfoTable = null;

                        var GetMetricsTable =GetMetrics(conn);
                        var UpdateSessionsTable = GetSessions(conn);
                        var GetBlockingInfoTable = GetBlockingInfo(conn);

                        UpdateMetrics(GetMetricsTable);
                        UpdateSessions(UpdateSessionsTable);
                        UpdateBlocking(GetBlockingInfoTable);


                        DrawWaitGraph();
                        UpdateSpidBoxes();
                        if (topQueries != null)
                            UpdateTopQueries(topQueries);
                        LastUpdateText.Text = $"Updated: {DateTime.Now:HH:mm:ss} (tick {_tickCount})";
                    });
                    conn.Close();
                });
            }
            catch (Exception ex) { Dispatcher.Invoke(() => { StatusText.Text = $"Error: {ex.Message}"; }); }

        }

        private DataTable GetMetrics(SqlConnection conn)
        {
            const string q = @"DECLARE @BR BIGINT, @SC BIGINT, @TR BIGINT, @cpu INT
SELECT @BR=ISNULL(SUM(CONVERT(BIGINT,cntr_value)),0) FROM sys.dm_os_performance_counters WITH(NOLOCK) WHERE LOWER(object_name) LIKE '%sql statistics%' AND LOWER(counter_name)='batch requests/sec'
SELECT @SC=ISNULL(SUM(CONVERT(BIGINT,cntr_value)),0) FROM sys.dm_os_performance_counters WITH(NOLOCK) WHERE LOWER(object_name) LIKE '%sql statistics%' AND LOWER(counter_name)='sql compilations/sec'
SELECT @TR=ISNULL(SUM(CONVERT(BIGINT,cntr_value)),0) FROM sys.dm_os_performance_counters WITH(NOLOCK) WHERE LOWER(object_name) LIKE '%databases%' AND LOWER(counter_name)='transactions/sec' AND LOWER(instance_name)<>'_total'
SELECT TOP 1 @cpu=CONVERT(XML,record).value('(./Record/SchedulerMonitorEvent/SystemHealth/ProcessUtilization)[1]','int') FROM sys.dm_os_ring_buffers WITH(NOLOCK) WHERE ring_buffer_type='RING_BUFFER_SCHEDULER_MONITOR' ORDER BY timestamp DESC
SELECT ISNULL(@cpu,0) AS CPU,
SUM(CONVERT(BIGINT,CASE WHEN wait_type LIKE 'LCK%' THEN wait_time_ms-signal_wait_time_ms ELSE 0 END)) AS Locks,
SUM(CONVERT(BIGINT,CASE WHEN wait_type LIKE 'LATCH%' OR wait_type LIKE 'PAGELATCH%' OR wait_type LIKE 'PAGEIOLATCH%' THEN wait_time_ms-signal_wait_time_ms ELSE 0 END)) AS Reads,
SUM(CONVERT(BIGINT,CASE WHEN wait_type LIKE '%IO_COMPLETION%' OR wait_type='WRITELOG' THEN wait_time_ms-signal_wait_time_ms ELSE 0 END)) AS Writes,
SUM(CONVERT(BIGINT,CASE WHEN wait_type IN('NETWORKIO','OLEDB','ASYNC_NETWORK_IO') THEN wait_time_ms-signal_wait_time_ms ELSE 0 END)) AS Network,
SUM(CONVERT(BIGINT,CASE WHEN wait_type LIKE 'BACKUP%' THEN wait_time_ms-signal_wait_time_ms ELSE 0 END)) AS [Backup],
SUM(CONVERT(BIGINT,CASE WHEN wait_type='CMEMTHREAD' OR wait_type LIKE 'RESOURCE_SEMAPHORE%' THEN wait_time_ms-signal_wait_time_ms ELSE 0 END)) AS Memory,
SUM(CONVERT(BIGINT,CASE WHEN wait_type IN('CXPACKET','EXCHANGE') THEN wait_time_ms-signal_wait_time_ms ELSE 0 END)) AS Parallelism,
SUM(CONVERT(BIGINT,CASE WHEN wait_type IN('LOGBUFFER','LOGMGR','WRITELOG') THEN wait_time_ms-signal_wait_time_ms ELSE 0 END)) AS TransactionLog,
SUM(CONVERT(BIGINT,CASE WHEN wait_type IN('IO_QUEUE_LIMIT', 'IO_RETRY', 'LOG_RATE_GOVERNOR', 'POOL_LOG_RATE_GOVERNOR', 'PREEMPTIVE_DEBUG', 'RESMGR_THROTTLED', 'RESOURCE_SEMAPHORE', 'RESOURCE_SEMAPHORE_QUERY_COMPILE','SE_REPL_CATCHUP_THROTTLE','SE_REPL_COMMIT_ACK','SE_REPL_COMMIT_TURN','SE_REPL_ROLLBACK_ACK','SE_REPL_SLOW_SECONDARY_THROTTLE','THREADPOOL')THEN wait_time_ms-signal_wait_time_ms ELSE 0 END)) AS PoisonWaits,
SUM(CONVERT(BIGINT,CASE WHEN wait_type IN('LCK_M_RS_S', 'LCK_M_RS_U', 'LCK_M_RIn_NL','LCK_M_RIn_S', 'LCK_M_RIn_U','LCK_M_RIn_X', 'LCK_M_RX_S', 'LCK_M_RX_U','LCK_M_RX_X')THEN wait_time_ms-signal_wait_time_ms ELSE 0 END)) AS 'Poison Serializable Locking',
SUM(CONVERT(BIGINT,CASE WHEN wait_type = 'CMEMTHREAD' THEN wait_time_ms-signal_wait_time_ms ELSE 0 END)) AS 'Poison CMEMTHREAD and NUMA',
	
@@TOTAL_READ AS PhReads,@@TOTAL_WRITE AS PhWrites,@BR AS BatchReq,@SC AS SqlComp,@TR AS Trans 
 
FROM sys.dm_os_wait_stats WITH(NOLOCK)";
            using var cmd = new SqlCommand(q, conn) { CommandTimeout = 30 };
            var dt = new DataTable();
            try { new SqlDataAdapter(cmd).Fill(dt); } catch { /* Ignore empty metrics */ }
            return dt;
        }

        private DataTable GetSessions(SqlConnection conn)
        {
            string q = "SELECT TOP " + TopSessions.ToString();
            q += @"s.spid AS Spid,
                DB_NAME(s.dbid) AS [Database],
                status AS Status,
                CAST(cpu AS BIGINT) AS Cpu,
                CAST(physical_io AS BIGINT) AS PhysicalIo,
                hostname AS Hostname,
                program_name AS ProgramName,
                loginame AS LoginName,
                cmd AS Command,
                DATEDIFF(SECOND, last_batch, GETDATE()) AS IdleSeconds
                , ISNULL(SomeText.text,'') [text]
                , s.blocked
            FROM sys.sysprocesses s
            LEFT OUTER JOIN
            (
            SELECT spid AS Spid, e.text
            FROM sys.sysprocesses s
            CROSS APPLY sys.dm_exec_sql_text(sql_handle) e
            ) SomeText ON SomeText.spid = s.spid

            WHERE s.spid>50 AND program_name NOT LIKE '%SQLMonitorUI%'
            AND (hostname <> ''";
            if (!string.IsNullOrEmpty(_programFilter)) 
            {
                q += "AND program_name LIKE '%"+ _programFilter + "%')";
            }
            else
            {
                q += "AND program_name <> '')";
            }


            if (ShowSleepingSPIDs == false)
            {
                q += "AND status <> 'sleeping'";
            }

            q += @"ORDER BY (ISNULL(cpu,0) + ISNULL(physical_io,0)) DESC";
            using var cmd = new SqlCommand(q, conn) { CommandTimeout = 30 };
            var dt = new DataTable();
            try { new SqlDataAdapter(cmd).Fill(dt); } catch { /* Ignore empty sessions from filters */ }
            return dt;
        }

        private DataTable GetDriveLatency(SqlConnection conn)
        {

            string q = @"DECLARE @dynamicSQL NVARCHAR(4000)
            DECLARE @DaysUptime NUMERIC(23, 2);
            SELECT @DaysUptime = CAST(DATEDIFF(HOUR, create_date, GETDATE()) / 24.AS NUMERIC(23, 2))
            FROM sys.databases
            WHERE  database_id = 2;

            IF @DaysUptime = 0
                SET @DaysUptime = .01;
            DECLARE @fixeddrives TABLE (drive[NVARCHAR](5), FreeSpaceMB MONEY)
            SET @dynamicSQL = 'EXEC xp_fixeddrives ';
                        INSERT INTO @fixeddrives
                        EXEC SP_EXECUTESQL @dynamicSQL

            SELECT
             LEFT(mf.physical_name, 2) + '\' [Drive]
		    , SUM(io_stall) / SUM(num_of_reads + num_of_writes)  'Latency(ms)'
		    , (CONVERT(MONEY, SUM([num_of_reads])) + SUM([num_of_writes])) * 8 / 1024 / 1024 / CONVERT(MONEY, @DaysUptime) 'GB/day'
		    , CONVERT([VARCHAR](20), MAX(CAST(fd.FreeSpaceMB / 1024 as decimal(20, 2)))) + 'GB'[Free space]
		    , CASE WHEN SUM(num_of_reads) = 0 THEN '0' ELSE CONVERT([VARCHAR] (25),SUM(io_stall_read_ms) / SUM(num_of_reads)) END  'ReadLatency(ms)'
		    , CASE WHEN SUM(num_of_writes) = 0 THEN '0' ELSE CONVERT([VARCHAR] (25),SUM(io_stall_write_ms) / SUM(num_of_writes)) 
		    END 'WriteLatency(ms)'
            FROM[sys].dm_io_virtual_file_stats(NULL, NULL) AS vfs
            INNER JOIN[sys].master_files AS mf
            ON vfs.database_id = mf.database_id AND vfs.file_id = mf.file_id
            INNER JOIN @fixeddrives fd ON fd.drive COLLATE DATABASE_DEFAULT = LEFT(mf.physical_name, 1) COLLATE DATABASE_DEFAULT
            GROUP BY LEFT(mf.physical_name, 2);";
            using var cmd = new SqlCommand(q, conn) { CommandTimeout = 30 };
            var dt = new DataTable();
            try { new SqlDataAdapter(cmd).Fill(dt); } catch { /* Ignore query errors */ }
            return dt;
        }

        private DataTable GetServerDetails(SqlConnection conn)
        {

            string q = @"

            DECLARE @CPUcount INT;
            DECLARE @CPUsocketcount INT;
            DECLARE @CPUHyperthreadratio MONEY ;
            DECLARE @totalMemoryGB MONEY 
            DECLARE @AvailableMemoryGB MONEY 
            DECLARE @UsedMemory MONEY ;
            DECLARE @MemoryStateDesc [NVARCHAR] (50);
            DECLARE @VMType [NVARCHAR] (200)
            DECLARE @ServerType [NVARCHAR] (20);
            DECLARE @MaxRamServer INT
            DECLARE @SQLVersion INT;

            SELECT @VMType = RIGHT(@@version,CHARINDEX('(',REVERSE(@@version)));


            SELECT @CPUcount = cpu_count 
            , @CPUsocketcount = [cpu_count] / [hyperthread_ratio]
            , @CPUHyperthreadratio = [hyperthread_ratio]
            FROM [sys].dm_os_sys_info;
            EXEC sp_executesql N'SELECT @_UsedMemory =  CONVERT(MONEY,physical_memory_in_use_kb)/1024 /1000 FROM [sys].dm_os_process_memory WITH (NOLOCK) OPTION (RECOMPILE)'
            , N'@_UsedMemory MONEY  OUTPUT'
            , @_UsedMemory = @UsedMemory OUTPUT;

            EXEC sp_executesql N'SELECT @_totalMemoryGB = CONVERT(MONEY,total_physical_memory_kb)/1024/1000 FROM [sys].dm_os_sys_memory WITH (NOLOCK) OPTION (RECOMPILE)'
            , N'@_totalMemoryGB MONEY  OUTPUT'
            , @_totalMemoryGB = @totalMemoryGB OUTPUT;

            EXEC sp_executesql N'SELECT @_AvailableMemoryGB =  CONVERT(MONEY,available_physical_memory_kb)/1024/1000 FROM [sys].dm_os_sys_memory WITH (NOLOCK) OPTION (RECOMPILE);'
            , N'@_AvailableMemoryGB MONEY  OUTPUT'
            , @_AvailableMemoryGB = @AvailableMemoryGB OUTPUT;

            EXEC sp_executesql N'SELECT @_MemoryStateDesc =   system_memory_state_desc from  [sys].dm_os_sys_memory;'
            , N'@_MemoryStateDesc [NVARCHAR] (50) OUTPUT'
            , @_MemoryStateDesc = @MemoryStateDesc OUTPUT;

            SELECT 
            @@SERVERNAME [ServerName]
            ,ServerProperty('edition') [Edition]
            , [Sockets] =  ISNULL(replace(replace(replace(replace(CONVERT([NVARCHAR],CONVERT([VARCHAR](20),(@CPUsocketcount ) )), CHAR(9), ' '),CHAR(10),' '), CHAR(13), ' '), '  ',' '),'')
            , [Virtual CPUs] =  ISNULL(replace(replace(replace(replace(CONVERT([NVARCHAR],CONVERT([VARCHAR](20),@CPUcount   )), CHAR(9), ' '),CHAR(10),' '), CHAR(13), ' '), '  ',' ') ,'')
            , [VM Type] =  ISNULL(replace(replace(replace(replace(CONVERT([NVARCHAR],ISNULL(@VMType,'')), CHAR(9), ' '),CHAR(10),' '), CHAR(13), ' '), '  ',' ') ,'')
            , [MemoryGB] = ISNULL(CONVERT([VARCHAR](20), CONVERT(MONEY,CONVERT(FLOAT,@totalMemoryGB))),'')
            , [SQL Allocated] =ISNULL(CONVERT([VARCHAR](20), CONVERT(MONEY,CONVERT(FLOAT,@UsedMemory))) ,'')
            , [Used by SQL]= ISNULL(CONVERT([VARCHAR](20), CONVERT(FLOAT,@UsedMemory)),'')
            , [Memory State]= ISNULL((@MemoryStateDesc),'')  
            , [ServerName]= ISNULL(replace(replace(replace(replace(CONVERT([NVARCHAR],SERVERPROPERTY('ServerName')), CHAR(9), ' '),CHAR(10),' '), CHAR(13), ' '), '  ',' ') ,'')
            , [Version]= ISNULL(replace(replace(replace(replace(CONVERT([NVARCHAR],LEFT( @@version, PATINDEX('%-%',( @@version))-2) ), CHAR(9), ' '),CHAR(10),' '), CHAR(13), ' '), '  ',' ') ,'')
            , [BuildNr]= ISNULL(replace(replace(replace(replace(CONVERT([NVARCHAR],SERVERPROPERTY('ProductVersion')), CHAR(9), ' '),CHAR(10),' '), CHAR(13), ' '), '  ',' ') ,'')
            , [OS]=  ISNULL(replace(replace(replace(replace(CONVERT([NVARCHAR],RIGHT( @@version, LEN(@@version) - PATINDEX('% on %',( @@version))-3) ), CHAR(9), ' '),CHAR(10),' '), CHAR(13), ' '), '  ',' ') ,'')
            , [Edition]= ISNULL(replace(replace(replace(replace(CONVERT([NVARCHAR],SERVERPROPERTY('Edition')), CHAR(9), ' '),CHAR(10),' '), CHAR(13), ' '), '  ',' ') ,'')
            , [HADR]= ISNULL(replace(replace(replace(replace(CONVERT([NVARCHAR],SERVERPROPERTY('IsHadrEnabled')), CHAR(9), ' '),CHAR(10),' '), CHAR(13), ' '), '  ',' ') ,'')
            , [SA]= ISNULL(replace(replace(replace(replace(CONVERT([NVARCHAR],SERVERPROPERTY('IsIntegratedSecurityOnly' )), CHAR(9), ' '),CHAR(10),' '), CHAR(13), ' '), '  ',' '),'')
            , [Level]= ISNULL(replace(replace(replace(replace(CONVERT([NVARCHAR],SERVERPROPERTY('ProductLevel')), CHAR(9), ' '),CHAR(10),' '), CHAR(13), ' '), '  ',' '),'')
	            FROM [sys].[dm_os_sys_info] OPTION (RECOMPILE);";
            using var cmd = new SqlCommand(q, conn) { CommandTimeout = 30 };
            var dt = new DataTable();
            try { new SqlDataAdapter(cmd).Fill(dt); } catch { /* Ignore query errors */ }
            return dt;
        }
        private DataTable GetBlockingInfo(SqlConnection conn)
        {
             string q = "SELECT TOP " + TopSessions.ToString();
                q += @"t1.resource_type AS lock_type,
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
            INNER JOIN sys.dm_os_waiting_tasks t2 ON t1.lock_owner_address = t2.resource_address
            ORDER BY t2.wait_duration_ms DESC";

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

        private void UpdateDriveLatency(DataTable dt)
        {
            if (dt.Rows.Count == 0) return;
            var r = dt.Rows[0];
            var drv = r["Drive"];  
            var lat = Convert.ToInt32(r["Latency(ms)"]);
            var wl = Convert.ToDouble(r["GB/day"]);
            var fs = r["Free space"];
            var rlat = Convert.ToInt32(r["ReadLatency(ms)"]);
            var wlat = Convert.ToInt32(r["WriteLatency(ms)"]);
            // C:\	2   6.5883  965.21GB    5   0
            dt.Dispose();
        }
        private void UpdateGetServerDetails(DataTable dt)
        {
            if (dt.Rows.Count == 0) return;
            var r = dt.Rows[0];
            var sn = r["ServerName"];
            var ed = r["Edition"];
            var sckt =     Convert.ToInt32(r["Sockets"]);
            var cpus  = Convert.ToInt32(r["Virtual CPUs"]);
            var vm  = r["VM Type"];
            var ram  = Convert.ToDouble(r["MemoryGB"]);
            var all = Convert.ToDouble(r["SQL Allocated"]);
            var usd = Convert.ToDouble(r["Used by SQL"]);
            var ms = r["Memory State"]; 
            var vs = r["Version"];
            var bld = r["BuildNr"]; 
            var os = r["OS"];
            var ha = r["HADR"];
            var sa = r["SA"];
            var lvl = r["Level"];
            //MSI Developer Edition(64 - bit)	1   22(Hypervisor)    32.25   0.55    0.5489  Available physical memory is high MSI Microsoft SQL Server 2022(RT   16.0.4230.2 Windows 10 Home 10.0 < X64 > (Bu  Developer Edition(64 - bit)  0   1   RTM


            dt.Dispose();

        }
        private void UpdateMetrics(DataTable dt)
        {
             
            if (dt.Rows.Count == 0) return;
            var r = dt.Rows[0];
            
                
            var cpu = Convert.ToInt32(r["CPU"]);
            var now = DateTime.Now;
            var br = Convert.ToInt32(r["BatchReq"]);
            var tr = Convert.ToInt32(r["Trans"]);
            var sc = Convert.ToInt32(r["SqlComp"]);
            var reads = Convert.ToInt32(r["PhReads"]);
            var writes = Convert.ToInt32(r["PhWrites"]);


     
            var lcks = Convert.ToInt32(r["Locks"]);
            var rds = Convert.ToInt32(r["Reads"]);
            var wrts = Convert.ToInt32(r["Writes"]);
            var nw = Convert.ToInt32(r["Network"]);
            var bkp = Convert.ToInt32(r["Backup"]);
            var mm = Convert.ToInt32(r["Memory"]);
            var cx = Convert.ToInt32(r["Parallelism"]);
            var tlog = Convert.ToInt32(r["TransactionLog"]);
            var pw = Convert.ToInt32(r["PoisonWaits"]);
            var pws = Convert.ToInt32(r["Poison Serializable Locking"]);
            var pwn = Convert.ToInt32(r["Poison CMEMTHREAD and NUMA"]);

            
            CpuText.Text = cpu.ToString();
            CpuText.Foreground = new SolidColorBrush(cpu > 80 ? System.Windows.Media.Color.FromRgb(216, 59, 1) : System.Windows.Media.Color.FromRgb(0, 120, 212));


            if (_lastSampleTime != DateTime.MinValue)
            {
                var el = (now - _lastSampleTime).TotalSeconds;
                if (el > 0)
                {
                    if(((br - _lastBatchReq) / el) <= 0)
                    {
                        BatchReqText.Text = 0.ToString("N0");
                    }
                    else
                        BatchReqText.Text = ((br - _lastBatchReq) / el).ToString("N0");
                    if(((tr - _lastTrans) / el) <= 0)
                    {
                        TransText.Text = 0.ToString("N0");
                    }
                    else
                        TransText.Text = ((tr - _lastTrans) / el).ToString("N0");

                    if(((sc - _lastComp) / el) <=0 )
                    {
                        CompilationsText.Text = 0.ToString("N0");
                    }
                    else
                        CompilationsText.Text = ((sc - _lastComp) / el).ToString("N0");
                    // Page Reads and Writes as deltas per second
                    if(((reads - _lastReads) / el) <= 0)
                    {
                        ReadsText.Text = 0.ToString("N0");
                    }
                    else
                        ReadsText.Text = ((reads - _lastReads) / el).ToString("N0");
                    if(((writes - _lastWrites) / el) <= 0)
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

            // var lcks = Convert.ToInt32(r["Locks"]);
            // var rds = Convert.ToInt32(r["Reads"]);
            //var wrts = Convert.ToInt32(r["Writes"]);
            //var nw = Convert.ToInt32(r["Network"]);
            //var bkp = Convert.ToInt32(r["Backup"]);
            //var mm = Convert.ToInt32(r["Memory"]);
            // var cx = Convert.ToInt32(r["Parallelism"]);
            // var tlog = Convert.ToInt32(r["TransactionLog"]);
            // var pw = Convert.ToInt32(r["PoisonWaits"]);
            // var pws = Convert.ToInt32(r["Poison Serializable Locking"]);
            //var pwn = Convert.ToInt32(r["Poison CMEMTHREAD and NUMA"]);


            // When building currentWaits, use the same canonical keys:
            var currentWaits = new Dictionary<string, long> {
                { "Locks", Convert.ToInt32(r["Locks"]) },
                { "Reads/Latches", Convert.ToInt32(r["Reads"]) },
                { "Writes/I/O", Convert.ToInt32(r["Writes"]) },
                { "Network", Convert.ToInt32(r["Network"]) },
                { "Backup", Convert.ToInt32(r["Backup"]) },
                { "Memory", Convert.ToInt32(r["Memory"]) },
                { "Parallelism", Convert.ToInt32(r["Parallelism"]) },
                { "Transaction Log", Convert.ToInt32(r["TransactionLog"]) },
                { "PoisonWaits", Convert.ToInt32(r["PoisonWaits"]) }, // use SQL alias or map alias -> canonical key
                { "Poison Serializable Locking", Convert.ToInt32(r["Poison Serializable Locking"]) },
                { "Poison CMEMTHREAD and NUMA", Convert.ToInt32(r["Poison CMEMTHREAD and NUMA"]) }
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
                ws.Add(new WaitStatItem { WaitType = kv.Key, WaitTimeMs = kv.Value, Percentage = pct, Color = new System.Windows.Media.SolidColorBrush(_waitColors[kv.Key]) });
            }
            WaitStatsGrid.ItemsSource = ws.Where(w => w.WaitTimeMs > 0).OrderByDescending(w => w.WaitTimeMs).ToList();

            // Update graph with delta from last sample (actual ms values, not percentages)
            foreach (var kv in deltaFromLast)
            {
                var val = Math.Max(0, kv.Value);
                _waitHistory[kv.Key].Enqueue(val);
                while (_waitHistory[kv.Key].Count > MaxHistoryPoints) _waitHistory[kv.Key].Dequeue();
            }
            dt.Dispose();
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
                    pl.Points.Add(new System.Windows.Point((off + i) * xStep, yVal));
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
                    Foreground = System.Windows.Media.Brushes.Gray,
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
                    Stroke = System.Windows.Media.Brushes.LightGray,
                    StrokeThickness = 0.5,
                    StrokeDashArray = new DoubleCollection { 2, 2 }
                };
                WaitGraphCanvas.Children.Add(gridLine);
            }
        }

        private void UpdateSessionsAfterChange()
        {
            using var conn = new SqlConnection(_selectedConnectionString);
            conn.Open();
          
            //DataTable? UpdateSessionsTable = null;
            
            var UpdateSessionsTable = GetSessions(conn);
            UpdateSessions(UpdateSessionsTable);
        }
        private void UpdateSessions(DataTable dt)
        {
            var sess = new List<SessionItem>(); long totCpu = 0, totIo = 0;
            foreach (DataRow r in dt.Rows)
            {
                var cpu = Convert.ToInt32(r["Cpu"]); var io = Convert.ToInt32(r["PhysicalIo"]);
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
            dt.Dispose();
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
                    WaitDurationMs = r["wait_time"] != DBNull.Value ? Convert.ToInt32(r["wait_time"]) : 0,
                    WaitType = r["wait_type"]?.ToString() ?? "",
                    WaitStatement = r["wait_stmt"]?.ToString() ?? r["wait_batch"]?.ToString() ?? "",
                    BlockerStatement = r["block_stmt"]?.ToString() ?? ""

                });
            }

            BlockingGrid.ItemsSource = _currentBlocking.OrderByDescending(b => b.WaitDurationMs).ToList();
            BlockingCountText.Text = $" ({_currentBlocking.Count})";
            BlockingAlert.Visibility = _currentBlocking.Any() ? Visibility.Visible : Visibility.Collapsed;
            dt.Dispose();
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
                    ExecutionCount = r["Total Execution Count"] != DBNull.Value ? Convert.ToInt32(r["Total Execution Count"]) : 0,
                    TotalCpuTimeS = r["Total CPU Time in S"] != DBNull.Value ? Convert.ToDecimal(r["Total CPU Time in S"]) : 0,
                    TotalLogicalReads = r["Total Logical Reads"] != DBNull.Value ? Convert.ToInt32(r["Total Logical Reads"]) : 0,
                    TotalLogicalWrites = r["Total Logical Writes"] != DBNull.Value ? Convert.ToInt32(r["Total Logical Writes"]) : 0,
                    QueryText = r["Query"]?.ToString()?.Replace("\r", " ").Replace("\n", " ").Trim() ?? "",
                    PlanHandle = r["Plan Handle"] != DBNull.Value ? (byte[])r["Plan Handle"] : null
                });
            }

            TopQueriesGrid.ItemsSource = queries;
            dt.Dispose();
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

                _spidBoxPositions[sp.Spid] = new System.Drawing.Point( (int)(x + (boxWidth / 2)), (int)(y + (boxHeight / 2)));
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
                HorizontalAlignment =System.Windows.HorizontalAlignment.Center  ,
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


        protected override void OnClosed(EventArgs e) { StopMonitoring(); base.OnClosed(e); }
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