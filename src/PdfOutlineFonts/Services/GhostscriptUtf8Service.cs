using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using PdfOutlineFonts.ViewModels;

namespace PdfOutlineFonts.Services;

public sealed class GhostscriptUtf8Service : IGhostscriptService
{
    private const int GsArgEncodingUtf8 = 1;
    private const int GsQuit = -101;

    private readonly SemaphoreSlim initializeLock = new(1, 1);

    private bool initialized;
    private string? dllPath;
    private IntPtr libraryHandle;

    private GsApiNewInstance? gsApiNewInstance;
    private GsApiDeleteInstance? gsApiDeleteInstance;
    private GsApiSetArgEncoding? gsApiSetArgEncoding;
    private GsApiInitWithArgs? gsApiInitWithArgs;
    private GsApiExit? gsApiExit;

    public string? VersionText { get; private set; }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (initialized)
        {
            return;
        }

        await initializeLock.WaitAsync(cancellationToken);
        try
        {
            if (initialized)
            {
                return;
            }

            dllPath = await EnsureGhostscriptDllAsync(cancellationToken);
            LoadNativeApi(dllPath);

            var version = FileVersionInfo.GetVersionInfo(dllPath).FileVersion;
            VersionText = string.IsNullOrWhiteSpace(version) ? "未知版本" : version;

            initialized = true;
        }
        finally
        {
            initializeLock.Release();
        }
    }

    public async Task ConvertAsync(string inputPath, string outputPath, PdfConvertMode convertMode, int dpi, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var folder = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            Directory.CreateDirectory(folder);
        }

        var safeDpi = Math.Clamp(dpi, 72, 1200);
        var args = BuildArgs(inputPath, outputPath, convertMode, safeDpi);

        await Task.Run(() => ExecuteGhostscript(args, cancellationToken), cancellationToken);
    }

    private static string[] BuildArgs(string inputPath, string outputPath, PdfConvertMode convertMode, int dpi)
    {
        return convertMode switch
        {
            PdfConvertMode.Vector =>
            [
                "gs",
                "-dBATCH",
                "-dNOPAUSE",
                "-dQUIET",
                "-sDEVICE=pdfwrite",
                "-dNoOutputFonts",
                "-dCompatibilityLevel=1.4",
                $"-sOutputFile={outputPath}",
                inputPath
            ],
            PdfConvertMode.Image =>
            [
                "gs",
                "-dBATCH",
                "-dNOPAUSE",
                "-dQUIET",
                "-sDEVICE=pdfimage24",
                "-dAutoRotatePages=/None",
                "-dTextAlphaBits=4",
                "-dGraphicsAlphaBits=4",
                "-dAlignToPixels=0",
                "-dGridFitTT=2",
                $"-r{dpi}",
                $"-sOutputFile={outputPath}",
                inputPath
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(convertMode), convertMode, null)
        };
    }

    private void ExecuteGhostscript(string[] args, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var newInstance = gsApiNewInstance ?? throw new InvalidOperationException("Ghostscript API 未初始化。");
        var deleteInstance = gsApiDeleteInstance ?? throw new InvalidOperationException("Ghostscript API 未初始化。");
        var setArgEncoding = gsApiSetArgEncoding ?? throw new InvalidOperationException("Ghostscript API 未初始化。");
        var initWithArgs = gsApiInitWithArgs ?? throw new InvalidOperationException("Ghostscript API 未初始化。");
        var exit = gsApiExit ?? throw new InvalidOperationException("Ghostscript API 未初始化。");

        var argPointers = new IntPtr[args.Length];
        var argv = IntPtr.Zero;
        IntPtr instance = IntPtr.Zero;

        try
        {
            for (var i = 0; i < args.Length; i++)
            {
                argPointers[i] = Marshal.StringToCoTaskMemUTF8(args[i]);
            }

            argv = Marshal.AllocHGlobal(IntPtr.Size * args.Length);
            for (var i = 0; i < args.Length; i++)
            {
                Marshal.WriteIntPtr(argv, i * IntPtr.Size, argPointers[i]);
            }

            ThrowIfFailed(newInstance(out instance, IntPtr.Zero), "gsapi_new_instance");
            ThrowIfFailed(setArgEncoding(instance, GsArgEncodingUtf8), "gsapi_set_arg_encoding");

            var initCode = initWithArgs(instance, args.Length, argv);
            if (initCode != 0 && initCode != GsQuit)
            {
                ThrowIfFailed(initCode, "gsapi_init_with_args");
            }

            var exitCode = exit(instance);
            if (exitCode != 0 && exitCode != GsQuit)
            {
                ThrowIfFailed(exitCode, "gsapi_exit");
            }
        }
        finally
        {
            if (instance != IntPtr.Zero)
            {
                deleteInstance(instance);
            }

            if (argv != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(argv);
            }

            for (var i = 0; i < argPointers.Length; i++)
            {
                if (argPointers[i] != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(argPointers[i]);
                }
            }
        }
    }

    private void LoadNativeApi(string nativeDllPath)
    {
        libraryHandle = NativeLibrary.Load(nativeDllPath);

        gsApiNewInstance = LoadDelegate<GsApiNewInstance>("gsapi_new_instance");
        gsApiDeleteInstance = LoadDelegate<GsApiDeleteInstance>("gsapi_delete_instance");
        gsApiSetArgEncoding = LoadDelegate<GsApiSetArgEncoding>("gsapi_set_arg_encoding");
        gsApiInitWithArgs = LoadDelegate<GsApiInitWithArgs>("gsapi_init_with_args");
        gsApiExit = LoadDelegate<GsApiExit>("gsapi_exit");
    }

    private T LoadDelegate<T>(string exportName) where T : Delegate
    {
        if (!NativeLibrary.TryGetExport(libraryHandle, exportName, out var export))
        {
            throw new EntryPointNotFoundException($"未找到 Ghostscript 导出函数: {exportName}");
        }

        return Marshal.GetDelegateForFunctionPointer<T>(export);
    }

    private static void ThrowIfFailed(int code, string apiName)
    {
        if (code < 0)
        {
            throw new InvalidOperationException($"Ghostscript 调用失败: {apiName}, 错误码: {code}");
        }
    }

    private static async Task<string> EnsureGhostscriptDllAsync(CancellationToken cancellationToken)
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "PdfOutlineFonts");
        Directory.CreateDirectory(tempDirectory);

        var targetPath = Path.Combine(tempDirectory, "gsdll64.dll");
        if (File.Exists(targetPath))
        {
            return targetPath;
        }

        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith("Assets.gsdll64.dll", StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
        {
            throw new FileNotFoundException("未找到嵌入资源 gsdll64.dll。请将 gsdll64.dll 放入 Assets 目录并设置为 Embedded Resource。");
        }

        await using var resourceStream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException("读取嵌入资源 gsdll64.dll 失败。");

        await using var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await resourceStream.CopyToAsync(fileStream, cancellationToken);

        return targetPath;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GsApiNewInstance(out IntPtr instance, IntPtr callerHandle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GsApiDeleteInstance(IntPtr instance);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GsApiSetArgEncoding(IntPtr instance, int encoding);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GsApiInitWithArgs(IntPtr instance, int argc, IntPtr argv);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GsApiExit(IntPtr instance);
}
