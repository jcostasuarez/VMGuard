using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace VMGuard
{
    /// <summary>Busca vmrun.exe: ruta indicada, registro de VMware, PATH y Program Files.</summary>
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
                    // Ruta mal formada: se ignora
                }
            }
            return null;
        }

        private static IEnumerable<string> Candidates(string explicitPath)
        {
            if (!string.IsNullOrWhiteSpace(explicitPath))
            {
                yield return Environment.ExpandEnvironmentVariables(explicitPath);
                // Si la ruta indicada no existe no se sigue buscando: el usuario pidio esa
                yield break;
            }

            // Registro (vista de 32 y 64 bits, equivale a WOW6432Node y la clave normal)
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

            // Carpetas por defecto
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
