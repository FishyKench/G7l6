using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Records every Fighting / warm-up round of the VR arena (about 20 times a second): where every fighter is, which
/// way it faces, alive / crouching / sliding, plus your camera, and the events (shots, hits, kills, grenades, flags).
/// When the round ends the recording is handed to <see cref="Replay"/> (static + saved to disk). It lives on the
/// VRArena object. Recording costs a few hundred bytes a tick and never touches the match.
/// </summary>
public class ReplayRecorder : MonoBehaviour
{
    static ReplayRecorder current;
    VRArena arena;
    bool rec;
    int serial = -1;
    float t0, acc;
    readonly List<VRFighter> fighters = new List<VRFighter>();
    readonly Dictionary<VRFighter, int> index = new Dictionary<VRFighter, int>();
    readonly List<float> pos = new List<float>();
    readonly List<ushort> yaw = new List<ushort>();
    readonly List<byte> state = new List<byte>();
    readonly List<float> camPos = new List<float>();
    readonly List<ushort> camAng = new List<ushort>();
    readonly List<byte> camFov = new List<byte>();
    readonly List<ReplayEvent> events = new List<ReplayEvent>();
    readonly List<ReplayEvent> liveGrenades = new List<ReplayEvent>();
    VRFlag[] flags = new VRFlag[0];
    int[] flagOwner = new int[0];
    int ticks;
    bool ranked;
    float scoreYou, scoreThem;
    byte[] lastState;
    float[] lastPos;
    ushort[] lastYaw;

    public static bool Active => current && current.rec;
    float Now => Time.time - t0;

    void Awake()
    {
        arena = GetComponent<VRArena>();
        current = this;
    }

    void OnDestroy()
    {
        if (rec) Finish();
        if (current == this) current = null;
    }

    void Update()
    {
        if (PauseMenu.IsPaused || !arena) return;
        bool live = arena.State == VRArena.Mode.Fighting || arena.State == VRArena.Mode.Practice;
        if (rec && (!live || arena.RoundSerial != serial)) Finish();
        if (!rec && live && arena.Ready && arena.Player && VRFighter.All.Count >= 2) Begin();
        if (!rec) return;

        scoreYou = arena.Score[0];
        scoreThem = arena.Score[1];
        acc += Time.deltaTime;
        int guard = 0;
        while (acc >= ReplayRound.Step && guard++ < 4)
        {
            acc -= ReplayRound.Step;
            Sample();
        }
        if (acc > ReplayRound.Step) acc = 0f;
        TrackFlags();
    }

    void Begin()
    {
        rec = true;
        serial = arena.RoundSerial;
        ranked = arena.State == VRArena.Mode.Fighting;
        t0 = Time.time;
        acc = 0f;
        ticks = 0;
        pos.Clear(); yaw.Clear(); state.Clear(); camPos.Clear(); camAng.Clear(); camFov.Clear(); events.Clear(); liveGrenades.Clear();
        fighters.Clear();
        index.Clear();
        fighters.Add(arena.Player);
        var others = new List<VRFighter>();
        foreach (var f in VRFighter.All) if (f && f != arena.Player) others.Add(f);
        others.Sort((a, b) => a.team != b.team ? a.team.CompareTo(b.team) : string.CompareOrdinal(a.fighterName, b.fighterName));
        fighters.AddRange(others);
        for (int i = 0; i < fighters.Count; i++) index[fighters[i]] = i;
        lastState = new byte[fighters.Count];
        lastPos = new float[fighters.Count * 3];
        lastYaw = new ushort[fighters.Count];
        var fl = new List<VRFlag>();
        foreach (var f in arena.Flags) fl.Add(f);
        flags = fl.ToArray();
        flagOwner = new int[flags.Length];
        scoreYou = scoreThem = 0f;
        Sample();
    }

    void Sample()
    {
        for (int i = 0; i < fighters.Count; i++)
        {
            var f = fighters[i];
            if (f)
            {
                Vector3 p = f.transform.position;
                lastPos[i * 3] = p.x; lastPos[i * 3 + 1] = p.y; lastPos[i * 3 + 2] = p.z;
                lastYaw[i] = ReplayRound.PackAngle(f.transform.eulerAngles.y);
                byte s = 0;
                if (f.Alive) s |= ReplayRound.Alive;
                if (f is VRBot b) { if (b.IsCrouched) s |= ReplayRound.Crouch; if (b.IsSliding) s |= ReplayRound.Slide; }
                else if (f is VRPlayer pl) { if (pl.Crouched) s |= ReplayRound.Crouch; if (pl.Sliding) s |= ReplayRound.Slide; }
                lastState[i] = s;
            }
            else lastState[i] = 0;
            pos.Add(lastPos[i * 3]); pos.Add(lastPos[i * 3 + 1]); pos.Add(lastPos[i * 3 + 2]);
            yaw.Add(lastYaw[i]);
            state.Add(lastState[i]);
        }
        var cam = arena.Player ? arena.Player.Cam : null;
        if (cam)
        {
            Vector3 p = cam.transform.position;
            Vector3 e = cam.transform.eulerAngles;
            camPos.Add(p.x); camPos.Add(p.y); camPos.Add(p.z);
            camAng.Add(ReplayRound.PackAngle(e.x)); camAng.Add(ReplayRound.PackAngle(e.y)); camAng.Add(ReplayRound.PackAngle(e.z));
            camFov.Add((byte)Mathf.Clamp(Mathf.RoundToInt(cam.fieldOfView), 20, 120));
        }
        else
        {
            int n = camPos.Count;
            camPos.Add(n >= 3 ? camPos[n - 3] : 0f); camPos.Add(n >= 3 ? camPos[n - 2] : 0f); camPos.Add(n >= 3 ? camPos[n - 1] : 0f);
            int m = camAng.Count;
            for (int k = 0; k < 3; k++) camAng.Add(m >= 3 ? camAng[m - 3 + k] : (ushort)0);
            camFov.Add(80);
        }
        ticks++;
    }

