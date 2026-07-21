using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Aj179PStat
{
    public class BatteryStatus
    {
        public bool IsConnected { get; set; }
        public int BatteryPercent { get; set; }
        public string RawDataHex { get; set; } = string.Empty;
        public string StatusMessage { get; set; } = string.Empty;
    }

    public class HidDeviceManager
    {
        public const ushort TargetVendorId = 0x3151;
        public const ushort TargetProductId = 0x402D;

        #region Native Win32 Structures and Imports

        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;

        private const uint DIGCF_PRESENT = 0x00000002;
        private const uint DIGCF_DEVICEINTERFACE = 0x00000010;

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid interfaceClassGuid;
            public int flags;
            public IntPtr reserved;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct HIDD_ATTRIBUTES
        {
            public int Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        [DllImport("hid.dll", SetLastError = true)]
        private static extern void HidD_GetHidGuid(out Guid hidGuid);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr SetupDiGetClassDevs(
            ref Guid ClassGuid,
            IntPtr Enumerator,
            IntPtr hwndParent,
            uint Flags);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SetupDiEnumDeviceInterfaces(
            IntPtr DeviceInfoSet,
            IntPtr DeviceInfoData,
            ref Guid InterfaceClassGuid,
            uint MemberIndex,
            ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(
            IntPtr DeviceInfoSet,
            ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData,
            IntPtr DeviceInterfaceDetailData,
            int DeviceInterfaceDetailDataSize,
            out int RequiredSize,
            IntPtr DeviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetAttributes(IntPtr HidDeviceObject, ref HIDD_ATTRIBUTES Attributes);

        [DllImport("hid.dll", SetLastError = true, EntryPoint = "HidD_SetFeature")]
        private static extern bool HidD_SetFeature(IntPtr HidDeviceObject, byte[] lpReportBuffer, int ReportBufferLength);

        [DllImport("hid.dll", SetLastError = true, EntryPoint = "HidD_GetFeature")]
        private static extern bool HidD_GetFeature(IntPtr HidDeviceObject, byte[] lpReportBuffer, int ReportBufferLength);

        [DllImport("hid.dll", SetLastError = true, EntryPoint = "HidD_GetInputReport")]
        private static extern bool HidD_GetInputReport(IntPtr HidDeviceObject, byte[] lpReportBuffer, int ReportBufferLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToWrite, out uint lpNumberOfBytesWritten, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToRead, out uint lpNumberOfBytesRead, IntPtr lpOverlapped);

        #endregion

        public static List<string> GetMatchingDevicePaths(ushort vid, ushort pid)
        {
            var devicePaths = new List<string>();
            HidD_GetHidGuid(out Guid hidGuid);

            IntPtr deviceInfoSet = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == new IntPtr(-1))
                return devicePaths;

            try
            {
                SP_DEVICE_INTERFACE_DATA deviceInterfaceData = new SP_DEVICE_INTERFACE_DATA();
                deviceInterfaceData.cbSize = Marshal.SizeOf(deviceInterfaceData);

                uint memberIndex = 0;
                while (SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref hidGuid, memberIndex, ref deviceInterfaceData))
                {
                    memberIndex++;

                    SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref deviceInterfaceData, IntPtr.Zero, 0, out int requiredSize, IntPtr.Zero);

                    IntPtr detailDataBuffer = Marshal.AllocHGlobal(requiredSize);
                    try
                    {
                        Marshal.WriteInt32(detailDataBuffer, (IntPtr.Size == 8) ? 8 : 5);

                        if (SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref deviceInterfaceData, detailDataBuffer, requiredSize, out _, IntPtr.Zero))
                        {
                            IntPtr pDevicePath = detailDataBuffer + 4;
                            string? path = Marshal.PtrToStringAuto(pDevicePath);

                            if (!string.IsNullOrEmpty(path))
                            {
                                if (IsMatchingVidPid(path, vid, pid))
                                {
                                    devicePaths.Add(path);
                                }
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(detailDataBuffer);
                    }
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(deviceInfoSet);
            }

            return devicePaths;
        }

        private static bool IsMatchingVidPid(string devicePath, ushort vid, ushort pid)
        {
            string pathUpper = devicePath.ToUpperInvariant();
            string targetVidStr = $"VID_{vid:X4}";
            string targetPidStr = $"PID_{pid:X4}";

            if (pathUpper.Contains(targetVidStr) && pathUpper.Contains(targetPidStr))
                return true;

            IntPtr handle = CreateFile(devicePath, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle != IntPtr.Zero && handle != new IntPtr(-1))
            {
                try
                {
                    HIDD_ATTRIBUTES attrib = new HIDD_ATTRIBUTES();
                    attrib.Size = Marshal.SizeOf(attrib);
                    if (HidD_GetAttributes(handle, ref attrib))
                    {
                        return attrib.VendorID == vid && attrib.ProductID == pid;
                    }
                }
                finally
                {
                    CloseHandle(handle);
                }
            }

            return false;
        }

        public BatteryStatus ReadBatteryStatus()
        {
            var status = new BatteryStatus();
            List<string> paths = GetMatchingDevicePaths(TargetVendorId, TargetProductId);

            if (paths.Count == 0)
            {
                status.IsConnected = false;
                status.StatusMessage = "Ajazz AJ179 Pro not found (VID_3151 & PID_402D)";
                return status;
            }

            List<string> attemptLogs = new List<string>();

            foreach (string path in paths)
            {
                IntPtr handle = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                {
                    handle = CreateFile(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                }

                if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                {
                    attemptLogs.Add($"[Access Denied] {GetShortPath(path)}");
                    continue;
                }

                try
                {
                    byte[] response = SendAndReceiveReport(handle, out string methodUsed);
                    if (response != null && response.Length >= 3)
                    {
                        // data[2] is percent of battery (or response[3] if prepended report ID)
                        int batteryIdx = (response.Length == 65) ? 3 : 2;
                        byte batteryByte = response[batteryIdx];

                        if (batteryByte <= 100 && batteryByte > 0)
                        {
                            status.IsConnected = true;
                            status.BatteryPercent = batteryByte;
                            status.RawDataHex = BitConverter.ToString(response).Replace("-", " ");
                            status.StatusMessage = $"Method: {methodUsed}\r\nPath: {GetShortPath(path)}";
                            return status;
                        }
                        else
                        {
                            attemptLogs.Add($"[{methodUsed}] Response received but batteryByte out of range ({batteryByte})");
                        }
                    }
                    else
                    {
                        attemptLogs.Add($"[No Data] {GetShortPath(path)}");
                    }
                }
                finally
                {
                    CloseHandle(handle);
                }
            }

            status.IsConnected = true;
            status.StatusMessage = "Mouse detected, but payload response failed.\r\n" + string.Join("\r\n", attemptLogs);
            return status;
        }

        private static string GetShortPath(string fullPath)
        {
            return fullPath.Length > 40 ? "..." + fullPath.Substring(fullPath.Length - 37) : fullPath;
        }

        private byte[] SendAndReceiveReport(IntPtr handle, out string methodUsed)
        {
            byte[] req64 = new byte[64];
            req64[0] = 247; // 0xF7

            byte[] req65 = new byte[65];
            req65[0] = 0;   // Report ID 0
            req65[1] = 247; // 0xF7

            byte[] readBuf64 = new byte[64];
            byte[] readBuf65 = new byte[65];

            // Method 1: HidD_SetFeature / ReadFile or HidD_GetFeature (64 bytes)
            try
            {
                if (HidD_SetFeature(handle, req64, req64.Length))
                {
                    try
                    {
                        if (HidD_GetFeature(handle, readBuf64, readBuf64.Length))
                        {
                            methodUsed = "HidD_SetFeature / HidD_GetFeature (64b)";
                            return readBuf64;
                        }
                    }
                    catch { }

                    if (ReadFile(handle, readBuf64, (uint)readBuf64.Length, out uint bRead, IntPtr.Zero) && bRead > 0)
                    {
                        methodUsed = "HidD_SetFeature / ReadFile (64b)";
                        return readBuf64;
                    }
                }
            }
            catch { }

            // Method 2: HidD_SetFeature / ReadFile or HidD_GetFeature (65 bytes)
            try
            {
                if (HidD_SetFeature(handle, req65, req65.Length))
                {
                    try
                    {
                        if (HidD_GetFeature(handle, readBuf65, readBuf65.Length))
                        {
                            methodUsed = "HidD_SetFeature / HidD_GetFeature (65b)";
                            return readBuf65;
                        }
                    }
                    catch { }

                    if (ReadFile(handle, readBuf65, (uint)readBuf65.Length, out uint bRead, IntPtr.Zero) && bRead > 0)
                    {
                        methodUsed = "HidD_SetFeature / ReadFile (65b)";
                        return readBuf65;
                    }
                }
            }
            catch { }

            // Method 3: WriteFile / ReadFile (64 bytes)
            try
            {
                if (WriteFile(handle, req64, (uint)req64.Length, out _, IntPtr.Zero))
                {
                    if (ReadFile(handle, readBuf64, (uint)readBuf64.Length, out uint bytesRead, IntPtr.Zero) && bytesRead > 0)
                    {
                        methodUsed = "WriteFile / ReadFile (64b)";
                        return readBuf64;
                    }
                }
            }
            catch { }

            // Method 4: WriteFile / ReadFile (65 bytes)
            try
            {
                if (WriteFile(handle, req65, (uint)req65.Length, out _, IntPtr.Zero))
                {
                    if (ReadFile(handle, readBuf65, (uint)readBuf65.Length, out uint bytesRead, IntPtr.Zero) && bytesRead > 0)
                    {
                        methodUsed = "WriteFile / ReadFile (65b)";
                        return readBuf65;
                    }
                }
            }
            catch { }

            // Method 5: HidD_GetInputReport (64 bytes)
            try
            {
                if (HidD_GetInputReport(handle, readBuf64, readBuf64.Length))
                {
                    methodUsed = "HidD_GetInputReport (64b)";
                    return readBuf64;
                }
            }
            catch { }

            // Method 6: HidD_GetInputReport (65 bytes)
            try
            {
                if (HidD_GetInputReport(handle, readBuf65, readBuf65.Length))
                {
                    methodUsed = "HidD_GetInputReport (65b)";
                    return readBuf65;
                }
            }
            catch { }

            methodUsed = "None";
            return Array.Empty<byte>();
        }
    }
}
