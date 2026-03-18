//----------------------------------------------------------------------------------------------
//	Copyright © 2020 SIGMAKOKI Co.,LTD. All right reserved.
//	This class can be used by anyone provided that the copyright notice remains intact.
//	Program developed by ABED Toufik, checked and confirmed by K.Nobatake.
//
//	This class helper is used to operate SIGMAKOKI Controllers (GSC01, GIP-101B, SHOT302/304GS, Hit, HSC-103, PGC-04).
//	Use the class to build your own program.
//	We can provide support for the other controllers.
//	Contact us : Phone (+ 81-3-5638-8228) or sales@sigma-koki.com.
//----------------------------------------------------------------------------------------------

using System;
using System.IO.Ports;
using System.Text.RegularExpressions;
using System.Threading;

public partial class SKSampleClass
{
    public enum Controller_Type
    {
        SHOT_304GS,
        Hit_MV,
        PGC_04,
        SHOT_302GS,
        GIP_101B,
        HSC_103,
        GSC01
    }

    public SerialPort SK_SerialPort;
    private Controller_Type Controller;
    private int MaxAxis;
    private string ErrMsg;
    private string ErrTitle;
    public string CurPosText;
    public bool Language;
    public bool Hit_mode;

    public SKSampleClass()
    {
        SK_SerialPort = new SerialPort();
        SK_SerialPort.PortName = "COM1";
        SK_SerialPort.BaudRate = 9600;
        SK_SerialPort.NewLine = "\r\n";
        SK_SerialPort.ReadTimeout = 5000;
        SK_SerialPort.RtsEnable = true;
        ControllerType = Controller_Type.SHOT_304GS;
    }

    // Controller Type
    public Controller_Type ControllerType
    {
        get
        {
            return Controller;
        }

        set
        {
            Controller = value;
            if (Controller == Controller_Type.SHOT_302GS)
            {
                MaxAxis = 2;
            }
            else if (Controller == Controller_Type.GIP_101B || Controller == Controller_Type.GSC01)
            {
                MaxAxis = 1;
            }
            else if (Controller == Controller_Type.HSC_103)
            {
                Hit_mode = true;
                MaxAxis = 3;
            }
            else if (Controller == Controller_Type.PGC_04)
            {
                Hit_mode = true;
                MaxAxis = 4;
            }
            else if (Controller == Controller_Type.Hit_MV)
            {
                Hit_mode = true;
                MaxAxis = 4;
            }
            else
            {
                MaxAxis = 4;
            }
        }
    }

    // PortName
    public string PortName
    {
        get
        {
            return SK_SerialPort.PortName;
        }

        set
        {
            SK_SerialPort.PortName = value;
        }
    }

    // Baudrate
    public int Baudrate
    {
        get
        {
            return SK_SerialPort.BaudRate;
        }

        set
        {
            SK_SerialPort.BaudRate = value;
        }
    }

    // Delimiter
    public string Delimiter
    {
        get
        {
            return SK_SerialPort.NewLine;
        }

        set
        {
            SK_SerialPort.NewLine = value;
        }
    }

    // Timeout
    public int Timeout
    {
        get
        {
            return SK_SerialPort.ReadTimeout;
        }

        set
        {
            SK_SerialPort.ReadTimeout = value;
        }
    }

    // PortNames
    public string[] GetPortNames()
    {
        string[] sss;
        sss = SerialPort.GetPortNames();
        return sss;
    }

