using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using HsbgCardLookup.Game;
using Newtonsoft.Json.Linq;

namespace Toolkit.Standalone
{
    /// <summary>
    /// Dev harness: plays recorded games (state streams from tools\replay-states.py) into the real match
    /// recorder through the real game-state seam, at the pace the live reader publishes at.
    ///   --feed-states &lt;states folder&gt; &lt;output folder&gt; [seasons.json]
    /// </summary>
    internal static class StateFeed
    {
        private const int PollMs = 250;

        public static int Run(string[] args)
        {
            int i = Array.IndexOf(args, "--feed-states");
            if (args.Length < i + 3) { Console.Error.WriteLine("--feed-states <states folder> <output folder> [seasons.json]"); return 2; }
            string input = args[i + 1], output = args[i + 2];
            var calendar = args.Length > i + 3 ? SeasonCalendar.Load(args[i + 3]) : null;
            var log = new List<string>();
            int games = 0, addr = 0;
            foreach (string file in Directory.GetFiles(input, "*.states.jsonl.gz").OrderBy(f => f))
            {
                var game = new StandaloneGameState();
                var memory = new StandaloneMemory(game);
                DateTime clock = DateTime.MinValue;
                var recorder = new MatchRecorder(game, memory, output, log.Add) { UtcNow = () => clock };
                if (calendar != null) recorder.SeasonOf = calendar.Of;

                var entities = new Dictionary<int, EntitySnap>();
                int gameEntity = 0, local = 0, opponent = 0, friendly = 0, lastPublish = -PollMs, gameType = 0;
                DateTime start = DateTime.MinValue;
                int? rating = null, change = null;
                addr++;
                Action<int> publish = ms =>
                {
                    clock = start.AddMilliseconds(ms);
                    var s = new Snapshot { GameStateAddr = addr, GameEntityId = gameEntity, LocalPlayerEntityId = local, OpponentPlayerEntityId = opponent, Entities = entities, GameType = gameType };
                    for (int t = 0; t < 8; t++) s.Tiles.Add(new TileSnap());
                    game.Publish(s);
                    recorder.Poll();
                    lastPublish = ms;
                };
                int lastMs = 0;
                using (var reader = new StreamReader(new GZipStream(File.OpenRead(file), CompressionMode.Decompress)))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        var o = JObject.Parse(line);
                        if (o["id"] != null)
                        {
                            start = DateTime.ParseExact((string)o["startLocal"], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal).ToUniversalTime();
                            friendly = (int)o["friendly"];
                            string build = (string)o["build"];
                            // PegasusShared.GameType, as GameMgr.m_gameType holds it live.
                            string type = (string)o["gameType"];
                            gameType = type == "GT_BATTLEGROUNDS" ? 23 : type == "GT_BATTLEGROUNDS_FRIENDLY" ? 24
                                : type == "GT_BATTLEGROUNDS_DUO" ? 37 : type == "GT_BATTLEGROUNDS_DUO_FRIENDLY" ? 39 : 0;
                            recorder.BuildSource = () => build;
                            continue;
                        }
                        int ms = (int)o["ms"]; lastMs = ms;
                        if (o["rating"] != null) { rating = (int?)o["rating"]; change = (int?)o["change"]; continue; }
                        if (o["game"] != null)
                        {
                            if (entities.Count > 0) addr++;      // a rejoin: the client makes a new game object
                            entities = new Dictionary<int, EntitySnap>();
                            gameEntity = (int)o["game"];
                            foreach (var p in (JObject)o["players"])
                                if (int.Parse(p.Key) == friendly) local = (int)p.Value; else opponent = (int)p.Value;
                            continue;
                        }
                        // The reader would have published before this packet landed.
                        if (ms - lastPublish >= PollMs && entities.Count > 0) publish(ms - 1);
                        foreach (JArray e in (JArray)o["e"])
                        {
                            int id = (int)e[0];
                            EntitySnap snap;
                            if (!entities.TryGetValue(id, out snap)) entities[id] = snap = new EntitySnap { Id = id, Tags = new Dictionary<int, int>() };
                            snap.CardId = (string)e[1];
                            foreach (var t in (JObject)e[2]) snap.Tags[int.Parse(t.Key)] = (int)t.Value;
                        }
                    }
                }
                publish(lastMs + PollMs);
                clock = clock.AddSeconds(1);
                game.Publish(new Snapshot());        // the game object is gone
                recorder.Poll();
                if (rating != null) recorder.RatingChanged(rating.Value, change ?? 0);
                addr++;
                games++;
            }
            File.WriteAllLines(Path.Combine(output, "feed.log"), log);
            return games > 0 ? 0 : 1;
        }
    }
}
