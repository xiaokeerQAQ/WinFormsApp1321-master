using BLL.Hardware.ScanGang;
using System.Net;
using System.Text;
using System.Windows.Forms;

namespace WinFormsApp1321
{
    public partial class Form1 : Form
    {
        private bool isOn = false; // 按钮状态
        private bool isOn1 = false;
        private int currentCycle = 0; // 当前循环次数
        private int totalCycles = 0; // 总循环次数
        private CancellationTokenSource cancellationTokenSource; // 控制循环停止
        private bool isCalibrationMode = false;

        private System.Windows.Forms.Timer detectionTimer;

        private TCPServer _tcpServer;
        private PLCClient _plcClient;
        private ScanGangBasic _scanGangBasic;

        public Form1()
        {
            InitializeComponent();
            textBox1.ReadOnly = true;
            textBox1.Text = "";
            textBox2.Enabled = false;
            button4.Enabled = false;
            detectionTimer = new System.Windows.Forms.Timer();
            detectionTimer.Interval = 5000;
            detectionTimer.Tick += DetectionTimer_Tick;
            // 初始化 PLC 和扫码枪
            _plcClient = new PLCClient("127.0.0.1", 6000);
            _scanGangBasic = new ScanGangBasic();

            // 初始化 TCPServer，并传入 PLC 和扫码枪实例
            _tcpServer = new TCPServer(_plcClient, _scanGangBasic);
        }

