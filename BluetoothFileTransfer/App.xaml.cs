using System;
using System.Windows;

namespace BluetoothFileTransfer
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// 二重起動防止
        /// </summary>
        private static System.Threading.Mutex mutex;

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // 引数が「送信先アドレス」「送信ファイルパス」の構成であるならば、二重起動可能とする
            if (Environment.GetCommandLineArgs().Length == 3) return;

            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            mutex = new System.Threading.Mutex(false, System.IO.Path.GetFileNameWithoutExtension(assembly.GetName().CodeBase));

            if (!mutex.WaitOne(0, false))
            {
                mutex.Close();
                mutex = null;

                this.Shutdown();
            }
        }

        private void Application_Exit(object sender, ExitEventArgs e)
        {
            if (mutex != null)
            {
                mutex.ReleaseMutex();
                mutex.Close();
            }
        }
    }
}
