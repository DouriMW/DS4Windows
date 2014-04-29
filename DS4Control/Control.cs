﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DS4Library;
namespace DS4Control
{
    public class Control
    {
        X360Device x360Bus;
        DS4Device[] DS4Controllers = new DS4Device[4];
        //TPadModeSwitcher[] modeSwitcher = new TPadModeSwitcher[4];
        Mouse[] touchPad = new Mouse[4];
        private bool running = false;
        private DS4State[] MappedState = new DS4State[4];
        private DS4State[] CurrentState = new DS4State[4];
        private DS4State[] PreviousState = new DS4State[4];
        public DS4StateExposed[] ExposedState = new DS4StateExposed[4];

        public event EventHandler<DebugEventArgs> Debug = null;

        private class X360Data
        {
            public byte[] Report = new byte[28];
            public byte[] Rumble = new byte[8];
        }
        private X360Data[] processingData = new X360Data[4];

        public Control()
        {
            x360Bus = new X360Device();
            for (int i = 0; i < DS4Controllers.Length; i++)
            {
                processingData[i] = new X360Data();
                MappedState[i] = new DS4State();
                CurrentState[i] = new DS4State();
                PreviousState[i] = new DS4State();
                ExposedState[i] = new DS4StateExposed(CurrentState[i]);
            }
            GiveMouses();
        }

        private void WarnExclusiveModeFailure(DS4Device device)
        {
            if (DS4Devices.isExclusiveMode && !device.IsExclusive)
            {
                String message = "Warning: Could not open DS4 " + device.MacAddress + " exclusively.  " +
                "You must quit other applications like Steam, Uplay before activating the 'Hide DS4 Controller' option.";
                LogDebug(message);
                Log.LogToTray(message);
            }
        }

        public bool Start()
        {
            if (x360Bus.Open() && x360Bus.Start())
            {
                LogDebug("Starting...");
                DS4Devices.isExclusiveMode = Global.getUseExclusiveMode();
                LogDebug("Searching for controllers....");
                LogDebug("Using " + (DS4Devices.isExclusiveMode ? "Exclusive Mode": "Shared Mode"));
                try
                {
                    DS4Devices.findControllers();
                    IEnumerable<DS4Device> devices = DS4Devices.getDS4Controllers();
                    int ind = 0;
                    foreach (DS4Device device in devices)
                    {
                        LogDebug("Found Controller: " + device.MacAddress + " (" + device.ConnectionType + ")");
                        WarnExclusiveModeFailure(device);
                        DS4Controllers[ind] = device;
                        device.Removal -= DS4Devices.On_Removal;
                        device.Removal += this.On_DS4Removal;
                        device.Removal += DS4Devices.On_Removal;
                        //TPadModeSwitcher m_switcher = new TPadModeSwitcher(device, ind);
                        //m_switcher.Debug += OnDebug;
                        //modeSwitcher[ind] = m_switcher;
                        touchPad[ind] = new Mouse(ind, device);
                        DS4Color color = Global.loadColor(ind);
                        device.LightBarColor = color;
                        x360Bus.Plugin(ind + 1);
                        device.Report += this.On_Report;
                        //m_switcher.setMode(Global.getInitialMode(ind));
                        TouchPadOn(ind, device);
                        ind++;
                        LogDebug("Controller: " + device.MacAddress + " is ready to use");
                        Log.LogToTray("Controller: " + device.MacAddress + " is ready to use");
                        if (ind >= 4) // out of Xinput devices!
                            break;
                    }
                }
                catch (Exception e)
                {
                    LogDebug(e.Message);
                    Log.LogToTray(e.Message);
                }
                running = true;
                    
            }
            return true;
        }

