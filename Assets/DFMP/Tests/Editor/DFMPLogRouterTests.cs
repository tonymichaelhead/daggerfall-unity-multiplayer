using System.IO;
using NUnit.Framework;
using DFMP.Runtime;

namespace DFMP.Tests
{
    [TestFixture]
    public class DFMPLogRouterTests
    {
        [Test]
        public void GetLogFileName_UsesRoleSpecificNames()
        {
            Assert.AreEqual("server.log", DFMPLogRouter.GetLogFileName(DFMPLogRole.Server, 0));
            Assert.AreEqual("server1.log", DFMPLogRouter.GetLogFileName(DFMPLogRole.Server, 1));
            Assert.AreEqual("client.log", DFMPLogRouter.GetLogFileName(DFMPLogRole.Client, 0));
            Assert.AreEqual("client3.log", DFMPLogRouter.GetLogFileName(DFMPLogRole.Client, 3));
            Assert.AreEqual("server-prev.log", DFMPLogRouter.GetPreviousLogFileName(DFMPLogRole.Server, 0));
            Assert.AreEqual("client-prev.log", DFMPLogRouter.GetPreviousLogFileName(DFMPLogRole.Client, 0));
            Assert.AreEqual("client3-prev.log", DFMPLogRouter.GetPreviousLogFileName(DFMPLogRole.Client, 3));
        }

        [Test]
        public void GetDefaultLogDirectory_UsesProjectLocalLogsFolder()
        {
            string expectedSuffix = Path.Combine("Logs", "DFMP");
            StringAssert.EndsWith(expectedSuffix, DFMPLogRouter.GetDefaultLogDirectory());
        }

        [Test]
        public void OpenRoleLogFile_NumberedClientLogWhenDefaultIsInUse()
        {
            string testDirectory = Path.Combine(Path.GetTempPath(), "DFMPLogRouterTests", TestContext.CurrentContext.Test.ID);
            Directory.CreateDirectory(testDirectory);

            FileStream firstClientLog = null;
            FileStream secondClientLog = null;

            try
            {
                string firstPath;
                firstClientLog = DFMPLogRouter.OpenRoleLogFile(testDirectory, DFMPLogRole.Client, out firstPath);

                string secondPath;
                secondClientLog = DFMPLogRouter.OpenRoleLogFile(testDirectory, DFMPLogRole.Client, out secondPath);

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
            string testDirectory = Path.Combine(Path.GetTempPath(), "DFMPLogRouterTests", TestContext.CurrentContext.Test.ID);
            Directory.CreateDirectory(testDirectory);
            string expectedPath = Path.Combine(testDirectory, "server.log");
            string expectedPreviousPath = Path.Combine(testDirectory, "server-prev.log");
            File.WriteAllText(expectedPath, "existing log line\n");

            FileStream serverLog = null;

            try
            {
                string actualPath;
                serverLog = DFMPLogRouter.OpenRoleLogFile(testDirectory, DFMPLogRole.Server, out actualPath);

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