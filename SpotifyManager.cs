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

        private const string client_id = "aaee5c103e124265959ac70e0ae74b20";
        private const string client_secret = "e24b4b9bb1ce417ebc0d8694ecb07a43";

        public static SpotifyClient GlobalClient;
        static SpotifyManager()
        {
            var config = SpotifyClientConfig.CreateDefault();

            var request = new ClientCredentialsRequest(client_id, client_secret);
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
                Scopes.UserLibraryRead, Scopes.UserReadCurrentlyPlaying, Scopes.UserReadPrivate, Scopes.UserReadRecentlyPlayed);
        }
        
        public string GetConnectionURL(string url, params string[] scopes)
        {
            var uri = new Uri(url);

            var loginRequest = new LoginRequest(
                uri,
                client_id,
                LoginRequest.ResponseType.Code
            )
            {
                CodeChallengeMethod = "S256",
                CodeChallenge = challenge,
                Scope = scopes
            };

            return loginRequest.ToUri().AbsoluteUri;
        }

        private static async Task<SpotifyClient> ClientFromTokenResponse(PKCETokenResponse token_response, ulong discord_id, bool save=false)
        {
            var newResponse = await new OAuthClient().RequestToken(
                    new PKCETokenRefreshRequest(client_id, token_response.RefreshToken)
                );


            var auth = new PKCEAuthenticator(client_id, token_response);
            var config = SpotifyClientConfig.CreateDefault().WithAuthenticator(auth);
            
            if (save) SaveTokenResponse(token_response, discord_id);
            
            return new SpotifyClient(config);
        }
        
        public async Task<SpotifyClient> CreateClient(string code, string url, ulong discord_id)
        {
            if (DeserializeCreds().ContainsKey(discord_id))
                return null;
                
            var uri = new Uri(url);
            
            var initialResponse = await new OAuthClient().RequestToken(
                new PKCETokenRequest(client_id, code.Replace($"{url}?code=", ""), uri, verifier)
            );

            
            
            return await ClientFromTokenResponse(initialResponse, discord_id, true);
        }

        public static void SaveTokenResponse(PKCETokenResponse token_response, ulong discord_id)
        {
            var old_json = DeserializeCreds();
            old_json.Add(discord_id, token_response);
            File.WriteAllText("Connections.json", old_json.ToJson());
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
        
        public static async Task<SpotifyClient> GetClient(ulong discord_id)
        {
            var creds = DeserializeCreds();
            if (creds.ContainsKey(discord_id))
                return await ClientFromTokenResponse(creds[discord_id], discord_id);
            return null;
        }
    }
}