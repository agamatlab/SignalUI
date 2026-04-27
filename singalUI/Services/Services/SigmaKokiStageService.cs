using System;
using System.IO.Ports;
using System.Text.RegularExpressions;
using System.Threading;

namespace singalUI.Services
{
    /// <summary>
    /// SIGMA KOKI Stage Controller Service
    /// Based on SKSampleClass from SDK, cleaned up for Avalonia/.NET 9
    /// Supports GSC01, GIP-101B, SHOT302/304GS, Hit, HSC-103, PGC-04 controllers
    /// </summary>
    public class SigmaKokiStageService : IDisposable
    {
        public enum ControllerType
        {
            SHOT_304GS,
            Hit_MV,
            PGC_04,
            SHOT_302GS,
            GIP_101B,
            HSC_103,
            GSC01
        }

        private SerialPort _serialPort = new();
        private ControllerType _controller;
        private int _maxAxis;
        private bool _hitMode;
        private readonly object _lock = new();

        // Events instead of Windows Forms controls
        public event Action<string>? StatusChanged;
        public event Action<string>? ErrorOccurred;
        public event Action<int>? PositionChanged;

        public SigmaKokiStageService()
        {
            _serialPort.PortName = "COM1";
            _serialPort.BaudRate = 9600;
            _serialPort.NewLine = "\r\n";
            _serialPort.ReadTimeout = 5000;
            _serialPort.RtsEnable = true;
            ControllerTypeValue = ControllerType.SHOT_304GS;
        }

        // Controller Type
        public ControllerType ControllerTypeValue
        {
            get => _controller;
            set
            {
                _controller = value;
                if (_controller == ControllerType.SHOT_302GS)
                {
                    _maxAxis = 2;
                }
                else if (_controller == ControllerType.GIP_101B || _controller == ControllerType.GSC01)
                {
                    _maxAxis = 1;
                }
                else if (_controller == ControllerType.HSC_103)
                {
                    _hitMode = true;
                    _maxAxis = 3;
                }
                else if (_controller == ControllerType.PGC_04)
                {
                    _hitMode = true;
                    _maxAxis = 4;
                }
                else if (_controller == ControllerType.Hit_MV)
                {
                    _hitMode = true;
                    _maxAxis = 4;
                }
                else
                {
                    _maxAxis = 4;
                }
            }
        }

        public string PortName
        {
            get => _serialPort.PortName;
            set => _serialPort.PortName = value;
        }

        public int BaudRate
        {
            get => _serialPort.BaudRate;
            set => _serialPort.BaudRate = value;
        }

        public int Timeout
        {
            get => _serialPort.ReadTimeout;
            set => _serialPort.ReadTimeout = value;
        }

        public string[] GetPortNames() => SerialPort.GetPortNames();

        public bool IsConnected => _serialPort.IsOpen;

        /// <summary>
        /// Test connection to stage controller
        /// </summary>
        public bool ConnectTest()
        {
            try
            {
                if (!SGOpen())
                    return false;

                _serialPort.DiscardInBuffer();

                if (!SGWrite(_StatusQ()))
                    return false;

                string response = string.Empty;
                if (!SGRead(ref response))
                    return false;

                string status = GetStageStatus(response, 3, _maxAxis).ToUpper();
                bool connected = status == "R" || status == "B";

                StatusChanged?.Invoke(connected ? "Stage connected" : "Stage connection failed");
                return connected;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"Connection test failed: {ex.Message}");
                return false;
            }
        }

        public bool PortClose()
        {
            lock (_lock)
            {
                bool result = SGClose();
                StatusChanged?.Invoke(result ? "Stage disconnected" : "Failed to disconnect stage");
                return result;
            }
        }

