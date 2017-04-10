﻿using Noemax.Compression;
using SteamKit2;
using SteamKit2.GC;
using SteamKit2.GC.Dota.Internal;
using SteamKit2.GC.Internal;
using SteamKit2.Internal;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HGV.Crystalys
{
    public class DotaGameClient : IDisposable
    {
        #region Properties

        const int APPID = 570;

        private SteamClient SteamClient { get; set; }
        private WebClient WebClient { get; set; }

        private string Username { get; set; }
        private string Password { get; set; }
        private byte[] Sentry { get; set; }

        private bool AutoReconnect { get; set; }

        #endregion

        #region Constructor

        public DotaGameClient(bool auto_reconnect = false)
        {
            this.AutoReconnect = auto_reconnect;

            this.WebClient = new WebClient();
            this.SteamClient = new SteamClient();
        }

        #endregion

        #region Connect

        public Task<uint> Connect(string user, string password)
        {
            this.Username = user;
            this.Password = password;

            var guardian = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            Func<uint> HandshakeWithSteam = () =>
            {
                bool completed = false;
                uint version = 0;

                // get the GC handler, which is used for messaging DOTA
                var gcHandler = this.SteamClient.GetHandler<SteamGameCoordinator>();

                // register a few callbacks we're interested in
                var cbManager = new CallbackManager(this.SteamClient);

                // these are registered upon creation to a callback manager, 
                // which will then route the callbacks to the functions specified
                cbManager.Subscribe<SteamClient.ConnectedCallback>((SteamClient.ConnectedCallback callback) =>
                {
                    if (callback.Result == EResult.OK)
                    {
                        Trace.TraceInformation("Steam: Logging in '{0}'", this.Username);

                        // get the steamuser handler, which is used for logging on after successfully connecting
                        var UserHandler = this.SteamClient.GetHandler<SteamUser>();
                        UserHandler.LogOn(new SteamUser.LogOnDetails
                        {
                            Username = this.Username,
                            Password = this.Password,
                        });
                    }
                    else
                    {
                        var error = Enum.GetName(typeof(EResult), callback.Result);
                        throw new Exception(string.Format("Failed to Connect. Unknown Error: {0}", error));
                    }
                });

                cbManager.Subscribe<SteamClient.DisconnectedCallback>((SteamClient.DisconnectedCallback callback) =>
                {
                    if (this.AutoReconnect)
                    {
                        Trace.TraceInformation("Steam: Disconnected.");

                        // delay a little to give steam some time to finalize the DC
                        Thread.Sleep(TimeSpan.FromSeconds(10));

                        // reconect
                        Trace.TraceInformation("Steam: Reconnecting.");
                        this.SteamClient.Connect();
                    }
                });

                cbManager.Subscribe<SteamUser.LoggedOnCallback>((SteamUser.LoggedOnCallback callback) =>
                {

                    if (callback.Result == EResult.OK)
                    {
                        Trace.TraceInformation("Steam: LoggedOn");

                        // we've logged into the account
                        // now we need to inform the steam server that we're playing dota (in order to receive GC messages)
                        // steamkit doesn't expose the "play game" message through any handler, so we'll just send the message manually
                        var gameMsg = new ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayed);
                        gameMsg.Body.games_played.Add(new CMsgClientGamesPlayed.GamePlayed { game_id = new GameID(APPID) });

                        // send it off - notice here we're sending this message directly using the SteamClient
                        this.SteamClient.Send(gameMsg);

                        // delay a little to give steam some time to establish a GC connection to us
                        Thread.Sleep(TimeSpan.FromSeconds(1));

                        // inform the dota GC that we want a session
                        var helloMsg = new ClientGCMsgProtobuf<CMsgClientHello>((uint)EGCBaseClientMsg.k_EMsgGCClientHello);
                        helloMsg.Body.engine = ESourceEngine.k_ESE_Source2;
                        gcHandler.Send(helloMsg, APPID);
                    }
                    else if (callback.Result == EResult.AccountLogonDenied)
                    {
                        throw new Exception(string.Format("Steam Guard code required. Use STEAM GUARD to generate sentry."));
                    }
                    else if (callback.Result == EResult.AccountLoginDeniedNeedTwoFactor)
                    {
                        throw new Exception(string.Format("Two factor code required. Use STEAM GUARD to generate sentry."));
                    }
                    else
                    {
                        var error = Enum.GetName(typeof(EResult), callback.Result);
                        throw new Exception(string.Format("Failed to Login. Unknown Error: {0}", error));
                    }
                });

                cbManager.Subscribe<SteamGameCoordinator.MessageCallback>((SteamGameCoordinator.MessageCallback callback) =>
                {
                    if (callback.EMsg == (uint)EGCBaseClientMsg.k_EMsgGCClientWelcome)
                    {
                        var msg = new ClientGCMsgProtobuf<CMsgClientWelcome>(callback.Message);

                        version = msg.Body.version;

                        Trace.TraceInformation("Dota: GC Welcome");

                        completed = true;
                    }
                });

                // initiate the connection
                SteamClient.Connect();

                while (completed == false)
                {
                    // in order for the callbacks to get routed, they need to be handled by the manager
                    cbManager.RunWaitCallbacks(TimeSpan.FromSeconds(1));
                }

                return version;
            };

            return Task.Run<uint>(HandshakeWithSteam, guardian.Token);
        }

        #endregion

        #region Disconnect

        public void Dispose()
        {
            if(this.WebClient != null)
            {
                this.WebClient.Dispose();
                this.WebClient = null;
            }

            if (this.SteamClient != null)
            {
                this.SteamClient.Disconnect();
                this.SteamClient = null;
            }
        }

        #endregion

        #region DOTA Functions

        public async Task<CMsgDOTAMatch> DownloadMatchData(long matchId)
        {
            var guardian = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            Func<CMsgDOTAMatch> RequestMatchDetails = () =>
            {
                CMsgDOTAMatch matchDetails = null;

                // get the GC handler, which is used for messaging DOTA
                var gcHandler = this.SteamClient.GetHandler<SteamGameCoordinator>();

                // register a few callbacks we're interested in
                var cbManager = new CallbackManager(this.SteamClient);

                var sub = cbManager.Subscribe<SteamGameCoordinator.MessageCallback>((SteamGameCoordinator.MessageCallback callback) =>
                {
                    if (callback.EMsg == (uint)EDOTAGCMsg.k_EMsgGCMatchDetailsResponse)
                    {
                        Trace.TraceInformation("Dota: Match Data");

                        var msg = new ClientGCMsgProtobuf<CMsgGCMatchDetailsResponse>(callback.Message);
                        matchDetails = msg.Body.match;
                    }
                });

                // Send Request
                var request = new ClientGCMsgProtobuf<CMsgGCMatchDetailsRequest>((uint)EDOTAGCMsg.k_EMsgGCMatchDetailsRequest);
                request.Body.match_id = (ulong)matchId;
                gcHandler.Send(request, APPID);

                while (matchDetails == null)
                {
                    // in order for the callbacks to get routed, they need to be handled by the manager
                    cbManager.RunWaitCallbacks(TimeSpan.FromSeconds(1));
                }

                return matchDetails;
            };

            return await Task.Run<CMsgDOTAMatch>(RequestMatchDetails, guardian.Token);
        }

        public async Task<byte[]> DownloadReplay(long matchId)
        {
            var matchDetails = await DownloadMatchData(matchId);
            var data = await DownloadData(matchDetails, "dem");
            return data;
        }

        public async Task<CDOTAMatchMetadata> DownloadMeta(long matchId)
        {
            var matchDetails = await DownloadMatchData(matchId);
            var data = await DownloadData(matchDetails, "meta");

            using (var steam = new MemoryStream(data))
            {
                var meta = ProtoBuf.Serializer.Deserialize<CDOTAMatchMetadataFile>(steam);
                return meta.metadata;
            }
        }

        private async Task<byte[]> DownloadData(CMsgDOTAMatch matchDetails, string type)
        {
            var url = string.Format("http://replay{0}.valve.net/{1}/{2}_{3}.{4}.bz2", matchDetails.cluster, APPID, matchDetails.match_id, matchDetails.replay_salt, type);
            
            var compressedMatchData = await this.WebClient.DownloadDataTaskAsync(url);
            var uncompressedMatchData = CompressionFactory.BZip2.Decompress(compressedMatchData);

            return uncompressedMatchData;
        }

        #endregion
    }
}
