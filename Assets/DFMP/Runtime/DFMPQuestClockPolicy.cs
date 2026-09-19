using System;
using System.Collections.Generic;

namespace DFMP.Runtime
{
    public enum DFMPQuestClockKind
    {
        Unknown,
        Sequencing,
        FailureDeadline,
    }

    public static class DFMPQuestClockPolicy
    {
        // Generated from the Clock declarations in Assets/StreamingAssets/Quests.
        // Exact quest + clock identity prevents similarly-named mod clocks from being guessed.
        const string builtInClockCatalog =
            "$curevam:s.10,s.13,huntstart;$curewer:s.05,huntstart;00b00y00:1stparton;10c00y00:1stparton;20c00y00:1stparton,s.08;30c00y00:1stparton;40c00y00:1stparton,bonk,s.22,betrayquestor;50c00y00:1stparton;60c00y00:1stparton;70c00y00:1stparton;80c0xy00:1stparton;90c00y00:1stparton;__demo04:2dagger;__demo006:s.01,s.02;__demo09:2dung;__demo11:traveltime;__demo12:queston;__demo21:timer1;_brisien:invitepc,remindpc,pcfailed,oneday;_tutor__:page1,page2,page3,page4,page5,page6,page6a,page7,page8,page11,page12,page13,page14,page15;" +
            "a0c00y00:traveltime;a0c00y06:escapetime,shortdelay;a0c00y07:oneday;a0c00y08:s.00;a0c00y10:s.00,s.01,s.02;a0c00y11:questtime;a0c00y12:s.01;a0c00y14:oneday;a0c00y15:traveltime;a0c00y16:s.16,delay;a0c00y17:traveltime,extratime;a0c01y01:timer;a0c01y03:s.01;a0c01y06:escapetime,shortdelay,s.13;a0c01y09:questtime,shortdelay;a0c01y13:s.00;a0c0xy04:extratime,totaltime;a0c10y02:timeforq;a0c10y05:traveltime;a0c41y18:s.10;" +
            "b0b00y00:2mondung,s.07;b0b00y01:queston;b0b10y04:2mondung,2letter;b0b20y07:2dung;b0b40y08:2dung;b0b40y09:2dung;b0b50y11:2dung;b0b60y12:2dung;b0b70y14:2dung;b0b70y16:2dung;b0b71y03:finddaughter;b0b80y17:2dung;b0b81y02:2dung,s.30;b0c00y05:queston;b0c00y06:2dung;b0c00y10:2dung;b0c00y13:oneday;" +
            "c0b00y00:traveltime;c0b00y01:queston,s.04,s.06,s.14,s.16;c0b00y02:queston;c0b00y03:queston;c0b00y04:queston;c0b00y14:qtime;c0b10y05:traveltime;c0b10y06:traveltime;c0b10y07:traveltime;c0b10y15:qtime,shortdelay;c0b20y08:traveltime;c0b3xy09:traveltime;c0c00y10:questtime;c0c00y11:traveltime;c0c00y12:traveltime;c0c00y13:traveltime;custom01:timelimit;d0b00y00:1stparton;e0b00y00:1stparton;f0b00y00:1stparton;g0b00y00:queston;h0b00y00:1stparton;i0b00y00:1stparton;j0b00y00:1stparton;" +
            "k0c00y00:2storehouse,s.05;k0c00y02:2mondung;k0c00y03:2letter,queston;k0c00y04:queston;k0c00y05:queston,s.04;k0c00y06:letterarrivaltime,questtimelimit,delay;k0c00y07:queston,2ransom;k0c00y08:queston;k0c00y09:queston;k0c01y00:queston1,s.05;k0c01y10:queston;k0c0xy01:2mondung,s.05,s.06;k0c30y03:queston,s.03,s.13,s.26,s.27,2letter;" +
            "l0a01l00:traveltime,s.01;l0b00y00:queston;l0b00y01:queston;l0b00y02:queston;l0b00y03:queston;l0b10y01:1stparton,2ndparton;l0b10y03:queston;l0b20y02:1stparton;l0b30y03:1stparton;l0b30y09:1stparton;l0b40y04:1stparton;l0b50y11:s.00;l0b60y10:1stparton;" +
            "m0b00y00:1stparton;m0b00y06:2dung;m0b00y07:2dung;m0b00y15:2dung;m0b00y16:qtime;m0b00y17:qtime;m0b11y18:s.00,patsy,s.02,s.03,s.05,gettraitor,s.35;m0b1xy01:1stparton;m0b20y02:1stparton;m0b21y19:qtime,s.03,s.04;m0b30y03:1stparton;m0b30y04:1stparton;m0b30y08:2dung;m0b40y05:2dung,end;m0b50y09:2dung,end;m0b60y10:2dung;m0c00y11:2dung;m0c00y12:2dung;m0c00y13:2dung;m0c00y14:2dung;" +
            "n0b00y04:traveltime;n0b00y06:queston;n0b00y08:queston;n0b00y09:queston,s.02,s.05;n0b00y16:qtime;n0b00y17:time1,time2;n0b10y01:1stparton;n0b10y03:oneday,s.10;n0b11y18:qtime,s.04,s.05,s.06,healer;n0b20y02:oneday,s.09,s.12;n0b20y05:1stparton;n0b21y14:queston1,queston2;n0b30y15:traveltime,s.02,gothryd,s.08,s.09;n0b40y07:queston;n0c00y10:s.00,s.01;n0c00y11:traveltime,end;n0c00y12:1stparton;n0c00y13:traveltime;" +
            "o0a0al00:traveltime;o0b00y00:1stparton;o0b00y01:1stparton;o0b00y11:time,s.01;o0b00y12:qtime,s.01;o0b10y00:1stparton;o0b10y03:traveltime;o0b10y05:traveltime;o0b10y06:traveltime;o0b10y07:2palace;o0b20y02:traveltime;o0b2xy04:traveltime;o0b2xy08:traveltime;o0b2xy09:traveltime;o0b2xy10:traveltime;" +
            "p0a01l00:1stparton;p0b00l01:1stparton;p0b00l03:1stparton;p0b00l04:queston,s.05;p0b00l06:queston,2ndparton;p0b01l02:1stparton;p0b10l07:queston;p0b10l08:queston;p0b10l10:queston;p0b20l09:queston;q0c00y01:queston;q0c00y03:queston;q0c00y04:queston;q0c00y06:1stparton;q0c00y07:queston;q0c00y08:queston;q0c0xy02:queston;q0c10y00:queston;q0c20y02:1stparton;q0c4xy04:1stparton;" +
            "r0c10y00:1stparton,2ndparton;r0c10y01:queston,delay;r0c10y02:1stparton,2ndparton;r0c10y04:1stparton;r0c10y05:1stparton;r0c10y06:1stparton;r0c10y08:1stparton,2ndparton;r0c10y09:1stparton;r0c10y10:queston;r0c10y11:queston;r0c10y12:queston;r0c10y13:queston;r0c10y14:queston;r0c10y15:1stparton,2ndparton;r0c10y17:1stparton,2ndparton;r0c10y18:1stparton,2ndparton;r0c10y20:1stparton;r0c10y21:1stparton;r0c11y03:1stparton,2ndparton;r0c11y16:1stparton,2ndparton;r0c11y19:1stparton;r0c11y26:1stparton,2ndparton,s.03,total;r0c11y27:1stparton,2ndparton;r0c11y28:1stparton,2ndparton,total;r0c20y07:1stparton,2ndparton;r0c20y22:queston;r0c30y25:1stparton,2ndparton;r0c4xy23:queston;r0c60y24:queston;" +
            "s0000001:s.13;s0000002:s.00,1stparton,s.15;s0000003:2shedungent;s0000004:2ndgo,s.01,letterdelay;s0000005:2shedungent;s0000006:s.04,queston,s.16;s0000007:2mondung,2ndparton,delay,s.27,s.32;s0000008:brisiennafirstletter,delay,oneyear,akorithiletter,eadwyreletter,orcletter,kowletter,underkingletter,brisiennasecondletter;s0000009:giveletter,s.14;s0000010:s.04,itemindung;s0000011:s.01,s.11,s.18;s0000012:2palace,s.07;s0000013:2myndung,s.04;s0000016:delay;s0000017:2letter;s0000018:delay,s.01,s.06;s0000022:s.00;" +
            "s0000100:s.01,s.02;s0000101:s.01,s.02;s0000102:s.01,s.02;s0000103:s.01,s.02;s0000104:s.01,s.02;s0000106:delay;s0000500:s.00,firsttimer,executiondelay,escapetime;s0000501:s.00,patsy,time2;s0000502:s.00,s.03,towertime,s.18,outgoing;s0000503:s.00,s.02,s.10,s.11,keytime,s.21,s.31;s0000977:delay,s.05;s0000988:delay;s0000999:mainquestclock;t0c00y00:1stparton;u0c00y00:1stparton,escapetime;v0c00y00:1stparton;w0c00y00:1stparton;x0c00y00:1stparton;y0c00y00:1stparton;z0c00y00:1stparton";

