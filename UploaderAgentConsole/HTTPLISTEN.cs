using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Net;
using Grapevine.Server;
using Grapevine;

class HTTPLISTEN
{
    public void StartServer()
    {
        LOG.Write("Starting HTTP.");
        var server = new RESTServer();
        server.Start();

        while (server.IsListening)
        {
            System.Threading.Thread.Sleep(300);
        }
    }
}
public sealed class MyResource : RESTResource
{
    [RESTRoute]
    public void HandleAllGetRequests(HttpListenerContext context)
    {
        var rc = new ResponseClass();
        string studyuid = context.Request.QueryString["studyuid"];
        string seriesuid = context.Request.QueryString["seriesuid"];
        string check = context.Request.QueryString["check"];
        if (!string.IsNullOrEmpty(check))
        {
            rc.Success = true;
            rc.Message = "";
            rc.Data = true;
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(rc);
            this.SendTextResponse(context, json);
            return;
        }
        var node = ADCM.GetSelectedNode();
        try
        {
            LOG.Write("New request");
            if (string.IsNullOrEmpty(studyuid) || string.IsNullOrEmpty(seriesuid)) { throw new Exception("No studyuid or seriesuid provided"); }
            bool downloaded = ADCM.DownloadOneSeries(studyuid, seriesuid);
            if (!downloaded) { throw new Exception("Unable to download study"); }
            string seriesPath = Path.Combine(node.LocalStorage, studyuid, seriesuid);
            if (!Directory.Exists(seriesPath)) { throw new Exception("Series path not found: " + seriesPath); }
            var dcmFiles = Directory.GetFiles(seriesPath, "*.dcm");
            string filetouse = null;
            decimal mid = dcmFiles.Length / 2;
            int index = (int)Math.Ceiling(mid);
            for (int i = index; i < dcmFiles.Length; i++)
            {
                var dcm = dcmFiles[i];
                ClearCanvas.Dicom.DicomFile dcmFile = new ClearCanvas.Dicom.DicomFile(dcm);
                dcmFile.Load();
                ClearCanvas.ImageViewer.StudyManagement.LocalSopDataSource localds = new ClearCanvas.ImageViewer.StudyManagement.LocalSopDataSource(dcmFile);
                if (!localds.IsImage) { continue; }
                else
                {
                    filetouse = dcm;
                    break;
                }
            }
            if (string.IsNullOrEmpty(filetouse))
            {
                for (int i = 0; i < dcmFiles.Length; i++)
                {
                    var dcm = dcmFiles[i];
                    ClearCanvas.Dicom.DicomFile dcmFile = new ClearCanvas.Dicom.DicomFile(dcm);
                    dcmFile.Load();
                    ClearCanvas.ImageViewer.StudyManagement.LocalSopDataSource localds = new ClearCanvas.ImageViewer.StudyManagement.LocalSopDataSource(dcmFile);
                    if (!localds.IsImage) { continue; }
                    else
                    {
                        filetouse = dcm;
                        break;
                    }
                }
            }
            if(string.IsNullOrEmpty(filetouse)) { throw new Exception("Unable to find image in downloaded DICOM files"); }
            if (!File.Exists(filetouse)) { throw new Exception("Unable to find DICOM file to use"); }
                        
            string base64String = Convert.ToBase64String(AIMG.GetImageBytesFromDcm(filetouse));
            base64String = "data:image/jpeg;base64," + base64String;
            rc.Success = true;
            rc.Message = "";
            rc.Data = base64String;
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(rc);            
            this.SendTextResponse(context, json);
        }
        catch (Exception ex)
        {
            LOG.Write("ERROR: " + ex.Message);
            rc.Data = null;
            rc.Success = false;
            rc.Message = ex.Message;
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(rc);            
            this.SendTextResponse(context, json);
        }
        finally
        {
            string studypath = Path.Combine(node.LocalStorage, studyuid);
            if (Directory.Exists(studypath))
            {
                Directory.Delete(studypath, true);
            }
        }
    }

}
public class ResponseClass
{
    public bool Success { set; get; }
    public string Message { set; get; }
    public object Data { set; get; }
}