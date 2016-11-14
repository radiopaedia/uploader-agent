using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Text;
using System.IO;
using System.Net;

using ClearCanvas.Common;
using ClearCanvas.Dicom;
using ClearCanvas.Dicom.Codec;
using ClearCanvas.Dicom.Network;



/// <summary>
/// Summary description for DicomSCP
/// </summary>
public class DicomSCP : IDicomServerHandler
{
    #region Private Properties    
    private static ServerAssociationParameters _staticAssocParameters;
    private ServerAssociationParameters _assocParameters;

    private static bool isRunning = false;
    private string _aet;
    private int _port = 104; // Default to port 104
    private string _storePath;
    #endregion

    #region Public Properties
    public string Aet
    {
        get { return _aet; }
        set { _aet = value; }
    }

    public int Port
    {
        get { return _port; }
        set { _port = value; }
    }



    public bool IsRunning
    { get { return isRunning; } }
    #endregion

    #region Constructors
    private DicomSCP(ServerAssociationParameters assoc)
    {
        _assocParameters = assoc;
    }


    public DicomSCP(string aet, int port)
    {
        _aet = aet;
        _port = port;
    }
    #endregion

    /// <summary>
    /// Starts the service.
    /// </summary>
    /// <returns>True/False depending if service started successfully.</returns>
    public bool Start()
    {
        try
        {
            if (isRunning)
                return true;


            _staticAssocParameters = new ServerAssociationParameters(_aet, new IPEndPoint(IPAddress.Any, _port));


            AddPresentationContexts(_staticAssocParameters);

            if (DicomServer.StartListening(_staticAssocParameters,
                delegate (DicomServer server, ServerAssociationParameters assoc)
                {
                    return new DicomSCP(assoc)
                    {
                        Aet = _aet,
                        Port = _port
                    };
                }))
            {

                isRunning = true;
            }

        }
        catch (Exception ex)
        {


            throw ex;
        }

        return isRunning;
    }


