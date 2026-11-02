using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace VMGuard
{
    /// <summary>Finds vmrun.exe: explicit path, VMware registry keys, PATH and Program Files.</summary>
    internal static class VmrunLocator
    {
        private static readonly string[] RegistryKeys =
        {
            @"SOFTWARE\VMware, Inc.\VMware Workstation",
            @"SOFTWARE\VMware, Inc.\VMware VIX",
        };

        public static string Find(string explicitPath)
        {
            foreach (string c in Candidates(explicitPath))
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(c) && File.Exists(c)) return Path.GetFullPath(c);
                }
                catch
                {
                    // Malformed path: ignore it
                }
            }
            return null;
        }

        private static IEnumerable<string> Candidates(string explicitPath)
        {
            if (!string.IsNullOrWhiteSpace(explicitPath))
            {
                yield return Environment.ExpandEnvironmentVariables(explicitPath);
                // If the given path does not exist, do not keep looking: the user asked for that one
                yield break;
            }

            // Registry (32- and 64-bit views, i.e. WOW6432Node and the regular key)
            foreach (RegistryView view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            {
                foreach (string key in RegistryKeys)
                {
                    string dir = ReadInstallPath(view, key);
                    if (!string.IsNullOrEmpty(dir)) yield return Path.Combine(dir, "vmrun.exe");
                }
            }

            // PATH
            string pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string dir in pathVar.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string d = dir.Trim().Trim('"');
                if (d.Length > 0) yield return SafeCombine(d, "vmrun.exe");
            }

            // Default folders
            foreach (string pf in new[]
            {
                Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
                Environment.GetEnvironmentVariable("ProgramFiles"),
                Environment.GetEnvironmentVariable("ProgramW6432"),
            })
            {
                if (!string.IsNullOrEmpty(pf)) yield return Path.Combine(pf, @"VMware\VMware Workstation\vmrun.exe");
            }
        }

        private static string ReadInstallPath(RegistryView view, string subKey)
        {
            try
            {
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                using (var k = hklm.OpenSubKey(subKey))
                {
                    return k?.GetValue("InstallPath") as string;
                }
            }
            catch
            {
                return null;
            }
        }

        private static string SafeCombine(string dir, string file)
        {
            try { return Path.Combine(dir, file); } catch { return null; }
        }
    }
}
