using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using MvCamCtrl.NET;
using System.Runtime.InteropServices;
using System.Threading;
using System.IO;
using System.Net;
using System.Net.Sockets;

using System.Drawing.Imaging;
using System.Diagnostics;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
//调用外部类
using Files;
using URDate;
using URControl;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UR_点动控制器
{

    public partial class Form4 : Form
    {
        //用于摄像头调用的代码
        MyCamera.MV_CC_DEVICE_INFO_LIST m_pDeviceList;
        private MyCamera m_pMyCamera;
        bool m_bGrabbing;

        // ch:用于从驱动获取图像的缓存 | en:Buffer for getting image from driver
        UInt32 m_nBufSizeForDriver = 3072 * 2048 * 3;
        byte[] m_pBufForDriver = new byte[3072 * 2048 * 3];

        // ch:用于保存图像的缓存 | en:Buffer for saving image
        UInt32 m_nBufSizeForSaveImage = 3072 * 2048 * 3 * 3 + 2048;
        byte[] m_pBufForSaveImage = new byte[3072 * 2048 * 3 * 3 + 2048];

        public Form4()
        {
            InitializeComponent();
            Form.CheckForIllegalCrossThreadCalls = false;

        }

        [DllImport("AprilTagDll.dll", EntryPoint = "getTheMoveCommand", CharSet = CharSet.Unicode)]
        public static extern IntPtr getTheMoveCommand(double x, double y, double z, double rx, double ry, double rz);

        [DllImport("AprilTagDll.dll", EntryPoint = "getTheMoveCommandSecond", CharSet = CharSet.Unicode)]
        public static extern IntPtr getTheMoveCommandSecond(double x, double y, double z, double rx, double ry, double rz, double dx1, double dy1);

        [DllImport("AprilTagDll.dll", EntryPoint = "getTheMoveCommandX", CharSet = CharSet.Unicode)]
        public static extern IntPtr getTheMoveCommandX(double x, double y, double z, double rx, double ry, double rz);

        public double dx1;
        public double dy1;

        //主程序不应该关心细枝末节，只要知道问谁要到数据，还有要把数据给谁
        URDateHandle URDateCollector = new URDateHandle();
        URControlHandle URController = new URControlHandle();
        URControlHandle URPoweron = new URControlHandle();
        URControlHandle Commandtransmit = new URControlHandle();
       
        

        //声明全局的速度和加速度控制条
        public double SpeedRate;
        public double AccelerationRate;

        //这五个参数做成全局的会比较好用
        public double BasicSpeed;
        public double BasicAcceleration;
        public string Target_IP;
        public int Control_Port;
        public int DataRefreshRate;

        public double[] PreviousAngles = new double[6];
        public bool CurrentRunningState = false;

        //把所需要的图像也在初始化的时候就全部得到
        public Bitmap RobotState_Poweroff;
        public Bitmap RobotState_Ready;
        public Bitmap RobotState_SecurityStopped;
        public Bitmap RobotState_EmergencyStoped;
        public Bitmap RobotState_Teaching;

        public Bitmap RobotJog_XYZLeftClick;
        public Bitmap RobotJog_XYZLeftNormal;
        public Bitmap RobotJog_XYZRightClick;
        public Bitmap RobotJog_XYZRightNormal;
        public Bitmap RobotJog_XYZBackClick;
        public Bitmap RobotJog_XYZBackNormal;
        public Bitmap RobotJog_XYZForwardClick;
        public Bitmap RobotJog_XYZForwardNormal;
        public Bitmap RobotJog_XYZUpClick;
        public Bitmap RobotJog_XYZUpNormal;
        public Bitmap RobotJog_XYZDownClick;
        public Bitmap RobotJog_XYZDownNormal;
        public Bitmap RobotJog_RotateLeftClick;
        public Bitmap RobotJog_RotateLeftNormal;
        public Bitmap RobotJog_RotateRightClick;
        public Bitmap RobotJog_RotateRightNormal;

        //声明默认的配置文件路径
        public string DefaultINIPath = Convert.ToString(System.AppDomain.CurrentDomain.BaseDirectory) + "Config.ini";

        private void Form1_Load(object sender, EventArgs e)
        {

            //执行委托的绑定
            URDateCollector.OnGetPositionSuccess += new URDateHandle.GetPositionSuccess(UpdatePositionsValue);
            URDateCollector.OnGetAngleSuccess += new URDateHandle.GetAngleSuccess(UpdateAnglesValue);
            URDateCollector.OnGetRobotStateSuccess += new URDateHandle.GetRobotStateSuccess(UpdateRobotState);
            URDateCollector.OnGetRobotSpeedSuccess += new URDateHandle.GetRobotSpeedSuccess(UpdateRobotSpeed);

            //这里直接读取配置文件是否启用了自动连接
            FilesINI ConfigController = new FilesINI();
            string AutoConnection = ConfigController.INIRead("UR控制参数", "IfAutoConnect", DefaultINIPath);


            //这里读取到每个图片，方便下面修状态信息
            Do_Initilize_Image();

            //这里初始化速度值，其实速度值一直在跳变的
            Do_Initilize_RobotSpeed();
            //创建socket server
            Creat_server();

            //如果启用了自动连接，则直接获取所有自动连接参数，并运行连接方法
            if (AutoConnection == "YES")
            {
                //这里初始化的是获取机器人参数
                Do_Initilize();
            }


            
        }

        //不管用户是否勾选自动连接，手动连接都是执行这个方法，区别只是改好了配置文件再连接还是不用改就可以连接
        private void Do_Initilize()
        {
            FilesINI ConfigController = new FilesINI();

            Target_IP = ConfigController.INIRead("UR控制参数", "RemoteIP", DefaultINIPath);
            Control_Port = Convert.ToInt32(ConfigController.INIRead("UR控制参数", "RemoteControlPort", DefaultINIPath));
            DataRefreshRate = Convert.ToInt32(ConfigController.INIRead("UR运动参数", "BasicRefreshRate", DefaultINIPath));

            BasicSpeed = Convert.ToDouble(ConfigController.INIRead("UR运动参数", "BasicSpeed", DefaultINIPath));
            BasicAcceleration = Convert.ToDouble(ConfigController.INIRead("UR运动参数", "BasicAcceleration", DefaultINIPath));


            //我在URDateHandle中定义了刷新速度是静态的，所以可以直接赋值(先赋值，后实例化对象，否则直接运行就报错)
            URDateHandle.ScanRate = DataRefreshRate;

            //初始化URDateCollector，开始从502端口实时采集UR数据(需要提供要采集UR的IP地址)
            URDateCollector.InitialMoniter(Target_IP);

            //初始化URControlHandle，生成一个clientSocket，方便从30001-30003端口直接发送指令
            URController.Creat_client(Target_IP, Control_Port);
            //初始化URControlHandle，生成一个clientSocket，方便从29999端口发送上电启动指令
            URPoweron.Creat_client(Target_IP, 29999);
            //初始化URControlHandle，生成一个clientSocket，方便传输命令到下位机
            //Commandtransmit.Creat_client("192.168.1.10", 4333);
           
            //初始化速度和加速度(基准速度0.15 最高变成2倍即0.2，最低变成0.1倍即0.01)
            SpeedRate = BasicSpeed * SpeedBar.Value / 10;
            AccelerationRate = BasicAcceleration * AccelerationBar.Value / 10;
        }


        //创建socket服务器
        public Socket socket;
        public void Creat_server()
        {
            //创建一个新的Socket,这里我们使用最常用的基于TCP的Stream Socket（流式套接字）
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            //将该socket绑定到主机上面的某个端口
            socket.Bind(new IPEndPoint(IPAddress.Any, 4332));

            //启动监听，并且设置一个最大的队列长度
            socket.Listen(4);

            //开始接受客户端连接请求
            socket.BeginAccept(new AsyncCallback(ClientAccepted), socket);

        }
        public Socket client;
        public void ClientAccepted(IAsyncResult ar)
        {

            socket = ar.AsyncState as Socket;

            //这就是客户端的Socket实例，我们后续可以将其保存起来
             client = socket.EndAccept(ar);

            //给客户端发送一个欢迎消息
            //client.Send(Encoding.Unicode.GetBytes( DateTime.Now.ToString()));


            //实现每隔两秒钟给服务器发一个消息
            //这里我们使用了一个定时器



            //接收客户端的消息(这个和在客户端实现的方式是一样的）
       //     client.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, new AsyncCallback(ReceiveMessage), client);

            //准备接受下一个客户端请求
            socket.BeginAccept(new AsyncCallback(ClientAccepted), socket);
        }


      
        public void Writelog(string action)
        {
            using (FileStream fs = new FileStream("Data\\action.log", FileMode.Append))
            {
                //写入
                using (StreamWriter sw = new StreamWriter(fs))
                {
                    System.DateTime currentTime = System.DateTime.Now;
                    string Text_write = currentTime.ToString("f") + ":" + action;
                    sw.WriteLine(Text_write);

                }

            }

        }
        public void Write_Tasklog(string action)
        {
            using (FileStream fs = new FileStream("Data\\task.log", FileMode.Append))
            {
                //写入
                using (StreamWriter sw = new StreamWriter(fs))
                {
                    string Text_write =  action;
                    sw.WriteLine(Text_write);

                }

            }

        }
        static byte[] buffer = new byte[1024];



        public void CancelOperation()
        {

            socket.Send(Encoding.UTF8.GetBytes(GlobalData.ID + "##cancel"));


        }
        //传输下位机的回传信息
        public void Down_Back()
        {
            //获取初始位置
            socket.Send(Encoding.Unicode.GetBytes(GlobalData.back_message));

        }
        //把主界面所需要的图片都从文件中实例化
        private void Do_Initilize_Image()
        {
            string DebugDir = Convert.ToString(System.AppDomain.CurrentDomain.BaseDirectory);

            #region 机器人状态的图片
            string RobotState_Poweroff_PicDir = DebugDir + "\\Button\\BtnPoweroff.png";
            RobotState_Poweroff = new Bitmap(RobotState_Poweroff_PicDir);

            string RobotState_Ready_PicDir = DebugDir + "\\Button\\BtnReady.png";
            RobotState_Ready = new Bitmap(RobotState_Ready_PicDir);

            string RobotState_SecurityStopped_PicDir = DebugDir + "\\Button\\BtnSecurityStopped.png";
            RobotState_SecurityStopped = new Bitmap(RobotState_SecurityStopped_PicDir);

            string RobotState_EmergencyStoped_PicDir = DebugDir + "\\Button\\BtnEmergencyStoped.png";
            RobotState_EmergencyStoped = new Bitmap(RobotState_EmergencyStoped_PicDir);

            string RobotState_Teaching_PicDir = DebugDir + "\\Button\\BtnTeaching.png";
            RobotState_Teaching = new Bitmap(RobotState_Teaching_PicDir);

            #endregion

            #region XYZ控制按钮
            //XYZ左移按钮
            string RobotJog_XYZLeftClick_PicDir = DebugDir + "\\Arrow\\ArrowXYZLeft_click.png";
            RobotJog_XYZLeftClick = new Bitmap(RobotJog_XYZLeftClick_PicDir);

            string RobotJog_XYZLeftNormal_PicDir = DebugDir + "\\Arrow\\ArrowXYZLeft_normal.png";
            RobotJog_XYZLeftNormal = new Bitmap(RobotJog_XYZLeftNormal_PicDir);

            //XYZ右移按钮
            string RobotJog_XYZRightClick_PicDir = DebugDir + "\\Arrow\\ArrowXYZRight_click.png";
            RobotJog_XYZRightClick = new Bitmap(RobotJog_XYZRightClick_PicDir);

            string RobotJog_XYZRightNormal_PicDir = DebugDir + "\\Arrow\\ArrowXYZRight_normal.png";
            RobotJog_XYZRightNormal = new Bitmap(RobotJog_XYZRightNormal_PicDir);

            //XYZ后移按钮
            string RobotJog_XYZBackClick_PicDir = DebugDir + "\\Arrow\\ArrowXYZBack_click.png";
            RobotJog_XYZBackClick = new Bitmap(RobotJog_XYZBackClick_PicDir);

            string RobotJog_XYZBackNormal_PicDir = DebugDir + "\\Arrow\\ArrowXYZBack_normal.png";
            RobotJog_XYZBackNormal = new Bitmap(RobotJog_XYZBackNormal_PicDir);

            //XYZ前移按钮
            string RobotJog_XYZForwardClick_PicDir = DebugDir + "\\Arrow\\ArrowXYZForward_click.png";
            RobotJog_XYZForwardClick = new Bitmap(RobotJog_XYZForwardClick_PicDir);

            string RobotJog_XYZForwardNormal_PicDir = DebugDir + "\\Arrow\\ArrowXYZForward_normal.png";
            RobotJog_XYZForwardNormal = new Bitmap(RobotJog_XYZForwardNormal_PicDir);

            //XYZ上移按钮
            string RobotJog_XYZUpClick_PicDir = DebugDir + "\\Arrow\\ArrowXYZUp_click.png";
            RobotJog_XYZUpClick = new Bitmap(RobotJog_XYZUpClick_PicDir);

            string RobotJog_XYZUpNormal_PicDir = DebugDir + "\\Arrow\\ArrowXYZUp_normal.png";
            RobotJog_XYZUpNormal = new Bitmap(RobotJog_XYZUpNormal_PicDir);

            //XYZ下移按钮
            string RobotJog_XYZDownClick_PicDir = DebugDir + "\\Arrow\\ArrowXYZDown_click.png";
            RobotJog_XYZDownClick = new Bitmap(RobotJog_XYZDownClick_PicDir);

            string RobotJog_XYZDownNormal_PicDir = DebugDir + "\\Arrow\\ArrowXYZDown_normal.png";
            RobotJog_XYZDownNormal = new Bitmap(RobotJog_XYZDownNormal_PicDir);

            #endregion


            #region 六轴运动按钮
            //六轴旋转的左移按钮
            string RobotJog_RotateLeftClick_PicDir = DebugDir + "\\Arrow\\Arrow_left_click.png";
            RobotJog_RotateLeftClick = new Bitmap(RobotJog_RotateLeftClick_PicDir);

            string RobotJog_RotateLeftNormal_PicDir = DebugDir + "\\Arrow\\Arrow_left_normal.png";
            RobotJog_RotateLeftNormal = new Bitmap(RobotJog_RotateLeftNormal_PicDir);

            //六轴旋转的右移按钮
            string RobotJog_RotateRightClick_PicDir = DebugDir + "\\Arrow\\Arrow_right_click.png";
            RobotJog_RotateRightClick = new Bitmap(RobotJog_RotateRightClick_PicDir);

            string RobotJog_RotateRightNormal_PicDir = DebugDir + "\\Arrow\\Arrow_right_normal.png";
            RobotJog_RotateRightNormal = new Bitmap(RobotJog_RotateRightNormal_PicDir);

            #endregion

            //默认初始化的时候是这幅图片
            RobotStatusPic.Image = RobotState_Poweroff;

        }

        private void Do_Initilize_RobotSpeed()
        {
            //如果要检测UR的TCP速度，但是由于晃动问题，请使用get_target_tcp_speed方法
            label_VX.Visible = false;
            label_VY.Visible = false;
            label_VZ.Visible = false;
            label_VRX.Visible = false;
            label_VRY.Visible = false;
            label_VRZ.Visible = false;

        }

        private void SpeedChange(object sender, EventArgs e)
        {
            SpeedRate = BasicSpeed * SpeedBar.Value / 10;
        }

        private void AccelerationChange(object sender, EventArgs e)
        {
            AccelerationRate = BasicAcceleration * AccelerationBar.Value / 10;
        }

        //退出程序要把所有都释放掉
        private void QuitApp(object sender, FormClosingEventArgs e)
        {
            URController.Close_client();
            URController = null;
        }

        //将取到的数据放入文本框(当需要被通知时候触发)
        void UpdatePositionsValue(float[] Positions)
        {

            X_Position.Text = Positions[0].ToString("0.0");
            Y_Position.Text = Positions[1].ToString("0.0");
            Z_Position.Text = Positions[2].ToString("0.0");
            U_Position.Text = Positions[3].ToString("0.000");
            V_Position.Text = Positions[4].ToString("0.000");
            W_Position.Text = Positions[5].ToString("0.000");
        }

        void UpdateAnglesValue(double[] Angles)
        {

            int[] AngleBar_Values = new int[6];

            //由于Angle已经取到的是正负360度，所以正负要做区分
            for (int i = 0; i < Angles.Length; i++)
            {
                if (Angles[i] < 0)
                {
                    AngleBar_Values[i] = 360 - Math.Abs(Convert.ToInt32(Angles[i]));
                }
                else
                {
                    AngleBar_Values[i] = 360 + Math.Abs(Convert.ToInt32(Angles[i]));
                }
            }

            //这里使用了自定义控件，所以不再是Value属性
            AngleBarX.Position = AngleBar_Values[0];
            AngleBarY.Position = AngleBar_Values[1];
            AngleBarZ.Position = AngleBar_Values[2];
            AngleBarU.Position = AngleBar_Values[3];
            AngleBarV.Position = AngleBar_Values[4];
            AngleBarW.Position = AngleBar_Values[5];

            AngleBarX.Text = Angles[0].ToString("0.00") + "  °";
            AngleBarY.Text = Angles[1].ToString("0.00") + "  °";
            AngleBarZ.Text = Angles[2].ToString("0.00") + "  °";
            AngleBarU.Text = Angles[3].ToString("0.00") + "  °";
            AngleBarV.Text = Angles[4].ToString("0.00") + "  °";
            AngleBarW.Text = Angles[5].ToString("0.00") + "  °";

            int TotalCount = 0;
            //这里除了把六个角度值都放到界面中显示，还根据六个角度值的变化情况，得出机器人是否在运行的结果（大致的判断方法）
            for (int i = 0; i < 6; i++)
            {
                //只要不等，就让他相等(暂时认为是两者的差值在我指定的误差范围之内，而不是绝对相等)
                if (Math.Abs(PreviousAngles[i] - Angles[i]) > 0.02)
                {
                    //只要发现一个超过范围的，就让总体计数值加1，看最后有多少不一样
                    TotalCount++;
                }
                PreviousAngles[i] = Angles[i];
            }

            //如果最多只有1个数据变动较小，则不管，认为机器人没动，否则认为动了
            if (TotalCount > 2)
            {
                CurrentRunningState = true;
            }
            else
            {
                CurrentRunningState = false;
            }

            RobotRunningLabel.Text = CurrentRunningState.ToString();


        }

        void UpdateRobotState(int[] RobotState)
        {
            if (RobotState[1] == 1)
            {
                try
                {
                    RobotStatusPic.Image = RobotState_SecurityStopped;
                    this.RobotStatusLabel.Text = "安全停机";
                    socket.Send(Encoding.Unicode.GetBytes("error"));
                    //解除停机
                    string unlock_str = "unlock protective stop";
                    URPoweron.Send_command(unlock_str);
                    //机械臂复位
                    string Resetcommandone = "movel(p[" + (URDateHandle.Positions_X + 0.05).ToString("0.0000") + "," + (URDateHandle.Positions_Y - 0.05).ToString("0.0000") + "," + URDateHandle.Positions_Z.ToString("0.0000") + "," + URDateHandle.Positions_U.ToString("0.0000") + "," + URDateHandle.Positions_V.ToString("0.0000") + "," + URDateHandle.Positions_W.ToString("0.0000") + "], a = " + AccelerationRate.ToString() + ", v = " + SpeedRate.ToString() + ")";
                    URController.Send_command(Resetcommandone);
                    string Resetcommandtwo = "movel(p[-0.1595,-0.0017,0.5434,-0.007,-0.176,0.763], a = 0.15, v = 0.15)";
                    URController.Send_command(Resetcommandtwo);
                    Writelog("机械臂安全停机复位");
                }
                catch { }
            }
            if (RobotState[2] == 1)
            {
                try
                {
                    RobotStatusPic.Image = RobotState_EmergencyStoped;
                    this.RobotStatusLabel.Text = "紧急停机";
                    Writelog("机械臂紧急停机");
                }
                catch { }
            }
            if (RobotState[3] == 1)
            {
                try
                {
                    RobotStatusPic.Image = RobotState_Teaching;
                    this.RobotStatusLabel.Text = "示教模式";
                    Writelog("机械臂示教模式");
                }
                catch { }
            }

            //这里的判断一定要跟前面区分开来，只有前面都不满足的时候这里才判断，否则这里的结果会和前面冲突，显示到界面上就会闪烁了
            if (RobotState[1] != 1 && RobotState[2] != 1 && RobotState[3] != 1)
            {
                if (RobotState[0] == 1)
                {
                    try
                    {
                        RobotStatusPic.Image = RobotState_Ready;
                        this.RobotStatusLabel.Text = "已连接";
                        
                    }
                    catch { }
                }

            }




        }

        void UpdateRobotSpeed(int[] RobotSpeed)
        {
            //更新速度信息有一个很严重的问题，由于UR的抖动的不确定性，即如果UR没有运行，但是末端还是会有微量的移动，所以速度会一直在跳变
            //如果此时按下示教按钮再松开，则抖动问题就没了，再按下示教按钮，再松开，问题又出现了，在我们无法确切的排除掉抖动问题之前，我们无法靠TCP的速度的寄存器来判定当前机器人是否在运行
            /**/
            try
            {
                label_VX.Text = RobotSpeed[0].ToString("0.0000");
                label_VY.Text = RobotSpeed[1].ToString("0.0000");
                label_VZ.Text = RobotSpeed[2].ToString("0.0000");
                label_VRX.Text = RobotSpeed[3].ToString("0.0000");
                label_VRY.Text = RobotSpeed[4].ToString("0.0000");
                label_VRZ.Text = RobotSpeed[5].ToString("0.0000");
            }
            catch { }

        }


        //对于XYZ的线性移动，需要定义一个方法，只需要传入要移动的轴和移动方向(方向就是1和-1)，返回移动的命令
        string GetLinearMovementCommand(string whatAxis, int direction)
        {
            //不管怎么样都要获取当前的坐标值
            double new_X = URDateHandle.Positions_X;
            double new_Y = URDateHandle.Positions_Y;
            double new_Z = URDateHandle.Positions_Z;
            double new_U = URDateHandle.Positions_U;
            double new_V = URDateHandle.Positions_V;
            double new_W = URDateHandle.Positions_W;

            //然后根据点动的按钮，判断要改哪个值(这里不是旋转，只有X,Y,Z三种可能)，直接覆盖到真实的当前XYZ值
            if (whatAxis == "X")
            {
                if (direction == 1)
                {
                    new_X = new_X + 10;
                    new_Y = new_Y + 10;
                }
                else if (direction == -1)
                {
                    new_X = new_X - 10;
                    new_Y = new_Y - 10;
                }

            }
            else if (whatAxis == "Y")
            {
                if (direction == 1)
                {
                    new_X = new_X - 10;
                    new_Y = new_Y + 10;
                }
                else if (direction == -1)
                {
                    new_X = new_X + 10;
                    new_Y = new_Y - 10;
                }
            }
            else if (whatAxis == "Z")
            {
                new_Z = ((new_Z + 10) * direction);
            }
            else
            {
                //也有可能我不要移动，只是要看指令
            }


            //最后把方向运动的指令发送出去
            string command = "movel(p[" + new_X.ToString("0.0000") + "," + new_Y.ToString("0.0000") + "," + new_Z.ToString("0.0000") + "," + new_U.ToString("0.0000") + "," + new_V.ToString("0.0000") + "," + new_W.ToString("0.0000") + "], a = " + AccelerationRate.ToString() + ", v = " + SpeedRate.ToString() + ")";
            CustomCommand.Text = command;
            return command;
        }

        //对于六轴转动，跟前面类似
        string GetRotationMovementCommand(string whatAxis, int direction)
        {
            //不管怎么样都要获取当前的六个关节值
            double new_X = URDateHandle.Angles_X;
            double new_Y = URDateHandle.Angles_Y;
            double new_Z = URDateHandle.Angles_Z;
            double new_U = URDateHandle.Angles_U;
            double new_V = URDateHandle.Angles_V;
            double new_W = URDateHandle.Angles_W;

            if (whatAxis == "X")
            {
                new_X = ((new_X + 100) * direction);
            }
            else if (whatAxis == "Y")
            {
                new_Y = ((new_Y + 100) * direction);
            }
            else if (whatAxis == "Z")
            {
                new_Z = ((new_Z + 100) * direction);
            }
            else if (whatAxis == "U")
            {
                new_U = ((new_U + 100) * direction);
            }
            else if (whatAxis == "V")
            {
                new_V = ((new_V + 100) * direction);
            }
            else if (whatAxis == "W")
            {
                new_W = ((new_W + 100) * direction);
            }
            else
            {
                //也有可能我不要移动，只是要看指令
            }
            //最后把方向运动的指令发送出去
            string command = "movej([" + new_X.ToString("0.0000") + "," + new_Y.ToString("0.0000") + "," + new_Z.ToString("0.0000") + "," + new_U.ToString("0.0000") + "," + new_V.ToString("0.0000") + "," + new_W.ToString("0.0000") + "], a = " + AccelerationRate.ToString() + ", v = " + SpeedRate.ToString() + ")";
            CustomCommand.Text = command;
            return command;

        }

        //发送停止命令则很简单了，都是发送stopl(1)
        string GetStopCommand()
        {
            string StopCommand = "stopl(1)";
            CustomCommand.Text = StopCommand;
            return StopCommand;
        }

        # region XYZ平移区域
        //XYZ左移按钮按下
        private void Move_Left_Down(object sender, MouseEventArgs e)
        {
            try
            {
                this.Move_Left.Image = RobotJog_XYZLeftClick;
                string str = GetLinearMovementCommand("X", -1);
                URController.Send_command(str);
            }
            catch { }

        }
        //XYZ左移按钮松开
        private void Move_Left_Up(object sender, MouseEventArgs e)
        {
            try
            {
                this.Move_Left.Image = RobotJog_XYZLeftNormal;
                string str = GetStopCommand();
                URController.Send_command(str);
            }
            catch { }

        }
        //XYZ右移按钮按下
        private void Move_Right_Down(object sender, MouseEventArgs e)
        {
            try
            {
                this.Move_Right.Image = RobotJog_XYZRightClick;
                string str = GetLinearMovementCommand("X", 1);
                URController.Send_command(str);
            }
            catch { }

        }
        //XYZ右移按钮松开
        private void Move_Right_Up(object sender, MouseEventArgs e)
        {
            try
            {
                this.Move_Right.Image = RobotJog_XYZRightNormal;
                string str = GetStopCommand();
                URController.Send_command(str);
            }
            catch { }

        }
        //XYZ后移按钮按下
        private void Move_Back_Down(object sender, MouseEventArgs e)
        {
            try
            {
                this.Move_Back.Image = RobotJog_XYZBackClick;
                string str = GetLinearMovementCommand("Y", 1);
                URController.Send_command(str);
            }
            catch { }

        }
        //XYZ后移按钮松开
        private void Move_Back_Up(object sender, MouseEventArgs e)
        {
            try
            {
                this.Move_Back.Image = RobotJog_XYZBackNormal;
                string str = GetStopCommand();
                URController.Send_command(str);
            }
            catch { }

        }
        //XYZ前移按钮按下
        private void Move_Forward_Down(object sender, MouseEventArgs e)
        {
            try
            {
                this.Move_Forward.Image = RobotJog_XYZForwardClick;
                string str = GetLinearMovementCommand("Y", -1);
                URController.Send_command(str);
            }
            catch { }
        }
        //XYZ前移按钮松开
        private void Move_Forward_Up(object sender, MouseEventArgs e)
        {
            try
            {
                this.Move_Forward.Image = RobotJog_XYZForwardNormal;
                string str = GetStopCommand();
                URController.Send_command(str);
            }
            catch { }
        }
        //XYZ上移按钮按下
        private void Move_Up_Down(object sender, MouseEventArgs e)
        {
            try
            {
                this.Move_Up.Image = RobotJog_XYZUpClick;
                string str = GetLinearMovementCommand("Z", 1);
                URController.Send_command(str);
            }
            catch { }
        }
        //XYZ上移按钮松开
        private void Move_Up_Up(object sender, MouseEventArgs e)
        {
            try
            {
                this.Move_Up.Image = RobotJog_XYZUpNormal;
                string str = GetStopCommand();
                URController.Send_command(str);
            }
            catch { }
        }
        //XYZ下移按钮按下
        private void Move_Down_Down(object sender, MouseEventArgs e)
        {
            try
            {
                this.Move_Down.Image = RobotJog_XYZDownClick;
                string str = GetLinearMovementCommand("Z", -1);
                URController.Send_command(str);
            }
            catch { }
        }
        //XYZ下移按钮松开
        private void Move_Down_Up(object sender, MouseEventArgs e)
        {
            try
            {
                this.Move_Down.Image = RobotJog_XYZDownNormal;
                string str = GetStopCommand();
                URController.Send_command(str);
            }
            catch { }
        }

        #endregion

        #region X左右旋转

        //六轴旋转（X向左转按下）
        private void X_Left_Rotate_Down(object sender, MouseEventArgs e)
        {
            try
            {
                this.X_Left_Rotate.Image = RobotJog_RotateLeftClick;
                string str = GetRotationMovementCommand("X", -1);
                URController.Send_command(str);
            }
            catch { }


        }
        //六轴旋转（X向左转松开）
        private void X_Left_Rotate_Up(object sender, MouseEventArgs e)
        {
            try
            {
                this.X_Left_Rotate.Image = RobotJog_RotateLeftNormal;
                string str = GetStopCommand();
                URController.Send_command(str);
            }
            catch { }
        }
        //六轴旋转（X向右转按下）
        private void X_Right_Rotate_Down(object sender, MouseEventArgs e)
        {
            try
            {
                this.X_Right_Rotate.Image = RobotJog_RotateRightClick;
                string str = GetRotationMovementCommand("X", 1);
                URController.Send_command(str);
            }
            catch { }
        }
        //六轴旋转（X向右转松开）
        private void X_Right_Rotate_Up(object sender, MouseEventArgs e)
        {
            try
            {
                this.X_Right_Rotate.Image = RobotJog_RotateRightNormal;
                string str = GetStopCommand();
                URController.Send_command(str);
            }
            catch { }
        }

        //六轴旋转（Y向左转按下）
        private void Y_Left_Rotate_Down(object sender, MouseEventArgs e)
        {
            try
            {
                this.Y_Left_Rotate.Image = RobotJog_RotateLeftClick;
                string str = GetRotationMovementCommand("Y", -1);
                URController.Send_command(str);
            }
            catch { }
        }
        //六轴旋转（Y向左转松开）
        private void Y_Left_Rotate_Up(object sender, MouseEventArgs e)
        {
            try
            {
                this.Y_Left_Rotate.Image = RobotJog_RotateLeftNormal;
                string str = GetStopCommand();
                URController.Send_command(str);
            }
            catch { }
        }

        //六轴旋转（Y向右转按下）
        private void Y_Right_Rotate_Down(object sender, MouseEventArgs e)
        {
            try
            {
                this.Y_Right_Rotate.Image = RobotJog_RotateRightClick;
                string str = GetRotationMovementCommand("Y", 1);
                URController.Send_command(str);
            }
            catch { }
        }
        //六轴旋转（Y向右转松开）
        private void Y_Right_Rotate_Up(object sender, MouseEventArgs e)
        {
            try
            {
                this.Y_Right_Rotate.Image = RobotJog_RotateRightNormal;
                string str = GetStopCommand();
                URController.Send_command(str);
            }
            catch { }
        }


        //六轴旋转（Z向左转按下）
        private void Z_Left_Rotate_Down(object sender, MouseEventArgs e)
        {
            try
            {
                this.Z_Left_Rotate.Image = RobotJog_RotateLeftClick;
                string str = GetRotationMovementCommand("Z", -1);
                URController.Send_command(str);
            }
            catch { }
        }
        //六轴旋转（Z向左转松开）
        private void Z_Left_Rotate_Up(object sender, MouseEventArgs e)
        {
            try
            {
                this.Z_Left_Rotate.Image = RobotJog_RotateLeftNormal;
                string str = GetStopCommand();
                URController.Send_command(str);
            }
            catch { }
        }
        //六轴旋转（Z向右转按下）
        private void Z_Right_Rotate_Down(object sender, MouseEventArgs e)
        {
            try
            {
                this.Z_Right_Rotate.Image = RobotJog_RotateRightClick;
                string str = GetRotationMovementCommand("Z", 1);
                URController.Send_command(str);
            }
            catch { }
        }
        //六轴旋转（Z向右转松开）
        private void Z_Right_Rotate_Up(object sender, MouseEventArgs e)
        {
            try
            {
                this.Z_Right_Rotate.Image = RobotJog_RotateRightNormal;
                string str = GetStopCommand();
                URController.Send_command(str);
            }
            catch { }
        }

        //六轴旋转（U向左转按下）
        private void U_Left_Rotate_Down(object sender, MouseEventArgs e)
        {
            try
            {
                this.U_Left_Rotate.Image = RobotJog_RotateLeftClick;
                string str = GetRotationMovementCommand("U", -1);
                URController.Send_command(str);
            }
            catch { }
        }
        //六轴旋转（U向左转松开）
        private void U_Left_Rotate_Up(object sender, MouseEventArgs e)
        {
            try
            {
                this.U_Left_Rotate.Image = RobotJog_RotateLeftNormal;
                string str = GetStopCommand();
                URController.Send_command(str);
            }
            catch { }
        }
        //六轴旋转（U向右转按下）
        private void U_Right_Rotate_Down(object sender, MouseEventArgs e)
        {
            try
            {
                this.U_Right_Rotate.Image = RobotJog_RotateRightClick;
                string str = GetRotationMovementCommand("U", 1);
                URController.Send_command(str);
            }
            catch { }
        }
        //六轴旋转（U向右转松开）
        private void U_Right_Rotate_Up(object sender, MouseEventArgs e)
        {
            try
            {
                this.U_Right_Rotate.Image = RobotJog_RotateRightNormal;
                string str = GetStopCommand();
                URController.Send_command(str);
            }
            catch { }
        }

        //六轴旋转（V向左转按下）
        private void V_Left_Rotate_Down(object sender, MouseEventArgs e)
        {
            try
            {
                this.V_Left_Rotate.Image = RobotJog_RotateLeftClick;
                string str = GetRotationMovementCommand("V", -1);
                URController.Send_command(str);
            }
            catch { }
        }
        //六轴旋转（V向左转松开）
        private void V_Left_Rotate_Up(object sender, MouseEventArgs e)
        {
            try
            {
                this.V_Left_Rotate.Image = RobotJog_RotateLeftNormal;
                string str = GetStopCommand();
                URController.Send_command(str);
            }
            catch { }
        }
        //六轴旋转（V向右转按下）
        private void V_Right_Rotate_Down(object sender, MouseEventArgs e)
        {
            try
            {
                this.V_Right_Rotate.Image = RobotJog_RotateRightClick;
                string str = GetRotationMovementCommand("V", 1);
                URController.Send_command(str);
            }
            catch { }
        }
        //六轴旋转（V向右转松开）
        private void V_Right_Rotate_Up(object sender, MouseEventArgs e)
        {
            try
            {
                this.V_Right_Rotate.Image = RobotJog_RotateRightNormal;
                string str = GetStopCommand();
                URController.Send_command(str);
            }
            catch { }
        }
        //六轴旋转（W向左转按下）
        private void W_Left_Rotate_Down(object sender, MouseEventArgs e)
        {
            try
            {
                this.W_Left_Rotate.Image = RobotJog_RotateLeftClick;
                string str = GetRotationMovementCommand("W", -1);
                URController.Send_command(str);
            }
            catch { }
        }
        //六轴旋转（W向左转松开）
        private void W_Left_Rotate_Up(object sender, MouseEventArgs e)
        {
            try
            {
                this.W_Left_Rotate.Image = RobotJog_RotateLeftNormal;
                string str = GetStopCommand();
                URController.Send_command(str);
            }
            catch { }
        }
        //六轴旋转（W向右转按下）
        private void W_Right_Rotate_Down(object sender, MouseEventArgs e)
        {
            try
            {
                this.W_Right_Rotate.Image = RobotJog_RotateRightClick;
                string str = GetRotationMovementCommand("W", 1);
                URController.Send_command(str);
            }
            catch { }
        }
        //六轴旋转（W向右转松开）
        private void W_Right_Rotate_Up(object sender, MouseEventArgs e)
        {
            try
            {
                this.W_Right_Rotate.Image = RobotJog_RotateRightNormal;
                string str = GetStopCommand();
                URController.Send_command(str);
            }
            catch { }
        }
        # endregion


        #region 顶部菜单栏

        //文件-参数设置
        private void File_SetParameter_Click(object sender, EventArgs e)
        {

            //我决定还是少用一点华而不实的功能，不就是设置参数嘛，何必搞一大堆配置文件，又不是很多参数，直接打开这个窗口
            Config ConfigWindow = new Config(DefaultINIPath);
            ConfigWindow.ShowDialog();
        }

        //文件-手动连接
        private void File_Connect_Click(object sender, EventArgs e)
        {
            //用户没有勾选自动连接，则是每次修改好了的配置文件去读取并执行连接方法
            Do_Initilize();
        }

        //文件-断开连接
        private void File_Disconnect_Click(object sender, EventArgs e)
        {
            //用户点击断开连接，则
            URDateCollector = null;
            URController.Close_client();
        }


        //帮助-所有版本
        private void Help_AllVersion_Click(object sender, EventArgs e)
        {
            System.Diagnostics.Process.Start("http://pan.baidu.com/s/1i3KBSDf");
        }

        //帮助-关于本软件
        private void Help_Hint_Click(object sender, EventArgs e)
        {
            //生成一个实例
            AboutMe About = new AboutMe();
            About.ShowDialog();
        }

        //帮助-问题反馈
        private void Help_Feedback_Click(object sender, EventArgs e)
        {
            //生成一个实例
            Feedback Feed = new Feedback();
            Feed.ShowDialog();
        }



        //常用工具-IP修改
        private void Tools_IPChange_Click(object sender, EventArgs e)
        {
            //生成一个实例
            IPChange IPWindow = new IPChange();
            IPWindow.ShowDialog();
        }

        //常用工具-自定义命令面板(单击一次显示，再单击一次隐藏)
        private void Tools_PersonalCommand_Click(object sender, EventArgs e)
        {

            //由于点击按钮之后都会触发，所以只需要判断这三个控件的可见性即可反复的显示或隐藏
            if (CustomLabel.Visible == false)
            {
                CustomLabel.Visible = true;
                CustomCommand.Visible = true;
                btnCustomSend.Visible = true;
                Change_All_Position.Visible = true;
                Change_All_Angles.Visible = true;

                Tools_PersonalCommand.Text = "隐藏自定义命令";
            }
            else
            {
                CustomLabel.Visible = false;
                CustomCommand.Visible = false;
                btnCustomSend.Visible = false;
                Change_All_Position.Visible = false;
                Change_All_Angles.Visible = false;

                Tools_PersonalCommand.Text = "显示自定义命令";
            }
        }

        //这就是自定义命令的三个控件，只要控制他们显示与隐藏即可（btnCustomSend,CustomCommand,CustomLabel）
        private void btnCustomSend_Click(object sender, EventArgs e)
        {
            //我在测试的框子中可以放任意命令
            string str = CustomCommand.Text;
            URController.Send_command(str);
        }

        //常用工具-G代码转换面板
        private void Tools_Gcode_Click(object sender, EventArgs e)
        {
            GCode GcodeWindow = new GCode(DefaultINIPath);
            GcodeWindow.Show();
        }

        //常用工具-增强示教面板
        private void Tools_Teach_Click(object sender, EventArgs e)
        {
            Teach TeachWindow = new Teach(DefaultINIPath);
            TeachWindow.Show();
        }

        //常用工具-相机标定及特征识别面板
        private void Tools_CameraCalibrate_Click(object sender, EventArgs e)
        {
            CameraCalibration CameraWindow = new CameraCalibration(DefaultINIPath);
            CameraWindow.Show();
        }

        //常用工具-相机标定及特征追踪面板
        private void Tools_CameraTracking_Click(object sender, EventArgs e)
        {

        }

        //常用工具，相机标定及视觉分拣面板
        private void Tools_CameraSorting_Click(object sender, EventArgs e)
        {

        }


        //测试工具：寄存器读写测试
        private void Tools_RegisterTest_Click(object sender, EventArgs e)
        {
            //还是要把配置文件的地址传过去
            Register RegisterWindow = new Register(DefaultINIPath);
            RegisterWindow.Show();
        }

        //测试工具：绘图工具测试
        private void Tools_DrawingTest_Click(object sender, EventArgs e)
        {
            Painting PaintWindow = new Painting();
            PaintWindow.Show();
        }

        //测试工具：图像轮廓拟合测试
        private void Tools_ImageProfileTest_Click(object sender, EventArgs e)
        {

        }

        //测试工具：双臂协同测试面板
        private void Tools_TorsoTest_Click(object sender, EventArgs e)
        {
            Torso TorsoWindow = new Torso();
            TorsoWindow.Show();
        }


        //测试工具：Dashboard
        private void Tools_DashboardTest_Click(object sender, EventArgs e)
        {
            Dashboard DashboardWindow = new Dashboard(DefaultINIPath);
            DashboardWindow.Show();
        }



        #endregion

        //有时候我就是要往X方向走1mm，则直接修改坐标即可
        private void Change_All_Position_Click(object sender, EventArgs e)
        {
            //获取下面六个值，然后发送(并没有ABC这个轴，我只是不作处理)
            //有时候我就是需要得到当前的TCP坐标值而已
            string str = GetLinearMovementCommand("ABC", 1);
            CustomCommand.Text = str;

        }

        private void Change_All_Angles_Click(object sender, EventArgs e)
        {
            //获取下面六个值，然后发送(并没有ABC这个轴，我只是不作处理)
            //有时候我就是需要得到当前的六轴关节值而已
            string str = GetRotationMovementCommand("ABC", 1);
            CustomCommand.Text = str;
        }

        private void Help_UpdateHistory_Click(object sender, EventArgs e)
        {
            //获取当前目录(我把发布方式改成Release就是Release而不是Debug了)
            //string ReleaseDir = Convert.ToString(System.AppDomain.CurrentDomain.BaseDirectory);
            System.Diagnostics.Process.Start("Document\\history.doc");
        }

        private void HelpDocument_Click(object sender, EventArgs e)
        {
            System.Diagnostics.Process.Start("Document\\readme.doc");
        }

        private void RobotStatusPic_Click(object sender, EventArgs e)
        {
            /*
            //用一张图片来表示所有情况
            Point Point_PowerOff = new Point(760, 535);
            Point Point_Ready = new Point(680, 535);
            */
        }

        private void button1_Click(object sender, EventArgs e)
        {

        }

        private void ShowErrorMsg(string csMessage, int nErrorNum)
        {
            string errorMsg;
            if (nErrorNum == 0)
            {
                errorMsg = csMessage;
            }
            else
            {
                errorMsg = csMessage + ": Error =" + String.Format("{0:X}", nErrorNum);
            }

            switch (nErrorNum)
            {
                case MyCamera.MV_E_HANDLE: errorMsg += " Error or invalid handle "; break;
                case MyCamera.MV_E_SUPPORT: errorMsg += " Not supported function "; break;
                case MyCamera.MV_E_BUFOVER: errorMsg += " Cache is full "; break;
                case MyCamera.MV_E_CALLORDER: errorMsg += " Function calling order error "; break;
                case MyCamera.MV_E_PARAMETER: errorMsg += " Incorrect parameter "; break;
                case MyCamera.MV_E_RESOURCE: errorMsg += " Applying resource failed "; break;
                case MyCamera.MV_E_NODATA: errorMsg += " No data "; break;
                case MyCamera.MV_E_PRECONDITION: errorMsg += " Precondition error, or running environment changed "; break;
                case MyCamera.MV_E_VERSION: errorMsg += " Version mismatches "; break;
                case MyCamera.MV_E_NOENOUGH_BUF: errorMsg += " Insufficient memory "; break;
                case MyCamera.MV_E_UNKNOW: errorMsg += " Unknown error "; break;
                case MyCamera.MV_E_GC_GENERIC: errorMsg += " General error "; break;
                case MyCamera.MV_E_GC_ACCESS: errorMsg += " Node accessing condition error "; break;
                case MyCamera.MV_E_ACCESS_DENIED: errorMsg += " No permission "; break;
                case MyCamera.MV_E_BUSY: errorMsg += " Device is busy, or network disconnected "; break;
                case MyCamera.MV_E_NETER: errorMsg += " Network error "; break;
            }

            MessageBox.Show(errorMsg, "PROMPT");
        }



        private void bnEnum_Click(object sender, EventArgs e)
        {
            DeviceListAcq();
        }


        private void DeviceListAcq()
        {
            int nRet;
            // ch:创建设备列表 en:Create Device List
            System.GC.Collect();
            cbDeviceList.Items.Clear();
            nRet = MyCamera.MV_CC_EnumDevices_NET(MyCamera.MV_GIGE_DEVICE | MyCamera.MV_USB_DEVICE, ref m_pDeviceList);
            if (0 != nRet)
            {
                ShowErrorMsg("Enumerate devices fail!", 0);
                return;
            }

            // ch:在窗体列表中显示设备名 | en:Display device name in the form list
            for (int i = 0; i < m_pDeviceList.nDeviceNum; i++)
            {
                MyCamera.MV_CC_DEVICE_INFO device = (MyCamera.MV_CC_DEVICE_INFO)Marshal.PtrToStructure(m_pDeviceList.pDeviceInfo[i], typeof(MyCamera.MV_CC_DEVICE_INFO));
                if (device.nTLayerType == MyCamera.MV_GIGE_DEVICE)
                {
                    IntPtr buffer = Marshal.UnsafeAddrOfPinnedArrayElement(device.SpecialInfo.stGigEInfo, 0);
                    MyCamera.MV_GIGE_DEVICE_INFO gigeInfo = (MyCamera.MV_GIGE_DEVICE_INFO)Marshal.PtrToStructure(buffer, typeof(MyCamera.MV_GIGE_DEVICE_INFO));
                    if (gigeInfo.chUserDefinedName != "")
                    {
                        cbDeviceList.Items.Add("GigE: " + gigeInfo.chUserDefinedName + " (" + gigeInfo.chSerialNumber + ")");
                    }
                    else
                    {
                        cbDeviceList.Items.Add("GigE: " + gigeInfo.chManufacturerName + " " + gigeInfo.chModelName + " (" + gigeInfo.chSerialNumber + ")");
                    }
                }
                else if (device.nTLayerType == MyCamera.MV_USB_DEVICE)
                {
                    IntPtr buffer = Marshal.UnsafeAddrOfPinnedArrayElement(device.SpecialInfo.stUsb3VInfo, 0);
                    MyCamera.MV_USB3_DEVICE_INFO usbInfo = (MyCamera.MV_USB3_DEVICE_INFO)Marshal.PtrToStructure(buffer, typeof(MyCamera.MV_USB3_DEVICE_INFO));
                    if (usbInfo.chUserDefinedName != "")
                    {
                        cbDeviceList.Items.Add("USB: " + usbInfo.chUserDefinedName + " (" + usbInfo.chSerialNumber + ")");
                    }
                    else
                    {
                        cbDeviceList.Items.Add("USB: " + usbInfo.chManufacturerName + " " + usbInfo.chModelName + " (" + usbInfo.chSerialNumber + ")");
                    }
                }
            }

            // ch:选择第一项 | en:Select the first item
            if (m_pDeviceList.nDeviceNum != 0)
            {
                cbDeviceList.SelectedIndex = 0;
            }
        }

        private void SetCtrlWhenOpen()
        {
            bnOpen.Enabled = false;

            bnClose.Enabled = true;
            bnStartGrab.Enabled = true;
            bnStopGrab.Enabled = false;
            bnContinuesMode.Enabled = true;
            bnContinuesMode.Checked = true;
            bnTriggerMode.Enabled = true;

        }
        private void bnOpen_Click(object sender, EventArgs e)
        {
            if (m_pDeviceList.nDeviceNum == 0 || cbDeviceList.SelectedIndex == -1)
            {
                ShowErrorMsg("No device, please select", 0);
                return;
            }
            int nRet = -1;

            // ch:获取选择的设备信息 | en:Get selected device information
            MyCamera.MV_CC_DEVICE_INFO device =
                (MyCamera.MV_CC_DEVICE_INFO)Marshal.PtrToStructure(m_pDeviceList.pDeviceInfo[cbDeviceList.SelectedIndex],
                                                              typeof(MyCamera.MV_CC_DEVICE_INFO));

            // ch:打开设备 | en:Open device
            if (null == m_pMyCamera)
            {
                m_pMyCamera = new MyCamera();
                if (null == m_pMyCamera)
                {
                    return;
                }
            }

            nRet = m_pMyCamera.MV_CC_CreateDevice_NET(ref device);
            if (MyCamera.MV_OK != nRet)
            {
                return;
            }

            nRet = m_pMyCamera.MV_CC_OpenDevice_NET();
            if (MyCamera.MV_OK != nRet)
            {
                m_pMyCamera.MV_CC_DestroyDevice_NET();
                ShowErrorMsg("Device open fail!", nRet);
                return;
            }

            // ch:探测网络最佳包大小(只对GigE相机有效) | en:Detection network optimal package size(It only works for the GigE camera)
            if (device.nTLayerType == MyCamera.MV_GIGE_DEVICE)
            {
                int nPacketSize = m_pMyCamera.MV_CC_GetOptimalPacketSize_NET();
                if (nPacketSize > 0)
                {
                    nRet = m_pMyCamera.MV_CC_SetIntValue_NET("GevSCPSPacketSize", (uint)nPacketSize);
                    if (nRet != MyCamera.MV_OK)
                    {
                        Console.WriteLine("Warning: Set Packet Size failed {0:x8}", nRet);
                    }
                }
                else
                {
                    Console.WriteLine("Warning: Get Packet Size failed {0:x8}", nPacketSize);
                }
            }

            // ch:设置采集连续模式 | en:Set Continues Aquisition Mode
            m_pMyCamera.MV_CC_SetEnumValue_NET("AcquisitionMode", 2);// ch:工作在连续模式 | en:Acquisition On Continuous Mode
            m_pMyCamera.MV_CC_SetEnumValue_NET("TriggerMode", 0);    // ch:连续模式 | en:Continuous

            //  bnGetParam_Click(null, null);// ch:获取参数 | en:Get parameters

            // ch:控件操作 | en:Control operation
            SetCtrlWhenOpen();
        }
        private void SetCtrlWhenClose()
        {
            bnOpen.Enabled = true;

            bnClose.Enabled = false;
            bnStartGrab.Enabled = false;
            bnStopGrab.Enabled = false;
            bnContinuesMode.Enabled = false;
            bnTriggerMode.Enabled = false;


        }
        private void bnClose_Click(object sender, EventArgs e)
        {
            // ch:关闭设备 | en:Close Device
            int nRet;

            nRet = m_pMyCamera.MV_CC_CloseDevice_NET();
            if (MyCamera.MV_OK != nRet)
            {
                return;
            }

            nRet = m_pMyCamera.MV_CC_DestroyDevice_NET();
            if (MyCamera.MV_OK != nRet)
            {
                return;
            }

            // ch:控件操作 | en:Control Operation
            SetCtrlWhenClose();

            // ch:取流标志位清零 | en:Reset flow flag bit
            m_bGrabbing = false;
        }
        private void SetCtrlWhenStartGrab()
        {
            bnStartGrab.Enabled = false;
            bnStopGrab.Enabled = true;




            bnSaveJpg.Enabled = true;
        }
        private void bnStartGrab_Click(object sender, EventArgs e)
        {
            int nRet;

            // ch:开始采集 | en:Start Grabbing
            nRet = m_pMyCamera.MV_CC_StartGrabbing_NET();
            if (MyCamera.MV_OK != nRet)
            {
                ShowErrorMsg("Trigger Fail!", nRet);
                return;
            }

            // ch:控件操作 | en:Control Operation
            SetCtrlWhenStartGrab();

            // ch:标志位置位true | en:Set position bit true
            m_bGrabbing = true;


            // ch:显示 | en:Display
            nRet = m_pMyCamera.MV_CC_Display_NET(pictureBox1.Handle);
            if (MyCamera.MV_OK != nRet)
            {
                ShowErrorMsg("Display Fail！", nRet);
            }
        }
        private void SetCtrlWhenStopGrab()
        {
            bnStartGrab.Enabled = true;
            bnStopGrab.Enabled = false;

            bnSaveJpg.Enabled = false;
        }
        private void bnStopGrab_Click(object sender, EventArgs e)
        {
            int nRet = -1;
            // ch:停止采集 | en:Stop Grabbing
            nRet = m_pMyCamera.MV_CC_StopGrabbing_NET();
            if (nRet != MyCamera.MV_OK)
            {
                ShowErrorMsg("Stop Grabbing Fail!", nRet);
            }

            // ch:标志位设为false | en:Set flag bit false
            m_bGrabbing = false;

            // ch:控件操作 | en:Control Operation
            SetCtrlWhenStopGrab();
        }

        private void bnSaveJpg_Click(object sender, EventArgs e)
        {
            DeleteImage();
            string ImageName = "Test.jpg";
            CaptureImage(ImageName);
        }

        private void DeleteImage()
        {
            string directoryPath = "SavedImages";
            System.IO.DirectoryInfo directory = new DirectoryInfo(directoryPath);
            foreach (System.IO.FileInfo file in directory.GetFiles()) file.Delete();
        }

        private void CaptureImage(string ImageName)
        {
            int nRet;
            UInt32 nPayloadSize = 0;
            MyCamera.MVCC_INTVALUE stParam = new MyCamera.MVCC_INTVALUE();
            nRet = m_pMyCamera.MV_CC_GetIntValue_NET("PayloadSize", ref stParam);
            if (MyCamera.MV_OK != nRet)
            {
                ShowErrorMsg("Get PayloadSize failed", nRet);
                return;
            }
            nPayloadSize = stParam.nCurValue;
            if (nPayloadSize > m_nBufSizeForDriver)
            {
                m_nBufSizeForDriver = nPayloadSize;
                m_pBufForDriver = new byte[m_nBufSizeForDriver];

                // ch:同时对保存图像的缓存做大小判断处理 | en:Determine the buffer size to save image
                // ch:BMP图片大小：width * height * 3 + 2048(预留BMP头大小) | en:BMP image size: width * height * 3 + 2048 (Reserved for BMP header)
                m_nBufSizeForSaveImage = m_nBufSizeForDriver * 3 + 2048;
                m_pBufForSaveImage = new byte[m_nBufSizeForSaveImage];
            }

            IntPtr pData = Marshal.UnsafeAddrOfPinnedArrayElement(m_pBufForDriver, 0);
            MyCamera.MV_FRAME_OUT_INFO_EX stFrameInfo = new MyCamera.MV_FRAME_OUT_INFO_EX();

            // ch:超时获取一帧，超时时间为1秒 | en:Get one frame timeout, timeout is 1 sec
            nRet = m_pMyCamera.MV_CC_GetOneFrameTimeout_NET(pData, m_nBufSizeForDriver, ref stFrameInfo, 1000);
            if (MyCamera.MV_OK != nRet)
            {
                ShowErrorMsg("No Data!", nRet);
                return;
            }

            IntPtr pImage = Marshal.UnsafeAddrOfPinnedArrayElement(m_pBufForSaveImage, 0);
            MyCamera.MV_SAVE_IMAGE_PARAM_EX stSaveParam = new MyCamera.MV_SAVE_IMAGE_PARAM_EX();
            stSaveParam.enImageType = MyCamera.MV_SAVE_IAMGE_TYPE.MV_Image_Jpeg;
            stSaveParam.enPixelType = stFrameInfo.enPixelType;
            stSaveParam.pData = pData;
            stSaveParam.nDataLen = stFrameInfo.nFrameLen;
            stSaveParam.nHeight = stFrameInfo.nHeight;
            stSaveParam.nWidth = stFrameInfo.nWidth;
            stSaveParam.pImageBuffer = pImage;
            stSaveParam.nBufferSize = m_nBufSizeForSaveImage;
            stSaveParam.nJpgQuality = 80;

            nRet = m_pMyCamera.MV_CC_SaveImageEx_NET(ref stSaveParam);
            if (MyCamera.MV_OK != nRet)
            {
                ShowErrorMsg("Save Fail!", 0);
                return;
            }


            try
            {
                FileStream file = new FileStream("SavedImages\\" + ImageName, FileMode.Create, FileAccess.Write);
                file.Write(m_pBufForSaveImage, 0, (int)stSaveParam.nImageLen);
                file.Close();
            }
            catch
            {
            }
            ShowErrorMsg("Save Succeed!", 0);
        }

        private void bnMoveStepOne_Click(object sender, EventArgs e)
        {
            double x = URDateHandle.Positions_X;
            double y = URDateHandle.Positions_Y;
            double z = URDateHandle.Positions_Z;
            double rx = URDateHandle.Positions_U;
            double ry = URDateHandle.Positions_V;
            double rz = URDateHandle.Positions_W;

            IntPtr CommandMovePtr = getTheMoveCommand(x, y, z, rx, ry, rz);
            string CommandMove = Marshal.PtrToStringAnsi(CommandMovePtr);
            string[] CommandMoveThree = CommandMove.Split('#');

            CustomCommand.Text = CommandMoveThree[0];
            dx1 = System.Convert.ToDouble(CommandMoveThree[1]);
            dy1 = System.Convert.ToDouble(CommandMoveThree[2]);
        }

        private void bnCaptureTwoImages_Click(object sender, EventArgs e)
        {
            DeleteImage();//先删除原本有的图片

            string ImageNameOne = "1.jpg";
            CaptureImage(ImageNameOne);

        }

        private void bnYZMoveOneCm_Click(object sender, EventArgs e)
        {
            //不管怎么样都要获取当前的坐标值
            double new_X = URDateHandle.Positions_X;
            double new_Y = URDateHandle.Positions_Y;
            double new_Z = URDateHandle.Positions_Z;
            double new_U = URDateHandle.Positions_U;
            double new_V = URDateHandle.Positions_V;
            double new_W = URDateHandle.Positions_W;

            new_Y -= 0.01;
            new_Z -= 0.01;

            //最后把方向运动的指令发送出去
            string command = "movel(p[" + new_X.ToString("0.0000") + "," + new_Y.ToString("0.0000") + "," + new_Z.ToString("0.0000") + "," + new_U.ToString("0.0000") + "," + new_V.ToString("0.0000") + "," + new_W.ToString("0.0000") + "], a = " + AccelerationRate.ToString() + ", v = " + SpeedRate.ToString() + ")";
            CustomCommand.Text = command;

        }

        private void bnCaptureOneImage_Click(object sender, EventArgs e)
        {
            DeleteImage();

            string ImageNameOne = "1.jpg";
            CaptureImage(ImageNameOne);
        }

        private void bnMoveToCenter_Click(object sender, EventArgs e)
        {
            double x = URDateHandle.Positions_X;
            double y = URDateHandle.Positions_Y;
            double z = URDateHandle.Positions_Z;
            double rx = URDateHandle.Positions_U;
            double ry = URDateHandle.Positions_V;
            double rz = URDateHandle.Positions_W;

            IntPtr CommandMovePtr = getTheMoveCommandSecond(x, y, z, rx, ry, rz, dx1, dy1);
            string CommandMove = Marshal.PtrToStringAnsi(CommandMovePtr);
            CustomCommand.Text = CommandMove;
        }

        private void CaptureImageTwoOfTwo_Click(object sender, EventArgs e)
        {
            string ImageNameTwo = "2.jpg";
            CaptureImage(ImageNameTwo);
        }

        private void bnMoveX_Click(object sender, EventArgs e)
        {
            //不管怎么样都要获取当前的坐标值
            double x = URDateHandle.Positions_X;
            double y = URDateHandle.Positions_Y;
            double z = URDateHandle.Positions_Z;
            double rx = URDateHandle.Positions_U;
            double ry = URDateHandle.Positions_V;
            double rz = URDateHandle.Positions_W;

            IntPtr CommandMovePtr = getTheMoveCommandX(x, y, z, rx, ry, rz);
            string CommandMove = Marshal.PtrToStringAnsi(CommandMovePtr);
            CustomCommand.Text = CommandMove;
        }

        //子集
        //public class Property
        //{
        //    public string qr_id;
        //    public string command;
        //} 
        //public class Point
        //{
        //    public string point_id;
        //    public string qr_id;
        //    public string command;
        //    //public Property point_property=new Property();

        //}
        private void button2_Click(object sender, EventArgs e)
        {

            FinalX_d.Text = (System.Convert.ToDouble(initialx.Text) - URDateHandle.Positions_X).ToString("0.0000");
            FinalY_d.Text = (System.Convert.ToDouble(initialy.Text) - URDateHandle.Positions_Y).ToString("0.0000");
            FinalZ_d.Text = (System.Convert.ToDouble(initialz.Text) - URDateHandle.Positions_Z).ToString("0.0000");
            FinalU_d.Text = (System.Convert.ToDouble(initialrx.Text) - URDateHandle.Positions_U).ToString("0.0000");
            FinalV_d.Text =(System.Convert.ToDouble(initialry.Text) - URDateHandle.Positions_V).ToString("0.0000");
            FinalW_d.Text = (System.Convert.ToDouble(initialrz.Text) - URDateHandle.Positions_W).ToString("0.0000");

            string point_id = Point_ID.Text;
            string x = initialx.Text;
            string y = initialy.Text;
            string z = initialz.Text;
            string rx = initialrx.Text;
            string ry = initialry.Text;
            string rz = initialrz.Text;
            string acceleration = Add_Value.Text;
            string speed = Speed_Value.Text;
            string finalx_d = FinalX_d.Text;
            string finaly_d = FinalY_d.Text;
            string finalz_d = FinalZ_d.Text;
            string finalrx_d = FinalU_d.Text;
            string finalry_d = FinalV_d.Text;
            string finalrz_d = FinalW_d.Text;


            string command = x + "#" + y + "#" + z + "#" + rx + "#" + ry + "#" + rz + "#" + finalx_d + "#" + finaly_d + "#" + finalz_d + "#" + finalrx_d + "#" + finalry_d + "#" + finalrz_d ;

            string jsonText_write = point_id + "#" + command ;
            CustomCommand.Text = jsonText_write;
            using (FileStream fs = new FileStream("Data\\test.json", FileMode.Append))
            {
                //写入
                using (StreamWriter sw = new StreamWriter(fs))
                {
                    sw.WriteLine(jsonText_write);
                }

            }

        }

        //private void button1_Click_1(object sender, EventArgs e)
        //{
        //    //把机械臂的位置信息显示在文本框
        //    initialx.Text = URDateHandle.Positions_X.ToString("0.0000");
        //    initialy.Text = URDateHandle.Positions_Y.ToString("0.0000");
        //    initialz.Text = URDateHandle.Positions_Z.ToString("0.0000");
        //    initialrx.Text = URDateHandle.Positions_U.ToString("0.0000");
        //    initialry.Text = URDateHandle.Positions_V.ToString("0.0000");
        //    initialrz.Text = URDateHandle.Positions_W.ToString("0.0000");
        //    Add_Value.Text = "0.15";
        //    Speed_Value.Text = "0.15";
        //}

        private void Auto_alignment_Click(object sender, EventArgs e)
        {
            //第一次截图
            DeleteImage();//先删除原本有的图片
            string ImageNameOne = "1.jpg";
            CaptureImage(ImageNameOne);

            //yz轴移动1cm
            //不管怎么样都要获取当前的坐标值
            double new_X = URDateHandle.Positions_X;
            double new_Y = URDateHandle.Positions_Y;
            double new_Z = URDateHandle.Positions_Z;
            double new_U = URDateHandle.Positions_U;
            double new_V = URDateHandle.Positions_V;
            double new_W = URDateHandle.Positions_W;

            new_Y += 0.01;
            new_Z -= 0.01;

            //最后把方向运动的指令发送出去
            string command = "movel(p[" + new_X.ToString("0.0000") + "," + new_Y.ToString("0.0000") + "," + new_Z.ToString("0.0000") + "," + new_U.ToString("0.0000") + "," + new_V.ToString("0.0000") + "," + new_W.ToString("0.0000") + "], a = " + AccelerationRate.ToString() + ", v = " + SpeedRate.ToString() + ")";
            URController.Send_command(command);

            Thread.Sleep(5000);

            //capture the second image
            string ImageNameTwo = "2.jpg";
            CaptureImage(ImageNameTwo);

            //Move to center step one
            double x = URDateHandle.Positions_X;
            double y = URDateHandle.Positions_Y;
            double z = URDateHandle.Positions_Z;
            double rx = URDateHandle.Positions_U;
            double ry = URDateHandle.Positions_V;
            double rz = URDateHandle.Positions_W;

            IntPtr CommandMovePtr = getTheMoveCommand(x, y, z, rx, ry, rz);
            string CommandMove = Marshal.PtrToStringAnsi(CommandMovePtr);
            string[] CommandMoveThree = CommandMove.Split('#');

            URController.Send_command(CommandMoveThree[0]);//send move command

            dx1 = System.Convert.ToDouble(CommandMoveThree[1]);
            dy1 = System.Convert.ToDouble(CommandMoveThree[2]);

            Thread.Sleep(5000);

            DeleteImage();

            string ImageNameOneStepTwo = "1.jpg";
            CaptureImage(ImageNameOneStepTwo);

            //Move to center step two
            x = URDateHandle.Positions_X;
            y = URDateHandle.Positions_Y;
            z = URDateHandle.Positions_Z;
            rx = URDateHandle.Positions_U;
            ry = URDateHandle.Positions_V;
            rz = URDateHandle.Positions_W;

            IntPtr CommandMovePtrStepTwo = getTheMoveCommandSecond(x, y, z, rx, ry, rz, dx1, dy1);
            string CommandMoveStepTwo = Marshal.PtrToStringAnsi(CommandMovePtrStepTwo);
            URController.Send_command(CommandMoveStepTwo);//send move command
        }

        private void poweron_Click(object sender, EventArgs e)
        {
            //机械臂上电
            string poweron_str = "power on";
            URPoweron.Send_command(poweron_str);
        }

        private void start_Click(object sender, EventArgs e)
        {
            //机械臂启动
            string start_str = "brake release";
            URPoweron.Send_command(start_str);
        }

        private void stop_Click(object sender, EventArgs e)
        {
            //机械臂急停
            string stop_str = "stopl(1)";
            URController.Send_command(stop_str);
        }

  

        private void shut_down_Click(object sender, EventArgs e)
        {
            //控制器断电
            string shut_down = "shutdown";
            URPoweron.Send_command(shut_down);
        }

        private void poweroff_Click(object sender, EventArgs e)
        {
            //机械臂断电
            string poweroff_str = "power off";
            URPoweron.Send_command(poweroff_str);
        }


        private void button3_Click_1(object sender, EventArgs e)
        {
            string poweroff_str = "unlock protective stop";
            URPoweron.Send_command(poweroff_str);
        }

        private void Initial_Position_Click_1(object sender, EventArgs e)
        {
            //机械臂复位
            string Resetcommandone = "movel(p[" + (URDateHandle.Positions_X + 0.05).ToString("0.0000") + "," + (URDateHandle.Positions_Y - 0.05).ToString("0.0000") + "," + URDateHandle.Positions_Z.ToString("0.0000") + "," + URDateHandle.Positions_U.ToString("0.0000") + "," + URDateHandle.Positions_V.ToString("0.0000") + "," + URDateHandle.Positions_W.ToString("0.0000") + "], a = " + AccelerationRate.ToString() + ", v = " + SpeedRate.ToString() + ")";
            URController.Send_command(Resetcommandone);
            Thread.Sleep(3000);
            string Resetcommandtwo = "movel(p[-0.3124,-0.1686,0.4468,-1.497,-0.618,0.763], a = 0.15, v = 0.15)";
            URController.Send_command(Resetcommandtwo);
            Thread.Sleep(5000);
            string Resetcommandthree = "movel(p[-0.1881,0.0258,0.5247,-0.135,-0.056,0.817], a = 0.15, v = 0.15)";
            URController.Send_command(Resetcommandthree);
        }

        private void button3_Click(object sender, EventArgs e)
        {
            Commandtransmit.Creat_client("127.0.0.1", 4332);
            Commandtransmit.Send_command("10kVF2##（702）开关柜分闸、合闸指示");
        }

        private void cancelTask_Click(object sender, EventArgs e)
        {
            Commandtransmit.Creat_client("192.168.1.10", 4333);
            Commandtransmit.Send_command("cancel");
        }

      

       
    }
}