        private void UpdateLabel8(string text)
        {
            if (label8.InvokeRequired)
            {
                label8.Invoke(new Action(() =>
                {
                    label8.Text = text;
                    label8.Visible = !string.IsNullOrWhiteSpace(text); // 为空时隐藏
                }));
            }
            else
            {
                label8.Text = text;
                label8.Visible = !string.IsNullOrWhiteSpace(text); // 为空时隐藏
            }
        }
        private async void button5_Click(object sender, EventArgs e)
        {


            bool writeSuccess = await _plcClient.WriteDRegisterAsync(2130, 3);

            if (!writeSuccess)
            {
                // MessageBox.Show("无法写入 PLC（D2130 = 3），启动自校准失败！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                UpdateLabel8("报警信息：启动自校准失败！");
                return;
            }

            TCPServer.Mode = 1;
            SelectionForm selectionForm = new SelectionForm();
            selectionForm.ShowDialog();

            if (selectionForm.DialogResult != DialogResult.OK)
            {
                // MessageBox.Show("自校准模式未开启。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                //UpdateLabel8("自校准模式未开启");
                return;
            }

            DialogResult result = MessageBox.Show(
                $"系统文件：C:\\system\\system.ini\n" +
                $"标样文件：{selectionForm.StandardFilePath}\n" +
                $"标定循环次数：{selectionForm.CalibrationCount}\n" +
                $"时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n\n" +
                "放入样棒后点击确认？",
                "放入样棒",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question
            );

            if (result == DialogResult.Cancel)
            {
                await _plcClient.WriteDRegisterAsync(2135, 1);
                // MessageBox.Show("自校准模式未开启。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);

                return;
            }

            bool sampleInserted = false;
            while (!sampleInserted)
            {
                int[] response = await _plcClient.ReadDRegisterAsync(2132, 1);
                if (response == null)
                {
                    // MessageBox.Show("无法读取 D2132，检查 PLC 连接。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    UpdateLabel8("报警信息：无法读取D2132，检查连接");
                    return;
                }

                if (response[0] == 1) // 扫码区有样棒
                {
                    confirmWriteSuccess = await _plcClient.WriteDRegisterAsync(2132, 3);
                    if (!confirmWriteSuccess)
                    {
                        // MessageBox.Show("无法通知 PLC 开始循环（D2132 = 3 失败）", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        UpdateLabel8("报警信息：无法通知PLC开启循环");

                        return;
                    }
                    UpdateLabel8("");
                    string selectedStandardFile = selectionForm.StandardFilePath;
                    totalCycles = selectionForm.CalibrationCount;
                    currentCycle = 0;

                    isOn1 = true;
                    button5.Enabled = false;
                    button6.Enabled = true;
                    label1.Text = "当前状态：自校准模式";
                    label6.Text = "自校准模式运行中";
                    cancellationTokenSource = new CancellationTokenSource();
                    Task.Run(() => RunCalibrationLoop(selectedStandardFile, cancellationTokenSource.Token));

                    sampleInserted = true;
                }
                else
                {
                    result = MessageBox.Show("扫码区没有样棒或待检棒，请放入样棒后点击确认。", "等待样棒", MessageBoxButtons.OKCancel);
                    if (result == DialogResult.Cancel)
                    {
                        //MessageBox.Show("自校准模式未开启。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }
            }
        }


        private async void button6_Click_1(object sender, EventArgs e)
        {

            bool writeSuccess = await _plcClient.WriteDRegisterAsync(2133, 1);

            if (!writeSuccess)
            {
                // MessageBox.Show("无法停止自校准模式，写入 D2133 失败！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                UpdateLabel8("报警信息：无法停止自校准模式");
                return;
            }
            UpdateLabel8("");
            StopCalibration(false);

            isOn1 = false;
            button5.Enabled = true;
            button6.Enabled = true;
            label1.Text = "当前状态：待机状态";
        }



        private async void button1_Click(object sender, EventArgs e)
        {
        }
        private async void button2_Click(object sender, EventArgs e)
        {
            /* if (!isOn) // 进入检测模式
             {
                 bool writeSuccess = await _plcClient.WriteDRegisterAsync(2130, 1);

                 if (writeSuccess)
                 {
                     isOn = true;
                     button2.Text = "退出检测模式";
                     label1.Text = "当前状态：检测模式";
                     button1.Enabled = false;

                     textBox2.Enabled = true;
                     button4.Enabled = true;

                     detectionTimer.Start();
                 }
                 else
                 {
                     MessageBox.Show("无法进入检测模式，PLC通信失败！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                 }
             }
             else // 退出检测模式
             {
                 bool writeSuccess = await _plcClient.WriteDRegisterAsync(2140, 1);

                 if (writeSuccess)
                 {
                     isOn = false;
                     detectionTimer.Stop();
                     button2.Text = "进入检测模式";
                     label1.Text = "当前状态：待机状态";
                     button1.Enabled = true;
                 }
                 else
                 {
                     MessageBox.Show("无法退出检测模式，PLC通信失败！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                 }
             }*/
        }


        private async void DetectionTimer_Tick(object sender, EventArgs e)
        {
            while (isOn)
            {

                int[] response = await _plcClient.ReadDRegisterAsync(2132, 1);

                if (response != null && response.Length > 0 && response[0] == 1)
                {
                    // 如果 D2132 为 1，开始扫码
                    await ReadAndSendBarcode();
                }


                int[] stopSignal = await _plcClient.ReadDRegisterAsync(2140, 1);
                if (stopSignal != null && stopSignal.Length > 0 && stopSignal[0] == 1)
                {
                    Console.WriteLine("检测模式手动停止...");
                    await StopDetectionAsync();
                    break; // 退出循环
                }

                await Task.Delay(1000);
            }
        }
        private async Task ReadAndSendBarcode()
        {
            string result;
            string errStr;
            bool success = _scanGangBasic.ScanOnce(out result, out errStr);

            if (success && result != "未扫描到条码")
            {
                // 显示条码
                textBox2.Text = result;


                bool writeSuccess = await _plcClient.WriteDRegisterAsync(2132, 3);

                if (writeSuccess)
                {
                    Console.WriteLine($"条码 {result} 扫描成功！");

                    //  ProcessFinalTestData 进行试件判断
                    bool isQualified = _tcpServer.ProcessFinalTestData();


                    int statusCode = isQualified ? 1 : 2;
                    await _plcClient.WriteDRegisterAsync(2138, statusCode);

                    Console.WriteLine($"试件检测结果：{(isQualified ? "合格" : "不合格")}，已发送至 D2138。");
                }
                else
                {
                    Console.WriteLine("无法向 PLC 发送扫码成功信息！");
                }
            }
            else
            {
                Console.WriteLine($"扫描失败：{errStr}");
            }
        }



        private async Task StopDetectionAsync()
        {

            // 关闭检测模式界面（Form2）
            /* foreach (Form form in Application.OpenForms)
             {
                 if (form is Form2)
                 {
                     form.Close();
                     break;
                 }
             }*/

            // 启用自校准按钮
            button5.Enabled = true;
            button6.Enabled = true;
            // 状态更新
            isOn = false;
            label9.Text = "检测模式";
            label1.Text = "当前状态：待机状态";
        }

        private void button3_Click(object sender, EventArgs e)
        {
            /* // 复位状态
             //isCalibrationMode = false;
             isOn = false;


             label1.Text = "当前状态：待机状态";


             button1.Enabled = true;
             button2.Enabled = true;
             button1.Text = "自校准模式关闭";
             button2.Text = "检测模式关闭";

             MessageBox.Show("系统已恢复为待机状态！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
          //   StopCalibration(true);*/
        }

        /*  private async Task RunCalibrationLoop(string selectedStandardFile, CancellationToken token)
          {
              DateTime lastCycleEndTime = DateTime.Now;
              string iniPath = "C:\\system\\system.ini";

              while (currentCycle < totalCycles)
              {
                  if (token.IsCancellationRequested)
                  {
                      MessageBox.Show("自校准任务已停止！", "停止", MessageBoxButtons.OK, MessageBoxIcon.Information);
                      StopCalibration();
                      return;
                  }

                  currentCycle++;
                  UpdateCycleLabel();

                  bool isMatched = CompareIniFiles("D:\\标样\\样管1.ini", selectedStandardFile);

                  if (!isMatched)
                  {
                      MessageBox.Show("出现缺陷数据异常！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                      StopCalibration();
                      return;
                  }

                  lastCycleEndTime = DateTime.Now;  // 记录本次循环的结束时间

                  if (currentCycle >= totalCycles)
                  {
                      MessageBox.Show("检测完成！所有循环已执行。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);

                      DateTime validUntil = lastCycleEndTime.AddHours(2); // 计算有效期限
                      WriteDeadlineToIni(iniPath, validUntil);  // 写入 system.ini
                      UpdateValidUntilLabel(validUntil); // 更新 UI

                      StopCalibration();
                  }

                  await Task.Delay(10000, token);
              }
          }*/
        public bool confirmWriteSuccess;
        private async Task RunCalibrationLoop(string selectedStandardFile, CancellationToken token)
        {
            DateTime lastCycleEndTime = DateTime.Now;
            string iniPath = "C:\\system\\system.ini";

            while (currentCycle < totalCycles && !token.IsCancellationRequested)
            {
                NotifyCycleStart();

                if (!await WaitForTestCompletion())
                {
                    StopCalibration(true);
                    return;
                }

                // 使用超时机制来判断测试结果是否合格
                bool isTestPassed = await CheckTestResultWithTimeout(TimeSpan.FromSeconds(20));
                int registerValue = isTestPassed ? 2 : 1;

                // 如果不合格，直接停止校准
                if (!isTestPassed)
                {
                    bool writeFail = await _plcClient.WriteDRegisterAsync(2142, registerValue);
                    if (!writeFail)
                    {
                        MessageBox.Show($"写入 D2142 失败，校准终止！", "错误", MessageBoxButtons.OK);
                    }
                    MessageBox.Show("校准不合格，停止校准。", "不合格", MessageBoxButtons.OK);

                    StopCalibration(true);
                    return;
                }

                // 如果不是第一次循环，合格后向 D2142 写入 2 并检查 D2132
                if (currentCycle > 0)
                {
                    bool writeSuccess = await _plcClient.WriteDRegisterAsync(2142, registerValue);
                    if (!writeSuccess)
                    {
                        // MessageBox.Show($"写入 D2142 失败，校准终止！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        UpdateLabel8("报警信息：写入PLC D2142失败，循环停止");
                        StopCalibration(true);
                        return;
                    }
                    else
                    {

                        UpdateLabel8("");
                    }

                    // 检查 D2132 是否为 1 来决定是否进行下一次循环
                    int[] response = await _plcClient.ReadDRegisterAsync(2132, 1);
                    if (response == null || response.Length == 0 || response[0] != 1)
                    {

                        // MessageBox.Show("D2132 不为 1，停止校准。", "停止", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        UpdateLabel7("报警信息：无法进行循环");
                        StopCalibration(true);
                        return;
                    }
                    else
                    {
                        UpdateLabel7("");
                    }
                }

                UpdateCycleCount();

                if (currentCycle >= totalCycles)
                {
                    CompleteCalibration(lastCycleEndTime, iniPath);
                    return;
                }
            }
        }

        private async Task<bool> CheckTestResultWithTimeout(TimeSpan timeout)
        {
            // Console.WriteLine("开始检查测试结果...");
            var task = Task.Run(() => _tcpServer.ProcessFinalTestData());
            if (await Task.WhenAny(task, Task.Delay(timeout)) == task)
            {
                UpdateLabel7("");
                // Console.WriteLine("测试结果已返回。");
                return task.Result;
            }
            else
            {
                //Console.WriteLine("超时未接收到测试结果。");
                // MessageBox.Show("未能在20秒内接收到样棒是否合格的结果。", "超时错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                UpdateLabel7("提示信息：未能在20秒内接收到样棒是否合格的结果");
                return false;
            }
        }

        private void NotifyCycleStart()
        {
            //MessageBox.Show($"第 {currentCycle + 1} 次校准开始！", "开始校准", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private async Task<bool> WaitForTestCompletion()
        {
            while (!_tcpServer.IsTestCompleted())
            {
                await Task.Delay(1000);
            }
            return true;
        }

        private void UpdateCycleCount()
        {
            currentCycle++;
            UpdateCycleLabel();
        }

        private void CompleteCalibration(DateTime lastCycleEndTime, string iniPath)
        {
            MessageBox.Show("所有循环已执行。", "完成", MessageBoxButtons.OK);

            DateTime validUntil = lastCycleEndTime.AddHours(8);
            WriteDeadlineToIni(iniPath, validUntil);
            UpdateValidUntilLabel(validUntil);

            this.Invoke(new Action(() => button7.Enabled = true));

            StopCalibration(false);
        }


        /*   private async Task RunCalibrationLoop(string selectedStandardFile, CancellationToken token)
           {
               DateTime lastCycleEndTime = DateTime.Now;
               string iniPath = "C:\\system\\system.ini";
               string sampleFolder = "D:\\标样\\yangguang"; // 样管文件夹路径

               int fileIndex = 1; // 样管文件索引

               while (currentCycle < totalCycles)
               {
                   if (token.IsCancellationRequested)
                   {
                       MessageBox.Show("自校准任务已停止！", "停止", MessageBoxButtons.OK, MessageBoxIcon.Information);
                       StopCalibration();
                       return;
                   }

                   currentCycle++;
                   UpdateCycleLabel();

                   // 生成当前循环的样管文件名
                   string sampleFile = Path.Combine(sampleFolder, $"样管{fileIndex}.ini");

                   // 检查文件是否存在
                   if (!File.Exists(sampleFile))
                   {
                       MessageBox.Show($"缺少样管文件: {sampleFile}！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                       StopCalibration();
                       return;
                   }



                   if (currentCycle >= totalCycles)
                   {
                       MessageBox.Show("检测完成！所有循环已执行。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);

                       DateTime validUntil = lastCycleEndTime.AddHours(2); // 计算有效期限
                       WriteDeadlineToIni(iniPath, validUntil); // 写入 system.ini
                       UpdateValidUntilLabel(validUntil); // 更新 UI

                       this.Invoke(new Action(() =>
                       {
                           button2.Enabled = true;  // 只有成功完成才启用检测模式
                       }));

                       StopCalibration(false);
                   }

                   await Task.Delay(10000, token); // 等待 10 秒，进入下一次循环
               }
           }*/
        private void WriteDeadlineToIni(string iniPath, DateTime deadline)
        {
            try
            {
                List<string> lines = new List<string>();

                if (File.Exists(iniPath))
                {
                    lines = File.ReadAllLines(iniPath).ToList();
                }

                bool found = false;
                for (int i = 0; i < lines.Count; i++)
                {
                    if (lines[i].StartsWith("Deadline="))
                    {
                        lines[i] = $"Deadline={deadline:yyyy-MM-dd HH:mm:ss}"; // 直接更新
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    lines.Add($"Deadline={deadline:yyyy-MM-dd HH:mm:ss}"); // 确保一行
                }

                File.WriteAllLines(iniPath, lines);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"写入系统文件失败: {ex.Message}", "错误", MessageBoxButtons.OK);
            }
        }



        private void UpdateValidUntilLabel(DateTime validUntil)
        {
            if (label3.InvokeRequired)
            {
                label3.Invoke(new Action(() => UpdateValidUntilLabel(validUntil)));
            }
            else
            {
                label3.Text = $"检测有效期限：{validUntil:yyyy-MM-dd HH:mm:ss}";
            }
        }



        private DateTime ReadDeadlineFromIni(string iniPath)
        {
            try
            {
                if (!File.Exists(iniPath))
                    return DateTime.MinValue;

                string[] lines = File.ReadAllLines(iniPath);
                foreach (string line in lines)
                {
                    if (line.StartsWith("Deadline="))
                    {
                        string deadlineStr = line.Split('=')[1].Trim();
                        if (DateTime.TryParse(deadlineStr, out DateTime deadline))
                        {
                            return deadline;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"读取系统文件失败: {ex.Message}", "错误", MessageBoxButtons.OK);
            }

            return DateTime.MinValue;
        }


        private async void Form1_Load(object sender, EventArgs e)
        {
            string iniPath = "C:\\system\\system.ini";
            DateTime deadline = ReadDeadlineFromIni(iniPath);
            if (deadline != DateTime.MinValue)
            {
                UpdateValidUntilLabel(deadline);
            }

            Task.Run(() => CheckDeadline()); // 启动检查任务

            try
            {

                // 连接 PLC
                bool plcConnected = await _plcClient.ConnectAsync();
                if (plcConnected)
                {
                    Console.WriteLine("PLC 连接成功");
                }
                else
                {
                    Console.WriteLine("PLC 连接失败");
                }


                // 连接扫码枪
                string scannerIp = "127.0.0.1"; // 你的扫码枪 IP
                int scannerPort = 5001; // 端口号
                string deviceId = "Scanner_01"; // 设备 ID
                string errorMessage = string.Empty;
                bool scannerConnected = _scanGangBasic.Connect(scannerIp, scannerPort, deviceId, out errorMessage);
                if (scannerConnected)
                {
                    Console.WriteLine("扫码枪连接成功");
                }
                else
                {
                    Console.WriteLine("扫码枪连接失败");
                }

                // 启动 TCP 服务器
                await _tcpServer.StartWoLiuAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"初始化失败: {ex.Message}");
            }
        }


        private async void CheckDeadline()
        {
            while (true)
            {
                string iniPath = "C:\\system\\system.ini";
                DateTime deadline = ReadDeadlineFromIni(iniPath);
                DateTime now = DateTime.Now;

                if (deadline != DateTime.MinValue)
                {
                    TimeSpan remaining = deadline - now;

                    if (remaining.TotalMinutes <= 60 && remaining.TotalMinutes > 59)
                    {
                        //MessageBox.Show("检测有效期即将到期！剩余不到 1 小时。", "提醒", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        UpdateLabel7("提示信息：检测有效期剩余不到1小时");
                    }
                    else if (remaining.TotalMinutes > 60)
                    {

                        UpdateLabel7("");
                    }
                    else if (remaining.TotalSeconds <= 0)
                    {
                        //MessageBox.Show("检测有效期已过期！", "警告", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        UpdateLabel7("报警信息：检测有效期已过期");
                        // 使用 Invoke 确保 UI 线程操作
                        if (button7.InvokeRequired&& button8.InvokeRequired)
                        {
                            button7.Invoke(new Action(() => button7.Enabled = false));
                            button8.Invoke(new Action(() => button8.Enabled = false));
                        }
                        else
                        {
                            button7.Enabled = false;
                            button8.Enabled = false;
                        }
                    }
                    else
                    {

                        UpdateLabel7("");
                    }
                }

                await Task.Delay(1800000); // 每 30fz检查一次
            }
        }

        /*  private void UpdateLabel7(string text)
          {
              if (label7.InvokeRequired)
              {
                  label7.Invoke(new Action(() => label7.Text = text));
              }
              else
              {
                  label7.Text = text;
              }
          }*/
        private void UpdateLabel7(string text)
        {
            if (label7.InvokeRequired)
            {
                label7.Invoke(new Action(() =>
                {
                    label7.Text = text;
                    label7.Visible = !string.IsNullOrWhiteSpace(text); // 为空时隐藏
                }));
            }
            else
            {
                label7.Text = text;
                label7.Visible = !string.IsNullOrWhiteSpace(text); // 为空时隐藏
            }
        }


        private void UpdateCycleLabel()
        {
            // 更新当前循环次数和总循环次数
            if (label2.InvokeRequired)
            {
                // 如果在非UI线程，使用Invoke来回到UI线程更新
                label2.Invoke(new Action(UpdateCycleLabel));
            }
            else
            {
                label2.Text = $"当前循环次数: {currentCycle}";
            }
        }


        private void StopCalibration(bool isManualStop = false)
        {
            if (cancellationTokenSource != null)
            {
                cancellationTokenSource.Cancel();
                cancellationTokenSource.Dispose();
                cancellationTokenSource = null;
            }

            bool isCalibrationSuccessful = (currentCycle > 0 && currentCycle >= totalCycles);

            currentCycle = 0;
            totalCycles = 0;
            isOn1 = false;

            this.Invoke(new Action(() =>
            {
                label6.Text = "自校准模式";
                label1.Text = "当前状态：待机状态";
                label2.Text = "当前循环次数：0";

                // 手动停止 or 异常终止，都应该禁用检测模式
                button7.Enabled = isCalibrationSuccessful && !isManualStop;
                button8.Enabled = isCalibrationSuccessful && !isManualStop;
                label3.Text = $"停止自校准时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            }));
        }


        private void toolStripComboBox1_Click(object sender, EventArgs e)
        {

        }

        private void dataGridView1_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {

        }

        private void label1_Click(object sender, EventArgs e)
        {

        }



        /* private void Form1_Load(object sender, EventArgs e)
         {

         }*/


        private void label2_Click(object sender, EventArgs e)
        {

        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void label3_Click(object sender, EventArgs e)
        {

        }

        private void label4_Click(object sender, EventArgs e)
        {

        }





        private void label4_Click_1(object sender, EventArgs e)
        {

        }

        private void label5_Click(object sender, EventArgs e)
        {

        }

        private void button4_Click(object sender, EventArgs e)
        {

            if (!isOn)
            {
                MessageBox.Show("请先进入检测模式！", "提示", MessageBoxButtons.OK);
                return;
            }


            string batchNumber = textBox2.Text.Trim();


            if (string.IsNullOrWhiteSpace(batchNumber))
            {
                MessageBox.Show("批次号不能为空，请输入有效的批次号！", "警告", MessageBoxButtons.OK);
                return;
            }


            SaveBatchNumberToFile(batchNumber);
        }
        private void SaveBatchNumberToFile(string batchNumber)
        {
            string directoryPath = @"C:\system\"; // 保存目录
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string filePath = Path.Combine(directoryPath, $"{timestamp}_batch.txt");

            try
            {

                if (!Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }


                string content = $"时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n批次号：{batchNumber}\n-------------------\n";
                File.AppendAllText(filePath, content, Encoding.UTF8);


                MessageBox.Show($"批次号已成功保存到文件：{filePath}", "保存成功", MessageBoxButtons.OK);
            }
            catch (Exception ex)
            {

                MessageBox.Show($"保存批次号时发生错误：{ex.Message}", "错误", MessageBoxButtons.OK);
            }
        }

        private void textBox2_TextChanged(object sender, EventArgs e)
        {

        }

        private void textBox1_TextChanged(object sender, EventArgs e)
        {

        }



        private void label6_Click(object sender, EventArgs e)
        {

        }

        private void label7_Click(object sender, EventArgs e)
        {

        }

        private void label9_Click(object sender, EventArgs e)
        {

        }

        private async void button7_Click_1(object sender, EventArgs e)
        {
            bool writeSuccess = await _plcClient.WriteDRegisterAsync(2130, 1);

            if (writeSuccess)
            {
                isOn = true;
                button5.Enabled = false;
                button6.Enabled = false;
                button8.Enabled = true;   // 启用退出按钮

                // button2.Text = "进入检测模式"; // 这个可以根据需要修改按钮文本
                label9.Text = "检测模式运行中";

                label1.Text = "当前状态：检测模式";

                button5.Enabled = false;  // 禁用其他不需要的按钮
                button6.Enabled = false;
                textBox2.Enabled = true;
                button4.Enabled = true;

                detectionTimer.Start();

                UpdateLabel7(""); // 清空报警信息
            }
            else
            {
                // MessageBox.Show("无法进入检测模式，PLC通信失败！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                UpdateLabel7("报警信息：无法进入检测模式！");
            }
        }

        private async void button8_Click(object sender, EventArgs e)
        {
            bool writeSuccess = await _plcClient.WriteDRegisterAsync(2140, 1);

            if (writeSuccess)
            {
                isOn = false;
                detectionTimer.Stop();
                button7.Enabled = true;
                button8.Enabled = true;

                label9.Text = "检测模式";  // 修改按钮文本
                label1.Text = "当前状态：待机状态";

                button5.Enabled = true;
                button6.Enabled = true;
                textBox2.Enabled = false;
                button4.Enabled = false;

                UpdateLabel7("");
            }
            else
            {
                //MessageBox.Show("无法退出检测模式，PLC通信失败！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                UpdateLabel7("无法退出检测模式！");
            }
        }
    }

}
