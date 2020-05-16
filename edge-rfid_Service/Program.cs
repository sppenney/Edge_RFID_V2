using System;
using NATS.Client;
using AmbientEdge.Protocol.Ext.Rfid;
using System.Timers;
using Google.Protobuf;
using System.Collections.Generic;
using System.Linq;
using AmbientEdge.Protocol.Core;
using AmbientEdge.Logging;
using Serilog;
using System.Threading.Tasks;
using System.Threading;

using static AmbientEdge.Protocol.Core.EdgeResponse.Types;
using Iot.Device.Pn532;
using STAN.Client;
using Iot.Device.Pn532.ListPassive;

// -----------------------------------------------------------------
// URL Layout for the service
// -----------------------------------------------------------------
// RFID event observation resource:  _PUBLIC.rfid-service.events
// -----------------------------------------------------------------
namespace AmbientEdge
{
    class Program
    {
        static void Main(string[] args)
        {
            // Configure logging
            var logConfig = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .WriteTo.File("logs\\myapp.txt", rollingInterval: RollingInterval.Day);
            Log.Logger = new Serilogger(logConfig);
            Log.Information("Starting service");

            // Start program
            Program p = new Program();
            p.Init();
        }

        // Private data
        EdgeConnection edge = null;
        private ObservedResource observer;
        private Task RFIDTask;
        private CancellationTokenSource CancelToken; // The token for cancellation

        // Constants
        const string RFIDDataType = "ambientedge.protocol.ext.rfid:1.0.0";
        readonly PayloadInfo rfidPayloadInfo = EdgeUtils.CreateProto3PayloadInfo(RFIDDataType);

        // RFID Variables
        private static Pn532 rfidHat;
        private string reader = "/dev/ttyS0";

        // Init method
        internal void Init()
        {
            // Create our edge connection
            StanOptions opts = StanOptions.GetDefaultOptions();
            opts.NatsURL = "192.168.0.8:4222";
            edge = new EdgeConnection("test-cluster", "rfid", opts);
            // Open the connection
            edge.Open();
            // Register an observed route manager to automatically handle remote observers with constraints
            var resourceMgr = edge.RegisterObservedResourceManager("events", new RequestConstraints(RFIDDataType, "proto3"));
            // Setup a handler for receiving new resource observer events
            resourceMgr.OnResourceObserverCreated += ObserverCreated;
            // Setup a handler for receiving observer removed events
            resourceMgr.OnResourceObserverRemoved += ObserverRemoved;

        }

        // Event handler for receiving new resource observer events 
        private void ObserverCreated(object sender, ObservedResource obs)
        {
            // Store the incoming observed resource
            observer = obs;
            Log.Information("ObserverCreated {@observer}", obs);
            CancelToken = new CancellationTokenSource();

            // Start the RFID reader hardware using an asynchronous Task 
            Log.Information("Starting the RFID reader...");
            RFIDTask = new Task(() =>
            {
                RFIDHardwareManager(CancelToken.Token);
            });
            RFIDTask.Start();
            Log.Information("Started RFID Reader");
        }

        // NOTE: This section is not tested! I'm including it as a possible approach.
        private async Task RFIDHardwareManager(CancellationToken cancelToken)
        {
            while (!cancelToken.IsCancellationRequested)
            {

                    rfidHat = new Pn532(reader);
                    byte[] retData = null;
                    retData = rfidHat.ListPassiveTarget(MaxTarget.One, TargetBaudRate.B106kbpsTypeA);
                    Thread.Sleep(200);

                    do 
                    {
                        var decrypted = rfidHat.TryDecode106kbpsTypeA(retData.AsSpan().Slice(1));
                        var uid = BitConverter.ToString(decrypted.NfcId);
                        rfidHat.ReleaseTarget(1);
                        close();
                        Console.WriteLine("Unique User ID:  " + uid);
                        SendRFIDEvent(uid);
                        break; 

                    }

                    while(retData != null);
                    // Give time to PN532 to process
                    rfidHat.Dispose();
                    Thread.Sleep(1000);                    

            }
            
        }

        public static void close()
        {
            if (rfidHat != null)
            {
                Thread.Sleep(500);
                rfidHat.Dispose();
            }
        }

        // Event handler for receiving observer removed events
        private void ObserverRemoved(object sender, ObservedResource obs)
        {
            observer = null;
            // Here, we cancel the token, which should stop the RFIDHardwareManager
            CancelToken?.Cancel();
            Log.Information("ObserverRemoved {@observer}", obs);
        }

        private void SendRFIDEvent(string incomingUID)
        {
            EdgeResponse resp = EdgeUtils.NewSuccessResponse();
            rfidevent e = new rfidevent
            {
                Uid = incomingUID
            };

            resp.Payload = e.ToByteString();
            observer.SendEvent(resp);
        }
    }
}