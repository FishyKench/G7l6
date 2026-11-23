using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The world outside the main menu's windows, moving: kids on the swings, the see-saw and the roundabout, kids running about the
/// park, someone walking their dog, cars going by, the Ferris wheel turning on the hill, clouds drifting, now and then a plane.
/// Everything is made by the menu builder (simple shapes); this just moves it.
/// </summary>
public class MenuWorld : MonoBehaviour
{
    public Transform[] swings;          // pivots at the top bar: they swing round local X
    public Transform seesaw;            // pivot in the middle: rocks round local X
    public Transform roundabout;        // spins round Y
    public Transform[] runners;         // kids running in loops
    public Vector3[] runnerCentres;
    public float[] runnerRadii;
    public Transform walker, dog;       // a walk up and down the pavement
    public Vector3 walkFrom, walkTo;
    public Transform ferrisWheel;       // turns round local Z
    public Transform[] ferrisCabins;    // stay upright
    public Transform[] clouds;
    public Vector3 cloudDrift = new Vector3(0.6f, 0f, 0.2f);
    public GameObject[] carTemplates;   // inactive: copies drive along the road
    public Vector3 roadFrom, roadTo, laneOffset;
    public Transform plane;
    public Vector3 planeFrom, planeTo;
    public Transform kite;              // dances on the wind above the park
    public Transform kiteHand;          // the kid holding the string
    public LineRenderer kiteString;
    public Transform ball;              // a kickabout: the ball goes from kid to kid
    public Transform[] kickers;

    readonly List<(Transform car, Vector3 from, Vector3 to, float t, float speed)> cars = new List<(Transform, Vector3, Vector3, float, float)>();
    float nextCar = 2f, walkT, planeT = -1f, nextPlane = 12f;
    Vector3 kiteHome;
    int ballFrom, ballTo = 1;
    float ballT;
    Vector3[] kickerHome;

    void Start()
    {
        if (kite) kiteHome = kite.position;
        if (kickers != null) { kickerHome = new Vector3[kickers.Length]; for (int i = 0; i < kickers.Length; i++) if (kickers[i]) kickerHome[i] = kickers[i].position; }
    }

    void Update()
    {
        float t = Time.time, dt = Time.deltaTime;
        if (swings != null)
            for (int i = 0; i < swings.Length; i++)
                if (swings[i]) swings[i].localRotation = Quaternion.Euler(Mathf.Sin(t * 1.9f + i * 1.7f) * (30f + i * 6f), 0f, 0f);
        if (seesaw) seesaw.localRotation = Quaternion.Euler(Mathf.Sin(t * 1.3f) * 14f, 0f, 0f);
        if (roundabout) roundabout.Rotate(0f, 60f * dt, 0f, Space.Self);
        if (runners != null)
            for (int i = 0; i < runners.Length; i++)
            {
                if (!runners[i]) continue;
                float a = t * (0.55f + 0.1f * i) * (i % 2 == 0 ? 1f : -1f) + i * 2f;
                Vector3 p = runnerCentres[i] + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * runnerRadii[i];
                p.y = runnerCentres[i].y + Mathf.Abs(Mathf.Sin(t * 9f + i)) * 0.12f; // (running bob)
                Vector3 step = p - runners[i].position; step.y = 0f;
                runners[i].position = p;
                if (step.sqrMagnitude > 1e-6f) runners[i].rotation = Quaternion.LookRotation(step.normalized);
            }
        if (walker)
        {
            walkT += dt * 0.035f;
            float k = Mathf.PingPong(walkT, 1f);
            bool back = Mathf.Repeat(walkT, 2f) > 1f;
            Vector3 p = Vector3.Lerp(walkFrom, walkTo, k);
            walker.position = p + Vector3.up * Mathf.Abs(Mathf.Sin(t * 6f)) * 0.04f;
            walker.rotation = Quaternion.LookRotation((back ? walkFrom - walkTo : walkTo - walkFrom).normalized);
            if (dog)
            {
                dog.position = p + walker.forward * 1.2f + walker.right * 0.5f + Vector3.up * Mathf.Abs(Mathf.Sin(t * 10f)) * 0.05f;
                dog.rotation = walker.rotation;
            }
        }
        if (ferrisWheel)
        {
            ferrisWheel.Rotate(0f, 0f, 5f * dt, Space.Self);
            if (ferrisCabins != null) foreach (var c in ferrisCabins) if (c) c.rotation = Quaternion.LookRotation(ferrisWheel.forward, Vector3.up);
        }
        if (clouds != null)
            foreach (var c in clouds)
            {
                if (!c) continue;
                c.position += cloudDrift * dt;
                if ((c.position - transform.position).magnitude > 260f) c.position -= cloudDrift.normalized * 480f; // round again
            }
        Cars(dt);
        Plane(dt);
        Kite(t);
        Kickabout(dt, t);
    }

