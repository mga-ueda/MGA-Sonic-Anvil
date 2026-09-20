using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows;
using ComIDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;
using WpfIDataObject = System.Windows.IDataObject;

namespace MgaSonicAnvil.UI;

/// <summary>
/// エクスプローラーへフォルダを渡すとき、WPF の MemoryStream だと
/// Preferred DropEffect が OLE に乗らず、同じドライブでは移動になる。
/// WPF の IDataObject としても渡し、DoDragDrop に包まれないようにする。
/// HGLOBAL の DWORD としてコピーを毎回渡す。
/// </summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
internal sealed class LibraryCopyDropDataObject : WpfIDataObject, ComIDataObject
{
    private const int SOk = 0;
    private const int SFalse = 1;
    private const int DataSSameFormatEtc = 0x00040130;
    private const int DvETymed = unchecked((int)0x80040069);
    private const int OleEAdviseNotSupported = unchecked((int)0x80040003);
    private const uint GmemMoveable = 0x0002;
    private const uint GmemZeroInit = 0x0040;

    private readonly DataObject _data;
    private readonly ushort _preferredFormat;

    private ComIDataObject Inner => _data;

    public LibraryCopyDropDataObject(DataObject inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _data = inner;
        _preferredFormat = Native.RegisterClipboardFormat(LibraryShellFiles.PreferredDropEffectFormat);
    }

    public static WpfIDataObject? FromPaths(IEnumerable<string?> paths)
    {
        var data = LibraryShellFiles.CreateCopyData(paths);
        return data is null ? null : new LibraryCopyDropDataObject(data);
    }

    object WpfIDataObject.GetData(string format, bool autoConvert) =>
        IsPreferredName(format)
            ? LibraryShellFiles.CreateCopyEffectStream()
            : _data.GetData(format, autoConvert);

    object WpfIDataObject.GetData(string format) =>
        ((WpfIDataObject)this).GetData(format, autoConvert: true);

    object WpfIDataObject.GetData(Type format) => _data.GetData(format);

    bool WpfIDataObject.GetDataPresent(string format, bool autoConvert) =>
        IsPreferredName(format) || _data.GetDataPresent(format, autoConvert);

    bool WpfIDataObject.GetDataPresent(string format) =>
        ((WpfIDataObject)this).GetDataPresent(format, autoConvert: true);

    bool WpfIDataObject.GetDataPresent(Type format) => _data.GetDataPresent(format);

    string[] WpfIDataObject.GetFormats(bool autoConvert)
    {
        var inner = _data.GetFormats(autoConvert);
        var list = new List<string>(inner.Length + 1)
        {
            LibraryShellFiles.PreferredDropEffectFormat,
        };
        foreach (var item in inner)
        {
            if (!IsPreferredName(item))
            {
                list.Add(item);
            }
        }

        return [.. list];
    }

    string[] WpfIDataObject.GetFormats() => ((WpfIDataObject)this).GetFormats(autoConvert: true);

    void WpfIDataObject.SetData(object data) => _data.SetData(data);

    void WpfIDataObject.SetData(string format, object data) => _data.SetData(format, data);

    void WpfIDataObject.SetData(string format, object data, bool autoConvert) =>
        _data.SetData(format, data, autoConvert);

    void WpfIDataObject.SetData(Type format, object data) => _data.SetData(format, data);

    private static bool IsPreferredName(string? format) =>
        string.Equals(format, LibraryShellFiles.PreferredDropEffectFormat, StringComparison.OrdinalIgnoreCase);

    internal static int? ReadPreferredDropEffect(ComIDataObject data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var format = PreferredFormatEtc(Native.RegisterClipboardFormat(LibraryShellFiles.PreferredDropEffectFormat));
        data.GetData(ref format, out var medium);
        try
        {
            if (medium.tymed != TYMED.TYMED_HGLOBAL || medium.unionmember == IntPtr.Zero)
            {
                return null;
            }

            var ptr = Native.GlobalLock(medium.unionmember);
            if (ptr == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return Marshal.ReadInt32(ptr);
            }
            finally
            {
                Native.GlobalUnlock(medium.unionmember);
            }
        }
        finally
        {
            Native.ReleaseStgMedium(ref medium);
        }
    }

    void ComIDataObject.GetData(ref FORMATETC format, out STGMEDIUM medium)
    {
        if (IsPreferred(format))
        {
            if ((format.tymed & TYMED.TYMED_HGLOBAL) == 0)
            {
                throw new COMException(null, DvETymed);
            }

            medium = AllocPreferred();
            return;
        }

        Inner.GetData(ref format, out medium);
    }

    void ComIDataObject.GetDataHere(ref FORMATETC format, ref STGMEDIUM medium)
    {
        if (!IsPreferred(format))
        {
            Inner.GetDataHere(ref format, ref medium);
            return;
        }

        if (medium.tymed != TYMED.TYMED_HGLOBAL || medium.unionmember == IntPtr.Zero)
        {
            throw new COMException(null, DvETymed);
        }

        WritePreferred(medium.unionmember);
    }

