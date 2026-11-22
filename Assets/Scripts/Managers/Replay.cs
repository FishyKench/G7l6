using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>One thing that happened in a recorded round (a shot, a kill, a grenade, a flag changing hands).</summary>
public class ReplayEvent
{
    public const byte Shot = 0, Kill = 1, Grenade = 2, Flag = 3;
    public const byte FragKind = 0, FlashKind = 1;

    public float t;           // seconds since the round started
    public byte kind;
    public short a = -1;      // actor: shooter / killer / thrower / flag index
    public short b = -1;      // target: who was hit / the victim
    public byte extra;        // Shot: 0 world, 1 body, 2 head.  Kill: 1 = headshot.  Grenade: FragKind/FlashKind.  Flag: 0 neutral, 1 your team, 2 enemy
    public Vector3 p0, p1;    // Shot: from / to.  Kill: victim spot.  Grenade: throw spot / blast spot.  Flag: spot
    public float t2 = -1f;    // Grenade: when it blew up
}

/// <summary>
/// A whole recorded round: ~20 samples a second of every fighter (position, facing, alive / crouching / sliding),
/// your camera, and the events. Kept compact in flat arrays so five of them are well under 2 MB.
/// </summary>
public class ReplayRound
{
    public const float Step = 0.05f;      // 20 Hz
    public const byte Alive = 1, Crouch = 2, Slide = 4;

    public int id;
    public string stamp = "";
    public bool ranked;
    public bool won;
    public int scoreYou, scoreThem;
    public float duration;
    public int ticks, fighters;
    public string[] names = new string[0];
    public byte[] teams = new byte[0];
    public Vector3[] flagPos = new Vector3[0];
    public string[] flagLetters = new string[0];
    public float[] pos = new float[0];       // ticks * fighters * 3
    public ushort[] yaw = new ushort[0];     // ticks * fighters
    public byte[] state = new byte[0];       // ticks * fighters
    public float[] camPos = new float[0];    // ticks * 3
    public ushort[] camAng = new ushort[0];  // ticks * 3 (pitch, yaw, roll)
    public byte[] camFov = new byte[0];      // ticks
    public List<ReplayEvent> events = new List<ReplayEvent>();

    public string Title => ranked ? "Ranked round" : "Warm-up round";

    public static ushort PackAngle(float degrees)
    {
        degrees = Mathf.Repeat(degrees, 360f);
        return (ushort)Mathf.Clamp(Mathf.RoundToInt(degrees / 360f * 65535f), 0, 65535);
    }

    public static float UnpackAngle(ushort v) => v / 65535f * 360f;

    int Clamp(int tick) => Mathf.Clamp(tick, 0, Mathf.Max(0, ticks - 1));

    public Vector3 PosAt(int tick, int f)
    {
        int i = (Clamp(tick) * fighters + f) * 3;
        return new Vector3(pos[i], pos[i + 1], pos[i + 2]);
    }

    public byte StateAt(int tick, int f) => state[Clamp(tick) * fighters + f];
    public float YawAt(int tick, int f) => UnpackAngle(yaw[Clamp(tick) * fighters + f]);

    /// <summary>Smoothly interpolated pose of a fighter. Respawns (alive flag changes, big jumps) are not blended.</summary>
    public void Sample(float time, int f, out Vector3 p, out float yawDeg, out byte st)
    {
        float k = Mathf.Clamp(time, 0f, Mathf.Max(0f, (ticks - 1) * Step)) / Step;
        int i0 = Mathf.FloorToInt(k), i1 = Clamp(i0 + 1);
        i0 = Clamp(i0);
        float u = k - Mathf.Floor(k);
        byte s0 = StateAt(i0, f), s1 = StateAt(i1, f);
        Vector3 p0 = PosAt(i0, f), p1 = PosAt(i1, f);
        st = u < 0.5f ? s0 : s1;
        if (((s0 ^ s1) & Alive) != 0 || (p1 - p0).sqrMagnitude > 36f)
        {
            p = u < 0.5f ? p0 : p1;
            yawDeg = YawAt(u < 0.5f ? i0 : i1, f);
            return;
        }
        p = Vector3.Lerp(p0, p1, u);
        yawDeg = Mathf.LerpAngle(YawAt(i0, f), YawAt(i1, f), u);
    }

