using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using SpotifyAPI.Web;

namespace AssiSharpPlayer
{

    public class SongInfo
    {
        public FullTrack track;
        public ulong user;
        private float popularity;
        private Dictionary<ulong, float> user_ratings = new();
        private List<string> genres;
        private List<ulong> users;
        
        public SongInfo(FullTrack track, ulong user, List<ulong> users)
        {
            this.track = track;
            this.users = users;
            this.user = user;

            popularity = track.Popularity / 100f;
            genres = track.Artists[0].GetFull().GetAwaiter().GetResult().Genres;
            
            CalculateUserRatings();
        }

        public void CalculateUserRatings()
        {
            //TODO: calculate if in liked songs, if in liked albums, if in liked artists, if in top songs, if in top artists, if in fav genres.
            
            foreach (var u in users)
                user_ratings.Add(u, 1f);
        }

        public float CalculateSongRating(Dictionary<string, float> genre_weights, Dictionary<ulong, float> user_weights)
        {
            var genre_overlapping = genres.Intersect(genre_weights.Keys);
            float genre_value = genre_overlapping.Sum(g => genre_weights[g]);


            Dictionary<ulong, float> new_user_ratings = new(user_ratings);
            foreach (var u in new_user_ratings.Keys)
                new_user_ratings[u] *= user_weights[u];
            
            return popularity * genre_value * new_user_ratings.Values.Average();
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
                if (request.Items.Count < 50) return result;
            }

            var newrequest = await getter(remainder());

            newrequest.Items!.CopyTo(result, limit - limit % 50);

            return result;
        }

        public static async Task<IEnumerable<T>> MakeRequest<T>(
            Func<PersonalizationTopRequest, Task<Paging<T>>> getter,
            int limit, PersonalizationTopRequest.TimeRange timeRange) where T : IPlayableItem
        {
            return await GenerateRequest(getter, limit,
                i => new PersonalizationTopRequest
                    {Limit = 50, Offset = i * 50, TimeRangeParam = timeRange},
                () =>
                {
                    return new PersonalizationTopRequest
                        {Limit = limit % 50, Offset = limit - limit % 50 - 1, TimeRangeParam = timeRange};
                }
            );
        }

        public static async Task<IEnumerable<T>> MakeRequest<T>(
            Func<LibraryTracksRequest, Task<Paging<T>>> getter,
            int limit)
        {
            return await GenerateRequest(getter, limit,
                i =>
                {
                    var a = getter(new LibraryTracksRequest {Limit = 50, Offset = i * 50});
                    return new LibraryTracksRequest {Limit = 50, Offset = i * 50};
                },
                 () => new LibraryTracksRequest {Limit = limit % 50, Offset = limit - limit % 50}
            );
        }
    }
    
    public class RadioPlayer
    {
        private List<SongInfo> songs = new();

        private Dictionary<string, float> genreWeights = new();
        private Dictionary<ulong, float> userWeights = new();

        public RadioPlayer(List<ulong> users)
        {
            var tracks = GetTracksOfUsers(users).GetAwaiter().GetResult();
            foreach (var u in tracks.Keys)
            {
                foreach (var t in tracks[u])
                {
                    foreach (var g in t.Artists[0].GetFull().GetAwaiter().GetResult().Genres)
                    {
                        if (genreWeights.Keys.Contains(g)) genreWeights[g] += 1;
                        else genreWeights[g] = 1;
                    }
                    songs.Add(new SongInfo(t, u, users));
                }

            }

            foreach (var u in users)
                userWeights[u] = 1;
        }

        private static T WeightedRandom<T>(Dictionary<T, float> dict)
        {
            float length = dict.Values.Sum();
            var random = new Random();
            float p = random.Next((int)MathF.Floor(length * 1000)) / 1000f;
            float v = 0;
            foreach (var item in dict.Keys)
            {
                if (v + dict[item] < p) v += dict[item];
                else return item;
            }
            throw new Exception("p overexceeded dict weights");
        }
        
        public async Task<FullTrack> BigDic()
        {
            Dictionary<SongInfo, float> song_weights = new();

            foreach (var s in songs)
                song_weights[s] = s.CalculateSongRating(genreWeights, userWeights);

            SongInfo track = WeightedRandom(song_weights);

            await BalanceWeights(track);

            return track.track;
        }

        private const float reduceGenreWeight = 0.25f;
        private const float reduceUserWeight = 0.25f;

        private const int top_songs_limit = 50;
        private const int liked_songs_limit = 350;
        
        private async Task BalanceWeights(SongInfo track)
        {
            List<string> track_genres = (await track.track.Artists[0].GetFull()).Genres;
            foreach (var g in genreWeights.Keys)
            {
                if (track_genres.Contains(g)) 
                    genreWeights[g] -= reduceGenreWeight / track_genres.Count;
                else genreWeights[g] += reduceGenreWeight / genreWeights.Count;
            }
            
            foreach (var u in userWeights.Keys)
            {
                if (u == track.user) userWeights[u] -= reduceUserWeight;
                else userWeights[u] += reduceUserWeight / genreWeights.Count;
            }
        }
        
        private static async Task<Dictionary<ulong, IEnumerable<FullTrack>>> GetTracksOfUsers(IEnumerable<ulong> users)
        {
            Dictionary<ulong, IEnumerable<FullTrack>> result = new();
            foreach (var u in users)
            {
                var client = await SpotifyManager.GetClient(u);
                List<FullTrack> songs = new();

                songs = songs.Extend(await Requestor.MakeRequest(client.Personalization.GetTopTracks, top_songs_limit,
                    PersonalizationTopRequest.TimeRange.ShortTerm));

                List<string> albums = new();

                for (int i = 0; i < songs.Count; i++)
                {
                    var s = songs[i];
                    try
                    {
                        var album = await s.Album.GetFull();
                        if (albums.Contains(album.Name)) continue;

                        songs = songs.Extend(
                            album.Tracks.Items!.Select(
                                t => t.GetFull().GetAwaiter().GetResult()
                            )
                        );
                        albums.Add(album.Name);
                    }
                    catch (APITooManyRequestsException)
                    {
                        Console.WriteLine("fuck off spotify");
                        i--;
                        await Task.Delay(1000);
                    }

                }
                
                var savedTracks = await Requestor.MakeRequest(client.Library.GetTracks, liked_songs_limit);                
                songs = songs.Extend(savedTracks.Select(s => s.Track));

                result[u] = songs;
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

        public static void Main()
        {
            Console.WriteLine("started");
            RadioPlayer r = new(new List<ulong>() {417610386871812098});
            var songs = r.BigDic().GetAwaiter().GetResult();
            Console.WriteLine("ended");
        }
        
    }
}
