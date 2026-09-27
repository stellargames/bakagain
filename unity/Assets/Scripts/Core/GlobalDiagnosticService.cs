namespace BakAgain.Core
{
    using System;
    using System.IO;
    using UnityEngine;
    using VContainer.Unity;

    public class GlobalDiagnosticService : IInitializable, IDisposable
    {
        private string _logFilePath;

        public void Initialize()
        {
            _logFilePath = Path.Combine(Application.persistentDataPath, "critical_errors.log");
            
            // Clear old log file on new session or append, based on preference. For now, appending.
            File.AppendAllText(_logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] ================= SESSION START =================\n");
            File.AppendAllText(_logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] GlobalDiagnosticService.Initialize() called and handlers registered.\n");

            AppDomain.CurrentDomain.UnhandledException += HandleUnhandledException;
        }

        public void Dispose()
        {
            File.AppendAllText(_logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] GlobalDiagnosticService.Dispose() called.\n");
            AppDomain.CurrentDomain.UnhandledException -= HandleUnhandledException;
            File.AppendAllText(_logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] ================= SESSION END ===================\n");
        }

        private void HandleUnhandledException(object sender, UnhandledExceptionEventArgs args)
        {
            var e = (Exception)args.ExceptionObject;
            string message = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [GlobalDiagnosticService] Unhandled Exception:\nMessage: {e.Message}\nStackTrace: {e.StackTrace}\nIsTerminating: {args.IsTerminating}\n";
            Debug.LogError(message); // Keep Unity log
            File.AppendAllText(_logFilePath, message); // Direct file log

            if (args.IsTerminating)
            {
                string terminatingMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [GlobalDiagnosticService] Application is terminating due to unhandled exception: {e.Message}\n";
                Debug.LogError(terminatingMessage);
                File.AppendAllText(_logFilePath, terminatingMessage);
            }
        }
    }
}