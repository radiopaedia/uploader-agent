using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Net;
using Newtonsoft.Json;

using System.IO;

public static class AUP
{
    public static string CreateCase(DbCase dbcase)
    {
        try
        {
            var ucase = new UploadCase();
            ucase.age = dbcase.age;
            ucase.body = dbcase.body;
            ucase.diagnostic_certainty_id = dbcase.diagnostic_certainty_id;
            ucase.presentation = dbcase.presentation;
            ucase.suitable_for_quiz = dbcase.suitable_for_quiz;
            ucase.system_id = dbcase.system_id;
            ucase.title = dbcase.title;
            string postData = JsonConvert.SerializeObject(ucase);
            byte[] data = System.Text.Encoding.UTF8.GetBytes(postData);

            var api = ACFG.GetSiteApiDetails();
            var user = AOA.GetUserRefreshIfNecessary(dbcase.username, dbcase.case_id);


            string responseFromServer = null;
            try
            {
                WebRequest request = WebRequest.Create(api.cases_url);
                request.Method = "POST";
                request.ContentType = "application/json";
                request.Headers.Add("Authorization", "Bearer " + user.access_token);

                Stream stream = request.GetRequestStream();
                stream.Write(data, 0, data.Length);
                stream.Close();

                WebResponse response = request.GetResponse();
                var dataStream = response.GetResponseStream();
                System.IO.StreamReader reader = new System.IO.StreamReader(dataStream);
                responseFromServer = reader.ReadToEnd();
                if (string.IsNullOrEmpty(responseFromServer))
                    throw new Exception("Unable to get response from server when creating case");
                reader.Close();
                dataStream.Close();
                response.Close();
            }
            catch (WebException ex)
            {
                using (var stream = ex.Response.GetResponseStream())
                using (var reader = new StreamReader(stream))
                {
                    string errorResponse = reader.ReadToEnd();
                    LOG.InsertEvent("Unable to create new case on Radiopedia server", "API", errorResponse, dbcase.case_id);
                }
                return null;
            }


            var respObj = JsonConvert.DeserializeObject<CaseResponse>(responseFromServer);
            dbcase.r_case_id = respObj.id;
            APetaPoco.SetConnectionString("cn1");
            var bm = APetaPoco.PpUpdate(dbcase);
            if (!bm.Success)
                throw new Exception("Unable to update Case in database");
            LOG.InsertEvent("Successfully created Case on Radiopaedia:\n" + respObj.id, "API", responseFromServer, dbcase.case_id);
            return respObj.id;
        }
        catch(Exception ex)
        {
            string errorString = "Error at :" + System.Reflection.MethodBase.GetCurrentMethod().Name;
            LOG.Write(errorString);
            LOG.Write(ex.Message);
            LOG.InsertEvent(errorString, "API", ex.Message, dbcase.case_id);
            return null;
        }        
    }

    public static bool MarkCaseComplete(string RcaseId, string username, string DbCaseId)
    {
        try
        {
            var user = AOA.GetUserRefreshIfNecessary(username, DbCaseId);
            var api = ACFG.GetSiteApiDetails();
            WebRequest request = WebRequest.Create(api.cases_url + RcaseId + "/mark_upload_finished");
            request.Method = "PUT";
            //request.ContentType = "application/json";
            request.Headers.Add("Authorization", "Bearer " + user.access_token);

            WebResponse response = request.GetResponse();
            var dataStream = response.GetResponseStream();
            System.IO.StreamReader reader = new System.IO.StreamReader(dataStream);
            string responseFromServer = reader.ReadToEnd();
            if (string.IsNullOrEmpty(responseFromServer))
                throw new Exception("Unable to get response from server when marking case complete");
            reader.Close();
            dataStream.Close();
            response.Close();
            var respObj = JsonConvert.DeserializeObject<CaseResponse>(responseFromServer);
            LOG.InsertEvent("Successfully marked study as completed on Radiopaedia", "API", responseFromServer, DbCaseId);
            return true;
        }
        catch (Exception ex)
        {
            string errorString = "Error at :" + System.Reflection.MethodBase.GetCurrentMethod().Name;
            LOG.Write(errorString);
            LOG.Write(ex.Message);
            LOG.InsertEvent(errorString, "API", ex.Message, DbCaseId);
            return false;
        }        
    }

