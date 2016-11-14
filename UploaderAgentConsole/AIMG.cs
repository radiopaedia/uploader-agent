using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using ClearCanvas.Common;
using ClearCanvas.Dicom;
using ClearCanvas.Dicom.Iod;
using ClearCanvas.Dicom.Iod.Iods;
using ClearCanvas.Dicom.Network;
using ClearCanvas.Dicom.Network.Scu;
using ClearCanvas.ImageViewer;
using ClearCanvas.ImageViewer.StudyManagement;
using ClearCanvas.ImageViewer.Tools.Standard;
using ClearCanvas.ImageViewer.Imaging;
using ClearCanvas.ImageViewer.Common;
using ClearCanvas.ImageViewer.Configuration;

using System.IO;

using System.Drawing;
using System.Drawing.Imaging;

using Ionic.Zip;

public static class AIMG
{
    public static void MultiFrameProcess(DbStudy study)
    {
        string dcmPath = ADCM.GetStoreString();
        var seriesList = Directory.GetDirectories(Path.Combine(dcmPath, study.study_uid));
        foreach (var sePath in seriesList)
        {
            var filesList = Directory.GetFiles(sePath, "*.dcm");
            if (filesList.Length < 2)
                continue;
            for (int i = 0; i < filesList.Length; i++)
            {
                var dcm = new DicomFile(filesList[i]);
                dcm.Load();
                int frameCount = dcm.DataSet[DicomTags.NumberOfFrames].GetInt16(0, 0);                
                if (frameCount > 1)
                {
                    string newSeriesUID = sePath + "." + i;
                    newSeriesUID = newSeriesUID.Substring(newSeriesUID.LastIndexOf(Path.DirectorySeparatorChar) + 1);
                    string newSeriesPath = Path.Combine(dcmPath, study.study_uid, newSeriesUID);
                    Directory.CreateDirectory(newSeriesPath);
                    string fileName = Path.GetFileName(filesList[i]);
                    string oldPath = filesList[i];
                    string newPath = Path.Combine(newSeriesPath, fileName);
                    File.Move(filesList[i], Path.Combine(newSeriesPath, fileName));
                }
            }                        
        }
        foreach (string sePath in seriesList)
        {
            var filesCount = Directory.GetFiles(sePath);
            if (filesCount.Length < 1)
                Directory.Delete(sePath);
        }
    }
    public static bool ConvertDcmToPng(DbStudy study)
    {        
        try
        {
            string dcmPath = ADCM.GetStoreString();
            string studyPath = Path.Combine(dcmPath, study.study_uid);
            if (!Directory.Exists(studyPath))
                throw new Exception("Study path not found");
            var allSeriesPaths = Directory.GetDirectories(studyPath);
            if (allSeriesPaths.Length < 1)
                throw new Exception("No series subdirectories");

            foreach (var s in allSeriesPaths)
            {
                var dcmFiles = Directory.GetFiles(s, "*.dcm");
                if (dcmFiles.Length < 1)
                    throw new Exception("No DCM files inside series path: " + s);
                DicomFile tempdcm = new DicomFile(dcmFiles[0]);
                tempdcm.Load();
                var seriesName = tempdcm.DataSet[DicomTags.SeriesDescription].GetString(0, null);
                var seriesUID = s.Substring(s.LastIndexOf(Path.DirectorySeparatorChar) + 1);
                if (string.IsNullOrEmpty(seriesName))
                    seriesName = "Unamed_Series";

                APetaPoco.SetConnectionString("cn1");
                var bm = APetaPoco.PpRetrieveOne<DbSeries>("Series", "[case_id] = '" + study.case_id + "' AND [series_uid] = '" + seriesUID + "'");
                DbSeries dbseries = null;
                if (bm.Success) { dbseries = (DbSeries)bm.Data; }

                string outputPath = Path.Combine(dcmPath, "OUTPUT", study.case_id, study.study_uid, seriesUID);
                if (!Directory.Exists(outputPath))
                    Directory.CreateDirectory(outputPath);

                //int fileCount = 0;

                for (int k = 0; k < dcmFiles.Length; k++)
                {
                    DicomFile dcmFile = new DicomFile(dcmFiles[k]);
                    dcmFile.Load();
                    int fileCount = 0;
                    var windowWidth = dcmFile.DataSet[DicomTags.WindowWidth].ToString();
                    if (!string.IsNullOrEmpty(windowWidth))
                    {
                        var tempSplitString = windowWidth.Split('\\');
                        windowWidth = tempSplitString[0];
                    }
                    var windowCenter = dcmFile.DataSet[DicomTags.WindowCenter].ToString();
                    if (!string.IsNullOrEmpty(windowCenter))
                    {
                        var tempSplitString = windowCenter.Split('\\');
                        windowCenter = tempSplitString[0];
                    }
                    var ww = dcmFile.DataSet[DicomTags.WindowWidth].GetFloat32(0, 0);
                    var wc = dcmFile.DataSet[DicomTags.WindowCenter].GetFloat32(0, 0);

                    if (ww == 0 && !string.IsNullOrEmpty(windowWidth))
                    {
                        if (windowWidth.Contains("."))
                        {
                            var tempSplitString = windowWidth.Split('.');
                            ww = int.Parse(tempSplitString[0]);
                        }
                        else
                        {
                            ww = int.Parse(windowWidth);
                        }

                    }
                    if (wc == 0 && !string.IsNullOrEmpty(windowCenter))
                    {
                        if (windowCenter.Contains("."))
                        {
                            var tempSplitString = windowCenter.Split('.');
                            wc = int.Parse(tempSplitString[0]);
                        }
                        else
                        {
                            wc = int.Parse(windowCenter);
                        }

                    }

                    LocalSopDataSource localds = new LocalSopDataSource(dcmFile);
                    if (!localds.IsImage) { continue; }
                    ImageSop sop = new ImageSop(localds);
                    int frameCount = sop.Frames.Count;
                    var fileName = dcmFile.DataSet[DicomTags.InstanceNumber].GetInt16(0, 0);
                    if (frameCount > 1)
                    {
                        for (int j = 1; j <= frameCount; j++)
                        {
                            GC.Collect();
                            Frame f = sop.Frames[j];
                            var jpgPath = Path.Combine(outputPath, fileName + "." + j + ".png");
                            Bitmap bmp = null;
                            if (string.IsNullOrEmpty(windowWidth) || string.IsNullOrEmpty(windowCenter))
                            {
                                bmp = DrawDefaultFrame(f);
                            }
                            else
                            {
                                bmp = DrawLutFrame(f, ww, wc);
                            }
                            if (bmp != null)
                            {
                                if(dbseries != null && dbseries.crop_h != null
                                    && dbseries.crop_w != null
                                    && dbseries.crop_x != null
                                    && dbseries.crop_y != null)
                                {
                                    bmp = Crop(bmp, dbseries.crop_x.Value, dbseries.crop_y.Value, dbseries.crop_w.Value, dbseries.crop_h.Value);
                                }
                                SaveImage(bmp, jpgPath);
                            }
                            fileCount += 1;
                            GC.Collect();
                        }
                    }
                    else
                    {
                        GC.Collect();
                        var jpgPath = Path.Combine(outputPath, fileName + ".png");
                        Frame f = sop.Frames[1];
                        Bitmap bmp = null;
                        if (string.IsNullOrEmpty(windowWidth) || string.IsNullOrEmpty(windowCenter))
                        {
                            bmp = DrawDefaultFrame(f);
                        }
                        else
                        {
                            bmp = DrawLutFrame(f, ww, wc);
                        }
                        if (bmp != null)
                        {
                            if (dbseries != null && dbseries.crop_h != null
                                    && dbseries.crop_w != null
                                    && dbseries.crop_x != null
                                    && dbseries.crop_y != null)
                            {
                                bmp = Crop(bmp, dbseries.crop_x.Value, dbseries.crop_y.Value, dbseries.crop_w.Value, dbseries.crop_h.Value);
                            }
                            SaveImage(bmp, jpgPath);
                        }
                        fileCount += 1;
                        GC.Collect();
                    }
                }
            }
            LOG.InsertEvent("Successfully converted study from DCM to PNG", "IMG", null, study.case_id, study.study_uid);
            return true;
        }
        catch (Exception ex)
        {
            string errorString = "Error at :" + System.Reflection.MethodBase.GetCurrentMethod().Name;
            LOG.Write(errorString);
            LOG.Write(ex.Message);
            LOG.InsertEvent(errorString, "IMG", ex.Message, study.case_id, study.study_uid);
            return false;
        }
        
    }       
    public static void DeleteExcessImages(DbStudy study)
    {
        try
        {
            string dcmPath = ADCM.GetStoreString();
            string outputPath = Path.Combine(dcmPath, "OUTPUT", study.case_id, study.study_uid);
            if (!Directory.Exists(outputPath)) { throw new Exception("No output path found: " + outputPath); }
            foreach (var ser in study.series)
            {
                string serPath = Path.Combine(outputPath, ser.series_uid);
                if (Directory.Exists(serPath))
                {
                    var sortedList = Directory.GetFiles(serPath, "*.png").OrderBy(f => int.Parse(Path.GetFileNameWithoutExtension(f))).ToList();
                    if (sortedList.Count > ser.images)
                    {
                        int start = 1;
                        int end = ser.images;
                        int stepping = 1;
                        if (ser.start_image != null && ser.start_image > 0)
                            start = ser.start_image.Value;
                        if (ser.end_image != null && ser.end_image > 0)
                            end = ser.end_image.Value;
                        if (end > sortedList.Count)
                            end = sortedList.Count;
                        if (ser.every_image != null && ser.every_image > 0)
                            stepping = ser.every_image.Value;                        

                        List<string> keepPaths = new List<string>();

                        LOG.Write(string.Format("Start: {0}| End: {1}| Stepping: {2}", start, end, stepping));

                        for (int i = start - 1; i < end; i += stepping)
                        {
                            //LOG.Write(string.Format("[{0}] Keeping {1}", i, sortedList[i]));
                            keepPaths.Add(sortedList[i]);
                        }

                        foreach (var f in sortedList)
                        {
                            if (!keepPaths.Contains(f))
                            {
                                //LOG.Write(string.Format("Deleting {0}", f));
                                File.Delete(f);
                            }
                        }
                    }
                }
                else
                {
                    LOG.Write("No series path: " + serPath);
                }
            }
        }
        catch(Exception ex)
        {
            LOG.Write(ex.Message);
        }
        
    }
    private static List<string> SortNumberPaths(List<string> unsorted)
    {
        Dictionary<int, string> dict = new Dictionary<int, string>();
        foreach(var f in unsorted)
        {
            FileInfo fi = new FileInfo(f);
            int _int = int.Parse(fi.Name);
            dict.Add(_int, f);
        }
        var orderedList = dict.OrderBy(x => x.Key).ToList();
        List<string> orderedPaths = new List<string>();
        for(int i = 0; i < orderedList.Count; i++)
        {
            orderedPaths.Add(orderedList[i].Value);
        }
        return orderedPaths;
    }
    private static Bitmap Crop(Bitmap src, int x, int y, int w, int h)
    {
        int width = w;
        int height = h;
        if(w > src.Width) { width = src.Width; }
        if(h > src.Height) { height = src.Height; }
        Rectangle cropRect = new Rectangle(x, y, width, height);
        Bitmap target = new Bitmap(cropRect.Width, cropRect.Height);
        using (Graphics g = Graphics.FromImage(target))
        {
            g.DrawImage(src, new Rectangle(0, 0, target.Width, target.Height), cropRect, GraphicsUnit.Pixel);
        }
        return target;
    }
    private static Bitmap DrawDefaultFrame(Frame f)
    {
        var presentationImage = PresentationImageFactory.Create(f);
        var bitmap = presentationImage.DrawToBitmap(f.Columns, f.Rows);
        try
        {
            var bmp = presentationImage.DrawToBitmap(f.Columns, f.Rows);
            if (f.Columns == f.Rows)
            {
                if (f.Columns < 512)
                {
                    bmp = ResizeBitmap(bmp, 512, 512);
                }
            }
            return bmp;
        }
        catch { return null; }
    }
    private static Bitmap DrawLutFrame(Frame f, double ww, double wc)
    {
        IPresentationImage pres = PresentationImageFactory.Create(f);
        IVoiLutProvider provider = ((IVoiLutProvider)pres);
        IVoiLutManager manager = provider.VoiLutManager;
        var linearLut = manager.VoiLut as IVoiLutLinear;
        if (linearLut != null)
        {

            var standardLut = linearLut as IBasicVoiLutLinear;

            if (standardLut == null)
            {
                var installLut = new BasicVoiLutLinear(ww, wc);

                manager.InstallVoiLut(installLut);
            }
            else
            {
                standardLut.WindowWidth = ww;
                standardLut.WindowCenter = wc;
            }
            provider.Draw();
        }
        try
        {
            var bmp = pres.DrawToBitmap(f.Columns, f.Rows);
            if (f.Columns == f.Rows)
            {
                if (f.Columns < 512)
                {
                    bmp = ResizeBitmap(bmp, 512, 512);
                }
            }
            return bmp;
        }
        catch { return null; }
    }
    public static void OptiPng(DbStudy study)
    {
        string dcmPath = ADCM.GetStoreString();
        foreach(var ser in study.series)
        {
            string outputPath = Path.Combine(dcmPath, "OUTPUT", study.case_id, study.study_uid, ser.series_uid);
            if (Directory.Exists(outputPath))
            {
                var pngs = Directory.GetFiles(outputPath, "*.png");
                if(pngs == null || pngs.Length < 1) { continue; }
                foreach (var f in pngs)
                {
                    if (File.Exists(@".\OptiPNG\optipng.exe"))
                    {
                        // Run OPTIPNG with level 7 compression.
                        System.Diagnostics.ProcessStartInfo info = new System.Diagnostics.ProcessStartInfo();
                        info.FileName = @".\OptiPNG\optipng.exe";
                        info.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
                        info.Arguments = "\"" + f + "\"";

                        // Use Process for the application.
                        using (System.Diagnostics.Process exe = System.Diagnostics.Process.Start(info))
                        {
                            exe.WaitForExit();
                        }
                    }
                }
            }
        }                      
    }
    public static void DeleteAllFiles(string scpPath)
    {
        if (Directory.Exists(scpPath))
        {
            Directory.Delete(scpPath, true);
        }
    }
    //private static void ReorderImages(string seriesPath)
    //{
    //    if (Directory.Exists(seriesPath))
    //    {
    //        var files = Directory.GetFiles(seriesPath);
            
