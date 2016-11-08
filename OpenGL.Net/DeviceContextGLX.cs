
// Copyright (C) 2012-2016 Luca Piccioni
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301
// USA

using System;
using System.Diagnostics;
using System.Collections.Generic;

namespace OpenGL
{
	/// <summary>
	///Device context for Unix-based platforms.
	/// </summary>
	sealed class DeviceContextGLX : DeviceContext
	{
		#region Constructors

		/// <summary>
		/// Initializes the <see cref="DeviceContextGLX"/> class.
		/// </summary>
		static DeviceContextGLX()
		{
			// Be notified about XServer errors
			Glx.UnsafeNativeMethods.XSetErrorHandler(XServerErrorHandler);
		}

		/// <summary>
		/// Construct a <see cref="Derm.Render.XServerDeviceContext"/> class, initialized with the display of a control.
		/// </summary>
		/// <param name="display">
		/// A <see cref="IntPtr"/> that specifies the display handle used to create <paramref name="windowHandle"/>.
		/// Specifies the connection to the X server.
		/// </param>
		/// <param name='windowHandle'>
		/// A <see cref="IntPtr"/> that specifies the window handle used to create the device context.
		/// </param>
		/// <exception cref='ArgumentException'>
		/// Is thrown when <paramref name="windowHandle"/> is <see cref="IntPtr.Zero"/>.
		/// </exception>
		/// <exception cref="PlatformNotSupportedException">
		/// Exception thrown if the current assembly is not executed by a (supported) Mono runtime.
		/// </exception>
		/// <exception cref="InvalidOperationException">
		/// Exception thrown if the current Mono runtime has not yet opened a display connection.
		/// </exception>
		public DeviceContextGLX(IntPtr display, IntPtr windowHandle)
		{
			if (display == IntPtr.Zero)
				throw new ArgumentException("invalid X display", "display");
			if (windowHandle == IntPtr.Zero)
				throw new ArgumentException("invalid X window", "windowHandle");

			_Display = display;
			_WindowHandle = windowHandle;

			// Query GLX extensions
			QueryVersion();

			if (Version < Glx.Version_130)
				throw new NotSupportedException("missing GLX 1.3 or greater");
		}

		#endregion

		#region Device Information

		/// <summary>
		/// The opened display.
		/// </summary>
		public IntPtr Display
		{
			get
			{
				if (IsDisposed)
					throw new ObjectDisposedException("XServerDeviceContext");
				return (_Display);
			}
		}

		/// <summary>
		/// The opened display.
		/// </summary>
		private readonly IntPtr _Display;

		/// <summary>
		/// The window handle.
		/// </summary>
		private readonly IntPtr _WindowHandle;

		/// <summary>
		/// The framebuffer configuration (<see cref="SetPixelFormat"/>).
		/// </summary>
		private IntPtr _FBConfig;

		/// <summary>
		/// The <see cref="Glx.XVisualInfo"/> corresponding to <see cref="_FBConfig"/>
		/// </summary>
		private Glx.XVisualInfo _XVisualInfo;
		
		#endregion

		#region Window Factory

		/// <summary>
		/// Native window implementation for Windows.
		/// </summary>
		internal class NativeWindow : INativeWindow
		{
			#region Constructors