    // test the connection success
    public bool ConnectTest()
    {
        string sss;
        string st;
        string WkStr;
        try
        {
            // device open
            if (SGOpen() == false)
            {
                return false;
            }

            SK_SerialPort.DiscardInBuffer();

            // Q command send
            if (SGWrite(_StatusQ()) == false)
            {
                return false;
            }

            // response receive
            sss = string.Empty;
            if (SGRead(ref sss) == false)
            {
                return false;
            }

            // received character check
            WkStr = Get_Stage_Status(sss, 3, MaxAxis);
            st = WkStr.ToUpper();
            if (Equals(st, "R") || Equals(st, "B"))
            {
                return true;
            }
            else
            {
                return false;
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    public bool PortClose()
    {
        bool rtn;
        rtn = SGClose();
        return rtn;
    }

    // Wait to Ready
    public bool Wait_Ready()
    {
        string sss;
        string st;
        try
        {
            do
            {
                if (SGWrite(_StatusQ()) == false)
                {
                    return false;
                }

                // response received
                sss = null;
                if (SGRead(ref sss) == false)
                {
                    return false;
                }

                if (!Equals(sss, ""))
                {
                    if (Hit_mode == true)
                    {
                        st = get_status_hit(1);

                        if (Equals(st, "0"))
                        {
                            if (!Equals(st, "1"))
                            {
                                return true;
                            }
                        }
                    }
                    if (Hit_mode == false)
                    {
                        st = Get_Stage_Status(sss, 3, MaxAxis).ToUpper();
                        if (Equals(st, "R"))
                        {
                            return true;
                        }
                    }

                }
            }
            while (true);
        }

        catch (Exception)
        {
            return false;
        }
    }

    // Current position
    public int GetPosition()
    {
        string sss;
        string pos;

        // Ready confirmation
        if (Wait_Ready() == false)
        {
            return 0;
        }

        // Q command transmission
        if (SGWrite(_StatusQ()) == false)
        {
            return 0;
        }

        sss = null;
        if (SGRead(ref sss) == false)
        {
            return 0;
        }

        pos = Get_Coordinate_Value(sss, MaxAxis);
        return int.Parse(pos);
    }

    // Mechanical origin return
    public bool ReturnOrigin(int Axis)
    {
        string sss;

        // Ready confirmation
        if (Wait_Ready() == false)
        {
            return false;
        }

        // H command send
        if (SGWrite(_ReturnOrigin(Axis)) == false)
        {
            return false;
        }

        // response receive
        sss = null;
        if (SGRead(ref sss) == false)
        {
            return false;
        }

        // response OK?
        if (sss.IndexOf("OK") < 0)
        {
            return false;
        }

        // Ready confirmation
        if (Wait_Ready() == false)
        {
            return false;
        }

        return true;
    }

    // Set Hit Mode
    public bool HitMode()
    {
        string sss;

        if (SGWrite(_HitMode()) == false)
        {
            return false;
        }

        sss = null;
        if (SGRead(ref sss) == false)
        {
            return false;
        }

        if (sss.IndexOf("OK") < 0)
        {
            return false;
        }

        if (Wait_Ready() == false)
        {
            return false;
        }

        return true;
    }

    // Set Shot mode
    public bool ShotMode()
    {
        string sss;

        if (SGWrite(_ShotMode()) == false)
        {
            return false;
        }

        sss = null;
        if (SGRead(ref sss) == false)
        {
            return false;
        }

        if (sss.IndexOf("OK") < 0)
        {
            return false;
        }

        if (Wait_Ready() == false)
        {
            return false;
        }

        return true;
    }

    public bool MoveA(int Axis, int Vdata)
    {
        string sss;

        if (SGWrite(_MoveAbsolutely(Axis, Vdata)) == false)
        {
            return false;
        }

        sss = null;
        if (SGRead(ref sss) == false)
        {
            return false;
        }

        if (sss.IndexOf("OK") < 0)
        {
            return false;
        }

        if (Hit_mode == false)
        {
            if (SGWrite(_Go()) == false)
            {
                return false;
            }

            sss = null;
            if (SGRead(ref sss) == false)
            {
                return false;
            }

            if (sss.IndexOf("OK") < 0)
            {
                return false;
            }
        }

        if (Wait_Ready() == false)
        {
            return false;
        }

        return true;
    }

    // Movement (relative)
    public bool MoveR(int Axis, int Vdata)
    {
        string sss;

        if (SGWrite(_MoveRelativity(Axis, Vdata)) == false)
        {
            return false;
        }

        sss = null;
        if (SGRead(ref sss) == false)
        {
            return false;
        }

        if (sss.IndexOf("OK") < 0)
        {
            return false;
        }

        if (Hit_mode == false)
        {
            if (SGWrite(_Go()) == false)
            {
                return false;
            }

            sss = null;
            if (SGRead(ref sss) == false)
            {
                return false;
            }

            if (sss.IndexOf("OK") < 0)
            {
                return false;
            }
        }

        if (Wait_Ready() == false)
        {
            return false;
        }

        return true;
    }

    // Logical origin setting
    public bool ResetPosition(int Axis)
    {
        string sss;

        if (Wait_Ready() == false)
        {
            return false;
        }

        if (SGWrite(_ReturnLogicalOrigin(Axis)) == false)
        {
            return false;
        }

        sss = null;
        if (SGRead(ref sss) == false)
        {
            return false;
        }

        if (sss.IndexOf("OK") < 0)
        {
            return false;
        }

        if (Wait_Ready() == false)
        {
            return false;
        }

        return true;
    }

    // Logical origin return
    public bool ReturnLogicalOrigin(int Axis, int cPos = 0)
    {
        string sss;

        if (Wait_Ready() == false)
        {
            return false;
        }

        if (SGWrite(_MoveRelativity(Axis, cPos * -1)) == false)
        {
            return false;
        }

        sss = null;
        if (SGRead(ref sss) == false)
        {
            return false;
        }

        if (sss.IndexOf("OK") < 0)
        {
            return false;
        }

        if (SGWrite(_Go()) == false)
        {
            return false;
        }

        sss = null;
        if (SGRead(ref sss) == false)
        {
            return false;
        }

        if (sss.IndexOf("OK") < 0)
        {
            return false;
        }

        if (Wait_Ready() == false)
        {
            return false;
        }

        return true;
    }

    // Deceleration stop
    public bool StopStage(int Axis)
    {
        string sss;

        if (SGWrite(_StopStage(Axis)) == false)
        {
            return false;
        }

        sss = null;
        if (SGRead(ref sss) == false)
        {
            return false;
        }

        if (sss.IndexOf("OK") < 0)
        {
            return false;
        }

        return true;
    }

    // emergency stop
    public bool StopStageEmergency()
    {
        string sss;

        if (SGWrite(_StopStageEmergency()) == false)
        {
            return false;
        }

        sss = null;
        if (SGRead(ref sss) == false)
        {
            return false;
        }

        if (sss.IndexOf("OK") < 0)
        {
            return false;
        }

        return true;
    }

    // Position position write (GIP101)
    public bool SetPosition(int PNo, int Value)
    {
        string sss;

        if (SGWrite(_SetPosition(PNo, Value)) == false)
        {
            return false;
        }

        sss = null;
        if (SGRead(ref sss) == false)
        {
            return false;
        }

        if (sss.IndexOf("OK") < 0)
        {
            return false;
        }

        return true;
    }

    // Position position movement (GIP101)
    public bool MovePosition(int PNo)
    {
        string sss;

        if (SGWrite(_MovePosition(PNo)) == false)
        {
            return false;
        }

        sss = null;
        if (SGRead(ref sss) == false)
        {
            return false;
        }

        if (sss.IndexOf("OK") < 0)
        {
            return false;
        }

        if (SGWrite(_Go()) == false)
        {
            return false;
        }

        sss = null;
        if (SGRead(ref sss) == false)
        {
            return false;
        }

        if (sss.IndexOf("OK") < 0)
        {
            return false;
        }

        if (Wait_Ready() == false)
        {
            return false;
        }

        return true;
    }

    // Position position acquisition (GIP101)
    public string StatusP(int PNo)
    {
        string sss;

        if (SGWrite(_Status3(PNo.ToString())) == false)
        {
            return null;
        }

        sss = null;
        if (SGRead(ref sss) == false)
        {
            return null;
        }

        return sss;
    }

    public bool SGOpen()
    {
        try
        {
            if (SK_SerialPort.IsOpen == false)
            {
                SK_SerialPort.Open();
            }

            return true;
        }
        catch (Exception)
        {
            if (Language == true)
            {
                ErrMsg = "デバイスOPEN時にエラーが発生しました。";
                ErrTitle = "通信エラー";
            }
            else
            {
                ErrMsg = "Error occurred at the time of the device open.";
                ErrTitle = "Communication Error";
            }

            Console.Error.WriteLine($"{ErrTitle}: {ErrMsg}");
            return false;
        }
    }

    // Close
    public bool SGClose()
    {
        try
        {
            if (SK_SerialPort.IsOpen == true)
            {
                SK_SerialPort.Close();
            }

            return true;
        }
        catch (Exception)
        {
            if (Language == true)
            {
                ErrMsg = "デバイスCLOSE時にエラーが発生しました。";
                ErrTitle = "通信エラー";
            }
            else
            {
                ErrMsg = "Error occurred at the time of the device close.";
                ErrTitle = "Communication Error";
            }

            Console.Error.WriteLine($"{ErrTitle}: {ErrMsg}");
            return false;
        }
    }

    // Write
    public bool SGWrite(string Wcmd)
    {
        try
        {
            if (SK_SerialPort.CtsHolding != true)
            {
                if (Language == true)
                {
                    ErrMsg = "送信時にエラーが発生しました。";
                    ErrTitle = "通信エラー";
                }
                else
                {
                    ErrMsg = "Error occurred at the time of the transmission.";
                    ErrTitle = "Communication Error";
                }

                Console.Error.WriteLine($"{ErrTitle}: {ErrMsg}");
                return false;
            }

            SK_SerialPort.WriteLine(Wcmd);
            return true;
        }
        catch (Exception)
        {
            if (Language == true)
            {
                ErrMsg = "送信時にエラーが発生しました。";
                ErrTitle = "通信エラー";
            }
            else
            {
                ErrMsg = "Error occurred at the time of the transmission.";
                ErrTitle = "Communication Error";
            }

            Console.Error.WriteLine($"{ErrTitle}: {ErrMsg}");
            return false;
        }
    }

    // Read
    public bool SGRead(ref string Rcmd)
    {
        try
        {
            Rcmd = SK_SerialPort.ReadLine();
            return true;
        }
        catch (Exception)
        {
            if (Language == true)
            {
                ErrMsg = "受信時にエラーが発生しました。";
                ErrTitle = "通信エラー";
            }
            else
            {
                ErrMsg = "Error occurred at the time of the reception.";
                ErrTitle = "Communication Error";
            }

            Console.Error.WriteLine($"{ErrTitle}: {ErrMsg}");
            return false;
        }
    }

    // Moving direction (internal function)
    private string GetAxisDir(int Value)
    {
        if (Value < 0)
        {
            return "-";
        }
        else
        {
            return "+";
        }
    }

    // Moving direction2 (internal function)
    private string GetAxisDir2(bool Value)
    {
        if (Value == false)
        {
            return "-";
        }
        else
        {
            return "+";
        }
    }

    // Extract coordinate values from Q command
    private string inGet_Coordinate_Value(string Qstatus, int AxesNum, int Axis)
    {
        int stp;
        int AxLen;
        string BeforeStr;
        string AfterStr;
        AxLen = Get_Qlen(AxesNum);

        if (Hit_mode == true)
        {
            switch (Axis)
            {
                case 4:
                    {
                        stp = 3;
                        break;
                    }

                case 3:
                    {
                        stp = 2;
                        break;
                    }

                case 2:
                    {
                        stp = 1;
                        break;
                    }

                default:
                    {
                        stp = 0;
                        break;
                    }
            }

            int kk = Qstatus.Length;
            BeforeStr = Qstatus.Substring(stp, kk - stp);
            BeforeStr = BeforeStr.Substring(0, BeforeStr.IndexOf(","));
            AfterStr = BeforeStr.Replace(" ", "");
            return AfterStr;
        }
        else
        {
            if (Qstatus.Length < AxLen)
            {
                return "0";
            }

            switch (Axis)
            {
                case 4:
                    {
                        stp = 34;
                        break;
                    }

                case 3:
                    {
                        stp = 23;
                        break;
                    }

                case 2:
                    {
                        stp = 12;
                        break;
                    }

                default:
                    {
                        stp = 1;
                        break;
                    }
            }

            BeforeStr = Qstatus.Substring(stp - 1, 10);
            AfterStr = BeforeStr.Replace(" ", "");
            return AfterStr;
        }
    }

    // Extract status from Q-command
    private string inGet_Stage_Status(string Qstatus, int AxesNum, int ACKNumber)
    {
        int stp;
        int AxLen;
        AxLen = Get_Qlen(AxesNum);

        if (Qstatus.Length < AxLen)
        {
            return "0";
        }

        switch (AxesNum)
        {
            case 4:
                {
                    switch (ACKNumber)
                    {
                        case 1:
                            {
                                stp = 45;
                                break;
                            }

                        case 2:
                            {
                                stp = 47;
                                break;
                            }

                        default:
                            {
                                stp = 49;
                                break;
                            }
                    }

                    break;
                }

            case 3:
                {
                    switch (ACKNumber)
                    {
                        case 1:
                            {
                                stp = 34;
                                break;
                            }

                        case 2:
                            {
                                stp = 36;
                                break;
                            }

                        default:
                            {
                                stp = 38;
                                break;
                            }
                    }

                    break;
                }

            case 2:
                {
                    switch (ACKNumber)
                    {
                        case 1:
                            {
                                stp = 23;
                                break;
                            }

                        case 2:
                            {
                                stp = 25;
                                break;
                            }

                        default:
                            {
                                stp = 27;
                                break;
                            }
                    }

                    break;
                }

            default:
                {
                    switch (ACKNumber)
                    {
                        case 1:
                            {
                                stp = 12;
                                break;
                            }

                        case 2:
                            {
                                stp = 14;
                                break;
                            }

                        default:
                            {
                                stp = 16;
                                break;
                            }
                    }

                    break;
                }
        }

        return Qstatus.Substring(stp - 1, 1);
    }

    // Reply string length of Q command
    private int Get_Qlen(int AxesNum)
    {
        switch (AxesNum)
        {
            case 4:
                {
                    return 49;
                }

            case 3:
                {
                    return 38;
                }

            case 2:
                {
                    return 27;
                }

            case 1:
                {
                    return 16;
                }

            default:
                {
                    return 0;
                }
        }
    }

    // Return origin
    private string _ReturnOrigin(int Axis)
    {
        if (Hit_mode == true)
        {
            if (Axis == 0 || Axis == 1)
            {
                return "H:1";
            }
            else if (Axis == 2)
            {
                return "H:,1";
            }
            else if (Axis == 3)
            {
                return "H:,,1";
            }
            else if (Axis == 4)
            {
                return "H:,,,1";
            }
            else
            {
                return "";
            }

        }
        else
        {
            if (Axis == 0)
            {
                return "H:1";
            }
            else if (Axis >= 1 && Axis <= MaxAxis)
            {
                return "H:" + Axis.ToString();
            }
            else
            {
                return "";
            }

        }
    }

    // Move relativity
    private string _MoveRelativity(int Axis, int Value1)
    {
        string WkStr;
        if (Hit_mode == true)
        {
            if (Axis == 0 || Axis == 1)
            {
                WkStr = "M:" + GetAxisDir(Value1) + Math.Abs(Value1).ToString();
                return WkStr;
            }
            if (Axis == 2)
            {
                WkStr = "M:," + GetAxisDir(Value1) + Math.Abs(Value1).ToString();
                return WkStr;
            }
            if (Axis == 3)
            {
                WkStr = "M:,," + GetAxisDir(Value1) + Math.Abs(Value1).ToString();
                return WkStr;
            }
            if (Axis == 4)
            {
                WkStr = "M:,,," + GetAxisDir(Value1) + Math.Abs(Value1).ToString();
                return WkStr;
            }
            else
            {
                return "";
            }
        }
        else
        {
            if (Axis == 0)
            {
                WkStr = "M:1" + GetAxisDir(Value1) + "P" + Math.Abs(Value1).ToString();
                return WkStr;
            }
            else if (Axis >= 1 && Axis <= MaxAxis)
            {
                WkStr = "M:" + Axis.ToString() + GetAxisDir(Value1) + "P" + Math.Abs(Value1).ToString();
                return WkStr;
            }
            else
            {
                return "";
            }
        }
    }

    // Move absolutely
    private string _MoveAbsolutely(int Axis, int Value1)
    {

        string WkStr;
        if (Hit_mode == true)
        {
            if (Axis == 0 || Axis == 1)
            {
                WkStr = "A:" + GetAxisDir(Value1) + Math.Abs(Value1).ToString();
                return WkStr;
            }
            if (Axis == 2)
            {
                WkStr = "A:," + GetAxisDir(Value1) + Math.Abs(Value1).ToString();
                return WkStr;
            }
            if (Axis == 3)
            {
                WkStr = "A:,," + GetAxisDir(Value1) + Math.Abs(Value1).ToString();
                return WkStr;
            }
            if (Axis == 4)
            {
                WkStr = "A:,,," + GetAxisDir(Value1) + Math.Abs(Value1).ToString();
                return WkStr;
            }
            else
            {
                return "";
            }
        }
        else
        {
            if (Axis == 0)
            {
                WkStr = "A:1" + GetAxisDir(Value1) + "P" + Math.Abs(Value1).ToString();
                return WkStr;
            }
            else if (Axis >= 1 && Axis <= MaxAxis)
            {
                WkStr = "A:" + Axis.ToString() + GetAxisDir(Value1) + "P" + Math.Abs(Value1).ToString();
                return WkStr;
            }
            else
            {
                return "";
            }
        }
    }

    // Move with JOG
    private string _Jog(int Axis, bool Dir_Renamed)
    {
        string WkStr;
        if (Hit_mode == true)
        {
            if (Axis == 0 || Axis == 1)
            {
                WkStr = "J:" + GetAxisDir2(Dir_Renamed);
                return WkStr;
            }
            else if (Axis == 2)
            {
                WkStr = "J:," + GetAxisDir2(Dir_Renamed);
                return WkStr;
            }
            else if (Axis == 3)
            {
                WkStr = "J:,," + GetAxisDir2(Dir_Renamed);
                return WkStr;
            }
            else if (Axis == 4)
            {
                WkStr = "J:,,," + GetAxisDir2(Dir_Renamed);
                return WkStr;
            }
            else
            {
                return "";
            }

        }
        else
        {
            if (Axis == 0)
            {
                WkStr = "J:1" + GetAxisDir2(Dir_Renamed);
                return WkStr;
            }
            else if (Axis >= 1 && Axis <= MaxAxis)
            {
                WkStr = "J:" + Axis.ToString() + GetAxisDir2(Dir_Renamed);
                return WkStr;
            }
            else
            {
                return "";
            }
        }
    }

    // Drive
    private string _Go()
    {
        return "G:";
    }

    // Return logical origin
    private string _ReturnLogicalOrigin(int Axis)
    {
        if (Hit_mode == true)
        {
            if (Axis == 0 || Axis == 1)
            {
                return "R:1";
            }
            else if (Axis == 2)
            {
                return "R:,1";
            }
            else if (Axis == 3)
            {
                return "R:,,1";
            }
            else if (Axis == 4)
            {
                return "R:,,,1";
            }
            else
            {
                return "";
            }


        }
        else
        {
            if (Axis == 0)
            {
                return "R:1";
            }
            else if (Axis >= 1 && Axis <= MaxAxis)
            {
                return "R:" + Axis.ToString();
            }
            else
            {
                return "";
            }
        }
    }

    // Move Position
    private string _MovePosition(int BNo)
    {
        if (BNo == 0)
        {
            return "B:1";
        }
        else if (BNo >= 1 && BNo <= 5)
        {
            return "B:" + BNo.ToString();
        }
        else
        {
            return "";
        }
    }

    // Set Position
    private string _SetPosition(int BNo, int Value1)
    {
        string WkStr;
        if (BNo == 0)
        {
            WkStr = "P:B1" + GetAxisDir(Value1) + "P" + Math.Abs(Value1).ToString();
            return WkStr;
        }
        else if (BNo >= 1 && BNo <= 5)
        {
            WkStr = "P:B" + BNo.ToString() + GetAxisDir(Value1) + "P" + Math.Abs(Value1).ToString();
            return WkStr;
        }
        else
        {
            return "";
        }
    }

    // Emergency stop
    private string _StopStageEmergency()
    {
        return "L:E";
    }

    // Stop
    private string _StopStage(int Axis)
    {
        if (Hit_mode == true)
        {
            if (Axis == 0 || Axis == 1)
            {
                return "L:1";
            }
            else if (Axis == 2)
            {
                return "L:,1";
            }
            else if (Axis == 3)
            {
                return "L:,,1";
            }
            else if (Axis == 4)
            {
                return "L:,,,1";
            }
            else
            {
                return "";
            }

        }
        else
        {
            if (Axis == 0)
            {
                return "L:1";
            }
            else if (Axis >= 1 && Axis <= MaxAxis)
            {
                return "L:" + Axis.ToString();
            }
            else
            {
                return "";
            }
        }
    }

    // Speed
    private string _Speed(int Axis, int Slow, int Fast, int Rate_Renamed)
    {
        string WkStr;
        if (Hit_mode == true)
        {
            if (Slow < 1 || Slow > 999999999 || Fast < 1 || Fast > 999999999 || Rate_Renamed < 0 || Rate_Renamed > 1000)
            {
                return "";
            }
            if (Axis == 0 || Axis == 1)
            {
                WkStr = "D:1" + Math.Abs(Slow).ToString() + Math.Abs(Fast).ToString() + Math.Abs(Rate_Renamed).ToString();
                return WkStr;
            }
            if (Axis == 2 || Axis <= MaxAxis)
            {
                WkStr = "D:" + Axis.ToString() + Math.Abs(Slow).ToString() + Math.Abs(Fast).ToString() + Math.Abs(Rate_Renamed).ToString();
                return WkStr;
            }
            else
            {
                return "";
            }

        }
        else
        {

            if (Slow < 1 || Slow > 500000 || Fast < 1 || Fast > 500000 || Rate_Renamed < 0 || Rate_Renamed > 1000)
            {
                return "";
            }

            if (Axis == 0)
            {
                WkStr = "D:W" + "S" + Math.Abs(Slow).ToString() + "F" + Math.Abs(Fast).ToString() + "R" + Math.Abs(Rate_Renamed).ToString();
                return WkStr;
            }
            else if (Axis >= 1 && Axis <= MaxAxis)
            {
                WkStr = "D:" + Axis.ToString() + "S" + Math.Abs(Slow).ToString() + "F" + Math.Abs(Fast).ToString() + "R" + Math.Abs(Rate_Renamed).ToString();
                return WkStr;
            }
            else
            {
                return "";
            }
        }
    }

    public bool Speed(int Axis, int Slow, int Fast, int Rate_Renamed)
    {

        if (SGWrite(_Speed(Axis, Slow, Fast, Rate_Renamed)) == false)
        {
            return false;
        }
        else
        {
            return true;
        }

    }

    // Get the status "Q"
    private string _StatusQ()
    {
        return "Q:";
    }

    // Get the status "!"
    private string _Status2()
    {
        return "!:";
    }

    // Get the status "?"
    public string _Status3(string Para)
    {
        return "?:" + Para;
    }

    // Get the coordinate value from the status "Q"
    private string Get_Coordinate_Value(string Qstatus, int Axis = 1)
    {
        return inGet_Coordinate_Value(Qstatus, 1, 1);
    }

    // Get the status from the status "Q"
    private string Get_Stage_Status(string Qstatus, int ACKNumber, int Axis = 1)
    {
        return inGet_Stage_Status(Qstatus, Axis, ACKNumber);
    }

    private string get_status_hit(int Axis)
    {
        string Wkl;
        Wkl = string.Empty;
        SGWrite(_Status2());
        Thread.Sleep(100);
        SGRead(ref Wkl);
        int stp;
        switch (Axis)
        {
            case 4:
                {
                    stp = 3;
                    break;
                }

            case 3:
                {
                    stp = 2;
                    break;
                }

            case 2:
                {
                    stp = 1;
                    break;
                }

            default:
                {
                    stp = 0;
                    break;
                }
        }
        int kk = Wkl.Length;
        Wkl = Wkl.Substring(stp, kk - stp);
        Wkl = Wkl.Substring(0, Wkl.IndexOf(","));
        return Wkl;
    }

    // Hit Mode
    private string _HitMode()
    {
        return "Z:1";
    }

    // Shot Mode
    private string _ShotMode()
    {
        return "Z:0";
    }

}
