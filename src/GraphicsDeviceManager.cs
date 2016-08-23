#region License
/* FNA - XNA4 Reimplementation for Desktop Platforms
 * Copyright 2009-2016 Ethan Lee and the MonoGame Team
 *
 * Released under the Microsoft Public License.
 * See LICENSE for details.
 */
#endregion

#region Using Statements
using System;
using Microsoft.Xna.Framework.Graphics;

#endregion

namespace Microsoft.Xna.Framework
{
	public class GraphicsDeviceManager : IGraphicsDeviceService, IDisposable, IGraphicsDeviceManager
	{
		#region Public Events
		public event EventHandler<EventArgs> Disposed;
		#endregion

		#region Public Properties
		public GraphicsProfile GraphicsProfile { get; set; }
		public GraphicsDevice GraphicsDevice
		{
			get
			{
				/* FIXME: If you call this before Game.Initialize(), you can
				 * actually get a device in XNA4. But, even in XNA4, Game.Run
				 * is what calls CreateDevice! So is this check accurate?
				 * -flibit
				 */
				if (graphicsDevice == null)
					((IGraphicsDeviceManager) this).CreateDevice();
				return graphicsDevice;
			}
		}
		public bool IsFullScreen { get; set; }
		public bool PreferMultiSampling { get; set; }
		public SurfaceFormat PreferredBackBufferFormat { get; set; }
		public int PreferredBackBufferHeight { get; set; }
		public int PreferredBackBufferWidth { get; set; }
		public DepthFormat PreferredDepthStencilFormat { get; set; }
		public bool SynchronizeWithVerticalRetrace { get; set; }
		public DisplayOrientation SupportedOrientations
		{
			get { return supportedOrientations; }
			set
			{
				supportedOrientations = value;
				if (game.Window != null)
					game.Window.SetSupportedOrientations(supportedOrientations);
			}
		}
		#endregion

		#region Private Variables
		private Game game;
		private GraphicsDevice graphicsDevice;
		private DisplayOrientation supportedOrientations;
		private bool drawBegun;
		private bool disposed;
		#endregion

		#region Public Static Fields
		public static readonly int DefaultBackBufferWidth = 800;
		public static readonly int DefaultBackBufferHeight = 480;
		#endregion

		#region IGraphicsDeviceService Events
		public event EventHandler<EventArgs> DeviceCreated;
		public event EventHandler<EventArgs> DeviceDisposing;
		public event EventHandler<EventArgs> DeviceReset;
		public event EventHandler<EventArgs> DeviceResetting;
		public event EventHandler<PreparingDeviceSettingsEventArgs> PreparingDeviceSettings;
		#endregion

		#region Public Constructor
		public GraphicsDeviceManager(Game game)
		{
			if (game == null)
				throw new ArgumentNullException("The game cannot be null!");

			this.game = game;

			supportedOrientations = DisplayOrientation.Default;

			PreferredBackBufferHeight = DefaultBackBufferHeight;
			PreferredBackBufferWidth = DefaultBackBufferWidth;

			PreferredBackBufferFormat = SurfaceFormat.Color;
			PreferredDepthStencilFormat = DepthFormat.Depth24;

			SynchronizeWithVerticalRetrace = true;

			PreferMultiSampling = false;

			if (game.Services.GetService(typeof (IGraphicsDeviceManager)) != null)
				throw new ArgumentException("Graphics Device Manager Already Present");

			game.Services.AddService(typeof (IGraphicsDeviceManager), this);
			game.Services.AddService(typeof (IGraphicsDeviceService), this);
		}
		#endregion

		#region Deconstructor
		~GraphicsDeviceManager()
		{
			Dispose(false);
		}
		#endregion

		#region Dispose Methods
		protected virtual void Dispose(bool disposing)
		{
			if (!disposed)
			{
				if (disposing)
				{
					if (graphicsDevice != null)
					{
						OnDeviceDisposing(this, EventArgs.Empty);
						graphicsDevice.Dispose();
						graphicsDevice = null;
					}
				}
				if (Disposed != null)
					Disposed(this, EventArgs.Empty);
				disposed = true;
			}
		}
		void IDisposable.Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}
		#endregion