    void Kite(float t)
    {
        if (!kite) return;
        // swoops and loops on the breeze
        kite.position = kiteHome + new Vector3(Mathf.Sin(t * 0.7f) * 1.6f + Mathf.Sin(t * 1.9f) * 0.5f, Mathf.Sin(t * 1.1f) * 0.9f + Mathf.Sin(t * 2.7f) * 0.25f, Mathf.Cos(t * 0.5f) * 0.6f);
        kite.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(kite.position - (kiteHand ? kiteHand.position : kiteHome), Vector3.up).normalized + Vector3.up * 0.4f) * Quaternion.Euler(0f, 0f, Mathf.Sin(t * 1.3f) * 25f);
        if (kiteString && kiteHand)
        {
            Vector3 a = kiteHand.position + Vector3.up * 1.1f, b = kite.position;
            for (int i = 0; i < kiteString.positionCount; i++)
            {
                float k = i / (float)(kiteString.positionCount - 1);
                kiteString.SetPosition(i, Vector3.Lerp(a, b, k) + Vector3.down * Mathf.Sin(k * Mathf.PI) * 0.8f); // (a little sag)
            }
        }
    }

    void Kickabout(float dt, float t)
    {
        if (!ball || kickers == null || kickers.Length < 2 || kickerHome == null) return;
        // the kids shuffle about their spots, always watching the ball
        for (int i = 0; i < kickers.Length; i++)
        {
            if (!kickers[i]) continue;
            Vector3 home = kickerHome[i] + new Vector3(Mathf.Sin(t * 0.8f + i * 2.1f), 0f, Mathf.Cos(t * 0.6f + i * 1.3f)) * 0.6f;
            if (i == ballTo) home = Vector3.Lerp(home, new Vector3(ball.position.x, home.y, ball.position.z), 0.35f); // running onto it
            kickers[i].position = Vector3.MoveTowards(kickers[i].position, home, dt * 2.5f) + Vector3.up * 0f;
            Vector3 look = ball.position - kickers[i].position; look.y = 0f;
            if (look.sqrMagnitude > 0.01f) kickers[i].rotation = Quaternion.Slerp(kickers[i].rotation, Quaternion.LookRotation(look.normalized), dt * 6f);
        }
        ballT += dt / 1.3f;
        Vector3 from = kickers[ballFrom].position, to = kickers[ballTo].position;
        Vector3 p = Vector3.Lerp(from, to, Mathf.Clamp01(ballT));
        p.y = Mathf.Lerp(kickerHome[ballFrom].y, kickerHome[ballTo].y, ballT) + 0.12f + Mathf.Sin(Mathf.Clamp01(ballT) * Mathf.PI) * (ballFrom % 2 == 0 ? 1.6f : 0.35f);
        ball.position = p;
        ball.Rotate(400f * dt, 0f, 0f, Space.Self);
        if (ballT >= 1f)
        {
            ballT = 0f;
            ballFrom = ballTo;
            do ballTo = Random.Range(0, kickers.Length); while (ballTo == ballFrom);
        }
    }

    void Cars(float dt)
    {
        nextCar -= dt;
        if (nextCar <= 0f && carTemplates != null && carTemplates.Length > 0)
        {
            nextCar = Random.Range(3f, 6f);
            bool reverse = Random.value < 0.5f;
            var template = carTemplates[Random.Range(0, carTemplates.Length)];
            var car = Instantiate(template, transform).transform;
            car.gameObject.SetActive(true);
            Vector3 from = (reverse ? roadTo : roadFrom) + (reverse ? -laneOffset : laneOffset);
            Vector3 to = (reverse ? roadFrom : roadTo) + (reverse ? -laneOffset : laneOffset);
            car.position = from;
            car.rotation = Quaternion.LookRotation((to - from).normalized);
            cars.Add((car, from, to, 0f, Random.Range(7f, 11f)));
        }
        for (int i = cars.Count - 1; i >= 0; i--)
        {
            var c = cars[i];
            if (!c.car) { cars.RemoveAt(i); continue; }
            float len = Vector3.Distance(c.from, c.to);
            c.t += c.speed * dt / Mathf.Max(1f, len);
            c.car.position = Vector3.Lerp(c.from, c.to, c.t);
            if (c.t >= 1f) { Destroy(c.car.gameObject); cars.RemoveAt(i); continue; }
            cars[i] = c;
        }
    }

    void Plane(float dt)
    {
        if (!plane) return;
        if (planeT < 0f)
        {
            plane.gameObject.SetActive(false);
            nextPlane -= dt;
            if (nextPlane <= 0f) { planeT = 0f; nextPlane = Random.Range(25f, 45f); plane.gameObject.SetActive(true); }
            return;
        }
        planeT += dt / 40f;
        plane.position = Vector3.Lerp(planeFrom, planeTo, planeT);
        plane.rotation = Quaternion.LookRotation((planeTo - planeFrom).normalized);
        if (planeT >= 1f) planeT = -1f;
    }
}
