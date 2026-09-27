namespace BakAgain.Tests.Editor.Core.Fixtures {
    using Microsoft.Extensions.Logging;
    using System;

    /// <summary>
    /// No-op <see cref="ILogger{T}"/> for use in unit tests where logging is not under test.
    /// Avoids pulling in Microsoft.Extensions.Logging.Abstractions or going through
    /// Unity's <c>LogManager</c> (which requires Unity runtime APIs).
    /// </summary>
    public sealed class NullLogger<T> : ILogger<T> {
        public IDisposable BeginScope<TState>(TState state) => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception exception, Func<TState, Exception, string> formatter) { }

        private sealed class NullScope : IDisposable {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