    public static string CreateStudy(DbStudy dbstudy, string username, string caseId)
    {       
        try
        {
            var ustudy = new StudyUpload();
            ustudy.modality = GetModality(dbstudy.modality);
            ustudy.caption = dbstudy.caption;
            ustudy.findings = dbstudy.findings;
            ustudy.position = dbstudy.position;
            string postData = JsonConvert.SerializeObject(ustudy);
            byte[] data = System.Text.Encoding.UTF8.GetBytes(postData);

            string responseFromServer = null;
            try
            {
                var api = ACFG.GetSiteApiDetails();
                var user = AOA.GetUserRefreshIfNecessary(username, caseId);
                WebRequest request = WebRequest.Create(api.cases_url + caseId + "/studies");
                request.Method = "POST";
                request.ContentType = "application/json";
                request.Headers.Add("Authorization", "Bearer " + user.access_token);

                Stream stream = request.GetRequestStream();
                stream.Write(data, 0, data.Length);
                stream.Close();

                WebResponse response = request.GetResponse();
                var dataStream = response.GetResponseStream();
                System.IO.StreamReader reader = new System.IO.StreamReader(dataStream);
                responseFromServer = reader.ReadToEnd();
                reader.Close();
                dataStream.Close();
                response.Close();
            }
            catch (WebException ex)
            {
                using (var stream = ex.Response.GetResponseStream())
                using (var reader = new StreamReader(stream))
                {
                    string errorResponse = reader.ReadToEnd();
                    LOG.InsertEvent("Unable to create new study on Radiopedia server", "API", errorResponse, dbstudy.case_id, dbstudy.study_uid);
                }
                return null;
            }
            catch (Exception ex)
            {
                string errorString = "Error at :" + System.Reflection.MethodBase.GetCurrentMethod().Name;
                LOG.Write(errorString);
                LOG.Write(ex.Message);
                LOG.InsertEvent(errorString, "API", ex.Message, dbstudy.case_id, dbstudy.study_uid);
                return null;
            }

            var respObj = JsonConvert.DeserializeObject<StudyResponse>(responseFromServer);
            dbstudy.r_study_id = respObj.id;
            APetaPoco.SetConnectionString("cn1");
            var bm = APetaPoco.PpUpdate(dbstudy);
            if (!bm.Success)
                throw new Exception("Unable to update Study in database");
            LOG.InsertEvent("Successfully created new study:\n" + respObj.id, "API", responseFromServer, dbstudy.case_id, dbstudy.study_uid);
            return respObj.id;
        }
        catch (Exception ex)
        {
            string errorString = "Error at :" + System.Reflection.MethodBase.GetCurrentMethod().Name;
            LOG.Write(errorString);
            LOG.Write(ex.Message);
            LOG.InsertEvent(errorString, "API", ex.Message, dbstudy.case_id, dbstudy.study_uid);
            return null;
        }
        finally { GC.Collect(); }
    }

    public static bool UploadZip(string RcaseId, string studyId, string zipPath, string username, string DbCaseId, string stydyUid)
    {
        try
        {
            var fi = new FileInfo(zipPath);            
            byte[] data = File.ReadAllBytes(zipPath);
            string responseFromServer = null;
            try
            {
                var api = ACFG.GetSiteApiDetails();
                var user = AOA.GetUserRefreshIfNecessary(username, DbCaseId);
                //WebRequest request = WebRequest.Create(api.cases_url + RcaseId + "/studies/" + studyId + "/images");

                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(api.cases_url + RcaseId + "/studies/" + studyId + "/images");
                request.ContentType = "application/zip";
                request.Method = "POST";
                request.ServicePoint.Expect100Continue = true;
                request.Timeout = 60 * 60 * 1000;
                request.Method = "POST";
                request.ContentType = "application/zip";
                request.Headers.Add("Authorization", "Bearer " + user.access_token);

                Stream stream = request.GetRequestStream();
                stream.Write(data, 0, data.Length);
                stream.Close();

                WebResponse response = request.GetResponse();
                var dataStream = response.GetResponseStream();
                System.IO.StreamReader reader = new System.IO.StreamReader(dataStream);
                responseFromServer = reader.ReadToEnd();
                reader.Close();
                dataStream.Close();
                response.Close();
            }
            catch (WebException ex)
            {
                using (var stream = ex.Response.GetResponseStream())
                using (var reader = new StreamReader(stream))
                {
                    string errorResponse = reader.ReadToEnd();
                    LOG.InsertEvent("Unable to upload zip: " + fi.Name, "API", errorResponse, DbCaseId, stydyUid);
                }
                return false;
            }
            
            var respObj = JsonConvert.DeserializeObject<UploadResponse>(responseFromServer);
            LOG.Write("Uploaded Filename: " + respObj.filename);
            LOG.InsertEvent("Successfully uploaded ZIP", "API", responseFromServer, DbCaseId, stydyUid);
            return true;
        }
        catch(Exception ex)
        {
            string errorString = "Error at :" + System.Reflection.MethodBase.GetCurrentMethod().Name;
            LOG.Write(errorString);
            LOG.Write(ex.Message);
            LOG.InsertEvent(errorString, "API", ex.Message, DbCaseId, stydyUid);
            return false;
        }
        finally { GC.Collect(); }
    }

