using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using DSharpPlus.Exceptions;
using SpotifyAPI.Web;

namespace AssiSharpPlayer
{
    /*public class SongInfo
    {
        public FullTrack track;
        public Listener user;
        private float popularity;
        private Dictionary<Listener, float> userRatings = new();
        public List<string> genres;
        private List<Listener> users;
        
        public SongInfo(FullTrack track, FullArtist artist, Listener user, List<Listener> users)
        {
            this.track = track;
            this.users = users;
            this.user = user;

            popularity = track.Popularity / 100f;

            genres = artist.Genres;

            
            CalculateUserRatings().GetAwaiter().GetResult();
        }

        private const float LikedArtistWeight = 40;
        private const float LikedAlbumWeight = 50;
        private const float TopSongWeight = 120;
        private const float LikedSongWeight = 70;
        private const float GenreWeight = 20;

        public Task AddUserRating(Listener u)
        {
            var genreOverlapping = genres.Intersect(u.genreWeights.Keys);
            float genreValue = genreOverlapping.Sum(g => u.genreWeights[g]) * GenreWeight;

            bool userSaved = u.likedTracks.Contains(track);
            float userSavedValue = Convert.ToInt32(userSaved) * LikedSongWeight;

            bool userSavedArtist = u.favoriteArtists.Select(a => a.Href).Contains(track.Artists[0].Href);
            float userSavedArtistValue = Convert.ToInt32(userSavedArtist) * LikedArtistWeight;

            bool userSavedAlbum = u.favoriteAlbums.Select(a => a.Href).Contains(track.Album.Href);
            float userSavedAlbumValue = Convert.ToInt32(userSavedAlbum) * LikedAlbumWeight;

            bool userTopTrack = u.topTracks.Select(t => t.Href).Contains(track.Href);
            float userTopTrackValue = Convert.ToInt32(userTopTrack);
            if (userTopTrack) userTopTrackValue = (Listener.TopSongsLimit - u.topTracks.IndexOf(track)) * TopSongWeight;

            float rating = genreValue + userSavedAlbumValue + userSavedArtistValue + userSavedValue + userTopTrackValue;
            userRatings.Add(u, rating);

            return Task.CompletedTask;
        }
        
        private async Task CalculateUserRatings()
        {
            //calculate if in liked songs, if in liked albums, if in liked artists, if in top songs, if in top artists, if in fav genres.

            foreach (var u in users)
            {
                await AddUserRating(u);
            }
        }

        public float CalculateSongRating(Dictionary<string, float> genreWeights, Dictionary<Listener, float> userWeights)
        {
            var genreOverlapping = genres.Intersect(genreWeights.Keys);
            float genreValue = genreOverlapping.Sum(g => genreWeights[g]);


            Dictionary<Listener, float> newUserRatings = new(userRatings);
            foreach (var u in newUserRatings.Keys)
                newUserRatings[u] *= userWeights[u];
            
            return popularity * genreValue * newUserRatings.Values.Average();
        }
    }
    
    public class Ref<T>
    {
        public T Value
        {
            get => getter();
            set => setter(value);
        }
  

        Func<T> getter;
        Action<T> setter;
  
        public Ref(Func<T> getter, Action<T> setter)
        {
            this.getter = getter;
            this.setter = setter;
        }
    }

    public class Listener
    {
        public SpotifyClient client;
        
        public const int TopSongsLimit = 70;
        private const int TopArtistsLimit = 30;
        private const int LikedSongsLimit = 1000;
        private const int SavedAlbumsLimit = 50;
        private const int LikeAlbumBySongsInTopThresh = 3;
        private const int LikeArtistBySongsInTopThresh = 3;

        public Listener(ulong id)
        {
            client = SpotifyManager.GetClient(id).GetAwaiter().GetResult();
            SetTopTracks().GetAwaiter().GetResult();
            SetAlbums().GetAwaiter().GetResult();
            SetArtists().GetAwaiter().GetResult();
            SetLikedSongs().GetAwaiter().GetResult();
            SetTopGenres();
        }

        private async Task SetTopTracks()
        {
            topTracks = new List<FullTrack>(await Requestor.MakeRequest(client.Personalization.GetTopTracks, TopSongsLimit,
                PersonalizationTopRequest.TimeRange.ShortTerm));
            
        }
        
        private async Task SetAlbums()
        {
            favoriteAlbums = favoriteAlbums.Extend
            (
                (await Requestor.MakeRequest(client.Library.GetAlbums, SavedAlbumsLimit)).Select(a => a.Album)
            ).ToHashSet();

            HashSet<string> albumNames = favoriteArtists.Select(a => a.Href).ToHashSet();
            Dictionary<string, int> albumDict = new();
            foreach (var track in topTracks)
            {
                var album = track.Album.Href;
                if (albumDict.ContainsKey(album)) albumDict[album] += 1;
                else albumDict[album] = 1;

                if (albumDict[album] == LikeAlbumBySongsInTopThresh && !albumNames.Contains(album)) favoriteAlbums.Add(await track.Album.GetFull());
            }
        }

        private async Task SetArtists()
        {
            favoriteArtists = favoriteArtists.Extend
            (
                await Requestor.MakeRequest(client.Personalization.GetTopArtists, TopArtistsLimit, PersonalizationTopRequest.TimeRange.ShortTerm)
            ).ToHashSet();


            HashSet<string> artistNames = favoriteArtists.Select(a => a.Href).ToHashSet();
            Dictionary<string, int> artistDict = new();
            foreach (var track in topTracks)
            {
                var artist = await track.Artists[0].GetFull();
                topTrackArtists.Add(artist);
                
                if (artistDict.ContainsKey(artist.Href)) artistDict[artist.Href] += 1;
                else artistDict[artist.Href] = 1;
                if (artistDict[artist.Href] == LikeArtistBySongsInTopThresh && !artistNames.Contains(artist.Href)) favoriteArtists.Add(artist);
            }
        }

        private async Task SetLikedSongs()
        {
            likedTracks = likedTracks.Extend
            (
                (await Requestor.MakeRequest(client.Library.GetTracks, LikedSongsLimit)).Select(t => t.Track)
            ).ToHashSet();
        }

        private void SetTopGenres()
        {
            foreach (var genres in topTrackArtists.Select(a => a.Genres))
            {
                foreach (var g in genres)
                {
                    if (genreWeights.ContainsKey(g)) genreWeights[g] += 1;
                    else genreWeights[g] = 1;
                }
            }
        }
        
        public async Task<IEnumerable<SongInfo>> GetSongInfos(List<Listener> listeners)
        {
            
            HashSet<SongInfo> result = new();
            result.Append<SongInfo>(topTracks.Select(t => new SongInfo(t, this, listeners)));
            result.Append<SongInfo>(likedTracks.Select(t => new SongInfo(t, this, listeners)));
            return result;
        }
        
        public HashSet<FullArtist> topTrackArtists = new();
        public HashSet<FullAlbum> favoriteAlbums = new();
        public HashSet<FullArtist> favoriteArtists = new();
        public List<FullTrack> topTracks;
        public HashSet<FullTrack> likedTracks = new();
        public Dictionary<string, float> genreWeights = new();
    
    }


    class FullRequestSession
    {
        

        private bool open = true;
        private bool join = false;
        private Task requestTask;
        private bool listen_until_joined = true;

        public FullRequestSession(bool listen_until_joined=true)
        {
            this.listen_until_joined = listen_until_joined;
        }
        
        public async Task Start()
        {
            join = false;
            open = true;
            requestTask = RequestFullArtistBatch();
            await requestTask;
        }
        
        private async Task RequestFullArtistBatch(int request_limit = 20)
        {
            while (open)
            {
                while (requests.Count < request_limit) await Task.Delay(1000);
                var r = requests.GetRange(0, request_limit);
                var answers = (await SpotifyManager.GlobalClient.Artists.GetSeveral(
                    new(r.Select(i => i.Item1).ToList()))).Artists;

                for (int i = 0; i < request_limit; i++)
                {
                    r[i].Item2.Value = answers[i];
                }

                if ((requests.Count == 0 && !listen_until_joined) || join)
                    open = false;
                
            }
        }

        private List<Tuple<string, Ref<FullArtist>>> requests;
        
        public Task RequestFullArtist(Ref<FullArtist> reference, string id)
        {
            requests.Add(new Tuple<string, Ref<FullArtist>>(id, reference));
            Task.WaitAll(requestTask);
            return Task.CompletedTask;
        }

        public Task Join()
        {
            join = true;
            Task.WaitAll(requestTask);
            return Task.CompletedTask;
        }
    }
    
    public static class Requestor
    {
        private static async Task<IEnumerable<T>> GenerateRequest<T, T2>(
            Func<T2, Task<Paging<T>>> getter,
            int limit, Func<int, T2> generator, Func<T2> remainder) where T2 : RequestParams
        {
            var result = new T[limit];

            for (int i = 0; i < MathF.Floor(limit / 50f); i++)
            {
                Paging<T> request = await getter(generator(i));
                request.Items!.CopyTo(result, i * 50);
                if (request.Items.Count < 50) return result.Clean();
            }

            if (limit % 50 == 0) return result.Clean();
            
            var newrequest = await getter(remainder());
            newrequest.Items!.CopyTo(result, limit - limit % 50);

            return result.Clean();
        }

        public static async Task<IEnumerable<T>> MakeRequest<T>(
            Func<PersonalizationTopRequest, Task<Paging<T>>> getter,
            int limit, PersonalizationTopRequest.TimeRange timeRange)
        {
            return await GenerateRequest(getter, limit,
                i => new PersonalizationTopRequest
                    {Limit = 50, Offset = i * 50, TimeRangeParam = timeRange},
                () => new PersonalizationTopRequest
                    {Limit = limit % 50, Offset = limit - limit % 50, TimeRangeParam = timeRange});
        }

        public static async Task<IEnumerable<T>> MakeRequest<T>(
            Func<LibraryTracksRequest, Task<Paging<T>>> getter,
            int limit)
        {
            return await GenerateRequest(getter, limit,
                i => new LibraryTracksRequest {Limit = 50, Offset = i * 50},
                 () => new LibraryTracksRequest {Limit = limit % 50, Offset = limit - limit % 50}
            );
        }
        
        public static async Task<IEnumerable<T>> MakeRequest<T>(
            Func<LibraryAlbumsRequest, Task<Paging<T>>> getter,
            int limit)
        {
            return await GenerateRequest(getter, limit,
                i => new LibraryAlbumsRequest {Limit = 50, Offset = i * 50},
                () => new LibraryAlbumsRequest {Limit = limit % 50, Offset = limit - limit % 50}
            );
        }
    }
    
    */


    public class RadioPlayer
    {
        private List<ulong> users;
        public RadioPlayer(List<ulong> users)
        {
            this.users = users;
        }

        private bool TrackInBlacklist(FullTrack track)
        {
            return users.Where(u => Program.blacklists.ContainsKey(u)).Any(u => Program.blacklists[u].Contains(track.Id));
        }
        
        public async Task<FullTrack> RandomFavorite(IEnumerable<FullTrack> history)
        {
            Random r = new();
            var ui = r.Next(users.Count);
            var u = users[ui];

            var client = await SpotifyManager.GetClient(u);

            List<FullTrack> tracks = (await client.Personalization.GetTopTracks
                (new(){Limit = 50, TimeRangeParam = PersonalizationTopRequest.TimeRange.ShortTerm})).Items;

            int tracki = r.Next(tracks!.Count);
            while (history.Contains(tracks[tracki]) || TrackInBlacklist(tracks[tracki])) tracki = r.Next(tracks!.Count); 
            
            return tracks[tracki];
        }
    }

    
}