        public bool Stop()
        {
            if (running)
            {
                running = false;
                LogDebug("Stopping X360 Controllers");
                bool anyUnplugged = false;
                for (int i = 0; i < DS4Controllers.Length; i++)
                {
                    if (DS4Controllers[i] != null)
                    {
                        CurrentState[i].Battery = PreviousState[i].Battery = 0; // Reset for the next connection's initial status change.
                        x360Bus.Unplug(i + 1);
                        anyUnplugged = true;
                        DS4Controllers[i] = null;
                        //modeSwitcher[i] = null;
                        touchPad[i] = null;
                    }
                }
                if (anyUnplugged)
                    System.Threading.Thread.Sleep(XINPUT_UNPLUG_SETTLE_TIME);
                x360Bus.Stop();
                LogDebug("Stopping DS4 Controllers");
                DS4Devices.stopControllers();
                LogDebug("Stopped DS4 Tool");
                Global.ControllerStatusChanged(this);
            }
            return true;

        }

        public bool HotPlug()
        {
            if (running)
            {
                DS4Devices.findControllers();
                IEnumerable<DS4Device> devices = DS4Devices.getDS4Controllers();
                foreach (DS4Device device in devices)
                {
                    if (device.IsDisconnecting)
                        continue;
                    if (((Func<bool>)delegate
                    {
                        for (Int32 Index = 0; Index < DS4Controllers.Length; Index++)
                            if (DS4Controllers[Index] != null &&
                                DS4Controllers[Index].MacAddress == device.MacAddress)
                                return true;
                        return false;
                    })())
                        continue;
                    for (Int32 Index = 0; Index < DS4Controllers.Length; Index++)
                        if (DS4Controllers[Index] == null)
                        {
                            LogDebug("Found Controller: " + device.MacAddress + " (" + device.ConnectionType + ")");
                            WarnExclusiveModeFailure(device);
                            DS4Controllers[Index] = device;
                            device.Removal -= DS4Devices.On_Removal;
                            device.Removal += this.On_DS4Removal;
                            device.Removal += DS4Devices.On_Removal;
                            //TPadModeSwitcher m_switcher = new TPadModeSwitcher(device, Index);
                            //m_switcher.Debug += OnDebug;
                            //modeSwitcher[Index] = m_switcher;
                            touchPad[Index] = new Mouse(Index, device); 
                            device.LightBarColor = Global.loadColor(Index);
                            device.Report += this.On_Report;
                            x360Bus.Plugin(Index + 1);
                            //m_switcher.setMode(Global.getInitialMode(Index));
                            TouchPadOn(Index, device);
                            LogDebug("Controller: " + device.MacAddress + " is ready to use");
                            Log.LogToTray("Controller: " + device.MacAddress + " is ready to use");
                            break;
                        }
                }
            }
            return true;
        }

        public void TouchPadOn(int ind, DS4Device device)
        {
            ITouchpadBehaviour tPad = touchPad[ind];
            device.Touchpad.TouchButtonDown += tPad.touchButtonDown;
            device.Touchpad.TouchButtonUp += tPad.touchButtonUp;
            device.Touchpad.TouchesBegan += tPad.touchesBegan;
            device.Touchpad.TouchesMoved += tPad.touchesMoved;
            device.Touchpad.TouchesEnded += tPad.touchesEnded;
            device.Touchpad.TouchUnchanged += tPad.touchUnchanged;
            //LogDebug("Touchpad mode for " + device.MacAddress + " is now " + tmode.ToString());
            //Log.LogToTray("Touchpad mode for " + device.MacAddress + " is now " + tmode.ToString());
            Global.ControllerStatusChanged(this);
        }

        public string getDS4ControllerInfo(int index)
        {
            if (DS4Controllers[index] != null)
            {
                DS4Device d = DS4Controllers[index];
                if (!d.IsAlive())
                    return "Connecting..."; // awaiting the first battery charge indication
                String battery;
                if (d.Charging)
                {
                    if (d.Battery >= 100)
                        battery = "Charged";
                    else
                        battery = "Charging:" + d.Battery + "%";
                }
                else
                {
                    battery = "Battery: " + d.Battery + "%";
                }
                return d.MacAddress + " (" + d.ConnectionType + "), " + battery;
                //return d.MacAddress + " (" + d.ConnectionType + "), Battery is " + battery + ", Touchpad in " + modeSwitcher[index].ToString();
            }
            else
                return String.Empty;
        }

