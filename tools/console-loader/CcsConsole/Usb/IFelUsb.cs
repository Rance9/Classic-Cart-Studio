// Classic Cart Studio console engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
using System;
using System.Collections.Generic;

namespace CcsConsole.Usb
{
    /// <summary>The bulk transport the FEL protocol runs on (one claimed device, IN endpoint 0x82, OUT endpoint 0x01).</summary>
    public interface IFelUsb : IDisposable
    {
        /// <summary>opens the device with this VID/PID and checks it has exactly the FEL endpoints; why explains a refusal</summary>
        bool Open(ushort vid, ushort pid, out string why);
        void Close();
        /// <summary>returns the number of bytes written (0 = nothing could be written)</summary>
        int Write(byte[] buffer, int offset, int count, int timeoutMs);
        /// <summary>returns the number of bytes read; throws on a transport error</summary>
        int Read(byte[] buffer, int offset, int count, int timeoutMs);
    }

    /// <summary>a USB device as Windows lists it (no device is opened to list it)</summary>
    public sealed class UsbDeviceEntry
    {
        public ushort Vid, Pid;
        public string Name, Path;
        public override string ToString() => string.Format("{0:X4}:{1:X4} {2}", Vid, Pid, Name);
    }

    public interface IUsbDeviceList { IEnumerable<UsbDeviceEntry> Devices(); }
}
