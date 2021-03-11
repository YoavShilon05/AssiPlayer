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

    public class SongInfo
    {
        public FullTrack track;
        public Listener user;
        private float popularity;
        private Dictionary<Listener, float> userRatings = new();
        public List<string> genres;
        private List<Listener> users;
        
        public SongInfo(FullTrack track, Listener user, List<Listener> users)
        {
            this.track = track;
            this.users = users;
            this.user = user;

            popularity = track.Popularity / 100f;

            genres = Listener.tracks[track].Genres;

            
            CalculateUserRatings().GetAwaiter().GetResult();
        }

        private const float LikedArtistWeight = 40;
        private const float LikedAlbumWeight = 50;
        private const float TopSongWeight = 120;
        private const float LikedSongWeight = 70;
        private const float GenreWeight = 20;

        private Task CalculateUserRatings()
        {
            //calculate if in liked songs, if in liked albums, if in liked artists, if in top songs, if in top artists, if in fav genres.

            foreach (var u in users)
            {
                var genreOverlapping = genres.Intersect(u.genreWeights.Keys);
                var u1 = u;
                float genreValue = genreOverlapping.Sum(g => u1.genreWeights[g]) * GenreWeight;

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
            }

            return Task.CompletedTask;
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
        
        public async Task<IEnumerable<SongInfo>> GetNewSongInfos(List<Listener> listeners)
        {

            const int requestSize = 20;
            
            HashSet<FullTrack> result = new();

            List<FullTrack[]> requests = new List<FullTrack[]>();
            int i = 0;
            
            foreach (var s in likedTracks)
            {
                if (tracks.ContainsKey(s)) continue;

                if (i % requestSize == 0)
                {
                    requests.Add(new FullTrack[requestSize]);
                    requests[^1][0] = s;
                }
                else
                {
                    requests[^1][i % requestSize] = s;
                }
                
                tracks.Add(s, null);
                result.Add(s);
                i++;
            }

            requests[^1] = requests[^1].Clean().ToArray();
            
            List<FullTrack> requestRemainders = new();

            foreach (var track in topTracks)
            {
                if (tracks.Keys.Select(t => t.Href).Contains(track.Href)) continue;
                
                tracks.Add(track, null);
                requestRemainders.Add(track);
            }
            
            if (requestRemainders.Count > 0)
            {
                // ReSharper disable once PossibleLossOfFraction
                for (int j = 0; j < MathF.Floor(requestRemainders.Count / requestSize); j++)
                {
                    requests.Add(new FullTrack[requestSize]);
                    requestRemainders.GetRange(j * requestSize, requestSize).CopyTo(requests[^1]);
                }
                requests.Add(new FullTrack[requestRemainders.Count % requestSize]);
                requestRemainders.GetRange(requestRemainders.Count - requestRemainders.Count % requestSize,
                    requestRemainders.Count % requestSize).CopyTo(requests[^1]);
            }

            await SetTrackArtists(requests);
            
            return result.Select(t => new SongInfo(t, this, listeners));
        }

        private static async Task SetTrackArtists(IEnumerable<FullTrack[]> requests)
        {
            foreach (var r in requests)
            {
                var ids = r.Select(request => request.Artists[0].Id).ToList();
                var results = await SpotifyManager.GlobalClient.Artists.GetSeveral(new(ids));

                for (int i = 0; i < r.Length; i++)
                {
                    var artist = results.Artists[i];
                    tracks[r[i]] = artist;
                }
            }
        }

        public static Dictionary<FullTrack, FullArtist> tracks = new();
        
        public HashSet<FullArtist> topTrackArtists = new();
        public HashSet<FullAlbum> favoriteAlbums = new();
        public HashSet<FullArtist> favoriteArtists = new();
        public List<FullTrack> topTracks;
        public HashSet<FullTrack> likedTracks = new();
        public Dictionary<string, float> genreWeights = new();
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
    
    public class RadioPlayer
    {
        private List<SongInfo> songs;

        private Dictionary<string, float> genreWeights = new();
        private Dictionary<Listener, float> userWeights = new();
        private List<Listener> listeners = new();

        public RadioPlayer(IEnumerable<ulong> users)
        {
            foreach (var u in users) listeners.Add(new Listener(u));
            
            songs = new List<SongInfo>(GetTracksOfUsers(listeners).GetAwaiter().GetResult());
            
            
            foreach (var l in listeners)
                userWeights[l] = 1;
        }

        private static T WeightedRandom<T>(Dictionary<T, float> dict)
        {
            float length = dict.Values.Sum();
            var random = new Random();
            float p = random.Next(Convert.ToInt32(MathF.Floor(length * 1000) / 1000f));
            float v = 0;
            foreach (var item in dict.Keys)
            {
                if (v + dict[item] < p) v += dict[item];
                else return item;
            }
            throw new Exception("p overexceeded dict weights");
        }
        
        public FullTrack BigDic()
        {
            Dictionary<SongInfo, float> songWeights = new();

            foreach (var s in songs)
                songWeights[s] = s.CalculateSongRating(genreWeights, userWeights);

            SongInfo track = WeightedRandom(songWeights);

            BalanceWeights(track);
            songs.Remove(track);
            
            return track.track;
        }

        private const float ReduceGenreWeight = 0.25f;
        private const float ReduceUserWeight = 0.25f;

        private void BalanceWeights(SongInfo track)
        {
            List<string> trackGenres = track.genres;

            foreach (var g in trackGenres)
                genreWeights[g] -= ReduceGenreWeight / trackGenres.Count;

            foreach (var g in genreWeights.Keys)
                genreWeights[g] += ReduceGenreWeight / genreWeights.Count;
            


            userWeights[track.user] -= ReduceUserWeight;
            foreach (var u in userWeights.Keys)
            {
                userWeights[u] += ReduceUserWeight / listeners.Count;
            }
        }
        
        private async Task<IEnumerable<SongInfo>> GetTracksOfUsers(IEnumerable<Listener> users)
        {
            List<SongInfo> result = new();
            var enumerable = users.ToList();
            foreach (var u in enumerable)
            {
                
                var tracks = await u.GetNewSongInfos(enumerable);
                
                foreach (var s in tracks)
                {
                    result.Add(s);
                    foreach (var g in s.genres)
                    {
                        if (genreWeights.ContainsKey(g)) genreWeights[g] += 1;
                        else genreWeights[g] = 1;
                    }
                }
                
            }

            return result;
        }
        
        public static async Task<FullTrack> RandomFavorite(IEnumerable<DiscordMember> membersListening,
                                                           List<TrackRecord> history, Queue<TrackRecord> queue)
        {
            IEnumerable<SpotifyClient> clients =
                membersListening.Select(m => SpotifyManager.GetClient(m.Id).GetAwaiter().GetResult());

            List<FullTrack> tracks = new();
            foreach (var client in clients)
            {
                if (client != null)
                {
                    tracks.AddRange(
                        (await client.Personalization.GetTopTracks(new() {Limit = 50})).Items ??
                        throw new ArgumentNullException(nameof(membersListening)));
                }
            }

            FullTrack result = null;
            while (result == null || history.Select(r => r.Track).Contains(result) ||
                   queue.Select(r => r.Track).Contains(result))
                result = tracks.RandomChoice();

            return result;
        }

        public static void NotMain()
        {
            Console.WriteLine("started");
            RadioPlayer r = new(new List<ulong>() {329960504376426496, 417610386871812098});
            Console.WriteLine("created radio player");
            for (int i = 0; i < 100; i++)
            {
                r.BigDic();
            }
            Console.WriteLine("ended");
        }
        
    }
}
