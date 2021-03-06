using System;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Threading;
using System.Management;
using System.Reflection;
using System.Net;
using System.Diagnostics;
using System.IO;


namespace FSX_Google_Earth_Tracker
{
	static class Program
	{
		public static bool bRestart = false;

		static bool bFirstInstance = false;
		static Mutex mtxSingleInstance = new Mutex(true, "FSXGET Single Instance Mutex", out bFirstInstance);

		/// <summary>
		/// The main entry point for the application.
		/// </summary>
		[STAThread]
		static void Main()
		{
			try
			{
				if (!bFirstInstance)
					return;

				#region Check for Windows and Service Pack Version

				// If OS is before Windows XP, don't run at all
				if (System.Environment.OSVersion.Version.Major < 5)
				{
					MessageBox.Show("This application requires Windows XP or later. Aborting!", AssemblyTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
					return;
				}
				// If OS is Windows XP, Server 2003, etc.
				else if (System.Environment.OSVersion.Version.Major == 5)
				{
					// If it's Windows 2000
					if (System.Environment.OSVersion.Version.Minor == 0)
					{
						MessageBox.Show("This application requires Windows XP or later. Aborting!", AssemblyTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
						return;
					}
					// If it's Windows XP
					else if (System.Environment.OSVersion.Version.Minor == 1)
					{
						SelectQuery query = new SelectQuery("Win32_OperatingSystem");
						ManagementObjectSearcher searcher = new ManagementObjectSearcher(query);
						foreach (ManagementObject mo in searcher.Get())
						{
							// If Service Pack 2 is not installed
							if (int.Parse(mo["ServicePackMajorVersion"].ToString()) < 2)
							{
								MessageBox.Show("On Windows XP this application requires Service Pack 2 (or higher) to be installed. Aborting!", AssemblyTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
								return;
							}
						}
					}
					// If it's anything else (not XP or 2000) check if HttpListener is supported
					else if (!HttpListener.IsSupported)
					{
						MessageBox.Show("Your system cannot run this application. It's designed for Windows XP or later. You may try to install the latest version of the .NET Framework and any Service Packs for your operating system and run the application again after that. If it still fails to start, switch to Windows XP or later please. Aborting!", AssemblyTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
						return;
					}
				}

				#endregion

				#region Check for SimConnect

				// Check if SimConnect is installed and install if necessary
				try
				{
					Assembly assmbl = Assembly.Load("Microsoft.FlightSimulator.SimConnect");
				}
				catch
				{
					DialogResult dlgRes = MessageBox.Show("SimConnect isn't installed on your system but is required. Do you want " + AssemblyTitle + " to automatically install SimConnect now?", AssemblyTitle, MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
					if (dlgRes == DialogResult.Cancel)
					{
						MessageBox.Show(AssemblyTitle + " cannot run without SimConnect and will exit now!", AssemblyTitle, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
						return;
					}

#if DEBUG
					String szLibPath = Application.StartupPath + "\\..\\..\\lib";
#else
					String szLibPath = Application.StartupPath + "\\lib";
#endif

					ProcessStartInfo startInfo = new ProcessStartInfo();

					startInfo.UseShellExecute = true;
					startInfo.WorkingDirectory = szLibPath;
					startInfo.FileName = @"SimConnect.msi";
					startInfo.Arguments = "/passive";

					try
					{
						Process p = Process.Start(startInfo);
						p.WaitForExit();
						if (p.ExitCode != 0)
						{
							if (p.ExitCode == 1602)
							{
								MessageBox.Show("You must accept the license agreement to install SimConnect. " + AssemblyTitle + " cannot run without SimConnect and will exit now!", AssemblyTitle, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
								return;
							}
							else
								throw new Exception("Install SimConnect Exception");
						}
					}
					catch
					{
						MessageBox.Show("There has been a problem installing SimConnect. Please try to install manually. Aborting!", AssemblyTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
						return;
					}

					MessageBox.Show("SimConnect has been installed succesfully! " + AssemblyTitle + " will now restart.", AssemblyTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
					bRestart = true;
					return;
				}

				#endregion

				#region CleanUp

				// Do some clean up
#if DEBUG
				String szUserAppPath = Application.StartupPath + "\\..\\..\\AppData\\" + AssemblyVersion;
#else
				String szUserAppPath = Application.UserAppDataPath;
#endif
				String szVersion = AssemblyVersion;
				if (szUserAppPath.Substring(szUserAppPath.Length - szVersion.Length) == szVersion)
				{
					szUserAppPath = szUserAppPath + "\\..";
					String[] dirs = Directory.GetDirectories(szUserAppPath);
					foreach (String dir in dirs)
					{
						if (dir.Substring(dir.Length - szVersion.Length) != szVersion)
						{
							try
							{
								Directory.Delete(dir, true);
							}
							catch { }
						}

					}

					String[] files = Directory.GetFiles(szUserAppPath);
					foreach (String file in files)
					{
						try
						{
							File.Delete(file);
						}
						catch { }
					}
				}

				#endregion

				#region StartUp

				Application.EnableVisualStyles();
				Application.SetCompatibleTextRenderingDefault(false);

				//Form frmMain = new Form1();
				//frmMain.Visible = false;

				Application.Run(new Form1());

				#endregion
			}
			finally
			{
				mtxSingleInstance.Close();
				if (bRestart)
					Application.Restart();
			}
		}


		public static string AssemblyTitle
		{
			get
			{
				// Get all Title attributes on this assembly
				object[] attributes = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyTitleAttribute), false);
				// If there is at least one Title attribute
				if (attributes.Length > 0)
				{
					// Select the first one
					AssemblyTitleAttribute titleAttribute = (AssemblyTitleAttribute)attributes[0];
					// If it is not an empty string, return it
					if (titleAttribute.Title != "")
						return titleAttribute.Title;
				}
				// If there was no Title attribute, or if the Title attribute was the empty string, return the .exe name
				return System.IO.Path.GetFileNameWithoutExtension(Assembly.GetExecutingAssembly().CodeBase);
			}
		}

		public static string AssemblyVersion
		{
			get
			{
				return Assembly.GetExecutingAssembly().GetName().Version.ToString();
			}
		}

	}
}