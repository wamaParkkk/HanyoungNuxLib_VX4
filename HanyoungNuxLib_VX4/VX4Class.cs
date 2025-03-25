using System;
using System.IO;
using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace HanyoungNuxLib_VX4
{
    public class VX4Class
    {
        private SerialPort _serialPort;
        
        public VX4Class(string portName, int baudRate, Parity parity, int dataBits, StopBits stopBits)
        {
            _serialPort = new SerialPort(portName, baudRate, parity, dataBits, stopBits);
            _serialPort.ReadTimeout = 1000;
            _serialPort.WriteTimeout = 1000;
        }

        public bool Open()
        {
            try
            {
                if (!_serialPort.IsOpen)
                    _serialPort.Open();

                Global.EventLog("Connected to VX4 Controller");
                return true;
            }
            catch (Exception ex)
            {
                Global.EventLog($"[ERROR] opening serial port: {ex.Message}");
                return false;
            }
        }

        public void Close()
        {
            if (_serialPort.IsOpen)
                _serialPort.Close();
        }

        private readonly object _serialLock = new object();

        public string SendCommand(string command)
        {
            lock (_serialLock)  //동기화 처리
            {
                try
                {
                    if (_serialPort == null || !_serialPort.IsOpen)
                    {
                        Global.EventLog("[ERROR] Serial port is closed");
                        return null;
                    }

                    _serialPort.DiscardInBuffer();
                    _serialPort.Write(command);
                    Thread.Sleep(150);  // 응답 대기

                    // ReadTimeout을 설정하여 무한 대기 방지
                    _serialPort.ReadTimeout = 1000;
                    string response = _serialPort.ReadLine().Trim();

                    Global.EventLog($"Sent: {command}");
                    Global.EventLog($"Received: {response}\n");

                    return response;
                }
                catch (TimeoutException)
                {
                    Global.EventLog("[ERROR] Serial read timeout");
                    return null;
                }
                catch (IOException ex)
                {
                    Global.EventLog($"[ERROR] I/O operation canceled: {ex.Message}");
                    return null;
                }
                catch (Exception ex)
                {
                    Global.EventLog($"[ERROR] {ex.Message}\n{ex.StackTrace}");
                    return null;
                }
            }
        }
    }

    public class VX4Controller
    {
        [DllImport("kernel32")]
        private static extern int GetPrivateProfileString(string section, string key, string def, StringBuilder retVal, int size, string filePath);

        private VX4Class _vx4class;

        public VX4Controller()
        {
            // Ini file read
            StringBuilder sbVal = new StringBuilder();

            GetPrivateProfileString("PortName", "Port", "", sbVal, sbVal.Capacity, string.Format("{0}{1}", Global.serialPortInfoPath, "HanyoungNuxVX4PortInfo.ini"));
            string strPortName = sbVal.ToString();

            GetPrivateProfileString("BaudRate", "BaudRate", "", sbVal, sbVal.Capacity, string.Format("{0}{1}", Global.serialPortInfoPath, "HanyoungNuxVX4PortInfo.ini"));
            int iBaudRate = Convert.ToInt32(sbVal.ToString());

            GetPrivateProfileString("Parity", "Parity", "", sbVal, sbVal.Capacity, string.Format("{0}{1}", Global.serialPortInfoPath, "HanyoungNuxVX4PortInfo.ini"));
            Parity parity = (Parity)Convert.ToInt32(sbVal.ToString());

            GetPrivateProfileString("DataBits", "DataBits", "", sbVal, sbVal.Capacity, string.Format("{0}{1}", Global.serialPortInfoPath, "HanyoungNuxVX4PortInfo.ini"));
            int iDataBits = Convert.ToInt32(sbVal.ToString());
            
            GetPrivateProfileString("StopBits", "StopBits", "", sbVal, sbVal.Capacity, string.Format("{0}{1}", Global.serialPortInfoPath, "HanyoungNuxVX4PortInfo.ini"));
            StopBits stopBits = (StopBits)Convert.ToInt32(sbVal.ToString());            

            _vx4class = new VX4Class(strPortName, iBaudRate, parity, iDataBits, stopBits);
        }
        
        public bool Connect() => _vx4class.Open();

        public void Disconnect() => _vx4class.Close();        

        public double ReadPV(int addr)
        {
            string command = $"\x02{addr:D2}DRS,01,0000\x0D\x0A";
            string response = _vx4class.SendCommand(command);

            if (string.IsNullOrEmpty(response))
                return double.NaN;

            string[] parts = response.Split(',');
            if (parts.Length < 3)
                return double.NaN;

            try
            {
                // 응답 값을 16진수(HEX) → 10진수 변환 후 소수점 반영
                int pvValue = Convert.ToInt32(parts[2], 16);
                return pvValue / 10.0;
            }
            catch (FormatException)
            {
                Global.EventLog("[ERROR] Invalid HEX format in PV response");
                return double.NaN;
            }
        }

        public double ReadSV(int addr)
        {
            string command = $"\x02{addr:D2}DRS,01,0103\x0D\x0A";
            string response = _vx4class.SendCommand(command);

            if (string.IsNullOrEmpty(response))
                return double.NaN;
            
            string[] parts = response.Split(',');
            if (parts.Length < 3)
                return double.NaN;
            
            try
            {
                int svValue = Convert.ToInt32(parts[2], 16);
                return svValue / 10.0;
            }
            catch (FormatException)
            {
                Global.EventLog("[ERROR] Invalid HEX format in SV response");
                return double.NaN;
            }
        }

        public bool SetSV(int addr, double value)
        {            
            // 설정 온도값을 10배한 후 16진수(HEX)로 변환
            int svValue = (int)(value * 10);
            string command = $"\x02{addr:D2}DWS,01,0103,{svValue:X4}\x0D\x0A";

            string response = _vx4class.SendCommand(command);
            return !string.IsNullOrEmpty(response) && response.Contains("OK");
        }        
    }
}
