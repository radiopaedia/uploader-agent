using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

using System.Configuration;

using System.Data.SqlClient;

using PetaPoco;

/// <summary>
/// Helper functions for PetaPoco
/// </summary>
public static class APetaPoco
{
    public static BoolMessage _checkVal = new BoolMessage();
    public static PetaPoco.Database _pp = new Database();
    public static string _cstring = null;
    public static void SetConnectionString(string cstring)
    {
        _cstring = null;
        if (!string.IsNullOrEmpty(cstring))
            _cstring = cstring;
        if (ConfigurationManager.ConnectionStrings[cstring] == null)
        {
            _pp = new Database(cstring);
        }
        else
        {
            //DecryptConnectionStrings();
            _pp = new Database(cstring);
            //EncryptConnectionStrings();
        }

    }


    public static void DeleteSql(string _tableName, string _condition)
    {
        try
        {
            if (string.IsNullOrEmpty(_cstring))
                throw new Exception("Connection String not set");
            if (string.IsNullOrEmpty(_condition))
                throw new Exception("Condition String not set");
            string query = string.Format("DELETE FROM [{0}] WHERE {1}", _tableName.ToUpper(), _condition);
            using (SqlConnection conn = new SqlConnection(_cstring))
            {
                SqlCommand cmd = new SqlCommand(query, conn);
                conn.Open();
                cmd.ExecuteNonQuery();
                conn.Close();
            }
        }
        catch (Exception ex)
        {
            return;
        }
    }
    public static BoolMessage PpDelete(object o)
    {
        _checkVal.Data = null;
        try
        {
            if (string.IsNullOrEmpty(_cstring))
                throw new Exception("No connection string provided");
            int rows = _pp.Delete(o);
            _checkVal.Success = true;
            _checkVal.Message = string.Format("Successfully deleted {0} rows", rows);
        }
        catch (Exception ex)
        {
            _checkVal.Success = false;
            _checkVal.Message = ex.Message;
        }
        return _checkVal;
    }

    public static BoolMessage PpUpdate(object o)
    {
        _checkVal.Data = null;
        try
        {
            if (string.IsNullOrEmpty(_cstring))
                throw new Exception("No connection string provided");
            int rows = _pp.Update(o);
            _checkVal.Success = true;
            _checkVal.Message = string.Format("Successfully updated {0} rows", rows);
        }
        catch (Exception ex)
        {
            _checkVal.Success = false;
            _checkVal.Message = ex.Message;
        }
        return _checkVal;
    }
    public static BoolMessage PpRetrieveList<T>(string tableName, string condition = null)
    {
        _checkVal.Data = null;
        try
        {
            if (string.IsNullOrEmpty(_cstring))
                throw new Exception("No connection string provided");
            List<T> rtnList = null;
            if (string.IsNullOrEmpty(condition))
            {
                rtnList = _pp.Query<T>(string.Format("SELECT * FROM [{0}]", tableName)).ToList();
            }
            else
            {
                rtnList = _pp.Query<T>(string.Format("SELECT * FROM [{0}] WHERE {1}", tableName, condition)).ToList();
            }
            _checkVal.Success = true;
            _checkVal.Message = string.Format("Successfully returned list from table {0}", tableName);
            _checkVal.Data = rtnList;
        }
        catch (Exception ex)
        {
            _checkVal.Data = null;
            _checkVal.Success = false;
            _checkVal.Message = ex.Message;
        }
        return _checkVal;
    }
    public static BoolMessage PpRetrieveOne<T>(string tableName, string condition)
    {
        _checkVal.Data = null;
        try
        {
            if (string.IsNullOrEmpty(_cstring))
                throw new Exception("No connection string provided");
            _checkVal.Data = _pp.SingleOrDefault<T>(string.Format("SELECT * FROM [{0}] WHERE {1}", tableName, condition));
            _checkVal.Success = true;
            _checkVal.Message = string.Format("Successfully returned one item from table {0}", tableName);
        }
        catch (Exception ex)
        {
            _checkVal.Data = null;
            _checkVal.Success = false;
            _checkVal.Message = ex.Message;
        }
        return _checkVal;
    }
    public static BoolMessage PpRetrieveCustomQuery<T>(string query)
    {
        _checkVal.Data = null;
        try
        {
            if (string.IsNullOrEmpty(_cstring))
                throw new Exception("Connection String not set");
            if (string.IsNullOrEmpty(query))
                throw new Exception("No query string provided");
            _checkVal.Data = _pp.Query<T>(query).ToList();
            _checkVal.Success = true;
            _checkVal.Message = "Successfully retrieved list based on custom query";
        }
        catch (Exception ex)
        {
            _checkVal.Data = null;
            _checkVal.Success = false;
            _checkVal.Message = ex.Message;
        }
        return _checkVal;
    }
    public static BoolMessage PpInsert(object obj)
    {
        _checkVal.Data = null;
        try
        {
            if (string.IsNullOrEmpty(_cstring))
                throw new Exception("No connection string provided");
            _pp.Insert(obj);
            _checkVal.Success = true;
            _checkVal.Message = string.Format("Successfully inserted {0}", obj.ToString());
        }
        catch (Exception ex)
        {
            _checkVal.Success = false;
            _checkVal.Message = ex.Message;
        }
        return _checkVal;
    }

    public static BoolMessage PpGetScalar<T>(string query)
    {
        _checkVal.Data = null;
        try
        {
            if (string.IsNullOrEmpty(_cstring))
                throw new Exception("No connection string provided");
            _checkVal.Data = _pp.ExecuteScalar<T>(query);
            _checkVal.Success = true;
            _checkVal.Message = string.Format("Successfully retrieved scalar");
        }
        catch (Exception ex)
        {
            _checkVal.Data = null;
            _checkVal.Success = false;
            _checkVal.Message = ex.Message;
        }
        return _checkVal;
    }
}

public class BoolMessage
{
    public bool Success { set; get; }
    public string Message { set; get; }
    public List<string> Messages { set; get; }
    public object Data { set; get; }
    public BoolMessage()
    {
        Success = false;
        Message = string.Empty;
        Messages = new List<string>();
        Data = null;
    }
}