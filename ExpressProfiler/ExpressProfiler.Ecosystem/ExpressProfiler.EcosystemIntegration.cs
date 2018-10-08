using System;
using System.Diagnostics;
using System.IO;
using RedGate.SIPFrameworkShared;

namespace ExpressProfiler.Ecosystem
{
    class ExpressProfiler : ISsmsAddin4
    {
        public string Version => "2.0";
        public string Description => "Ecosystem integration for ExpressProfiler";
        public string Name => "ExpressProfiler";
        public string Author => "ExpressProfiler";
        public string Url => "https://expressprofiler.codeplex.com/";

        internal static ISsmsFunctionalityProvider6 m_Provider;
        public void OnLoad(ISsmsExtendedFunctionalityProvider provider)
        {
            m_Provider = (ISsmsFunctionalityProvider6)provider;
            m_Provider.AddToolbarItem(new ExecuteExpressProfiler());
            var command = new ExecuteExpressProfiler();
            m_Provider.AddToolsMenuItem(command);
        }


        public void OnNodeChanged(ObjectExplorerNodeDescriptorBase node)
        {
        }

        public void OnShutdown(){}
    }

    public class ExecuteExpressProfiler :  ISharedCommand
    {
        public void Execute()
        {
            string param ="";
            if(ExpressProfiler.m_Provider.ObjectExplorerWatcher.TryGetSelectedConnection(out var con))
            {
                string server = con.Server;
                string user = con.UserName;
                string password = con.Password;
                bool trusted = con.IsUsingIntegratedSecurity;
                param = trusted ? $"-server \"{server}\"" : $"-server \"{server}\" -user \"{user}\" -password \"{password}\"";
            }
            string root = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string profiler = Path.Combine(root, "ExpressProfiler\\ExpressProfiler.exe");
            Process.Start(profiler, param);
        }

        public string Name => "ExpressProfilerExecute";
        public string Caption => "ExpressProfiler";
        public string Tooltip => "Execute ExpressProfiler";
        public ICommandImage Icon { get; } = new CommandImageForEmbeddedResources(typeof(ExecuteExpressProfiler).Assembly, "ExpressProfiler.Ecosystem.Resources.Icon.png");

        public string[] DefaultBindings => new string[] { };
        public bool Visible => true;
        public bool Enabled => true;
    }
}
