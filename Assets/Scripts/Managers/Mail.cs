using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// MyMail's mailbox: every message you have received (newest 50 are kept, saved in PlayerPrefs).
/// Kinds: Job (Mom's side jobs: accept, do it, get paid), Invite (tournament), Delivery (ShopNest notices), Info.
/// Anything can call <see cref="Send"/>; the Browser's MyMail page shows <see cref="Messages"/> and listens to
/// <see cref="Changed"/>. <see cref="Arrived"/> fires once per new message (MailRuntime shows the phone toast).
/// The jobs themselves (spawning the trash bag / plants, paying) live in MailRuntime.
/// </summary>
public static class Mail
{
    public enum Kind { Job, Invite, Delivery, Info }
    public enum JobState { None, Offered, Accepted, Done, Expired }
    public enum JobType { None, Trash, Plants }

    public const int MaxMessages = 50;
    const string Key = "G7l6.Mail.Box";
    static readonly string[] OnceKeys = { "welcome", "invite" };

    [Serializable]
    public class Message
    {
        public int id;
        public string from, subject, body, day, time;
        public bool read;
        public int kind, jobState, jobType, pay;

        public Kind MailKind => (Kind)Mathf.Clamp(kind, 0, 3);
        public JobState State => (JobState)Mathf.Clamp(jobState, 0, 4);
        public JobType Job => (JobType)Mathf.Clamp(jobType, 0, 2);
        public string When => string.IsNullOrEmpty(time) ? day : time;
    }

    [Serializable]
    class SaveData
    {
        public int nextId = 1;
        public List<Message> items = new List<Message>();
    }

    static SaveData data;

    /// <summary>Anything in the mailbox changed (new mail, read, job state).</summary>
    public static event Action Changed;
    /// <summary>A brand-new message arrived.</summary>
    public static event Action<Message> Arrived;

    static void Load()
    {
        if (data != null) return;
        data = new SaveData();
        try
        {
            string json = PlayerPrefs.GetString(Key, "");
            if (!string.IsNullOrEmpty(json))
            {
                var loaded = JsonUtility.FromJson<SaveData>(json);
                if (loaded != null && loaded.items != null) data = loaded;
            }
        }
        catch (Exception) { data = new SaveData(); }
        foreach (var m in data.items) if (m.id >= data.nextId) data.nextId = m.id + 1;
    }

    static void Save()
    {
        PlayerPrefs.SetString(Key, JsonUtility.ToJson(data));
        PlayerPrefs.Save();
    }

    /// <summary>Newest first.</summary>
    public static List<Message> Messages
    {
        get
        {
            Load();
            var list = new List<Message>(data.items);
            list.Reverse();
            return list;
        }
    }

    public static int Unread
    {
        get
        {
            Load();
            int n = 0;
            foreach (var m in data.items) if (!m.read) n++;
            return n;
        }
    }

    public static Message Find(int id)
    {
        Load();
        return data.items.Find(m => m.id == id);
    }

    /// <summary>Delivers a message into the inbox (unread).</summary>
    public static Message Send(string from, string subject, string body, Kind kind, JobType job = JobType.None, int pay = 0)
    {
        Load();
        var m = new Message
        {
            id = data.nextId++,
            from = from, subject = subject, body = body,
            day = Wallet.DayLabel, time = Clock(),
            read = false, kind = (int)kind,
            jobType = (int)job, pay = pay,
            jobState = (int)(kind == Kind.Job ? JobState.Offered : JobState.None),
        };
        data.items.Add(m);
        while (data.items.Count > MaxMessages) data.items.RemoveAt(0);
        Save();
        Changed?.Invoke();
        Arrived?.Invoke(m);
        return m;
    }

    public static void MarkRead(int id)
    {
        var m = Find(id);
        if (m == null || m.read) return;
        m.read = true;
        Save();
        Changed?.Invoke();
    }

    public static void SetJobState(int id, JobState state)
    {
        var m = Find(id);
        if (m == null || m.State == state) return;
        m.jobState = (int)state;
        Save();
        Changed?.Invoke();
    }

    /// <summary>True the first time it's called for this key (survives restarts); used for the welcome and invite mails.</summary>
    public static bool Once(string key)
    {
        string k = "G7l6.Mail.Once." + key;
        if (PlayerPrefs.GetInt(k, 0) == 1) return false;
        PlayerPrefs.SetInt(k, 1);
        PlayerPrefs.Save();
        return true;
    }

    /// <summary>Debug: empty the mailbox and forget the one-off mails.</summary>
    public static void ResetAll()
    {
        PlayerPrefs.DeleteKey(Key);
        foreach (var k in OnceKeys) PlayerPrefs.DeleteKey("G7l6.Mail.Once." + k);
        data = new SaveData();
        Changed?.Invoke();
    }

    /// <summary>"3:42 PM" from the day's clock ("" if there's no day running).</summary>
    public static string Clock()
    {
        var day = DayManager.Instance;
        if (!day) return "";
        float hour = day.Hour;
        int h = Mathf.FloorToInt(hour), m = Mathf.FloorToInt((hour - h) * 60f);
        return $"{(h > 12 ? h - 12 : h)}:{m:00} {(h >= 12 ? "PM" : "AM")}";
    }
}
