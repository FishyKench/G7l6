using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The maths questions for the homework, made up fresh every time (random numbers; the answer is worked out from them, nothing is
/// stored). The numbers stay small - adding and taking away with 2-digit numbers, times tables up to 9 x 9 (now and then 10, 11, 12
/// or 13 x a single digit), never dividing - and the questions get longer instead: more steps the longer you've been playing and
/// with every gadget you've bought (webcam, ring light, graphics card, streaming PC). Times are worked out first, as in real maths.
/// The answer is never negative.
/// </summary>
public static class MathHomework
{
    public struct Question
    {
        public string text;   // e.g. "47 + 7 x 8 - 25"
        public long answer;
    }

    /// <summary>0 = the first days ... 6 = hardest. Every 3 days played +1, every gadget +1.</summary>
    public static int Level
    {
        get
        {
            int gadgets = 0;
            foreach (var key in new[] { "webcam", "ringlight", "gpu", "streampc" })
                if (RoomUnlock.Has(key)) gadgets++;
            return Mathf.Clamp((Tuning.DayNumber - 1) / 3 + gadgets, 0, 6);
        }
    }

    // level -> (how many parts, how many of them are a "times")
    static readonly (int parts, int times)[] Shapes =
    {
        (2, 0), // 47 + 38
        (3, 0), // 47 + 38 - 25
        (2, 1), // 7 x 8 + 36
        (3, 1), // 52 + 6 x 9 - 37
        (3, 2), // 7 x 8 + 12 x 4 - 59
        (4, 1), // 64 - 28 + 9 x 7 + 45
        (4, 2), // 8 x 9 + 73 - 11 x 6 + 38
    };

    static int R(int min, int max) => Random.Range(min, max + 1);

    public static Question Make(int level)
    {
        var (parts, times) = Shapes[Mathf.Clamp(level, 0, Shapes.Length - 1)];
        // which parts are a "times" (shuffled in among the plain numbers)
        var isTimes = new List<bool>();
        for (int i = 0; i < parts; i++) isTimes.Add(i < times);
        for (int i = isTimes.Count - 1; i > 0; i--) { int j = R(0, i); (isTimes[i], isTimes[j]) = (isTimes[j], isTimes[i]); }

        var text = new System.Text.StringBuilder();
        long total = 0;
        for (int i = 0; i < parts; i++)
        {
            string part;
            long value;
            if (isTimes[i])
            {
                // a single digit times a single digit, now and then 10-13 times a single digit
                int a = Random.value < 0.25f ? R(10, 13) : R(2, 9), b = R(2, 9);
                if (Random.value < 0.5f) (a, b) = (b, a);
                part = $"{a} x {b}";
                value = (long)a * b;
            }
            else
            {
                value = R(10, 99);
                part = value.ToString();
            }
            if (i == 0) { total = value; text.Append(part); continue; }
            // take away only when it can't go below zero
            bool minus = Random.value < 0.5f && total - value >= 0;
            total += minus ? -value : value;
            text.Append(minus ? " - " : " + ").Append(part);
        }
        return new Question { text = text.ToString(), answer = total };
    }
}
