using System.Runtime.InteropServices;

namespace CopyFinder.Services;

internal static class ShellFileSavePicker
{
    private const uint FOS_OVERWRITEPROMPT = 0x00000002;
    private const uint FOS_FORCEFILESYSTEM = 0x00000040;
    private const uint FOS_PATHMUSTEXIST = 0x00000800;
    private const uint SIGDN_FILESYSPATH = 0x80058000;
    private const int ERROR_CANCELLED = unchecked((int)0x800704C7);
    private static readonly Guid FileSaveDialogClsid = new("C0B4E2F3-BA21-4773-8DBA-335EC946EB8B");

    public static string? PickReportFile(IntPtr owner, string suggestedFileName)
    {
        var dialogType = Type.GetTypeFromCLSID(FileSaveDialogClsid, throwOnError: true)!;
        var dialog = (IFileSaveDialog)Activator.CreateInstance(dialogType)!;
        IShellItem? resultItem = null;
        IntPtr pathPointer = IntPtr.Zero;

        try
        {
            dialog.GetOptions(out var options);
            dialog.SetOptions(options | FOS_OVERWRITEPROMPT | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST);
            dialog.SetTitle("Export report");
            dialog.SetFileName($"{suggestedFileName}.csv");
            dialog.SetDefaultExtension("csv");
            dialog.SetFileTypes(2,
            [
                new FilterSpec("CSV report (*.csv)", "*.csv"),
                new FilterSpec("JSON report (*.json)", "*.json")
            ]);
            dialog.SetFileTypeIndex(1);

            var result = dialog.Show(owner);
            if (result == ERROR_CANCELLED)
            {
                return null;
            }

            Marshal.ThrowExceptionForHR(result);
            dialog.GetResult(out resultItem);
            resultItem.GetDisplayName(SIGDN_FILESYSPATH, out pathPointer);
            return Marshal.PtrToStringUni(pathPointer);
        }
        finally
        {
            if (pathPointer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(pathPointer);
            }

            if (resultItem is not null)
            {
                Marshal.ReleaseComObject(resultItem);
            }

            Marshal.ReleaseComObject(dialog);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private readonly struct FilterSpec(string name, string spec)
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public readonly string Name = name;

        [MarshalAs(UnmanagedType.LPWStr)]
        public readonly string Spec = spec;
    }

    [ComImport]
    [Guid("84BCCD23-5FDE-4CDB-AEA4-AF64B83D78AB")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileSaveDialog
    {
        [PreserveSig]
        int Show(IntPtr parent);
        void SetFileTypes(uint cFileTypes, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] FilterSpec[] rgFilterSpec);
        void SetFileTypeIndex(uint iFileType);
        void GetFileTypeIndex(out uint piFileType);
        void Advise(IntPtr pfde, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOptions(uint fos);
        void GetOptions(out uint pfos);
        void SetDefaultFolder(IShellItem psi);
        void SetFolder(IShellItem psi);
        void GetFolder(out IShellItem ppsi);
        void GetCurrentSelection(out IShellItem ppsi);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        void GetResult(out IShellItem ppsi);
        void AddPlace(IShellItem psi, uint fdap);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        void Close(int hr);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr pFilter);
        void SetSaveAsItem(IShellItem psi);
        void SetProperties(IntPtr pStore);
        void SetCollectedProperties(IntPtr pList, bool fAppendDefault);
        void GetProperties(out IntPtr ppStore);
        void ApplyProperties(IShellItem psi, IntPtr pStore, IntPtr hwnd, IntPtr pSink);
    }

    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdnName, out IntPtr ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }
}
