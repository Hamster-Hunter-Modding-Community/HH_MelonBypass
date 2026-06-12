using MelonLoader;
using HarmonyLib;
using System;
using System.Reflection;
using System.Runtime.InteropServices;

[assembly: MelonInfo(typeof(HH_Bypass.HH_BypassMod), "HH Bypass", "1.1.0", "CORRUPTED_GAMEZ")]
[assembly: MelonGame("Hamuno", "Hamster Hunter")]

namespace HH_Bypass
{
    public class HH_BypassMod : MelonMod
    {
        public override void OnInitializeMelon()
        {
            LoggerInstance.Msg("HH Bypass v1.1.0 loading...");

            var loadScriptsType         = FindIl2CppType("LoadScripts");
            var apartmentCrashGuardType = FindIl2CppType("ApartmentCrashGuard");
            var saveSlotUIType          = FindIl2CppType("SaveSlotUI");
            var networkStarterType      = FindIl2CppType("NetworkStarter");

            if (loadScriptsType == null) { LoggerInstance.Error("LoadScripts not found!"); return; }

            const BindingFlags ALL = BindingFlags.Public | BindingFlags.NonPublic |
                                     BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

            PatchMethod(loadScriptsType, "Awake",                       ALL, "Block_Awake");
            PatchMethod(loadScriptsType, "DetectCheatIndicators",       ALL, "Block_DetectCheatIndicators");
            PatchMethod(loadScriptsType, "GetSuspiciousPreloadEntries", ALL, "Block_GetSuspiciousPreloadEntries");
            PatchMethod(loadScriptsType, "GetFileHash",                 ALL, "Block_GetFileHash");
            PatchMethod(loadScriptsType, "PingBackendBadActor",         ALL, "Block_PingBackendBadActor");

            if (apartmentCrashGuardType != null)
                PatchMethod(apartmentCrashGuardType, "Awake", ALL, "CrashGuard_Prefix");

            if (saveSlotUIType != null)
                PatchMethod(saveSlotUIType, "RefreshSlotDisplay", ALL, "RefreshSlotDisplay_Postfix", postfix: true);

            if (networkStarterType != null)
                PatchMethod(networkStarterType, "StartHostForOwnSave", ALL, "StartHostForOwnSave_Prefix");

            LoggerInstance.Msg("All patches applied.");
        }

        void PatchMethod(Type type, string method, BindingFlags flags, string patchName, bool postfix = false)
        {
            var target = type.GetMethod(method, flags);
            if (target == null) { LoggerInstance.Warning($"{type.Name}.{method} not found."); return; }
            var pm = typeof(Patches).GetMethod(patchName, BindingFlags.Static | BindingFlags.Public);
            if (postfix) HarmonyInstance.Patch(target, postfix: new HarmonyMethod(pm));
            else         HarmonyInstance.Patch(target, prefix:  new HarmonyMethod(pm));
            LoggerInstance.Msg($"  Patched {type.Name}.{method}");
        }

