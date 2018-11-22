using System;
namespace Helper
{
    public static class CallbackTools
    {
        public static void Handle(ref Action handler, string title)
        {
            if (handler != null)
            {
                try
                {
                    handler();
                }
                catch (Exception exception)
                {
                    Helper.Console.Error("[CallbackTools.Handle()] {0}, ex= {1},\n\n StackTrace={2}", title, exception, exception.StackTrace);
                }
                finally
                {
                    handler = null;
                }
            }
        }

        public static void Handle<T>(ref Action<T> handler, T self, string title)
        {
            Handle<T, string>(ref handler, self, title, string.Empty);
        }

        public static void Handle<T, U>(ref Action<T> handler, T self, string title, U text)
        {
            if (handler != null)
            {
                try
                {
                    handler(self);
                }
                catch (Exception exception)
                {
                    Helper.Console.Error("[CallbackTools.Handle()] {0} {1}, ex= {2},\n\n StackTrace={3},\n\n this= {4}", title, text.ToString(), exception.ToString(), exception.StackTrace, self);
                }
                finally
                {
                    handler = null;
                }
            }
        }
    }
}

