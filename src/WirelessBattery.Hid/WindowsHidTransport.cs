using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using WirelessBattery.Core;

namespace WirelessBattery.Hid;

public sealed class WindowsHidTransport : IHidTransport
{
    private readonly Action<string>? trace;

    public WindowsHidTransport(Action<string>? trace = null) => this.trace = trace;

    public IReadOnlyList<HidCollection> Enumerate(ushort vendorId, ushort productId)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        Native.HidD_GetHidGuid(out var hidGuid);
        var set = Native.SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x12);
        if (set == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var results = new List<HidCollection>();
        try
        {
            for (uint index = 0; ; index++)
            {
                var data = new Native.InterfaceData { Size = Marshal.SizeOf<Native.InterfaceData>() };
                if (!Native.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, index, ref data))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == 259) break; // ERROR_NO_MORE_ITEMS
                    throw new Win32Exception(error);
                }
                Native.SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out var needed, IntPtr.Zero);
                if (needed < 8) continue;
                var detail = Marshal.AllocHGlobal((int)needed);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!Native.SetupDiGetDeviceInterfaceDetail(set, ref data, detail, needed, out _, IntPtr.Zero))
                        continue;
                    var path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4));
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    using var handle = Native.CreateFile(path, 0, 0x3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                    if (handle.IsInvalid) continue;
                    var attributes = new Native.HidAttributes { Size = Marshal.SizeOf<Native.HidAttributes>() };
                    if (!Native.HidD_GetAttributes(handle, ref attributes) ||
                        attributes.VendorId != vendorId || attributes.ProductId != productId)
                        continue;
                    if (!Native.HidD_GetPreparsedData(handle, out var preparsed)) continue;
                    try
                    {
                        if (Native.HidP_GetCaps(preparsed, out var caps) < 0) continue;
                        results.Add(new HidCollection(path, attributes.VendorId, attributes.ProductId,
                            caps.UsagePage, caps.Usage, caps.InputReportLength,
                            caps.OutputReportLength, caps.FeatureReportLength));
                    }
                    finally { Native.HidD_FreePreparsedData(preparsed); }
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { Native.SetupDiDestroyDeviceInfoList(set); }
        return results;
    }

    public async Task<byte[]?> ExchangeAsync(HidCollection collection, byte[] request, TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (request.Length > collection.OutputReportLength) throw new ArgumentException("Request exceeds output report length");
        using var handle = Native.CreateFile(collection.Path, 0xC0000000, 0x3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
        if (handle.IsInvalid) throw new IOException($"Cannot open HID collection: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
        await using var stream = new FileStream(handle, FileAccess.ReadWrite, 4096, isAsync: true);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            var output = new byte[collection.OutputReportLength];
            request.CopyTo(output, 0);
            trace?.Invoke($"{collection.VendorId:X4}:{collection.ProductId:X4} TX {Convert.ToHexString(output)}");
            await stream.WriteAsync(output, timeoutCts.Token);
            var input = new byte[collection.InputReportLength];
            var count = await stream.ReadAsync(input, timeoutCts.Token);
            if (count > 0) trace?.Invoke($"{collection.VendorId:X4}:{collection.ProductId:X4} RX {Convert.ToHexString(input.AsSpan(0, count))}");
            return count == 0 ? null : input[..count];
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
    }

    // Some composite receivers write a short report and answer on a separate long-report Collection.
    public async Task<byte[]?> ExchangeAcrossAsync(HidCollection outputCollection, HidCollection inputCollection,
        byte[] request, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (request.Length > outputCollection.OutputReportLength)
            throw new ArgumentException("Request exceeds output report length");
        using var readHandle = Native.CreateFile(inputCollection.Path, 0x80000000, 0x3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
        if (readHandle.IsInvalid)
            throw new IOException($"Cannot open HID input collection: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
        await using var reader = new FileStream(readHandle, FileAccess.Read, 4096, isAsync: true);
        using var writeHandle = Native.CreateFile(outputCollection.Path, 0x40000000, 0x3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
        if (writeHandle.IsInvalid)
            throw new IOException($"Cannot open HID output collection: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
        await using var writer = new FileStream(writeHandle, FileAccess.Write, 4096, isAsync: true);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            var input = new byte[inputCollection.InputReportLength];
            var pendingRead = reader.ReadAsync(input, timeoutCts.Token);
            var output = new byte[outputCollection.OutputReportLength];
            request.CopyTo(output, 0);
            trace?.Invoke($"{outputCollection.VendorId:X4}:{outputCollection.ProductId:X4} TX {Convert.ToHexString(output)}");
            await writer.WriteAsync(output, timeoutCts.Token);
            var count = await pendingRead;
            if (count > 0) trace?.Invoke($"{inputCollection.VendorId:X4}:{inputCollection.ProductId:X4} RX {Convert.ToHexString(input.AsSpan(0, count))}");
            return count == 0 ? null : input[..count];
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct InterfaceData
        {
            public int Size;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct HidAttributes
        {
            public int Size;
            public ushort VendorId;
            public ushort ProductId;
            public ushort VersionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct HidCaps
        {
            public ushort Usage;
            public ushort UsagePage;
            public short InputReportLength;
            public short OutputReportLength;
            public short FeatureReportLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        [DllImport("hid.dll")] internal static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("hid.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool HidD_GetAttributes(SafeFileHandle handle, ref HidAttributes attributes);
        [DllImport("hid.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr preparsed);
        [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool HidD_FreePreparsedData(IntPtr preparsed);
        [DllImport("hid.dll")] internal static extern int HidP_GetCaps(IntPtr preparsed, out HidCaps caps);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, string? enumerator, IntPtr parent, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr deviceInfo, ref Guid classGuid,
            uint index, ref InterfaceData data);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref InterfaceData data,
            IntPtr detail, uint detailSize, out uint requiredSize, IntPtr deviceInfo);
        [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security,
            uint creationDisposition, uint flags, IntPtr template);
    }
}
