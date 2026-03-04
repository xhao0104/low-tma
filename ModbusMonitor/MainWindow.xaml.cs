using System.Windows;
using ModbusMonitor.ViewModels;

namespace ModbusMonitor
{
    /// <summary>
    /// 主窗口后台代码
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 窗口关闭时释放 Modbus 连接资源
        /// </summary>
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.Cleanup();
            }
        }
    }
}