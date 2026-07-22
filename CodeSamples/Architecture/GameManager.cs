using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [Header("Scene")]
    public string targetScene;
    public string loadingSceneName = "LoadingScene";

    [Header("Spawn")]
    public string targetSpawnID;

    [Header("Respawn")]
    public Vector3 respawnPosition;

    [Header("Transport State")]
    public bool transportTank = false;
    public bool playerIsInTank = false;

    [Header("Loading State")]
    public bool playerSpawnFinished = false;
    public bool tankSpawnFinished = true;

    [Header("Level Start / Restart Point")]
    [SerializeField] private string levelStartScene;
    [SerializeField] private string levelStartSpawnID;
    [SerializeField] private bool levelStartTransportTank;
    [SerializeField] private bool levelStartPlayerIsInTank;

    [Header("Save Data")]
    [SerializeField] private int activeSaveSlot = -1;
    [SerializeField] private bool applyRuntimeDataOnNextScene;
    [SerializeField] private PlayerSaveData runtimePlayerData = new PlayerSaveData();
    [SerializeField] private PlayerSaveData levelStartPlayerData = new PlayerSaveData();

    [Header("Debug")]
    [SerializeField] private bool debugMode = true;

    private bool restartingLevel;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (debugMode)
                Debug.Log("[GameManager] CREATED");
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void BeginSceneLoad()
    {
        playerSpawnFinished = false;
        tankSpawnFinished = transportTank ? false : true;

        if (debugMode)
        {
            Debug.Log("[GameManager] Scene load started");
            Debug.Log("[GameManager] Target Scene: " + targetScene);
            Debug.Log("[GameManager] Target Spawn ID: " + targetSpawnID);
            Debug.Log("[GameManager] Transport Tank: " + transportTank);
            Debug.Log("[GameManager] Player Is In Tank: " + playerIsInTank);
        }
    }

    public void MarkPlayerSpawnFinished()
    {
        playerSpawnFinished = true;

        if (debugMode)
            Debug.Log("[GameManager] PLAYER SPAWN FINISHED");
    }

    public void MarkTankSpawnFinished()
    {
        tankSpawnFinished = true;

        if (debugMode)
            Debug.Log("[GameManager] TANK SPAWN FINISHED");
    }

    public bool IsEverythingSpawned()
    {
        return playerSpawnFinished && tankSpawnFinished;
    }

    public void SetPlayerInTank(bool value)
    {
        playerIsInTank = value;

        if (debugMode)
            Debug.Log("[GameManager] Player Is In Tank = " + value);
    }

    public void SetTransportTank(bool value)
    {
        transportTank = value;

        if (debugMode)
            Debug.Log("[GameManager] Transport Tank = " + value);
    }

    // =========================================================
    // MAIN MENU / SAVE SLOTS
    // =========================================================

    public void PrepareNewGame(string sceneName, string spawnID, bool withTank)
    {
        Time.timeScale = 1f;

        activeSaveSlot = -1;
        applyRuntimeDataOnNextScene = false;
        runtimePlayerData.Clear();
        levelStartPlayerData.Clear();

        targetScene = sceneName;
        targetSpawnID = spawnID;

        transportTank = withTank;
        playerIsInTank = withTank;

        playerSpawnFinished = false;
        tankSpawnFinished = withTank ? false : true;
        restartingLevel = false;

        if (debugMode)
            Debug.Log("[GameManager] New game prepared.");
    }

    public bool PrepareContinueFromSlot(int slot)
    {
        if (!SaveSlotExists(slot))
        {
            Debug.LogWarning("[GameManager] Save slot " + slot + " is empty.");
            return false;
        }

        Time.timeScale = 1f;

        activeSaveSlot = slot;

        targetScene = PlayerPrefs.GetString(GetSceneKey(slot), string.Empty);
        targetSpawnID = PlayerPrefs.GetString(GetSpawnKey(slot), string.Empty);

        transportTank = PlayerPrefs.GetInt(GetTankKey(slot), 0) == 1;
        playerIsInTank = PlayerPrefs.GetInt(GetPlayerInTankKey(slot), transportTank ? 1 : 0) == 1;

        LoadPlayerDataFromSlot(slot, runtimePlayerData);
        applyRuntimeDataOnNextScene = runtimePlayerData.hasData;

        playerSpawnFinished = false;
        tankSpawnFinished = transportTank ? false : true;
        restartingLevel = false;

        if (debugMode)
        {
            Debug.Log("[GameManager] Continue prepared from slot " + slot);
            Debug.Log("[GameManager] Scene: " + targetScene);
            Debug.Log("[GameManager] Spawn ID: " + targetSpawnID);
            Debug.Log("[GameManager] Transport Tank: " + transportTank);
            Debug.Log("[GameManager] Has Player Data: " + runtimePlayerData.hasData);
        }

        return !string.IsNullOrWhiteSpace(targetScene);
    }

    public void SaveGameToSlot(int slot, string fallbackScene, string fallbackSpawnID)
    {
        string sceneName = !string.IsNullOrWhiteSpace(targetScene)
            ? targetScene
            : fallbackScene;

        string spawnID = !string.IsNullOrWhiteSpace(targetSpawnID)
            ? targetSpawnID
            : fallbackSpawnID;

        CapturePlayerRuntimeData();

        PlayerPrefs.SetString(GetSceneKey(slot), sceneName);
        PlayerPrefs.SetString(GetSpawnKey(slot), spawnID);
        PlayerPrefs.SetInt(GetTankKey(slot), transportTank ? 1 : 0);
        PlayerPrefs.SetInt(GetPlayerInTankKey(slot), playerIsInTank ? 1 : 0);

        SavePlayerDataToSlot(slot, runtimePlayerData);

        PlayerPrefs.Save();
        activeSaveSlot = slot;

        if (debugMode)
        {
            Debug.Log("[GameManager] Saved game to slot " + slot);
            Debug.Log("[GameManager] Scene: " + sceneName);
            Debug.Log("[GameManager] Spawn ID: " + spawnID);
            Debug.Log("[GameManager] Transport Tank: " + transportTank);
            Debug.Log("[GameManager] Player In Tank: " + playerIsInTank);
        }
    }

    public static bool SaveSlotExists(int slot)
    {
        return PlayerPrefs.HasKey(GetSceneKey(slot));
    }

    public static string GetSceneKey(int slot) => "SaveSlot_" + slot + "_Scene";
    public static string GetSpawnKey(int slot) => "SaveSlot_" + slot + "_SpawnID";
    public static string GetTankKey(int slot) => "SaveSlot_" + slot + "_WithTank";
    public static string GetPlayerInTankKey(int slot) => "SaveSlot_" + slot + "_PlayerInTank";

    private static string GetPlayerDataKey(int slot, string field)
    {
        return "SaveSlot_" + slot + "_Player_" + field;
    }

    private void SavePlayerDataToSlot(int slot, PlayerSaveData data)
    {
        if (data == null || !data.hasData)
        {
            PlayerPrefs.SetInt(GetPlayerDataKey(slot, "HasData"), 0);
            return;
        }

        PlayerPrefs.SetInt(GetPlayerDataKey(slot, "HasData"), 1);
        PlayerPrefs.SetFloat(GetPlayerDataKey(slot, "CatHealth"), data.catHealth);
        PlayerPrefs.SetInt(GetPlayerDataKey(slot, "CurrentAmmo"), data.currentAmmo);
        PlayerPrefs.SetInt(GetPlayerDataKey(slot, "ReservedAmmo"), data.reservedAmmo);
        PlayerPrefs.SetInt(GetPlayerDataKey(slot, "SelectedSlot"), data.selectedSlot);

        PlayerPrefs.SetInt(GetPlayerDataKey(slot, "Slot2Full"), data.slot2Full ? 1 : 0);
        PlayerPrefs.SetString(GetPlayerDataKey(slot, "Slot2ItemID"), data.slot2ItemID ?? string.Empty);
        PlayerPrefs.SetInt(GetPlayerDataKey(slot, "Slot2Count"), data.slot2Count);

        PlayerPrefs.SetInt(GetPlayerDataKey(slot, "Slot3Full"), data.slot3Full ? 1 : 0);
        PlayerPrefs.SetString(GetPlayerDataKey(slot, "Slot3ItemID"), data.slot3ItemID ?? string.Empty);
        PlayerPrefs.SetInt(GetPlayerDataKey(slot, "Slot3Count"), data.slot3Count);
    }

    private void LoadPlayerDataFromSlot(int slot, PlayerSaveData data)
    {
        if (data == null)
            return;

        data.Clear();

        if (PlayerPrefs.GetInt(GetPlayerDataKey(slot, "HasData"), 0) != 1)
            return;

        data.hasData = true;
        data.catHealth = PlayerPrefs.GetFloat(GetPlayerDataKey(slot, "CatHealth"), 100f);
        data.currentAmmo = PlayerPrefs.GetInt(GetPlayerDataKey(slot, "CurrentAmmo"), 30);
        data.reservedAmmo = PlayerPrefs.GetInt(GetPlayerDataKey(slot, "ReservedAmmo"), 60);
        data.selectedSlot = PlayerPrefs.GetInt(GetPlayerDataKey(slot, "SelectedSlot"), -1);

        data.slot2Full = PlayerPrefs.GetInt(GetPlayerDataKey(slot, "Slot2Full"), 0) == 1;
        data.slot2ItemID = PlayerPrefs.GetString(GetPlayerDataKey(slot, "Slot2ItemID"), string.Empty);
        data.slot2Count = PlayerPrefs.GetInt(GetPlayerDataKey(slot, "Slot2Count"), 0);

        data.slot3Full = PlayerPrefs.GetInt(GetPlayerDataKey(slot, "Slot3Full"), 0) == 1;
        data.slot3ItemID = PlayerPrefs.GetString(GetPlayerDataKey(slot, "Slot3ItemID"), string.Empty);
        data.slot3Count = PlayerPrefs.GetInt(GetPlayerDataKey(slot, "Slot3Count"), 0);
    }

    // =========================================================
    // RUNTIME PLAYER DATA BETWEEN LEVELS
    // =========================================================

    public void CapturePlayerRuntimeData()
    {
        runtimePlayerData.Clear();
        runtimePlayerData.hasData = true;

        MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        int count = 0;

        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (behaviour is IPlayerSaveParticipant participant)
            {
                participant.CapturePlayerSaveData(runtimePlayerData);
                count++;
            }
        }

        if (debugMode)
            Debug.Log("[GameManager] Runtime player data captured. Participants: " + count);
    }

    public void MarkRuntimeDataForNextScene()
    {
        applyRuntimeDataOnNextScene = runtimePlayerData != null && runtimePlayerData.hasData;

        if (debugMode)
            Debug.Log("[GameManager] Runtime player data marked for next scene: " + applyRuntimeDataOnNextScene);
    }

    public void ApplyPendingRuntimePlayerDataToScene()
    {
        if (!applyRuntimeDataOnNextScene)
            return;

        if (runtimePlayerData == null || !runtimePlayerData.hasData)
        {
            applyRuntimeDataOnNextScene = false;
            return;
        }

        MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        int count = 0;

        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (behaviour is IPlayerSaveParticipant participant)
            {
                participant.ApplyPlayerSaveData(runtimePlayerData);
                count++;
            }
        }

        applyRuntimeDataOnNextScene = false;

        if (debugMode)
            Debug.Log("[GameManager] Runtime player data applied. Participants: " + count);
    }

    // =========================================================
    // LEVEL START / RESTART
    // =========================================================

    public void SaveRestartPoint(string sceneName, string spawnID, bool withTank)
    {
        levelStartScene = sceneName;
        levelStartSpawnID = spawnID;

        levelStartTransportTank = withTank;
        levelStartPlayerIsInTank = withTank;

        targetScene = sceneName;
        targetSpawnID = spawnID;

        transportTank = withTank;
        playerIsInTank = withTank;

        if (debugMode)
        {
            Debug.Log(
                "[GameManager] Restart point saved | Scene: " +
                levelStartScene +
                " | SpawnID: " +
                levelStartSpawnID +
                " | WithTank: " +
                levelStartTransportTank
            );
        }
    }

    public void SaveLevelStartState()
    {
        if (string.IsNullOrWhiteSpace(levelStartScene))
            levelStartScene = SceneManager.GetActiveScene().name;

        if (string.IsNullOrWhiteSpace(levelStartSpawnID))
            levelStartSpawnID = targetSpawnID;

        CaptureLevelStartPlayerData();

        MonoBehaviour[] allBehaviours = FindObjectsByType<MonoBehaviour>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        foreach (MonoBehaviour behaviour in allBehaviours)
        {
            if (behaviour is ILevelResettable resettable)
                resettable.SaveLevelStartState();
        }

        if (debugMode)
        {
            Debug.Log("[GameManager] LEVEL START STATE SAVED");
            Debug.Log("[GameManager] Scene: " + levelStartScene);
            Debug.Log("[GameManager] Spawn ID: " + levelStartSpawnID);
            Debug.Log("[GameManager] Transport Tank: " + levelStartTransportTank);
            Debug.Log("[GameManager] Player In Tank: " + levelStartPlayerIsInTank);
            Debug.Log("[GameManager] Has Level Start Player Data: " + levelStartPlayerData.hasData);
        }
    }

    private void CaptureLevelStartPlayerData()
    {
        levelStartPlayerData.Clear();
        levelStartPlayerData.hasData = true;

        MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (behaviour is IPlayerSaveParticipant participant)
                participant.CapturePlayerSaveData(levelStartPlayerData);
        }
    }

    public void RestartLevelFromStart()
    {
        if (restartingLevel)
            return;

        restartingLevel = true;

        Time.timeScale = 1f;

        if (string.IsNullOrWhiteSpace(levelStartScene))
            levelStartScene = SceneManager.GetActiveScene().name;

        if (string.IsNullOrWhiteSpace(levelStartSpawnID))
            levelStartSpawnID = targetSpawnID;

        targetScene = levelStartScene;
        targetSpawnID = levelStartSpawnID;

        transportTank = levelStartTransportTank;
        playerIsInTank = levelStartPlayerIsInTank;

        runtimePlayerData.CopyFrom(levelStartPlayerData);
        applyRuntimeDataOnNextScene = runtimePlayerData.hasData;

        RestoreLevelStartStateOnPersistentObjects();

        if (debugMode)
        {
            Debug.Log("[GameManager] RESTARTING LEVEL THROUGH LOADING SCENE");
            Debug.Log("[GameManager] Target Scene: " + targetScene);
            Debug.Log("[GameManager] Target Spawn ID: " + targetSpawnID);
            Debug.Log("[GameManager] Transport Tank: " + transportTank);
            Debug.Log("[GameManager] Player In Tank: " + playerIsInTank);
            Debug.Log("[GameManager] Apply Player Data On Next Scene: " + applyRuntimeDataOnNextScene);
        }

        SceneManager.LoadScene(loadingSceneName);
    }

    public void RestartFromSavedLevelPoint()
    {
        RestartLevelFromStart();
    }

    private void RestoreLevelStartStateOnPersistentObjects()
    {
        MonoBehaviour[] allBehaviours = FindObjectsByType<MonoBehaviour>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        foreach (MonoBehaviour behaviour in allBehaviours)
        {
            if (behaviour is ILevelResettable resettable)
                resettable.RestoreLevelStartState();
        }
    }

    public void FinishRestart()
    {
        restartingLevel = false;

        if (debugMode)
            Debug.Log("[GameManager] Restart finished.");
    }
}

