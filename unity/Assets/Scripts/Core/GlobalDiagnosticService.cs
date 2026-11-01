namespace BakAgain.Core
{
    using System;
    using System.IO;
    using UnityEngine;
    using VContainer.Unity;

    /// <summary>
    /// Writes an unhandled exception to persistentDataPath/critical_errors.log, next to the Unity log.
    /// </summary>
    /// <remarks>
    /// Only a caught exception is written (TASK-1016): session start/stop lines grew the file to 8 MB over
    /// 6,338 sessions with one real entry in it. The file is rotated to critical_errors-prev.log once it
    /// passes <see cref="MaxBytes"/>, and a stack trace is cut at <see cref="MaxStackLines"/> lines, since a
    /// stack overflow alone wrote ~37,000.
    /// </remarks>
    public class GlobalDiagnosticService : IInitializable, IDisposable
    {
        internal const long MaxBytes = 256 * 1024;
        internal const int MaxStackLines = 200;

        private string _logFilePath;

        public void Initialize()
        {
            _logFilePath = Path.Combine(Application.persistentDataPath, "critical_errors.log");
            AppDomain.CurrentDomain.UnhandledException += HandleUnhandledException;
        }

        public void Dispose()
        {
            AppDomain.CurrentDomain.UnhandledException -= HandleUnhandledException;
        }

        private void HandleUnhandledException(object sender, UnhandledExceptionEventArgs args)
        {
            string message = Entry((Exception)args.ExceptionObject, args.IsTerminating, DateTime.Now);
            Debug.LogError(message);
            Append(_logFilePath, message);
        }

        internal static string Entry(Exception e, bool terminating, DateTime at)
        {
            string[] stack = (e.StackTrace ?? string.Empty).Split('\n');
            string trace = stack.Length <= MaxStackLines
                ? string.Join("\n", stack)
                : string.Join("\n", stack, 0, MaxStackLines) + $"\n... {stack.Length - MaxStackLines} more lines";
            return $"[{at:yyyy-MM-dd HH:mm:ss.fff}] Unhandled exception{(terminating ? " (terminating)" : "")}: "
                + $"{e.GetType().FullName}: {e.Message}\n{trace}\n";
        }

        /// <summary>Appends <paramref name="text"/>, first moving a file past the cap to "-prev".</summary>
        internal static void Append(string path, string text, long maxBytes = MaxBytes)
        {
            var file = new FileInfo(path);
            if (file.Exists && file.Length + text.Length > maxBytes)
            {
                string prev = Path.Combine(file.DirectoryName ?? string.Empty,
                    Path.GetFileNameWithoutExtension(path) + "-prev" + file.Extension);
                File.Copy(path, prev, overwrite: true);
                File.Delete(path);
            }
            File.AppendAllText(path, text);
        }
    }
}
