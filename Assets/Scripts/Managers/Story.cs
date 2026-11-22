using System;
using UnityEngine;

/// <summary>
/// The season's story: short texts (your phone) and mails from Jay, Mom, TryHardTina and the league, at set moments of each
/// 3-week season. Some depend on how you're doing (Mom's trust, your rank, followers). Each one comes once (Campaign.Once).
/// CampaignRuntime calls <see cref="Tick"/> every frame during the day.
///
/// The arc: Jay dreams of the Season Final; Mom just started a new job and is trying to trust you with the house; Tina, the
/// league favourite, keeps an eye on you. Week 3 ends with the Season Final - and one of the endings (see CampaignUI.ShowEnding).
/// </summary>
public static class Story
{
    class Beat
    {
        public int week, day;             // week of the season (1-3), day (0 Mon .. 4 Fri)
        public float hour;
        public string id, from;
        public bool mail;
        public Func<bool> when;           // null = always
        public Func<string> subject, body;
    }

    static int S => Campaign.Season;

    static readonly Beat[] Beats =
    {
        // ---------------- week 1
        new Beat { week = 1, day = 0, hour = 15.05f, id = "jay-start", from = "JAY",
            body = () => $"SEASON {S} STARTS TODAY. season final in 3 weeks. you + me. we're doing this" },
        new Beat { week = 1, day = 0, hour = 15.25f, id = "mom-job", from = "Mom", mail = true,
            subject = () => "My new job",
            body = () => "Hi sweetie,\n\nI started the new job today, so I'll be home around 6 every evening. That means you're in charge of the house in the afternoons.\n\n" +
                         "I know your games matter to you. I'm not against them. Just show me you can handle both, and we're fine.\n\nLove, Mom" },
        new Beat { week = 1, day = 3, hour = 15.3f, id = "tina-hello", from = "TryHardTina", mail = true,
            subject = () => "saw you in ranked",
            body = () => "You're the one Kev keeps complaining about? Cute.\n\nI've won the last two season finals. If you want to stop me, you'll need Diamond by week 3. Good luck with that.\n\n- Tina" },
        new Beat { week = 1, day = 4, hour = 15.1f, id = "jay-fri", from = "JAY",
            body = () => Campaign.TournamentToday ? "WE'RE IN THE TOURNAMENT. warm up early ok??" : "we didn't make the tournament... next week. we lock in" },

        // ---------------- week 2
        new Beat { week = 2, day = 0, hour = 15.2f, id = "mom-week2", from = "Mom", mail = true,
            subject = () => Trust.Value >= 60 ? "Proud of you" : Trust.Value < 40 ? "We need to talk" : "Week two",
            body = () => Trust.Value >= 60
                ? "Last week went better than I expected. The house was in one piece and you still found time for your games. Keep it up and I'll stop worrying.\n\nLove, Mom"
                : Trust.Value < 40
                ? "Last week was rough. I came home to a mess more than once. I don't want to be the bad guy, but if this keeps up, the headset goes in my closet.\n\nEarn it back. I know you can.\n\nMom"
                : "One week down. Not perfect, not terrible. Let's make this one better, okay?\n\nLove, Mom" },
        new Beat { week = 2, day = 1, hour = 15.4f, id = "jay-parents", from = "JAY",
            body = () => "my mom says if my grades drop i can't play the final lol. how's yours" },
        new Beat { week = 2, day = 2, hour = 15.3f, id = "tina-stream", from = "TryHardTina", mail = true,
            subject = () => "free coaching",
            body = () => "I stream my scrims on LiveLoop every evening. Watch and learn something before the final.\n\n" +
                         (ClipHub.Followers > 0 ? $"(Saw your channel. {ClipHub.Followers} followers. Adorable.)\n\n" : "") + "- Tina" },
        new Beat { week = 2, day = 3, hour = 16.6f, id = "mom-tired", from = "MOM",
            body = () => Trust.Value >= 50 ? "long day at work. thank you for keeping things going at home. it means a lot x" : "long day. please, just keep the house clean tonight. that's all i ask" },
        new Beat { week = 2, day = 4, hour = 15.1f, id = "jay-week3", from = "JAY",
            body = () => $"one week left. the final needs {Campaign.RankName(Campaign.GateRankFor(Campaign.Week + 1))}. no excuses" },

        // ---------------- week 3: the final week
        new Beat { week = 3, day = 0, hour = 15.08f, id = "league-final", from = "Ranked League", mail = true,
            subject = () => "Season Final - this Friday",
            body = () => $"This is the final week of Season {S}.\n\nThe Season Final is on Friday: 1st place ${Campaign.Prize1st}, 2nd place ${Campaign.Prize2nd}. " +
                         $"You need {Campaign.RankName(Campaign.GateRank)} ({Campaign.GateRating} RP) by Thursday night.\n\n" +
                         $"Players under 18 also need a parent's OK to compete.\n\n- The Ranked League" },
        new Beat { week = 3, day = 0, hour = 15.35f, id = "mom-final", from = "Mom", mail = true,
            subject = () => "About Friday",
            body = () => Trust.Value >= Trust.FinalNeeds
                ? "Jay's mom told me about this Season Final on Friday. If the house stays in one piece this week, you can play. I'll even come home early to watch the end.\n\nI mean it, though. Keep it together.\n\nLove, Mom"
                : $"Jay's mom told me about this Season Final on Friday. Honestly, with how the house has looked, I'm not saying yes yet.\n\nGood report cards this week and we'll see. Prove me wrong.\n\nMom" },
        new Beat { week = 3, day = 2, hour = 15.5f, id = "jay-sleep", from = "JAY",
            body = () => "i can't sleep thinking about friday. are we good? are you good?" },
        new Beat { week = 3, day = 3, hour = 15.3f, id = "tina-final", from = "TryHardTina", mail = true,
            when = () => Campaign.ReachedGate,
            subject = () => "see you friday",
            body = () => "So you actually made it. Fine. See you in the final.\n\nDon't choke.\n\n- Tina" },
        new Beat { week = 3, day = 4, hour = 15.05f, id = "mom-friday", from = "MOM",
            body = () => Campaign.TournamentToday ? "good luck today sweetie!! i'm leaving work early to watch. proud of you either way x"
                       : Campaign.ReachedGate ? "I'm sorry about today. You know why. Next season, okay?"
                       : "I know you wanted the final. Next season. Love you." },
        new Beat { week = 3, day = 4, hour = 15.2f, id = "jay-final", from = "JAY",
            when = () => Campaign.TournamentToday,
            body = () => "THIS IS IT. season final. whatever happens, best season ever" },
    };

