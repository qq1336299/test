using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

using URControl;

namespace UR_点动控制器
{
    public partial class Form2 : Form
    {
        public Form1 myform1;
        public Form2()
        {
            InitializeComponent();
            Form.CheckForIllegalCrossThreadCalls = false;

            point_id.Text = GlobalData.ID;
            status.Text = GlobalData.Status;
            try
            { 
                //pictureBox1.Load(GlobalData.Imagepath); 
            }
            catch
            {
               
            }
            
        }

        URControlHandle Commandtransmit = new URControlHandle();
       
        private void confirm_Click(object sender, EventArgs e)
        {
            GlobalData.User_Operating = "confirm";
            //Commandtransmit.Creat_client("127.0.0.1", 4333);
            ////发送确认消息后等待下位机返回信息
            //GlobalData.back_message = Commandtransmit.Send_command_WithFeedback("confirm##" + GlobalData.ID + "##" + GlobalData.Status + "##");
            //myform1.socket.Send(Encoding.UTF8.GetBytes(GlobalData.back_message));
            //string str = GlobalData.back_message;
            this.Close();
           
        }

        private void cancel_Click(object sender, EventArgs e)
        {
            GlobalData.User_Operating = "cancel";
            //myform1.CancelOperation();
            this.Close();
        }
    }
}
