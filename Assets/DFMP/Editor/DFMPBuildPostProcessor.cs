using System;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace DFMP.Editor
{
    /// <summary>
    /// Post-build processor for DFMP dedicated server binaries.
    /// Modifies the Windows PE header subsystem from Windows GUI (2) to Windows Console (3)
    /// so the executable attaches directly to PowerShell / cmd stdout and streams live logs.
    /// </summary>
    public static class DFMPBuildPostProcessor
    {
        private const int PE_HEADER_POINTER_OFFSET = 0x3C;
        private const int PE_SIGNATURE_AND_COFF_HEADER_SIZE = 24;
        private const int OPTIONAL_HEADER_SUBSYSTEM_OFFSET = 68;
        private const ushort SUBSYSTEM_WINDOWS_CUI = 3; // Console Subsystem

        [PostProcessBuild(100)]
        public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
        {
            if (target != BuildTarget.StandaloneWindows && target != BuildTarget.StandaloneWindows64)
                return;

            if (!File.Exists(pathToBuiltProject))
                return;

            try
            {
                byte[] data = File.ReadAllBytes(pathToBuiltProject);
                if (data.Length < 0x100)
                    return;

                int peOffset = BitConverter.ToInt32(data, PE_HEADER_POINTER_OFFSET);
                if (peOffset <= 0 || peOffset + PE_SIGNATURE_AND_COFF_HEADER_SIZE + OPTIONAL_HEADER_SUBSYSTEM_OFFSET >= data.Length)
                    return;

                // Check "PE\0\0" signature
                if (data[peOffset] != 'P' || data[peOffset + 1] != 'E' || data[peOffset + 2] != 0 || data[peOffset + 3] != 0)
                    return;

                int subsystemOffset = peOffset + PE_SIGNATURE_AND_COFF_HEADER_SIZE + OPTIONAL_HEADER_SUBSYSTEM_OFFSET;
                ushort currentSubsystem = BitConverter.ToUInt16(data, subsystemOffset);

                if (currentSubsystem != SUBSYSTEM_WINDOWS_CUI)
                {
                    data[subsystemOffset] = (byte)(SUBSYSTEM_WINDOWS_CUI & 0xFF);
                    data[subsystemOffset + 1] = (byte)((SUBSYSTEM_WINDOWS_CUI >> 8) & 0xFF);
                    File.WriteAllBytes(pathToBuiltProject, data);

                    Debug.Log($"[DFMP] Patched Windows PE Subsystem to Console (3) for '{Path.GetFileName(pathToBuiltProject)}'");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DFMP] Failed to patch PE Subsystem to Console: {ex.Message}");
            }
        }
    }
}
