﻿using Ionic.Zip;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace MSCLoader
{
    /// <summary>
    /// This is main Mod Loader class.
    /// </summary>
    public partial class ModLoader : MonoBehaviour
    {
        /// <summary>
        /// A list of all loaded mods.
        /// </summary>
        public static List<Mod> LoadedMods { get; internal set; }

        /// <summary>
        /// The current version of the ModLoader.
        /// </summary>
        public static readonly string MSCLoader_Ver = "1.1.16"; //TODO: replace with assembly version

        /// <summary>
        /// Is this version of ModLoader experimental (this is NOT game experimental branch)
        /// </summary>
#if Debug
        public static readonly bool experimental = true;
#else
        public static readonly bool experimental = false;
#endif

        /// <summary>
        /// Is DevMode active
        /// </summary>
#if DevMode
        public static readonly bool devMode = true;
#else
        public static readonly bool devMode = false;
#endif

        internal static string GetMetadataFolder(string fn) => Path.Combine(MetadataFolder, fn);


        void Awake()
        {
            StopAskingForMscoSupport();
            if (GameObject.Find("Music") != null)
                GameObject.Find("Music").GetComponent<AudioSource>().Stop();
        }

        /// <summary>
        /// Main init
        /// </summary>
        internal static void Init_NP(string cfg)
        {
            switch (cfg)
            {
                case "GF":
                    Init_GF();
                    break;
                case "MD":
                    Init_MD();
                    break;
                case "AD":
                    Init_AD();
                    break;
                default:
                    Init_GF();
                    break;
            }
        }

        internal static void Init_MD()
        {
            if (unloader) return;
            ModsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"MySummerCar\Mods");
            PrepareModLoader();
        }

        internal static void Init_GF()
        {
            if (unloader) return;
            ModsFolder = Path.GetFullPath(Path.Combine("Mods", ""));
            PrepareModLoader();
        }

        internal static void Init_AD()
        {
            if (unloader) return;
            ModsFolder = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"..\LocalLow\Amistech\My Summer Car\Mods"));
            PrepareModLoader();
        }

        private static void PrepareModLoader()
        {
            if (!loaderPrepared)
            {
                loaderPrepared = true;
                GameObject go = new GameObject("MSCLoader", typeof(ModLoader));
                Instance = go.GetComponent<ModLoader>();
                DontDestroyOnLoad(go);
                Instance.Init();
            }
        }
        bool vse = false;
        private void OnLevelWasLoaded(int level)
        {
            switch (Application.loadedLevelName)
            {
                case "MainMenu":
                    CurrentScene = CurrentScene.MainMenu;
                    if (GameObject.Find("Music"))
                        GameObject.Find("Music").GetComponent<AudioSource>().Play();
                    if (QualitySettings.vSyncCount != 0)
                        vse = true;
                    if ((bool)ModMenu.forceMenuVsync.GetValue() && !vse)
                        QualitySettings.vSyncCount = 1; //vsync in menu
                    if (GameObject.Find("MSCLoader Info") == null)
                    {
                        MainMenuInfo();
                    }
                    if (allModsLoaded)
                    {
                        loaderPrepared = false;
                        mscUnloader.MSCLoaderReset();
                        unloader = true;
                        return;
                    }
                    break;
                case "Intro":
                    CurrentScene = CurrentScene.NewGameIntro;

                    if (!IsModsDoneResetting && !IsModsResetting)
                    {
                        IsModsResetting = true;
                        StartCoroutine(NewGameMods());
                    }
                    break;
                case "GAME":
                    CurrentScene = CurrentScene.Game;
                    if ((bool)ModMenu.forceMenuVsync.GetValue() && !vse)
                        QualitySettings.vSyncCount = 0;

                    menuInfoAnim.Play("fade_out");
                    StartLoadingMods(!(bool)ModMenu.syncLoad.GetValue());
                    ModMenu.ModButton_temp();
                    break;
                case "Ending":
                    CurrentScene = CurrentScene.Ending;
                    break;
            }
        }

        private void StartLoadingMods(bool async)
        {
            if (!allModsLoaded && !IsModsLoading)
            {
                IsModsLoading = true;
                if (async)
                    StartCoroutine(LoadModsAsync());
                else
                    StartCoroutine(LoadMods());

            }
        }

        private void Init()
        {
            //Set config and Assets folder in selected mods folder
            ConfigFolder = Path.Combine(ModsFolder, "Config");
            SettingsFolder = Path.Combine(ConfigFolder, "Mod Settings");
            AssetsFolder = Path.Combine(ModsFolder, "Assets");

            //Move from old to new location if updated from before 1.1
            if (!Directory.Exists(SettingsFolder) && Directory.Exists(ConfigFolder))
            {
                Directory.CreateDirectory(SettingsFolder);
                foreach (string dir in Directory.GetDirectories(ConfigFolder))
                {
                    if (new DirectoryInfo(dir).Name != "Mod Settings")
                    {
                        try
                        {
                            Directory.Move(dir, Path.Combine(SettingsFolder, new DirectoryInfo(dir).Name));
                        }
                        catch (Exception ex)
                        {
                            System.Console.WriteLine($"{ex.Message} (Failed to update folder structure)");
                        }
                    }
                }
            }
            MetadataFolder = Path.Combine(ConfigFolder, "Mod Metadata");

            if (GameObject.Find("MSCUnloader") == null)
            {
                GameObject go = new GameObject("MSCUnloader", typeof(MSCUnloader));
                mscUnloader = go.GetComponent<MSCUnloader>();
                DontDestroyOnLoad(go);
            }
            else
            {
                mscUnloader = GameObject.Find("MSCUnloader").GetComponent<MSCUnloader>();
            }
            ModUI.CreateCanvas();
            allModsLoaded = false;
            LoadedMods = new List<Mod>();
            InvalidMods = new List<string>();
            mscUnloader.reset = false;
            if (!Directory.Exists(ModsFolder))
                Directory.CreateDirectory(ModsFolder);
            if (!Directory.Exists("upd_tmp"))
                Directory.CreateDirectory("upd_tmp");

            if (!Directory.Exists(ConfigFolder))
            {
                Directory.CreateDirectory(ConfigFolder);
                Directory.CreateDirectory(SettingsFolder);
                Directory.CreateDirectory(MetadataFolder);
                Directory.CreateDirectory(Path.Combine(MetadataFolder, "Mod Icons"));
            }
            if (!Directory.Exists(MetadataFolder))
            {
                Directory.CreateDirectory(MetadataFolder);
                Directory.CreateDirectory(Path.Combine(MetadataFolder, "Mod Icons"));

            }
            if (!Directory.Exists(AssetsFolder))
                Directory.CreateDirectory(AssetsFolder);

            LoadCoreAssets();
            LoadMod(new ModConsole(), MSCLoader_Ver);
            LoadedMods[0].ModSettings();
            LoadMod(new ModMenu(), MSCLoader_Ver);
            LoadedMods[1].ModSettings();
            ModMenu.LoadSettings();
            if (experimental)
                ModConsole.Print($"<color=green>ModLoader <b>v{MSCLoader_Ver}</b> ready</color> [<color=magenta>Experimental</color> <color=lime>build {expBuild}</color>]");
            else
                ModConsole.Print($"<color=green>ModLoader <b>v{MSCLoader_Ver}</b> ready</color>");
            MainMenuInfo();
            upd_tmp = Directory.GetFiles("upd_tmp", "*.zip");
            if (upd_tmp.Length == 0)
            {
                ContinueInit();
            }
            else
            {
                UnpackUpdates();
            }
        }
        void ContinueInit()
        {
            LoadReferences();
            PreLoadMods();
            ModConsole.Print($"<color=orange>Found <color=green><b>{actualModList.Length}</b></color> mods!</color>");
            try
            {
                if (File.Exists(Path.GetFullPath(Path.Combine("LAUNCHER.exe", ""))) || File.Exists(Path.GetFullPath(Path.Combine("SmartSteamEmu64.dll", ""))) || File.Exists(Path.GetFullPath(Path.Combine("SmartSteamEmu.dll", ""))))
                {
                    ModConsole.Print($"<color=orange>Hello <color=green><b>{"SmartSteamEmu!"}</b></color>!</color>");
                    throw new Exception("[EMULATOR] Do What You Want, Cause A Pirate Is Free... You Are A Pirate!");
                }
                Steamworks.SteamAPI.Init();
                steamID = Steamworks.SteamUser.GetSteamID().ToString();
                ModConsole.Print($"<color=orange>Hello <color=green><b>{Steamworks.SteamFriends.GetPersonaName()}</b></color>!</color>");
                WebClient webClient = new WebClient();
                webClient.Headers.Add("user-agent", $"MSCLoader/{MSCLoader_Ver} ({SystemInfo.operatingSystem})");
                webClient.DownloadStringCompleted += sAuthCheckCompleted;
                webClient.DownloadStringAsync(new Uri($"{serverURL}/sauth.php?sid={steamID}"));
                if (LoadedMods.Count >= 100)
                    Steamworks.SteamFriends.SetRichPresence("status", $"This madman is playing with {actualModList.Length} mods.");
                else if (LoadedMods.Count >= 50)
                    Steamworks.SteamFriends.SetRichPresence("status", $"Playing with {actualModList.Length} mods. Crazy!");
                else
                    Steamworks.SteamFriends.SetRichPresence("status", $"Playing with {actualModList.Length} mods.");

            }
            catch (Exception e)
            {
                steamID = null;
                ModConsole.Error("Steam client doesn't exists.");
                if (devMode)
                    ModConsole.Error(e.ToString());
                System.Console.WriteLine(e);
                if (CheckSteam())
                {
                    System.Console.WriteLine(new AccessViolationException().Message);
                    Environment.Exit(0);
                }
            }
            LoadModsSettings();
            ModMenu.LoadBinds();
            GameObject old_callbacks = new GameObject("BC Callbacks");
            old_callbacks.transform.SetParent(gameObject.transform, false);
            if (OnGUImods.Length > 0) old_callbacks.AddComponent<BC_ModOnGUI>().modLoader = this;
            if (UpdateMods.Length > 0) old_callbacks.AddComponent<BC_ModUpdate>().modLoader = this;
            if (FixedUpdateMods.Length > 0) old_callbacks.AddComponent<BC_ModFixedUpdate>().modLoader = this;
            GameObject mod_callbacks = new GameObject("MSCLoader Callbacks");
            mod_callbacks.transform.SetParent(gameObject.transform, false);
            if (Mod_OnGUI.Length > 0) mod_callbacks.AddComponent<A_ModOnGUI>().modLoader = this;
            if (Mod_Update.Length > 0) mod_callbacks.AddComponent<A_ModUpdate>().modLoader = this;
            if (Mod_FixedUpdate.Length > 0) mod_callbacks.AddComponent<A_ModFixedUpdate>().modLoader = this;
            if (!rtmm)
            {
                if (ModMenu.cfmu_set != 0)
                {
                    string sp = Path.Combine(SettingsFolder, @"MSCLoader_Settings\lastCheck");
                    if (File.Exists(sp))
                    {
                        DateTime lastCheck;
                        string lastCheckS = File.ReadAllText(sp);
                        DateTime.TryParse(lastCheckS, out lastCheck);
                        if ((DateTime.Now - lastCheck).TotalDays >= ModMenu.cfmu_set || (DateTime.Now - lastCheck).TotalDays < 0)
                        {
                            StartCoroutine(CheckForModsUpdates());
                            File.WriteAllText(sp, DateTime.Now.ToString());
                        }
                        else
                        {
                            Mod[] mod = LoadedMods.Where(x => !x.ID.StartsWith("MSCLoader_")).ToArray();
                            for (int i = 0; i < mod.Length; i++)
                            {
                                ModMetadata.ReadMetadata(mod[i]);
                            }
                        }
                    }
                    else
                    {
                        StartCoroutine(CheckForModsUpdates());
                        File.WriteAllText(sp, DateTime.Now.ToString());
                    }
                }
                else
                {
                    StartCoroutine(CheckForModsUpdates());
                }
            }

            if (devMode)
                ModConsole.Warning("You are running ModLoader in <color=red><b>DevMode</b></color>, this mode is <b>only for modders</b> and shouldn't be use in normal gameplay.");
            System.Console.WriteLine(SystemInfo.operatingSystem); //operating system version to output_log.txt
            if (saveErrors != null)
            {
                if (saveErrors.Count > 0 && wasSaving)
                {
                    ModUI.ShowMessage($"Some mod thrown an error during saving{Environment.NewLine}Check console for more information!");
                    for (int i = 0; i < saveErrors.Count; i++)
                    {
                        ModConsole.Error(saveErrors[i]);
                    }
                }
                wasSaving = false;
            }
        }

        [Serializable]
        class SaveOtk
        {
            public string k1;
            public string k2;
        }

        private void sAuthCheckCompleted(object sender, DownloadStringCompletedEventArgs e)
        {
            try
            {
                if (e.Error != null)
                    throw new Exception(e.Error.Message);

                string result = e.Result;

                if (result != string.Empty)
                {
                    string[] ed = result.Split('|');
                    if (ed[0] == "error")
                    {
                        switch (ed[1])
                        {
                            case "0":
                                throw new Exception("Getting steamID failed.");
                            case "1":
                                throw new Exception("steamID rejected.");
                            default:
                                throw new Exception("Unknown error.");
                        }
                    }
                    else if (ed[0] == "ok")
                    {
                        SaveOtk s = new SaveOtk
                        {
                            k1 = ed[1],
                            k2 = ed[2]
                        };
                        System.Runtime.Serialization.Formatters.Binary.BinaryFormatter f = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
                        string sp = Path.Combine(SettingsFolder, @"MSCLoader_Settings\otk.bin");
                        FileStream st = new FileStream(sp, FileMode.Create);
                        f.Serialize(st, s);
                        st.Close();
                    }
                    else
                    {
                        System.Console.WriteLine("Unknown: " + ed[0]);
                        throw new Exception("Unknown server response.");
                    }
                }
                bool ret = Steamworks.SteamApps.GetCurrentBetaName(out string Name, 128);
                if (ret && (bool)ModMenu.expWarning.GetValue())
                {
                    if (Name != "default_32bit") //32bit is NOT experimental branch
                        ModUI.ShowMessage($"<color=orange><b>Warning:</b></color>{Environment.NewLine}You are using beta build: <color=orange><b>{Name}</b></color>{Environment.NewLine}{Environment.NewLine}Remember that some mods may not work correctly on beta branches.", "Experimental build warning");
                }
                System.Console.WriteLine($"MSC buildID: <b>{Steamworks.SteamApps.GetAppBuildId()}</b>");
                if (Steamworks.SteamApps.GetAppBuildId() == 1)
                    throw new DivideByZeroException();
            }
            catch (Exception ex)
            {
                string sp = Path.Combine(SettingsFolder, @"MSCLoader_Settings\otk.bin");
                if (e.Error != null)
                {
                    if (File.Exists(sp))
                    {
                        System.Runtime.Serialization.Formatters.Binary.BinaryFormatter f = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
                        FileStream st = new FileStream(sp, FileMode.Open);
                        SaveOtk s = f.Deserialize(st) as SaveOtk;
                        st.Close();
                        string otk = "otk_" + SidChecksumCalculator($"{steamID}{s.k1}");
                        if (s.k2.CompareTo(otk) != 0)
                        {
                            File.Delete(sp);
                            steamID = null;
                            ModConsole.Error("SteamAPI failed with error: " + ex.Message);
                            if (CheckSteam())
                            {
                                System.Console.WriteLine(new AccessViolationException().Message);
                                Environment.Exit(0);
                            }
                        }
                        else
                        {
                            System.Console.WriteLine("offline");
                        }
                    }
                    else
                    {
                        steamID = null;
                        ModConsole.Error("SteamAPI failed with error: " + ex.Message);
                        if (CheckSteam())
                        {
                            System.Console.WriteLine(new AccessViolationException().Message);
                            Environment.Exit(0);
                        }
                    }
                }
                else
                {
                    if (File.Exists(sp))
                        File.Delete(sp);
                    steamID = null;
                    ModConsole.Error("SteamAPI failed with error: " + ex.Message);
                    if (devMode)
                        ModConsole.Error(ex.ToString());
                    if (CheckSteam())
                    {
                        System.Console.WriteLine(new AccessViolationException().Message);
                        Environment.Exit(0);
                    }
                }
                System.Console.WriteLine(ex);
            }
        }

        private void LoadReferences()
        {
            //TODO: Read references metadata and add to list
            if (Directory.Exists(Path.Combine(ModsFolder, "References")))
            {
                string[] files = Directory.GetFiles(Path.Combine(ModsFolder, "References"), "*.dll");
                for (int i = 0; i < files.Length; i++)
                {
                    Assembly.LoadFrom(files[i]);
                }
            }
            else
            {
                Directory.CreateDirectory(Path.Combine(ModsFolder, "References"));
            }
        }
        GameObject loading2;
        private void LoadCoreAssets()
        {
            ModConsole.Print("Loading core assets...");
            AssetBundle ab = LoadAssets.LoadBundle("MSCLoader.CoreAssets.core.unity3d");
            guiskin = ab.LoadAsset<GUISkin>("MSCLoader.guiskin");
            ModUI.messageBox = ab.LoadAsset<GameObject>("MSCLoader MB.prefab");
            ModUI.messageBoxBtn = ab.LoadAsset<GameObject>("MB_Button.prefab");
            mainMenuInfo = ab.LoadAsset<GameObject>("MSCLoader Info.prefab");
            GameObject loadingP = ab.LoadAsset<GameObject>("LoadingMods.prefab");
            GameObject loadingMetaP = ab.LoadAsset<GameObject>("MSCLoader pbar.prefab");
            #region Loading BC
            GameObject loadingP_old = ab.LoadAsset<GameObject>("LoadingMods_old.prefab"); //BC for MOP
            loading2 = GameObject.Instantiate(loadingP_old);
            loading2.SetActive(false);
            loading2.name = "MSCLoader loading screen";
            loading2.transform.SetParent(ModUI.GetCanvas().transform, false);
            loading2.transform.GetChild(2).GetComponent<Text>().text = $"MSCLOADER <color=green>{MSCLoader_Ver}</color> (BC Mode)";
            #endregion
            loading = GameObject.Instantiate(loadingP);
            loading.SetActive(false);
            loading.name = "MSCLoader loading dialog";
            loading.transform.SetParent(ModUI.GetCanvas().transform, false);
            loading.transform.GetChild(0).GetComponent<Text>().text = $"MSCLoader <color=green>{MSCLoader_Ver}</color>";
            loadingTitle = loading.transform.GetChild(1).GetChild(0).GetChild(0).GetChild(0).GetComponent<Text>();
            loadingMod = loading.transform.GetChild(1).GetChild(0).GetChild(1).GetChild(0).GetComponent<Text>();
            loadingProgress = loading.transform.GetChild(1).GetChild(0).GetChild(2).GetComponent<Slider>();
            loadingMeta = GameObject.Instantiate(loadingMetaP);
            loadingMeta.SetActive(false);
            loadingMeta.name = "MSCLoader update dialog";
            loadingMeta.transform.SetParent(ModUI.GetCanvas().transform, false);
            updateTitle = loadingMeta.transform.GetChild(0).GetChild(0).GetChild(0).GetComponent<Text>();
            updateStatus = loadingMeta.transform.GetChild(0).GetChild(1).GetChild(0).GetComponent<Text>();
            updateProgress = loadingMeta.transform.GetChild(0).GetChild(2).GetComponent<Slider>();
            GameObject.Destroy(loadingP);
            GameObject.Destroy(loadingMetaP);
         //   GameObject.Destroy(loadingP_old);
            ModConsole.Print("Loading core assets completed!");
            ab.Unload(false);
        }

        /// <summary>
        /// Toggle main menu path via settings
        /// </summary>
        internal static void MainMenuPath()
        {
            Instance.mainMenuInfo.transform.GetChild(1).gameObject.SetActive((bool)ModMenu.modPath.GetValue());
        }
        Text modUpdates;
        private void MainMenuInfo()
        {
            Text info, mf;
            mainMenuInfo = Instantiate(mainMenuInfo);
            mainMenuInfo.name = "MSCLoader Info";
            menuInfoAnim = mainMenuInfo.GetComponent<Animation>();
            menuInfoAnim.Play("fade_in");
            info = mainMenuInfo.transform.GetChild(0).gameObject.GetComponent<Text>();
            mf = mainMenuInfo.transform.GetChild(1).gameObject.GetComponent<Text>();
            modUpdates = mainMenuInfo.transform.GetChild(2).gameObject.GetComponent<Text>();
            info.text = $"Mod Loader MSCLoader <color=cyan>v{MSCLoader_Ver}</color> is ready! (<color=orange>Checking for updates...</color>)";
            WebClient client = new WebClient();
            client.Headers.Add("user-agent", $"MSCLoader/{MSCLoader_Ver} ({SystemInfo.operatingSystem})");

            //client.Proxy = new WebProxy("127.0.0.1:8888"); //ONLY FOR TESTING
            client.DownloadStringCompleted += VersionCheckCompleted;
            string branch = "unknown";
            if (experimental)
                branch = "exp_build";
            else
                branch = "stable";
            client.DownloadStringAsync(new Uri($"{serverURL}/ver.php?core={branch}"));

            mf.text = $"<color=orange>Mods folder:</color> {ModsFolder}";
            MainMenuPath();
            modUpdates.text = string.Empty;
            mainMenuInfo.transform.SetParent(ModUI.GetCanvas().transform, false);
        }

        private void VersionCheckCompleted(object sender, DownloadStringCompletedEventArgs e)
        {
            Text info = mainMenuInfo.transform.GetChild(0).gameObject.GetComponent<Text>();
            try
            {
                if (e.Error != null)
                    throw new Exception(e.Error.Message);

                string[] result = e.Result.Split('|');
                if (result[0] == "error")
                {
                    switch (result[1])
                    {
                        case "0":
                            throw new Exception("Unknown branch");
                        case "1":
                            throw new Exception("Database connection error");
                        default:
                            throw new Exception("Unknown error");
                    }

                }
                else if (result[0] == "ok")
                {
                    if (result[1].Trim().Length > 8)
                        throw new Exception("Parse Error, please report that problem!");
                    int i;
                    if (experimental)
                        i = expBuild.CompareTo(result[1].Trim());
                    else
                        i = MSCLoader_Ver.CompareTo(result[1].Trim());
                    if (i != 0)
                        if (experimental)
                            info.text = $"MSCLoader <color=cyan>v{MSCLoader_Ver}</color> is ready! [<color=magenta>Experimental</color> <color=lime>build {expBuild}</color>] (<color=orange>New build available: <b>{result[1]}</b></color>)";
                        else
                            info.text = $"MSCLoader <color=cyan>v{MSCLoader_Ver}</color> is ready! (<color=orange>New version available: <b>v{result[1].Trim()}</b></color>)";
                    else if (i == 0)
                        if (experimental)
                            info.text = $"MSCLoader <color=cyan>v{MSCLoader_Ver}</color> is ready! [<color=magenta>Experimental</color> <color=lime>build {expBuild}</color>]";
                        else
                            info.text = $"MSCLoader <color=cyan>v{MSCLoader_Ver}</color> is ready! (<color=lime>Up to date</color>)";
                }
                else
                {
                    System.Console.WriteLine("Unknown: " + result[0]);
                    throw new Exception("Unknown server response.");
                }
            }
            catch (Exception ex)
            {
                ModConsole.Error($"Check for new version failed with error: {ex.Message}");
                if (devMode)
                    ModConsole.Error(ex.ToString());
                System.Console.WriteLine(ex);
                if (experimental)
                    info.text = $"MSCLoader <color=cyan>v{MSCLoader_Ver}</color> is ready! [<color=magenta>Experimental</color> <color=lime>build {expBuild}</color>]";
                else
                    info.text = $"MSCLoader <color=cyan>v{MSCLoader_Ver}</color> is ready!";

            }
            if (devMode)
                info.text += " [<color=red><b>Dev Mode!</b></color>]";
        }

        internal static void ModException(Exception e, Mod mod)
        {
            string errorDetails = $"{Environment.NewLine}<b>Details: </b>{e.Message} in <b>{new StackTrace(e, true).GetFrame(0).GetMethod()}</b>";
            ModConsole.Error($"Mod <b>{mod.ID}</b> throw an error!{errorDetails}");
            if (devMode)
                ModConsole.Error(e.ToString());
            System.Console.WriteLine(e);
        }
        Text loadingTitle;
        Text loadingMod;
        Slider loadingProgress;
        IEnumerator NewGameMods()
        {
            ModConsole.Print("<color=aqua>==== Resetting mods ====</color>");
            loading.transform.SetAsLastSibling(); //Always on top
            loading.SetActive(true);
            loadingTitle.text = "Resetting mods".ToUpper();
            loadingProgress.maxValue = BC_ModList.Length + Mod_OnNewGame.Length;
            for (int i = 0; i < Mod_OnNewGame.Length; i++)
            {
                loadingProgress.value++;
                loadingMod.text = Mod_OnNewGame[i].Name;
                yield return null;
                try
                {
                    Mod_OnNewGame[i].A_OnNewGame.Invoke();
                }
                catch (Exception e)
                {
                    ModException(e, Mod_OnNewGame[i]);
                }

            }
            for (int i = 0; i < BC_ModList.Length; i++)
            {
                loadingProgress.value++;
                loadingMod.text = BC_ModList[i].Name;
                yield return null;
                try
                {
                    BC_ModList[i].OnNewGame();
                }
                catch (Exception e)
                {
                    ModException(e, BC_ModList[i]);
                }

            }
            loadingMod.text = "Resetting Done! You can skip intro now!";
            yield return new WaitForSeconds(1f);
            loading.SetActive(false);
            IsModsDoneResetting = true;
            ModConsole.Print("<color=aqua>==== Resetting mods finished ====</color>");
            IsModsResetting = false;
        }

        IEnumerator LoadMods()
        {
            ModConsole.Print("<color=aqua>==== Loading mods (Phase 1) ====</color><color=#505050ff>");
            loadingTitle.text = "Loading mods - Phase 1".ToUpper();
            loadingMod.text = "Loading mods. Please wait...";
            loadingProgress.maxValue = 100;
            loading.transform.SetAsLastSibling(); //Always on top
            loading.SetActive(true);
            yield return null;
            for (int i = 0; i < Mod_PreLoad.Length; i++)
            {
                if (Mod_PreLoad[i].isDisabled) continue;
                try
                {
                    Mod_PreLoad[i].A_PreLoad.Invoke();
                }
                catch (Exception e)
                {
                    ModException(e, Mod_PreLoad[i]);
                }
            }
            for (int i = 0; i < PLoadMods.Length; i++)
            {
                if (PLoadMods[i].isDisabled) continue;
                try
                {
                    PLoadMods[i].PreLoad();
                }
                catch (Exception e)
                {
                    ModException(e, PLoadMods[i]);
                }
            }
            loadingProgress.value = 33;
            loadingTitle.text = "Waiting...".ToUpper();
            loadingMod.text = "Waiting for game to finish load...";
            while (GameObject.Find("PLAYER/Pivot/AnimPivot/Camera/FPSCamera") == null)
                yield return null;
            loading2.SetActive(true);
            loadingTitle.text = "Loading mods - Phase 2".ToUpper();
            loadingMod.text = "Loading mods. Please wait...";
            ModConsole.Print("</color><color=aqua>==== Loading mods (Phase 2) ====</color><color=#505050ff>");
            yield return null;
            for (int i = 0; i < Mod_OnLoad.Length; i++)
            {
                if (Mod_OnLoad[i].isDisabled) continue;
                try
                {
                    Mod_OnLoad[i].A_OnLoad.Invoke();
                }
                catch (Exception e)
                {
                    ModException(e, Mod_OnLoad[i]);
                }
            }
            for (int i = 0; i < BC_ModList.Length; i++)
            {
                if (BC_ModList[i].isDisabled) continue;
                try
                {
                    BC_ModList[i].OnLoad();
                }
                catch (Exception e)
                {
                    ModException(e, BC_ModList[i]);
                }
            }
            loadingProgress.value = 66;
            ModMenu.LoadBinds();
            loadingTitle.text = "Loading mods - Phase 3".ToUpper();
            loadingMod.text = "Loading mods. Please wait...";
            ModConsole.Print("</color><color=aqua>==== Loading mods (Phase 3) ====</color><color=#505050ff>");
            yield return null;
            for (int i = 0; i < Mod_PostLoad.Length; i++)
            {
                if (Mod_PostLoad[i].isDisabled) continue;
                try
                {
                    Mod_PostLoad[i].A_PostLoad.Invoke();
                }
                catch (Exception e)
                {
                    ModException(e, Mod_PostLoad[i]);
                }
            }
            for (int i = 0; i < SecondPassMods.Length; i++)
            {
                if (SecondPassMods[i].isDisabled) continue;
                try
                {
                    SecondPassMods[i].SecondPassOnLoad();
                }
                catch (Exception e)
                {
                    ModException(e, SecondPassMods[i]);
                }
            }
            
            loadingProgress.value = 100;
            loadingMod.text = "Finishing touches...";
            yield return null;
            GameObject.Find("ITEMS").FsmInject("Save game", SaveMods);
            ModConsole.Print("</color>");
            allModsLoaded = true;
            loading.SetActive(false);
            loading2.SetActive(false);
        }

        IEnumerator LoadModsAsync()
        {
            ModConsole.Print("<color=aqua>==== Loading mods (Phase 1) ====</color><color=#505050ff>");
            loadingTitle.text = "Loading mods - Phase 1".ToUpper();
            loadingMod.text = "Loading mods. Please wait...";
            loadingProgress.maxValue = PLoadMods.Length + BC_ModList.Length + SecondPassMods.Length;
            loadingProgress.maxValue += Mod_PreLoad.Length + Mod_OnLoad.Length + Mod_PostLoad.Length;

            loading.transform.SetAsLastSibling(); //Always on top
            loading.SetActive(true);
            yield return null;
            for (int i = 0; i < Mod_PreLoad.Length; i++)
            {
                loadingProgress.value++;
                loadingMod.text = Mod_PreLoad[i].ID;
                if (Mod_PreLoad[i].isDisabled) continue;
                try
                {
                    Mod_PreLoad[i].A_PreLoad.Invoke();
                }
                catch (Exception e)
                {
                    ModException(e, Mod_PreLoad[i]);
                }
                yield return null;
            }
            for (int i = 0; i < PLoadMods.Length; i++)
            {
                loadingProgress.value++;
                loadingMod.text = PLoadMods[i].ID;
                if (PLoadMods[i].isDisabled) continue;
                try
                {
                    PLoadMods[i].PreLoad();
                }
                catch (Exception e)
                {
                    ModException(e, PLoadMods[i]);
                }
                yield return null;
            }
            loadingTitle.text = "Waiting...".ToUpper();
            loadingMod.text = "Waiting for game to finish load...";
            while (GameObject.Find("PLAYER/Pivot/AnimPivot/Camera/FPSCamera") == null) 
                yield return null;

            loading2.SetActive(true);
            loadingTitle.text = "Loading mods - Phase 2".ToUpper();
            loadingMod.text = "Loading mods. Please wait...";
            ModConsole.Print("</color><color=aqua>==== Loading mods (Phase 2) ====</color><color=#505050ff>");
            yield return null;
            for (int i = 0; i < Mod_OnLoad.Length; i++)
            {
                loadingProgress.value++;
                loadingMod.text = Mod_OnLoad[i].ID;
                if (Mod_OnLoad[i].isDisabled) continue;
                try
                {
                    Mod_OnLoad[i].A_OnLoad.Invoke();
                }
                catch (Exception e)
                {
                    ModException(e, Mod_OnLoad[i]);
                }
                yield return null;
            }
            for (int i = 0; i < BC_ModList.Length; i++)
            {
                loadingProgress.value++;
                loadingMod.text = BC_ModList[i].ID;
                if (BC_ModList[i].isDisabled) continue;
                try
                {
                    BC_ModList[i].OnLoad();                  
                }
                catch (Exception e)
                {
                    ModException(e, BC_ModList[i]);
                }
                yield return null;
            }
            ModMenu.LoadBinds();
            loadingTitle.text = "Loading mods - Phase 3".ToUpper();
            loadingMod.text = "Loading mods. Please wait...";
            ModConsole.Print("</color><color=aqua>==== Loading mods (Phase 3) ====</color><color=#505050ff>");
            yield return null;
            for (int i = 0; i < Mod_PostLoad.Length; i++)
            {
                loadingProgress.value++;
                loadingMod.text = Mod_PostLoad[i].ID;
                if (Mod_PostLoad[i].isDisabled) continue;
                try
                {
                    Mod_PostLoad[i].A_PostLoad.Invoke();
                }
                catch (Exception e)
                {
                    ModException(e, Mod_PostLoad[i]);
                }
            }
            for (int i = 0; i < SecondPassMods.Length; i++)
            {
                loadingProgress.value++;
                loadingMod.text = SecondPassMods[i].ID;
                if (SecondPassMods[i].isDisabled) continue;
                try
                {
                    SecondPassMods[i].SecondPassOnLoad();
                }
                catch (Exception e)
                {
                    ModException(e, SecondPassMods[i]);
                }
                yield return null;

            }            
            loadingProgress.value = loadingProgress.maxValue;
            loadingMod.text = "Finishing touches...";
            yield return null;
            GameObject.Find("ITEMS").FsmInject("Save game", SaveMods);
            ModConsole.Print("</color>");
            allModsLoaded = true;
            loading.SetActive(false);
            loading2.SetActive(false);
        }

        private static bool wasSaving = false;
        private void SaveMods()
        {
            saveErrors = new List<string>();
            wasSaving = true;
            for (int i = 0; i < Mod_OnSave.Length; i++)
            {
                if (Mod_OnSave[i].isDisabled) continue;
                try
                {
                    Mod_OnSave[i].A_OnSave.Invoke();
                }
                catch (Exception e)
                {
                    ModException(e, Mod_OnSave[i]);
                }
            }
            for (int i = 0; i < OnSaveMods.Length; i++)
            {
                if (OnSaveMods[i].isDisabled) continue;
                try
                {
                    OnSaveMods[i].OnSave();
                }
                catch (Exception e)
                {
                    ModException(e, OnSaveMods[i]);
                }
            }
        }

        internal static bool CheckEmptyMethod(Mod mod, string methodName)
        {
            //TO TRASH
            MethodInfo method = mod.GetType().GetMethod(methodName);
            return (method.IsVirtual && method.DeclaringType == mod.GetType() && method.GetMethodBody().GetILAsByteArray().Length > 2);
        }
        private void PreLoadMods()
        {
            // Load .dll files
            string[] files = Directory.GetFiles(ModsFolder);
            for (int i = 0; i < files.Length; i++)
            {
                if (files[i].EndsWith(".dll"))
                {
                    LoadDLL(files[i]);
                }
            }
            actualModList = LoadedMods.Where(x => !x.ID.StartsWith("MSCLoader_")).ToArray();
            BC_ModList = actualModList.Where(x => !x.newFormat).ToArray();

            PLoadMods = BC_ModList.Where(x => CheckEmptyMethod(x, "PreLoad")).ToArray();
            SecondPassMods = BC_ModList.Where(x => x.SecondPass || CheckEmptyMethod(x, "PostLoad")).ToArray();
            OnGUImods = BC_ModList.Where(x => CheckEmptyMethod(x, "OnGUI") || CheckEmptyMethod(x, "MenuOnGUI")).ToArray();
            UpdateMods = BC_ModList.Where(x => CheckEmptyMethod(x, "Update") || CheckEmptyMethod(x, "MenuUpdate")).ToArray();
            FixedUpdateMods = BC_ModList.Where(x => CheckEmptyMethod(x, "FixedUpdate") || CheckEmptyMethod(x, "MenuFixedUpdate")).ToArray();
            OnSaveMods = BC_ModList.Where(x => CheckEmptyMethod(x, "OnSave")).ToArray();

            Mod_OnNewGame = actualModList.Where(x => x.newFormat && x.A_OnNewGame != null).ToArray();
            Mod_PreLoad = actualModList.Where(x => x.newFormat && x.A_PreLoad != null).ToArray();
            Mod_OnLoad = actualModList.Where(x => x.newFormat && x.A_OnLoad != null).ToArray();
            Mod_PostLoad = actualModList.Where(x => x.newFormat && x.A_PostLoad != null).ToArray();
            Mod_OnSave = actualModList.Where(x => x.newFormat && x.A_OnSave != null).ToArray();
            Mod_OnGUI = actualModList.Where(x => x.newFormat && x.A_OnGUI != null).ToArray();
            Mod_Update = LoadedMods.Where(x => x.newFormat && x.A_Update != null).ToArray();
            Mod_FixedUpdate = actualModList.Where(x => x.newFormat && x.A_FixedUpdate != null).ToArray();

            //cleanup files if not in dev mode
            if (!devMode)
            {

                string cleanupLast = Path.Combine(SettingsFolder, @"MSCLoader_Settings\lastCleanupCheck");
                if (File.Exists(cleanupLast))
                {
                    string lastCheckS = File.ReadAllText(cleanupLast);
                    DateTime.TryParse(lastCheckS, out DateTime lastCheck);
                    if ((DateTime.Now - lastCheck).TotalDays >= 14 || (DateTime.Now - lastCheck).TotalDays < 0)
                    {
                        bool found = false;
                        List<string> cleanupList = new List<string>();
                        foreach (string dir in Directory.GetDirectories(AssetsFolder))
                        {
                            if (!LoadedMods.Exists(x => x.ID == new DirectoryInfo(dir).Name))
                            {
                                found = true;
                                cleanupList.Add(new DirectoryInfo(dir).Name);
                            }
                        }
                        if (found)
                            ModUI.ShowYesNoMessage($"There are unused mod files/assets that can be cleaned up.{Environment.NewLine}{Environment.NewLine}List of unused mod files:{Environment.NewLine}<color=aqua>{string.Join(", ", cleanupList.ToArray())}</color>{Environment.NewLine}Do you want to clean them up?", "Unused files found", CleanupFolders);
                        File.WriteAllText(cleanupLast, DateTime.Now.ToString());
                    }
                }
                else
                {
                    File.WriteAllText(cleanupLast, DateTime.Now.ToString());
                }

            }

        }
        void CleanupFolders()
        {
            string[] setFold = Directory.GetDirectories(SettingsFolder);
            for (int i = 0; i < setFold.Length; i++)
            {
                if (!LoadedMods.Exists(x => x.ID == new DirectoryInfo(setFold[i]).Name))
                {
                    try
                    {
                        Directory.Delete(setFold[i], true);
                    }
                    catch (Exception ex)
                    {
                        ModConsole.Error($"{ex.Message} (corrupted file?)");
                    }
                }
            }
            string[] assFold = Directory.GetDirectories(AssetsFolder);
            for (int i = 0; i < assFold.Length; i++)
            {
                if (!LoadedMods.Exists(x => x.ID == new DirectoryInfo(assFold[i]).Name))
                {
                    try
                    {
                        Directory.Delete(assFold[i], true);
                    }
                    catch (Exception ex)
                    {
                        ModConsole.Error($"{ex.Message} (corrupted file?)");
                    }
                }
            }
        }
        private void LoadModsSettings()
        {
            for (int i = 0; i < LoadedMods.Count; i++)
            {
                if (LoadedMods[i].ID.StartsWith("MSCLoader_"))
                    continue;
                try
                {
                    LoadedMods[i].ModSettings();
                }
                catch (Exception e)
                {
                    if (LoadedMods[i].proSettings) System.Console.WriteLine(e);
                    else
                    {
                        ModConsole.Error($"Settings error for mod <b>{LoadedMods[i].ID}</b>{Environment.NewLine}<b>Details:</b> {e.Message}");
                        if (devMode)
                            ModConsole.Error(e.ToString());
                        System.Console.WriteLine(e);
                    }
                }
            }
            ModMenu.LoadSettings();
        }

        private void LoadDLL(string file)
        {
            try
            {
                Assembly asm = Assembly.LoadFrom(file);
                bool isMod = false;

                AssemblyName[] list = asm.GetReferencedAssemblies();
                if (File.ReadAllText(file).Contains("RegistryKey"))
                    throw new FileLoadException();

                //Warn about wrong .net target, source of some mod crashes.
                if (!asm.ImageRuntimeVersion.Equals(Assembly.GetExecutingAssembly().ImageRuntimeVersion))
                    ModConsole.Warning($"File <b>{Path.GetFileName(file)}</b> is targeting runtime version <b>{asm.ImageRuntimeVersion}</b> which is different that current running version <b>{Assembly.GetExecutingAssembly().ImageRuntimeVersion}</b>. This may cause unexpected behaviours, check your target assembly.");

                // Look through all public classes                
                Type[] asmTypes = asm.GetTypes();
                for (int j = 0; j < asmTypes.Length; j++)
                {
                    string msVer = null;
                    if (typeof(Mod).IsAssignableFrom(asmTypes[j]))
                    {
                        for (int i = 0; i < list.Length; i++)
                        {
                            if (list[i].Name == "MSCLoader")
                            {
                                string[] verparse = list[i].Version.ToString().Split('.');
                                if (list[i].Version.ToString() == "1.0.0.0")
                                    msVer = "0.1";
                                else
                                {
                                    if (verparse[2] == "0")
                                        msVer = $"{verparse[0]}.{verparse[1]}";
                                    else
                                        msVer = $"{verparse[0]}.{verparse[1]}.{verparse[2]}";
                                }
                            }

                        }
                        isMod = true;
                        LoadMod((Mod)Activator.CreateInstance(asmTypes[j]), msVer, file);
                        break;
                    }
                    else
                    {
                        isMod = false;
                    }
                }
                if (!isMod)
                {
                    ModConsole.Error($"<b>{Path.GetFileName(file)}</b> - doesn't look like a mod or missing Mod subclass!{Environment.NewLine}<b>Details:</b> File loaded correctly, but failed to find Mod methods.{Environment.NewLine}If this is a reference put this file into \"<b>References</b>\" folder.");
                    InvalidMods.Add(Path.GetFileName(file));
                }
            }
            catch (Exception e)
            {
                ModConsole.Error($"<b>{Path.GetFileName(file)}</b> - doesn't look like a mod, remove this file from mods folder!{Environment.NewLine}<b>Details:</b> {e.GetFullMessage()}{Environment.NewLine}");
                
                if (devMode)
                    ModConsole.Error(e.ToString());
                System.Console.WriteLine(e);
                InvalidMods.Add(Path.GetFileName(file));
            }

        }

        private void LoadMod(Mod mod, string msver, string fname = null)
        {
            // Check if mod already exists
            if (!LoadedMods.Contains(mod))
            {
                // Create config folder
                if (!Directory.Exists(Path.Combine(SettingsFolder, mod.ID)))
                {
                    Directory.CreateDirectory(Path.Combine(SettingsFolder, mod.ID));
                }
                mod.compiledVersion = msver;
                mod.fileName = fname;
                LoadedMods.Add(mod);
                Console.WriteLine($"Detected As: {mod.Name} (ID: {mod.ID}) v{mod.Version}");
                try
                {
                    mod.ModSetup();
                    if (mod.newFormat && mod.fileName == null)
                    {
                        mod.A_OnMenuLoad?.Invoke();
                    }
                }
                catch (Exception e)
                {
                    ModException(e, mod);
                }
                if (File.Exists(GetMetadataFolder($"{mod.ID}.json")))
                {
                    string serializedData = File.ReadAllText(GetMetadataFolder($"{mod.ID}.json"));
                    mod.metadata = JsonConvert.DeserializeObject<ModsManifest>(serializedData);
                }
            }
            else
            {
                ModConsole.Error($"<color=orange><b>Mod already loaded (or duplicated ID):</b></color><color=red><b>{mod.ID}</b></color>");
            }
        }

        internal void A_OnGUI()
        {
            GUI.skin = guiskin;
            for (int i = 0; i < Mod_OnGUI.Length; i++)
            {
                if (Mod_OnGUI[i].isDisabled)
                    continue;
                try
                {
                    if (allModsLoaded || Mod_OnGUI[i].menuCallbacks)
                        Mod_OnGUI[i].A_OnGUI.Invoke();
                }
                catch (Exception e)
                {
                    ModExceptionHandler(e, Mod_OnGUI[i]);
                }
            }
        }

        internal void BC_OnGUI()
        {
            GUI.skin = guiskin;
            for (int i = 0; i < OnGUImods.Length; i++)
            {
                if (OnGUImods[i].isDisabled)
                    continue;
                try
                {
                    if (allModsLoaded || OnGUImods[i].LoadInMenu)
                        OnGUImods[i].OnGUI();
                }
                catch (Exception e)
                {
                    ModExceptionHandler(e, OnGUImods[i]);
                }
            }
        }
        internal void A_Update()
        {
            for (int i = 0; i < Mod_Update.Length; i++)
            {
                if (Mod_Update[i].isDisabled)
                    continue;
                try
                {
                    if (allModsLoaded || Mod_Update[i].menuCallbacks)
                        Mod_Update[i].A_Update.Invoke();
                }
                catch (Exception e)
                {
                    ModExceptionHandler(e, Mod_Update[i]);
                }
            }
        }

        internal void BC_Update()
        {
            for (int i = 0; i < UpdateMods.Length; i++)
            {
                if (UpdateMods[i].isDisabled)
                    continue;
                try
                {
                    if (allModsLoaded || UpdateMods[i].LoadInMenu)
                        UpdateMods[i].Update();
                }
                catch (Exception e)
                {
                    ModExceptionHandler(e, UpdateMods[i]);
                }
            }
        }
        internal void A_FixedUpdate()
        {
            for (int i = 0; i < Mod_FixedUpdate.Length; i++)
            {
                if (Mod_FixedUpdate[i].isDisabled)
                    continue;
                try
                {
                    if (allModsLoaded || Mod_FixedUpdate[i].LoadInMenu)
                        Mod_FixedUpdate[i].A_FixedUpdate.Invoke();
                }
                catch (Exception e)
                {
                    ModExceptionHandler(e, Mod_FixedUpdate[i]);
                }
            }
        }
        internal void BC_FixedUpdate()
        {
            for (int i = 0; i < FixedUpdateMods.Length; i++)
            {
                if (FixedUpdateMods[i].isDisabled)
                    continue;
                try
                {
                    if (allModsLoaded || FixedUpdateMods[i].LoadInMenu)
                        FixedUpdateMods[i].FixedUpdate();
                }
                catch (Exception e)
                {
                    ModExceptionHandler(e, FixedUpdateMods[i]);
                }
            }
        }

        void ModExceptionHandler(Exception e, Mod mod)
        {
            if (LogAllErrors)
            {
                ModException(e, mod);
            }
            if (allModsLoaded && fullyLoaded)
                mod.modErrors++;
            if (devMode)
            {
                if (mod.modErrors == 30)
                {
                    ModConsole.Error($"Mod <b>{mod.ID}</b> spams <b>too many errors each frame</b>! Last error: ");
                    ModConsole.Error(e.ToString());
                    if ((bool)ModMenu.dm_disabler.GetValue())
                    {
                        mod.isDisabled = true;
                        ModConsole.Warning($"[DevMode] Mod <b>{mod.ID}</b> has been disabled!");
                    }
                    else
                    {
                        ModConsole.Warning($"[DevMode] Mod <b>{mod.ID}</b> is still running!");
                    }
                }
            }
            else
            {
                if (mod.modErrors >= 30)
                {
                    mod.isDisabled = true;
                    ModConsole.Error($"Mod <b>{mod.ID}</b> has been <b>disabled!</b> Because it spams too many errors each frame!{Environment.NewLine}Report this problem to mod author.{Environment.NewLine}Last error message:");
                    ModConsole.Error(e.GetFullMessage());
                }
            }
        }
        void StopAskingForMscoSupport()
        {
            if (File.Exists(Path.Combine("", @"mysummercar_Data\Managed\MSCOClient.dll")))
            {
                System.Console.WriteLine($"MSCOClient.dll - {new AccessViolationException().Message}");
                File.Delete(Path.Combine("", @"mysummercar_Data\Managed\MSCOClient.dll"));
                Application.Quit();
            }
        }
        internal static string SidChecksumCalculator(string rawData)
        {
            System.Security.Cryptography.SHA1 sha256 = System.Security.Cryptography.SHA1.Create();
            byte[] bytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(rawData));
            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            for (int i = 0; i < bytes.Length; i++)
            {
                builder.Append(bytes[i].ToString("x2"));
            }
            return builder.ToString();
        }
    }

}
