namespace BakAgain.Tests.Editor.Core {
    using System;
    using System.IO;
    using System.Linq;
    using BakAgain.Core;
    using NUnit.Framework;

    /// <summary>critical_errors.log stays small (TASK-1016).</summary>
    public class CriticalErrorLogTests {
        private string _dir;

        [SetUp]
        public void SetUp() {
            _dir = Path.Combine(Path.GetTempPath(), "critlog-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(_dir, recursive: true);

        [Test]
        public void AFilePastTheCap_IsMovedToPrev_AndTheNewEntryStartsAFreshFile() {
            string log = Path.Combine(_dir, "critical_errors.log");
            File.WriteAllText(log, new string('x', 90));

            GlobalDiagnosticService.Append(log, new string('y', 20), maxBytes: 100);

            Assert.AreEqual(new string('y', 20), File.ReadAllText(log));
            Assert.AreEqual(new string('x', 90), File.ReadAllText(Path.Combine(_dir, "critical_errors-prev.log")));
        }

        [Test]
        public void AFileUnderTheCap_IsAppendedTo() {
            string log = Path.Combine(_dir, "critical_errors.log");
            File.WriteAllText(log, "a");

            GlobalDiagnosticService.Append(log, "b", maxBytes: 100);

            Assert.AreEqual("ab", File.ReadAllText(log));
            Assert.IsFalse(File.Exists(Path.Combine(_dir, "critical_errors-prev.log")));
        }

        [Test]
        public void ARunawayStackTrace_IsCut() {
            Exception e = Overflowed();

            string entry = GlobalDiagnosticService.Entry(e, terminating: true, new DateTime(2026, 11, 1));

            int lines = entry.Split('\n').Length;
            Assert.LessOrEqual(lines, GlobalDiagnosticService.MaxStackLines + 3);
            StringAssert.Contains("more lines", entry);
            StringAssert.Contains("(terminating)", entry);
        }

        private static Exception Overflowed() {
            try {
                Recurse(GlobalDiagnosticService.MaxStackLines + 50);
            } catch (InvalidOperationException e) {
                return e;
            }
            throw new AssertionException("no exception");
        }

        private static int Recurse(int depth) =>
            depth == 0 ? throw new InvalidOperationException("deep") : Recurse(depth - 1) + 1;
    }
}
