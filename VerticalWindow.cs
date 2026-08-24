using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace VerticalView;

/// <summary>Presents the game's render target texture into a second SDL window via a shared
/// OpenGL context and a GPU-to-GPU framebuffer blit. Pixels never touch the CPU, and all
/// GL work for this window happens on a background thread; the game thread only sets an
/// integer and signals.</summary>
internal sealed class VerticalWindow : IDisposable
{
    private const uint GlReadFramebuffer = 0x8CA8;
    private const uint GlDrawFramebuffer = 0x8CA9;
    private const uint GlColorAttachment0 = 0x8CE0;
    private const uint GlTexture2D = 0x0DE1;
    private const uint GlColorBufferBit = 0x4000;
    private const uint GlLinear = 0x2601;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GenFramebuffers(int n, out uint framebuffers);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void BindFramebuffer(uint target, uint framebuffer);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void FramebufferTexture2D(uint target, uint attachment, uint textureTarget, uint texture, int level);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void BlitFramebuffer(int srcX0, int srcY0, int srcX1, int srcY1, int dstX0, int dstY0, int dstX1, int dstY1, uint mask, uint filter);

    private readonly IntPtr window;
    private readonly IntPtr context;
    private readonly int textureWidth;
    private readonly int textureHeight;
    private readonly Action<string> onError;
    private readonly AutoResetEvent signal = new AutoResetEvent(false);
    private readonly Thread thread;
    private volatile int presentTexture;
    private volatile bool disposed;

    /// <summary>Must be called on the game thread, with the game's GL context current, so the new context shares its textures.</summary>
    public VerticalWindow(string title, int windowWidth, int windowHeight, int textureWidth, int textureHeight, int x, int y, Action<string> onError)
    {
        this.textureWidth = textureWidth;
        this.textureHeight = textureHeight;
        this.onError = onError;
        IntPtr gameWindow = Sdl.GlGetCurrentWindow();
        IntPtr gameContext = Sdl.GlGetCurrentContext();
        if (gameContext == IntPtr.Zero)
        {
            throw new InvalidOperationException("no current GL context; the game is not using the OpenGL backend");
        }
        window = Sdl.CreateWindow(
            title,
            x == int.MinValue ? Sdl.WindowPosUndefined : x,
            y == int.MinValue ? Sdl.WindowPosUndefined : y,
            windowWidth, windowHeight, Sdl.WindowOpenGL | Sdl.WindowResizable);
        if (window == IntPtr.Zero)
        {
            throw new InvalidOperationException("SDL_CreateWindow: " + Sdl.GetError());
        }
        Sdl.GlSetAttribute(Sdl.GlShareWithCurrentContext, 1);
        context = Sdl.GlCreateContext(window);
        Sdl.GlMakeCurrent(gameWindow, gameContext);
        if (context == IntPtr.Zero)
        {
            Sdl.DestroyWindow(window);
            throw new InvalidOperationException("SDL_GL_CreateContext: " + Sdl.GetError());
        }
        thread = new Thread(PresentLoop) { IsBackground = true, Name = "VerticalView present" };
        thread.Start();
    }

    /// <summary>Queue a finished GL texture for display. Never blocks; if the worker is busy, the newest texture wins.</summary>
    public void Present(int glTexture)
    {
        if (disposed)
        {
            return;
        }
        presentTexture = glTexture;
        signal.Set();
    }

    private static T Load<T>(string name) where T : Delegate
    {
        IntPtr pointer = Sdl.GlGetProcAddress(name);
        if (pointer == IntPtr.Zero)
        {
            throw new InvalidOperationException("missing GL function: " + name);
        }
        return Marshal.GetDelegateForFunctionPointer<T>(pointer);
    }

    private void PresentLoop()
    {
        try
        {
            if (Sdl.GlMakeCurrent(window, context) != 0)
            {
                onError("SDL_GL_MakeCurrent: " + Sdl.GetError());
                return;
            }
            Sdl.GlSetSwapInterval(0); // never wait for vblank
            GenFramebuffers glGenFramebuffers = Load<GenFramebuffers>("glGenFramebuffers");
            BindFramebuffer glBindFramebuffer = Load<BindFramebuffer>("glBindFramebuffer");
            FramebufferTexture2D glFramebufferTexture2D = Load<FramebufferTexture2D>("glFramebufferTexture2D");
            BlitFramebuffer glBlitFramebuffer = Load<BlitFramebuffer>("glBlitFramebuffer");
            glGenFramebuffers(1, out uint framebuffer);
            while (true)
            {
                signal.WaitOne();
                if (disposed)
                {
                    break;
                }
                int texture = presentTexture;
                if (texture <= 0)
                {
                    continue;
                }
                glBindFramebuffer(GlReadFramebuffer, framebuffer);
                glFramebufferTexture2D(GlReadFramebuffer, GlColorAttachment0, GlTexture2D, (uint)texture, 0);
                glBindFramebuffer(GlDrawFramebuffer, 0);
                Sdl.GlGetDrawableSize(window, out int windowWidth, out int windowHeight);
                // source y inverted: MonoGame render targets store row 0 at the visual top, GL's default framebuffer is bottom-up
                glBlitFramebuffer(0, textureHeight, textureWidth, 0, 0, 0, windowWidth, windowHeight, GlColorBufferBit, GlLinear);
                Sdl.GlSwapWindow(window);
            }
        }
        catch (Exception exception)
        {
            onError("vertical window present thread died: " + exception);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        signal.Set();
        thread.Join(1000);
        Sdl.GlDeleteContext(context);
        Sdl.DestroyWindow(window);
    }
}
