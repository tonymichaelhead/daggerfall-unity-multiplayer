using System.IO;
using NUnit.Framework;
using DFCoop.Runtime;

namespace DFCoop.Tests
{
    [TestFixture]
    public class DFCoopLogRouterTests
    {
        [Test]
        public void GetLogFileName_UsesRoleSpecificNames()
        {
            Assert.AreEqual("server.log", DFCoopLogRouter.GetLogFileName(DFCoopLogRole.Server, 0));
            Assert.AreEqual("server1.log", DFCoopLogRouter.GetLogFileName(DFCoopLogRole.Server, 1));
            Assert.AreEqual("client.log", DFCoopLogRouter.GetLogFileName(DFCoopLogRole.Client, 0));
            Assert.AreEqual("client3.log", DFCoopLogRouter.GetLogFileName(DFCoopLogRole.Client, 3));
            Assert.AreEqual("server-prev.log", DFCoopLogRouter.GetPreviousLogFileName(DFCoopLogRole.Server, 0));
            Assert.AreEqual("client-prev.log", DFCoopLogRouter.GetPreviousLogFileName(DFCoopLogRole.Client, 0));
            Assert.AreEqual("client3-prev.log", DFCoopLogRouter.GetPreviousLogFileName(DFCoopLogRole.Client, 3));
        }

        [Test]
        public void GetDefaultLogDirectory_UsesProjectLocalLogsFolder()
        {
            string expectedSuffix = Path.Combine("Logs", "DFCoop");
            StringAssert.EndsWith(expectedSuffix, DFCoopLogRouter.GetDefaultLogDirectory());
        }

        [Test]
        public void OpenRoleLogFile_NumberedClientLogWhenDefaultIsInUse()
        {
            string testDirectory = Path.Combine(Path.GetTempPath(), "DFCoopLogRouterTests", TestContext.CurrentContext.Test.ID);
            Directory.CreateDirectory(testDirectory);

            FileStream firstClientLog = null;
            FileStream secondClientLog = null;

            try
            {
                string firstPath;
                firstClientLog = DFCoopLogRouter.OpenRoleLogFile(testDirectory, DFCoopLogRole.Client, out firstPath);

                string secondPath;
                secondClientLog = DFCoopLogRouter.OpenRoleLogFile(testDirectory, DFCoopLogRole.Client, out secondPath);

                Assert.AreEqual(Path.Combine(testDirectory, "client.log"), firstPath);
                Assert.AreEqual(Path.Combine(testDirectory, "client1.log"), secondPath);
            }
            finally
            {
                if (secondClientLog != null)
                    secondClientLog.Dispose();

                if (firstClientLog != null)
                    firstClientLog.Dispose();

                if (Directory.Exists(testDirectory))
                    Directory.Delete(testDirectory, true);
            }
        }

        [Test]
        public void OpenRoleLogFile_RotatesExistingLogFileToPrevious()
        {
            string testDirectory = Path.Combine(Path.GetTempPath(), "DFCoopLogRouterTests", TestContext.CurrentContext.Test.ID);
            Directory.CreateDirectory(testDirectory);
            string expectedPath = Path.Combine(testDirectory, "server.log");
            string expectedPreviousPath = Path.Combine(testDirectory, "server-prev.log");
            File.WriteAllText(expectedPath, "existing log line\n");

            FileStream serverLog = null;

            try
            {
                string actualPath;
                serverLog = DFCoopLogRouter.OpenRoleLogFile(testDirectory, DFCoopLogRole.Server, out actualPath);

                Assert.AreEqual(expectedPath, actualPath);
                Assert.IsTrue(File.Exists(expectedPath));
                Assert.IsTrue(File.Exists(expectedPreviousPath));
                serverLog.Dispose();
                serverLog = null;

                Assert.AreEqual(string.Empty, File.ReadAllText(expectedPath));
                StringAssert.Contains("existing log line", File.ReadAllText(expectedPreviousPath));
            }
            finally
            {
                if (serverLog != null)
                    serverLog.Dispose();

                if (Directory.Exists(testDirectory))
                    Directory.Delete(testDirectory, true);
            }
        }
    }
}