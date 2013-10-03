/*
* Author: Edan Cain, ESRI, 380 New York Street, Redlands, California, 92373, USA. Email: ecain@esri.com Tel: 1-909-793-2853
*
* Code demonstrates how to structure REST calls for interaction with ArcGIS.com organization accounts. How to connect
* to a SQLService database table and pull the structure and records. The structure and records that are used to dynamically 
* create and populate with the signed in user's ArcGIS.com organisation account.
*
 * NB: A big TODO is that I was unable to successfully create a SQL database table that include either of their 
 * geometryType fields within my Windows Azure account. Therefore, there is no code herein that processes either 
 * datatype. Should be easy to read and copy the geometries to push to ArcGIS.com.
 * 
* Calls are made via HttpWebRequests based on string descriptors. Both GET and POST calls are within this code
* and response format is JSON.
*
* HttpWebResponses are dynamically binded too via the DataContract objects found within the AGOLRestHandler.dll. 
* The code and project that creates this dll can be found within my GitHub listings. It is not a comprehensive 
* codebase for all interactions with ArcGIS.com for Organizations, but does cover a great deal. 
* 
* Code is not supported by ESRI inc, there are no use restrictions, you are free to distribute, modify and use this code.
* Enhancement or functional code requests should be sent to Edan Cain, ecain@esri.com.
*
* Code only supports connection to SQLService databases and creation of Feature Services. It also shows how to create a
* unique value renderer for Polygon feature service data types.
 * 
 * NB: This is a work in progress. UI is functional if somewhat ugly. 
*
* Code created to help support the Start-up community by the ESRI Emerging Business Team. If you are a start up company,
* please contact Myles Sutherland at msutherland@esri.com.
*/

#region ESRI Types
/*    
  Constant	Value	Description
  esriFieldTypeSmallInteger	0	Short Integer.
  esriFieldTypeInteger	1	Long Integer.
  esriFieldTypeSingle	2	Single-precision floating-point number.
  esriFieldTypeDouble	3	Double-precision floating-point number.
  esriFieldTypeString	4	Character string.
  esriFieldTypeDate	5	Date.
  esriFieldTypeOID	6	Long Integer representing an object identifier.
  esriFieldTypeGeometry	7	Geometry.
  esriFieldTypeBlob	8	Binary Large Object.
  esriFieldTypeRaster	9	Raster.
  esriFieldTypeGUID	10	Globally Unique Identifier.
  esriFieldTypeGlobalID	11	ESRI Global ID.
  esriFieldTypeXML 
  */

/*Feature Edit Tools
http://help.arcgis.com/en/sdk/10.0/java_ao_adf/api/arcgiswebservices/com/esri/arcgisws/EsriFeatureEditTool.html
"esriFeatureEditToolNone"
"esriFeatureEditToolPoint"
"esriFeatureEditToolLine"
"esriFeatureEditToolPolygon"
"esriFeatureEditToolAutoCompletePolygon"
"esriFeatureEditToolCircle"
"esriFeatureEditToolEllipse"
"esriFeatureEditToolRectangle"
"esriFeatureEditToolFreehand"
*/