    public void SampleCamera(float time, out Vector3 p, out Quaternion rot, out float fov)
    {
        float k = Mathf.Clamp(time, 0f, Mathf.Max(0f, (ticks - 1) * Step)) / Step;
        int i0 = Mathf.FloorToInt(k), i1 = Clamp(i0 + 1);
        i0 = Clamp(i0);
        float u = k - Mathf.Floor(k);
        Vector3 a = new Vector3(camPos[i0 * 3], camPos[i0 * 3 + 1], camPos[i0 * 3 + 2]);
        Vector3 b = new Vector3(camPos[i1 * 3], camPos[i1 * 3 + 1], camPos[i1 * 3 + 2]);
        p = (b - a).sqrMagnitude > 36f ? (u < 0.5f ? a : b) : Vector3.Lerp(a, b, u);
        float pitch = Mathf.LerpAngle(UnpackAngle(camAng[i0 * 3]), UnpackAngle(camAng[i1 * 3]), u);
        float yw = Mathf.LerpAngle(UnpackAngle(camAng[i0 * 3 + 1]), UnpackAngle(camAng[i1 * 3 + 1]), u);
        float roll = Mathf.LerpAngle(UnpackAngle(camAng[i0 * 3 + 2]), UnpackAngle(camAng[i1 * 3 + 2]), u);
        rot = Quaternion.Euler(pitch, yw, roll);
        fov = Mathf.Lerp(camFov[i0], camFov[i1], u);
    }

    /// <summary>0 neutral, 1 your team, 2 the enemy: who owned a flag at a moment.</summary>
    public int FlagOwnerAt(int flag, float time)
    {
        int owner = 0;
        foreach (var e in events)
        {
            if (e.t > time) break;
            if (e.kind == ReplayEvent.Flag && e.a == flag) owner = e.extra;
        }
        return owner;
    }
}

/// <summary>
/// The recorded rounds. Static, so they survive the VR scene (and the house) being unloaded, and saved to a
/// compact binary file in the persistent data path so they survive restarts. At most <see cref="MaxRounds"/> kept.
/// </summary>
public static class Replay
{
    public const int MaxRounds = 5;
    const int Version = 1;

    static readonly List<ReplayRound> rounds = new List<ReplayRound>();
    static bool loaded;

    public static event Action Changed;

    /// <summary>Oldest first.</summary>
    public static List<ReplayRound> Rounds { get { Load(); return rounds; } }
    public static int Count => Rounds.Count;

    static string FilePath => Path.Combine(Application.persistentDataPath, "ClipHub", "replays.bin");

    public static void Add(ReplayRound r)
    {
        Load();
        int max = 0;
        foreach (var x in rounds) max = Mathf.Max(max, x.id);
        r.id = max + 1;
        rounds.Add(r);
        while (rounds.Count > MaxRounds)
        {
            // Drop the oldest warm-up first, else the oldest of all.
            int drop = rounds.FindIndex(x => !x.ranked);
            if (drop < 0 || drop == rounds.Count - 1) drop = 0;
            rounds.RemoveAt(drop);
        }
        Save();
        Changed?.Invoke();
    }

    public static void Clear()
    {
        Load();
        rounds.Clear();
        Save();
        Changed?.Invoke();
    }

    public static string NowStamp()
    {
        string time = "";
        var day = DayManager.Instance;
        if (day)
        {
            int h = Mathf.FloorToInt(day.Hour), m = Mathf.FloorToInt((day.Hour - h) * 60f);
            time = $" - {(h > 12 ? h - 12 : h)}:{m:00} {(h >= 12 ? "PM" : "AM")}";
        }
        return Wallet.DayLabel + time;
    }

    // ------------------------------------------------------------------ file

