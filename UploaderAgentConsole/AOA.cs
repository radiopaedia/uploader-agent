using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

using System.Net;
using Newtonsoft.Json;

/// <summary>
/// Class for OAuth handlers
/// /// </summary>
public static class AOA
{
    public static TokenResponse TradeAuthCodeForToken(string authCode)
    {
        try
        {
            var api = ACFG.GetSiteApiDetails();
            WebRequest request = WebRequest.Create(api.oauth_url + "token?client_id=" + api.site_id + "&client_secret=" + api.site_secret + "&code=" + authCode + "&grant_type=authorization_code&redirect_uri=" + api.redirect_url);
            request.Method = "POST";
            WebResponse response = request.GetResponse();
            var dataStream = response.GetResponseStream();
            System.IO.StreamReader reader = new System.IO.StreamReader(dataStream);
            string responseFromServer = reader.ReadToEnd();
            reader.Close();
            dataStream.Close();
            response.Close();
            return JsonConvert.DeserializeObject<TokenResponse>(responseFromServer);
        }
        catch { return null; }
    }

    public static void RefreshToken(string refreshToken, string caseid)
    {
        try
        {
            var api = ACFG.GetSiteApiDetails();
            string responseFromServer = null;
            try
            {
                WebRequest request = WebRequest.Create(api.oauth_url + "token?client_id=" + api.site_id + "&client_secret=" + api.site_secret + "&refresh_token=" + refreshToken + "&grant_type=refresh_token");
                request.Method = "POST";
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
                using (var reader = new System.IO.StreamReader(stream))
                {
                    string errorResponse = reader.ReadToEnd();
                    LOG.InsertEvent("Unable to upload zip", "API", errorResponse, caseid);
                }
                GC.Collect();
                return;
            }
            var tokenResp = JsonConvert.DeserializeObject<TokenResponse>(responseFromServer);
            if (tokenResp == null || tokenResp == default(TokenResponse))
                throw new Exception("Unable to retrieve valid token response");
            APetaPoco.SetConnectionString("cn1");
            var bm = APetaPoco.PpRetrieveOne<User>("Users", "[refresh_token] = '" + refreshToken + "'");
            if (bm.Success)
            {
                var user = (User)bm.Data;
                user.access_token = tokenResp.access_token;
                user.refresh_token = tokenResp.refresh_token;
                user.expiry_date = DateTime.Now.AddSeconds(tokenResp.expires_in - 10);
                bm = APetaPoco.PpUpdate(user);
                if (!bm.Success)
                    throw new Exception("Unable to update user details with new token");
            }
        }
        catch (Exception ex)
        {
            string errorString = "Error at :" + System.Reflection.MethodBase.GetCurrentMethod().Name;
            LOG.Write(errorString);
            LOG.Write(ex.Message);
            LOG.InsertEvent(errorString, "API", ex.Message, caseid);            
        }
        finally { GC.Collect(); }        
    }
    public static User GetUserRefreshIfNecessary(string username, string caseid)
    {
        try
        {
            APetaPoco.SetConnectionString("cn1");
            var bm = APetaPoco.PpRetrieveOne<User>("Users", "[username] = '" + username + "'");
            if (!bm.Success)
                throw new Exception("Unable to retrieve user details for case: " + username);
            var user = (User)bm.Data;
            if (DateTime.Now > user.expiry_date.Value.AddMinutes(-30))
            {
                RefreshToken(user.refresh_token, caseid);
                bm = APetaPoco.PpRetrieveOne<User>("Users", "[username] = '" + username + "'");
                if (!bm.Success)
                    throw new Exception("Unable to retrieve UPDATED user details for case: " + username);
                user = (User)bm.Data;
            }
            return user;
        }
        catch (Exception ex)
        {
            string errorString = "Error at :" + System.Reflection.MethodBase.GetCurrentMethod().Name;
            LOG.Write(errorString);
            LOG.Write(ex.Message);
            LOG.InsertEvent(errorString, "API", ex.Message, caseid);
            return null;
        }
        finally { GC.Collect(); }        
    }

    public static UserResponse GetUserName(string token)
    {
        try
        {
            var api = ACFG.GetSiteApiDetails();
            WebRequest request = WebRequest.Create(api.users_url);
            request.Method = "GET";
            //request.ContentType = "application/json";
            request.Headers.Add("Authorization", "Bearer " + token);

            WebResponse response = request.GetResponse();
            var dataStream = response.GetResponseStream();
            System.IO.StreamReader reader = new System.IO.StreamReader(dataStream);
            string responseFromServer = reader.ReadToEnd();
            if (string.IsNullOrEmpty(responseFromServer))
                throw new Exception("Unable to get response from server when marking case complete");
            reader.Close();
            dataStream.Close();
            response.Close();
            var respObj = JsonConvert.DeserializeObject<UserResponse>(responseFromServer);
            return respObj;
        }
        catch (Exception ex)
        {
            return null;
        }

    }


    //public static string GetAuthCode()
    //{
    //    string client_id = "ba325141e3db766579feb0ba3b22d7087142053633a02d8d2c19f82bbcfd3d04";
    //    string client_secret = "1f3a57940372fc9fbfb5f58efaee27c5d23d45c03ccf856bbc4c30af17bcb556";
    //    string redirect_url = "http://localhost:51339/PostAuth.aspx";
    //    WebRequest request = WebRequest.Create("http://sandbox.radiopaedia.org/oauth/authorize?client_id="+client_id+"&redirect_uri="+redirect_url+"&response_type=code");
    //    request.Method = "GET";
    //    WebResponse response = request.GetResponse();
    //    var dataStream = response.GetResponseStream();
    //    System.IO.StreamReader reader = new System.IO.StreamReader(dataStream);
    //    string responseFromServer = reader.ReadToEnd();
    //    reader.Close();
    //    dataStream.Close();
    //    response.Close();

    //}
}



public class TokenResponse
{
    public string access_token { set; get; }
    public string token_type { set; get; }
    public int expires_in { set; get; }
    public string refresh_token { set; get; }
    public string scope { set; get; }
    public long created_at { set; get; }
}
public class UserResponse
{
    public string login { set; get; }
    public Quotas quotas { set; get; }
}
public class Quotas
{
    public int? allowed_draft_cases { set; get; }
    public int? allowed_unlisted_cases { set; get; }
    public int? allowed_unlisted_playlists { set; get; }
    public int draft_case_count { set; get; }
    public int unlisted_case_count { set; get; }
    public int unlisted_playlist_count { set; get; }
}
//{"access_token":"38246d410cd801e09086c892112353b04f22416f432811ddee0d3e7d1d2212c8","token_type":"bearer","expires_in":86400,"refresh_token":"03be68594d360a8a8f728131a05de1f96669dade68c28b2e414326b848335ce0","scope":"cases","created_at":1458791436}