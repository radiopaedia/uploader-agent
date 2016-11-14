using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

public static class ACASE
{
    public static List<DbCase> GetPendingCases()
    {
        APetaPoco.SetConnectionString("cn1");
        var bm = APetaPoco.PpRetrieveList<DbCase>("Cases", "[Status] = 'PENDING'");
        if (bm.Success)
        {
            var list = (List<DbCase>)bm.Data;
            foreach(var c in list)
            {
                c.study_list = GetStudiesForCase(c.case_id);
            }
            return list;            
        }
        else
            return null;
    }
    private static List<DbStudy> GetStudiesForCase(string caseid)
    {
        List<DbStudy> studies = new List<DbStudy>();
        APetaPoco.SetConnectionString("cn1");
        var bm = APetaPoco.PpRetrieveList<DbStudy>("Studies", string.Format("[case_id] = '{0}'", caseid));
        if (bm.Success)
        {
            studies = (List<DbStudy>)bm.Data;
            foreach (var s in studies)
            {
                bm = APetaPoco.PpRetrieveList<DbSeries>("Series", string.Format("[case_id] = '{0}' AND [study_uid] = '{1}'", caseid, s.study_uid));
                if (bm.Success)
                {
                    var series = (List<DbSeries>)bm.Data;
                    s.series = series;
                }
            }
            return studies;
        }
        else { return null; }
    }    
    public static void ProcessCase(DbCase pcase)
    {
        try
        {
            LOG.Write("Checking user quotas...");
            if (!CheckQuotas(pcase))
            {
                throw new Exception("Quota exceeded");
            }
            LOG.Write("Creating case on Radiopaedia...");
            pcase.r_case_id = AUP.CreateCase(pcase);
            if (string.IsNullOrEmpty(pcase.r_case_id))
                throw new Exception("Unable to create case id, cannot continue");
            LOG.Write("Case created id: " + pcase.r_case_id);

            if (pcase.study_list == null || pcase.study_list.Count < 1)
            {
                pcase.study_list = GetStudiesForCase(pcase.case_id);
                if (pcase.study_list == null || pcase.study_list.Count < 1) { throw new Exception("No studies in case: " + pcase.case_id); }
            }

            foreach (var st in pcase.study_list)
            {
                LOG.InsertEvent("Starting to process case: " + pcase.case_id, "AGENT", null, pcase.case_id);
                LOG.Write("Creating Radiopaedia Study...");
                st.r_study_id = AUP.CreateStudy(st, pcase.username, pcase.r_case_id);
                if (string.IsNullOrEmpty(st.r_study_id)) { throw new Exception("Unable to create study id on Radiopaedia"); }
                LOG.Write("Study ID created: " + st.r_study_id);

                LOG.Write("Study: " + st.description + "[" + st.modality + "]");
                LOG.Write("Downloading...");
                bool downloadComplete = ADCM.DownloadStudy(st, 0);
                if (!downloadComplete)
                {
                    ClearCaseFiles(pcase);
                    throw new Exception("Unable to download study - can't continue");
                }
                LOG.Write("Download finished");
                LOG.Write("Converting DCM to PNG...");
                AIMG.MultiFrameProcess(st);
                bool convertComplete = AIMG.ConvertDcmToPng(st);
                if (!convertComplete)
                {
                    ClearCaseFiles(pcase);
                    throw new Exception("Unable to convert study to PNG");
                }
                LOG.Write("Completed image conversion");

                LOG.Write("Deleting excess images...");
                AIMG.DeleteExcessImages(st);
                LOG.Write("Completed deleting excess images.");

                LOG.Write("Optimizing PNG's for study...");
                AIMG.OptiPng(st);
                LOG.Write("Completed optimization.");

                bool zipComplete = AIMG.ZipSeries(st);
                if (!zipComplete)
                    throw new Exception("Unable to create zips for study");


                string outPath = Path.Combine(ADCM.GetStoreString(), "OUTPUT", pcase.case_id, st.study_uid);
                var zips = Directory.GetFiles(outPath, "*.zip");
                foreach (var z in zips)
                {
                    string fileName = Path.GetFileName(z);                    

                    string[] sizes = { "B", "KB", "MB", "GB" };
                    double len = new FileInfo(z).Length;
                    int order = 0;
                    while (len >= 1024 && ++order < sizes.Length)
                    {
                        len = len / 1024;
                    }

                    LOG.Write(string.Format("Uploading: {2} ({0:0.##} {1})", len, sizes[order], fileName));
                    bool uploadedZip = AUP.UploadZip2(pcase.r_case_id, st.r_study_id, z, pcase.username, pcase.case_id, st.study_uid);       
                    if(!uploadedZip)
                    {
                        try
                        {
                            LOG.Write("Retry maxed out, copying zip to error output");
                            string errorFolder = Path.Combine(@".\Error_uploads\", pcase.case_id);
                            if (!Directory.Exists(errorFolder)) { Directory.CreateDirectory(errorFolder); }
                            string errorPath = Path.Combine(errorFolder, fileName);
                            File.Copy(z, errorPath);
                        }
                        catch
                        {
                            continue;
                        }                                                                        
                    }
                    LOG.Write("Finished uploading");
                }
            }
            LOG.Write("Marking case as completed");
            AUP.MarkCaseComplete(pcase.r_case_id, pcase.username, pcase.case_id);
            SetCaseStatus(pcase, "COMPLETED", "Case fully uploaded: http://radiopaedia.org/cases/" + pcase.r_case_id);
            System.Threading.Thread.Sleep(1000);
            LOG.Write("Finished with case: " + pcase.case_id);
            ClearCaseFiles(pcase);
            LOG.InsertEvent("Finished with case: " + pcase.case_id, "AGENT", null, pcase.case_id);            
        }
        catch (Exception ex)
        {
            string errorString = "Error at :" + System.Reflection.MethodBase.GetCurrentMethod().Name;
            LOG.Write(errorString);
            LOG.Write(ex.Message);
            LOG.InsertEvent(errorString, "AGENT", ex.Message, pcase.case_id);
            SetCaseStatus(pcase, "ERROR", ex.Message);
            ClearCaseFiles(pcase);
        }
        finally { GC.Collect(); }        
    }
    private static bool CheckQuotas(DbCase pcase)
    {
        var user = AOA.GetUserRefreshIfNecessary(pcase.username, pcase.case_id);
        var api_user = AOA.GetUserName(user.access_token);
        if(api_user.quotas.allowed_draft_cases == null || api_user.quotas.allowed_unlisted_cases == null) { return true; }
        if (api_user.quotas.draft_case_count >= api_user.quotas.allowed_draft_cases) { return false; }
        if (api_user.quotas.unlisted_case_count >= api_user.quotas.allowed_unlisted_cases) { return false; }
        return true;
    }
    private static void SetCaseStatus(DbCase dcase, string status, string statusMsg)
    {
        dcase.status = status;
        dcase.status_message = statusMsg;
        APetaPoco.SetConnectionString("cn1");
        var bm = APetaPoco.PpUpdate(dcase);
    }
    private static DbStudy GetStudy(string condition)
    {
        APetaPoco.SetConnectionString("cn1");
        var bm = APetaPoco.PpRetrieveOne<DbStudy>("Studies", condition);
        if (!bm.Success)
            throw new Exception("Unable to get Study based on condition: " + condition);
        var dbs = (DbStudy)bm.Data;

        bm = APetaPoco.PpRetrieveList<DbSeries>("Series", condition);
        if (bm.Success)
        {
            var series = (List<DbSeries>)bm.Data;
            dbs.series = series;
        }

        return dbs;
    }

    public static void ClearCaseFiles(DbCase pcase)
    {
        string dcmPath = ADCM.GetStoreString();
        foreach(var st in pcase.study_list)
        {
            string stDir = Path.Combine(dcmPath, st.study_uid);
            if (Directory.Exists(stDir))
                Directory.Delete(stDir, true);
        }
        string outputPath = Path.Combine(dcmPath, "OUTPUT", pcase.case_id);
        if (Directory.Exists(outputPath))
            Directory.Delete(outputPath, true);
    }
}
