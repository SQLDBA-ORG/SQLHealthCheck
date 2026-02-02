using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime;

namespace SqlCheckLibrary.Services
{
    /// <summary>
    /// Enterprise resource and memory management service
    /// Implements best practices for garbage collection, connection pooling, and resource cleanup
    /// </summary>
    public static class ResourceManager
    {
        private static readonly object _lock = new object();
        private static bool _isOptimized = false;

        /// <summary>
        /// Initialize resource optimization settings for the application
        /// Call this once at application startup
        /// </summary>
        public static void InitializeResourceOptimization()
        {
            lock (_lock)
            {
                if (_isOptimized)
                    return;

                try
                {
                    // Enable server garbage collection for better throughput in multi-threaded scenarios
                    // This is configured in the project file, but we validate here
                    if (!GCSettings.IsServerGC)
                    {
                        Debug.WriteLine("WARNING: Server GC is not enabled. Consider adding <ServerGarbageCollection>true</ServerGarbageCollection> to project file");
                    }

                    // Use concurrent GC for better responsiveness
                    GCSettings.LatencyMode = GCLatencyMode.Interactive;

                    // Configure large object heap compaction for better memory management
                    GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;

                    _isOptimized = true;
                    Debug.WriteLine("Resource optimization initialized successfully");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Resource optimization warning: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Perform aggressive garbage collection and cleanup
        /// Use sparingly - only when significant memory has been released
        /// </summary>
        public static void AggressiveCleanup()
        {
            // Force full garbage collection
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, true, true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, true, true);

            // Compact large object heap
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect();

            Debug.WriteLine($"Aggressive cleanup completed. Memory: {GC.GetTotalMemory(false):N0} bytes");
        }

        /// <summary>
        /// Perform lightweight garbage collection hint
        /// This is safe to call periodically and lets GC decide when to run
        /// </summary>
        public static void SuggestCleanup()
        {
            // Only collect Gen0 and Gen1 - less aggressive
            GC.Collect(1, GCCollectionMode.Optimized, false);
        }

        /// <summary>
        /// Get current memory usage statistics
        /// </summary>
        public static MemoryStatistics GetMemoryStatistics()
        {
            var process = Process.GetCurrentProcess();

            return new MemoryStatistics
            {
                TotalManagedMemory = GC.GetTotalMemory(false),
                WorkingSet = process.WorkingSet64,
                PrivateMemory = process.PrivateMemorySize64,
                Gen0Collections = GC.CollectionCount(0),
                Gen1Collections = GC.CollectionCount(1),
                Gen2Collections = GC.CollectionCount(2),
                IsServerGC = GCSettings.IsServerGC,
                LatencyMode = GCSettings.LatencyMode.ToString()
            };
        }

        /// <summary>
        /// Monitor memory pressure and return recommendation
        /// </summary>
        public static MemoryPressure CheckMemoryPressure()
        {
            var stats = GetMemoryStatistics();
            var process = Process.GetCurrentProcess();

            // Calculate memory pressure based on working set vs total available
            var workingSetMB = stats.WorkingSet / 1024 / 1024;

            if (workingSetMB > 1024) // Over 1GB
                return MemoryPressure.High;
            else if (workingSetMB > 512) // Over 512MB
                return MemoryPressure.Medium;
            else
                return MemoryPressure.Low;
        }

        /// <summary>
        /// Safe disposal pattern for IDisposable objects
        /// </summary>
        public static void SafeDispose(IDisposable? disposable)
        {
            try
            {
                disposable?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Disposal warning: {ex.Message}");
            }
        }

        /// <summary>
        /// Safe disposal with action callback
        /// </summary>
        public static void SafeDispose(IDisposable? disposable, Action<Exception>? onError)
        {
            try
            {
                disposable?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Disposal warning: {ex.Message}");
                onError?.Invoke(ex);
            }
        }
    }

    /// <summary>
    /// Memory usage statistics
    /// </summary>
    public class MemoryStatistics
    {
        public long TotalManagedMemory { get; set; }
        public long WorkingSet { get; set; }
        public long PrivateMemory { get; set; }
        public int Gen0Collections { get; set; }
        public int Gen1Collections { get; set; }
        public int Gen2Collections { get; set; }
        public bool IsServerGC { get; set; }
        public string LatencyMode { get; set; } = string.Empty;

        public string TotalManagedMemoryFormatted => FormatBytes(TotalManagedMemory);
        public string WorkingSetFormatted => FormatBytes(WorkingSet);
        public string PrivateMemoryFormatted => FormatBytes(PrivateMemory);

        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }

    /// <summary>
    /// Memory pressure levels
    /// </summary>
    public enum MemoryPressure
    {
        Low,
        Medium,
        High
    }

    /// <summary>
    /// Disposable scope helper for automatic cleanup
    /// Usage: using (var scope = new DisposableScope()) { ... }
    /// </summary>
    public class DisposableScope : IDisposable
    {
        private readonly List<IDisposable> _disposables = new List<IDisposable>();
        private bool _disposed = false;

        public void Add(IDisposable disposable)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(DisposableScope));
            
            _disposables.Add(disposable);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            foreach (var disposable in _disposables)
            {
                ResourceManager.SafeDispose(disposable);
            }

            _disposables.Clear();
            _disposed = true;
        }
    }
}