    void TrackFlags()
    {
        for (int i = 0; i < flags.Length; i++)
        {
            if (!flags[i]) continue;
            int owner = flags[i].Owner + 1; // 0 neutral, 1 team 0, 2 team 1
            if (owner == flagOwner[i]) continue;
            flagOwner[i] = owner;
            events.Add(new ReplayEvent { t = Now, kind = ReplayEvent.Flag, a = (short)i, extra = (byte)owner, p0 = flags[i].Position });
        }
    }

    void Finish()
    {
        rec = false;
        if (ticks >= 100) // under ~5 s isn't worth keeping
        {
            int f = fighters.Count;
            var r = new ReplayRound
            {
                stamp = Replay.NowStamp(),
                ranked = ranked,
                ticks = ticks,
                fighters = f,
                duration = (ticks - 1) * ReplayRound.Step,
                scoreYou = Mathf.FloorToInt(scoreYou),
                scoreThem = Mathf.FloorToInt(scoreThem),
                won = scoreYou > scoreThem,
                names = new string[f],
                teams = new byte[f],
                flagPos = new Vector3[flags.Length],
                flagLetters = new string[flags.Length],
                pos = pos.ToArray(), yaw = yaw.ToArray(), state = state.ToArray(),
                camPos = camPos.ToArray(), camAng = camAng.ToArray(), camFov = camFov.ToArray(),
                events = new List<ReplayEvent>(events),
            };
            for (int i = 0; i < f; i++)
            {
                r.names[i] = fighters[i] ? fighters[i].fighterName : "?";
                r.teams[i] = (byte)(fighters[i] ? Mathf.Clamp(fighters[i].team, 0, 1) : 0);
            }
            for (int i = 0; i < flags.Length; i++)
            {
                r.flagPos[i] = flags[i] ? flags[i].Position : Vector3.zero;
                r.flagLetters[i] = flags[i] ? flags[i].letter : "?";
            }
            r.events.Sort((x, y) => x.t.CompareTo(y.t));
            Replay.Add(r);
        }
        pos.Clear(); yaw.Clear(); state.Clear(); camPos.Clear(); camAng.Clear(); camFov.Clear(); events.Clear(); liveGrenades.Clear();
        fighters.Clear();
        index.Clear();
    }

    // ------------------------------------------------------------------ events (called by the arena)

    int Idx(VRFighter f) => f && index.TryGetValue(f, out int i) ? i : -1;

    void AddShot(VRFighter shooter, Vector3 from, Vector3 to, VRFighter hit, bool head)
    {
        events.Add(new ReplayEvent { t = Now, kind = ReplayEvent.Shot, a = (short)Idx(shooter), b = (short)Idx(hit), extra = (byte)(hit ? (head ? 2 : 1) : 0), p0 = from, p1 = to });
    }

    void AddKill(VRFighter killer, VRFighter victim, bool head)
    {
        events.Add(new ReplayEvent { t = Now, kind = ReplayEvent.Kill, a = (short)Idx(killer), b = (short)Idx(victim), extra = (byte)(head ? 1 : 0),
            p0 = victim ? victim.AimPoint : Vector3.zero });
    }

    void AddThrow(VRFighter thrower, Vector3 from, VRGrenade.Kind kind)
    {
        var e = new ReplayEvent { t = Now, kind = ReplayEvent.Grenade, a = (short)Idx(thrower), extra = kind == VRGrenade.Kind.Frag ? ReplayEvent.FragKind : ReplayEvent.FlashKind, p0 = from, p1 = from };
        events.Add(e);
        liveGrenades.Add(e);
    }

    void AddBoom(VRGrenade.Kind kind, Vector3 at)
    {
        byte k = kind == VRGrenade.Kind.Frag ? ReplayEvent.FragKind : ReplayEvent.FlashKind;
        int i = liveGrenades.FindIndex(g => g.extra == k);
        if (i >= 0)
        {
            var g = liveGrenades[i];
            liveGrenades.RemoveAt(i);
            g.p1 = at;
            g.t2 = Now;
        }
    }

    public static void Shot(VRFighter shooter, Vector3 from, Vector3 to, VRFighter hit, bool head) { if (Active) current.AddShot(shooter, from, to, hit, head); }
    public static void Kill(VRFighter killer, VRFighter victim, bool head) { if (Active) current.AddKill(killer, victim, head); }
    public static void Throw(VRFighter thrower, Vector3 from, VRGrenade.Kind kind) { if (Active) current.AddThrow(thrower, from, kind); }
    public static void Boom(VRGrenade.Kind kind, Vector3 at) { if (Active) current.AddBoom(kind, at); }
}
