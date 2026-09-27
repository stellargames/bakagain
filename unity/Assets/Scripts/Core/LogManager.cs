namespace BakAgain.Core {
    using Microsoft.Extensions.Logging;
    using Serilog;
    using Serilog.Extensions.Logging;
    using Serilog.Sinks.Unity3D;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    public static class LogManager {
        private static ILoggerFactory _loggerFactory;
        private static readonly object Lock = new();

        /// <summary>
        /// Built on first use. Everything goes to Unity's log (the Editor log, or a player's
        /// <c>Player.log</c>), which is the file the Preferences screen points bug reporters at.
        /// </summary>
        public static ILoggerFactory LoggerFactory {
            get {
                lock (Lock) {
                    return _loggerFactory ??= CreateLoggerFactory();
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        public static void Initialize() {
            _ = LoggerFactory;
            Application.quitting -= Shutdown;
            Application.quitting += Shutdown;
        }

        private static ILoggerFactory CreateLoggerFactory() {
            LoggerConfiguration config = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .Enrich.FromLogContext()
                .WriteTo.Unity3D(outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u4}] [{SourceContext}] {Message:lj}{NewLine}{Exception}");

#if !ENABLE_LOGGING
            config = config.MinimumLevel.Is(Serilog.Events.LogEventLevel.Fatal + 1);
            Debug.Log("LogManager: ENABLE_LOGGING not defined, Serilog pipeline set to minimal output.");
#endif

            Log.Logger = config.CreateLogger();
            return new SerilogLoggerFactory(Log.Logger);
        }

        private static void Shutdown() {
            Application.quitting -= Shutdown;
            ILogger logger = LoggerFactory?.CreateLogger(nameof(LogManager));
            logger?.LogInformation("LogManager: Shutting down. Flushing logs.");
            Log.CloseAndFlush();
            _loggerFactory = null;
        }
    }
}