#endregion
using AGOLRestHandler;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace SQLtoFeatureService
{
  public partial class Form1 : Form
  {
    Dictionary<string, string> _sqlServerTableFieldNameAndTypeDictionary;
    FeatureEditsResponse _featureEditResponse;
    FeatureServiceCreationResponse _featureServiceCreationResponse;
    JavaScriptSerializer _javaScriptSerializer;
    List<string> _fieldNames;
    List<IDataRecord> _records;
    string _arcgGISOnlineOrganizationToken;
    string _arcgGISOnlineOrganizationID;
    string _arcgGISOnlineFeatureServiceURL;
    string _arcgGISOnlineOrganizationEndpoint;
    string _tableName;
    int _featureserviceErrors = 0;
    SqlConnection _SQLConnection;

    enum EditType
    {
      add,
      delete,
      update
    }
    enum GeometryType
    {
      point,
      line,
      polygon
    }

    public Form1()
    {
      InitializeComponent();

      DataGridViewColourPickerColumn col = new DataGridViewColourPickerColumn()
      {
        Name = "Unique Colors",
        Width = 200
      };
      dataGridView1.Columns.Add(col);
    }

    #region ESRI ArcGISOnline

    private string CompleteServiceUrl()
    {
      if (_featureServiceCreationResponse.ServiceUrl.EndsWith("/0/"))
        return _featureServiceCreationResponse.ServiceUrl + "applyEdits?f=pjson&token=" + _arcgGISOnlineOrganizationToken;
      else
        return _featureServiceCreationResponse.ServiceUrl + "/0/applyEdits?f=pjson&token=" + _arcgGISOnlineOrganizationToken;
    }

    private string GetJSONResponseString(string url, string jsonTransmission)
    {
      //create a request using the url that can recieve a POST
      HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(url);

      //stipulate that this request is a POST
      httpWebRequest.Method = "POST";

      //convert the data to send into a byte array.
      byte[] bytesToSend = Encoding.UTF8.GetBytes(jsonTransmission);

      //we need to declare the content length next
      httpWebRequest.ContentLength = bytesToSend.Length;

      //set the content type property 
      httpWebRequest.ContentType = "application/x-www-form-urlencoded";

      //get the request stream
      Stream dataStream = httpWebRequest.GetRequestStream();

      //write the data to the request stream
      dataStream.Write(bytesToSend, 0, bytesToSend.Length);

      //close it as we have no further use of it.
      dataStream.Close();

      //make the request to the server
      HttpWebResponse httpResponse = (HttpWebResponse)httpWebRequest.GetResponse();

      //return the JSON representation from the response
      return DeserializeResponse(httpResponse.GetResponseStream());
    }

    private string DeserializeResponse(System.IO.Stream stream)
    {
      string JSON = string.Empty;

      using (StreamReader reader = new StreamReader(stream))
        JSON = reader.ReadToEnd();

      return JSON;
    }

    private FeatureEditsResponse FeatureEditRequest(string baseURL, string jsonEdits)
    {
      FeatureEditsResponse addFeatDataContract = _javaScriptSerializer.Deserialize<FeatureEditsResponse>(GetJSONResponseString(baseURL, jsonEdits));
      return addFeatDataContract;
    }

    private UniqueValueInfo UniqueValues(string value, string label, string description, PolygonSymbol sym)
    {
      UniqueValueInfo uniqueValueInfo = new UniqueValueInfo()
      {
        value = value,
        label = label,
        description = description,
        symbol = sym
      };

      return uniqueValueInfo;
    }

    /// <summary>
    /// Code demonstrates how you would create a unique value renderer with 5 polygon unique symbol values
    /// </summary>
    /// <returns></returns>
    private UniqueValueInfos UniqueValues()
    {
      UniqueValueInfos infos = new UniqueValueInfos();
      infos.uniqueValueInfos = new UniqueValueInfo[5];

      PolygonSymbol symbol1 = new PolygonSymbol()
      {
        type = "esriSFS",
        style = "esriSLSSolid",
        color = new object[] { 0, 255, 255, 255 },
        outline = new Outline()
        {
          type = "esriSLS",
          style = "esriSLSSolid",
          color = new object[] { 110, 110, 110, 255 },
          width = 1
        }
      };
      infos.uniqueValueInfos[0] = UniqueValues("1", "1", "", symbol1);

      PolygonSymbol symbol2 = new PolygonSymbol()
      {
        type = "esriSFS",
        style = "esriSFSSolid",
        color = new object[] { 0, 191, 255, 255 },
        outline = new Outline()
        {
          type = "esriSLS",
          style = "esriSLSSolid",
          color = new object[] { 110, 110, 110, 255 },
          width = 1
        }
      };
      infos.uniqueValueInfos[1] = UniqueValues("2", "2", "", symbol2);

      PolygonSymbol symbol3 = new PolygonSymbol()
      {
        type = "esriSFS",
        style = "esriSFSSolid",
        color = new object[] { 0, 128, 255, 255 },
        outline = new Outline()
        {
          type = "esriSLS",
          style = "esriSLSSolid",
          color = new object[] { 110, 110, 110, 255 },
          width = 1
        }
      };
      infos.uniqueValueInfos[2] = UniqueValues("3", "3", "", symbol3);

      PolygonSymbol symbol4 = new PolygonSymbol()
      {
        type = "esriSFS",
        style = "esriSFSSolid",
        color = new object[] { 0, 64, 255, 255 },
        outline = new Outline()
        {
          type = "esriSLS",
          style = "esriSLSSolid",
          color = new object[] { 110, 110, 110, 255 },
          width = 1
        }
      };
      infos.uniqueValueInfos[3] = UniqueValues("4", "4", "", symbol2);

      PolygonSymbol symbol5 = new PolygonSymbol()
      {
        type = "esriSFS",
        style = "esriSFSSolid",
        color = new object[] { 0, 0, 255, 255 },
        outline = new Outline()
        {
          type = "esriSLS",
          style = "esriSLSSolid",
          color = new object[] { 110, 110, 110, 255 },
          width = 1
        }
      };
      infos.uniqueValueInfos[4] = UniqueValues("5", "5", "", symbol2);

      return infos;
    }

    /// <summary>
    /// Code to demonstrate creation of a polygon unique value renderer. 
    /// We need to create a default symbol, then create the other values that will be part of the 
    /// collection of unique symbols used. 
    /// The symbol used to display is based on a field value within the feature service that we
    /// are required to state the field name within the renderer.
    /// </summary>
    /// <returns></returns>
    private PolygonRenderer UniqueValueRender()
    {
      PolygonSymbol defaultSym = new PolygonSymbol()
      {
        type = "esriSFS",
        style = "esriSFSSolid",
        color = new object[] { 198, 245, 215, 255 },
        outline = new Outline()
          {
            type = "esriSLS",
            style = "esriSLSSolid",
            color = new object[] { 110, 110, 110, 255 },
            width = 1
          }
      };

      UniqueValueInfos infos = UniqueValues();

      PolygonRenderer renderer = new PolygonRenderer()
      {
        type = "uniqueValue",
        field1 = cboFields.Text,
        field2 = "",
        field3 = "",
        fieldDelimiter = "",
        defaultSymbol = defaultSym,
        defaultLabel = "<all other values>",
        uniqueValueInfos = infos.uniqueValueInfos
      };

      return renderer;
    }

    /// <summary>
    /// Function provides you with two separate work flows based on what happens in the UI. If a connection to 
    /// a SQL server table, the table structure will have been copied into the member variable _fieldNameTypeDict.
    /// If the user has described the field names and field types, we use that instead.
    /// </summary>
    /// <param name="fields"></param>
    private void Fields(List<object> fields)
    {
      //DO NOT CHANGE PROPERTIES FOR THE fieldFID
      Field fieldFID = new Field()
      {
        name = "OBJECTID",
        type = "esriFieldTypeInteger",
        alias = "OBJECTID",
        sqlType = "sqlTypeInteger",
        nullable = false,
        editable = false,
        domain = null,
        defaultValue = null
      };

      fields.Add(fieldFID);

      //The following loops through the dictionary of value pairs required for the feature service attribution 
      //table. Using the field type, create the appropriate field type and use the key value as the name of the
      //field. I have listed only field field types, please expand on this for your needs. Listed at the top of 
      //this field are the ESRI field types you can use.
      if (_sqlServerTableFieldNameAndTypeDictionary != null)
      {
        foreach (KeyValuePair<string, string> keyValuePair in _sqlServerTableFieldNameAndTypeDictionary)
        {
          Console.WriteLine("Key = {0}, Value = {1}", keyValuePair.Key, keyValuePair.Value);

          if (keyValuePair.Value == "nvarchar")
          {
            FieldString field0 = new FieldString()
            {
              name = keyValuePair.Key,
              type = "esriFieldTypeString",
              alias = keyValuePair.Key,
              sqlType = "sqlTypeNVarchar",
              length = 256,
              nullable = true,
              editable = true,
              domain = null,
              defaultValue = null
            };

            fields.Add(field0);
          }

          if (keyValuePair.Value == "float")
          {
            Field field1 = new Field()
            {
              name = keyValuePair.Key,
              type = "esriFieldTypeDouble",
              alias = keyValuePair.Key,
              sqlType = "sqlTypeFloat",
              nullable = true,
              editable = true,
              domain = null,
              defaultValue = null
            };

            fields.Add(field1);
          }

          if (keyValuePair.Value == "int")
          {
            Field fieldInt = new Field()
            {
              name = keyValuePair.Key,
              type = "esriFieldTypeInteger",
              alias = keyValuePair.Key,
              sqlType = "sqlTypeInteger",
              nullable = false,
              editable = false,
              domain = null,
              defaultValue = 0
            };

            fields.Add(fieldInt);
          }

          if (keyValuePair.Value == "char")
          {
            FieldString fieldchar = new FieldString()
            {
              name = keyValuePair.Key,
              type = "esriFieldTypeString",
              alias = keyValuePair.Key,
              sqlType = "sqlTypeNVarchar",
              length = 256,
              nullable = true,
              editable = true,
              domain = null,
              defaultValue = null
            };

            fields.Add(fieldchar);
          }

          if (keyValuePair.Value == "datetime")
          {
            FieldString fieldchar = new FieldString()
            {
              name = keyValuePair.Key,
              type = "esriFieldTypeDate",
              alias = keyValuePair.Key,
              sqlType = "sqlTypeDateTime",
              nullable = true,
              editable = true,
              domain = null,
              defaultValue = null
            };

            fields.Add(fieldchar);
          }
        }
      }
      else
      {
        foreach (DataGridViewRow row in dataGridViewFields.Rows)
        {
          if (row.Cells[1].FormattedValue.ToString() == "Text")
          {
            FieldString field0 = new FieldString()
            {
              name = row.Cells[0].FormattedValue.ToString(),
              type = "esriFieldTypeString",
              alias = row.Cells[0].FormattedValue.ToString(),
              sqlType = "sqlTypeNVarchar",
              length = 256,
              nullable = true,
              editable = true,
              domain = null,
              defaultValue = null
            };

            fields.Add(field0);
          }

          if (row.Cells[1].FormattedValue.ToString() == "Float" || row.Cells[1].FormattedValue.ToString() == "Double")
          {
            Field field1 = new Field()
            {
              name = row.Cells[0].FormattedValue.ToString(),
              type = "esriFieldTypeDouble",
              alias = row.Cells[0].FormattedValue.ToString(),
              sqlType = "sqlTypeFloat",
              nullable = true,
              editable = true,
              domain = null,
              defaultValue = null
            };

            fields.Add(field1);
          }

          if (row.Cells[1].FormattedValue.ToString() == "Raster")
          {
            Field field1 = new Field()
            {
              name = row.Cells[0].FormattedValue.ToString(),
              type = "esriFieldTypeRaster",
              alias = row.Cells[0].FormattedValue.ToString(),
              sqlType = "sqlTypeFloat", //??? Todo
              nullable = true,
              editable = true,
              domain = null,
              defaultValue = null
            };

            fields.Add(field1);
          }

          if (row.Cells[1].FormattedValue.ToString() == "Short" || row.Cells[1].FormattedValue.ToString() == "Long")
          {
            Field fieldInt = new Field()
            {
              name = row.Cells[0].FormattedValue.ToString(),
              type = "esriFieldTypeInteger",
              alias = row.Cells[0].FormattedValue.ToString(),
              sqlType = "sqlTypeInteger",
              nullable = false,
              editable = false,
              domain = null,
              defaultValue = 0
            };

            fields.Add(fieldInt);
          }

          if (row.Cells[1].FormattedValue.ToString() == "Date")
          {
            FieldString fieldchar = new FieldString()
            {
              name = row.Cells[0].FormattedValue.ToString(),
              type = "esriFieldTypeDate",
              alias = row.Cells[0].FormattedValue.ToString(),
              sqlType = "sqlTypeDateTime",
              nullable = true,
              editable = true,
              domain = null,
              defaultValue = null
            };

            fields.Add(fieldchar);
          }
        }
      }
    }

    /// <summary>
    /// From the color argument return the JSON representation of the color.
    /// </summary>
    /// <param name="color"></param>
    /// <returns></returns>
    private string ColorArrayString(Color color)
    {
      string strColor = "[" + color.R + "," + color.G + "," + color.B + "," + color.A + "]";
      return strColor;
    }

    /// <summary>
    /// Create JSON string representing the UI datagridview row values. Value, label, description, symbol, color
    /// </summary>
    /// <returns></returns>
    private string Colors()
    {
      DataGridViewColourPickerCell obj;
      Color color;
      string values = string.Empty;

      foreach (DataGridViewRow row in dataGridView1.Rows)
      {
        obj = row.Cells[3] as DataGridViewColourPickerCell;
        color = obj.BackColor;
        if (values.Length != 0)
          values += ",";

        values += "{\"value\": \"" + row.Cells["Value"].Value + "\",\"label\": \"" + row.Cells["Label"].Value + "\",\"description\": \"" + row.Cells["Description"].Value + "\",\"symbol\": {\"type\": \"esriSFS\",\"style\": \"esriSFSSolid\",\"color\": " + ColorArrayString(color) + ",\"outline\": {\"type\": \"esriSLS\",\"style\": \"esriSLSSolid\",\"color\": [110,110,110,255],\"width\": " + hScrollBar1.Value.ToString() + "}}}";
      }

      return values;
    }

    /// <summary>
    /// With the Feature Service having been created, lets add the attribution definition to 
    /// it to make the service a functional, fully qualified feature service..
    /// </summary>
    /// <param name="geometryType"></param>
    /// <returns></returns>
    private bool AddDefinitionToLayer(GeometryType geometryType)
    {
      Extent extent = null;
      PointSymbol symbol = null;
      Renderer renderer = null;
      DrawingInfo drawingInfo = null;
      List<object> fields = new List<object>();
      Template template = null;
      EditorTrackingInfo editorTrackingInfo = null;
      AdminLayerInfoAttribute adminLayerInfo = null;
      DefinitionLayer layer = null;
      string formattedRequest = string.Empty;
      string jsonResponse = string.Empty;
      FeatureLayerAttributes featLayerAttributes = null;

      this.Cursor = Cursors.WaitCursor;

      featLayerAttributes = new FeatureLayerAttributes();

      //write in your default extent values here:
      extent = new Extent()
      {
        xmin = -14999999.999999743,
        ymin = 1859754.5323447795,
        xmax = -6199999.999999896,
        ymax = 7841397.327701188,
        spatialReference = new SpatialReference() { wkid = 102100, latestWkid = 3857 },
      };

      var json = string.Empty;

      if (geometryType == GeometryType.point)
      {
        symbol = new PointSymbol()
        {
          type = "esriPMS",
          url = "RedSphere.png",
          imageData = "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",
          contentType = "image/png",
          color = null,
          width = 15,
          height = 15,
          angle = 0,
          xoffset = 0,
          yoffset = 0
        };

        renderer = new PointRenderer()
        {
          type = "simple",
          symbol = symbol,
          label = "",
          description = ""
        };
      }
      else //POLYGON: with unique value rendering
      {
         renderer = UniqueValueRender();
        json = new JavaScriptSerializer().Serialize(renderer);

        //json = "{\"renderer\" : {\"type\": \"uniqueValue\",\"field1\": \"" + cboFields.Text + "\",\"field2\": \"\",\"field3\": \"\",";
        //json += "\"fieldDelimiter\": \"\",\"defaultSymbol\": {\"type\": \"esriSFS\",\"style\": \"esriSFSSolid\",\"color\": [198,245,215,255],";
        //json += "\"outline\": {\"type\": \"esriSLS\",\"style\": \"esriSLSSolid\",\"color\": [110,110,110,255],\"width\": 1}},";
        //json += "\"defaultLabel\": \"<all other values>\",\"uniqueValueInfos\": [" + Colors();
        //json += "]}, \"transparency\" : " + hScrollBar2.Value + ", \"labelingInfo\": null}";
      }

      drawingInfo = new DrawingInfo()
      {
        renderer = json,//renderer,
        transparency = 50,
        labelingInfo = null
      };

      var drawinfo = new JavaScriptSerializer().Serialize(drawingInfo);

      Fields(fields);

      if (geometryType == GeometryType.polygon)
      {
        template = new Template()
        {
          name = "New Feature",
          description = "",
          drawingTool = "esriFeatureEditToolPolygon",
          prototype = new Prototype()
          {
            attributes = new Attributes()
          }
        };
      }

      editorTrackingInfo = new EditorTrackingInfo()
      {
        enableEditorTracking = false,
        enableOwnershipAccessControl = false,
        allowOthersToUpdate = true,
        allowOthersToDelete = true
      };

      adminLayerInfo = new AdminLayerInfoAttribute()
      {
        geometryField = new GeometryField()
        {
          name = "Shape",
          srid = 102100
        }
      };

      layer = new DefinitionLayer()
      {
        currentVersion = 10.11,
        id = 0,
        name = _featureServiceCreationResponse.Name,
        type = featLayerAttributes != null ? featLayerAttributes.type != null ? featLayerAttributes.type : "Feature Layer" : "Feature Layer",
        displayField = featLayerAttributes != null ? featLayerAttributes.displayField != null ? featLayerAttributes.displayField : "" : "",
        description = "",
        copyrightText = featLayerAttributes != null ? featLayerAttributes.copyrightText != null ? featLayerAttributes.copyrightText : "" : "",
        defaultVisibility = true,
        relationships = featLayerAttributes != null ? featLayerAttributes.relationShips != null ? featLayerAttributes.relationShips : new object[] { } : new object[] { },
        isDataVersioned = featLayerAttributes != null ? featLayerAttributes.isDataVersioned : false,
        supportsRollbackOnFailureParameter = true,
        supportsStatistics = true,
        supportsAdvancedQueries = true,
        geometryType = featLayerAttributes != null ? featLayerAttributes.geometryType != null ? featLayerAttributes.geometryType : "esriGeometryPolygon" : "esriGeometryPoint",
        minScale = featLayerAttributes != null ? featLayerAttributes.minScale : 0,
        maxScale = featLayerAttributes != null ? featLayerAttributes.maxScale : 0,
        extent = extent,
        drawingInfo = json,
        allowGeometryUpdates = true,
        hasAttachments = featLayerAttributes != null ? featLayerAttributes.hasAttachments : false,
        htmlPopupType = featLayerAttributes != null ? featLayerAttributes.htmlPopupType != null ? featLayerAttributes.htmlPopupType : "esriServerHTMLPopupTypeNone" : "esriServerHTMLPopupTypeNone",
        hasM = featLayerAttributes != null ? featLayerAttributes.hasM : false,
        hasZ = featLayerAttributes != null ? featLayerAttributes.hasZ : false,
        objectIdField = featLayerAttributes != null ? featLayerAttributes.objectIdField != null ? featLayerAttributes.objectIdField : "OBJECTID" : "OBJECTID",
        globalIdField = featLayerAttributes != null ? featLayerAttributes.globalIdField != null ? featLayerAttributes.globalIdField : "" : "",
        typeIdField = featLayerAttributes != null ? featLayerAttributes.typeIdField != null ? featLayerAttributes.typeIdField : "" : "",
        fields = fields.ToArray(),
        types = featLayerAttributes != null ? featLayerAttributes.types != null ? featLayerAttributes.types : new object[0] : new object[0],
        templates = new Template[1] { template },
        supportedQueryFormats = featLayerAttributes != null ? featLayerAttributes.supportedQueryFormats != null ? featLayerAttributes.supportedQueryFormats : "JSON" : "JSON",
        hasStaticData = featLayerAttributes != null ? featLayerAttributes.hasStaticData : false,
        maxRecordCount = 2000,
        capabilities = featLayerAttributes != null ? featLayerAttributes.capabilities != null ? featLayerAttributes.capabilities : "Query,Editing,Create,Update,Delete" : "Query,Editing,Create,Update,Delete",
        adminLayerInfo = adminLayerInfo
      };

      DefinitionLayer[] layers = new DefinitionLayer[1] { layer };

      AddDefinition definition = new AddDefinition()
      {
        layers = layers
      };

      string serviceEndPoint = "http://services.arcgis.com/";
      string serviceEndPoint2 = "http://services1.arcgis.com/";//NB: Trial Account endpoint!!!!

      string featureServiceName = _featureServiceCreationResponse.Name;

      if (featureServiceName == null)
        featureServiceName = txtFeatureServiceName.Text;

      string requestURL = string.Format("{0}{1}/arcgis/admin/services/{2}.FeatureServer/AddToDefinition", serviceEndPoint, _arcgGISOnlineOrganizationID, _featureServiceCreationResponse.Name);

      bool b = RequestAndResponseHandler.AddToFeatureServiceDefinition(requestURL, definition, _arcgGISOnlineOrganizationToken, _arcgGISOnlineOrganizationEndpoint, out formattedRequest, out jsonResponse);
      label32.Text = "Success: False";
      this.Cursor = Cursors.Default;
      
      if (!b)
      {
        requestURL = string.Format("{0}{1}/arcgis/admin/services/{2}.FeatureServer/AddToDefinition", serviceEndPoint2, _arcgGISOnlineOrganizationID, _featureServiceCreationResponse.Name);
        b = RequestAndResponseHandler.AddToFeatureServiceDefinition(requestURL, definition, _arcgGISOnlineOrganizationToken, _arcgGISOnlineOrganizationEndpoint, out formattedRequest, out jsonResponse);
      }

      if (b)
      {
        label32.Text = "Success: True";
        //TODO: add records
        btnUpdate.Enabled = true;
      }

      return b;
    }

    /// <summary>
    /// Function to create the calls to the ArcGIS.com server to authorize the user
    /// against their Organisation account.
    /// </summary>
    /// <returns></returns>
    private Authentication AuthorizeAgainstArcGISOnline()
    {
      string url = "https://www.arcgis.com/sharing/generatetoken?f=json";
      string jsonTransmission = "username=" + txtAGOUserName.Text + "&password=" + txtAGOPassword.Text + "&expiration=100080&referer=" + "http://startups.maps.arcgis.com/" + "&f=pjson";
      //create a request using the url that can recieve a POST
      string JSON = string.Empty;
      try
      {
        string strOut = string.Empty;
        JSON = RequestAndResponseHandler.HttpWebRequestHelper(url, jsonTransmission, "http://startups.maps.arcgis.com/", out strOut);

        AGOL_Error AGOLError;
        if (JSON.Contains("error"))
        {
          _javaScriptSerializer = new JavaScriptSerializer();
          AGOLError = _javaScriptSerializer.Deserialize<AGOL_Error>(JSON);
          label1.Text = AGOLError.error.code + ". " + AGOLError.error.message + ". " + AGOLError.error.details[0];
          return null;
        }
      }
      catch
      {
        return null;
      }

      JavaScriptSerializer jScriptSerializer = new JavaScriptSerializer();
      Authentication authenticationDataContract = jScriptSerializer.Deserialize<Authentication>(JSON) as Authentication;
      _arcgGISOnlineOrganizationToken = authenticationDataContract.token;
      label1.Text = "Token: " + _arcgGISOnlineOrganizationToken;
      return authenticationDataContract;
    }

    /// <summary>
    /// Is the feature service name available within the user's organisation account?
    /// Attempts to create a feature service with a name already in use will result
    /// in failure. Check prior to doing so.
    /// </summary>
    /// <returns></returns>
    private bool IsDesiredFeatureServiceNameAvailable()
    {
      this.Cursor = Cursors.WaitCursor;

      string formattedRequest = string.Empty;
      string jsonResponse = string.Empty;

      _tableName = txtFeatureServiceName.Text;

      if (string.IsNullOrEmpty(_tableName))
        _tableName = txtFeatureServiceName.Text;

      if (string.IsNullOrEmpty(_tableName))
      {
        this.Cursor = Cursors.Default;
        return false;
      }

      if (!_arcgGISOnlineOrganizationEndpoint.EndsWith("/"))
        _arcgGISOnlineOrganizationEndpoint += "/";

      _arcgGISOnlineOrganizationEndpoint += string.Format("sharing/portals/{0}", _arcgGISOnlineOrganizationID);

      //http://startups.maps.arcgis.com/sharing/portals/q7zPNeKmTWeh7Aor
      bool isAvailable = RequestAndResponseHandler.IsFeatureServiceNameAvailable(/*txtFeatureServiceName.Text*/_tableName, _arcgGISOnlineOrganizationEndpoint, _arcgGISOnlineOrganizationToken, _arcgGISOnlineOrganizationEndpoint, out formattedRequest, out jsonResponse);

      this.Cursor = Cursors.Default;

      grpbxCreateFeatureService.Enabled = isAvailable;

      if (isAvailable)
      {
        btnCreateFeatureService.Enabled = true;
        lblFSAvailable.Text = "Available: True";
        return true;
      }
      else
      {
        btnCreateFeatureService.Enabled = false;
        btnAddDefinitionToLayer.Enabled = false;
        lblFSAvailable.Text = "Available: False";
        return false;
      }
    }

    /// <summary>
    /// Get data on Organizational content, and that of the user specific.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void Self()
    {
      //self
      this.Cursor = Cursors.WaitCursor;
      string formattedRequest;
      string responseJSON;

      Self response = RequestAndResponseHandler.SelfWebRequest("http://www.arcgis.com/sharing/rest/community/self", _arcgGISOnlineOrganizationToken, out formattedRequest, out responseJSON);

      _arcgGISOnlineOrganizationID = response.orgId;
      label2.Text = "Self: Organization ID = " + _arcgGISOnlineOrganizationID;

      label12.Enabled = txtFeatureServiceName.Enabled = true;
      this.Cursor = Cursors.Default;
    }

    private bool CreateFeatureService()
    {
      this.Cursor = Cursors.WaitCursor;

      string formattedRequest = string.Empty;
      string jsonResponse = string.Empty;

      string serviceURL = string.Format("{0}sharing/content/users/{1}/createService", _arcgGISOnlineOrganizationEndpoint, txtAGOUserName.Text);

      _featureServiceCreationResponse = RequestAndResponseHandler.CreateNewFeatureService(_tableName, serviceURL, _arcgGISOnlineOrganizationToken, _arcgGISOnlineOrganizationEndpoint + "home/content.html", out formattedRequest, out jsonResponse);
      if (_featureServiceCreationResponse == null)
      {
        _featureserviceErrors++;
        label16.Text = "Errors: " + _featureserviceErrors.ToString();
        return false;
      }

      lblCreateFSSuccess.Text = "Success: " + _featureServiceCreationResponse.Success.ToString();
      lblFeatureServiceURL.Text = "URL: " + _featureServiceCreationResponse.ServiceUrl;
      _arcgGISOnlineFeatureServiceURL = _featureServiceCreationResponse.ServiceUrl;
      lblItemID.Text = "ID: " + _featureServiceCreationResponse.ServiceItemId;

      try
      {
        btnAddDefinitionToLayer.Enabled = true;
        groupBox8.Enabled = _featureServiceCreationResponse.Success;
        btnCreateFeatureService.Enabled = false;
      }
      catch (Exception ex)
      {
        Console.WriteLine(ex.Message);
        return false;
      }
      this.Cursor = Cursors.Default;
      return true;
    }

    /// <summary>
    /// This operation adds, updates and deletes features to the associated feature layer or table(POST only).
    /// </summary>
    /// <param name="type"></param>
    private void EditFeatureService(EditType type, GeometryType geomType, MyPoint myPoint, MyPoly myPoly)
    {
      string formattedRequest = string.Empty;
      string jsonResponse = string.Empty;
      string jsonToSend = string.Empty;
      string attributes = string.Empty;
      string geometry = string.Empty;

      string url = CompleteServiceUrl();

      switch (type)
      {
        case EditType.add:
          {
            attributes = "\"attributes\":{\"";
            int counter = 0;

            if (_records != null)
            {
              foreach (IDataRecord record in _records)
              {
                foreach (string field in _fieldNames)
                {
                  attributes += string.Format("{0}\":\"{1}\",\"", field, record[counter].ToString());
                  counter++;
                }

                break;
              }
            }

            if (counter == 0) //no records added. Create a dummy record.
            {
              foreach (DataGridViewRow row in dataGridViewFields.Rows)
              {
                //todo: did I set a default color symbol?
                attributes += string.Format("{0}\":\"{1}\",\"", row.Cells[0].FormattedValue.ToString(), null);
              }
            }

            //remove the excess. Meaning remove the trailing rubbish
            attributes = attributes.Remove(attributes.Length - 8);

            if (geomType == GeometryType.polygon)
            {
              //Polygon geometry
              MyPoint p;
              jsonToSend = "adds=[{\"geometry\":{\"rings\":[[";
              for (int i = 0; i < myPoly.Ring.Count; i++)
              {
                p = myPoly.Ring[i];
                jsonToSend += "[" + p.X + "," + p.Y + "],";
              }
              jsonToSend = jsonToSend.Remove(jsonToSend.Length - 1, 1);
              jsonToSend += "]],\"spatialReference\":{\"wkid\":102100}}," + attributes + "}}]";

              //example
              // [{"geometry":{"rings":[[[-8304737.273855386,5018862.730810074],[-8286086.638953812,5017945.486470653],[-8280583.172917282,5006632.806284452],
              //[-8303820.029515964,4995931.622324532],[-8322164.916304397,5006938.554397592],[-8304737.273855386,5018862.730810074]]],
              //"spatialReference":{"wkid":102100}},"attributes":{"BUFF_DIST":"3","BufferArea":null,"BufferPerimeter":null}}]
            
            }
            else
            {
              //NB: Sample does not provide an entry method for the user to enter an XY for point geom types. 
              //this is for demo purposes with a point feature service with the spatial ref as set below.
              //Users of this code need to build this functionality into their app, either with text input or map click.
              //Supplied XY places a point on the Coronado Bridge, San Diego, California

              jsonToSend = "adds=[{\"geometry\":{\"x\":" + myPoint.X + ",\"y\":" + myPoint.Y + ",\"spatialReference\":{\"wkid\":102100}}," + attributes + "}}]";
             }

            break;
          }
        case EditType.delete:
          {
            break;
          }
        case EditType.update:
          {
            break;
          }
        default:
          break;
      }

      //Make the HttpWebRequest
      _featureEditResponse = RequestAndResponseHandler.FeatureEditRequest(url, jsonToSend, out jsonResponse);

      switch (type)
      {
        case EditType.add:
          {
            if (_featureEditResponse.addResults == null)
              break;

            lblEditingResponse.Text = string.Format("Success: {0}, ObjectID: {1}, GlobalID: {2}, Error: {3}", _featureEditResponse.addResults[0].success,
              _featureEditResponse.addResults[0].objectId, _featureEditResponse.addResults[0].globalId, _featureEditResponse.addResults[0].error);

            break;
          }
        case EditType.delete:
          {
            break;
          }
        case EditType.update:
          {
            break;
          }
        default:
          break;
      }
    }

    #endregion

    #region SQL and MySQL

    /// <summary>
    /// Create a SqlServer database connection, constructor requires a string of attribution for the connection
    /// </summary>
    private void SqlConnection()
    {
      _SQLConnection = new SqlConnection("user id=" + txtSQLUserName.Text + 
                                       ";password=" + txtSQLPassword.Text +
                                       ";server=" + txtSQLServer.Text +
                                       ";Trusted_Connection=yes;" +
                                       "database=" + txtSQLDatabase.Text +
                                       ";connection timeout=30");
    }

    /// <summary>
    /// Function reads all of the all of the SqlDataReader Field names and dataType 
    /// and populate our in memory dictionary that will be used when creating the 
    /// same structure within a new feature service.
    /// </summary>
    /// <param name="myReader"></param>
    private void ReadTableColumnNamesAndType(SqlDataReader myReader)
    {
      if (_fieldNames == null)
        _fieldNames = new List<string>();

      for (int i = 0; i < myReader.FieldCount; i++)
      {
        string name = myReader.GetName(i);
        IDataRecord record = (IDataRecord)myReader;

        string type = record.GetFieldType(i).ToString();
        type = record.GetDataTypeName(i).ToString();

        //add the field name and the data type to our dictionary.
        _sqlServerTableFieldNameAndTypeDictionary.Add(name, type);

        //display values to the user
        PopulateFieldDataGridView(name, type);

        //list the name in the ui combobox. Used to selection of field
        //to use for Unique value renderer value.
        cboFields.Items.Add(name);

        //TODO: could perhaps remove this member var and simply maintain 
        //only one collection of field names above.
        _fieldNames.Add(name);
      }
    }

    /// <summary>
    /// To make the datagrid field data types resemble those of an ArcMap field type picker,
    /// perform a little behind the scenes Text changes.
    /// </summary>
    /// <param name="name"></param>
    /// <param name="type"></param>
    private void PopulateFieldDataGridView(string name, string type)
    {
      if (type == "nvarchar" || type == "char")
      {
        string[] row = { name, "Text" };
        dataGridViewFields.Rows.Add(row);
      }

      if (type == "float")
      {
        string[] row = { name, "Float" };
        dataGridViewFields.Rows.Add(row);
      }

      if (type == "int")
      {
        string[] row = { name, "Short" };
        dataGridViewFields.Rows.Add(row);
      }

      if (type == "datetime")
      {
        string[] row = { name, "Date" };
        dataGridViewFields.Rows.Add(row);
      }
    }

    /// <summary>
    /// With the SqlServer connection settings given within the UI, attempt a connection
    /// </summary>
    /// <returns></returns>
    private bool ConnectToSqlServer()
    {
      try
      {
        _SQLConnection.Open();
        SqlDataReader myReader = null;
        _tableName = txtSQLTableName.Text;

        using (SqlCommand myCommand = new SqlCommand("select * from dbo." + _tableName, _SQLConnection))
        {
          myReader = myCommand.ExecuteReader();

          string name = string.Empty;
          string type = string.Empty;

          _sqlServerTableFieldNameAndTypeDictionary = new Dictionary<string, string>();
          ReadTableColumnNamesAndType(myReader);

          _records = new List<IDataRecord>();

          while (myReader.Read())
            _records.Add(myReader);

          // Call Close when done reading.
          myReader.Close();
        }
      }
      catch (Exception e)
      {
        Console.WriteLine(e.ToString());
        return false;
      }

      //close the connection
      _SQLConnection.Close();
      return true;
    }

    #endregion

    #region Button Events

    private void btnCreateFeatureService_Click(object sender, EventArgs e)
    {
      CreateFeatureService();
    }

    private void btnAddDefinitionToLayer_Click(object sender, EventArgs e)
    {
      GeometryType type = rdBtnPoint.Checked ? GeometryType.point : GeometryType.polygon;
      AddDefinitionToLayer(type);
    }

    private void btnAGOLConnect_Click(object sender, EventArgs e)
    {
      Authentication auth = AuthorizeAgainstArcGISOnline();

      if (auth != null)
      {
        //make the self call
        Self();
        grpbxFeatureServiceName.Enabled = true;
        _arcgGISOnlineOrganizationEndpoint = txtOrgURL.Text;

        if (!_arcgGISOnlineOrganizationEndpoint.EndsWith("/"))
          _arcgGISOnlineOrganizationEndpoint += "/";
      }
    }

    private void btnIsNameAvailable_Click(object sender, EventArgs e)
    {
      IsDesiredFeatureServiceNameAvailable();
    }

    private void btnConnect_Click(object sender, EventArgs e)
    {
      //SQL
      _sqlServerTableFieldNameAndTypeDictionary = null;
      _records = null;
      dataGridViewFields.Rows.Clear();
      cboFields.Items.Clear();

      SqlConnection();
      bool sqlConnected = ConnectToSqlServer();
    }

    private void btnUpdate_Click(object sender, EventArgs e)
    {
      UpdateData();
    }

    private void UpdateData()
    {
      FieldInfo[] fldInfos = new FieldInfo[1]{new FieldInfo()
      {
        isEditable = false,
        stringFieldOption = "textbox",
        tooltip = "Field values pertaining to unique color ranges",
        label = "Unique Values",
        visible = true,
        fieldname = cboFields.Text
      }};

      UpdateLayer layer = new UpdateLayer()
      {
        id = 0,
        popupInfo = new PopupInfo()
        {
          showAttachments = false,
          fieldInfos = fldInfos,
          description = null,
          label = "Unique value renderer",
        }
      };

      Extent extent = new Extent()
      {
        xmin = -14999999.999999743,
        ymin = 1859754.5323447795,
        xmax = -6199999.999999896,
        ymax = 7841397.327701188,
        spatialReference = new SpatialReference() { wkid = 102100 },
      };

      Part part = new Part()
      {
        extent = extent,
        frameids = new int[] { 0 }
      };

      AnalysisInfo analysisInfoTEST = new AnalysisInfo()
      {
        toolName = "",
        jobParams = new JobParams()
        {
          InputLayer = new InputLayer()
          {//TODO input layer endpoint
            url = _arcgGISOnlineFeatureServiceURL + "/0", //"http://services.arcgis.com/q7zPNeKmTWeh7Aor/arcgis/rest/services/nyvoters_Party/FeatureServer/0",
            serviceToken = _arcgGISOnlineOrganizationToken
          },
          OutputName = new OutputName()
          {
            serviceProperties = new ServiceProperty()
            {
              name = "Unique_Values"
            },
            itemProperties = new ItemProperties()
            {
              itemID = _featureServiceCreationResponse.ServiceItemId,
            }
          },
          context = new Context()
          {
            extent = extent,
            parts = new Part[1] { part }
          }
        }
      };

      Update update = new AGOLRestHandler.Update()
      {
        title = txtFeatureServiceName.Text,
        description = string.Empty,
        tags = "",
        extent = extent,
        thumbnailURL = "http://services.arcgisonline.com/ArcGIS/rest/services/World_Topo_Map/MapServer/export?size=200,133&bboxSR=4326&format=png24&f=image&bbox=-161.697,-79.691,161.697,85",
        typeKeywords = "ArcGIS Server,Data,Feature Access,Feature Service,jobUrl:http://analysis.arcgis.com/arcgis/rest/services/tasks/GPServer/CreateBuffers/jobs/j6a67709cac1e4568a26d32568d01a695,Service,Hosted Service",
        text = new AGOLRestHandler.Text()
        {
          layers = new UpdateLayer[1] { layer },
          analysisInfo = analysisInfoTEST
        }
      };

      //
      string url = "http://startups.maps.arcgis.com/sharing/content/users/edan/items/" + _featureServiceCreationResponse.ServiceItemId + "/update";
      string jsontest = RequestAndResponseHandler.UpdateFeatureService(update, url);

      btnUpdate.Enabled = false;
    }

    private void btnCreatePoly_Click(object sender, EventArgs e)
    {
      MyPoly poly = new MyPoly();
      MyPoint p0 = new MyPoint(-8167838.556196676, 5040265.098729953);
      poly.Ring.Add(p0);
      MyPoint p1 = new MyPoint(-8165392.571291549, 5036137.499202552);
      poly.Ring.Add(p1);
      MyPoint p2 = new MyPoint(-8166004.06751783, 5035678.877032841);
      poly.Ring.Add(p2);
      MyPoint p3 = new MyPoint(-8171354.659497795, 5035678.877032841);
      poly.Ring.Add(p3);
      MyPoint p4 = new MyPoint(-8167838.556196676, 5040265.098729953);
      poly.Ring.Add(p4);

      EditFeatureService(EditType.add, GeometryType.polygon, null, poly);
    }

    private void btnCreatePoint_Click(object sender, EventArgs e)
    {
      MyPoint point = new MyPoint(-13041634.9497585, 3853952.46755234);
      EditFeatureService(EditType.add, GeometryType.point, point, null);
    }

    #endregion

    #region UI Interaction
    private void dataGridViewFields_CellValueChanged(object sender, DataGridViewCellEventArgs e)
    {
      if (e.RowIndex == -1)
        return;

      if (e.ColumnIndex == 1)
        return;

      object obj = dataGridViewFields[e.ColumnIndex, e.RowIndex];
      DataGridViewCell cell = obj as DataGridViewCell;

      if (e.RowIndex < dataGridViewFields.Rows.Count - 1)
        cboFields.Items.Insert(e.RowIndex, cell.EditedFormattedValue);
      else
        cboFields.Items.Add(cell.EditedFormattedValue);
    }

    private void hScrollBar1_Scroll(object sender, ScrollEventArgs e)
    {
      lblScroll.Text = hScrollBar1.Value.ToString();
    }

    private void hScrollBar2_Scroll(object sender, ScrollEventArgs e)
    {
      lblTransparency.Text = hScrollBar2.Value.ToString();
    }

    private void rdBtnPolygon_CheckedChanged(object sender, EventArgs e)
    {
      dataGridView1.Enabled = rdBtnPolygon.Checked;
    }

    #endregion

    public class MyPoint
    {
      public MyPoint(double x, double y)
      {
        X = x;
        Y = y;
      }

      public double X { get; set; }
      public double Y { get; set; }
    }

    public class MyPoly
    {
      public MyPoly()
      {
        Ring = new List<MyPoint>();
      }

      public List<MyPoint> Ring { get; set; }
    }
  }
}
