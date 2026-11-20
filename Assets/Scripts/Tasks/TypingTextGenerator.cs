using System.Text;
using UnityEngine;

/// <summary>
/// Makes random homework text out of sentence templates, so every page is different.
/// Uses capital letters, commas and periods so the player has to type them exactly.
/// </summary>
public static class TypingTextGenerator
{
    static readonly string[] Names = { "Sara", "Omar", "Lina", "Adam", "Maya", "Yusuf", "Noor", "Karim", "Lily", "Sam", "Hana", "Leo" };
    static readonly string[] Cities = { "Paris", "London", "Tokyo", "Rome", "Cairo", "Madrid", "Dubai", "Berlin", "Istanbul", "Seoul" };
    static readonly string[] Days = { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };
    static readonly string[] Subjects = { "the teacher", "my brother", "the old man", "a tired student", "our neighbor", "the little girl", "my uncle", "the new boy" };
    static readonly string[] Pronouns = { "she", "he", "they", "we" };
    static readonly string[] Verbs = { "found", "painted", "carried", "forgot", "cleaned", "dropped", "borrowed", "fixed", "opened", "lost", "hid", "sold", "wrapped", "measured" };
    static readonly string[] Adjectives = { "blue", "heavy", "strange", "broken", "tiny", "noisy", "golden", "dusty", "old", "empty", "shiny", "wooden", "ancient", "orange" };
    static readonly string[] Nouns = { "box", "bicycle", "letter", "lamp", "backpack", "umbrella", "notebook", "clock", "map", "guitar", "bottle", "key", "painting", "kite" };
    static readonly string[] Places = { "near the river", "at the old market", "behind the school", "in the garden", "under the bridge", "at the train station", "in the library", "on the roof" };
    static readonly string[] When = { "before dinner", "the next morning", "very quickly", "without a word", "in the rain", "after school", "late at night", "just in time" };
    static readonly string[] Openers = { "After lunch", "In the morning", "Without thinking", "Later that night", "Every summer", "To everyone's surprise", "Before the bell rang" };
    static readonly string[] Joins = { "but", "so", "and", "yet" };

    public static string Generate(int minLength)
    {
        var sb = new StringBuilder();
        int guard = 0;
        while (sb.Length < minLength && guard++ < 20)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(Sentence());
        }
        return sb.ToString();
    }

    static string Sentence()
    {
        switch (Random.Range(0, 6))
        {
            case 0: return Cap($"{Subject()} {Pick(Verbs)} {Article(Pick(Adjectives))} {Pick(Nouns)} {Pick(Places)}.");
            case 1: return $"{Pick(Openers)}, {Subject()} {Pick(Verbs)} {Article(Pick(Adjectives))} {Pick(Nouns)}.";
            case 2: return Cap($"{Subject()} {Pick(Verbs)} the {Pick(Nouns)}, {Pick(Joins)} {Pick(Pronouns)} {Pick(Verbs)} it {Pick(When)}.");
            case 3: return $"In {Pick(Cities)}, {Pick(Names)} and {Pick(Names)} {Pick(Verbs)} the {Pick(Adjectives)} {Pick(Nouns)}.";
            case 4: return $"{Pick(Names)} said the {Pick(Nouns)} was {Pick(Adjectives)}, so {Pick(Pronouns)} {Pick(Verbs)} it {Pick(When)}.";
            default: return $"On {Pick(Days)}, {Pick(Names)} {Pick(Verbs)} {Article(Pick(Adjectives))} {Pick(Nouns)} in {Pick(Cities)}.";
        }
    }

    /// <summary>A random subject: either a name (always capitalized) or a lowercase phrase like "the teacher".</summary>
    static string Subject() => Random.value < 0.5f ? Pick(Names) : Pick(Subjects);

    static string Pick(string[] list) => list[Random.Range(0, list.Length)];

    static string Article(string word) => ("aeiou".IndexOf(char.ToLowerInvariant(word[0])) >= 0 ? "an " : "a ") + word;

    static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
}
