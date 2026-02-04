using System;
using System.Windows;

namespace SqlMonitorUI
{
    public partial class LiveQueryEditorWindow : Window
    {
        private readonly LiveQueryViewModel _query;
        private readonly string _originalSql;

        public LiveQueryEditorWindow(LiveQueryViewModel query)
        {
            InitializeComponent();
            _query = query;
            _originalSql = query.Sql;

            // Set window title and header
            Title = $"Edit Query: {query.Name}";
            HeaderText.Text = $"Edit Query: {query.Name}";
            DescriptionText.Text = query.Description;

            // Load current values
            EnabledCheckBox.IsChecked = query.Enabled;
            TimeoutTextBox.Text = query.TimeoutSeconds.ToString();
            RefreshTicksTextBox.Text = query.RefreshEveryNTicks.ToString();
            SqlTextBox.Text = query.Sql;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // Validate inputs
            if (!int.TryParse(TimeoutTextBox.Text, out int timeout) || timeout < 1)
            {
                MessageBox.Show("Please enter a valid timeout (positive number).",
                    "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                TimeoutTextBox.Focus();
                return;
            }

            if (!int.TryParse(RefreshTicksTextBox.Text, out int refreshTicks) || refreshTicks < 1)
            {
                MessageBox.Show("Please enter a valid refresh interval (positive number).",
                    "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                RefreshTicksTextBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(SqlTextBox.Text))
            {
                MessageBox.Show("SQL query cannot be empty.",
                    "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                SqlTextBox.Focus();
                return;
            }

            // Update the view model
            _query.Enabled = EnabledCheckBox.IsChecked ?? true;
            _query.TimeoutSeconds = timeout;
            _query.RefreshEveryNTicks = refreshTicks;
            _query.Sql = SqlTextBox.Text;

            DialogResult = true;
            Close();
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Reset this query to its default SQL?\n\nThis will reload the default query from the application defaults.",
                "Confirm Reset",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                // Get default SQL for this query type
                var defaultSql = GetDefaultSql(_query.Name);
                if (!string.IsNullOrEmpty(defaultSql))
                {
                    SqlTextBox.Text = defaultSql;
                    MessageBox.Show("Query reset to default.", "Reset Complete", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private string GetDefaultSql(string queryName)
        {
            // Return default SQL for each query type
            switch (queryName)
            {
                case "Metrics":
                    return @"DECLARE @BR BIGINT, @SC BIGINT, @TR BIGINT, @cpu INT
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
SUM(CONVERT(BIGINT,CASE WHEN wait_type IN('LCK_M_RS_S', 'LCK_M_RS_U', 'LCK_M_RIn_NL','LCK_M_RIn_S', 'LCK_M_RIn_U','LCK_M_RIn_X', 'LCK_M_RX_S', 'LCK_M_RX_U','LCK_M_RX_X')THEN wait_time_ms-signal_wait_time_ms ELSE 0 END)) AS [Poison Serializable Locking],
SUM(CONVERT(BIGINT,CASE WHEN wait_type = 'CMEMTHREAD' THEN wait_time_ms-signal_wait_time_ms ELSE 0 END)) AS [Poison CMEMTHREAD and NUMA],
@@TOTAL_READ AS PhReads,@@TOTAL_WRITE AS PhWrites,@BR AS BatchReq,@SC AS SqlComp,@TR AS Trans
FROM sys.dm_os_wait_stats WITH(NOLOCK)";

                case "Sessions":
                    return @"SELECT TOP {TopN} s.spid AS Spid,
    DB_NAME(s.dbid) AS [Database],
    status AS Status,
    CAST(cpu AS BIGINT) AS Cpu,
    CAST(physical_io AS BIGINT) AS PhysicalIo,
    hostname AS Hostname,
    program_name AS ProgramName,
    loginame AS LoginName,
    cmd AS Command,
    CASE WHEN last_batch IS NULL OR last_batch < DATEADD(DAY, -30, GETDATE()) THEN 999999 
         ELSE DATEDIFF(SECOND, last_batch, GETDATE()) END AS IdleSeconds,
    ISNULL(SomeText.text,'') [text],
    s.blocked
FROM sys.sysprocesses s
LEFT OUTER JOIN (
    SELECT spid AS Spid, e.text
    FROM sys.sysprocesses s WITH (NOLOCK)
    CROSS APPLY sys.dm_exec_sql_text(sql_handle) e
) SomeText ON SomeText.spid = s.spid
WHERE s.spid > 50 
    AND program_name NOT LIKE '%SQLMonitorUI%'
    AND hostname <> ''
    AND program_name <> ''
{StatusFilter}
{ProgramFilter}
ORDER BY (ISNULL(cpu,0) + ISNULL(physical_io,0)) DESC";

                case "TopQueries":
                    return @"SELECT
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
        FROM sys.dm_exec_query_stats s WITH (NOLOCK)
        GROUP BY s.plan_handle ORDER BY SUM(s.total_logical_reads + s.total_logical_writes) DESC
    ) TT
) TMP
OUTER APPLY sys.dm_exec_sql_text(TMP.[Plan Handle]) AS st
OUTER APPLY sys.dm_exec_query_plan(TMP.[Plan Handle]) AS qp
ORDER BY Category, [Total Logical Reads] DESC
OPTION (MAXDOP 1)";

                default:
                    MessageBox.Show($"No default SQL available for '{queryName}'.\n\nPlease edit the LiveMonitoring.config.json file directly.",
                        "No Default", MessageBoxButton.OK, MessageBoxImage.Information);
                    return "";
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
