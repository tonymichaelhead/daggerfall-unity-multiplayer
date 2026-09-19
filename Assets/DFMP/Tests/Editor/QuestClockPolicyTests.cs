using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using DFMP.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace DFMP.Tests
{
    [TestFixture]
    public class QuestClockPolicyTests
    {
        [Test]
        public void BuiltInEndQuestClock_IsFailureDeadline()
        {
            DFMPQuestClockKind kind = DFMPQuestClockPolicy.Classify(
                "A0C00Y00",
                "_traveltime_",
                new[] { "EndQuest" });

            Assert.AreEqual(DFMPQuestClockKind.FailureDeadline, kind);
            Assert.IsTrue(DFMPQuestClockPolicy.ShouldSuppress(true, false, kind));
            Assert.IsFalse(DFMPQuestClockPolicy.ShouldSuppress(true, true, kind));
            Assert.IsFalse(DFMPQuestClockPolicy.ShouldSuppress(false, false, kind));
        }

        [Test]
        public void BuiltInSequencingClock_RemainsEnabled()
        {
            DFMPQuestClockKind kind = DFMPQuestClockPolicy.Classify(
                "_TUTOR__",
                "_page1_",
                new[] { "Prompt" });

            Assert.AreEqual(DFMPQuestClockKind.Sequencing, kind);
            Assert.IsFalse(DFMPQuestClockPolicy.ShouldSuppress(true, false, kind));
        }

        [TestCase("S0000100", "_S.02_")]
        [TestCase("S0000988", "_delay_")]
        public void BuiltInNarrativeCleanupEndClock_RemainsEnabled(string questName, string clockSymbol)
        {
            DFMPQuestClockKind kind = DFMPQuestClockPolicy.Classify(
                questName,
                clockSymbol,
                new[] { "EndQuest" });

            Assert.AreEqual(DFMPQuestClockKind.Sequencing, kind);
            Assert.IsFalse(DFMPQuestClockPolicy.ShouldSuppress(true, false, kind));
        }

        [Test]
        public void BrisiennaIndirectFailureClock_IsSuppressed()
        {
            DFMPQuestClockKind kind = DFMPQuestClockPolicy.Classify(
                "_BRISIEN",
                "_pcfailed_",
                new[] { "StartStopTimer", "ChangeReputeWith", "GivePc" });

            Assert.AreEqual(DFMPQuestClockKind.FailureDeadline, kind);
            Assert.IsTrue(DFMPQuestClockPolicy.ShouldSuppress(true, false, kind));
        }

        [Test]
        public void UnknownModdedClock_FailsClosed()
        {
            DFMPQuestClockKind kind = DFMPQuestClockPolicy.Classify(
                "MODQUEST",
                "_deadline_",
                new[] { "EndQuest" });

            Assert.AreEqual(DFMPQuestClockKind.Unknown, kind);
            Assert.IsFalse(DFMPQuestClockPolicy.ShouldSuppress(true, false, kind));
        }

        [Test]
        public void AlteredBuiltInException_FailsClosed()
        {
            DFMPQuestClockKind kind = DFMPQuestClockPolicy.Classify(
                "S0000100",
                "_S.02_",
                new[] { "CreateFoe" });

            Assert.AreEqual(DFMPQuestClockKind.Unknown, kind);
            Assert.IsFalse(DFMPQuestClockPolicy.ShouldSuppress(true, false, kind));
        }

        [Test]
        public void ShippedQuestClockAudit_ClassifiesEveryClock()
        {
            string questDirectory = Path.Combine(Application.dataPath, "StreamingAssets/Quests");
            int clockCount = 0;
            var unknown = new List<string>();

            foreach (string path in Directory.GetFiles(questDirectory, "*.txt"))
            {
                string source = File.ReadAllText(path);
                Match questMatch = Regex.Match(source, @"(?im)^\s*Quest:\s*(\S+)");
                string questName = questMatch.Success
                    ? questMatch.Groups[1].Value
                    : Path.GetFileNameWithoutExtension(path);

                MatchCollection clocks = Regex.Matches(source, @"(?im)^\s*Clock\s+(?<clock>\S+)");
                foreach (Match clockMatch in clocks)
                {
                    clockCount++;
                    string clock = clockMatch.Groups["clock"].Value;
                    string symbol = Regex.Escape(clock.Trim('_'));
                    Match taskMatch = Regex.Match(
                        source,
                        @"(?ims)^\s*_?" + symbol + @"_?\s+task:\s*$" +
                        @"(?<body>.*?)(?=^\s*$|^\s*(?:\S+\s+task:|variable\s+|until\s+.*performed:)|\z)");
                    bool hasEndQuest = taskMatch.Success &&
                        Regex.IsMatch(taskMatch.Groups["body"].Value, @"(?im)^\s*end quest\b");
                    string[] actionTypes = hasEndQuest
                        ? new[] { "EndQuest" }
                        : new string[0];

                    DFMPQuestClockKind kind = DFMPQuestClockPolicy.Classify(
                        questName,
                        clock,
                        actionTypes);
                    if (kind == DFMPQuestClockKind.Unknown)
                        unknown.Add(questName + ":" + clock);
                }
            }

            Assert.AreEqual(399, clockCount, "The shipped clock catalog changed; audit new clocks before classifying them.");
            Assert.IsEmpty(unknown, "Unclassified shipped clocks: " + string.Join(", ", unknown.ToArray()));
        }
    }
}
