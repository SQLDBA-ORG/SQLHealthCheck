using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SqlMonitorUI
{
    /// <summary>
    /// Application configuration manager with file watching support
    /// </summary>
    public class AppConfig : IDisposable
    {
        private static readonly object _lock = new object();
        private static AppConfig? _instance;
        
        private readonly string _configPath;
        private FileSystemWatcher? _watcher;
        private ConfigData _config;
        private bool _isDisposed;

        /// <summary>
        /// Event raised when configuration changes (from file or programmatically)
        /// </summary>
        public event EventHandler<ConfigChangedEventArgs>? ConfigChanged;

        /// <summary>
        /// Singleton instance
        /// </summary>
        public static AppConfig Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new AppConfig();
                        }
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// List of server names
        /// </summary>
        public List<string> Servers
        {
            get => _config.Servers ?? new List<string>();
            set
            {
                if (_config.Servers == null || !_config.Servers.SequenceEqual(value))
                {
                    _config.Servers = value?.ToList() ?? new List<string>();
                    Save();
                    OnConfigChanged(ConfigChangeSource.Application);
                }
            }
        }

        /// <summary>
        /// Whether to run checks in parallel
        /// </summary>
        public bool RunInParallel
        {
            get => _config.RunInParallel;
            set
            {
                if (_config.RunInParallel != value)
                {
                    _config.RunInParallel = value;
                    Save();
                }
            }
        }

        /// <summary>
        /// Use Windows Authentication
        /// </summary>
        public bool UseWindowsAuth
        {
            get => _config.UseWindowsAuth;
            set
            {
                if (_config.UseWindowsAuth != value)
                {
                    _config.UseWindowsAuth = value;
                    Save();
                }
            }
        }

        /// <summary>
        /// Default database
        /// </summary>
        public string DefaultDatabase
        {
            get => _config.DefaultDatabase ?? "master";
            set
            {
                if (_config.DefaultDatabase != value)
                {
                    _config.DefaultDatabase = value;
                    Save();
                }
            }
        }

        private AppConfig()
        {
            // Config file in same directory as executable
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            _configPath = Path.Combine(appDir, "SqlHealthMonitor.config");
            
            _config = new ConfigData();
            Load();
            StartFileWatcher();
        }

        /// <summary>
        /// Load configuration from file
        /// </summary>
        public void Load()
        {
            lock (_lock)
            {
                try
                {
                    if (File.Exists(_configPath))
                    {
                        var json = File.ReadAllText(_configPath);
                        var loaded = JsonSerializer.Deserialize<ConfigData>(json);
                        if (loaded != null)
                        {
                            _config = loaded;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error loading config: {ex.Message}");
                    // Keep existing config on error
                }
            }
        }

        /// <summary>
        /// Save configuration to file
        /// </summary>
        public void Save()
        {
            lock (_lock)
            {
                try
                {
                    // Temporarily disable watcher to avoid triggering reload
                    if (_watcher != null)
                    {
                        _watcher.EnableRaisingEvents = false;
                    }

                    var options = new JsonSerializerOptions 
                    { 
                        WriteIndented = true 
                    };
                    var json = JsonSerializer.Serialize(_config, options);
                    File.WriteAllText(_configPath, json);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error saving config: {ex.Message}");
                }
                finally
                {
                    // Re-enable watcher
                    if (_watcher != null)
                    {
                        _watcher.EnableRaisingEvents = true;
                    }
                }
            }
        }

        private void StartFileWatcher()
        {
            try
            {
                var directory = Path.GetDirectoryName(_configPath);
                var fileName = Path.GetFileName(_configPath);

                if (string.IsNullOrEmpty(directory))
                    directory = AppDomain.CurrentDomain.BaseDirectory;

                _watcher = new FileSystemWatcher(directory, fileName)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
                };

                _watcher.Changed += OnFileChanged;
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error starting file watcher: {ex.Message}");
            }
        }

        private DateTime _lastFileChange = DateTime.MinValue;

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            // Debounce - ignore events within 500ms of each other
            var now = DateTime.Now;
            if ((now - _lastFileChange).TotalMilliseconds < 500)
                return;
            
            _lastFileChange = now;

            // Small delay to ensure file is fully written
            System.Threading.Thread.Sleep(100);

            var oldServers = _config.Servers?.ToList() ?? new List<string>();
            Load();
            var newServers = _config.Servers ?? new List<string>();

            // Only raise event if servers actually changed
            if (!oldServers.SequenceEqual(newServers))
            {
                OnConfigChanged(ConfigChangeSource.File);
            }
        }

        private void OnConfigChanged(ConfigChangeSource source)
        {
            ConfigChanged?.Invoke(this, new ConfigChangedEventArgs(source, Servers));
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _watcher?.Dispose();
                _isDisposed = true;
            }
        }
    }

    /// <summary>
    /// Configuration data structure
    /// </summary>
    public class ConfigData
    {
        public List<string> Servers { get; set; } = new List<string>();
        public bool RunInParallel { get; set; } = true;
        public bool UseWindowsAuth { get; set; } = true;
        public string DefaultDatabase { get; set; } = "master";
        public bool EncryptConnection { get; set; } = true;
        public bool TrustServerCertificate { get; set; } = true;
    }

    /// <summary>
    /// Event args for configuration changes
    /// </summary>
    public class ConfigChangedEventArgs : EventArgs
    {
        public ConfigChangeSource Source { get; }
        public List<string> Servers { get; }

        public ConfigChangedEventArgs(ConfigChangeSource source, List<string> servers)
        {
            Source = source;
            Servers = servers;
        }
    }

    /// <summary>
    /// Source of configuration change
    /// </summary>
    public enum ConfigChangeSource
    {
        File,
        Application
    }
}
