using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using System.IO.Ports;
using System.ComponentModel;
using System.IO;

namespace serious
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = this;

            // kick off the port monitor
            m_portMonitor = new Thread(new ThreadStart(this.PortMonitorJob));
            m_portMonitor.IsBackground = true;
            m_portMonitor.Start();
        }

        public string PortNames { get; private set; }
        public string WindowTitle {
            get
            {
                if (PortNames == null || PortNames.Length == 0)
                    return "serious - waiting for ports";

                return String.Format("serious - reading from {0}, @ 115200/8N1", PortNames);
            }
        }

        public string TTY
        {
            get
            {
                lock (m_tty)
                {
                    if (m_tty.Count > 0)
                        return String.Join("\n", m_tty) + "\n" + m_currLine;
                    return m_currLine;
                }
            }
        }
        List<string> m_tty = new List<string>();
        string m_currLine = "";

        private void OutputLine(string line)
        {
            lock (m_tty)
            {
                if (m_currLine.Length > 0)
                {
                    m_tty.Add(m_currLine);
                    m_currLine = "";
                }
                m_tty.Add(line);
            }

            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs("TTY"));
        }

        private void FinishLine()
        {
            lock (m_tty)
            {
                if (m_currLine.Length > 0)
                {
                    m_tty.Add(m_currLine);
                    m_currLine = "";
                }
            }

            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs("TTY"));
        }

        private void OutputChar(char c)
        {
            lock (m_tty)
            {
                m_currLine += c;
            }

            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs("TTY"));
        }




        #region port listening
        private void ListenToComPorts(string[] ports)
        {
            lock (m_portListeners)
            {
                foreach (string port in ports)
                {
                    if (m_portListeners.ContainsKey(port))
                        continue;

                    Thread listener = new Thread(new ParameterizedThreadStart(this.PortListenerJob));
                    listener.IsBackground = true;
                    m_portListeners[port] = listener;

                    listener.Start(port);
                }
            }
        }

        Dictionary<string, Thread> m_portListeners = new Dictionary<string, Thread>();

        private void PortListenerJob(object portName)
        {
            SerialPort port = new SerialPort();
            port.PortName = (string)portName;
            port.BaudRate = 115200;
            port.DataBits = 8;
            port.Parity = Parity.None;
            port.StopBits = StopBits.One;

            port.ReadTimeout = 100;

            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    port.Open();
                    OutputLine("----------- port " + portName + " opened & listening... -----------\n");
                    break;
                }
                catch (IOException)
                { /* sometimes happens when the port's not ready */ }
                catch (UnauthorizedAccessException)
                {
                    OutputLine("port " + portName + " is probably already in use. giving up.");
                    break;
                }

                Thread.Sleep(200);
            }

            while (port.IsOpen)
            {
                try
                {
                    int b = port.ReadByte();
                    char c = Convert.ToChar(b);

                    if (c == '\n')
                        FinishLine();
                    else if (c != '\r')
                        OutputChar(c);
                }
                catch (TimeoutException)
                { /**/ }
                catch (Exception e) when (e is IOException || e is InvalidOperationException)
                {
                    OutputLine("----------- port closed -----------\n\n....attempting to reopen...");

                    for (int attempt=0; attempt<10; ++attempt)
                    {
                        Thread.Sleep(200);
                        try
                        {
                            port.Open();
                            OutputLine("success!\n\n");
                            break;
                        }
                        catch (IOException)
                        { /**/ }
                    }

                    if (!port.IsOpen)
                        break;
                }
            }

            OutputLine("abandoning port " + portName + "\n\n");

            port.Dispose();

            lock(m_portListeners)
            {
                m_portListeners.Remove((string)portName);
            }
        }
#endregion

#region port monitoring
        private Thread m_portMonitor;

        private bool HavePortsChanged(string[] oldPorts, string[] newPorts)
        {
            if (oldPorts.Length != newPorts.Length)
                return true;

            for (int i = 0; i < oldPorts.Length; ++i)
            {
                if (!oldPorts[i].Equals(newPorts[i]))
                    return true;
            }

            return false;
        }

        private void UpdateAvailablePorts(string[] ports)
        {
            PortNames = String.Join(", ", ports);
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs("PortNames"));
                PropertyChanged(this, new PropertyChangedEventArgs("WindowTitle"));
            }
        }

        private void PortMonitorJob()
        {
            // init
            string[] oldPorts = SerialPort.GetPortNames();
            UpdateAvailablePorts(oldPorts);
            ListenToComPorts(oldPorts);

            while (true)
            {
                Thread.Sleep(300);

                string[] currPorts = SerialPort.GetPortNames();
                if (HavePortsChanged(oldPorts, currPorts))
                {
                    UpdateAvailablePorts(currPorts);
                    ListenToComPorts(currPorts);

                    oldPorts = currPorts;
                }
            }
        }
        #endregion

        private void TtyTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            TtyTextBox.ScrollToEnd();
        }
    }
}
