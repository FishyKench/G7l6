using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The food and cookware models (Kenney Food Kit, Assets/Models/FBX format) the kitchen needs at runtime, linked by the kitchen tool
/// (HouseBuilder.BuildBiggerKitchen). Cooking spawns copies of these: "carrot", "onion", "pot", "frying-pan"...
/// </summary>
public class CookingKit : MonoBehaviour
{
    [Serializable]
    public class Entry
    {
        public string id;
        public GameObject model;
    }

    public List<Entry> models = new List<Entry>();

    public static CookingKit Instance { get; private set; }
    void Awake() => Instance = this;

    public GameObject Model(string id)
    {
        foreach (var e in models) if (e.id == id && e.model) return e.model;
        return null;
    }
}
