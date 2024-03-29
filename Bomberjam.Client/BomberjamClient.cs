using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Bomberjam.Client.Colyseus;
using Bomberjam.Client.GameSchema;

namespace Bomberjam.Client
{
    internal class BomberjamClient : IDisposable
    {
        private const string ApplicationName = "bomberjam";

        private readonly TaskCompletionSource<bool> _gameEndedTcs;
        private readonly BomberjamOptions _options;
        private readonly Colyseus.Client _client;
        private readonly IBot _bot;
        private Room<GameStateSchema> _room;
        private string _sessionId;

        public BomberjamClient(BomberjamOptions options, int botIndex)
        {
            this._options = options;
            this._bot = options.Bots[botIndex];
            this._gameEndedTcs = new TaskCompletionSource<bool>();
            this._client = new Colyseus.Client(options.WsServerUri);

            this._client.OnOpen += (sender, e) =>
            {
                this.Print($"Client {this._client.Id} connexion opened");
                Task.Run(this.JoinRoomAsync);
            };

            this._client.OnError += (sender, e) =>
            {
                this.Print($"Client {this._client.Id} error: {e.Exception}");
                Task.Run(this.CloseAsync);
            };
        }

        public string RoomId
        {
            get => this._room?.Id;
        }

        public async Task ConnectAsync()
        {
            await this._client.ConnectAsync();
            await this._gameEndedTcs.Task;
        }

        private async Task JoinRoomAsync()
        {
            var optionsDict = new Dictionary<string, object>
            {
                { "spectate", false },
                { "name", this._options.PlayerName },
                { "roomId", this._options.RoomId },
                { "training", this._options.Mode == GameMode.Training },
                { "createNewRoom", this._options.Mode == GameMode.Training && !this._options.IsSilent },
            };

            this._room?.LeaveAsync();
            this._room = await this._client.Join<GameStateSchema>(ApplicationName, optionsDict);
            this.AddRoomEventHandlers();
        }

        private void AddRoomEventHandlers()
        {
            this._room.OnReadyToConnect += (sender, e) => { this._room.ConnectAsync(); };

            this._room.OnJoin += (sender, e) =>
            {
                this.Print($"Room {this._room.Id} joined");
                this._sessionId = this._room.SessionId;

                if (!this._options.IsSilent)
                {
                    Task.Run(OpenGameInBrowser);
                }
            };

            this._room.OnStateChange += this.OnStateChangedRunBot;

            this._room.OnError += (sender, e) =>
            {
                this.Print(e.Exception.ToString());
                Task.Run(this.CloseAsync);
            };
        }

        private async void OnStateChangedRunBot(object sender, StateChangeEventArgs<GameStateSchema> e)
        {
            if (IsGameFinished(e.State))
            {
                await this.CloseAsync();
                this._gameEndedTcs.TrySetResult(true);
            }
            else
            {
                await this.RunBotOneTickAtATime(e.State);
            }
        }

        private int _lockStatus;

        private async Task RunBotOneTickAtATime(GameStateSchema stateSchema)
        {
            if (IsGameWaitingForPlayers(stateSchema) || IsGameFinished(stateSchema) || stateSchema.isSimulationPaused)
                return;
            
            var lockWasTaken = false;
            
            try
            {
                lockWasTaken = Interlocked.CompareExchange(ref this._lockStatus, 1, 0) == 0;
                
                if (lockWasTaken)
                {
                    await RunBot(stateSchema);
                }
                else
                {
                    this.Print($"Skipping tick {stateSchema.tick} because another thread is still processing a previous tick");
                }
            }
            finally
            {
                if (lockWasTaken)
                {
                    Thread.VolatileWrite(ref this._lockStatus, 0);
                }
            }
        }

        private async Task RunBot(GameStateSchema stateSchema)
        {
            try
            {
                var state = GameState.CreateFromSchema(stateSchema);
                var botAction = await Task.Run(() => this._bot.GetAction(state, this._sessionId));
                var botActionStr = GameActionToString(botAction);

                await SendActionToRoom(stateSchema, botActionStr);
            }
            catch (Exception ex)
            {
                this.Print($"Bot logic error occured: {Environment.NewLine}{ex}");
            }
        }

        private static bool IsGameFinished(GameStateSchema stateSchema)
        {
            return stateSchema.state == 1;
        }

        private static bool IsGameWaitingForPlayers(GameStateSchema state)
        {
            return state.state == -1;
        }

        private static string GameActionToString(GameAction action)
        {
            return Enum.GetName(action.GetType(), action).ToLowerInvariant();
        }

        private Task SendActionToRoom(GameStateSchema state, string action)
        {
            var res = new Dictionary<string, object>
            {
                { "action", action },
                { "tick", state.tick }
            };

            try
            {
                return this._room.SendAsync(res);
            }
            catch (Exception ex)
            {
                this.Print($"An error occured while sending {action} to server: {Environment.NewLine}{ex}");
            }

            return Task.CompletedTask;
        }

        private void Print(string text)
        {
            if (!this._options.IsSilent)
            {
                Console.WriteLine(text);
            }
        }

        private void OpenGameInBrowser()
        {
            var scheme = this._options.WsServerUri.Scheme == "wss" ? "https" : "http";
            var host = this._options.WsServerUri.Host;
            var port = this._options.WsServerUri.Port;
            var viewerUrlStr = $"{scheme}://{host}:{port}/games/{this._room.Id}";

            OpenInBrowser(viewerUrlStr);
        }

        private static void OpenInBrowser(string url)
        {
            try
            {
                Process.Start(url);
            }
            catch
            {
                // hack because of this: https://github.com/dotnet/corefx/issues/10361
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    url = url.Replace("&", "^&");
                    Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Process.Start("xdg-open", url);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start("open", url);
                }
                else
                {
                    throw;
                }
            }
        }

        public async Task CloseAsync()
        {
            if (this._client != null)
            {
                await this._client.CloseAsync();
            }

            if (this._room != null)
            {
                await this._room.CloseAsync();
            }

            this._gameEndedTcs.TrySetResult(true);
        }

        public void Dispose()
        {
            this._client?.Dispose();
            this._room?.Dispose();
        }
    }
}