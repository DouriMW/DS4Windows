﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DS4Control
{
    public class Log
    {
        public static event EventHandler<DebugEventArgs> TrayIconLog;
        public static event EventHandler<DebugEventArgs> GuiLog;

        public static void LogToGui(string data, bool warning)
        {
            if (GuiLog != null)
            {
                GuiLog(null, new DebugEventArgs(data, warning));
            }
        }

        public static void LogToTray(string data)
        {
            if (TrayIconLog != null)
            {
                TrayIconLog(null, new DebugEventArgs(data, false));
            }
        }
    }
}