        public string getShortDS4ControllerInfo(int index)
        {
            if (DS4Controllers[index] != null)
            {
                DS4Device d = DS4Controllers[index];
                String battery;
                if (!d.IsAlive())
                    battery = "...";
                if (d.Charging)
                {
                    if (d.Battery >= 100)
                        battery = "Full";
                    else
                        battery = ">" + d.Battery + '%';
                }
                else
                {
                    battery = d.Battery + "%";
                }
                return battery + ' ' + d.ConnectionType;
            }
            else
                return "None";
        }

        /* public string[] getAvailableControllerModes(int index)
        {
            List<string> modes = new List<String>();
            if (DS4Controllers[index] != null)
                foreach (ITouchpadBehaviour mode in modeSwitcher[index].getAvailableModes())
                    modes.Add(mode.ToString());
            return modes.ToArray();
        } 

        public string getDS4ControllerMode(int index)
        {
            if (DS4Controllers[index] != null)
            {
                DS4Device d = DS4Controllers[index];
                if (!d.IsAlive())
                    return null; // awaiting the first battery charge indication
                return modeSwitcher[index].ToString();
            }
            else
                return "couldn't find";
        } */

        private int XINPUT_UNPLUG_SETTLE_TIME = 250; // Inhibit races that occur with the asynchronous teardown of ScpVBus -> X360 driver instance.
        //Called when DS4 is disconnected or timed out
        protected virtual void On_DS4Removal(object sender, EventArgs e)
        {
            DS4Device device = (DS4Device)sender;
            int ind = -1;
            for (int i = 0; i < DS4Controllers.Length; i++)
                if (DS4Controllers[i] != null && device.MacAddress == DS4Controllers[i].MacAddress)
                    ind = i;
            if (ind != -1)
            {
                CurrentState[ind].Battery = PreviousState[ind].Battery = 0; // Reset for the next connection's initial status change.
                x360Bus.Unplug(ind + 1);
                LogDebug("Controller " + device.MacAddress + " was removed or lost connection");
                Log.LogToTray("Controller " + device.MacAddress + " was removed or lost connection");
                System.Threading.Thread.Sleep(XINPUT_UNPLUG_SETTLE_TIME);
                DS4Controllers[ind] = null;
                //modeSwitcher[ind] = null;
                touchPad[ind] = null;
                Global.ControllerStatusChanged(this);
            }
        }

        //Called every time the new input report has arrived
        protected virtual void On_Report(object sender, EventArgs e)
        {

            DS4Device device = (DS4Device)sender;

            int ind=-1;
            for (int i=0; i<DS4Controllers.Length; i++)
                if(device == DS4Controllers[i])
                    ind = i;

            if (ind!=-1)
            {
                device.getExposedState(ExposedState[ind], CurrentState[ind]);
                DS4State cState = CurrentState[ind];
                device.getPreviousState(PreviousState[ind]);
                DS4State pState = PreviousState[ind];
                if (pState.Battery != cState.Battery)
                    Global.ControllerStatusChanged(this);

                //bool wasButtonMouse = modeSwitcher[ind].getCurrentMode() is ButtonMouse;
                CheckForHotkeys(ind, cState, pState);
                //if (wasButtonMouse && modeSwitcher[ind].getCurrentMode() is ButtonMouse)
                {
                    //ButtonMouse mode = (ButtonMouse)modeSwitcher[ind].getCurrentMode();
                    // XXX so disgusting, need to virtualize this again
                   // mode.getDS4State().CopyTo(cState);
                }
                
                if (Global.getHasCustomKeysorButtons(ind))
                {
                    Mapping.MapCustom(ind, cState, MappedState[ind]);
                    cState = MappedState[ind];
                }

                // Update the GUI/whatever.
                DS4LightBar.updateLightBar(device, ind);

                x360Bus.Parse(cState, processingData[ind].Report, ind);
                // We push the translated Xinput state, and simultaneously we
                // pull back any possible rumble data coming from Xinput consumers.
                if (x360Bus.Report(processingData[ind].Report, processingData[ind].Rumble))
                {
                    Byte Big = (Byte)(processingData[ind].Rumble[3]);
                    Byte Small = (Byte)(processingData[ind].Rumble[4]);

                    if (processingData[ind].Rumble[1] == 0x08)
                    {
                        setRumble(Small, Big, ind);
                    }
                }

                // Output any synthetic events.
                Mapping.Commit(ind);
                // Pull settings updates.
                device.IdleTimeout = Global.getIdleDisconnectTimeout(ind);
            }
        }

