using System;
using System.Runtime.InteropServices;

namespace VerticalView;

internal static class Sdl
{
    private const string Library = "SDL2.dll";

    public const uint WindowOpenGL = 0x00000002;
    public const uint WindowResizable = 0x00000020;
    public const int WindowPosUndefined = 0x1FFF0000;
    public const int GlShareWithCurrentContext = 22;

    static Sdl()
    {
        NativeLibrary.SetDllImportResolver(typeof(Sdl).Assembly, Resolve);
    }

    private static IntPtr Resolve(string name, System.Reflection.Assembly assembly, DllImportSearchPath? path)
    {
        if (name != Library)
        {
            return IntPtr.Zero;
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return NativeLibrary.Load("libSDL2-2.0.so.0");
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return NativeLibrary.Load("libSDL2-2.0.0.dylib");
        }
        return IntPtr.Zero;
    }

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_CreateWindow")]
    public static extern IntPtr CreateWindow([MarshalAs(UnmanagedType.LPUTF8Str)] string title, int x, int y, int width, int height, uint flags);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_DestroyWindow")]
    public static extern void DestroyWindow(IntPtr window);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GL_SetAttribute")]
    public static extern int GlSetAttribute(int attribute, int value);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GL_CreateContext")]
    public static extern IntPtr GlCreateContext(IntPtr window);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GL_DeleteContext")]
    public static extern void GlDeleteContext(IntPtr context);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GL_MakeCurrent")]
    public static extern int GlMakeCurrent(IntPtr window, IntPtr context);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GL_GetCurrentWindow")]
    public static extern IntPtr GlGetCurrentWindow();

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GL_GetCurrentContext")]
    public static extern IntPtr GlGetCurrentContext();

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GL_SetSwapInterval")]
    public static extern int GlSetSwapInterval(int interval);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GL_SwapWindow")]
    public static extern void GlSwapWindow(IntPtr window);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GL_GetDrawableSize")]
    public static extern void GlGetDrawableSize(IntPtr window, out int width, out int height);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GL_GetProcAddress")]
    public static extern IntPtr GlGetProcAddress([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GetError")]
    private static extern IntPtr GetErrorPointer();

    public static string GetError()
    {
        return Marshal.PtrToStringUTF8(GetErrorPointer()) ?? string.Empty;
    }
}