    /// <summary>Sends any beat whose moment has come.</summary>
    public static void Tick(float hour)
    {
        int week = Campaign.WeekOfSeason, day = Campaign.DayIndex;
        var dm = DayManager.Instance;
        foreach (var b in Beats)
        {
            if (b.week != week || b.day != day || hour < b.hour) continue;
            if (b.when != null && !b.when()) continue;
            if (!Campaign.Once("story." + b.id)) continue;
            if (b.mail) Mail.Send(b.from, b.subject(), b.body(), Mail.Kind.Info);
            else if (dm) dm.SendText(b.from, b.body());
        }
    }

    // ------------------------------------------------------------------ endings

    public struct Ending
    {
        public string title, tagline, story;
    }

    /// <summary>How the season ended: the Season Final result, Mom's trust and your channel decide it.</summary>
    public static Ending EndingNow()
    {
        var result = Campaign.TournamentOutcome;
        bool won = result == Campaign.TournamentResult.First;
        int trust = Trust.Value, followers = ClipHub.Followers;
        if (won && trust >= 60)
            return new Ending { title = "THE PRO", tagline = "Season champion - and the house survived",
                story = "You won the Season Final and the house is still standing. A team called the Neon Owls slid into your messages with a tryout offer. " +
                        "Mom put the trophy on the fridge and has told everyone at work. Twice. Jay hasn't stopped screaming." };
        if (won)
            return new Ending { title = "CHAMPION... GROUNDED", tagline = "You won the final. Then Mom saw the kitchen.",
                story = "The trophy is on top of the fridge, where you can see it but not reach it - right next to your headset. " +
                        "Was it worth it? Jay says yes. Mom says \"we'll see\"." };
        if (trust < Trust.HomeworkFirstBelow)
            return new Ending { title = "GROUNDED", tagline = "The house won this season",
                story = "Mom unplugged the PC and put the headset in her closet \"until further notice\". The cat now sleeps on your keyboard. " +
                        "There's always next season... if you're allowed." };
        if (followers >= 300)
            return new Ending { title = "THE STREAMER", tagline = "No trophy - but everyone was watching",
                story = $"{followers:N0} people watched you try this season. Your clip of the cat stealing the remote mid-match went everywhere. " +
                        "Mom asked what a \"sub\" is. Next season, you play with an audience." };
        if (trust >= 75)
            return new Ending { title = "THE GOOD KID", tagline = "Mom's proud. That counts.",
                story = "No trophy this time, but Mom took you out for pizza and said she's proud of how you handled everything. " +
                        "Jay is already planning next season. Honestly? Not a bad way to end it." };
        return new Ending { title = "SO CLOSE", tagline = "Next season, for real",
            story = "You fought all season and came up short. Jay sent a voice note that was just a long sigh, then \"next season we're cooking\". " +
                    "Mom left a note on your door: \"Proud of you for trying. Now clean your room.\"" };
    }
}
