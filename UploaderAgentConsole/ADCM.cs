using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

using ClearCanvas.Common;
using ClearCanvas.Dicom;
using ClearCanvas.Dicom.Iod;
using ClearCanvas.Dicom.Iod.Iods;
using ClearCanvas.Dicom.Network;
using ClearCanvas.Dicom.Network.Scu;

using System.IO;
/// <summary>
/// Code Behind for DICOM related Handlers
/// </summary>
public static class ADCM
{
    public static string _storePath = null;
    public static string GetStoreString()
    {
        if (string.IsNullOrEmpty(_storePath))
        {
            var node = GetSelectedNode();
            _storePath = node.LocalStorage;
        }
        return _storePath;
    }
    public static Study GetStudyFromAccession(string accession)
    {
        var node = GetSelectedNode();
        if (node == null)
            throw new Exception("Unable to get selected DICOM node");
        StudyRootFindScu findScu = new StudyRootFindScu();
        StudyQueryIod queryMessage = new StudyQueryIod();
        queryMessage.SetCommonTags();
        queryMessage.AccessionNumber = accession;
        IList<StudyQueryIod> results = findScu.Find(node.LocalAe, node.AET, node.IP, node.Port, queryMessage);
        if (results.Count == 1)
        {
            Study st = new Study();
            st.Accession = results[0].AccessionNumber;
            st.Images = (int)results[0].NumberOfStudyRelatedInstances;
            st.PatientDob = results[0].PatientsBirthDate;
            st.PatientFirstname = results[0].PatientsName.FirstName;
            st.PatientId = results[0].PatientId;
            st.PatientSurname = results[0].PatientsName.LastName;
            st.StudyDate = results[0].StudyDate;
            st.StudyDescription = results[0].StudyDescription;
            st.StudyModality = results[0].ModalitiesInStudy;
            st.StudyUid = results[0].StudyInstanceUid;
            if (st.StudyDate != null && st.PatientDob != null)
            {
                int age = st.StudyDate.Value.Year - st.PatientDob.Value.Year;
                if (st.PatientDob.Value > st.StudyDate.Value.AddYears(-age)) age--;
                st.PatientAge = age;
            }
            StudyRootFindScu seriesFindScu = new StudyRootFindScu();
            SeriesQueryIod seriesQuery = new SeriesQueryIod();
            seriesQuery.SetCommonTags();
            seriesQuery.StudyInstanceUid = results[0].StudyInstanceUid;
            IList<SeriesQueryIod> seriesResults = seriesFindScu.Find(node.LocalAe, node.AET, node.IP, node.Port, seriesQuery);
            if (seriesResults.Count > 0)
            {
                st.Series = new List<Series>();
                foreach (var se in seriesResults)
                {
                    Series s = new Series();
                    s.StudyUid = results[0].StudyInstanceUid;
                    s.Images = (int)se.NumberOfSeriesRelatedInstances;
                    s.SeriesDescription = se.SeriesDescription;
                    s.SeriesModality = se.Modality;
                    s.SeriesUid = se.SeriesInstanceUid;
                    st.Series.Add(s);
                }
            }
            return st;
        }
        else
        {
            throw new Exception("No study found");
        }
    }

