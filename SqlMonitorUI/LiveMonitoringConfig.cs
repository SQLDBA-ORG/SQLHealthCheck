using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SqlMonitorUI
{
    /// <summary>
    /// Configuration for Live Monitoring SQL queries.
    /// Watches config file for changes and auto-reloads.
    /// </summary>
    public class LiveMonitoringConfig : IDisposable
    {
        private static readonly string ConfigFileName = "LiveMonitoring.config.json";
        private static readonly object _lock = new object();
        private static LiveMonitoringConfig? _instance;
        private FileSystemWatcher? _watcher;
        private bool _isDisposed;
        private DateTime _lastReloadTime = DateTime.MinValue;
        private readonly TimeSpan _reloadDebounce = TimeSpan.FromMilliseconds(500);

        /// <summary>
        /// Event fired when configuration is reloaded from file
        /// </summary>
        public event EventHandler? ConfigReloaded;

        #region Singleton
        public static LiveMonitoringConfig Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new LiveMonitoringConfig();
                            _instance.Load();
                            _instance.StartFileWatcher();
                        }
                    }
                }
                return _instance;
            }
        }
        #endregion

        #region Configuration Properties

        /// <summary>
        /// Refresh interval in milliseconds (default: 1000ms = 1 second)
        /// </summary>
        public int RefreshIntervalMs { get; set; } = 1000;

        /// <summary>
        /// Default command timeout for SQL queries in seconds
        /// </summary>
        public int QueryTimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// SQL Queries configuration
        /// </summary>
        public QueryConfigurations Queries { get; set; } = new QueryConfigurations();

        /// <summary>
        /// Notes for users (ignored by code)
        /// </summary>
        [JsonIgnore]
        public object? Notes { get; set; }

        // Convenience properties to access refresh intervals
        [JsonIgnore]
        public int TopQueriesRefreshInterval => Queries.TopQueries.RefreshEveryNTicks;
        [JsonIgnore]
        public int DriveLatencyRefreshInterval => Queries.DriveLatency.RefreshEveryNTicks;
        [JsonIgnore]
        public int ServerDetailsRefreshInterval => Queries.ServerDetails.RefreshEveryNTicks;
        [JsonIgnore]
        public int CommandTimeoutSeconds => QueryTimeoutSeconds;

        #endregion

        #region Load/Save

        public void Load()
        {
            try
            {
                var configPath = GetConfigPath();
                if (File.Exists(configPath))
                {
                    var json = File.ReadAllText(configPath);
                    var options = GetJsonOptions();
                    var loaded = JsonSerializer.Deserialize<LiveMonitoringConfig>(json, options);
                    if (loaded != null)
                    {
                        CopyFrom(loaded);
                        System.Diagnostics.Debug.WriteLine($"LiveMonitoring.config loaded successfully");
                    }
                }
                else
                {
                    // Create default config file
                    Save();
                    System.Diagnostics.Debug.WriteLine($"LiveMonitoring.config created with defaults");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading LiveMonitoring.config: {ex.Message}");
                // Use defaults on error
            }
        }

        public void Save()
        {
            try
            {
                var configPath = GetConfigPath();
                var json = JsonSerializer.Serialize(this, GetJsonOptions());
                File.WriteAllText(configPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving LiveMonitoring.config: {ex.Message}");
            }
        }

        public void Reload()
        {
            // Debounce rapid reloads (file system can fire multiple events)
            if (DateTime.Now - _lastReloadTime < _reloadDebounce)
                return;

            _lastReloadTime = DateTime.Now;
            
            try
            {
                Load();
                ConfigReloaded?.Invoke(this, EventArgs.Empty);
                System.Diagnostics.Debug.WriteLine("LiveMonitoring.config reloaded - queries updated");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reloading config: {ex.Message}");
            }
        }

        private void CopyFrom(LiveMonitoringConfig other)
        {
            RefreshIntervalMs = other.RefreshIntervalMs;
            QueryTimeoutSeconds = other.QueryTimeoutSeconds;
            Queries = other.Queries ?? new QueryConfigurations();
        }

        private static string GetConfigPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFileName);
        }

        private static JsonSerializerOptions GetJsonOptions()
        {
            return new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNameCaseInsensitive = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };
        }

        #endregion

        #region File Watcher

        private void StartFileWatcher()
        {
            try
            {
                var configPath = GetConfigPath();
                var directory = Path.GetDirectoryName(configPath);
                var fileName = Path.GetFileName(configPath);

                if (string.IsNullOrEmpty(directory)) return;

                _watcher = new FileSystemWatcher(directory, fileName)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                    EnableRaisingEvents = true
                };

                _watcher.Changed += OnConfigFileChanged;
                _watcher.Created += OnConfigFileChanged;

                System.Diagnostics.Debug.WriteLine($"Watching for changes: {configPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error starting file watcher: {ex.Message}");
            }
        }

        private void OnConfigFileChanged(object sender, FileSystemEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"Config file changed: {e.ChangeType}");
            Reload();
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Get SQL with placeholders replaced
        /// </summary>
        public string GetSessionsSql(int topN, bool showSleeping, string programFilter)
        {
            var sql = Queries.Sessions.Sql;
            sql = sql.Replace("{TopN}", topN.ToString());
            sql = sql.Replace("{StatusFilter}", showSleeping ? "" : "AND status <> 'sleeping'");
            sql = sql.Replace("{ProgramFilter}", 
                string.IsNullOrEmpty(programFilter) ? "" : $"AND program_name LIKE '%{programFilter}%'");
            return sql;
        }

        /// <summary>
        /// Check if a query should run this tick
        /// </summary>
        public bool ShouldRefresh(QueryConfig config, int tickCount)
        {
            if (!config.Enabled) return false;
            return tickCount == 1 || tickCount % config.RefreshEveryNTicks == 0;
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (!_isDisposed)
            {
                if (_watcher != null)
                {
                    _watcher.Changed -= OnConfigFileChanged;
                    _watcher.Created -= OnConfigFileChanged;
                    _watcher.Dispose();
                    _watcher = null;
                }
                _isDisposed = true;
            }
        }

        #endregion
    }

    #region Configuration Classes

    /// <summary>
    /// Container for all query configurations
    /// </summary>
    public class QueryConfigurations
    {
        public QueryConfig Metrics { get; set; } = new QueryConfig 
        { 
            Description = "Server metrics",
            TimeoutSeconds = 30,
            RefreshEveryNTicks = 1
        };

        public SessionsQueryConfig Sessions { get; set; } = new SessionsQueryConfig();

        public QueryConfig Blocking { get; set; } = new QueryConfig
        {
            Description = "Blocking chains",
            TimeoutSeconds = 15,
            RefreshEveryNTicks = 1
        };

        public QueryConfig TopQueries { get; set; } = new QueryConfig
        {
            Description = "Top queries by I/O",
            TimeoutSeconds = 30,
            RefreshEveryNTicks = 30
        };

        public QueryConfig DriveLatency { get; set; } = new QueryConfig
        {
            Description = "Drive latency stats",
            TimeoutSeconds = 15,
            RefreshEveryNTicks = 30
        };

        public QueryConfig ServerDetails { get; set; } = new QueryConfig
        {
            Description = "Server info",
            TimeoutSeconds = 15,
            RefreshEveryNTicks = 60
        };
    }

    /// <summary>
    /// Configuration for a single query
    /// </summary>
    public class QueryConfig
    {
        public string Description { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public int TimeoutSeconds { get; set; } = 30;
        public int RefreshEveryNTicks { get; set; } = 1;
        public string Sql { get; set; } = "";
    }

    /// <summary>
    /// Sessions query with TopN setting
    /// </summary>
    public class SessionsQueryConfig : QueryConfig
    {
        public int TopN { get; set; } = 20;

        public SessionsQueryConfig()
        {
            Description = "Active sessions";
            TimeoutSeconds = 15;
            RefreshEveryNTicks = 1;
        }
    }

    #endregion
}