			/// <summary>
			/// Default constructor.
			/// </summary>
			public NativeWindow()
			{
				try {
					// Open display
					if ((_Display = Glx.XOpenDisplay(IntPtr.Zero)) == IntPtr.Zero)
						throw new InvalidOperationException("unable to connect to X server");

					Glx.XVisualInfo visual;
					IntPtr config;

					int[] attributes = new int[] {
						Glx.DRAWABLE_TYPE, (int)Glx.WINDOW_BIT,
						Glx.RENDER_TYPE, (int)Glx.RGBA_BIT,
						Glx.DOUBLEBUFFER,  unchecked((int)Glx.DONT_CARE),
						Glx.RED_SIZE, 1,
						Glx.GREEN_SIZE, 1,
						Glx.BLUE_SIZE, 1,
						0
					};

					int screen = Glx.XDefaultScreen(_Display);

					// Get basic visual
					unsafe {
						int[] choosenConfigCount = new int[1];

						IntPtr* choosenConfigs = Glx.ChooseFBConfig(_Display, screen, attributes, choosenConfigCount);
						if (choosenConfigCount[0] == 0)
							throw new InvalidOperationException("unable to find basic visual");
						config = *choosenConfigs;
						KhronosApi.LogComment("Choosen config is 0x{0}", config.ToString("X8"));

						visual = Glx.GetVisualFromFBConfig(_Display, config);
						KhronosApi.LogComment("Choosen visual is {0}", visual);

						Glx.XFree((IntPtr)choosenConfigs);

						_InternalConfig = config;
						_InternalVisual = visual;
					}

					Glx.XSetWindowAttributes setWindowAttrs = new Glx.XSetWindowAttributes();
					IntPtr rootWindow = Glx.XRootWindow(_Display, screen);
					ulong setWindowAttrFlags = /* CWBorderPixel | CWColormap | CWEventMask*/ (1L<<3) | (1L<<13) | (1L<<11);

					setWindowAttrs.border_pixel = IntPtr.Zero;
					setWindowAttrs.event_mask = /* StructureNotifyMask	*/ new IntPtr(1L << 17);
					setWindowAttrs.colormap = Glx.XCreateColormap(_Display, rootWindow, visual.visual, /* AllocNone */ 0);

					if ((_Handle = Glx.XCreateWindow(_Display, rootWindow, 0, 0, 64, 64, 0, visual.depth, /* InputOutput */ 0, visual.visual, new UIntPtr(setWindowAttrFlags), ref setWindowAttrs)) == IntPtr.Zero)
						throw new InvalidOperationException("unable to create window");

					// Assign FB configuration to window: essential to make CreateContext(IntPtr) working
					_GlxHandle = Glx.CreateWindow(_Display, config, _Handle, null);
				
				} catch {
					Dispose();
					throw;
				}
			}

			internal static IntPtr _InternalConfig;

			internal static Glx.XVisualInfo _InternalVisual;

			#endregion

			#region INativeWindow Implementation

			/// <summary>
			/// Get the display handle associated this instance.
			/// </summary>
			IntPtr INativeWindow.Display { get { return (_Display); } }

			/// <summary>
			/// The native window handle.
			/// </summary>
			private IntPtr _Display;

			/// <summary>
			/// Get the native window handle.
			/// </summary>
			IntPtr INativeWindow.Handle { get { return (_GlxHandle); } }

			/// <summary>
			/// The native window handle.
			/// </summary>
			private IntPtr _Handle;

			/// <summary>
			/// The GLX window handle.
			/// </summary>
			private IntPtr _GlxHandle;

			/// <summary>
			/// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
			/// </summary>
			public void Dispose()
			{
				if (_Handle != IntPtr.Zero) {
					Glx.UnsafeNativeMethods.XDestroyWindow(_Display, _Handle);
					_Handle = IntPtr.Zero;
				}

				if (_Display != IntPtr.Zero) {
					Glx.UnsafeNativeMethods.XCloseDisplay(_Display);
					_Display = IntPtr.Zero;
				}
			}

			#endregion
		}

		#endregion
		
		#region Multithreading Support
		
		/// <summary>
		/// Initializes the X11 multithreading.
		/// </summary>
		/// <exception cref='InvalidOperationException'>
		/// Is thrown when an operation cannot be performed.
		/// </exception>
		public static void InitializeMultithreading()
		{
			// Ensure to have X11 thread system initialized
			int initialized = Glx.UnsafeNativeMethods.XInitThreads();
			KhronosApi.LogFunction("XInitThreads() = {0}", initialized);
			
			if (initialized == 0)
				throw new InvalidOperationException("platform does not support multithreading");
			
			_MultithreadingInitialized = true;
		}
		
		/// <summary>
		/// Gets a value indicating whether if X11 multithreading is initialized.
		/// </summary>
		/// <value>
		/// It returns <c>true</c> if this instance is X11 multithreading is initialized; otherwise, <c>false</c>.
		/// </value>
		internal static bool IsMultithreadingInitialized { get { return (_MultithreadingInitialized); } }
		
		/// <summary>
		/// Flag indicating whether X11 multithreading is initialized.
		/// </summary>
		private static bool _MultithreadingInitialized;
		
		#endregion
		
		#region GLX Version
		