    public static bool DownloadOneSeries(string studyuid, string seriesuid, int retryCount = 0)
    {
        var node = GetSelectedNode();
        var cstore = new DicomSCP(node.LocalAe, 104);
        try
        {
            LOG.Write("Retry Count: " + retryCount);

            StudyRootFindScu findScu = new StudyRootFindScu();
            SeriesQueryIod queryMessage = new SeriesQueryIod();
            queryMessage.SetCommonTags();
            queryMessage.StudyInstanceUid = studyuid;
            queryMessage.SeriesInstanceUid = seriesuid;
            IList<SeriesQueryIod> results = findScu.Find(node.LocalAe, node.AET, node.IP, node.Port, queryMessage);
            if (results.Count != 1)
                throw new Exception(string.Format("Unable to query study on PACS: [{0}]", studyuid));

            int expectedFiles = (int)results[0].NumberOfSeriesRelatedInstances;

            if (!System.IO.Directory.Exists(node.LocalStorage))
                System.IO.Directory.CreateDirectory(node.LocalStorage);
            cstore.Start();
            while (!cstore.IsRunning)
                System.Threading.Thread.Sleep(1000);

            MoveScuBase moveScu = new StudyRootMoveScu(node.LocalAe, node.AET, node.IP, node.Port, node.LocalAe);
            moveScu.ReadTimeout = 600000;
            moveScu.WriteTimeout = 600000;
            moveScu.AddStudyInstanceUid(studyuid);
            moveScu.AddSeriesInstanceUid(seriesuid);

            DateTime started = DateTime.Now;

            moveScu.Move();
            System.Threading.Thread.Sleep(2000);
            if (moveScu.Status == ScuOperationStatus.AssociationRejected || moveScu.Status == ScuOperationStatus.Canceled ||
                moveScu.Status == ScuOperationStatus.ConnectFailed || moveScu.Status == ScuOperationStatus.Failed ||
                moveScu.Status == ScuOperationStatus.NetworkError || moveScu.Status == ScuOperationStatus.TimeoutExpired ||
                moveScu.Status == ScuOperationStatus.UnexpectedMessage || moveScu.FailureSubOperations != 0)
            {
                if (retryCount > 4)
                    throw new Exception(string.Format("Failed moving study [{0}] | Status : {1} | Failed Operations: {2}", studyuid, moveScu.Status, moveScu.FailureSubOperations));
                retryCount += 1;
                if (cstore.IsRunning)
                    cstore.Stop();
                DownloadOneSeries(studyuid, seriesuid, retryCount);
            }

            string downloadFolder = GetStoreString();
            downloadFolder = Path.Combine(downloadFolder, studyuid, seriesuid);
            bool direxist = Directory.Exists(downloadFolder);
            while (!direxist)
            {
                if ((DateTime.Now - started).TotalMinutes > 3)
                {
                    if (cstore.IsRunning)
                        cstore.Stop();
                    throw new Exception("Waited too long for images to come in");
                }
                //LOG.Write("Waiting for images to come in...");
                System.Threading.Thread.Sleep(1 * 1000);
                direxist = Directory.Exists(downloadFolder);
            }

            var dcmfiles = Directory.GetFiles(downloadFolder, "*.dcm");
            var dcmCount = dcmfiles.Length;
            while (dcmCount < expectedFiles)
            {
                if ((DateTime.Now - started).TotalMinutes > 3)
                {
                    if (cstore.IsRunning)
                        cstore.Stop();
                    throw new Exception("Waited too long for images to come in");
                }
                //LOG.Write("Waiting for images to come in...");
                System.Threading.Thread.Sleep(1 * 1000);
                dcmfiles = Directory.GetFiles(downloadFolder, "*.dcm");
                if (dcmfiles.Length != dcmCount)
                {
                    if (dcmfiles.Length >= expectedFiles) { break; }
                    dcmCount = dcmfiles.Length;
                    started = DateTime.Now;
                }
            }

            if (cstore.IsRunning)
                cstore.Stop();
            LOG.Write("Series successfully downloaded from PACS");
            return true;
        }
        catch (Exception ex)
        {
            if (cstore.IsRunning)
                cstore.Stop();
            string errorString = "Error at :" + System.Reflection.MethodBase.GetCurrentMethod().Name;
            LOG.Write(errorString);
            LOG.Write(ex.Message);
            return false;
        }
    }

    public static List<Study> GetPatientStudiesFromId(string patientId)
    {
        var node = GetSelectedNode();
        if (node == null)
            throw new Exception("Unable to get selected DICOM node");
        StudyRootFindScu findScu = new StudyRootFindScu();
        StudyQueryIod queryMessage = new StudyQueryIod();
        queryMessage.SetCommonTags();
        queryMessage.PatientId = patientId;
        IList<StudyQueryIod> results = findScu.Find(node.LocalAe, node.AET, node.IP, node.Port, queryMessage);
        if (results.Count > 0)
        {
            List<Study> stList = new List<Study>();

            foreach (var st in results)
            {
                var nst = new Study();
                nst.Accession = st.AccessionNumber;
                nst.Images = (int)st.NumberOfStudyRelatedInstances;
                nst.PatientDob = st.PatientsBirthDate;
                nst.PatientFirstname = st.PatientsName.FirstName;
                nst.PatientId = st.PatientId;
                nst.PatientSurname = st.PatientsName.LastName;
                nst.StudyDate = st.StudyDate;
                nst.StudyDescription = st.StudyDescription;
                nst.StudyModality = st.ModalitiesInStudy;
                nst.StudyUid = st.StudyInstanceUid;
                if (nst.StudyDate != null && nst.PatientDob != null)
                {
                    int age = nst.StudyDate.Value.Year - nst.PatientDob.Value.Year;
                    if (nst.PatientDob.Value > nst.StudyDate.Value.AddYears(-age)) age--;
                    nst.PatientAge = age;
                }
                StudyRootFindScu seriesFindScu = new StudyRootFindScu();
                SeriesQueryIod seriesQuery = new SeriesQueryIod();
                seriesQuery.SetCommonTags();
                seriesQuery.StudyInstanceUid = st.StudyInstanceUid;
                IList<SeriesQueryIod> seriesResults = seriesFindScu.Find(node.LocalAe, node.AET, node.IP, node.Port, seriesQuery);
                if (seriesResults.Count > 0)
                {
                    nst.Series = new List<Series>();
                    foreach (var se in seriesResults)
                    {
                        Series s = new Series();
                        s.StudyUid = results[0].StudyInstanceUid;
                        s.Images = (int)se.NumberOfSeriesRelatedInstances;
                        s.SeriesDescription = se.SeriesDescription;
                        s.SeriesModality = se.Modality;
                        s.SeriesUid = se.SeriesInstanceUid;
                        nst.Series.Add(s);
                    }
                }
                stList.Add(nst);
            }
            return stList;
        }
        else
        {
            throw new Exception("Unable to find studies for patient");
        }
    }

