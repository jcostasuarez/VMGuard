using System;
using System.IO;
using System.Text;
using System.Threading;

namespace VMGuard
{
    /// <summary>
    /// Timestamped text log. It is written from several threads (and a dry-run
    /// instance may run next to the installed one), so it uses a named mutex.
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

        /// <summary>If not null, every line is also sent here (console in dry-run mode).</summary>
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
                        // If the log cannot be written there is not much else to do
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