		/// <summary>
		/// Get the GLX version.
		/// </summary>
		public KhronosVersion Version { get { return (_GlxVersion); } }
		
		/// <summary>
		/// Query the GLX version supported by current implementation.
		/// </summary>
		private void QueryVersion()
		{
			using (Glx.XLock xLock = new Glx.XLock(Display)) {
				int[] majorArg = new int[1], minorArg = new int[1];
	
				Glx.QueryVersion(Display, majorArg, minorArg);

				_GlxVersion = new KhronosVersion(majorArg[0], minorArg[0], KhronosVersion.ApiGlx);
			}
		}
		
		/// <summary>
		/// The GLX major version.
		/// </summary>
		private KhronosVersion _GlxVersion;

		#endregion

		#region DeviceContext Overrides

		/// <summary>
		/// Create a simple context.
		/// </summary>
		/// <returns>
		/// A <see cref="IntPtr"/> that represents the handle of the created context. If the context cannot be
		/// created, it returns IntPtr.Zero.
		/// </returns>
		internal override IntPtr CreateSimpleContext()
		{
			using (Glx.XLock xLock = new Glx.XLock(Display)) {
				// Create direct context
				IntPtr rContext = CreateContext(IntPtr.Zero);

				if (rContext == IntPtr.Zero)
					throw new InvalidOperationException("unable to create context");

				return (rContext);
			}
		}

		/// <summary>
		/// Creates a context.
		/// </summary>
		/// <param name="sharedContext">
		/// A <see cref="IntPtr"/> that specify a context that will share objects with the returned one. If
		/// it is IntPtr.Zero, no sharing is performed.
		/// </param>
		/// <returns>
		/// A <see cref="IntPtr"/> that represents the handle of the created context. If the context cannot be
		/// created, it returns IntPtr.Zero.
		/// </returns>
		/// <exception cref="InvalidOperationException">
		/// Exception thrown in the case <paramref name="sharedContext"/> is different from IntPtr.Zero, and the objects
		/// cannot be shared with it.
		/// </exception>
		public override IntPtr CreateContext(IntPtr sharedContext)
		{
			using (Glx.XLock displayLock = new Glx.XLock(Display)) {
				// Get the corresponding X visual info
				Glx.XVisualInfo xVisualInfo = _XVisualInfo != null ? _XVisualInfo : GetVisualInfoFromXWindow(_WindowHandle);

				Debug.Assert(xVisualInfo != null, "SetPixelFormat not executed or undetected XVisualInfo");
				return (Glx.CreateContext(Display, xVisualInfo, sharedContext, true));
			}
		}

		/// <summary>
		/// Get the <see cref="Glx.XVisualInfo"/> set on the specified X window.
		/// </summary>
		/// <param name="xWindow">
		/// The <see cref="IntPtr"/> that specifies the handle of the X window.
		/// </param>
		/// <returns>
		/// It returns the <see cref="Glx.XVisualInfo"/> set on <paramref name="xWindow"/>.
		/// </returns>
		private Glx.XVisualInfo GetVisualInfoFromXWindow(IntPtr xWindow)
		{
			Glx.XVisualInfo xVisualInfo;
			uint[] windowFBConfigId = new uint[1];
			int screen = Glx.XDefaultScreen(_Display);

			// Get the FB configuration associated to the native window
			Glx.QueryDrawable(_Display, _WindowHandle, Glx.FBCONFIG_ID, windowFBConfigId);

			if (windowFBConfigId[0] == 0) {
				KhronosApi.LogComment("Glx.QueryDrawable cannot query Glx.FBCONFIG_ID. Query manually.");

				return (NativeWindow._InternalVisual);
			}

			unsafe {
				int[] attributes = new int[] {
					Glx.FBCONFIG_ID, (int)windowFBConfigId[0],
					0,
				};

				int[] choosenConfigCount = new int[1];

				IntPtr* choosenConfigs = Glx.ChooseFBConfig(_Display, screen, attributes, choosenConfigCount);
				if (choosenConfigCount[0] == 0)
					throw new InvalidOperationException("unable to find X Window visual configuration");
				IntPtr configId = *choosenConfigs;

				xVisualInfo = Glx.GetVisualFromFBConfig(_Display, configId);

				Glx.XFree((IntPtr)choosenConfigs);
			}

			return (xVisualInfo);
		}