        /// <summary>
        /// Wait until stage is ready
        /// </summary>
        public bool WaitReady()
        {
            string response;
            string status;

            try
            {
                do
                {
                    // Give other threads a chance
                    Thread.Sleep(10);

                    if (!SGWrite(_StatusQ()))
                        return false;

                    response = string.Empty;
                    if (!SGRead(ref response))
                        return false;

                    if (!string.IsNullOrEmpty(response))
                    {
                        if (_hitMode)
                        {
                            status = GetStatusHit(1);
                            if (status == "0")
                                return true;
                        }
                        else
                        {
                            status = GetStageStatus(response, 3, _maxAxis).ToUpper();
                            if (status == "R")
                                return true;
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

        /// <summary>
        /// Get current position
        /// </summary>
        public int GetPosition(int axis = 1)
        {
            if (!WaitReady())
                return 0;

            if (!SGWrite(_StatusQ()))
                return 0;

            string response = string.Empty;
            if (!SGRead(ref response))
                return 0;

            string pos = GetCoordinateValue(response, axis);
            if (int.TryParse(pos, out int position))
            {
                PositionChanged?.Invoke(position);
                return position;
            }
            return 0;
        }

        /// <summary>
        /// Move absolutely to position
        /// </summary>
        public bool MoveAbsolutely(int axis, int position)
        {
            lock (_lock)
            {
                string response;

                if (!WaitReady())
                    return false;

                if (!SGWrite(_MoveAbsolutely(axis, position)))
                    return false;

                response = string.Empty;
                if (!SGRead(ref response))
                    return false;

                if (response.IndexOf("OK") < 0)
                    return false;

                if (!_hitMode)
                {
                    if (!SGWrite(_Go()))
                        return false;

                    response = string.Empty;
                    if (!SGRead(ref response))
                        return false;

                    if (response.IndexOf("OK") < 0)
                        return false;
                }

                StatusChanged?.Invoke($"Moving axis {axis} to {position}");
                return WaitReady();
            }
        }

        /// <summary>
        /// Move relatively by distance
        /// </summary>
        public bool MoveRelatively(int axis, int distance)
        {
            lock (_lock)
            {
                string response;

                if (!WaitReady())
                    return false;

                if (!SGWrite(_MoveRelativity(axis, distance)))
                    return false;

                response = string.Empty;
                if (!SGRead(ref response))
                    return false;

                if (response.IndexOf("OK") < 0)
                    return false;

                if (!_hitMode)
                {
                    if (!SGWrite(_Go()))
                        return false;

                    response = string.Empty;
                    if (!SGRead(ref response))
                        return false;

                    if (response.IndexOf("OK") < 0)
                        return false;
                }

                StatusChanged?.Invoke($"Moving axis {axis} by {distance}");
                return WaitReady();
            }
        }

        /// <summary>
        /// Return to mechanical origin
        /// </summary>
        public bool ReturnOrigin(int axis)
        {
            lock (_lock)
            {
                string response;

                if (!WaitReady())
                    return false;

                if (!SGWrite(_ReturnOrigin(axis)))
                    return false;

                response = string.Empty;
                if (!SGRead(ref response))
                    return false;

                if (response.IndexOf("OK") < 0)
                    return false;

                StatusChanged?.Invoke($"Returning axis {axis} to origin");
                return WaitReady();
            }
        }

        /// <summary>
        /// Stop stage (deceleration)
        /// </summary>
        public bool StopStage(int axis)
        {
            lock (_lock)
            {
                string response;

                if (!SGWrite(_StopStage(axis)))
                    return false;

                response = string.Empty;
                if (!SGRead(ref response))
                    return false;

                bool success = response.IndexOf("OK") >= 0;
                StatusChanged?.Invoke(success ? $"Stopped axis {axis}" : $"Failed to stop axis {axis}");
                return success;
            }
        }

        /// <summary>
        /// Emergency stop
        /// </summary>
        public bool StopStageEmergency()
        {
            lock (_lock)
            {
                string response;

                if (!SGWrite(_StopStageEmergency()))
                    return false;

                response = string.Empty;
                if (!SGRead(ref response))
                    return false;

                bool success = response.IndexOf("OK") >= 0;
                StatusChanged?.Invoke(success ? "Emergency stop activated" : "Emergency stop failed");
                return success;
            }
        }

        /// <summary>
        /// Set speed parameters
        /// </summary>
        public bool SetSpeed(int axis, int slow, int fast, int rate)
        {
            lock (_lock)
            {
                bool success = SGWrite(_Speed(axis, slow, fast, rate));
                StatusChanged?.Invoke(success ? $"Speed set: slow={slow}, fast={fast}, rate={rate}" : "Failed to set speed");
                return success;
            }
        }

        // Private helper methods

        private bool SGOpen()
        {
            try
            {
                if (!_serialPort.IsOpen)
                {
                    _serialPort.Open();
                }
                return true;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"Failed to open port: {ex.Message}");
                return false;
            }
        }

        private bool SGClose()
        {
            try
            {
                if (_serialPort.IsOpen)
                {
                    _serialPort.Close();
                }
                return true;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"Failed to close port: {ex.Message}");
                return false;
            }
        }

        private bool SGWrite(string command)
        {
            try
            {
                if (_serialPort.CtsHolding != true)
                {
                    ErrorOccurred?.Invoke("CTS not ready for transmission");
                    return false;
                }

                _serialPort.WriteLine(command);
                return true;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"Write failed: {ex.Message}");
                return false;
            }
        }

        private bool SGRead(ref string response)
        {
            try
            {
                response = _serialPort.ReadLine();
                return true;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"Read failed: {ex.Message}");
                return false;
            }
        }

        private string GetAxisDir(int value) => value < 0 ? "-" : "+";

        private string GetAxisDir2(bool value) => value ? "+" : "-";

        private string GetCoordinateValue(string qStatus, int axis)
        {
            return InGetCoordinateValue(qStatus, 1, axis);
        }

        private string InGetCoordinateValue(string qStatus, int axesNum, int axis)
        {
            int stp;
            int axLen = GetQLen(axesNum);

            if (_hitMode)
            {
                stp = axis switch
                {
                    4 => 3,
                    3 => 2,
                    2 => 1,
                    _ => 0
                };

                int kk = qStatus.Length;
                string beforeStr = qStatus.Substring(stp, kk - stp);
                beforeStr = beforeStr.Substring(0, beforeStr.IndexOf(","));
                return beforeStr.Replace(" ", "");
            }
            else
            {
                if (qStatus.Length < axLen)
                    return "0";

                stp = axis switch
                {
                    4 => 34,
                    3 => 23,
                    2 => 12,
                    _ => 1
                };

                string beforeStr = qStatus.Substring(stp - 1, 10);
                return beforeStr.Replace(" ", "");
            }
        }

        private string GetStageStatus(string qStatus, int ackNumber, int axis)
        {
            return InGetStageStatus(qStatus, axis, ackNumber);
        }

        private string InGetStageStatus(string qStatus, int axesNum, int ackNumber)
        {
            int stp;
            int axLen = GetQLen(axesNum);

            if (qStatus.Length < axLen)
                return "0";

            stp = (axesNum, ackNumber) switch
            {
                (4, 1) => 45,
                (4, 2) => 47,
                (4, _) => 49,
                (3, 1) => 34,
                (3, 2) => 36,
                (3, _) => 38,
                (2, 1) => 23,
                (2, 2) => 25,
                (2, _) => 27,
                (_, 1) => 12,
                (_, 2) => 14,
                (_, _) => 16
            };

            return qStatus.Substring(stp - 1, 1);
        }

        private int GetQLen(int axesNum) => axesNum switch
        {
            4 => 49,
            3 => 38,
            2 => 27,
            1 => 16,
            _ => 0
        };

        private string GetStatusHit(int axis)
        {
            string response = string.Empty;
            SGWrite(_Status2());
            Thread.Sleep(100);
            SGRead(ref response);

            int stp = axis switch
            {
                4 => 3,
                3 => 2,
                2 => 1,
                _ => 0
            };

            int kk = response.Length;
            response = response.Substring(stp, kk - stp);
            response = response.Substring(0, response.IndexOf(","));
            return response;
        }

        // Command generators

        private string _ReturnOrigin(int axis)
        {
            if (_hitMode)
            {
                return axis switch
                {
                    0 or 1 => "H:1",
                    2 => "H:,1",
                    3 => "H:,,1",
                    4 => "H:,,,1",
                    _ => ""
                };
            }
            else
            {
                if (axis == 0)
                    return "H:1";
                if (axis >= 1 && axis <= _maxAxis)
                    return "H:" + axis.ToString();
                return "";
            }
        }

        private string _MoveRelativity(int axis, int value)
        {
            if (_hitMode)
            {
                string dir = GetAxisDir(value);
                string abs = Math.Abs(value).ToString();
                return axis switch
                {
                    0 or 1 => "M:" + dir + abs,
                    2 => "M:," + dir + abs,
                    3 => "M:,," + dir + abs,
                    4 => "M:,,," + dir + abs,
                    _ => ""
                };
            }
            else
            {
                if (axis == 0)
                    return "M:1" + GetAxisDir(value) + "P" + Math.Abs(value);
                if (axis >= 1 && axis <= _maxAxis)
                    return "M:" + axis + GetAxisDir(value) + "P" + Math.Abs(value);
                return "";
            }
        }

        private string _MoveAbsolutely(int axis, int value)
        {
            if (_hitMode)
            {
                string dir = GetAxisDir(value);
                string abs = Math.Abs(value).ToString();
                return axis switch
                {
                    0 or 1 => "A:" + dir + abs,
                    2 => "A:," + dir + abs,
                    3 => "A:,," + dir + abs,
                    4 => "A:,,," + dir + abs,
                    _ => ""
                };
            }
            else
            {
                if (axis == 0)
                    return "A:1" + GetAxisDir(value) + "P" + Math.Abs(value);
                if (axis >= 1 && axis <= _maxAxis)
                    return "A:" + axis + GetAxisDir(value) + "P" + Math.Abs(value);
                return "";
            }
        }

        private string _Go() => "G:";

        private string _StopStage(int axis)
        {
            if (_hitMode)
            {
                return axis switch
                {
                    0 or 1 => "L:1",
                    2 => "L:,1",
                    3 => "L:,,1",
                    4 => "L:,,,1",
                    _ => ""
                };
            }
            else
            {
                if (axis == 0)
                    return "L:1";
                if (axis >= 1 && axis <= _maxAxis)
                    return "L:" + axis.ToString();
                return "";
            }
        }

        private string _StopStageEmergency() => "L:E";

        private string _Speed(int axis, int slow, int fast, int rate)
        {
            if (_hitMode)
            {
                if (slow < 1 || slow > 999999999 || fast < 1 || fast > 999999999 || rate < 0 || rate > 1000)
                    return "";

                if (axis == 0 || axis == 1)
                    return "D:1" + Math.Abs(slow) + Math.Abs(fast) + Math.Abs(rate);
                if (axis >= 2 && axis <= _maxAxis)
                    return "D:" + axis + Math.Abs(slow) + Math.Abs(fast) + Math.Abs(rate);
                return "";
            }
            else
            {
                if (slow < 1 || slow > 500000 || fast < 1 || fast > 500000 || rate < 0 || rate > 1000)
                    return "";

                if (axis == 0)
                    return "D:WS" + Math.Abs(slow) + "F" + Math.Abs(fast) + "R" + Math.Abs(rate);
                if (axis >= 1 && axis <= _maxAxis)
                    return "D:" + axis + "S" + Math.Abs(slow) + "F" + Math.Abs(fast) + "R" + Math.Abs(rate);
                return "";
            }
        }

        private string _StatusQ() => "Q:";
        private string _Status2() => "!:";

        public void Dispose()
        {
            if (_serialPort.IsOpen)
            {
                _serialPort.Close();
            }
            _serialPort.Dispose();
        }
    }
}
