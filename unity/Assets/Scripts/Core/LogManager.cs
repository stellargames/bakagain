namespace BakAgain.Core {
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Runtime.CompilerServices;
    using System.Text;
    using Microsoft.Extensions.Logging;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    /// <summary>
    /// Microsoft.Extensions.Logging over <c>Debug.unityLogger</c>. Lines read
    /// <c>HH:mm:ss.fff [INFO] [Category] message</c>; Debug/Information log, Warning warns, Error and
    /// Critical are console errors. The message rendering reproduces the Serilog <c>{Message:lj}</c>
    /// output this replaced (TASK-840): strings raw, everything else JSON-ish — <c>true</c>, <c>null</c>,
    /// quoted enums, <c>[1,2]</c> for collections — so existing log greps and test regexes still match.
    /// </summary>
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
                    return _loggerFactory ??= new UnityLoggerFactory();
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        public static void Initialize() {
            _ = LoggerFactory;
        }

        private sealed class UnityLoggerFactory : ILoggerFactory {
            public ILogger CreateLogger(string categoryName) => new UnityLogger(categoryName);
            public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException();
            public void Dispose() { }
        }

        private sealed class UnityLogger : ILogger {
            private readonly string _category;

            public UnityLogger(string category) {
                _category = category;
            }

            public IDisposable BeginScope<TState>(TState state) => null;

#if ENABLE_LOGGING
            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug && logLevel != LogLevel.None;
#else
            public bool IsEnabled(LogLevel logLevel) => false;
#endif

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception,
                Func<TState, Exception, string> formatter) {
                if (!IsEnabled(logLevel)) return;

                string message;
                try {
                    message = state is IReadOnlyList<KeyValuePair<string, object>> values
                        ? Render(values)
                        : formatter(state, exception);
                } catch (Exception) {
                    // A template whose holes outnumber its arguments throws while enumerating. Logging
                    // must never throw into the caller; Serilog dropped such a line too.
                    return;
                }

                string line = DateTimeOffset.Now.ToString("HH:mm:ss.fff", null) + " [" + LevelTag(logLevel) + "] ["
                              + _category + "] " + message + Environment.NewLine + exception;
                Debug.unityLogger.Log(logLevel switch {
                    LogLevel.Warning => LogType.Warning,
                    LogLevel.Error or LogLevel.Critical => LogType.Error,
                    _ => LogType.Log,
                }, (object)line.Trim());
            }

            private static string LevelTag(LogLevel level) => level switch {
                LogLevel.Trace => "VERB",
                LogLevel.Debug => "DBUG",
                LogLevel.Information => "INFO",
                LogLevel.Warning => "WARN",
                LogLevel.Error => "EROR",
                _ => "FATL",
            };
        }

        // ---- Serilog-compatible message rendering ----

        private static string Render(IReadOnlyList<KeyValuePair<string, object>> values) {
            string template = null;
            var properties = new Dictionary<string, object>();
            foreach (KeyValuePair<string, object> kv in values) {
                if (kv.Key == "{OriginalFormat}") template = kv.Value as string;
                else properties[kv.Key] = Capture(kv.Value);
            }
            if (template == null) return values.ToString();

            var sb = new StringBuilder(template.Length + 32);
            int i = 0;
            while (i < template.Length) {
                char c = template[i];
                if (c != '{' && c != '}') {
                    sb.Append(c);
                    i++;
                } else if (i + 1 < template.Length && template[i + 1] == c) {
                    sb.Append(c); // {{ or }}
                    i += 2;
                } else if (c == '}') {
                    sb.Append(c);
                    i++;
                } else {
                    int end = i + 1;
                    while (end < template.Length && IsValidInTag(template[end])) end++;
                    if (end == template.Length || template[end] != '}') {
                        sb.Append(template, i, end - i);
                        i = end;
                        continue;
                    }
                    string content = template.Substring(i + 1, end - i - 1);
                    if (!TryRenderProperty(content, properties, sb)) sb.Append('{').Append(content).Append('}');
                    i = end + 1;
                }
            }
            return sb.ToString();
        }

        private static bool IsValidInTag(char c) =>
            c != '}' && (char.IsLetterOrDigit(c) || char.IsPunctuation(c) || char.IsSymbol(c) || c == ' ' || c == ':');

        private static bool TryRenderProperty(string content, Dictionary<string, object> properties, StringBuilder sb) {
            string format = null;
            int colon = content.IndexOf(':');
            if (colon >= 0) {
                format = content.Substring(colon + 1);
                content = content.Substring(0, colon);
            }
            int? alignment = null;
            int comma = content.IndexOf(',');
            if (comma >= 0) {
                if (!int.TryParse(content.Substring(comma + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture,
                        out int width)) return false;
                alignment = width;
                content = content.Substring(0, comma);
            }
            if (content.Length == 0) return false;
            foreach (char ch in content) {
                if (!char.IsLetterOrDigit(ch) && ch != '_') return false;
            }
            if (!properties.TryGetValue(content, out object value)) return false;

            string text = value is string s ? s
                : format == null ? Json(value)
                : value is IFormattable f ? f.ToString(format, null)
                : value?.ToString() ?? "null";
            if (alignment is int a && text.Length < Math.Abs(a)) {
                text = a < 0 ? text.PadRight(-a) : text.PadLeft(a);
            }
            sb.Append(text);
            return true;
        }

        /// <summary>Serilog's default capturing: scalars stay, collections become lists, anything else its ToString().</summary>
        private static object Capture(object value) {
            switch (value) {
                case null:
                case string:
                    return value;
                case byte[] bytes:
                    return bytes.Length <= 1024
                        ? BitConverter.ToString(bytes).Replace("-", "")
                        : BitConverter.ToString(bytes, 0, 16).Replace("-", "") + "... (" + bytes.Length + " bytes)";
            }
            Type type = value.GetType();
            if ((type.IsPrimitive && value is not IntPtr and not UIntPtr) || type.IsEnum || value is decimal
                || value is DateTime || value is DateTimeOffset || value is TimeSpan || value is Guid || value is Uri) {
                return value;
            }
            if (value is IDictionary dictionary) {
                var entries = new List<KeyValuePair<object, object>>();
                foreach (DictionaryEntry e in dictionary) entries.Add(new(e.Key, Capture(e.Value)));
                return entries;
            }
            if (value is IEnumerable enumerable) {
                var items = new List<object>();
                foreach (object item in enumerable) items.Add(Capture(item));
                return items;
            }
            if (value is ITuple tuple && type.IsValueType) {
                var items = new List<object>();
                for (int n = 0; n < tuple.Length; n++) items.Add(Capture(tuple[n]));
                return items;
            }
            return value.ToString();
        }

        private static string Json(object value) {
            var sb = new StringBuilder();
            WriteJson(value, sb);
            return sb.ToString();
        }

        private static void WriteJson(object value, StringBuilder sb) {
            switch (value) {
                case null: sb.Append("null"); return;
                case string s: WriteJsonString(s, sb); return;
                case bool b: sb.Append(b ? "true" : "false"); return;
                case double d:
                    if (double.IsNaN(d) || double.IsInfinity(d)) WriteJsonString(d.ToString(CultureInfo.InvariantCulture), sb);
                    else sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                    return;
                case float f:
                    if (float.IsNaN(f) || float.IsInfinity(f)) WriteJsonString(f.ToString(CultureInfo.InvariantCulture), sb);
                    else sb.Append(f.ToString("R", CultureInfo.InvariantCulture));
                    return;
                case int or uint or long or ulong or short or ushort or byte or sbyte or decimal:
                    sb.Append(((IFormattable)value).ToString(null, CultureInfo.InvariantCulture));
                    return;
                case DateTime or DateTimeOffset:
                    WriteJsonString(((IFormattable)value).ToString("O", CultureInfo.InvariantCulture), sb);
                    return;
                case List<KeyValuePair<object, object>> entries:
                    sb.Append('{');
                    for (int n = 0; n < entries.Count; n++) {
                        if (n > 0) sb.Append(',');
                        WriteJsonString(entries[n].Key?.ToString() ?? "null", sb);
                        sb.Append(':');
                        WriteJson(entries[n].Value, sb);
                    }
                    sb.Append('}');
                    return;
                case List<object> items:
                    sb.Append('[');
                    for (int n = 0; n < items.Count; n++) {
                        if (n > 0) sb.Append(',');
                        WriteJson(items[n], sb);
                    }
                    sb.Append(']');
                    return;
                default:
                    WriteJsonString(value.ToString(), sb);
                    return;
            }
        }

        private static void WriteJsonString(string s, StringBuilder sb) {
            sb.Append('"');
            foreach (char c in s) {
                switch (c) {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32) sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