		/// <summary>
		/// Creates a context, specifying attributes.
		/// </summary>
		/// <param name="sharedContext">
		/// A <see cref="IntPtr"/> that specify a context that will share objects with the returned one. If
		/// it is IntPtr.Zero, no sharing is performed.
		/// </param>
		/// <param name="attribsList">
		/// A <see cref="T:Int32[]"/> that specifies the attributes list.
		/// </param>
		/// <returns>
		/// A <see cref="IntPtr"/> that represents the handle of the created context. If the context cannot be
		/// created, it returns IntPtr.Zero.
		/// </returns>
		/// <exception cref="ArgumentNullException">
		/// Exception thrown if <see cref="attribsList"/> is null.
		/// </exception>
		/// <exception cref="ArgumentException">
		/// Exception thrown if <paramref name="attribsList"/> length is zero or if the last item of <paramref name="attribsList"/>
		/// is not zero.
		/// </exception>
		public override IntPtr CreateContextAttrib(IntPtr sharedContext, int[] attribsList)
		{
			if (attribsList == null)
				throw new ArgumentNullException("attribsList");
			if (attribsList.Length == 0)
				throw new ArgumentException("zero length array", "attribsList");
			if (attribsList[attribsList.Length - 1] != 0)
				throw new ArgumentException("not zero-terminated array", "attribsList");

			using (Glx.XLock displayLock = new Glx.XLock(Display)) {
				Debug.Assert(_FBConfig != IntPtr.Zero, "SetPixelFormat not executed");
				return (Glx.CreateContextAttribsARB(Display, _FBConfig, sharedContext, true, attribsList));
			}
		}

		/// <summary>
		/// Makes the context current on the calling thread.
		/// </summary>
		/// <param name="ctx">
		/// A <see cref="IntPtr"/> that specify the context to be current on the calling thread, bound to
		/// thise device context. It can be IntPtr.Zero indicating that no context will be current.
		/// </param>
		/// <returns>
		/// It returns a boolean value indicating whether the operation was successful.
		/// </returns>
		/// <exception cref="NotSupportedException">
		/// Exception thrown if the current platform is not supported.
		/// </exception>
		protected override bool MakeCurrentCore(IntPtr ctx)
		{
			using (Glx.XLock displayLock = new Glx.XLock(Display)) {
				return (Glx.MakeCurrent(Display, ctx != IntPtr.Zero ? _WindowHandle : IntPtr.Zero, ctx));
			}
		}

		/// <summary>
		/// Deletes a context.
		/// </summary>
		/// <param name="ctx">
		/// A <see cref="IntPtr"/> that specify the context to be deleted.
		/// </param>
		/// <returns>
		/// It returns a boolean value indicating whether the operation was successful. If it returns false,
		/// query the exception by calling <see cref="GetPlatformException"/>.
		/// </returns>
		/// <remarks>
		/// <para>The context <paramref name="ctx"/> must not be current on any thread.</para>
		/// </remarks>
		/// <exception cref="ArgumentException">
		/// Exception thrown if <paramref name="ctx"/> is IntPtr.Zero.
		/// </exception>
		public override bool DeleteContext(IntPtr ctx)
		{
			if (ctx == IntPtr.Zero)
				throw new ArgumentException("ctx");

			using (Glx.XLock displayLock = new Glx.XLock(Display)) {
				Glx.DestroyContext(Display, ctx);
			}

			return (true);
		}

		/// <summary>
		/// Swap the buffers of a device.
		/// </summary>
		public override void SwapBuffers()
		{
			using (Glx.XLock displayLock = new Glx.XLock(Display)) {
				Glx.SwapBuffers(Display, _WindowHandle);
			}
		}

		/// <summary>
		/// Control the the buffers swap of a device.
		/// </summary>
		/// <param name="interval">
		/// A <see cref="System.Int32"/> that specifies the minimum number of video frames that are displayed
		/// before a buffer swap will occur.
		/// </param>
		/// <returns>
		/// It returns a boolean value indicating whether the operation was successful.
		/// </returns>
		public override bool SwapInterval(int interval)
		{
			// Keep into account the SwapIntervalEXT and SwapIntervalSGI entry points, relative to
			// two equivalent GLX extensions

			return (true);

			using (Glx.XLock displayLock = new Glx.XLock(Display)) {
				if (Glx.Delegates.pglXSwapIntervalEXT != null) {
					Glx.SwapIntervalEXT(Display, _WindowHandle, interval);
					return (true);
				} else if (Glx.Delegates.pglXSwapIntervalSGI != null)
					return (Glx.SwapIntervalSGI(interval) == 0);
				else
					throw new InvalidOperationException("binding point SwapInterval{EXT|SGI} cannot be found");
			}
		}

