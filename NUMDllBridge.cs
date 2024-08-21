using IGEMS.ILISP;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace NUMSTM
{
    public class NUMDllBridge
    {

        static FxServerProxy.FxServer server=null;
        static bool isConnected = false;
        static LispEngine self;
        static double[] axes = new double[20]; // maybe we have at most 9 axes, allocate 20 to be sure...
        static int currentProgram = 0;
        static string currentMode = string.Empty;
        static int currentModeIndex = -1;
        static bool currentProgramActive = false;
        static bool currentProgramStopped = false;
        static bool currentReset = false;
        static bool currentStart = false;
        static FxServerProxy.FileLoadingArgs.LoadingStatus currentLoadStatus = FxServerProxy.FileLoadingArgs.LoadingStatus.not_activated;
        static Timer timer = null;
        static int debugmode = 0; // bit1=message when connected, bit2=message when disconnected


        static bool postProcessMode = false; // means no timer and not $x,$y etc. updating

    
        private static void KillTimer()
        {
            if (timer != null)
            {
                timer.Stop();
                timer.Dispose();
                timer = null;
            }
        }

        private static void OnLispExit()
        {
            if(!postProcessMode)
                KillTimer();
            server.Disconnect(); // should set isConnected to false in callback
            server = null;
        }

        [LispSharp("NUMSTM-INIT")]
        static object NUMSTMInit(LispEngine L, Cons args)
        {
            self = L;

            int timerdelay = 0;
            L.GetIntArg(ref args, ref timerdelay);

            /* if (args != null)
             { // optional postprocess mode flag
                 object procmod=null;
                 L.GetArg(ref args, ref procmod, true);
                 if (procmod != null)
                     postProcessMode = true;
                 else
                     postProcessMode = false;

             }*/

            postProcessMode = (timerdelay <= 0);

            L.CheckArgSentinel(args);



            // setup some default lisp variables
            if (!postProcessMode)
            {
                L["$X"] = 0.0;
                L["$Y"] = 0.0;
                L["$Z"] = 0.0;
                L["$A"] = 0.0;
                L["$B"] = 0.0;
                L["$C"] = 0.0;
            }


            L.RegisterCallAtExit("NUMSTM", OnLispExit);


            if (server == null)
            {
                server = new FxServerProxy.FxServer();

                server.AxisPositionEvent += Server_AxisPositionEvent;
                server.ConnectionStatusEvent += Server_ConnectionStatusEvent; // still need thoose in postprocess mode for waiting tieouts when connecting etc.
                server.FileLoadingEvent += Server_FileLoadingEvent;
                server.ProgramStatusEvent += Server_ProgramStatusEvent;
            }


            if (!isConnected)
            {
                server.Connect();
                WaitUntilReallyConnected(10000);
            }


            if (!postProcessMode)
            {
                KillTimer();
                timer = new Timer();
                timer.Interval = timerdelay;
                timer.Tick += Timer_Tick;
                timer.Start();
            }

            return null;
        }


        [LispSharp("NUMSTM-DEBUGMODE")]
        static object NUMSTMDebugMode(LispEngine L, Cons args)
        {
            int mode = 0;
            L.GetIntArg(ref args, ref mode);
            L.CheckArgSentinel(args);
            debugmode = mode;
            return null;
        }

            private static void WaitUntilReallyConnected(int timeout_ms)
        {
            Stopwatch sw = Stopwatch.StartNew();

            while (!isConnected)
            {
                if (sw.ElapsedMilliseconds > timeout_ms)
                {
                    if (self != null)
                        Errors.Error(self, "Fatal error: failed to establish a connection to the controller within " + timeout_ms.ToString() + " milliseconds");
                    else
                        MessageBox.Show("Fatal error: failed to establish a connection to the controller within " + timeout_ms.ToString() + " milliseconds");
                    // Application.DoEvents();
                }
            }


        }

        private static void Timer_Tick(object sender, EventArgs e)
        {

            if (!isConnected)
                return; // protect from callback when not connected


            LispFunction callback = null;

            if(self!=null)
                callback=self["NUMSTM-TICK"] as LispFunction;

            if (callback != null)
            {
                try
                {
                    self.CallLispFunction(callback, null);
                }
                catch (Exception exc)
                {
                    KillTimer();
                    MessageBox.Show("NUMSTM timer stopped due to error in callback function NUMSTM-TICK");
                    throw;
                }
            }
        }

        private static void Server_ProgramStatusEvent(object sender, FxServerProxy.ProgramStatusArgs e)
        {
            currentProgram = e.CurrentProgram;
            currentMode = e.Mode;
            currentModeIndex = e.ModeIdx;
            currentProgramActive = e.ProgramActive;
            currentProgramStopped = e.ProgramStopped;
            currentReset = e.Reset;
            currentStart = e.Start;

            if (!postProcessMode && self != null)
            {
                self["$CURPROG"] = e.CurrentProgram;
                self["$MODE"] = e.Mode;
                self["$MODEIDX"] = e.ModeIdx;
                self["$PROGACTIVE"] = e.ProgramActive ? TypeT.T : null;
                self["$PROGSTOPPED"] = e.ProgramStopped ? TypeT.T : null;
                self["$RESET"] = e.Reset ? TypeT.T : null;
                self["$START"] = e.Start ? TypeT.T : null;
            }
        }

        private static void Server_FileLoadingEvent(object sender, FxServerProxy.FileLoadingArgs e)
        {
            currentLoadStatus = e.Status;

            if (!postProcessMode && self != null)
                self["$LOADSTATUS"] = (int)currentLoadStatus;

            /*
            canceled = 0,
            loading = 1,
            failed = 2,
            loaded = 3,
            activated = 4,
            not_activated = 5 */
        }

        private static void Server_ConnectionStatusEvent(object sender, FxServerProxy.ConnectionEventArgs e)
        {
                if (e.Status == FxServerProxy.ConnectionEventArgs.ConnectionStatus.Connected)
                {
                    isConnected = true;

                    if ((debugmode & 1) != 0)
                        MessageBox.Show("isConnected=T");
                }
                else if (e.Status == FxServerProxy.ConnectionEventArgs.ConnectionStatus.NotConnected)
                {
                    isConnected = false;

                    if ((debugmode & 2) != 0)
                        MessageBox.Show("isConnected=nil");
                }
                /*else if (e.Status == FxServerProxy.ConnectionEventArgs.ConnectionStatus.CommandVariables_Init_failed)
                {
                    // Do nothing 
                    int debug = 0;
                }
                else if (e.Status == FxServerProxy.ConnectionEventArgs.ConnectionStatus.ProgramVariables_Init_failed)
                {
                    // Do nothing 
                    int debug = 0;
                }*/
                else
                {
                    // some kind of error
                    if (self != null)
                        Errors.Error(self, "Failed NUM connection: " + e.Status.ToString());
                    else
                        MessageBox.Show("Failed NUM connection: " + e.Status.ToString());
                }
        }

        private static void Server_AxisPositionEvent(object sender, FxServerProxy.AxisPositionArgs e)
        {
            axes[e.Idx] = e.AxisPosition;

            if (!postProcessMode && self != null)
            {
                self["$" + e.AxisName.ToUpperInvariant()] = e.AxisPosition;
            }
        }


        [LispSharp("NUMSTM-ACTIVATEPROGRAM")]
        static object NUMSTMActivateProgram(LispEngine L, Cons args)
        {
            if (server == null)
                return -101;
            if(!isConnected)
                return -102;

            int prg = -1;
            L.GetIntArg(ref args, ref prg);
            return server.ActivateProgram(prg);

            // -1: not connected
            // -2: other file transfer active
            // -3: program number currently running, transfer not started
        }

        [LispSharp("NUMSTM-LOADFILE")]
        static object NUMSTMLoadFile(LispEngine L, Cons args)
        {
            if (server == null)
                return -103;
            if (!isConnected)
                return -104;

            string fname = string.Empty;
            int timeout_ms = 0;
            int prg = -1;

            L.GetStringArg(ref args, ref fname);
            L.GetIntArg(ref args, ref prg);
            L.GetIntArg(ref args, ref timeout_ms);
            int res = server.LoadFile(fname, prg);

            if (res < 0)
                return res;


            Stopwatch sw = Stopwatch.StartNew();
            currentLoadStatus = (FxServerProxy.FileLoadingArgs.LoadingStatus)(-1);
            while (currentLoadStatus != FxServerProxy.FileLoadingArgs.LoadingStatus.loaded)
            {
                if (sw.ElapsedMilliseconds > timeout_ms)
                {
                    res = -100; // IGEMS timeout error
                    break;
                }
            }

            // -1: not connected
            // -2: other file transfer active
            // -3: program number currently running, transfer not started

            return res;
        }

        [LispSharp("NUMSTM-NCRESET")]
        static object NUMSTMReset(LispEngine L, Cons args)
        {
            if (server == null)
                return -105;
            if (!isConnected)
                return -106;

            return server.NcReset();
        }

        [LispSharp("NUMSTM-NCSTART")]
        static object NUMSTMStart(LispEngine L, Cons args)
        {
            if (server == null)
                return -107;
            if (!isConnected)
                return -108;

            return server.NcStart();
        }

        [LispSharp("NUMSTM-NCSTOP")]
        static object NUMSTMStop(LispEngine L, Cons args)
        {
            if (server == null)
                return -109;
            if (!isConnected)
                return -110;

            return server.NcStop();
        }

        [LispSharp("NUMSTM-SETAUTOMODE")]
        static object NUMSTMSetAutoMode(LispEngine L, Cons args)
        {
            if (server == null)
                return -111;
            if (!isConnected)
                return -112;

            return server.SetAutoMode();
        }

        [LispSharp("NUMSTM-SETMANUALMODE")]
        static object NUMSTMSetManualMode(LispEngine L, Cons args)
        {

            if (server == null)
                return -113;
            if (!isConnected)
                return -114;

            return server.SetManualMode();
        }

        [LispSharp("NUMSTM-WRITEE8")]
        static object NUMSTMWriteE8(LispEngine L, Cons args)
        {
            if (server == null)
                return -115;
            if (!isConnected)
                return -116;



            int e8x = -1;
            int prg = -1;
            L.GetIntArg(ref args, ref e8x);
            L.GetIntArg(ref args, ref prg);
            return server.WriteE8xxxx(e8x, prg);
        }

        
    }
}