    public static bool UploadZip2(string RcaseId, string studyId, string zipPath, string username, string DbCaseId, string stydyUid)
    {
        var api = ACFG.GetSiteApiDetails();
        var user = AOA.GetUserRefreshIfNecessary(username, DbCaseId);
        // Build request
        var request = (HttpWebRequest)WebRequest.Create(api.cases_url + RcaseId + "/studies/" + studyId + "/images");
        request.Method = WebRequestMethods.Http.Post;
        request.AllowWriteStreamBuffering = false;
        request.ContentType = "application/zip";
        request.Timeout = 60 * 60 * 1000;
        request.ReadWriteTimeout = 60 * 60 * 1000;
        string fileName = Path.GetFileName(zipPath);
        request.Headers["Content-Disposition"] = string.Format("attachment; filename=\"{0}\"", fileName);
        request.Headers.Add("Authorization", "Bearer " + user.access_token);

        try
        {
            // Open source file
            using (var fileStream = File.OpenRead(zipPath))
            {
                // Set content length based on source file length
                request.ContentLength = fileStream.Length;

                // Get the request stream with the default timeout
                using (var requestStream = request.GetRequestStream())
                {
                    // Upload the file with no timeout
                    fileStream.CopyTo(requestStream);
                }
            }            
            using (var response = request.GetResponse())
            using (var responseStream = response.GetResponseStream())
            using (var reader = new StreamReader(responseStream))
            {
                var respObj = JsonConvert.DeserializeObject<UploadResponse>(reader.ReadToEnd());
                LOG.Write("Uploaded Filename: " + respObj.filename);
                LOG.InsertEvent("Successfully uploaded ZIP", "API", reader.ReadToEnd(), DbCaseId, stydyUid);
            }
        }
        catch (WebException ex)
        {            
            
            if (ex.Status == WebExceptionStatus.Timeout)
            {
                //LogError(ex, "Timeout while uploading '{0}'", fileName);
                LOG.InsertEvent("Timeout While uploading: " + zipPath, "API", ex.Message, DbCaseId, stydyUid);
                LOG.Write("Timeout Uploading");
                LOG.Write(ex.Message);
            }
            else
            {
                LOG.InsertEvent("Error While uploading: " + zipPath, "API", ex.Message, DbCaseId, stydyUid);
                LOG.Write("Error Uploading");
                LOG.Write(ex.Message);
            }
            return false;
        }
        return true;
    }

    public static string GetModality(string mod)
    {
        string modality = mod;
        switch (mod.ToUpper())
        {
            case "CR":
                modality = "X-ray";
                break;
            case "PR":
                modality = "X-ray";
                break;
            case "DR":
                modality = "X-ray";
                break;
            case "XA":
                modality = "DSA (angiography)";
                break;
            case "MG":
                modality = "Mammography";
                break;
            case "MR":
                modality = "MRI";
                break;
            case "US":
                modality = "Ultrasound";
                break;
            case "NM":
                modality = "Nuclear medicine";
                break;
            case "FL":
                modality = "Fluoroscopy";
                break;
            case "OT":
                modality = null;
                break;
            case "MRI":
                modality = "MRI";
                break;
            case "CT":
                modality = "CT";
                break;
            default:
                modality = null;
                break;
        }
        return modality;
    }
}
public class CaseResponse
{
    public string id;
    public string title;
    public string author_id;
    public DateTime created_at;
    public DateTime updated_at;
}

public class StudyResponse
{
    public string case_id;
    public DateTime created_at;
    public string caption;
    public string modality;
    public string findings;
    public string id;
    public DateTime updated_at;
}

public class UploadResponse
{
    public string id;
    public string filename;
    public string contributor_id;
    public DateTime created_at;
    public DateTime updated_at;
}
public class StudyUpload
{
    public string modality;
    public string findings;
    public int position;
    public string caption;
}
public class DBCaseResponse
{
    public int Id;
    public string r_case_id;
    public string db_case_id;
}
public class DBStudyResponse
{
    public int Id;
    public string r_case_id;
    public string db_case_id;
    public string r_study_id;
    public string study_uid;
}