        static readonly HashSet<string> builtInClocks = BuildCatalog();

        // These direct EndQuest clocks advance or clean up built-in narrative state.
        static readonly HashSet<string> sequencingEndClocks = new HashSet<string>(StringComparer.Ordinal)
        {
            Key("__DEMO21", "_timer1_"),
            Key("A0C00Y16", "_delay_"),
            Key("S0000002", "_S.15_"),
            Key("S0000004", "_letterdelay_"),
            Key("S0000007", "_delay_"),
            Key("S0000100", "_S.02_"),
            Key("S0000101", "_S.02_"),
            Key("S0000102", "_S.02_"),
            Key("S0000103", "_S.02_"),
            Key("S0000104", "_S.02_"),
            Key("S0000106", "_delay_"),
            Key("S0000502", "_outgoing_"),
            Key("S0000988", "_delay_"),
        };

        // Brisienna's penalty task starts the final cleanup timer rather than ending directly.
        static readonly HashSet<string> indirectFailureClocks = new HashSet<string>(StringComparer.Ordinal)
        {
            Key("_BRISIEN", "_pcfailed_"),
        };

        public static DFMPQuestClockKind Classify(
            string questName,
            string clockSymbol,
            IEnumerable<string> actionTypeNames)
        {
            string key = Key(questName, clockSymbol);
            if (!builtInClocks.Contains(key))
                return DFMPQuestClockKind.Unknown;

            bool hasEndQuest = false;
            if (actionTypeNames != null)
            {
                foreach (string actionTypeName in actionTypeNames)
                {
                    if (string.Equals(actionTypeName, "EndQuest", StringComparison.Ordinal))
                    {
                        hasEndQuest = true;
                        break;
                    }
                }
            }

            if (indirectFailureClocks.Contains(key))
                return hasEndQuest ? DFMPQuestClockKind.Unknown : DFMPQuestClockKind.FailureDeadline;
            if (sequencingEndClocks.Contains(key))
                return hasEndQuest ? DFMPQuestClockKind.Sequencing : DFMPQuestClockKind.Unknown;
            return hasEndQuest ? DFMPQuestClockKind.FailureDeadline : DFMPQuestClockKind.Sequencing;
        }

        public static bool ShouldSuppress(
            bool isMultiplayerClient,
            bool failureDeadlinesEnabled,
            DFMPQuestClockKind kind)
        {
            return isMultiplayerClient &&
                !failureDeadlinesEnabled &&
                kind == DFMPQuestClockKind.FailureDeadline;
        }

        static HashSet<string> BuildCatalog()
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            string[] quests = builtInClockCatalog.Split(';');
            foreach (string quest in quests)
            {
                string[] pair = quest.Split(':');
                if (pair.Length != 2)
                    continue;

                string[] clocks = pair[1].Split(',');
                foreach (string clock in clocks)
                    result.Add(Key(pair[0], clock));
            }

            return result;
        }

        static string Key(string questName, string clockSymbol)
        {
            return Normalize(questName) + ":" + Normalize(clockSymbol);
        }

        static string Normalize(string value)
        {
            return string.IsNullOrEmpty(value)
                ? string.Empty
                : value.Trim().Trim('_').ToLowerInvariant();
        }
    }
}