		#region Public Methods
		public void ApplyChanges()
		{
			// Calling ApplyChanges() before CreateDevice() should have no effect.
			if (graphicsDevice == null)
				return;

			// Recreate device information before resetting
			GraphicsDeviceInformation gdi = new GraphicsDeviceInformation();
			gdi.Adapter = GraphicsDevice.Adapter;
			gdi.GraphicsProfile = GraphicsDevice.GraphicsProfile;
			gdi.PresentationParameters = GraphicsDevice.PresentationParameters.Clone();

			/* Apply the GraphicsDevice changes to the new Parameters.
			 * Note that PreparingDeviceSettings can override any of these!
			 * -flibit
			 */
			gdi.PresentationParameters.BackBufferFormat =
				PreferredBackBufferFormat;
			gdi.PresentationParameters.BackBufferWidth =
				PreferredBackBufferWidth;
			gdi.PresentationParameters.BackBufferHeight =
				PreferredBackBufferHeight;
			gdi.PresentationParameters.DepthStencilFormat =
				PreferredDepthStencilFormat;
			gdi.PresentationParameters.IsFullScreen =
				IsFullScreen;
			if (!PreferMultiSampling)
				gdi.PresentationParameters.MultiSampleCount = 0;
			else if (gdi.PresentationParameters.MultiSampleCount == 0)
			{
				/* XNA4 seems to have an upper limit of 8, but I'm willing to
				 * limit this only in GraphicsDeviceManager's default setting.
				 * If you want even higher values, Reset() with a custom value.
				 * -flibit
				 */
				gdi.PresentationParameters.MultiSampleCount = Math.Min(
					GraphicsDevice.GLDevice.MaxMultiSampleCount,
					8
					);
			}

			// Give the user a chance to override the above settings.
			OnPreparingDeviceSettings(
				this,
				new PreparingDeviceSettingsEventArgs(gdi)
				);

			// We're about to reset a device, notify the application.
			OnDeviceResetting(this, EventArgs.Empty);

			// Make the Platform device changes.
			game.Window.BeginScreenDeviceChange(
				gdi.PresentationParameters.IsFullScreen
				);
			game.Window.EndScreenDeviceChange(
				gdi.Adapter.Description, // FIXME: Should be Name! -flibit
				gdi.PresentationParameters.BackBufferWidth,
				gdi.PresentationParameters.BackBufferHeight
				);

			// Apply the PresentInterval.
			FNAPlatform.SetPresentationInterval(
				SynchronizeWithVerticalRetrace
					? gdi.PresentationParameters.PresentationInterval
					: PresentInterval.Immediate
				);

			// Reset!
			GraphicsDevice.Reset(gdi.PresentationParameters, gdi.Adapter);

			// We just reset a device, notify the application.
			OnDeviceReset(this, EventArgs.Empty);
		}
		public void ToggleFullScreen()
		{
			IsFullScreen = !IsFullScreen;
			ApplyChanges();
		}
		#endregion

		#region Internal Methods
		internal void INTERNAL_ResizeGraphicsDevice(int width, int height)
		{
			PresentationParameters pp = GraphicsDevice.PresentationParameters;

			// Only reset if there's an actual change in size
			if (pp.BackBufferWidth != width || pp.BackBufferHeight != height)
			{
				// We're about to reset a device, notify the application.
				OnDeviceResetting(this, EventArgs.Empty);

				pp.BackBufferWidth = width;
				pp.BackBufferHeight = height;

				GraphicsDevice.Reset();

				// We just reset a device, notify the application.
				OnDeviceReset(this, EventArgs.Empty);
			}
		}
		#endregion

		#region Protected Methods
		protected virtual void OnDeviceCreated(object sender, EventArgs args)
		{
			if (DeviceCreated != null)
				DeviceCreated(sender, args);
		}
		protected virtual void OnDeviceDisposing(object sender, EventArgs args)
		{
			if (DeviceDisposing != null)
				DeviceDisposing(sender, args);
		}
		protected virtual void OnDeviceReset(object sender, EventArgs args)
		{
			if (DeviceReset != null)
				DeviceReset(sender, args);
		}
		protected virtual void OnDeviceResetting(object sender, EventArgs args)
		{
			if (DeviceResetting != null)
				DeviceResetting(sender, args);
		}
		protected virtual void OnPreparingDeviceSettings(
			object sender,
			PreparingDeviceSettingsEventArgs args
			)
		{
			if (PreparingDeviceSettings != null)
				PreparingDeviceSettings(sender, args);
		}
		#endregion

		#region IGraphicsDeviceManager Methods
		void IGraphicsDeviceManager.CreateDevice()
		{
			// Set the default device information
			GraphicsDeviceInformation gdi = new GraphicsDeviceInformation();
			gdi.Adapter = GraphicsAdapter.DefaultAdapter;
			gdi.GraphicsProfile = GraphicsProfile;
			gdi.PresentationParameters = new PresentationParameters();
			gdi.PresentationParameters.DeviceWindowHandle = game.Window.Handle;
			gdi.PresentationParameters.DepthStencilFormat = DepthFormat.Depth24;
			gdi.PresentationParameters.IsFullScreen = false;

			// Give the user a chance to change the initial settings
			OnPreparingDeviceSettings(
				this,
				new PreparingDeviceSettingsEventArgs(gdi)
				);

			// Apply these settings to this GraphicsDeviceManager
			GraphicsProfile = gdi.GraphicsProfile;
			PreferredBackBufferFormat = gdi.PresentationParameters.BackBufferFormat;
			PreferredDepthStencilFormat = gdi.PresentationParameters.DepthStencilFormat;

			// Create the GraphicsDevice, apply the initial settings.
			graphicsDevice = new GraphicsDevice(
				gdi.Adapter,
				gdi.GraphicsProfile,
				gdi.PresentationParameters
				);
			ApplyChanges();

			// Call the DeviceCreated Event
			OnDeviceCreated(this, EventArgs.Empty);
		}
		bool IGraphicsDeviceManager.BeginDraw()
		{
			if (graphicsDevice == null)
				return false;

			drawBegun = true;
			return true;
		}
		void IGraphicsDeviceManager.EndDraw()
		{
			if (graphicsDevice != null && drawBegun)
			{
				drawBegun = false;
				graphicsDevice.Present();
			}
		}
		#endregion
	}
}