using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Debug helper: makes up a believable recorded round (16 fighters walking the arena's walk map between the
/// capture points, firefights, a triple kill and a capture by you, one death, grenades) so the Clip Studio can be
/// tested without playing a VR round. Press F11 in the editor / a development build.
/// </summary>
public static class ReplaySynth
{
    class Walker
    {
        public int team;
        public Vector3 pos;
        public List<Vector3> path = new List<Vector3>();
        public int next;
        public float speed = 4.5f, yaw;
        public float deadUntil = -1f;
        public int flag;
    }

    struct Plan
    {
        public float t;
        public int killer, victim;
        public bool head, done;
    }

    public static ReplayRound Make()
    {
        var arena = VRArena.Instance;
        if (!arena || !arena.Ready || arena.Flags.Count == 0) return null;
        var rng = new System.Random(Random.Range(1, 100000));
        float R() => (float)rng.NextDouble();
        const int Count = 16;
        float duration = 52f;
        int ticks = Mathf.RoundToInt(duration / ReplayRound.Step) + 1;

        var flags = new List<VRFlag>(arena.Flags);
        var r = new ReplayRound
        {
            stamp = Replay.NowStamp() + " (test)", ranked = true, won = true, scoreYou = 100, scoreThem = 64, duration = duration, ticks = ticks, fighters = Count,
            names = new string[Count], teams = new byte[Count],
            flagPos = new Vector3[flags.Count], flagLetters = new string[flags.Count],
            pos = new float[ticks * Count * 3], yaw = new ushort[ticks * Count], state = new byte[ticks * Count],
            camPos = new float[ticks * 3], camAng = new ushort[ticks * 3], camFov = new byte[ticks],
        };
        for (int i = 0; i < flags.Count; i++) { r.flagPos[i] = flags[i].Position; r.flagLetters[i] = flags[i].letter; }
        string[] allies = { "You", "Jay", "Nova", "Rook", "Pixel", "Echo", "Vex", "Kilo" };
        string[] enemies = { "CampLord", "MrNoScope", "LagSwitch", "Ping999", "Zer0Chill", "Sweaty_Kev", "GrandmaGamer", "N00bSlay3r" };
        var w = new Walker[Count];
        for (int i = 0; i < Count; i++)
        {
            int team = i < 8 ? 0 : 1;
            r.names[i] = team == 0 ? allies[i] : enemies[i - 8];
            r.teams[i] = (byte)team;
            w[i] = new Walker { team = team, pos = Ground(arena.SpawnPosition(team, i % 8)), flag = i % flags.Count, speed = 4f + R() * 2.5f };
            NewPath(w[i], flags[w[i].flag].RandomSpot(0.5f));
        }

        // The story: you kill three in a row, take a point, get another kill and die near the end. Victims are
        // whoever is nearest to the killer at that moment, so the shots always make sense.
        var plan = new List<Plan>
        {
            new Plan { t = 14.0f, killer = 0, victim = -1 }, new Plan { t = 15.2f, killer = 0, victim = -1, head = true },
            new Plan { t = 16.5f, killer = 0, victim = -1 }, new Plan { t = 31.0f, killer = 0, victim = -1, head = true },
            new Plan { t = 44.0f, killer = -1, victim = 0 },
        };
        for (int i = 0; i < 14; i++) plan.Add(new Plan { t = 3f + R() * (duration - 6f), killer = rng.Next(1, Count), victim = -1, head = R() < 0.25f });
        plan.Sort((x, y) => x.t.CompareTo(y.t));

        int yourFlag = 1 % flags.Count;
        NewPath(w[0], flags[yourFlag].RandomSpot(0.25f));
        w[0].speed = 5.6f;
        float arrivedAt = -1f, holdUntil = -1f;

        var shots = new List<ReplayEvent>();
        float nextBurst = 0f;
        Vector3 you0 = w[0].pos;
        Vector3 yourFlat = Vector3.forward;
        for (int k = 0; k < ticks; k++)
        {
            float t = k * ReplayRound.Step;
            for (int i = 0; i < Count; i++)
            {
                var m = w[i];
                bool dead = t < m.deadUntil;
                if (!dead && m.deadUntil > 0f)
                {
                    m.deadUntil = -1f;
                    m.pos = Ground(arena.SpawnPosition(m.team, i % 8));
                    NewPath(m, flags[rng.Next(flags.Count)].RandomSpot(0.5f));
                }
                if (i == 0 && arrivedAt < 0f && Vector3.Distance(m.pos, flags[yourFlag].Position) < 3f) { arrivedAt = t; holdUntil = t + 7f; }
                if (!dead && !(i == 0 && t < holdUntil)) Step(m, ReplayRound.Step, flags, rng);
                int o = k * Count + i;
                r.pos[o * 3] = m.pos.x; r.pos[o * 3 + 1] = m.pos.y; r.pos[o * 3 + 2] = m.pos.z;
                r.yaw[o] = ReplayRound.PackAngle(m.yaw);
                r.state[o] = (byte)(dead ? 0 : ReplayRound.Alive | (i != 0 && rng.NextDouble() < 0.002 ? ReplayRound.Crouch : 0));
            }
            for (int pi = 0; pi < plan.Count; pi++)
            {
                var kl = plan[pi];
                if (kl.done || t < kl.t) continue;
                kl.done = true;
                plan[pi] = kl;
                int killer = kl.killer, victim = kl.victim;
                if (killer >= 0 && t < w[killer].deadUntil) continue;
                if (victim < 0 && killer >= 0) victim = Nearest(w, Count, killer, t);
                if (killer < 0 && victim >= 0) killer = Nearest(w, Count, victim, t);
                if (killer < 0 || victim < 0 || t < w[victim].deadUntil) continue;
                var vm = w[victim];
                Vector3 vpos = vm.pos, kpos = w[killer].pos;
                vm.deadUntil = t + 4f;
                r.state[k * Count + victim] = 0;
                Vector3 d2 = vpos - kpos;
                d2.y = 0f;
                if (d2.sqrMagnitude > 0.01f) w[killer].yaw = Mathf.Atan2(d2.x, d2.z) * Mathf.Rad2Deg;
                r.yaw[k * Count + killer] = ReplayRound.PackAngle(w[killer].yaw);
                Vector3 eye = kpos + Vector3.up * 1.5f;
                for (int s = 0; s < 5; s++)
                {
                    bool last = s == 4;
                    Vector3 to = vpos + Vector3.up * (last && kl.head ? 1.62f : 1.1f);
                    shots.Add(new ReplayEvent
                    {
                        t = Mathf.Max(0f, t - 0.4f + s * 0.1f), kind = ReplayEvent.Shot, a = (short)killer, b = (short)(last ? victim : -1),
                        extra = (byte)(last ? (kl.head ? 2 : 1) : 0), p0 = eye + (to - eye).normalized * 0.6f,
                        p1 = last ? to : to + new Vector3(R() - 0.5f, R() - 0.5f, R() - 0.5f),
                    });
                }
                r.events.Add(new ReplayEvent { t = t, kind = ReplayEvent.Kill, a = (short)killer, b = (short)victim, extra = (byte)(kl.head ? 1 : 0), p0 = vpos + Vector3.up * 1.1f });
            }
            // Random bursts of fire in the background.
            if (t >= nextBurst)
            {
                nextBurst = t + 0.35f + R() * 0.5f;
                int a = rng.Next(1, Count);
                int bi = rng.Next(0, Count);
                if (t >= w[a].deadUntil && r.teams[bi] != r.teams[a] && t >= w[bi].deadUntil && Vector3.Distance(w[a].pos, w[bi].pos) < 45f)
                {
                    Vector3 from = w[a].pos + Vector3.up * 1.3f;
                    Vector3 to = w[bi].pos + Vector3.up * (0.9f + R() * 0.4f) + new Vector3(R() - 0.5f, 0f, R() - 0.5f);
                    for (int s = 0; s < 3; s++)
                        shots.Add(new ReplayEvent { t = t + s * 0.09f, kind = ReplayEvent.Shot, a = (short)a, b = -1, extra = 0, p0 = from + (to - from).normalized * 0.6f, p1 = to });
                }
            }
            // Your camera: forward along your walk, turning to the victim around a kill.
            var you = w[0];
            Vector3 look = you.pos - you0;
            you0 = you.pos;
            look.y = 0f;
            if (look.sqrMagnitude > 1e-6f) yourFlat = look;
            float yourYaw = Mathf.Atan2(yourFlat.x, yourFlat.z) * Mathf.Rad2Deg, pitch = 2f;
            foreach (var e in r.events)
            {
                if (e.kind != ReplayEvent.Kill || e.a != 0 || t < e.t - 0.8f || t > e.t + 0.5f) continue;
                Vector3 d = e.p0 - (you.pos + Vector3.up * 1.6f);
                yourYaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            }
            r.yaw[k * Count] = ReplayRound.PackAngle(yourYaw);
            Vector3 camP = you.pos + Vector3.up * 1.6f;
            r.camPos[k * 3] = camP.x; r.camPos[k * 3 + 1] = camP.y; r.camPos[k * 3 + 2] = camP.z;
            r.camAng[k * 3] = ReplayRound.PackAngle(pitch);
            r.camAng[k * 3 + 1] = ReplayRound.PackAngle(yourYaw);
            r.camAng[k * 3 + 2] = ReplayRound.PackAngle(t < you.deadUntil ? 35f : 0f);
            r.camFov[k] = 80;
        }

        // Flag changes: the point you stand on (a few seconds after you arrive) and a couple of others.
        float capT = arrivedAt >= 0f ? arrivedAt + 3f : 12f;
        int other = yourFlag == 0 ? flags.Count - 1 : 0;
        r.events.Add(new ReplayEvent { t = capT, kind = ReplayEvent.Flag, a = (short)yourFlag, extra = 1, p0 = flags[yourFlag].Position });
        r.events.Add(new ReplayEvent { t = 26f, kind = ReplayEvent.Flag, a = (short)other, extra = 1, p0 = flags[other].Position });
        r.events.Add(new ReplayEvent { t = 38f, kind = ReplayEvent.Flag, a = (short)(flags.Count - 1), extra = 2, p0 = flags[flags.Count - 1].Position });
        // A frag and a flash.
        r.events.Add(new ReplayEvent { t = 20f, t2 = 22.4f, kind = ReplayEvent.Grenade, a = 9, extra = ReplayEvent.FragKind, p0 = w[9].pos + Vector3.up * 1.5f, p1 = flags[0].Position + Vector3.up * 0.3f });
        r.events.Add(new ReplayEvent { t = 27f, t2 = 28.6f, kind = ReplayEvent.Grenade, a = 2, extra = ReplayEvent.FlashKind, p0 = w[2].pos + Vector3.up * 1.5f, p1 = flags[flags.Count - 1].Position + Vector3.up * 0.3f });
        r.events.AddRange(shots);
        r.events.Sort((a, b) => a.t.CompareTo(b.t));
        Replay.Add(r);
        return r;
    }

    /// <summary>The nearest living enemy of fighter 'other' within 70 m, or -1.</summary>
    static int Nearest(Walker[] w, int count, int other, float t)
    {
        int best = -1;
        float bd = 70f;
        for (int j = 0; j < count; j++)
        {
            if (w[j].team == w[other].team || t < w[j].deadUntil) continue;
            float d = Vector3.Distance(w[j].pos, w[other].pos);
            if (d < bd) { bd = d; best = j; }
        }
        return best;
    }

    static Vector3 Ground(Vector3 p)
    {
        if (NavMesh.SamplePosition(p, out var hit, 8f, NavMesh.AllAreas)) return hit.position;
        return p;
    }

    static void NewPath(Walker m, Vector3 target)
    {
        m.path.Clear();
        m.next = 0;
        var path = new NavMeshPath();
        if (NavMesh.CalculatePath(m.pos, Ground(target), NavMesh.AllAreas, path) && path.corners.Length > 1)
            m.path.AddRange(path.corners);
        else { m.path.Add(m.pos); m.path.Add(Ground(target)); }
    }

    static void Step(Walker m, float dt, List<VRFlag> flags, System.Random rng)
    {
        float move = m.speed * dt;
        while (move > 0f && m.next < m.path.Count)
        {
            Vector3 to = m.path[m.next];
            Vector3 d = to - m.pos;
            float len = d.magnitude;
            if (len < 0.05f) { m.next++; continue; }
            Vector3 flat = new Vector3(d.x, 0f, d.z);
            if (flat.sqrMagnitude > 0.0001f) m.yaw = Mathf.MoveTowardsAngle(m.yaw, Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg, 540f * dt);
            if (len <= move) { m.pos = to; move -= len; m.next++; }
            else { m.pos += d / len * move; move = 0f; }
        }
        if (m.next >= m.path.Count)
        {
            m.flag = rng.Next(flags.Count);
            NewPath(m, flags[m.flag].RandomSpot(0.6f));
        }
    }
}
