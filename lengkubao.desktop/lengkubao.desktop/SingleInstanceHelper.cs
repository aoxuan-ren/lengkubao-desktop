using System;
using System.Threading;

namespace lengkubao.desktop
{
    internal static class SingleInstanceHelper
    {
        private const string MutexName = @"Global\LengKuBao.Desktop.SingleInstance";
        private const string ShowWindowEventName = @"Global\LengKuBao.Desktop.ShowMainWindow";
        private static EventWaitHandle ownerShowWindowEvent;

        public static bool TryAcquireMutex(out Mutex mutex)
        {
            bool createdNew;
            mutex = new Mutex(true, MutexName, out createdNew);
            if (!createdNew)
            {
                mutex.Dispose();
                mutex = null;
                return false;
            }
            return true;
        }

        public static void SignalExistingInstance()
        {
            try
            {
                using (var evt = EventWaitHandle.OpenExisting(ShowWindowEventName))
                {
                    evt.Set();
                }
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // 首个实例仍在启动中，忽略
            }
        }

        public static EventWaitHandle CreateShowWindowEvent()
        {
            return new EventWaitHandle(false, EventResetMode.AutoReset, ShowWindowEventName);
        }

        public static void PrepareOwnerInstance()
        {
            if (ownerShowWindowEvent == null)
                ownerShowWindowEvent = CreateShowWindowEvent();
        }

        public static EventWaitHandle GetOwnerShowWindowEvent()
        {
            return ownerShowWindowEvent;
        }
    }
}