    static void Load()
    {
        if (loaded) return;
        loaded = true;
        rounds.Clear();
        try
        {
            string path = FilePath;
            if (!File.Exists(path)) return;
            using (var r = new BinaryReader(File.OpenRead(path)))
            {
                if (r.ReadInt32() != Version) return;
                int n = r.ReadInt32();
                for (int i = 0; i < n; i++) rounds.Add(ReadRound(r));
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Replay] Couldn't read saved recordings, starting fresh: " + e.Message);
            rounds.Clear();
        }
    }

    static void Save()
    {
        try
        {
            string path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp";
            using (var w = new BinaryWriter(File.Create(tmp)))
            {
                w.Write(Version);
                w.Write(rounds.Count);
                foreach (var r in rounds) WriteRound(w, r);
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Replay] Couldn't save recordings: " + e.Message);
        }
    }

    static void WriteBytes(BinaryWriter w, Array a, int bytes)
    {
        var b = new byte[bytes];
        Buffer.BlockCopy(a, 0, b, 0, bytes);
        w.Write(bytes);
        w.Write(b);
    }

    static T[] ReadArray<T>(BinaryReader r, int size) where T : struct
    {
        int bytes = r.ReadInt32();
        var b = r.ReadBytes(bytes);
        var a = new T[bytes / size];
        Buffer.BlockCopy(b, 0, a, 0, bytes);
        return a;
    }

