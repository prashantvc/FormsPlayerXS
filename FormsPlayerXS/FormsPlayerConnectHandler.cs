
using System;
using System.Linq;
using MonoDevelop.Components.Commands;
using MonoDevelop.Ide;
using System.Net.NetworkInformation;
using Microsoft.AspNet.SignalR.Client;
using System.Xml;
using System.Xml.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.IO;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using FormsPlayerXS;

namespace FormsPlayerXS
{
	public class FormsPlayerConnectHandler : CommandHandler
	{
		public FormsPlayerConnectHandler ()
		{
			
			var mac = NetworkInterface.GetAllNetworkInterfaces ()
				.Where (nic => nic.NetworkInterfaceType == NetworkInterfaceType.Loopback).ToList();
			
			SessionId = NaiveBijective.Encode (NaiveBijective.Decode (mac.Select (c => c.Id.ToString ()).First()));
			Console.WriteLine ("session id {0}", SessionId);
			IdeApp.Workbench.ActiveDocumentChanged += IdeApp_Workbench_ActiveDocumentChanged;
		}

		void IdeApp_Workbench_ActiveDocumentChanged (object sender, EventArgs e)
		{	
			if (IdeApp.Workbench.ActiveDocument == null) {
				return;

			}
			IdeApp.Workbench.ActiveDocument.Saved += IdeApp_ActiveDocument_Saved;
		}

		void IdeApp_ActiveDocument_Saved (object sender, EventArgs e)
		{
			Publish (IdeApp.Workbench.ActiveDocument.FileName.FullPath);
		}

		protected override void Run ()
		{
			base.Run ();
			Console.WriteLine ("Start Player");

			if (IsConnected)
				Disconnect ();
			else
				Connect ();
		}

		protected override void Update (CommandInfo info)
		{
			//info.Enabled = IsSupportedFile;
			info.Text = IsConnected ? "Disconnect" : "Connect";

			Console.WriteLine (info.Command.Id);
		}

		bool IsSupportedFile {
			get {
				if (IdeApp.Workbench.ActiveDocument == null) {
					return false;
				}

				string fileExtension = IdeApp.Workbench.ActiveDocument.FileName.Extension.ToLowerInvariant ();
				return FileExtensions.Contains (fileExtension);
			}
		}

		void Disconnect ()
		{
			connection.Stop ();
			connection.Dispose ();
			connection = null;
			proxy = null;
			IsConnected = false;

			IdeApp.Workbench.StatusBar.ShowMessage ("Disconnected");
		}

		void Connect ()
		{
			IsConnected = false;
			connection = new HubConnection ("http://formsplayer.azurewebsites.net/");
			proxy = connection.CreateHubProxy ("FormsPlayer");

			try {
				connection.Start ().Wait (3000);
				IsConnected = true;
				IdeApp.Workbench.StatusBar.ShowMessage (string.Format("Successfully connected to FormsPlayer. Session ID: {0}", SessionId));
			} catch (Exception e) {
				IdeApp.Workbench.StatusBar.ShowMessage (string.Format("Error connecting to FormsPlayer: {0}", e.Message));
				connection.Dispose ();
				Console.WriteLine (e);
			}
		}

		void Publish (string fileName)
		{
			if (!IsConnected) {
				Console.WriteLine ("!FormsPlayer is not connected yet.");
				return;
			}

			if (Path.GetExtension (fileName) == ".xaml") {
				PublishXaml (fileName);
			} else if (Path.GetExtension (fileName) == ".json") {
				PublishJson (fileName);
			}
		}


		void PublishXaml (string fileName)
		{
			// Make sure we can read it as XML, just to safeguard the client.
			try {
				using (var reader = XmlReader.Create (fileName)) {
					var xdoc = XDocument.Load (reader);
					// Strip the x:Class attribute since it doesn't make 
					// sense for the deserialization and might break stuff.
					var xclass = xdoc.Root.Attribute ("{http://schemas.microsoft.com/winfx/2009/xaml}Class");
					if (xclass != null)
						xclass.Remove ();
					xclass = xdoc.Root.Attribute ("{http://schemas.microsoft.com/winfx/2006/xaml}Class");
					if (xclass != null)
						xclass.Remove ();

					var xml = xdoc.ToString (SaveOptions.DisableFormatting);
					//tracer.Info ("!Publishing XAML payload");

					proxy.Invoke ("Xaml", SessionId, xml)
						.ContinueWith (Console.WriteLine,
						CancellationToken.None,
						TaskContinuationOptions.OnlyOnFaulted,
						TaskScheduler.Default);
				}
			} catch (XmlException) {
				return;
			}
		}

		void PublishJson (string fileName)
		{
			// Make sure we can read it as XML, just to safeguard the client.
			try {

				var json = JObject.Parse (File.ReadAllText (fileName));
				Console.WriteLine ("!Publishing JSON payload");

				proxy.Invoke ("Json", SessionId, json.ToString (Newtonsoft.Json.Formatting.None))
					.ContinueWith (Console.WriteLine,
					CancellationToken.None,
					TaskContinuationOptions.OnlyOnFaulted,
					TaskScheduler.Default);

			} catch (JsonException) {
				return;
			}
		}

		public bool IsConnected {
			get;
			set;
		}

		string SessionId;

		HubConnection connection;
		IHubProxy proxy;

		readonly string[] FileExtensions = { ".json", ".xaml" };
	}

	public class PublishHandler : CommandHandler
	{
		
	}
}

