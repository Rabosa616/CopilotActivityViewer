using System;
using System.IO;
using System.Linq;
using System.Text;
using CopilotActivityViewer.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CopilotActivityViewer.Tests
{
    [TestClass]
    public class EventLogAndCatalogTests
    {
        private string tempRoot;

        [TestInitialize]
        public void Setup()
        {
            tempRoot = Path.Combine(Path.GetTempPath(), "CopilotActivityViewerTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
        }

        [TestCleanup]
        public void Cleanup()
        {
            Directory.Delete(tempRoot, recursive: true);
        }

        [TestMethod]
        public void ReaderReturnsOnlyCompleteLinesIncludingSplitMultibyteCharacters()
        {
            var path = Path.Combine(tempRoot, "events.jsonl");
            var bytes = Encoding.UTF8.GetBytes("{\"a\":1}\r\n{\"b\":\"àé\"}\n");
            var split = Array.IndexOf(bytes, (byte)0xC3) + 1;

            File.WriteAllBytes(path, bytes.Take(split).ToArray());
            var reader = new EventLogReader(path);
            CollectionAssert.AreEqual(new[] { "{\"a\":1}" }, reader.ReadNewLines().ToArray());

            using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            {
                stream.Write(bytes, split, bytes.Length - split);
            }

            CollectionAssert.AreEqual(new[] { "{\"b\":\"àé\"}" }, reader.ReadNewLines().ToArray());
            Assert.IsEmpty(reader.ReadNewLines());
            Assert.IsFalse(reader.WasReset);

            File.WriteAllText(path, "{}\n");
            CollectionAssert.AreEqual(new[] { "{}" }, reader.ReadNewLines().ToArray());
            Assert.IsTrue(reader.WasReset);
        }

        [TestMethod]
        public void ReaderReadsWhileFileIsOpenForWriting()
        {
            var path = Path.Combine(tempRoot, "events.jsonl");
            using (var writer = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)))
            {
                writer.Write("{\"x\":1}\n");
                writer.Flush();
                var reader = new EventLogReader(path);
                Assert.HasCount(1, reader.ReadNewLines());
            }
        }

        [TestMethod]
        public void CatalogDiscoversSessionsAndReadsWorkspaceName()
        {
            var older = Path.Combine(tempRoot, "older");
            var newer = Path.Combine(tempRoot, "newer");
            var empty = Path.Combine(tempRoot, "no-events");
            Directory.CreateDirectory(older);
            Directory.CreateDirectory(newer);
            Directory.CreateDirectory(empty);
            File.WriteAllText(Path.Combine(older, "events.jsonl"), "{}\n");
            File.WriteAllText(Path.Combine(newer, "events.jsonl"), "{}\n");
            File.SetLastWriteTimeUtc(Path.Combine(older, "events.jsonl"), DateTime.UtcNow.AddHours(-1));
            File.WriteAllText(
                Path.Combine(newer, "workspace.yaml"),
                "id: newer\ncwd: D:\\projects\\Contoso.Shop\nname: \"first line \\\"quoted\\\"\\r\\nsecond line\"\nuser_named: false\n");

            var catalog = new SessionCatalog(new[] { new System.Collections.Generic.KeyValuePair<string, string>("VS", tempRoot) });
            var sessions = catalog.Discover();

            CollectionAssert.AreEqual(new[] { "newer", "older" }, sessions.Select(s => s.Id).ToArray());
            Assert.AreEqual("first line \"quoted\"", sessions[0].Title);
            Assert.AreEqual(@"D:\projects\Contoso.Shop", sessions[0].Cwd);
            StringAssert.Contains(sessions[0].Display, "VS · [Contoso.Shop] first line");
        }

        [TestMethod]
        public void DefaultRootsCoverVisualStudioSsmsOtherHostsAndCli()
        {
            var local = Path.Combine(tempRoot, "Microsoft");
            Directory.CreateDirectory(Path.Combine(local, "VisualStudio", @"CopilotCli\session-state"));
            Directory.CreateDirectory(Path.Combine(local, "SQL Server Management Studio", "22.0", @"CopilotCli\session-state"));
            Directory.CreateDirectory(Path.Combine(local, "SomeHost", @"CopilotCli\session-state"));
            Directory.CreateDirectory(Path.Combine(local, "Unrelated", "Cache"));

            var roots = SessionCatalog.DefaultRoots(local, tempRoot).ToList();

            Assert.AreEqual("VS", roots[0].Key);
            Assert.AreEqual("CLI", roots[^1].Key);
            CollectionAssert.AreEquivalent(
                new[]
                {
                    "VS|" + Path.Combine(local, "VisualStudio", @"CopilotCli\session-state"),
                    "SSMS|" + Path.Combine(local, "SSMS", @"CopilotCli\session-state"),
                    "SomeHost|" + Path.Combine(local, "SomeHost", @"CopilotCli\session-state"),
                    "SSMS|" + Path.Combine(local, "SQL Server Management Studio", "22.0", @"CopilotCli\session-state"),
                    "CLI|" + Path.Combine(tempRoot, ".copilot", "session-state"),
                },
                roots.Select(r => r.Key + "|" + r.Value).ToArray());

            var session = Path.Combine(local, "SQL Server Management Studio", "22.0", @"CopilotCli\session-state", "s1");
            Directory.CreateDirectory(session);
            File.WriteAllText(Path.Combine(session, SessionCatalog.EventsFileName), "{}\n");
            var found = new SessionCatalog(() => SessionCatalog.DefaultRoots(local, tempRoot)).Discover();
            Assert.AreEqual("SSMS", found.Single().Source);
        }

        [TestMethod]
        public void CatalogIgnoresMissingRoots()
        {
            var catalog = new SessionCatalog(new[] { new System.Collections.Generic.KeyValuePair<string, string>("VS", Path.Combine(tempRoot, "missing")) });
            Assert.IsEmpty(catalog.Discover());
        }
    }
}