    int ComIDataObject.QueryGetData(ref FORMATETC format)
    {
        if (IsPreferred(format))
        {
            return (format.tymed & TYMED.TYMED_HGLOBAL) != 0 ? SOk : DvETymed;
        }

        return Inner.QueryGetData(ref format);
    }

    int ComIDataObject.GetCanonicalFormatEtc(ref FORMATETC formatIn, out FORMATETC formatOut)
    {
        if (IsPreferred(formatIn))
        {
            formatOut = PreferredFormatEtc(_preferredFormat);
            return DataSSameFormatEtc;
        }

        return Inner.GetCanonicalFormatEtc(ref formatIn, out formatOut);
    }

    void ComIDataObject.SetData(ref FORMATETC formatIn, ref STGMEDIUM medium, bool release) =>
        Inner.SetData(ref formatIn, ref medium, release);

    IEnumFORMATETC ComIDataObject.EnumFormatEtc(DATADIR direction)
    {
        var inner = Inner.EnumFormatEtc(direction);
        if (direction != DATADIR.DATADIR_GET)
        {
            return inner;
        }

        return new FormatEtcEnum(Merge(inner, PreferredFormatEtc(_preferredFormat)));
    }

    int ComIDataObject.DAdvise(ref FORMATETC pFormatetc, ADVF advf, IAdviseSink adviseSink, out int connection) =>
        Inner.DAdvise(ref pFormatetc, advf, adviseSink, out connection);

    void ComIDataObject.DUnadvise(int connection) => Inner.DUnadvise(connection);

    int ComIDataObject.EnumDAdvise(out IEnumSTATDATA? enumAdvise)
    {
        try
        {
            return Inner.EnumDAdvise(out enumAdvise);
        }
        catch (NotImplementedException)
        {
            enumAdvise = null;
            return OleEAdviseNotSupported;
        }
    }

    private bool IsPreferred(in FORMATETC format) =>
        unchecked((ushort)format.cfFormat) == _preferredFormat;

    private static FORMATETC PreferredFormatEtc(ushort format) =>
        new()
        {
            cfFormat = unchecked((short)format),
            dwAspect = DVASPECT.DVASPECT_CONTENT,
            lindex = -1,
            tymed = TYMED.TYMED_HGLOBAL,
            ptd = IntPtr.Zero,
        };

    private static STGMEDIUM AllocPreferred()
    {
        var handle = Native.GlobalAlloc(GmemMoveable | GmemZeroInit, (UIntPtr)4);
        if (handle == IntPtr.Zero)
        {
            throw new OutOfMemoryException();
        }

        WritePreferred(handle);
        return new STGMEDIUM
        {
            tymed = TYMED.TYMED_HGLOBAL,
            unionmember = handle,
            pUnkForRelease = null,
        };
    }

    private static void WritePreferred(IntPtr handle)
    {
        var ptr = Native.GlobalLock(handle);
        if (ptr == IntPtr.Zero)
        {
            throw new InvalidOperationException();
        }

        try
        {
            Marshal.WriteInt32(ptr, LibraryShellFiles.DropEffectCopy);
        }
        finally
        {
            Native.GlobalUnlock(handle);
        }
    }

    private static FORMATETC[] Merge(IEnumFORMATETC inner, FORMATETC extra)
    {
        var list = new List<FORMATETC> { extra };
        inner.Reset();
        var one = new FORMATETC[1];
        var fetched = new int[1];
        while (inner.Next(1, one, fetched) == 0 && fetched[0] == 1)
        {
            if (unchecked((ushort)one[0].cfFormat) != unchecked((ushort)extra.cfFormat))
            {
                list.Add(one[0]);
            }
        }

        return [.. list];
    }

    private sealed class FormatEtcEnum : IEnumFORMATETC
    {
        private readonly FORMATETC[] _items;
        private int _index;

        public FormatEtcEnum(FORMATETC[] items) => _items = items;

        public int Next(int celt, FORMATETC[] rgelt, int[] pceltFetched)
        {
            var n = 0;
            while (n < celt && _index < _items.Length)
            {
                rgelt[n++] = _items[_index++];
            }

            if (pceltFetched is { Length: > 0 })
            {
                pceltFetched[0] = n;
            }

            return n == celt ? SOk : SFalse;
        }

        public int Skip(int celt)
        {
            if (celt < 0)
            {
                return SFalse;
            }

            if (_index + celt > _items.Length)
            {
                _index = _items.Length;
                return SFalse;
            }

            _index += celt;
            return SOk;
        }

        public int Reset()
        {
            _index = 0;
            return SOk;
        }

        public void Clone(out IEnumFORMATETC newEnum) =>
            newEnum = new FormatEtcEnum(_items) { _index = _index };
    }

    private static class Native
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern ushort RegisterClipboardFormat(string format);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GlobalLock(IntPtr handle);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GlobalUnlock(IntPtr handle);

        [DllImport("ole32.dll")]
        public static extern void ReleaseStgMedium(ref STGMEDIUM medium);
    }
}