    //        for(int i = 0; i< files.Length; i++)
    //        {
    //            string extension = Path.GetExtension(files[i]);
    //            File.Move(files[i], Path.Combine(seriesPath, (i+1).ToString() + extension));
    //        }
    //    }
    //}
    private static Bitmap ResizeBitmap(Bitmap sourceBMP, int width, int height)
    {
        Bitmap result = new Bitmap(width, height);
        using (Graphics g = Graphics.FromImage(result))
            g.DrawImage(sourceBMP, 0, 0, width, height);
        return result;
    }
    private static void SaveImage(Bitmap bmp, string path)
    {
        var jgpEncoder = GetEncoder(ImageFormat.Png);


        var myEncoder = System.Drawing.Imaging.Encoder.Quality;
        var myEncoderParameters = new EncoderParameters(1);

        var myEncoderParameter = new EncoderParameter(myEncoder, 100L);
        myEncoderParameters.Param[0] = myEncoderParameter;
        bmp.Save(path, jgpEncoder, myEncoderParameters);
        GC.Collect();
    }
    private static ImageCodecInfo GetEncoder(ImageFormat format)
    {
        var codecs = ImageCodecInfo.GetImageDecoders();
        foreach (var codec in codecs)
        {
            if (codec.FormatID == format.Guid)
            {
                return codec;
            }
        }
        return null;
    }

