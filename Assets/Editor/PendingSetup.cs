using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-time setup jobs requested from outside Unity (e.g. by Claude): a request file in
/// Library/G7l6/ is picked up after the next script reload, the job runs once, and the file is
/// deleted. Nothing happens if there's no request file.
///   add-cat.request   - put the cat in the house scene
///   cat-size.request  - scale the cat (file contains the size, e.g. 1.5)
///   fix-stairs.request - smooth ramp over the stairs + re-bake the cat's walk map
///   player-room.request - redo just your bedroom as the gaming room (the rest of NewHouse stays)
///   fix-colliders.request - snug furniture colliders in NewHouse (nothing else changes)
///   ranked-match.request - your gaming desk gets the ranked match, homework moves to the office desk
///   vr-match.request - builds the VR arena scene and puts the VR headset (the ranked match) on your desk
///   finish-house.request - plank floors, wall joints, skirting boards, wardrobe + laundry pile in your room
/// </summary>
[InitializeOnLoad]
static class PendingSetup
{
    const string AddCatRequest = "Library/G7l6/add-cat.request";
    const string CatSizeRequest = "Library/G7l6/cat-size.request";
    const string FixStairsRequest = "Library/G7l6/fix-stairs.request";
    const string CatOverhaulRequest = "Library/G7l6/cat-overhaul.request";
    const string BuildHouseRequest = "Library/G7l6/build-house.request";
    const string PlayerRoomRequest = "Library/G7l6/player-room.request";
    const string CollidersRequest = "Library/G7l6/fix-colliders.request";
    const string MatchRequest = "Library/G7l6/ranked-match.request";
    const string VRRequest = "Library/G7l6/vr-match.request";
    const string FinishRequest = "Library/G7l6/finish-house.request";
    const double GiveUpAfterSeconds = 120;

    static double startedAt;
    static double nextCheck;

    static PendingSetup()
    {
        if (!File.Exists(AddCatRequest) && !File.Exists(CatSizeRequest) && !File.Exists(FixStairsRequest) && !File.Exists(CatOverhaulRequest)
            && !File.Exists(BuildHouseRequest) && !File.Exists(PlayerRoomRequest) && !File.Exists(CollidersRequest) && !File.Exists(MatchRequest) && !File.Exists(VRRequest) && !File.Exists(FinishRequest)) return;
        startedAt = EditorApplication.timeSinceStartup;
        EditorApplication.update += Poll;
    }

    static void Poll()
    {
        double now = EditorApplication.timeSinceStartup;
        if (now < nextCheck) return;
        nextCheck = now + 1.0;

        bool addCat = File.Exists(AddCatRequest);
        bool resize = File.Exists(CatSizeRequest);
        bool stairs = File.Exists(FixStairsRequest);
        bool overhaul = File.Exists(CatOverhaulRequest);
        bool buildHouse = File.Exists(BuildHouseRequest);
        bool playerRoom = File.Exists(PlayerRoomRequest);
        bool colliders = File.Exists(CollidersRequest);
        bool match = File.Exists(MatchRequest);
        bool vr = File.Exists(VRRequest);
        bool finish = File.Exists(FinishRequest);
        if (!addCat && !resize && !stairs && !overhaul && !buildHouse && !playerRoom && !colliders && !match && !vr && !finish) { EditorApplication.update -= Poll; return; }
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;

        // Wait for the cat model to finish importing.
        if (addCat && !AssetDatabase.LoadAssetAtPath<GameObject>(HouseTaskSetup.KittyPrefabPath))
        {
            if (now - startedAt > GiveUpAfterSeconds)
            {
                EditorApplication.update -= Poll;
                Debug.LogWarning("[G7l6] Waiting to add the cat, but the cat model isn't imported. Run G7l6 > Add Cat To House once it is.");
            }
            return;
        }

        EditorApplication.update -= Poll;

        if (addCat)
        {
            File.Delete(AddCatRequest);
            HouseTaskSetup.AddCatToHouse();
        }
        if (resize)
        {
            string text = File.ReadAllText(CatSizeRequest).Trim();
            File.Delete(CatSizeRequest);
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float size)) size = 1.5f;
            HouseTaskSetup.SetCatSize(size);
        }
        if (stairs)
        {
            File.Delete(FixStairsRequest);
            HouseTaskSetup.FixStairsMenu();
        }
        if (overhaul)
        {
            File.Delete(CatOverhaulRequest);
            HouseTaskSetup.CatOverhaul();
        }
        if (buildHouse)
        {
            File.Delete(BuildHouseRequest);
            HouseBuilder.Build();
        }
        if (playerRoom)
        {
            File.Delete(PlayerRoomRequest);
            HouseBuilder.RedoPlayerRoom();
        }
        if (colliders)
        {
            File.Delete(CollidersRequest);
            HouseBuilder.FixFurnitureColliders();
        }
        if (match)
        {
            File.Delete(MatchRequest);
            HouseBuilder.SetUpRankedMatch();
        }
        if (vr)
        {
            File.Delete(VRRequest);
            VRArenaBuilder.SetUp();
        }
        if (finish)
        {
            File.Delete(FinishRequest);
            HouseBuilder.FinishHouse();
        }
    }
}
