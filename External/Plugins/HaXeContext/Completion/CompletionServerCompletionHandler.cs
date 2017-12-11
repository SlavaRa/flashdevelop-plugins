using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using PluginCore;
using PluginCore.Managers;
using ProjectManager.Projects.Haxe;

namespace HaXeContext
{
    public delegate void FallbackNeededHandler(bool notSupported);

    public class CompletionServerCompletionHandler : IHaxeCompletionHandler
    {
        public event FallbackNeededHandler FallbackNeeded;

        private readonly Process haxeProcess;
        private readonly int port;
        private bool listening;
        private bool failure;

        public CompletionServerCompletionHandler(ProcessStartInfo haxeProcessStartInfo, int port)
        {
            this.haxeProcess = new Process {StartInfo = haxeProcessStartInfo, EnableRaisingEvents = true};
            this.port = port;
            Environment.SetEnvironmentVariable("HAXE_SERVER_PORT", "" + port);
        }

        public bool IsRunning()
        {
            try { return !haxeProcess.HasExited; } 
            catch { return false; }
        }

        /// <summary>
        /// Allows an object to try to free resources and perform other cleanup operations before it is reclaimed by garbage collection.
        /// </summary>
        ~CompletionServerCompletionHandler()
        {
            Stop();
        }

        public string GetCompletion(string[] args)
        {
            return GetCompletion(args, null);
        }
        public string GetCompletion(string[] args, string fileContent)
        {
            if (args == null || haxeProcess == null)
                return string.Empty;
            if (!IsRunning()) StartServer();
            try
            {
                var client = new TcpClient("127.0.0.1", port);
                var writer = new StreamWriter(client.GetStream());
                writer.WriteLine("--cwd " + ((HaxeProject) PluginBase.CurrentProject).Directory);
                foreach (var arg in args)
                    writer.WriteLine(arg);
                if (fileContent != null)
                {
                    writer.Write("\x01");
                    writer.Write(fileContent);
                }
                writer.Write("\0");
                writer.Flush();
                var reader = new StreamReader(client.GetStream());
                var lines = reader.ReadToEnd();
                client.Close();
                return lines;
            }
            catch(Exception ex)
            {
                TraceManager.AddAsync(ex.Message);
                if (!failure && FallbackNeeded != null)
                    FallbackNeeded(false);
                failure = true;
                return string.Empty;
            }
        }

        public void StartServer()
        {
            if (haxeProcess == null || IsRunning()) return;
            haxeProcess.Start();
            if (listening) return;
            listening = true;
            haxeProcess.BeginOutputReadLine();
            haxeProcess.BeginErrorReadLine();
            haxeProcess.OutputDataReceived += haxeProcess_OutputDataReceived;
            haxeProcess.ErrorDataReceived += haxeProcess_ErrorDataReceived;
        }

        static void haxeProcess_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            TraceManager.AddAsync(e.Data, 2);
        }

        void haxeProcess_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;
            if (!Regex.IsMatch(e.Data, "Error.*--wait")) return;
            if (!failure && FallbackNeeded != null) 
                FallbackNeeded(true);
            failure = true;
        }

        public void Stop()
        {
            if (IsRunning())
                haxeProcess.Kill();
        }
    }
}
