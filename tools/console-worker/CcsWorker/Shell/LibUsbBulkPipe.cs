// Classic Cart Studio console transport worker. GPL-3.0-or-later.
// Derived from Hakchi2 CE 3.9.3 Clovershell/ClovershellConnection.cs (commit 25bfcfd; GPL-3.0): the device search by VID 0x1F3A /
// PID 0xEFE8, configuration 1 and interface 0, the endpoint check (in 0x81, out 0x01), the 64 KiB reader, the write with up to ten
// retries 100 ms apart. LibUsbDotNet 2.2.29 (LGPL-3.0), the copy Hakchi2 CE 3.9.3 embeds.
// Modified by Rance9, 2026: constructed only by RealTransports.Open (Gate G1 closed in code: nothing here runs in this version);
// reads and writes through IBulkPipe; a missing or unexpected device is an exception, never a silent retry loop.
using LibUsbDotNet;
using LibUsbDotNet.Main;
using System.IO;
using System.Threading;

namespace CcsWorker.Shell
{
    internal sealed class LibUsbBulkPipe : IBulkPipe
    {
        const ushort Vid = 0x1F3A, Pid = 0xEFE8;
        UsbDevice device; UsbEndpointReader epReader; UsbEndpointWriter epWriter;

        internal LibUsbBulkPipe()
        {
            foreach (UsbRegistry reg in UsbDevice.AllDevices)
                if (reg.Vid == Vid && reg.Pid == Pid) { reg.Open(out device); break; }
            if (device == null) throw new ShellLostException("no clovershell device (USB 1F3A:EFE8) was found");
            var whole = device as IUsbDevice;
            if (!ReferenceEquals(whole, null)) { whole.SetConfiguration(1); whole.ClaimInterface(0); }
            int inEndp = -1, outEndp = -1;
            foreach (var config in device.Configs)
                foreach (var iface in config.InterfaceInfoList)
                    foreach (var endp in iface.EndpointInfoList)
                        if ((endp.Descriptor.EndpointID & 0x80) != 0) inEndp = endp.Descriptor.EndpointID; else outEndp = endp.Descriptor.EndpointID;
            if (inEndp != 0x81 || outEndp != 0x01) { Dispose(); throw new ShellLostException("the USB device is not clovershell (unexpected endpoints)"); }
            epReader = device.OpenEndpointReader((ReadEndpointID)inEndp, 65536);
            epWriter = device.OpenEndpointWriter((WriteEndpointID)outEndp);
        }

        public int Read(byte[] buffer, int offset, int count, int timeoutMs)
        {
            int l; var r = epReader.Read(buffer, offset, count, timeoutMs, out l);
            if (r == ErrorCode.Ok || r == ErrorCode.IoTimedOut) return l;
            throw new IOException("USB read: " + r);
        }

        public void Write(byte[] buffer, int offset, int count, int timeoutMs)
        {
            int repeats = 0;
            while (count > 0)
            {
                int l; var r = epWriter.Write(buffer, offset, count, timeoutMs, out l);
                offset += l; count -= l;
                if (r != ErrorCode.Ok) { if (++repeats > 10) break; Thread.Sleep(100); }
            }
            if (count > 0) throw new IOException("USB write error");
        }

        public void Dispose()
        {
            epReader?.Dispose(); epReader = null; epWriter?.Dispose(); epWriter = null;
            if (device != null) { var whole = device as IUsbDevice; try { if (!ReferenceEquals(whole, null)) whole.ReleaseInterface(0); } catch { } device.Close(); device = null; }
        }
    }
}
