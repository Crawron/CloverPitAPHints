using BepInEx;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using System;
using System.IO;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using BepInEx.Configuration;
using System.Collections.ObjectModel;
using CloverAPI.Content.Charms;

namespace CloverPitAPHints;

[BepInPlugin("github.calebri.cloverpitaphints", "CloverPitAPHints", "1.1.0")]
[BepInDependency("ModdingAPIs.cloverpit.CloverAPI", BepInDependency.DependencyFlags.HardDependency)]
public class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;

    private GameObject dm = null;
    private TextMeshProUGUI tmpro = null;

    private bool connected = false;
    private float timer = 0;
    private int deadnumOld = 0;

    private string ImagesPath;

    private static PowerupScript.Identifier charm1;

    // Settings Config
    private ConfigEntry<int> hintThresh;
    private ConfigEntry<bool> charmsEnabled;

    // AP Variables
    private ConfigEntry<string> ip;
    private ConfigEntry<int> port;
    private ConfigEntry<string> username;
    private ConfigEntry<string> pass;
    private ArchipelagoSession session;

    private void Awake()
    {
        // Plugin startup logic
        Logger = base.Logger;
        Logger.LogInfo("Plugin CloverPitAPHints is loaded!");

        ImagesPath = Path.Combine(Path.GetDirectoryName(Info.Location), "img");

        SceneManager.sceneLoaded += OnSceneLoaded;

        // Config Initialization
        hintThresh = Config.Bind<int>("Settings", "hintThresh", 3, "The first deadline that will give you a hint upon reaching it. Minimum value is 2.");
        charmsEnabled = Config.Bind<bool>("Settings", "charmsEnabled", true, "Controls if the custom charms should be enabled. Set to false to disable custom charms.");

        ip = Config.Bind<string>("Archipelago", "ip", "localhost", "Server IP");
        port = Config.Bind<int>("Archipelago", "port", 38281, new ConfigDescription("Server port.", new AcceptableValueRange<int>(0, 65535)));
        username = Config.Bind<string>("Archipelago", "name", "Player1", "Slot name.");
        pass = Config.Bind<string>("Archipelago", "password", "", "Server password.");

        session = ArchipelagoSessionFactory.CreateSession(ip.Value, port.Value);
        
        if (charmsEnabled.Value) { RegisterCharms(); }

        // Server Connection
        Logger.LogInfo("Attempting to connect to AP server...");
        APConnect(username.Value, pass.Value);
    }

    private void APConnect(string name, string pass)
    {
        LoginResult result;

        try
        {
            string[] tags = { "HintGame" };
            result = session.TryConnectAndLogin("", name, ItemsHandlingFlags.NoItems, null, tags, null, pass);
        }
        catch (Exception e)
        {
            result = new LoginFailure(e.GetBaseException().Message);
        }

        if (!result.Successful)
        {
            Logger.LogError("Failed to connect to Archipelago server. Make sure config is valid and restart the game.");
        }
        else
        {
            Logger.LogInfo("Successfully connected to Archipelago server.");
            connected = true;
        }
    }

    private void RegisterCharms()
    {
        Logger.LogInfo("Registering charms...");
        charm1 = CharmManager.Builder("CloverPitAPHints", "hintCharm")
            .WithName("1 Hint")
            .WithDescription("Grants 1 hint instantly.")
            .WithIsInstantPowerup(true)
            .WithStartingPrice(PowerupScript.PRICE_NORMAL)
            .WithTextureModel(Path.Combine(ImagesPath, "AP.png"))
            .WithStoreRerollChance(PowerupScript.STORE_REROLL_CHANCE_COMMON)
            .WithOnEquipEvent(_ =>
            {
                SendHint();
            })
            .BuildAndRegister();
        Logger.LogInfo($"Successfully registered charm: {charm1}");
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode lsm) // Recording exsistance / absence of TMP UI elements in scene
    {
        if (scene.name == "03GameScene") // 03GameScene is the room scene
        {
            Logger.LogInfo("Game scene loaded.");
            dm = GameObject.Find("Deadline Monitor");
            if (dm != null) // Record TMPUGUI component for use elsewhere
            {
                Logger.LogInfo("TMProUGUI component found in game scene.");
                tmpro = dm.GetComponentInChildren<TextMeshProUGUI>();
            }
            else // Record absence of dm and tmpro
            {
                tmpro = null;
            }
        }
        else
        {
            dm = null;
            tmpro = null;
        }
    }

    private void Update() // Runs every frame
    {
        timer += Time.deltaTime;

        if (timer >= 1)
        { // 1 sec timer to avoid log spam
            timer = 0;
            if (tmpro != null)
            {
                int deadnum = GetDeadline(); // Parse deadline monitor text

                if (deadnum > deadnumOld && deadnumOld != 0 && deadnum >= hintThresh.Value)
                {
                    Logger.LogInfo($"Deadline increased to {deadnum}.");

                    SendHint();
                }

                deadnumOld = deadnum;
            }
        }
    }

    private int GetDeadline()
    {
        if (tmpro == null)
        {
            return 0;
        }
        else
        {
            return int.Parse(tmpro.text.Split(' ')[1].Substring(1));
        }
    }

    private void SendHint()
    {
        if (!connected)
        {
            Logger.LogWarning("Attempted to send AP hint but connection never began.");
            return;
        }

        Logger.LogInfo("Issuing Archipelago hint to connected slot.");

        long item;

        try
        {
            ReadOnlyCollection<long> missing = session.Locations.AllMissingLocations;
            var random = new System.Random();
            item = missing[random.Next(missing.Count)];
        }
        catch
        {
            Logger.LogError("Failed to select random AP item from locations list; aborting hint.");
            return;
        }

        try
        {
            session.Locations.ScoutLocationsAsync(HintCreationPolicy.CreateAndAnnounce, item);
            Logger.LogInfo($"Sent hint for AP item with ID: {item}");
        }
        catch
        {
            Logger.LogError("Hint failed to send due to the Archipelago websocket being closed.");
        }
    }
}
