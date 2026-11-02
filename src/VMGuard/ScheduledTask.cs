using System;
using System.Runtime.InteropServices;

namespace VMGuard
{
    /// <summary>
    /// Minimal wrapper over the Windows Task Scheduler API ("Schedule.Service"),
    /// used only to remove the "VMGuard" sign-in task that earlier versions created.
    /// </summary>
    internal static class ScheduledTask
    {
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