    public static bool ZipSeries(DbStudy study)
    {
        try
        {
            string scpPath = ADCM.GetStoreString();
            string outputPath = Path.Combine(scpPath, "OUTPUT", study.case_id, study.study_uid);
            if (!Directory.Exists(outputPath))
                throw new Exception("Output path not found: " + outputPath);
            var seriesPaths = Directory.GetDirectories(outputPath);
            foreach (var s in seriesPaths)
            {
                //ReorderImages(s);
                var seriesUID = s.Substring(s.LastIndexOf(Path.DirectorySeparatorChar) + 1);
                string zipPath = Path.Combine(outputPath, seriesUID + ".zip");
                using (ZipFile zip = new ZipFile())
                {
                    zip.CompressionLevel = Ionic.Zlib.CompressionLevel.BestSpeed;
                    zip.AddDirectory(s);
                    zip.Save(zipPath);
                }
                FileInfo zipInfo = new FileInfo(zipPath);
                double megabytes = Math.Round((zipInfo.Length / 1024f) / 1024f, 2);
                LOG.Write("Zip created: " + zipInfo.Name + ", " + megabytes + " MB");
                LOG.InsertEvent("Zip created: " + zipInfo.Name + " - " + megabytes + "MB", "IMG", null, study.case_id, study.study_uid, seriesUID);
                System.Threading.Thread.Sleep(500);
            }
            LOG.InsertEvent("Successfully created ZIP files for all series in study", "IMG", null, study.case_id, study.study_uid);
            return true;
        }
        catch (Exception ex)
        {
            string errorString = "Error at :" + System.Reflection.MethodBase.GetCurrentMethod().Name;
            LOG.Write(errorString);
            LOG.Write(ex.Message);
            LOG.InsertEvent(errorString, "IMG", ex.Message, study.case_id, study.study_uid);
            return false;
        }        
    }