        public void GiveMouses()
        {
            Mapping.GetMouses(ref touchPad);
        }
        bool touchreleased = true;
        byte oldtouchvalue;
        protected virtual void CheckForHotkeys(int deviceID, DS4State cState, DS4State pState)
        {
            DS4Device d = DS4Controllers[deviceID];
            if (cState.Touch1 && !pState.Share && !pState.Options)
            {
                /*if (cState.Share)
                    Global.setTouchSensitivity(deviceID, 0);
                else if (cState.Options)
                    Global.setTouchSensitivity(deviceID, 100); */
            }
            if (cState.Touch1 && pState.PS)
            {
                if (Global.getTouchSensitivity(deviceID) > 0 && touchreleased)
                {
                    oldtouchvalue = Global.getTouchSensitivity(deviceID);
                    Global.setTouchSensitivity(deviceID, 0);
                    LogDebug("Touchpad Movement is now " + (Global.getTouchSensitivity(deviceID) > 0 ? "On" : "Off"));
                    Log.LogToTray("Touchpad Movement is now " + (Global.getTouchSensitivity(deviceID) > 0 ? "On" : "Off"));
                    touchreleased = false;
                }
                else if (touchreleased)
                {
                    Global.setTouchSensitivity(deviceID, oldtouchvalue);
                    LogDebug("Touchpad Movement is now " + (Global.getTouchSensitivity(deviceID) > 0 ? "On" : "Off"));
                    Log.LogToTray("Touchpad Movement is now " + (Global.getTouchSensitivity(deviceID) > 0 ? "On" : "Off"));
                    touchreleased = false;
                }
            }
            else
                touchreleased = true;
        }

        public virtual void LogDebug(String Data)
        {
            Console.WriteLine(System.DateTime.UtcNow.ToString("o") + "> " + Data);
            if (Debug != null)
            {
                DebugEventArgs args = new DebugEventArgs(Data);
                OnDebug(this, args);
            }
        }

        public virtual void OnDebug(object sender, DebugEventArgs args)
        {
            if (Debug != null)
                Debug(this, args);
        }

        //sets the rumble adjusted with rumble boost
        public virtual void setRumble(byte heavyMotor, byte lightMotor, int deviceNum)
        {
            byte boost = Global.loadRumbleBoost(deviceNum);
            uint lightBoosted = ((uint)lightMotor * (uint)boost) / 100;
            if (lightBoosted > 255)
                lightBoosted = 255;
            uint heavyBoosted = ((uint)heavyMotor * (uint)boost) / 100;
            if (heavyBoosted > 255)
                heavyBoosted = 255;
            if (Global.getRumbleSwap(deviceNum))
                DS4Controllers[deviceNum].setRumble((byte)heavyBoosted, (byte)lightBoosted);
            else
                DS4Controllers[deviceNum].setRumble((byte)lightBoosted, (byte)heavyBoosted);
        }
    }
}