    /// <summary>
    /// Stops the service.
    /// </summary>
    public void Stop()
    {
        try
        {
            if (isRunning)
            {
                DicomServer.StopListening(_staticAssocParameters);
            }

        }

        finally
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();

            isRunning = false;
        }
    }

    /// <summary>
    /// Association request.
    /// </summary>
    /// <param name="server"></param>
    /// <param name="association"></param>
    void IDicomServerHandler.OnReceiveAssociateRequest(DicomServer server, ServerAssociationParameters association)
    {

        server.SendAssociateAccept(association);
        ClearCanvas.Common.Platform.Log(LogLevel.Info, "Association request received from {0} ({1}).", association.CallingAE, association.RemoteEndPoint.Address);

    }

    /// <summary>
    /// Process C-Echo and C-Store request.
    /// </summary>
    /// <param name="server"></param>
    /// <param name="association"></param>
    /// <param name="presentationID"></param>
    /// <param name="message"></param>
    void IDicomServerHandler.OnReceiveRequestMessage(DicomServer server, ServerAssociationParameters association, byte presentationID, DicomMessage message)
    {

        if (message.CommandField == DicomCommandField.CEchoRequest)
        {
            server.SendCEchoResponse(presentationID, message.MessageId, DicomStatuses.Success);
            return;
        }
        else if (message.CommandField != DicomCommandField.CStoreRequest)
        {
            server.SendCEchoResponse(presentationID, message.MessageId, DicomStatuses.UnrecognizedOperation);
            return;
        }
        else if (message.CommandField == DicomCommandField.CStoreRequest)
        {
            Platform.Log(LogLevel.Info, message.DataSet.DumpString);

            ClearCanvas.Common.Platform.Log(LogLevel.Info, "C-Store request for {0}.", message.MessageId);
            String studyInstanceUid = null;
            String seriesInstanceUid = null;
            DicomUid sopInstanceUid;
            String patientName = null;

            bool ok = message.DataSet[DicomTags.SopInstanceUid].TryGetUid(0, out sopInstanceUid);
            if (ok) ok = message.DataSet[DicomTags.SeriesInstanceUid].TryGetString(0, out seriesInstanceUid);
            if (ok) ok = message.DataSet[DicomTags.StudyInstanceUid].TryGetString(0, out studyInstanceUid);
            if (ok) ok = message.DataSet[DicomTags.PatientsName].TryGetString(0, out patientName);

            //if (!ok)
            //{

            //    server.SendCStoreResponse(presentationID, message.MessageId, sopInstanceUid.UID, DicomStatuses.ProcessingFailure);
            //    return;
            //}

            try
            {
                // You can save the file by using this
                _storePath = ADCM.GetStoreString();
                if (string.IsNullOrEmpty(_storePath))
                    throw new Exception("No store path provided");
                string studyfolder = Path.Combine(_storePath, studyInstanceUid);
                studyfolder = Path.Combine(studyfolder, seriesInstanceUid);

                if (!Directory.Exists(studyfolder))
                    Directory.CreateDirectory(studyfolder);
                string filename = Path.Combine(studyfolder, message.DataSet[DicomTags.SopInstanceUid].ToString() + ".dcm");
                DicomFile file = new DicomFile(message, filename);
                file.Save(filename, DicomWriteOptions.Default);
                ClearCanvas.Common.Platform.Log(ClearCanvas.Common.LogLevel.Info, "Sending C-Store success response.");
                server.SendCStoreResponse(presentationID, message.MessageId, sopInstanceUid.UID, DicomStatuses.Success);
            }
            catch (Exception ex)
            {
                ClearCanvas.Common.Platform.Log(LogLevel.Error, ex, "Unable to store request {0}.", message.MessageId);

                server.SendCStoreResponse(presentationID, message.MessageId, sopInstanceUid != null ? sopInstanceUid.UID : string.Empty, DicomStatuses.ProcessingFailure);
            }

        }
    }

    public void OnReceiveDimseCommand(DicomServer server, ServerAssociationParameters association, byte presentationId,
                                     DicomAttributeCollection command)
    {

    }

    public IDicomFilestreamHandler OnStartFilestream(DicomServer server, ServerAssociationParameters association,
                                                    byte presentationId, DicomMessage message)
    {
        // Should not be called because OnReceiveDimseCommand isn't doing anything
        throw new NotImplementedException();
    }
    void IDicomServerHandler.OnReceiveResponseMessage(DicomServer server, ServerAssociationParameters association, byte presentationID, DicomMessage message)
    {
        ClearCanvas.Common.Platform.Log(LogLevel.Error, "Unexpectedly received response mess on server.");

        server.SendAssociateAbort(DicomAbortSource.ServiceUser, DicomAbortReason.UnrecognizedPDU);
    }



    void IDicomServerHandler.OnReceiveReleaseRequest(DicomServer server, ServerAssociationParameters association)
    {
        ClearCanvas.Common.Platform.Log(LogLevel.Info, "Received association release request from  {0}.", association.CallingAE);
    }

    void IDicomServerHandler.OnReceiveAbort(DicomServer server, ServerAssociationParameters association, DicomAbortSource source, DicomAbortReason reason)
    {
        ClearCanvas.Common.Platform.Log(LogLevel.Error, "Unexpected association abort received.");
    }

    void IDicomServerHandler.OnNetworkError(DicomServer server, ServerAssociationParameters association, Exception e)
    {
        if (e != null)
            ClearCanvas.Common.Platform.Log(LogLevel.Error, "Unexpected network error over association from {0}.\r\n{1}", association.CallingAE, e.Message);
    }

    void IDicomServerHandler.OnDimseTimeout(DicomServer server, ServerAssociationParameters association)
    {

        ClearCanvas.Common.Platform.Log(LogLevel.Info, "Received DIMSE Timeout, continuing listening for messages");
    }

    /// <summary>
    /// Adds supported presentation contexts for association.
    /// </summary>
    /// <param name="assoc"></param>
    private static void AddPresentationContexts(ServerAssociationParameters assoc)
    {

        byte pcid = assoc.AddPresentationContext(SopClass.VerificationSopClass);
        assoc.AddTransferSyntax(pcid, TransferSyntax.ImplicitVrLittleEndian);

        pcid = assoc.AddPresentationContext(SopClass.BasicTextSrStorage);
        assoc.AddTransferSyntax(pcid, TransferSyntax.ImplicitVrLittleEndian);

        pcid = assoc.AddPresentationContext(SopClass.MrImageStorage);
        assoc.AddTransferSyntax(pcid, TransferSyntax.ImplicitVrLittleEndian);

        pcid = assoc.AddPresentationContext(SopClass.CtImageStorage);
        assoc.AddTransferSyntax(pcid, TransferSyntax.ImplicitVrLittleEndian);

        pcid = assoc.AddPresentationContext(SopClass.SecondaryCaptureImageStorage);
        assoc.AddTransferSyntax(pcid, TransferSyntax.ImplicitVrLittleEndian);

        pcid = assoc.AddPresentationContext(SopClass.UltrasoundImageStorage);
        assoc.AddTransferSyntax(pcid, TransferSyntax.ImplicitVrLittleEndian);

        pcid = assoc.AddPresentationContext(SopClass.UltrasoundImageStorageRetired);
        assoc.AddTransferSyntax(pcid, TransferSyntax.ImplicitVrLittleEndian);


        pcid = assoc.AddPresentationContext(SopClass.UltrasoundMultiFrameImageStorage);
        assoc.AddTransferSyntax(pcid, TransferSyntax.ImplicitVrLittleEndian);

        pcid = assoc.AddPresentationContext(SopClass.UltrasoundMultiFrameImageStorageRetired);
        assoc.AddTransferSyntax(pcid, TransferSyntax.ImplicitVrLittleEndian);

        pcid = assoc.AddPresentationContext(SopClass.NuclearMedicineImageStorage);
        assoc.AddTransferSyntax(pcid, TransferSyntax.ImplicitVrLittleEndian);

        pcid = assoc.AddPresentationContext(SopClass.DigitalIntraOralXRayImageStorageForPresentation);
        assoc.AddTransferSyntax(pcid, TransferSyntax.ImplicitVrLittleEndian);

        pcid = assoc.AddPresentationContext(SopClass.DigitalIntraOralXRayImageStorageForProcessing);
        assoc.AddTransferSyntax(pcid, TransferSyntax.ImplicitVrLittleEndian);

        pcid = assoc.AddPresentationContext(SopClass.DigitalMammographyXRayImageStorageForPresentation);
        assoc.AddTransferSyntax(pcid, TransferSyntax.ImplicitVrLittleEndian);


        pcid = assoc.AddPresentationContext(SopClass.DigitalMammographyXRayImageStorageForProcessing);
        assoc.AddTransferSyntax(pcid, TransferSyntax.ImplicitVrLittleEndian);

        pcid = assoc.AddPresentationContext(SopClass.DigitalXRayImageStorageForPresentation);
        assoc.AddTransferSyntax(pcid, TransferSyntax.ImplicitVrLittleEndian);

        pcid = assoc.AddPresentationContext(SopClass.DigitalXRayImageStorageForProcessing);
        assoc.AddTransferSyntax(pcid, TransferSyntax.ImplicitVrLittleEndian);

        pcid = assoc.AddPresentationContext(SopClass.ComputedRadiographyImageStorage);
        assoc.AddTransferSyntax(pcid, TransferSyntax.ImplicitVrLittleEndian);


        pcid = assoc.AddPresentationContext(SopClass.GrayscaleSoftcopyPresentationStateStorageSopClass);
        assoc.AddTransferSyntax(pcid, TransferSyntax.ImplicitVrLittleEndian);


        pcid = assoc.AddPresentationContext(SopClass.KeyObjectSelectionDocumentStorage);
        assoc.AddTransferSyntax(pcid, TransferSyntax.ImplicitVrLittleEndian);


        pcid = assoc.AddPresentationContext(SopClass.OphthalmicPhotography16BitImageStorage);
        assoc.AddTransferSyntax(pcid, TransferSyntax.ImplicitVrLittleEndian);


        pcid = assoc.AddPresentationContext(SopClass.OphthalmicPhotography8BitImageStorage);
        assoc.AddTransferSyntax(pcid, TransferSyntax.ImplicitVrLittleEndian);


        pcid = assoc.AddPresentationContext(SopClass.VideoEndoscopicImageStorage);
        assoc.AddTransferSyntax(pcid, TransferSyntax.ImplicitVrLittleEndian);


        pcid = assoc.AddPresentationContext(SopClass.VideoMicroscopicImageStorage);
        assoc.AddTransferSyntax(pcid, TransferSyntax.ImplicitVrLittleEndian);


        pcid = assoc.AddPresentationContext(SopClass.VideoPhotographicImageStorage);
        assoc.AddTransferSyntax(pcid, TransferSyntax.ImplicitVrLittleEndian);


        pcid = assoc.AddPresentationContext(SopClass.VlEndoscopicImageStorage);
        assoc.AddTransferSyntax(pcid, TransferSyntax.ImplicitVrLittleEndian);

        pcid = assoc.AddPresentationContext(SopClass.VlMicroscopicImageStorage);
        assoc.AddTransferSyntax(pcid, TransferSyntax.ImplicitVrLittleEndian);

        pcid = assoc.AddPresentationContext(SopClass.VlPhotographicImageStorage);
        assoc.AddTransferSyntax(pcid, TransferSyntax.ImplicitVrLittleEndian);

        pcid = assoc.AddPresentationContext(SopClass.VlSlideCoordinatesMicroscopicImageStorage);
        assoc.AddTransferSyntax(pcid, TransferSyntax.ImplicitVrLittleEndian);

        pcid = assoc.AddPresentationContext(SopClass.XRayAngiographicBiPlaneImageStorageRetired);
        assoc.AddTransferSyntax(pcid, TransferSyntax.ImplicitVrLittleEndian);

        pcid = assoc.AddPresentationContext(SopClass.XRayAngiographicImageStorage);
        assoc.AddTransferSyntax(pcid, TransferSyntax.ImplicitVrLittleEndian);

        pcid = assoc.AddPresentationContext(SopClass.XRayRadiofluoroscopicImageStorage);
        assoc.AddTransferSyntax(pcid, TransferSyntax.ImplicitVrLittleEndian);

        pcid = assoc.AddPresentationContext(SopClass.XRayRadiationDoseSrStorage);
        assoc.AddTransferSyntax(pcid, TransferSyntax.ImplicitVrLittleEndian);

        pcid = assoc.AddPresentationContext(SopClass.ChestCadSrStorage);
        assoc.AddTransferSyntax(pcid, TransferSyntax.ImplicitVrLittleEndian);

        pcid = assoc.AddPresentationContext(SopClass.XRay3dAngiographicImageStorage);
        assoc.AddTransferSyntax(pcid, TransferSyntax.ImplicitVrLittleEndian);

        pcid = assoc.AddPresentationContext(SopClass.XRay3dCraniofacialImageStorage);
        assoc.AddTransferSyntax(pcid, TransferSyntax.ImplicitVrLittleEndian);

        pcid = assoc.AddPresentationContext(SopClass.EncapsulatedCdaStorage);
        assoc.AddTransferSyntax(pcid, TransferSyntax.ImplicitVrLittleEndian);

        pcid = assoc.AddPresentationContext(SopClass.OphthalmicTomographyImageStorage);
        assoc.AddTransferSyntax(pcid, TransferSyntax.ImplicitVrLittleEndian);


        pcid = assoc.AddPresentationContext(SopClass.BreastTomosynthesisImageStorage);
        assoc.AddTransferSyntax(pcid, TransferSyntax.ImplicitVrLittleEndian);

    }
}