    public static byte[] GetImageBytesFromDcm(string dcmPath)
    {
        DicomFile dcmFile = new DicomFile(dcmPath);
        dcmFile.Load();
        int fileCount = 0;
        var windowWidth = dcmFile.DataSet[DicomTags.WindowWidth].ToString();
        if (!string.IsNullOrEmpty(windowWidth))
        {
            var tempSplitString = windowWidth.Split('\\');
            windowWidth = tempSplitString[0];
        }
        var windowCenter = dcmFile.DataSet[DicomTags.WindowCenter].ToString();
        if (!string.IsNullOrEmpty(windowCenter))
        {
            var tempSplitString = windowCenter.Split('\\');
            windowCenter = tempSplitString[0];
        }
        var ww = dcmFile.DataSet[DicomTags.WindowWidth].GetFloat32(0, 0);
        var wc = dcmFile.DataSet[DicomTags.WindowCenter].GetFloat32(0, 0);

        if (ww == 0 && !string.IsNullOrEmpty(windowWidth))
        {
            if (windowWidth.Contains("."))
            {
                var tempSplitString = windowWidth.Split('.');
                ww = int.Parse(tempSplitString[0]);
            }
            else
            {
                ww = int.Parse(windowWidth);
            }

        }
        if (wc == 0 && !string.IsNullOrEmpty(windowCenter))
        {
            if (windowCenter.Contains("."))
            {
                var tempSplitString = windowCenter.Split('.');
                wc = int.Parse(tempSplitString[0]);
            }
            else
            {
                wc = int.Parse(windowCenter);
            }

        }

        LocalSopDataSource localds = new LocalSopDataSource(dcmFile);
        if (!localds.IsImage) { return null; }
        ImageSop sop = new ImageSop(localds);
        int frameCount = sop.Frames.Count;
        if (frameCount > 1)
        {
            int midFrame = Convert.ToInt32(frameCount / 2);
            GC.Collect();
            Frame f = sop.Frames[midFrame];
            //var jpgPath = Path.Combine(outputPath, fileName + "." + j + ".png");
            Bitmap bmp = null;
            if (string.IsNullOrEmpty(windowWidth) || string.IsNullOrEmpty(windowCenter))
            {
                bmp = DrawDefaultFrame(f);
            }
            else
            {
                bmp = DrawLutFrame(f, ww, wc);
            }
            if (bmp != null) { return ImageToByte2(bmp); }
        }
        else
        {
            GC.Collect();
            Frame f = sop.Frames[1];
            Bitmap bmp = null;
            if (string.IsNullOrEmpty(windowWidth) || string.IsNullOrEmpty(windowCenter))
            {
                bmp = DrawDefaultFrame(f);
            }
            else
            {
                bmp = DrawLutFrame(f, ww, wc);
            }
            if (bmp != null) { return ImageToByte2(bmp); }
            fileCount += 1;
            GC.Collect();
        }
        return null;
    }
    public static byte[] ImageToByte2(Image img)
    {
        byte[] byteArray = new byte[0];
        using (MemoryStream stream = new MemoryStream())
        {
            img.Save(stream, System.Drawing.Imaging.ImageFormat.Jpeg);
            stream.Close();
            byteArray = stream.ToArray();
        }
        return byteArray;
    }
}