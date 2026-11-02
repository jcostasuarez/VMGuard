using System;
using System.Runtime.InteropServices;

namespace VMGuard
{
    /// <summary>
    /// Minimal wrapper over the Windows Task Scheduler API ("Schedule.Service"),
    /// used to register VMGuard to start at sign-in for the current user.
    /// </summary>
    internal static class ScheduledTask
    {
        private const int TASK_CREATE_OR_UPDATE = 6;
        private const int TASK_LOGON_INTERACTIVE_TOKEN = 3;

        /// <summary>Creates or replaces the task from its XML definition.</summary>
        public static void Register(string name, string xml)
        {
            dynamic folder = RootFolder();
            folder.RegisterTask(name, xml, TASK_CREATE_OR_UPDATE, null, null, TASK_LOGON_INTERACTIVE_TOKEN, null);
        }

        /// <summary>Starts the task now. Does nothing if it does not exist.</summary>
        public static void Run(string name)
        {
            dynamic task = Find(name);
            if (task != null) task.Run(null);
        }

        /// <summary>Stops the running instance of the task, if any.</summary>
        public static void Stop(string name)
        {
            dynamic task = Find(name);
            if (task == null) return;
            try { task.Stop(0); } catch (COMException) { }
        }

        /// <summary>Deletes the task. Does nothing if it does not exist.</summary>
        public static void Delete(string name)
        {
            if (Find(name) == null) return;
            dynamic folder = RootFolder();
            folder.DeleteTask(name, 0);
        }

        /// <summary>True if a task with this name exists.</summary>
        public static bool Exists(string name) => Find(name) != null;

        private static object Find(string name)
        {
            dynamic folder = RootFolder();
            try { return folder.GetTask(name); }
            catch (COMException) { return null; }
            catch (System.IO.FileNotFoundException) { return null; }
        }

        private static object RootFolder()
        {
            Type t = Type.GetTypeFromProgID("Schedule.Service", throwOnError: true);
            dynamic service = Activator.CreateInstance(t);
            service.Connect();
            return service.GetFolder("\\");
        }
    }
}
