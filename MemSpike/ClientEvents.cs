using System;
using System.Collections.Generic;

namespace MemSpike
{
    /// <summary>One of the client's timed events, as the server sent it (times in UTC; null = open).</summary>
    internal sealed class ClientEvent
    {
        public string Name;
        public DateTime? Start, End;
    }

    /// <summary>
    /// The client's timed events (EventTimingManager.m_eventTimings): the server's own calendar, which is
    /// where Battlegrounds seasons live. "battlegrounds_reward_track_season_N" is the season players count
    /// (Season 14 = 2026-08-04), "battlegrounds_ranked_season_N" the rating period, numbered on its own.
    /// The manager is a service, held only by ServiceManager.s_runtimeServices in Blizzard.T5.ServiceLocator.
    /// </summary>
    internal static class ClientEvents
    {
        /// <summary>One of the client's services (EventTimingManager, GameMgr, NetCache, ...) by class name; 0 when absent.</summary>
        public static long Service(Mono main, string className)
        {
            long services = main.Obj(main.StaticObj(main.ClassIn("Blizzard.T5.ServiceLocator", "Blizzard.T5.Services.ServiceManager"), "s_runtimeServices"), "m_services");
            foreach (long info in Values(main, services))
            {
                long svc = main.Obj(info, "<Service>k__BackingField");
                if (svc != 0 && main.ClassName(main.ClassOf(svc)) == className) return svc;
            }
            return 0;
        }

        /// <summary>Every event whose name starts with <paramref name="prefix"/>; null when the client can't be read.</summary>
        public static List<ClientEvent> Read(Mono main, string prefix)
        {
            long manager = Service(main, "EventTimingManager");
            if (manager == 0) return null;

            var list = new List<ClientEvent>();
            foreach (long t in Values(main, main.Obj(manager, "m_eventTimings")))
            {
                string name = main.Str(t, "<Name>k__BackingField");
                if (name == null || !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(new ClientEvent
                {
                    Name = name,
                    Start = Time(main, main.FieldAddr(t, "<StartTimeUtc>k__BackingField")),
                    End = Time(main, main.FieldAddr(t, "<EndTimeUtc>k__BackingField")),
                });
            }
            return list;
        }

        // System Dictionary<K, V> with a reference V and a K of up to 8 bytes: Entry { int hashCode, next; K key; V value },
        // 24 bytes; a live entry has hashCode >= 0.
        private static IEnumerable<long> Values(Mono m, long dict)
        {
            long entries = m.Obj(dict, "_entries");
            int n = Math.Min(Math.Min(m.ArrayLen(entries), m.Int(dict, "_count")), 65536);
            for (int i = 0; i < n; i++)
            {
                long e = entries + 0x20 + i * 24L;
                if (m.M.I32(e) < 0) continue;
                long v = m.M.Ptr(e + 16);
                if (v != 0) yield return v;
            }
        }

        // Nullable<DateTime>, 16 bytes inline: a flag word and the DateTime's ticks (top two bits = its kind).
        // Which word comes first is the runtime's business, so the one that reads as a date is taken.
        private static DateTime? Time(Mono m, long addr)
        {
            if (addr == 0) return null;
            long a = m.M.Ptr(addr), b = m.M.Ptr(addr + 8);
            foreach (var pair in new[] { new[] { a, b }, new[] { b, a } })
            {
                long flag = pair[0], ticks = pair[1] & 0x3FFFFFFFFFFFFFFF;
                if ((flag & 0xFF) == 1 && ticks > 630000000000000000 && ticks < 670000000000000000)
                    return new DateTime(ticks, DateTimeKind.Utc);
            }
            return null;
        }
    }
}
