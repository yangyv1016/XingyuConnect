using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace XingyuConnect;

internal static class AudioDriver
{
    public const string PlaybackName = "手机音频";

    public static void Initialize(string? preferredId)
    {
        var endpointId = FindPlaybackEndpoint(preferredId);
        if (endpointId is null)
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XingyuConnect", "drivers", "VBCABLE-Pack45");
            ZipFile.ExtractToDirectory(Path.Combine(AppContext.BaseDirectory, "vbcable", "VBCABLE_Driver_Pack45.zip"), directory, overwriteFiles: true);
            using var installer = Process.Start(new ProcessStartInfo(Path.Combine(directory, "VBCABLE_Setup_x64.exe"))
                { UseShellExecute = true, WorkingDirectory = directory })!;
            installer.WaitForExit();
            endpointId = FindPlaybackEndpoint(preferredId);
            if (endpointId is null) throw new IOException("尚未检测到播放驱动。请确认安装成功；若安装器提示重启，请重启后再次初始化。");
        }
        using var enumerator = new MMDeviceEnumerator();
        using var endpoint = enumerator.GetDevice(endpointId);
        if (Equals(endpoint.Properties[PropertyKeys.PKEY_Device_DeviceDesc].Value, PlaybackName)) return;
        var policy = (IPolicyConfig)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9"), throwOnError: true)!)!;
        // Windows builds the read-only FriendlyName from this editable endpoint description.
        var key = PropertyKeys.PKEY_Device_DeviceDesc;
        var name = new PropVariant { vt = (short)VarEnum.VT_LPWSTR, pointerValue = Marshal.StringToCoTaskMemUni(PlaybackName) };
        try { Marshal.ThrowExceptionForHR(policy.SetPropertyValue(endpointId, false, ref key, ref name)); }
        finally
        {
            Marshal.FreeCoTaskMem(name.pointerValue);
            Marshal.ReleaseComObject(policy);
        }
        using var verified = enumerator.GetDevice(endpointId);
        if (!Equals(verified.Properties[PropertyKeys.PKEY_Device_DeviceDesc].Value, PlaybackName))
            throw new IOException("播放驱动已安装，但设备名称未保存，请重新初始化。");
    }

    // Windows 10/11 PolicyConfig ABI follows AudioDeviceCmdlets (MIT).
    // SetPropertyValue includes bFxStore; omitting it corrupts the COM call.
    // https://github.com/frgnca/AudioDeviceCmdlets/blob/master/SOURCE/IPolicyConfig.cs
    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr format);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, [MarshalAs(UnmanagedType.Bool)] bool defaultFormat, IntPtr format);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, [MarshalAs(UnmanagedType.Bool)] bool defaultPeriod, IntPtr period, IntPtr minimumPeriod);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr period);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, [MarshalAs(UnmanagedType.Bool)] bool effectsStore, ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, [MarshalAs(UnmanagedType.Bool)] bool effectsStore, ref PropertyKey key, ref PropVariant value);
    }

    private static string? FindPlaybackEndpoint(string? preferredId)
    {
        using var enumerator = new MMDeviceEnumerator();
        string? first = null;
        string? preferred = null;
        foreach (var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (endpoint)
            {
                if (endpoint.DeviceFriendlyName != "VB-Audio Virtual Cable") continue;
                first ??= endpoint.ID;
                if (endpoint.ID == preferredId) preferred = endpoint.ID;
            }
        }
        return preferred ?? first;
    }
}
