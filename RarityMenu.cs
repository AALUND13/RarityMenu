using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Photon.Pun;
using RarityLib.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using TMPro;
using UnboundLib;
using UnboundLib.Networking;
using UnboundLib.Utils;
using UnboundLib.Utils.UI;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RarntyMenu {
    [BepInDependency("com.willis.rounds.unbound")]
    [BepInDependency("root.rarity.lib", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInPlugin(ModId, ModName, Version)]
    [BepInProcess("Rounds.exe")]
    public class RarityMenu : BaseUnityPlugin {
        private const string ModId = "Rarity.Toggle";
        private const string ModName = "Rarity Toggle";
        public const string Version = "1.0.2";
        bool ready = false;
        int maxRarity = 2;
        static bool first = true;

        internal static List<CardInfo> defaultCards {
            get {
                return ((CardInfo[])typeof(CardManager).GetField("defaultCards", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null)).ToList();
            }
        }
        internal static List<CardInfo> activeCards {
            get {
                return ((ObservableCollection<CardInfo>)typeof(CardManager).GetField("activeCards", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null)).ToList();
            }
        }
        internal static List<CardInfo> inactiveCards {
            get {
                return (List<CardInfo>)typeof(CardManager).GetField("inactiveCards", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            }
            set { }
        }

        internal static List<CardInfo> allCards {
            get {
                var r = CardManager.cards.Values.Select(v => v.cardInfo).ToList();
                r.Sort((c1, c2) => c1.cardName.CompareTo(c2.cardName));
                return r;
            }
            set { }
        }

        public static Dictionary<string, ConfigEntry<string>> CardRaritys = new Dictionary<string, ConfigEntry<string>>();
        public static Dictionary<string, CardInfo.Rarity> CardDefaultRaritys = new Dictionary<string, CardInfo.Rarity>();
        public static Dictionary<string, TextMeshProUGUI> CardRaritysTexts = new Dictionary<string, TextMeshProUGUI>();
        public static Dictionary<string, List<CardInfo>> ModCards = new Dictionary<string, List<CardInfo>>();

        public void Awake() {
            new Harmony(ModId).PatchAll();
        }

        public void Start() {
            Unbound.Instance.ExecuteAfterFrames(60, () => {
                string mod = "Vanilla";
                foreach(CardInfo card in allCards) {
                    mod = CardManager.cards.Values.First(c => c.cardInfo == card).category;

                    // We remove the special characters from the card name to prevent issues with the config.
                    string safeCardName = SanitizeText(card.name);

                    CardRaritys[safeCardName] = Config.Bind(ModId, safeCardName, "DEFAULT", $"Rarity value of card {card.cardName} from {mod}");
                    CardDefaultRaritys[safeCardName] = card.rarity;
                    CardInfo.Rarity cardRarity = RarityUtils.GetRarity(CardRaritys[safeCardName].Value);
                    if(cardRarity == CardInfo.Rarity.Common && CardRaritys[safeCardName].Value != "Common") {
                        cardRarity = CardDefaultRaritys[safeCardName];
                        CardRaritys[safeCardName].Value = "DEFAULT";
                    }
                    card.rarity = cardRarity;
                    
                    if(!ModCards.ContainsKey(mod)) 
                        ModCards.Add(mod, new List<CardInfo>());

                    ModCards[mod].Add(card);
                }
                maxRarity = Enum.GetValues(typeof(CardInfo.Rarity)).Length - 1;
                ready = true;
            });
            Unbound.RegisterMenu(ModName, null, menu => Unbound.Instance.StartCoroutine(SetupGUI(menu)), null, false);
            Unbound.RegisterHandshake(ModId, OnHandShakeCompleted);
            SceneManager.sceneLoaded += (r, d) => first = true;
        }


        private void OnHandShakeCompleted() {
            if(PhotonNetwork.IsMasterClient) {
                NetworkingManager.RPC(typeof(RarityMenu), nameof(SyncSettings), new object[] { CardRaritys.Keys.ToArray(), CardRaritys.Values.Select(c => c.Value).ToArray() });
            }
        }

        [UnboundRPC]
        private static void SyncSettings(string[] cards, string[] rarities) {
            if(first) {
                first = false;
                Dictionary<string, string> cardRarities = new Dictionary<string, string>();
                for(int i = 0; i < cards.Length; i++) {
                    cardRarities[cards[i]] = rarities[i];
                }

                allCards.ForEach(c => c.rarity = cardRarities[c.name] != "DEFAULT" 
                    ? RarityUtils.GetRarity(cardRarities[c.name]) 
                    : CardDefaultRaritys[c.name]);
            }
        }


        private IEnumerator SetupGUI(GameObject menu) {
            yield return new WaitUntil(() => ready);
            yield return new WaitForSecondsRealtime(0.1f);
            NewGUI(menu);
            yield break;
        }

        private void NewGUI(GameObject menu) {
            MenuHandler.CreateText(ModName, menu, out _, 60, false, null, null, null, null);
            foreach(string mod in ModCards.Keys.OrderBy(m => m == "Vanilla" ? m : $"Z{m}")) {
                ModGUI(MenuHandler.CreateMenu(mod, () => { }, menu, 60, true, true, menu.transform.parent.gameObject), mod);
            }
        }

        private void ModGUI(GameObject menu, string mod) {
            MenuHandler.CreateText(mod.ToUpper(), menu, out _, 60, false, null, null, null, null);
            foreach(CardInfo card in ModCards[mod]) {
                string safeCardName = SanitizeText(card.name);

                MenuHandler.CreateText(card.cardName, menu, out _, 30, color: CardChoice.instance.GetCardColor(card.colorTheme));
                Color color = RarityUtils.GetRarityData(CardRaritys[safeCardName].Value != "DEFAULT" ? RarityUtils.GetRarity(CardRaritys[safeCardName].Value) : CardDefaultRaritys[safeCardName]).colorOff;
                
                CardRaritysTexts[safeCardName] = CreateSliderWithoutInput(CardRaritys[safeCardName].Value, menu, 30, -1, maxRarity, CardRaritys[safeCardName].Value == "DEFAULT" ? -1 : (int)RarityUtils.GetRarity(CardRaritys[safeCardName].Value), (value) => {

                    if(value >= 0)
                        CardRaritys[safeCardName].Value = RarityUtils.GetRarityData((CardInfo.Rarity)(int)value).name;
                    else
                        CardRaritys[safeCardName].Value = "DEFAULT";

                    card.rarity = CardRaritys[safeCardName].Value != "DEFAULT" 
                        ? RarityUtils.GetRarity(CardRaritys[safeCardName].Value) 
                        : CardDefaultRaritys[safeCardName];

                    try {
                        Color color = RarityUtils.GetRarityData(CardRaritys[safeCardName].Value != "DEFAULT" 
                            ? RarityUtils.GetRarity(CardRaritys[safeCardName].Value) 
                            : CardDefaultRaritys[safeCardName]).colorOff;
                        
                        CardRaritysTexts[safeCardName].text = value >= 0 
                            ? card.rarity.ToString().ToUpper() 
                            : "DEFAULT";

                        // We change the color of the text to white if the rarity is common
                        // Because the common rarity color is kinda hard to see.
                        CardRaritysTexts[safeCardName].color = GetCardRarityColor(card);
                    } catch { }
                }, out _, true, color: GetCardRarityColor(card)).GetComponentsInChildren<TextMeshProUGUI>()[2];
            }
        }

        private Color GetCardRarityColor(CardInfo card) {
            Color color = RarityUtils.GetRarityData(card.rarity).colorOff;
            float max = Mathf.Max(color.r, color.g, color.b);

            return max < 0.20 ? Color.white : color;
        }

        private static GameObject CreateSliderWithoutInput(string text, GameObject parent, int fontSize, float minValue, float maxValue, float defaultValue,
            UnityAction<float> onValueChangedAction, out Slider slider, bool wholeNumbers = false, Color? sliderColor = null, Slider.Direction direction = Slider.Direction.LeftToRight, bool forceUpper = true, Color? color = null, TMP_FontAsset font = null, Material fontMaterial = null, TextAlignmentOptions? alignmentOptions = null) {
            GameObject sliderObj = MenuHandler.CreateSlider(text, parent, fontSize, minValue, maxValue, defaultValue, onValueChangedAction, out slider, wholeNumbers, sliderColor, direction, forceUpper, color, font, fontMaterial, alignmentOptions);

            UnityEngine.GameObject.Destroy(sliderObj.GetComponentInChildren<TMP_InputField>().gameObject);

            return sliderObj;

        }

        internal static string SanitizeText(string text) {
            return Regex.Replace(text, @"[^0-9a-zA-Z_ ]+", "").Trim();
        }
    }

    [Serializable]
    [HarmonyPatch(typeof(ToggleCardsMenuHandler), nameof(ToggleCardsMenuHandler.UpdateVisualsCardObj))]
    public class Patch {
        public static void Prefix(GameObject cardObject) {
            Unbound.Instance.ExecuteAfterFrames(15, () => {
                if(ToggleCardsMenuHandler.cardMenuCanvas.gameObject.activeSelf) {
                    string name = cardObject.GetComponentInChildren<CardInfo>().name.Substring(0, cardObject.GetComponentInChildren<CardInfo>().name.Length - 7);
                    string safeCardName = RarityMenu.SanitizeText(name);

                    cardObject.GetComponentInChildren<CardInfo>().rarity = RarityMenu.CardRaritys[safeCardName].Value != "DEFAULT" 
                        ? RarityUtils.GetRarity(RarityMenu.CardRaritys[safeCardName].Value) 
                        : RarityMenu.CardDefaultRaritys[safeCardName];
                    
                    cardObject.GetComponentsInChildren<CardRarityColor>().ToList().ForEach(r => r.Toggle(true));
                }
            });
        }
    }
}