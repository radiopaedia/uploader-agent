using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


public static class LOG
{
    public static void Write(string msg)
    {
        var nowstring = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        Console.WriteLine(nowstring + " - " + msg);
        try
        {
            if (!System.IO.Directory.Exists(@".\Logs"))
                System.IO.Directory.CreateDirectory(@".\Logs");
            string logFileName = System.IO.Path.Combine(@".\Logs", System.DateTime.Now.ToString("yyyy-MM-dd") + ".txt");
            if (!System.IO.File.Exists(logFileName))
                DeleteOldLogs(7);
            using (System.IO.StreamWriter file = new System.IO.StreamWriter(logFileName, true))
                file.WriteLine(nowstring + " - " + msg);
        }
        catch (Exception ex)
        {
            Console.WriteLine(nowstring + " - " + msg);
            Console.WriteLine(nowstring + " - " + ex.Message);
        }
    }

    public static void DeleteOldLogs(int daysToKeep)
    {
        var nowstring = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        try
        {
            if (System.IO.Directory.Exists(@".\Logs"))
            {
                var logFiles = System.IO.Directory.GetFiles(@".\Logs", "*.txt");
                if (logFiles != null && logFiles.Length > 0)
                {
                    foreach (var l in logFiles)
                    {
                        DateTime dt = new DateTime();
                        string getFileName = System.IO.Path.GetFileNameWithoutExtension(l);
                        bool testDtBool = DateTime.TryParseExact(getFileName, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out dt);
                        if (testDtBool)
                        {
                            if ((DateTime.Today - dt.Date).TotalDays > daysToKeep)
                            {
                                System.IO.File.Delete(l);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(nowstring + " - " + ex.Message);
        }
    }

    public static DateTime RetrieveLinkerTimestamp()
    {
        string filePath = System.Reflection.Assembly.GetCallingAssembly().Location;
        const int c_PeHeaderOffset = 60;
        const int c_LinkerTimestampOffset = 8;
        byte[] b = new byte[2048];
        System.IO.Stream s = null;

        try
        {
            s = new System.IO.FileStream(filePath, System.IO.FileMode.Open, System.IO.FileAccess.Read);
            s.Read(b, 0, 2048);
        }
        finally
        {
            if (s != null)
            {
                s.Close();
            }
        }

        int i = System.BitConverter.ToInt32(b, c_PeHeaderOffset);
        int secondsSince1970 = System.BitConverter.ToInt32(b, i + c_LinkerTimestampOffset);
        DateTime dt = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        dt = dt.AddSeconds(secondsSince1970);
        dt = dt.ToLocalTime();
        return dt;
    }

    public static void InsertEvent(string msg, string type, string data = null, string dcase = null, string dstudy = null, string dseries = null)
    {
        try
        {
            Event e = new Event();
            e.Type = type;
            e.Message = msg;
            e.TimeStamp = DateTime.Now;
            e.Data = data;
            if (dcase != null)
            {
                e.InternalId = dcase;
            }
            if (dstudy != null)
            {
                e.StudyUid = dstudy;
            }
            if (dseries != null)
            {
                e.SeriesUid = dseries;
            }
            APetaPoco.SetConnectionString("cn1");
            var bm = APetaPoco.PpInsert(e);
            if (!bm.Success) { LOG.Write(bm.Message); }
        }
        catch (Exception ex)
        {
            string errorString = "Error at :" + System.Reflection.MethodBase.GetCurrentMethod().Name;
            LOG.Write(errorString);
            LOG.Write(ex.Message);
        }
        
    }
}
[PetaPoco.TableName("Events")]
public class Event
{
    public int Id { set; get; }
    public DateTime TimeStamp { set; get; }
    public string Type { set; get; }
    public string InternalId { set; get; }
    public string StudyUid { set; get; }
    public string SeriesUid { set; get; }
    public string Message { set; get; }
    public string Data { set; get; }
}

