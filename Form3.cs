using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

using System.IO;
using System.Text.RegularExpressions;
using System.Collections;
using Microsoft.Office.Core;
using Excel = Microsoft.Office.Interop.Excel;

namespace UR_点动控制器
{
    public partial class Form3 : Form
    {
        public Form3()
        {
            InitializeComponent();
        }

        private void dateTimePicker1_ValueChanged(object sender, EventArgs e)
        {
            DateTime dt1 = dateTimePicker1.Value;
            DateTime dt2 = dateTimePicker2.Value;
            if (dt1 > dt2)
            {
                MessageBox.Show("开始时间不可大于结束时间！");
            }
        }

        private void dateTimePicker2_ValueChanged(object sender, EventArgs e)
        {
            DateTime dt1 = dateTimePicker1.Value;
            DateTime dt2 = dateTimePicker2.Value;
            if (dt1 > dt2)
            {
                MessageBox.Show("结束时间不可小于起始时间！");
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            //新建一个datatable用于保存读入的数据
            DataTable dt = new DataTable();
            //给datatable添加8个列标题
            dt.Columns.Add("任务发送时间", typeof(String));
            dt.Columns.Add("任务名称", typeof(String));
            dt.Columns.Add("任务数量", typeof(String));
            dt.Columns.Add("点位ID", typeof(String));
            dt.Columns.Add("点位名称", typeof(String));
            dt.Columns.Add("点位操作时间", typeof(String));
            dt.Columns.Add("图片路径", typeof(String));
            dt.Columns.Add("操作结果", typeof(String));
            dt.Columns.Add("分合闸指示", typeof(String));
            dt.Columns.Add("位置指示", typeof(String));

            //指定查询log
            string logpath = @"D:\guozi\URController V1.7_Up\bin\Debug\Data\task.log";

            DateTime dt1 = dateTimePicker1.Value;
            DateTime dt2 = dateTimePicker2.Value;
            string pt1, pt2;
            //按照 年 月 日 提取dateTimePicker1的时间,即查询起始时间
            pt1 = dt1.ToString("yyyyMMddHHmm");
            long p1 = long.Parse(pt1.Substring(0, 4));
            long p2 = long.Parse(pt1.Substring(4, 2));
            long p3 = long.Parse(pt1.Substring(6, 2));
            long ptime1 = p1 * 10000 + p2 * 100 + p3;
            //按照 年 月 日 提取dateTimePicker2的时间,即查询结束时间
            pt2 = dt2.ToString("yyyyMMddHHmm");
            long P1 = long.Parse(pt2.Substring(0, 4));
            long P2 = long.Parse(pt2.Substring(4, 2));
            long P3 = long.Parse(pt2.Substring(6, 2));
            long ptime2 = P1 * 10000 + P2 * 100 + P3;

            //Read the file and display it line by line.
            System.IO.StreamReader log = new System.IO.StreamReader(logpath, System.Text.Encoding.Default);
            string line = null;
            line = log.ReadLine();
            while (line!=null)
            {
                //将log中每行的前几个表示时间的字符按照 年 月 日 提取出来用于与dateTimePicker2的时间(结束时间)作比较
                string logy = line.Substring(0, 4);
                string logM = line.Substring(5, 2);
                string logd = line.Substring(8, 2);
                long t1 = long.Parse(logy);
                long t2 = long.Parse(logM);
                long t3 = long.Parse(logd);
                long ttime = t1 * 10000 + t2 * 100 + t3;

                if ((ttime >= ptime1) && (ttime <= ptime2))
                {
                    //将每行数据，按照“*”进行分割
                    string[] data = line.Split('*');
                    //新建一行，并将读出的数据分段，分别存入8个对应的列中
                    DataRow dr = dt.NewRow();
                    dr[0] = data[0];
                    dr[1] = data[1];
                    dr[2] = data[2];
                    dr[3] = data[3];
                    dr[4] = data[4];
                    dr[5] = data[5];
                    dr[6] = data[6];
                    dr[7] = data[7];
                    dr[8] = data[8];
                    dr[9] = data[9];
                    //将这行数据加入到datatable中
                    dt.Rows.Add(dr);
                }
                line = log.ReadLine();
            }
            log.Close();
            //将datatable绑定到datagridview上显示结果
            this.dataGridView1.DataSource = dt;
            ////删除第一行
            //this.dataGridView1.Rows.RemoveAt(0);
            ////行头隐藏
            //this.dataGridView1.RowHeadersVisible = false;
        }

        private void button2_Click(object sender, EventArgs e)
        {
            DataTable dt = (DataTable)dataGridView1.DataSource;
            dt.Rows.Clear();
            dataGridView1.DataSource = dt;
        }

        private void button3_Click(object sender, EventArgs e)
        {
            ExportToExcel d = new ExportToExcel();
            d.OutputAsExcelFile(dataGridView1);
        }

        //将dataGridView中显示的数据导出到Excel
        //创建类
        public class ExportToExcel
        {
            public Excel.Application m_xlApp = null;

            public void OutputAsExcelFile(DataGridView dataGridView)
            {
                if (dataGridView.Rows.Count <= 0)
                {
                    MessageBox.Show("无数据！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning); return;
                }
                string filePath = "";
                SaveFileDialog s = new SaveFileDialog();
                s.Title = "保存Excel文件";
                s.Filter = "Excel文件(*.xls)|*.xls";
                s.FilterIndex = 1;
                if (s.ShowDialog() == DialogResult.OK)
                    filePath = s.FileName;
                else
                    return;

                //第一步：将dataGridView转化为dataTable,这样可以过滤掉dataGridView中的隐藏列
                DataTable tmpDataTable = new DataTable("tmpDataTable");
                DataTable modelTable = new DataTable("ModelTable");
                for (int column = 0; column < dataGridView.Columns.Count; column++)
                {
                    if (dataGridView.Columns[column].Visible == true)
                    {
                        DataColumn tempColumn = new DataColumn(dataGridView.Columns[column].HeaderText, typeof(string));
                        tmpDataTable.Columns.Add(tempColumn);
                        DataColumn modelColumn = new DataColumn(dataGridView.Columns[column].Name, typeof(string));
                        modelTable.Columns.Add(modelColumn);
                    }
                }
                for (int row = 0; row < dataGridView.Rows.Count; row++)
                {
                    if (dataGridView.Rows[row].Visible == false)
                        continue;
                    DataRow tempRow = tmpDataTable.NewRow();
                    for (int i = 0; i < tmpDataTable.Columns.Count; i++)
                        tempRow[i] = dataGridView.Rows[row].Cells[modelTable.Columns[i].ColumnName].Value;
                    tmpDataTable.Rows.Add(tempRow);
                }
                if (tmpDataTable == null)
                {
                    return;
                }

                //第二步：导出dataTable到Excel
                long rowNum = tmpDataTable.Rows.Count;//行数
                int columnNum = tmpDataTable.Columns.Count;//列数
                Excel.Application m_xlApp = new Excel.Application();
                m_xlApp.DisplayAlerts = false;//不显示更改提示
                m_xlApp.Visible = false;

                Excel.Workbooks workbooks = m_xlApp.Workbooks;
                Excel.Workbook workbook = workbooks.Add(Excel.XlWBATemplate.xlWBATWorksheet);
                Excel.Worksheet worksheet = (Excel.Worksheet)workbook.Worksheets[1];//取得sheet1

                try
                {
                    string[,] datas = new string[rowNum + 1, columnNum];
                    for (int i = 0; i < columnNum; i++) //写入字段
                        datas[0, i] = tmpDataTable.Columns[i].Caption;
                    //Excel.Range range = worksheet.get_Range(worksheet.Cells[1, 1], worksheet.Cells[1, columnNum]);
                    Excel.Range range = m_xlApp.Range[worksheet.Cells[1, 1], worksheet.Cells[1, columnNum]];
                    range.Interior.ColorIndex = 15;//15代表灰色
                    range.Font.Bold = true;
                    range.Font.Size = 10;

                    int r = 0;
                    for (r = 0; r < rowNum; r++)
                    {
                        for (int i = 0; i < columnNum; i++)
                        {
                            object obj = tmpDataTable.Rows[r][tmpDataTable.Columns[i].ToString()];
                            datas[r + 1, i] = obj == null ? "" : "'" + obj.ToString().Trim();//在obj.ToString()前加单引号是为了防止自动转化格式
                        }
                        System.Windows.Forms.Application.DoEvents();
                        //添加进度条
                    }
                    //Excel.Range fchR = worksheet.get_Range(worksheet.Cells[1, 1], worksheet.Cells[rowNum + 1, columnNum]);
                    Excel.Range fchR = m_xlApp.Range[worksheet.Cells[1, 1], worksheet.Cells[rowNum + 1, columnNum]];
                    fchR.Value2 = datas;

                    worksheet.Columns.EntireColumn.AutoFit();//列宽自适应。
                    //worksheet.Name = "dd";

                    //m_xlApp.WindowState = Excel.XlWindowState.xlMaximized;
                    m_xlApp.Visible = false;
                    // = worksheet.get_Range(worksheet.Cells[1, 1], worksheet.Cells[rowNum + 1, columnNum]);
                    range = m_xlApp.Range[worksheet.Cells[1, 1], worksheet.Cells[rowNum + 1, columnNum]];

                    //range.Interior.ColorIndex = 15;//15代表灰色
                    range.Font.Size = 9;
                    range.RowHeight = 14.25;
                    range.Borders.LineStyle = 1;
                    range.HorizontalAlignment = 1;
                    workbook.Saved = true;
                    workbook.SaveCopyAs(filePath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("导出异常：" + ex.Message, "导出异常", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                finally
                {
                    EndReport();
                }

                m_xlApp.Workbooks.Close();
                m_xlApp.Workbooks.Application.Quit();
                m_xlApp.Application.Quit();
                m_xlApp.Quit();
                MessageBox.Show("导出成功！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            private void EndReport()
            {
                object missing = System.Reflection.Missing.Value;
                try
                {
                    //m_xlApp.Workbooks.Close();
                    //m_xlApp.Workbooks.Application.Quit();
                    //m_xlApp.Application.Quit();
                    //m_xlApp.Quit();
                }
                catch { }
                finally
                {
                    try
                    {
                        System.Runtime.InteropServices.Marshal.ReleaseComObject(m_xlApp.Workbooks);
                        System.Runtime.InteropServices.Marshal.ReleaseComObject(m_xlApp.Application);
                        System.Runtime.InteropServices.Marshal.ReleaseComObject(m_xlApp);
                        m_xlApp = null;
                    }
                    catch { }
                    try
                    {
                        //清理垃圾进程
                        this.killProcessThread();
                    }
                    catch { }
                    GC.Collect();
                }
            }

            private void killProcessThread()
            {
                ArrayList myProcess = new ArrayList();
                for (int i = 0; i < myProcess.Count; i++)
                {
                    try
                    {
                        System.Diagnostics.Process.GetProcessById(int.Parse((string)myProcess[i])).Kill();
                    }
                    catch { }
                }
            }
        }

        private void dataGridView1_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            //加载选中路径的图片
            string imgpath = dataGridView1.CurrentCell.Value.ToString();
            this.pictureBox1.Load(imgpath);
        }



    }
}