    static void WriteVec(BinaryWriter w, Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
    static Vector3 ReadVec(BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

    static void WriteRound(BinaryWriter w, ReplayRound r)
    {
        w.Write(r.id); w.Write(r.stamp ?? ""); w.Write(r.ranked); w.Write(r.won);
        w.Write(r.scoreYou); w.Write(r.scoreThem); w.Write(r.duration); w.Write(r.ticks); w.Write(r.fighters);
        w.Write(r.names.Length);
        for (int i = 0; i < r.names.Length; i++) { w.Write(r.names[i] ?? ""); w.Write(r.teams[i]); }
        w.Write(r.flagPos.Length);
        for (int i = 0; i < r.flagPos.Length; i++) { WriteVec(w, r.flagPos[i]); w.Write(r.flagLetters[i] ?? ""); }
        WriteBytes(w, r.pos, r.pos.Length * 4);
        WriteBytes(w, r.yaw, r.yaw.Length * 2);
        WriteBytes(w, r.state, r.state.Length);
        WriteBytes(w, r.camPos, r.camPos.Length * 4);
        WriteBytes(w, r.camAng, r.camAng.Length * 2);
        WriteBytes(w, r.camFov, r.camFov.Length);
        w.Write(r.events.Count);
        foreach (var e in r.events)
        {
            w.Write(e.t); w.Write(e.kind); w.Write(e.a); w.Write(e.b); w.Write(e.extra);
            WriteVec(w, e.p0); WriteVec(w, e.p1); w.Write(e.t2);
        }
    }

    static ReplayRound ReadRound(BinaryReader r)
    {
        var x = new ReplayRound();
        x.id = r.ReadInt32(); x.stamp = r.ReadString(); x.ranked = r.ReadBoolean(); x.won = r.ReadBoolean();
        x.scoreYou = r.ReadInt32(); x.scoreThem = r.ReadInt32(); x.duration = r.ReadSingle(); x.ticks = r.ReadInt32(); x.fighters = r.ReadInt32();
        int n = r.ReadInt32();
        x.names = new string[n]; x.teams = new byte[n];
        for (int i = 0; i < n; i++) { x.names[i] = r.ReadString(); x.teams[i] = r.ReadByte(); }
        n = r.ReadInt32();
        x.flagPos = new Vector3[n]; x.flagLetters = new string[n];
        for (int i = 0; i < n; i++) { x.flagPos[i] = ReadVec(r); x.flagLetters[i] = r.ReadString(); }
        x.pos = ReadArray<float>(r, 4);
        x.yaw = ReadArray<ushort>(r, 2);
        x.state = ReadArray<byte>(r, 1);
        x.camPos = ReadArray<float>(r, 4);
        x.camAng = ReadArray<ushort>(r, 2);
        x.camFov = ReadArray<byte>(r, 1);
        n = r.ReadInt32();
        for (int i = 0; i < n; i++)
        {
            var e = new ReplayEvent();
            e.t = r.ReadSingle(); e.kind = r.ReadByte(); e.a = r.ReadInt16(); e.b = r.ReadInt16(); e.extra = r.ReadByte();
            e.p0 = ReadVec(r); e.p1 = ReadVec(r); e.t2 = r.ReadSingle();
            x.events.Add(e);
        }
        // Sanity: the arrays must match the header or playback would run off the end.
        if (x.ticks < 2 || x.fighters < 1 || x.pos.Length < x.ticks * x.fighters * 3 || x.yaw.Length < x.ticks * x.fighters
            || x.state.Length < x.ticks * x.fighters || x.camPos.Length < x.ticks * 3 || x.camAng.Length < x.ticks * 3 || x.camFov.Length < x.ticks)
            throw new InvalidDataException("recording is damaged");
        return x;
    }
}

/// <summary>A stretch of a round on the timeline: something worth keeping, or dead time.</summary>
public class ReplayMoment
{
    public enum Type { Dead, Action, Death, Capture, Kill, Multi }

    public float start, end;
    public Type type;
    public int kills, heads, captures, deaths;
    public float peak;     // the best instant in the moment (for thumbnails)
    public bool keep;
    public string label = "";
    public float Length => end - start;
    public bool Highlight => type != Type.Dead;

    /// <summary>Colour on the timeline.</summary>
    public static Color ColorOf(Type t)
    {
        switch (t)
        {
            case Type.Multi: return new Color32(255, 200, 60, 255);
            case Type.Kill: return new Color32(60, 220, 160, 255);
            case Type.Capture: return new Color32(80, 160, 255, 255);
            case Type.Death: return new Color32(255, 90, 100, 255);
            case Type.Action: return new Color32(170, 110, 255, 255);
            default: return new Color32(96, 100, 118, 255);
        }
    }

    public static string NameOf(Type t)
    {
        switch (t)
        {
            case Type.Multi: return "Multi-kill";
            case Type.Kill: return "Kill";
            case Type.Capture: return "Capture";
            case Type.Death: return "Death";
            case Type.Action: return "Firefight";
            default: return "Dead time";
        }
    }

    /// <summary>Finds the moments of a round (kills, multi-kills, captures, deaths) and fills the gaps with dead time.</summary>
    public static List<ReplayMoment> Build(ReplayRound r)
    {
        var raw = new List<ReplayMoment>();
        float dur = Mathf.Max(1f, r.duration);
        ReplayEvent lastKill = null;
        foreach (var e in r.events)
        {
            if (e.kind == ReplayEvent.Kill)
            {
                if (e.a == 0)
                {
                    bool head = e.extra == 1;
                    raw.Add(new ReplayMoment { start = e.t - 3f, end = e.t + 1.6f, type = Type.Kill, kills = 1, heads = head ? 1 : 0, peak = e.t - 0.1f });
                }
                else if (e.b == 0)
                    raw.Add(new ReplayMoment { start = e.t - 3.5f, end = e.t + 1.3f, type = Type.Death, deaths = 1, peak = e.t });
                lastKill = e;
            }
            else if (e.kind == ReplayEvent.Flag && e.extra == 1 && r.fighters > 0)
            {
                // Taken by your team: only a highlight if YOU were standing on it.
                Vector3 you = r.PosAt(Mathf.RoundToInt(e.t / ReplayRound.Step), 0);
                Vector3 d = you - e.p0; d.y = 0f;
                if (d.magnitude < 10f && (r.StateAt(Mathf.RoundToInt(e.t / ReplayRound.Step), 0) & ReplayRound.Alive) != 0)
                    raw.Add(new ReplayMoment { start = e.t - 4.5f, end = e.t + 2.2f, type = Type.Capture, captures = 1, peak = e.t,
                        label = "Cap " + (e.a >= 0 && e.a < r.flagLetters.Length ? r.flagLetters[e.a] : "") });
            }
        }
        // Nothing of yours happened (AFK, or just unlucky): show the biggest firefights instead.
        if (raw.Count == 0 && lastKill != null)
        {
            var kills = r.events.FindAll(e => e.kind == ReplayEvent.Kill);
            var clusters = new List<ReplayMoment>();
            foreach (var k in kills)
            {
                var c = clusters.Find(m => k.t >= m.start && k.t <= m.end);
                if (c == null) { c = new ReplayMoment { start = k.t - 2.5f, end = k.t + 1.5f, type = Type.Action, peak = k.t }; clusters.Add(c); }
                c.kills++;
                c.end = Mathf.Max(c.end, k.t + 1.5f);
            }
            clusters.Sort((x, y) => y.kills.CompareTo(x.kills));
            for (int i = 0; i < Mathf.Min(4, clusters.Count); i++) { clusters[i].kills = 0; raw.Add(clusters[i]); }
        }

        raw.Sort((x, y) => x.start.CompareTo(y.start));
        var merged = new List<ReplayMoment>();
        foreach (var m in raw)
        {
            m.start = Mathf.Max(0f, m.start);
            m.end = Mathf.Min(dur, Mathf.Max(m.end, m.start + 2.5f));
            if (m.start >= dur) continue;
            var last = merged.Count > 0 ? merged[merged.Count - 1] : null;
            if (last != null && m.start <= last.end + 0.4f)
            {
                last.end = Mathf.Max(last.end, m.end);
                last.kills += m.kills; last.heads += m.heads; last.captures += m.captures; last.deaths += m.deaths;
                if (m.type > last.type) { last.type = m.type; last.peak = m.peak; }
                else if (m.type == last.type && m.kills > 0) last.peak = m.peak;
                if (string.IsNullOrEmpty(last.label)) last.label = m.label;
            }
            else merged.Add(m);
        }
        foreach (var m in merged)
        {
            if (m.kills >= 2) m.type = Type.Multi;
            if (m.type == Type.Multi) { m.label = m.kills == 2 ? "Double" : m.kills == 3 ? "Triple" : m.kills == 4 ? "Quad" : "x" + m.kills; }
            else if (m.type == Type.Kill) m.label = m.heads > 0 ? "HS" : "Kill";
            else if (m.type == Type.Death) m.label = "Death";
            else if (m.type == Type.Action) m.label = "Fight";
            else if (m.type == Type.Capture && string.IsNullOrEmpty(m.label)) m.label = "Cap";
            m.keep = true;
        }

        var all = new List<ReplayMoment>();
        float cursor = 0f;
        foreach (var m in merged)
        {
            if (m.start - cursor > 0.3f) all.Add(new ReplayMoment { start = cursor, end = m.start, type = Type.Dead, label = "Dead", keep = false });
            else m.start = cursor;
            all.Add(m);
            cursor = m.end;
        }
        if (dur - cursor > 0.3f) all.Add(new ReplayMoment { start = cursor, end = dur, type = Type.Dead, label = "Dead", keep = false });
        return all;
    }

    /// <summary>Your numbers for the stats panel.</summary>
    public static void Stats(ReplayRound r, out int kills, out int deaths, out int heads, out int captures, out int bestStreak)
    {
        kills = deaths = heads = captures = bestStreak = 0;
        float lastKill = -99f;
        int streak = 0;
        foreach (var e in r.events)
        {
            if (e.kind == ReplayEvent.Kill)
            {
                if (e.a == 0)
                {
                    kills++;
                    if (e.extra == 1) heads++;
                    streak = e.t - lastKill < 5f ? streak + 1 : 1;
                    lastKill = e.t;
                    bestStreak = Mathf.Max(bestStreak, streak);
                }
                else if (e.b == 0) deaths++;
            }
            else if (e.kind == ReplayEvent.Flag && e.extra == 1)
            {
                Vector3 you = r.PosAt(Mathf.RoundToInt(e.t / ReplayRound.Step), 0);
                Vector3 d = you - e.p0; d.y = 0f;
                if (d.magnitude < 10f) captures++;
            }
        }
    }
}