    public static Node GetSelectedNode()
    {
        //return GetDebugNode();
        APetaPoco.SetConnectionString("cn1");
        var bm = APetaPoco.PpRetrieveOne<Node>("PACS", "[Selected] = 1");
        if (bm.Success)
            return (Node)bm.Data;
        else
            return null;
    }

    public static Node GetDebugNode()
    {
        APetaPoco.SetConnectionString("cn1");
        var bm = APetaPoco.PpRetrieveOne<Node>("PACS", "[LocalAe] = 'ANDYPACS3'");
        if (bm.Success)
            return (Node)bm.Data;
        else
            return null;
    }

    public static bool EchoNode(Node node)
    {
        try
        {

            VerificationScu scu = new VerificationScu();
            var result = scu.Verify(node.LocalAe, node.AET, node.IP, node.Port);
            if (result == VerificationResult.Success)
                return true;
            else
                return false;
        }
        catch { return false; }
    }

    public static bool DownloadStudy(DbStudy study, int retryCount = 0)
    {
        foreach(var se in study.series)
        {
            bool success = DownloadOneSeries(study.study_uid, se.series_uid);
            if (!success) { return false; }            
        }
        return true;
        //LOG.Write(string.Format("Downloading study [{0}] - {1} ({2})", study.modality, study.description, study.images));
        //LOG.Write("Retry Count: " + retryCount);
        //var node = GetSelectedNode();        
        //var cstore = new DicomSCP(node.LocalAe, 104);

        //StudyRootFindScu findScu = new StudyRootFindScu();
        //StudyQueryIod queryMessage = new StudyQueryIod();
        //queryMessage.SetCommonTags();
        //queryMessage.StudyInstanceUid = study.study_uid;
        //IList<StudyQueryIod> results = findScu.Find(node.LocalAe, node.AET, node.IP, node.Port, queryMessage);
        //if (results.Count != 1)
        //    throw new Exception(string.Format("Unable to query study on PACS: [{0}]", study.study_uid));

        //if (!System.IO.Directory.Exists(node.LocalStorage))
        //    System.IO.Directory.CreateDirectory(node.LocalStorage);
        //cstore.Start();
        //while (!cstore.IsRunning)
        //    System.Threading.Thread.Sleep(1000);
        //string singleSeriesOnly = System.Configuration.ConfigurationManager.AppSettings["singleSeries"];
        
        //MoveScuBase moveScu = new StudyRootMoveScu(node.LocalAe, node.AET, node.IP, node.Port, node.LocalAe);
        //moveScu.ReadTimeout = 600000;
        //moveScu.WriteTimeout = 600000;
        //moveScu.AddStudyInstanceUid(study.study_uid);
        //foreach (var se in study.series)
        //{
        //    moveScu.AddSeriesInstanceUid(se.series_uid);
        //    System.Diagnostics.Debug.WriteLine(se.series_uid);
        //}
        //moveScu.Move();
        //System.Threading.Thread.Sleep(2000);
        //if (moveScu.Status == ScuOperationStatus.AssociationRejected || moveScu.Status == ScuOperationStatus.Canceled ||
        //    moveScu.Status == ScuOperationStatus.ConnectFailed || moveScu.Status == ScuOperationStatus.Failed ||
        //    moveScu.Status == ScuOperationStatus.NetworkError || moveScu.Status == ScuOperationStatus.TimeoutExpired ||
        //    moveScu.Status == ScuOperationStatus.UnexpectedMessage || moveScu.FailureSubOperations != 0)
        //{
        //    if (retryCount > 4)
        //        throw new Exception(string.Format("Failed moving study [{0}] | Status : {1} | Failed Operations: {2}", study.study_uid, moveScu.Status, moveScu.FailureSubOperations));
        //    retryCount += 1;
        //    if (cstore.IsRunning)
        //        cstore.Stop();
        //    DownloadStudy(study, retryCount);
        //}
        //else
        //{
        //    System.Threading.Thread.Sleep(2000);
        //    cstore.Stop();
        //}
        //if (cstore.IsRunning)
        //    cstore.Stop();
        //LOG.InsertEvent("Successfully downloaded study: " + study.description, "DICOM", null, study.case_id, study.study_uid);
        //return true;

        try
        {            
            
        }
        catch (Exception ex)
        {
            
            string errorString = "Error at :" + System.Reflection.MethodBase.GetCurrentMethod().Name;
            LOG.Write(errorString);
            LOG.Write(ex.Message);
            LOG.InsertEvent(errorString, "DICOM", ex.Message, study.case_id, study.study_uid);
            return false;
        }        
    }                

}