        public static Type FindIl2CppType(string name)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(name) ?? asm.GetType("Il2Cpp." + name);
                if (t != null) return t;
            }
            return null;
        }

        public static IntPtr GetNativePtr(object obj)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var f = t.GetField("Pointer", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) return (IntPtr)f.GetValue(obj);
                t = t.BaseType;
            }
            return IntPtr.Zero;
        }

        public static int  ReadInt32(IntPtr p, int o) => Marshal.ReadInt32(p + o);
        public static bool ReadBool (IntPtr p, int o) => Marshal.ReadByte(p + o) != 0;
        public static void WriteBool(IntPtr p, int o, bool v) => Marshal.WriteByte(p + o, v ? (byte)1 : (byte)0);
    }

    public static class Patches
    {
        public static bool Block_Awake()
        {
            Melon<HH_BypassMod>.Logger.Msg("[HH Bypass] LoadScripts.Awake blocked.");
            return false;
        }
        public static bool Block_DetectCheatIndicators(ref string __result)
        { __result = string.Empty; return false; }
        public static bool Block_GetSuspiciousPreloadEntries(ref string __result)
        { __result = string.Empty; return false; }
        public static bool Block_GetFileHash(ref string __result)
        { __result = string.Empty; return false; }
        public static bool Block_PingBackendBadActor(ref object __result)
        { __result = null; return false; }

        public static bool CrashGuard_Prefix()
        {
            try
            {
                Type pp = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    pp = asm.GetType("UnityEngine.PlayerPrefs") ?? asm.GetType("Il2CppUnityEngine.PlayerPrefs");
                    if (pp != null) break;
                }
                pp?.GetMethod("DeleteKey", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(string) }, null)?.Invoke(null, new object[] { "DIRTY_FLAG_KEY" });
                pp?.GetMethod("Save", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
            }
            catch { }
            return true;
        }

        public static void RefreshSlotDisplay_Postfix(object __instance)
        {
            try
            {
                IntPtr ptr = HH_BypassMod.GetNativePtr(__instance);
                if (ptr == IntPtr.Zero) return;

                int  slotNumber = HH_BypassMod.ReadInt32(ptr, 0x20);
                bool slotExists = HH_BypassMod.ReadBool (ptr, 0x70);
                if (slotExists) return;

                Type saveSystem = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    saveSystem = asm.GetType("SaveSystem") ?? asm.GetType("Il2Cpp.SaveSystem");
                    if (saveSystem != null) break;
                }
                if (saveSystem == null) return;

                var setActive = saveSystem.GetMethod("SetActiveSlot", BindingFlags.Public | BindingFlags.Static);
                var getPath   = saveSystem.GetProperty("SaveFilePath", BindingFlags.Public | BindingFlags.Static);
                var getActive = saveSystem.GetProperty("ActiveSlot",   BindingFlags.Public | BindingFlags.Static);
                if (setActive == null || getPath == null || getActive == null) return;

                int currentActive = (int)(getActive.GetValue(null) ?? 1);
                int targetSlot    = slotNumber < 1 ? 1 : slotNumber;

                setActive.Invoke(null, new object[] { targetSlot, false });
                string slotPath = getPath.GetValue(null) as string;
                setActive.Invoke(null, new object[] { currentActive < 1 ? 1 : currentActive, false });

                bool fileOnDisk = !string.IsNullOrEmpty(slotPath) && System.IO.File.Exists(slotPath);
                if (fileOnDisk)
                {
                    HH_BypassMod.WriteBool(ptr, 0x70, true);
                    Melon<HH_BypassMod>.Logger.Msg($"[SaveSlot] Forced _slotExists=true for slot {slotNumber}.");
                }
            }
            catch (Exception ex)
            {
                Melon<HH_BypassMod>.Logger.Warning("[SaveSlot] Error: " + ex.Message);
            }
        }

        public static bool StartHostForOwnSave_Prefix()
        {
            try
            {
                Type nmType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    nmType = asm.GetType("Mirror.NetworkManager") ?? asm.GetType("Il2CppMirror.NetworkManager");
                    if (nmType != null) break;
                }
                if (nmType == null) return true;

                var singletonProp = nmType.GetProperty("singleton", BindingFlags.Public | BindingFlags.Static)
                                 ?? nmType.GetProperty("Singleton", BindingFlags.Public | BindingFlags.Static);
                object singleton = singletonProp?.GetValue(null);
                if (singleton == null) return true;

                IntPtr ptr = HH_BypassMod.GetNativePtr(singleton);
                if (ptr == IntPtr.Zero) return true;

                bool isActive = HH_BypassMod.ReadBool(ptr, 0x39);
                if (isActive)
                {
                    Melon<HH_BypassMod>.Logger.Msg("[Network] Clearing stale isNetworkActive flag.");
                    HH_BypassMod.WriteBool(ptr, 0x39, false);
                }
                return true;
            }
            catch (Exception ex)
            {
                Melon<HH_BypassMod>.Logger.Warning("[Network] Prefix error: " + ex.Message);
                return true;
            }
        }
    }
}
