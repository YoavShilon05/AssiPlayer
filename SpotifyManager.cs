using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SpotifyAPI.Web;
using Newtonsoft.Json;
using Swan;

namespace AssiSharpPlayer
{
    public class SpotifyManager
    {
        private string verifier;
        private string challenge;

        private const string ClientID = "aaee5c103e124265959ac70e0ae74b20";
        private const string ClientSecret = "e24b4b9bb1ce417ebc0d8694ecb07a43";

        public static readonly SpotifyClient GlobalClient;

        static SpotifyManager()
        {
            var config = SpotifyClientConfig.CreateDefault();

            var request = new ClientCredentialsRequest(ClientID, ClientSecret);
            var response = new OAuthClient(config).RequestToken(request).GetAwaiter().GetResult();

            GlobalClient = new SpotifyClient(config.WithToken(response.AccessToken));
        }

        public SpotifyManager()
        {
            (verifier, challenge) = PKCEUtil.GenerateCodes();
        }

        public string GetConnectionURL(string url)
        {
            return GetConnectionURL(url, Scopes.UserTopRead, Scopes.UserFollowRead,
                Scopes.UserLibraryRead, Scopes.UserReadCurrentlyPlaying, Scopes.UserReadPrivate,
                Scopes.UserReadRecentlyPlayed);
        }

        public string GetConnectionURL(string url, params string[] scopes)
        {
            var uri = new Uri(url);

            var loginRequest = new LoginRequest(
                uri,
                ClientID,
                LoginRequest.ResponseType.Code
            )
            {
                CodeChallengeMethod = "S256",
                CodeChallenge = challenge,
                Scope = scopes
            };

            return loginRequest.ToUri().AbsoluteUri;
        }

        private static async Task<SpotifyClient> ClientFromTokenResponse(
            PKCETokenResponse tokenResponse, ulong discordID, bool save = false)
        {
            var newResponse = await new OAuthClient().RequestToken(
                new PKCETokenRefreshRequest(ClientID, tokenResponse.RefreshToken)
            );

            // var auth = new PKCEAuthenticator(ClientID, tokenResponse);
            // var config = SpotifyClientConfig.CreateDefault().WithAuthenticator(auth);

            if (save) SaveTokenResponse(tokenResponse, discordID);

            return new SpotifyClient(newResponse.AccessToken);
        }

        public async Task<SpotifyClient> CreateClient(string code, string url, ulong discordID)
        {
            if (DeserializeCreds().ContainsKey(discordID))
                return null;

            var uri = new Uri(url);

            var initialResponse = await new OAuthClient().RequestToken(
                new PKCETokenRequest(ClientID, code.Replace($"{url}?code=", ""), uri, verifier)
            );


            return await ClientFromTokenResponse(initialResponse, discordID, true);
        }

        public static void SaveTokenResponse(PKCETokenResponse tokenResponse, ulong discordID)
        {
            var oldJson = DeserializeCreds();
            oldJson.Add(discordID, tokenResponse);
            File.WriteAllText("Connections.json", oldJson.ToJson());
        }

        public static Dictionary<ulong, PKCETokenResponse> DeserializeCreds() =>
            JsonConvert.DeserializeObject<Dictionary<ulong, PKCETokenResponse>>(File.ReadAllText("Connections.json"));

        public static async Task<FullTrack> SearchSong(string search)
        {
            var r = await GlobalClient.Search.Item(new(SearchRequest.Types.Track, search));
            return r.Tracks.Items![0];
        }

        public static async Task<FullAlbum> SearchAlbum(string search)
        {
            var r = await GlobalClient.Search.Item(new(SearchRequest.Types.Album, search));
            return await r.Albums.Items![0].GetFull();
        }

        public static async Task<SpotifyClient> GetClient(ulong discordID)
        {
            var creds = DeserializeCreds();
            if (creds.ContainsKey(discordID))
                return await ClientFromTokenResponse(creds[discordID], discordID);
            return null;
        }
    }
}