		/// <summary>
		/// Query platform extensions available.
		/// </summary>
		internal override void QueryPlatformExtensions()
		{
			Glx._CurrentExtensions = new Glx.Extensions();
			Glx._CurrentExtensions.Query(this);
		}

		/// <summary>
		/// Gets the platform exception relative to the last operation performed.
		/// </summary>
		/// <returns>
		/// The platform exception relative to the last operation performed.
		/// </returns>
		/// <exception cref="NotSupportedException">
		/// Exception thrown if the current platform is not supported.
		/// </exception>
		[System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Interoperability", "CA1404:CallGetLastErrorImmediatelyAfterPInvoke")]
		public override Exception GetPlatformException()
		{
			Exception platformException = null;

			lock (_DisplayErrorsLock) {
				if ((platformException = _DisplayErrors[Display]) != null)
					_DisplayErrors[Display] = null;
			}

			return (platformException);
		}

		/// <summary>
		/// The XServer error handler, invoked each time a X/GLX routine raise an error.
		/// </summary>
		/// <param name="displayHandle">
		/// A <see cref="IntPtr"/> that specifies the handle of the display on which the error occurred.
		/// </param>
		/// <param name="error_event">
		/// A <see cref="Glx.XErrorEvent"/> that describe the error.
		/// </param>
		/// <returns>
		/// It returns always 0.
		/// </returns>
		private static int XServerErrorHandler(IntPtr displayHandle, ref Glx.XErrorEvent error_event)
		{
			lock (_DisplayErrorsLock) {
				_DisplayErrors[displayHandle] = new GlxException(displayHandle, ref error_event);
			}

			return (0);
		}

		/// <summary>
		/// The display errors list lock.
		/// </summary>
		internal static readonly object _DisplayErrorsLock = new object();

		/// <summary>
		/// The display errors.
		/// </summary>
		internal static readonly Dictionary<IntPtr, Exception> _DisplayErrors = new Dictionary<IntPtr, Exception>();

		/// <summary>
		/// Get the pixel formats supported by this device.
		/// </summary>
		public override DevicePixelFormatCollection PixelsFormats
		{
			get
			{
				// Query GLX extensions
				Glx.Extensions glxExtensions = new Glx.Extensions();

				glxExtensions.Query(this);

				// Request configurations
				DevicePixelFormatCollection pFormats = new DevicePixelFormatCollection();

				using (Glx.XLock xLock = new Glx.XLock(Display)) {
					int configsCount = 0;

					unsafe
					{
						int screen = Glx.UnsafeNativeMethods.XDefaultScreen(_Display);
						IntPtr* configs = Glx.GetFBConfigs(Display, screen, out configsCount);

						for (int i = 0; i < configsCount; i++) {
							IntPtr configId = configs[i];
							int err, renderType, attribValue;

							#region Satisfy minimum requirements

							// Requires RGBA configuration
							err = Glx.GetFBConfigAttrib(Display, configId, Glx.RENDER_TYPE, out renderType);
							if (err != 0)
								throw new InvalidOperationException();
							if ((renderType & Glx.RGBA_BIT) == 0)
								continue;       // Ignore indexed visuals

							// Do not choose configurations with some caveat
							err = Glx.GetFBConfigAttrib(Display, configId, Glx.CONFIG_CAVEAT, out attribValue);
							if (attribValue == Glx.SLOW_CONFIG)
								continue;

							#endregion

							DevicePixelFormat pixelFormat = new DevicePixelFormat();

							pixelFormat.XFbConfig = configId;

							pixelFormat.XVisualInfo = Glx.GetVisualFromFBConfig(Display, configId);
							pixelFormat.RgbaUnsigned = (renderType & Glx.RGBA_FLOAT_BIT_ARB) == 0; ;
							pixelFormat.RgbaFloat = (renderType & Glx.RGBA_FLOAT_BIT_ARB) != 0;

							err = Glx.GetFBConfigAttrib(Display, configId, Glx.DRAWABLE_TYPE, out attribValue);
							if (err != 0)
								throw new InvalidOperationException("unable to get DRAWABLE_TYPE from framebuffer configuration");

							pixelFormat.RenderWindow = (attribValue & Glx.WINDOW_BIT) != 0;
							pixelFormat.RenderBuffer = false;
							pixelFormat.RenderPBuffer = (attribValue & Glx.PBUFFER_BIT) != 0;

							err = Glx.GetFBConfigAttrib(Display, configId, Glx.FBCONFIG_ID, out pixelFormat.FormatIndex);
							if (err != 0)
								throw new InvalidOperationException("unable to get FBCONFIG_ID from framebuffer configuration");

							err = Glx.GetFBConfigAttrib(Display, configId, Glx.BUFFER_SIZE, out pixelFormat.ColorBits);
							if (err != 0)
								throw new InvalidOperationException("unable to get BUFFER_SIZE from framebuffer configuration");

							err = Glx.GetFBConfigAttrib(Display, configId, Glx.DEPTH_SIZE, out pixelFormat.DepthBits);
							if (err != 0)
								throw new InvalidOperationException("unable to get DEPTH_SIZE from framebuffer configuration");

							err = Glx.GetFBConfigAttrib(Display, configId, Glx.STENCIL_SIZE, out pixelFormat.StencilBits);
							if (err != 0)
								throw new InvalidOperationException("unable to get STENCIL_SIZE from framebuffer configuration");

							if (glxExtensions.Multisample_ARB) {
								int hasMultisample = 0;

								err = Glx.GetFBConfigAttrib(Display, configId, Glx.SAMPLE_BUFFERS, out hasMultisample);
								if (err != 0)
									throw new InvalidOperationException("unable to get SAMPLE_BUFFERS from framebuffer configuration");

								if (hasMultisample != 0) {
									pixelFormat.MultisampleBits = 0;
									err = Glx.GetFBConfigAttrib(Display, configId, Glx.SAMPLES, out pixelFormat.MultisampleBits);
									if (err != 0)
										throw new InvalidOperationException("unable to get SAMPLES from framebuffer configuration");
								} else
									pixelFormat.MultisampleBits = 0;
							}

							err = Glx.GetFBConfigAttrib(Display, configId, Glx.DOUBLEBUFFER, out attribValue);
							if (err != 0)
								throw new InvalidOperationException("unable to get DOUBLEBUFFER from framebuffer configuration");
							pixelFormat.DoubleBuffer = attribValue != 0;

							err = Glx.GetFBConfigAttrib(Display, configId, Glx.STEREO, out attribValue);
							if (err != 0)
								throw new InvalidOperationException("unable to get STEREO from framebuffer configuration");
							pixelFormat.StereoBuffer = attribValue != 0;

							if (glxExtensions.FramebufferSRGB_ARB) {
								err = Glx.GetFBConfigAttrib(Display, configId, Glx.FRAMEBUFFER_SRGB_CAPABLE_ARB, out attribValue);
								if (err != 0)
									throw new InvalidOperationException("unable to get FRAMEBUFFER_SRGB_CAPABLE_ARB from framebuffer configuration");
								pixelFormat.SRGBCapable = attribValue != 0;
							} else
								pixelFormat.SRGBCapable = false;

							pFormats.Add(pixelFormat);
						}
					}
				}

				return (pFormats);
			}
		}

		/// <summary>
		/// Set the device pixel format.
		/// </summary>
		/// <param name="pixelFormat">
		/// A <see cref="DevicePixelFormat"/> that specifies the pixel format to set.
		/// </param>
		/// <exception cref="ArgumentNullException">
		/// Exception thrown if <paramref name="pixelFormat"/> is null.
		/// </exception>
		public override void SetPixelFormat(DevicePixelFormat pixelFormat)
		{
			if (pixelFormat == null)
				throw new ArgumentNullException("pixelFormat");

			_FBConfig = pixelFormat.XFbConfig;
			_XVisualInfo = pixelFormat.XVisualInfo;
		}

		#endregion
	}
}