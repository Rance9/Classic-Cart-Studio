// Classic Cart Studio console engine: the USB transport of the FEL loader (CcsConsole.Usb.IFelUsb over LibUsbDotNet).
// Derived in part from FelLib by Team Shinkansen (https://github.com/TeamShinkansen/FelLib), commit
// e28ddfed0e2dea29279fb37fa379329329fa6cca, file FelLib/Fel.cs: the USB-device opening, configuration, interface claiming and
// endpoint selection of Fel.Open, and the endpoint reads and writes of its Close, WriteToUSB and ReadFromUSB.
// Pinned source: https://github.com/TeamShinkansen/FelLib/blob/e28ddfed0e2dea29279fb37fa379329329fa6cca/FelLib/Fel.cs
// That portion is licensed under the GNU General Public License version 3, the LICENSE of that repository at that commit (the file
// itself carries no copyright notice); Classic Cart Studio does not change its terms.
// Changes for Classic Cart Studio, Copyright (C) 2026 Rance9, GPL-3.0-or-later (provenance and change notice added 2026-11-22):
//   - the device opening and endpoint check implement IFelUsb, so the FEL protocol (FelLib/Fel.cs here) can also run against a
//     simulated device;
//   - Open takes the device's VID and PID and, when it fails (no device, unexpected endpoints, a USB error), closes the device and
//     returns the reason in plain words; the debug output and the endpoints' maximum packet sizes are not kept;
//   - the board check (VerifyDevice, board 0x00166700) is not part of opening the device: FelLib/Fel.cs here does it after Open;
//   - Close also releases interface 0 of a whole USB device;
//   - Write and Read are single transfers with the caller's timeout (the loop over a whole buffer stays in FelLib/Fel.cs here);
//   - LibUsbDeviceList (the devices LibUsbDotNet can see) is this project's own.
// LibUsbDotNet 2.2.29 (LGPL-3.0) is the same transport Hakchi2 CE uses; on Windows it reaches the console through the
// WinUSB driver already installed for the FEL device (libwdi "Nintendo Classic", USB\VID_1F3A&PID_EFE8).
using LibUsbDotNet;
using LibUsbDotNet.Main;
using System;
using System.Collections.Generic;

namespace CcsConsole.Usb
{
    public sealed class LibUsbFelUsb : IFelUsb
    {
        UsbDevice device;
        UsbEndpointReader epReader;
        UsbEndpointWriter epWriter;

        public bool Open(ushort vid, ushort pid, out string why)
        {
            Close();
            try
            {
                device = UsbDevice.OpenUsbDevice(new UsbDeviceFinder(vid, pid));
                if (device == null) { why = string.Format("no USB device {0:X4}:{1:X4} could be opened (is the console in FEL mode, and is no other program using it?)", vid, pid); return false; }
                var whole = device as IUsbDevice;   // libusb-win32 / libusb: select the configuration and claim the interface; WinUSB: already an interface
                if (!ReferenceEquals(whole, null)) { whole.SetConfiguration(1); whole.ClaimInterface(0); }
                int inEndp = -1, outEndp = -1;
                foreach (var config in device.Configs)
                    foreach (var iface in config.InterfaceInfoList)
                        foreach (var endp in iface.EndpointInfoList)
                            if ((endp.Descriptor.EndpointID & 0x80) != 0) inEndp = endp.Descriptor.EndpointID; else outEndp = endp.Descriptor.EndpointID;
                if (inEndp != 0x82 || outEndp != 0x01) { why = string.Format("unexpected endpoints in 0x{0:X2} / out 0x{1:X2} (FEL uses 0x82 / 0x01)", inEndp, outEndp); Close(); return false; }
                epReader = device.OpenEndpointReader((ReadEndpointID)inEndp, 65536);
                epWriter = device.OpenEndpointWriter((WriteEndpointID)outEndp);
                why = null; return true;
            }
            catch (Exception ex) { why = "USB error: " + ex.Message; Close(); return false; }
        }

        public void Close()
        {
            epReader?.Dispose(); epReader = null;
            epWriter?.Dispose(); epWriter = null;
            if (device != null)
            {
                var whole = device as IUsbDevice;
                try { if (!ReferenceEquals(whole, null)) whole.ReleaseInterface(0); } catch { }
                device.Close(); device = null;
            }
        }

        public int Write(byte[] buffer, int offset, int count, int timeoutMs)
        {
            int l; epWriter.Write(buffer, offset, count, timeoutMs, out l); return l;
        }

        public int Read(byte[] buffer, int offset, int count, int timeoutMs)
        {
            int l; var r = epReader.Read(buffer, offset, count, timeoutMs, out l);
            if (r != ErrorCode.Ok) throw new Exception("USB read error: " + r);
            return l;
        }

        public void Dispose() { Close(); }
    }

    /// <summary>the devices LibUsbDotNet can see through installed USB drivers (WinUSB, libusb-win32, libusbK)</summary>
    public sealed class LibUsbDeviceList : IUsbDeviceList
    {
        public IEnumerable<UsbDeviceEntry> Devices()
        {
            var list = new List<UsbDeviceEntry>();
            foreach (UsbRegistry r in UsbDevice.AllDevices)
                list.Add(new UsbDeviceEntry { Vid = (ushort)r.Vid, Pid = (ushort)r.Pid, Name = r.FullName, Path = r.SymbolicName });
            return list;
        }
    }
}
