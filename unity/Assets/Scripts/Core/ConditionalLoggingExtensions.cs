namespace BakAgain.Core {
    using Microsoft.Extensions.Logging;
    using System;
    using System.Diagnostics;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    public static class ConditionalLoggingExtensions {
        private const string LoggingSymbol = "ENABLE_LOGGING";

        private static readonly EventId DefaultEventId = new(0);

        [Conditional(LoggingSymbol)]
        [HideInCallstack]
        public static void LogTrace(this ILogger logger, string message) {
            logger.LogTrace(DefaultEventId, message);
        }

        [Conditional(LoggingSymbol)]
        [HideInCallstack]
        public static void LogTrace<T0>(this ILogger logger, string message, T0 arg0) {
            logger.LogTrace(DefaultEventId, message, arg0);
        }

        [Conditional(LoggingSymbol)]
        [HideInCallstack]
        public static void LogTrace<T0, T1>(this ILogger logger, string message, T0 arg0, T1 arg1) {
            logger.LogTrace(DefaultEventId, message, arg0, arg1);
        }

        [Conditional(LoggingSymbol)]
        [HideInCallstack]
        public static void LogTrace<T0, T1, T2>(this ILogger logger, string message, T0 arg0, T1 arg1, T2 arg2) {
            logger.LogTrace(DefaultEventId, message, arg0, arg1, arg2);
        }

        [Conditional(LoggingSymbol)]
        [HideInCallstack]
        public static void LogTrace(this ILogger logger, Exception exception, string message) {
            logger.LogTrace(DefaultEventId, exception, message);
        }

        [Conditional(LoggingSymbol)]
        [HideInCallstack]
        public static void LogTrace<T0>(this ILogger logger, Exception exception, string message, T0 arg0) {
            logger.LogTrace(DefaultEventId, exception, message, arg0);
        }

        [Conditional(LoggingSymbol)]
        [HideInCallstack]
        public static void LogDebug(this ILogger logger, string message) {
            logger.LogDebug(DefaultEventId, message);
        }

        [Conditional(LoggingSymbol)]
        [HideInCallstack]
        public static void LogDebug<T0>(this ILogger logger, string message, T0 arg0) {
            logger.LogDebug(DefaultEventId, message, arg0);
        }

        [Conditional(LoggingSymbol)]
        [HideInCallstack]
        public static void LogDebug<T0, T1>(this ILogger logger, string message, T0 arg0, T1 arg1) {
            logger.LogDebug(DefaultEventId, message, arg0, arg1);
        }

        [Conditional(LoggingSymbol)]
        [HideInCallstack]
        public static void LogDebug<T0, T1, T2>(this ILogger logger, string message, T0 arg0, T1 arg1, T2 arg2) {
            logger.LogDebug(DefaultEventId, message, arg0, arg1, arg2);
        }

        [Conditional(LoggingSymbol)]
        [HideInCallstack]
        public static void LogDebug(this ILogger logger, Exception exception, string message) {
            logger.LogDebug(DefaultEventId, exception, message);
        }

        [Conditional(LoggingSymbol)]
        [HideInCallstack]
        public static void LogDebug<T0>(this ILogger logger, Exception exception, string message, T0 arg0) {
            logger.LogDebug(DefaultEventId, exception, message, arg0);
        }

        [Conditional(LoggingSymbol)]
        [HideInCallstack]
        public static void LogInformation(this ILogger logger, string message) {
            logger.LogInformation(DefaultEventId, message);
        }

        [Conditional(LoggingSymbol)]
        [HideInCallstack]
        public static void LogInformation<T0>(this ILogger logger, string message, T0 arg0) {
            logger.LogInformation(DefaultEventId, message, arg0);
        }

        [Conditional(LoggingSymbol)]
        [HideInCallstack]
        public static void LogInformation<T0, T1>(this ILogger logger, string message, T0 arg0, T1 arg1) {
            logger.LogInformation(DefaultEventId, message, arg0, arg1);
        }

        [Conditional(LoggingSymbol)]
        [HideInCallstack]
        public static void LogInformation(this ILogger logger, Exception exception, string message) {
            logger.LogInformation(DefaultEventId, exception, message);
        }

        [Conditional(LoggingSymbol)]
        [HideInCallstack]
        public static void LogInformation<T0>(this ILogger logger, Exception exception, string message, T0 arg0) {
            logger.LogInformation(DefaultEventId, exception, message, arg0);
        }

        [Conditional(LoggingSymbol)]
        [HideInCallstack]
        public static void LogWarning(this ILogger logger, string message) {
            logger.LogWarning(DefaultEventId, message);
        }

        [Conditional(LoggingSymbol)]
        [HideInCallstack]
        public static void LogWarning<T0>(this ILogger logger, string message, T0 arg0) {
            logger.LogWarning(DefaultEventId, message, arg0);
        }

        [Conditional(LoggingSymbol)]
        [HideInCallstack]
        public static void LogWarning(this ILogger logger, Exception exception, string message) {
            logger.LogWarning(DefaultEventId, exception, message);
        }

        [Conditional(LoggingSymbol)]
        [HideInCallstack]
        public static void LogWarning<T0>(this ILogger logger, Exception exception, string message, T0 arg0) {
            logger.LogWarning(DefaultEventId, exception, message, arg0);
        }

        [Conditional(LoggingSymbol)]
        [HideInCallstack]
        public static void LogError(this ILogger logger, string message) {
            logger.LogError(DefaultEventId, message);
        }

        [Conditional(LoggingSymbol)]
        [HideInCallstack]
        public static void LogError(this ILogger logger, Exception exception, string message) {
            logger.LogError(DefaultEventId, exception, message);
        }

        [Conditional(LoggingSymbol)]
        [HideInCallstack]
        public static void LogError<T0>(this ILogger logger, Exception exception, string message, T0 arg0) {
            logger.LogError(DefaultEventId, exception, message, arg0);
        }

        [Conditional(LoggingSymbol)]
        [HideInCallstack]
        public static void LogCritical(this ILogger logger, string message) {
            logger.LogCritical(DefaultEventId, message);
        }

        [Conditional(LoggingSymbol)]
        [HideInCallstack]
        public static void LogCritical(this ILogger logger, Exception exception, string message) {
            logger.LogCritical(DefaultEventId, exception, message);
        }

        [Conditional(LoggingSymbol)]
        [HideInCallstack]
        public static void LogCritical<T0>(this ILogger logger, Exception exception, string message, T0 arg0) {
            logger.LogCritical(DefaultEventId, exception, message, arg0);
        }

        [Conditional(LoggingSymbol)]
        [HideInCallstack]
        public static void LogDebug(this ILogger logger, string message, params object[] args) {
            logger.LogDebug(DefaultEventId, message, args);
        }

        [Conditional(LoggingSymbol)]
        [HideInCallstack]
        public static void LogInformation(this ILogger logger, string message, params object[] args) {
            logger.LogInformation(DefaultEventId, message, args);
        }

        [Conditional(LoggingSymbol)]
        [HideInCallstack]
        public static void LogWarning(this ILogger logger, string message, params object[] args) {
            logger.LogWarning(DefaultEventId, message, args);
        }

        [Conditional(LoggingSymbol)]
        [HideInCallstack]
        public static void LogError(this ILogger logger, string message, params object[] args) {
            logger.LogError(DefaultEventId, message, args);
        }

        [Conditional(LoggingSymbol)]
        [HideInCallstack]
        public static void LogError(this ILogger logger, Exception exception, string message, params object[] args) {
            logger.LogError(DefaultEventId, exception, message, args);
        }

        [Conditional(LoggingSymbol)]
        [HideInCallstack]
        public static void LogCritical(this ILogger logger, string message, params object[] args) {
            logger.LogCritical(DefaultEventId, message, args);
        }

        public static ILogger<T> CreateLogger<T>(this ILoggerFactory factory) {
            if (factory == null) {
                throw new ArgumentNullException(nameof(factory), "LoggerFactory cannot be null.");
            }

            return new Logger<T>(factory);
        }
    }
}