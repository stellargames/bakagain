[assembly: BakAgain.Tests.TestSupport.CheckExpectedLog]

namespace BakAgain.Tests.TestSupport {
    using System;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;
    using NUnit.Framework;
    using NUnit.Framework.Interfaces;
    using UnityEngine;
    using Object = UnityEngine.Object;

    /// <summary>
    /// <c>LogAssert.Expect</c> that also keeps the expected line out of the console and the Editor log.
    /// </summary>
    /// <remarks>
    /// <b>LogAssert.Expect only keeps a test from failing: the line is still written.</b> So every
    /// green run filled the log with guards that tests fire on purpose — 38 distinct warnings and
    /// errors on 2026-10-26 — and a real one hid among them (owner: errors are never noise;
    /// TASK-1004). This installs one handler on <c>Debug.unityLogger</c>, which the game's
    /// <c>LogManager</c> writes through too: a line matching the next pending expectation of its type
    /// is swallowed, everything else passes on unchanged. <see cref="CheckExpectedLogAttribute"/>
    /// fails a test whose expected line never came, which is what LogAssert.Expect gave us.
    /// </remarks>
    public static class ExpectedLog {
        private static readonly List<(LogType Type, Regex Pattern)> Pending = new();
        private static readonly List<Capture> Captures = new();
        private static readonly object Gate = new();
        private static Handler _handler;

        /// <summary>Expects one line of <paramref name="type"/> matching <paramref name="pattern"/>.</summary>
        public static void Expect(LogType type, Regex pattern) {
            Install();
            lock (Gate) {
                Pending.Add((type, pattern));
            }
        }

        /// <inheritdoc cref="Expect(LogType, Regex)"/>
        public static void Expect(LogType type, string pattern) => Expect(type, new Regex(pattern));

        /// <summary>
        /// Swallows and records every line matching <paramref name="pattern"/> until disposed — for a
        /// test that counts what a guard logs rather than expecting it once.
        /// </summary>
        public static Capture CaptureAll(Regex pattern) {
            Install();
            var capture = new Capture(pattern);
            lock (Gate) {
                Captures.Add(capture);
            }
            return capture;
        }

        /// <summary>The lines a <see cref="CaptureAll"/> scope took.</summary>
        public sealed class Capture : IDisposable {
            internal Capture(Regex pattern) {
                Pattern = pattern;
            }

            internal Regex Pattern { get; }

            public List<string> Lines { get; } = new();

            public void Dispose() {
                lock (Gate) {
                    Captures.Remove(this);
                }
            }
        }

        /// <summary>The expectations still waiting for their line; cleared.</summary>
        internal static List<(LogType Type, Regex Pattern)> TakeUnmet() {
            lock (Gate) {
                var unmet = new List<(LogType, Regex)>(Pending);
                Pending.Clear();
                return unmet;
            }
        }

        private static void Install() {
            if (_handler != null && Debug.unityLogger.logHandler == _handler) {
                return;
            }
            _handler = new Handler(Debug.unityLogger.logHandler);
            Debug.unityLogger.logHandler = _handler;
        }

        private static bool Swallows(LogType type, string message) {
            lock (Gate) {
                int at = Pending.FindIndex(p => p.Type == type && p.Pattern.IsMatch(message));
                if (at >= 0) {
                    Pending.RemoveAt(at);
                    return true;
                }
                Capture capture = Captures.Find(c => c.Pattern.IsMatch(message));
                capture?.Lines.Add(message);
                return capture != null;
            }
        }

        private sealed class Handler : ILogHandler {
            private readonly ILogHandler _next;

            public Handler(ILogHandler next) {
                _next = next;
            }

            public void LogFormat(LogType logType, Object context, string format, params object[] args) {
                string message = args == null || args.Length == 0 ? format : string.Format(format, args);
                if (!Swallows(logType, message)) {
                    _next.LogFormat(logType, context, format, args);
                }
            }

            public void LogException(Exception exception, Object context) {
                if (!Swallows(LogType.Exception, exception.GetType().Name + ": " + exception.Message)) {
                    _next.LogException(exception, context);
                }
            }
        }
    }

    /// <summary>Fails a test that expected a line through <see cref="ExpectedLog"/> and never got it.</summary>
    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class CheckExpectedLogAttribute : Attribute, ITestAction {
        public ActionTargets Targets => ActionTargets.Test;

        public void BeforeTest(ITest test) => ExpectedLog.TakeUnmet();

        public void AfterTest(ITest test) {
            List<(LogType Type, Regex Pattern)> unmet = ExpectedLog.TakeUnmet();
            if (unmet.Count > 0) {
                Assert.Fail("expected log line(s) never came: "
                    + string.Join(" | ", unmet.ConvertAll(u => u.Type + " /" + u.Pattern + "/")));
            }
        }
    }
}
