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
        private Dictionary<Listener, float> user_ratings = new();
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

        private const float liked_artist_weight = 40;
        private const float liked_album_weight = 50;
        private const float top_song_weight = 120;
        private const float liked_song_weight = 70;
        private const float genre_weight = 20;

        private async Task CalculateUserRatings()
        {
            //calculate if in liked songs, if in liked albums, if in liked artists, if in top songs, if in top artists, if in fav genres.

            for (int i = 0; i < users.Count; i++)
            {
                var u = users[i];

                //try
                //{
                    // best song ~ 10,000
                    var genre_overlapping = genres.Intersect(u.genreWeights.Keys);
                    float genre_value = genre_overlapping.Sum(g => u.genreWeights[g]) * genre_weight;

                    bool user_saved = u.likedTracks.Contains(track);
                    float user_saved_value = Convert.ToInt32(user_saved) * liked_song_weight;

                    bool user_saved_artist = u.favoriteArtists.Select(a => a.Href).Contains(track.Artists[0].Href);
                    float user_saved_artist_value = Convert.ToInt32(user_saved_artist) * liked_artist_weight;

                    bool user_saved_album = u.favoriteAlbums.Select(a => a.Href).Contains(track.Album.Href);
                    float user_saved_album_value = Convert.ToInt32(user_saved_album) * liked_album_weight;

                    bool user_top_track = u.topTracks.Select(t => t.Href).Contains(track.Href);
                    float user_top_track_value = Convert.ToInt32(user_top_track);
                    if (user_top_track) user_top_track_value = (Listener.top_songs_limit - u.topTracks.IndexOf(track)) * top_song_weight;

                    float rating = genre_value + user_saved_album_value + user_saved_artist_value + user_saved_value + user_top_track_value;
                    user_ratings.Add(u, rating);
                //}
                
                //catch (Exception)
                //{
                //    await Task.Delay(2500);
                //    i--;
                //}
            }
        }

        public float CalculateSongRating(Dictionary<string, float> genre_weights, Dictionary<Listener, float> user_weights)
        {
            var genre_overlapping = genres.Intersect(genre_weights.Keys);
            float genre_value = genre_overlapping.Sum(g => genre_weights[g]);


            Dictionary<Listener, float> new_user_ratings = new(user_ratings);
            foreach (var u in new_user_ratings.Keys)
                new_user_ratings[u] *= user_weights[u];
            
            return popularity * genre_value * new_user_ratings.Values.Average();
        }
    }

    public class Listener
    {
        public SpotifyClient client;
        
        public const int top_songs_limit = 70;
        private const int top_artists_limit = 30;
        private const int liked_songs_limit = 1000;
        private const int saved_albums_limit = 50;
        private const int like_album_by_songs_in_top_thresh = 3;
        private const int like_artist_by_songs_in_top_thresh = 3;

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
            topTracks = new List<FullTrack>(await Requestor.MakeRequest(client.Personalization.GetTopTracks, top_songs_limit,
                PersonalizationTopRequest.TimeRange.ShortTerm));
            
        }
        
        private async Task SetAlbums()
        {
            favoriteAlbums = favoriteAlbums.Extend
            (
                (await Requestor.MakeRequest(client.Library.GetAlbums, saved_albums_limit)).Select(a => a.Album)
            ).ToHashSet();

            HashSet<string> albumNames = favoriteArtists.Select(a => a.Href).ToHashSet();
            Dictionary<string, int> albumDict = new();
            foreach (var track in topTracks)
            {
                var album = track.Album.Href;
                if (albumDict.ContainsKey(album)) albumDict[album] += 1;
                else albumDict[album] = 1;

                if (albumDict[album] == like_album_by_songs_in_top_thresh && !albumNames.Contains(album)) favoriteAlbums.Add(await track.Album.GetFull());
            }
        }

        private async Task SetArtists()
        {
            favoriteArtists = favoriteArtists.Extend
            (
                await Requestor.MakeRequest(client.Personalization.GetTopArtists, top_artists_limit, PersonalizationTopRequest.TimeRange.ShortTerm)
            ).ToHashSet();


            HashSet<string> artistNames = favoriteArtists.Select(a => a.Href).ToHashSet();
            Dictionary<string, int> artistDict = new();
            foreach (var track in topTracks)
            {
                var artist = await track.Artists[0].GetFull();
                topTrackArtists.Add(artist);
                
                if (artistDict.ContainsKey(artist.Href)) artistDict[artist.Href] += 1;
                else artistDict[artist.Href] = 1;
                if (artistDict[artist.Href] == like_artist_by_songs_in_top_thresh && !artistNames.Contains(artist.Href)) favoriteArtists.Add(artist);
            }
        }

        private async Task SetLikedSongs()
        {
            likedTracks = likedTracks.Extend
            (
                (await Requestor.MakeRequest(client.Library.GetTracks, liked_songs_limit)).Select(t => t.Track)
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

        public RadioPlayer(List<ulong> users)
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
            Dictionary<SongInfo, float> song_weights = new();

            foreach (var s in songs)
                song_weights[s] = s.CalculateSongRating(genreWeights, userWeights);

            SongInfo track = WeightedRandom(song_weights);

            BalanceWeights(track);
            songs.Remove(track);
            
            return track.track;
        }

        private const float reduceGenreWeight = 0.25f;
        private const float reduceUserWeight = 0.25f;

        private void BalanceWeights(SongInfo track)
        {
            List<string> track_genres = track.genres;

            foreach (var g in track_genres)
                genreWeights[g] -= reduceGenreWeight / track_genres.Count;

            foreach (var g in genreWeights.Keys)
                genreWeights[g] += reduceGenreWeight / genreWeights.Count;
            


            userWeights[track.user] -= reduceUserWeight;
            foreach (var u in userWeights.Keys)
            {
                userWeights[u] += reduceUserWeight / listeners.Count;
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
            //TODO: don't throw if there are no connections
            IEnumerable<SpotifyClient> clients =
                membersListening.Select(m => SpotifyManager.GetClient(m.Id).GetAwaiter().GetResult());

            List<FullTrack> tracks = new();
            foreach (var client in clients)
                if (client != null)
                    tracks.AddRange(
                        (await client.Personalization.GetTopTracks(new() {Limit = 50})).Items ??
                        throw new ArgumentNullException(nameof(membersListening)));

            FullTrack result = null;
            while (result == null || history.Select(r => r.Track).Contains(result) ||
                   queue.Select(r => r.Track).Contains(result))
                result = tracks.RandomChoice();

            return result;
        }

        public static void NOTMain()
        {
            Console.WriteLine("started");
            RadioPlayer r = new(new List<ulong>() {329960504376426496, 417610386871812098});
            Console.WriteLine("created radio player");
            for (int i = 0; i < 100; i++)
            {
                var song = r.BigDic();
            }
            Console.WriteLine("ended");
        }
        
    }
}
