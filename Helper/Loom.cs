using System;
using System.Collections;
using System.Threading;
using Helper.Extended;
namespace Helper
{
    public static class Loom
    {
        private static readonly object _locker = new object();
        private static readonly WaitCallback _lpfnRunAsyncAction = new WaitCallback(Loom._RunAsyncAction);
        private static readonly ArrayList _receivedActions = new ArrayList();
        private static readonly ArrayList _tempActions = new ArrayList();

        private static void _RunAsyncAction(object state)
        {
            Action handler = state as Action;
            CallbackTools.Handle(ref handler, "[Loom._RunAsyncAction()]");
        }

        public static void QueueOnMainThread(Action action)
        {
            if (action == null) return;
            lock (_locker)
            {
                _receivedActions.Add(action);
            }
        }

        public static void RunAsync(Action action)
        {
            if (action != null)
            {
                ThreadPool.QueueUserWorkItem(_lpfnRunAsyncAction, action);
            }
        }

        internal static void Tick()
        {
            if (_receivedActions.Count <= 0) return;
            int num = _receivedActions.MoveToEx(_tempActions, _locker);
            for (int i = 0; i < num; i++)
            {
                Action handler = _tempActions[i] as Action;
                CallbackTools.Handle(ref handler, "[Loom._Tick()]");
            }
            _tempActions.Clear();
        }
    }
}

