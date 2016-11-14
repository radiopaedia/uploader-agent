using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using Topshelf;



using ClearCanvas.Common;
using ClearCanvas.Dicom;
using ClearCanvas.Dicom.Iod;
using ClearCanvas.Dicom.Iod.Iods;
using ClearCanvas.Dicom.Network;
using ClearCanvas.Dicom.Network.Scu;
namespace UploaderAgentConsole
{
    class Program
    {
        static void Main(string[] args)
        {

            //Debug2();
            //Console.Write("Done Debug");
            //Console.Read();
            //return;

            LOG.Write("Radiopaedia Upload Agent");
            LOG.Write("Created by Andy Le of The Royal Melbourne Hospital");
            LOG.Write("Built: " + LOG.RetrieveLinkerTimestamp().ToString("dd MMMM yyyy | HH:mm:ss"));
            //LOG.Write("Press 'ctrl+c' to quit.");


            try
            {
                HostFactory.Run(x =>
                {
                    x.Service<ServiceController>(s =>
                    {
                        s.ConstructUsing(name => new ServiceController());
                        s.WhenStarted(nc => nc.Start());
                        s.WhenStopped(nc => nc.Stop());
                    });
                    x.RunAsLocalSystem();
                    x.SetDescription("Radiopaedia Uploader Agent");
                    x.SetDisplayName("Radiopaedia Uploader Agent");
                    x.SetServiceName("Radiopaedia Uploader Agent");
                });
            }
            catch(Exception ex)
            {
                LOG.Write(ex.Message);
                LOG.Write(Newtonsoft.Json.JsonConvert.SerializeObject(ex));
            }                                   
        }
        static void Debug2()
        {
            //ADCM.DownloadOneSeries("1.2.840.114202.4.2604463977.4026103162.3502102038.3123374876", "1.3.51.0.7.225049604.36789.12869.46787.35328.59718.43415");

            var cstore = new DicomSCP("CAPITZZRADPAE", 104);
            ////var cstore = new DicomSCP("CAPITZZKPACS", 105);
            cstore.Start();
            ////while (!cstore.IsRunning)
            ////    System.Threading.Thread.Sleep(1000);

            MoveScuBase moveScu = new StudyRootMoveScu("CAPITZZRADPAE", "CAPITCYMOD1", "192.168.115.240", 5000, "CAPITZZRADPAE");


            moveScu.ReadTimeout = 600000;
            moveScu.WriteTimeout = 600000;
            moveScu.AddStudyInstanceUid("1.2.840.114202.4.2604463977.4026103162.3502102038.3123374876");
            moveScu.AddSeriesInstanceUid("1.3.51.0.7.225049604.36789.12869.46787.35328.59718.43415");
            moveScu.Move();

            Console.WriteLine("Done");
        }
        static void Debug()
        {
            //AUP.UploadZip("44186", "47777", @"d:\temp\1.3.12.2.1107.5.1.4.65115.3000001603240736279390001264491.zip", "henryknipe", "20160411091821_henryknipe", "1.3.12.2.1107.5.8.9.13.26.65.26.126.218.77882191");
            APetaPoco.SetConnectionString("cn1");
            var bm = APetaPoco.PpRetrieveOne<DbCase>("Cases", "[case_id] = '20160607180814_frank'");
            var pcase = (DbCase)bm.Data;
            bm = APetaPoco.PpRetrieveList<DbStudy>("Studies", "[case_id] = '20160607180814_frank'");
            pcase.study_list = (List<DbStudy>)bm.Data;
            foreach (var st in pcase.study_list)
            {
                bm = APetaPoco.PpRetrieveList<DbSeries>("Series", "[case_id] = '20160607180814_frank' AND [study_uid] = '" + st.study_uid + "'");
                st.series = (List<DbSeries>)bm.Data;
                //ADCM.DownloadStudy(st);
                LOG.Write("Converting DCM to PNG...");
                AIMG.MultiFrameProcess(st);
                bool convertComplete = AIMG.ConvertDcmToPng(st);
                //if (!convertComplete)
                //{
                //    ACASE.ClearCaseFiles(pcase);
                //    throw new Exception("Unable to convert study to PNG");
                //}
                LOG.Write("Completed image conversion");

                LOG.Write("Optimizing PNG's for study...");
                AIMG.OptiPng(st);
                LOG.Write("Completed optimization.");
                //AIMG.ZipSeries(st);
            }
            //ACASE.ProcessCase(pcase);
        }
    }
    public class ServiceController
    {
        public void Start()
        {
            LOG.Write("Radiopaedia Upload Agent");
            LOG.Write("Created by Andy Le of The Royal Melbourne Hospital");
            LOG.Write("Built: " + LOG.RetrieveLinkerTimestamp().ToString("dd MMMM yyyy | HH:mm:ss"));
            LOG.Write("Service Started");            
            var sl = new ServiceLoop();
            System.Threading.Thread listenerCaller = new System.Threading.Thread(new System.Threading.ThreadStart(sl.StartHttp));
            listenerCaller.Start();
        }
        public void Stop()
        {
            LOG.Write("Service Stopped");
        }
    }
    public class ServiceLoop
    {
        public void StartHttp()
        {
            var listener = new HTTPLISTEN();
            System.Threading.Thread listenerCaller = new System.Threading.Thread(new System.Threading.ThreadStart(listener.StartServer));
            listenerCaller.Start();
            DoLoop();
        }
        public void DoLoop()
        {
            while (true)
            {
                var list = ACASE.GetPendingCases();
                if (list != null)
                {
                    foreach (var c in list)
                    {
                        LOG.Write("Found new pending case: " + c.case_id);
                        ACASE.ProcessCase(c);
                    }
                }
                System.Threading.Thread.Sleep(1 * 1000 * 60);
            }
        }
    }
}


