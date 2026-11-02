using System;
using System.IO;
using System.Text;
using System.Threading;

namespace VMGuard
{
    /// <summary>
    /// Log de texto con fecha. Se escribe desde varios hilos (y puede haber una
    /// instancia en modo prueba junto a la instalada), asi que usa un mutex con nombre.
    /// </summary>
    internal sealed class Logger
    {
        private readonly string _path;
        private readonly bool _dryRun;
        private readonly object _lock = new object();

        public Logger(string path, bool dryRun)
        {
            _path = path;
            _dryRun = dryRun;
        }

        public string Path => _path;

        /// <summary>Si no es null, cada linea tambien se envia aqui (consola en modo prueba).</summary>
        public Action<string> Echo { get; set; }

        public void Write(string message)
        {
            if (_dryRun) message = "[DRYRUN] " + message;
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message;

            lock (_lock)
            {
                try { Echo?.Invoke(line); } catch { }

                using (var m = new Mutex(false, @"Local\VMGuardLog"))
                {
                    bool owned = false;
                    try
                    {
                        try { owned = m.WaitOne(5000); }
                        catch (AbandonedMutexException) { owned = true; }
                        File.AppendAllText(_path, line + Environment.NewLine, new UTF8Encoding(false));
                    }
                    catch
                    {
                        // Si no se puede escribir el log no hay mucho mas que hacer
                    }
                    finally
                    {
                        if (owned) try { m.ReleaseMutex(); } catch { }
                    }
                }
            }
        }
    }
}
