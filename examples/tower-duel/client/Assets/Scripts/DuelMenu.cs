using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace TowerDuel
{
    internal sealed class DuelMenu
    {
        private const float DesignHeight = 1000;
        private const float MaxWidth = 676;
        private const string DeckKey = "TowerDuel.Deck.v1";
        private const string DeckGuide = "點選兩張上場牌即可互換順序。";

        private readonly TowerDuelClient client;
        private readonly Action closeKeyboard;
        private readonly VisualElement shell, battlePage, deckPage, deckHand, deckReserve;
        private readonly TextField nameField, serverField, roomField;
        private readonly Button battleTab, cardsTab, quickMatch, createRoom, joinRoom, resetDeck;
        private readonly Label details, status;
        private readonly CardTile[] slots = new CardTile[DuelRules.DeckSize];
        private readonly Dictionary<string, CardTile> catalogue = new Dictionary<string, CardTile>();
        private readonly string[] defaults = new string[DuelRules.DeckSize];
        private string[] deck;
        private int selectedSlot = -1;
        private string cardToAdd;
        private bool busy;

        [Serializable]
        private sealed class SavedDeck
        {
            public string[] cards;
        }

        public DuelMenu(VisualElement lobby, TowerDuelClient client, Action closeKeyboard)
        {
            this.client = client;
            this.closeKeyboard = closeKeyboard;
            shell = lobby.Q("menuShell");
            battlePage = lobby.Q<ScrollView>("battlePage");
            deckPage = lobby.Q<ScrollView>("deckPage");
            deckHand = lobby.Q("deckHand");
            deckReserve = lobby.Q("deckReserve");
            details = lobby.Q<Label>("deckDetails");
            status = lobby.Q<Label>("lobbyStatus");
            nameField = lobby.Q<TextField>("nameField");
            serverField = lobby.Q<TextField>("serverField");
            roomField = lobby.Q<TextField>("roomField");
            battleTab = lobby.Q<Button>("battleTab");
            cardsTab = lobby.Q<Button>("cardsTab");
            quickMatch = lobby.Q<Button>("quickMatchButton");
            createRoom = lobby.Q<Button>("createButton");
            joinRoom = lobby.Q<Button>("joinButton");
            resetDeck = lobby.Q<Button>("defaultDeckButton");
            nameField.value = "玩家" + UnityEngine.Random.Range(10, 99);
            serverField.value = client.Server;
#if UNITY_WEBGL && !UNITY_EDITOR
            serverField.style.display = DisplayStyle.None;
#endif
            for (int i = 0; i < defaults.Length; i++)
                defaults[i] = client.Rules.cards[i].id;
            deck = LoadDeck();
            lobby.Q<Label>("deckCount").text = deck.Length + "/" + DuelRules.DeckSize;
            BuildDeckView(lobby.Q("catalogCards"));
            RefreshDeck();
            details.text = DeckGuide;
            battleTab.clicked += () => { closeKeyboard(); ShowCards(false); };
            cardsTab.clicked += () => { closeKeyboard(); ShowCards(true); };
            resetDeck.clicked += ResetDeck;
            createRoom.clicked += () => Connect("create");
            joinRoom.clicked += () => Connect("join");
            quickMatch.clicked += () =>
            {
                if (client.Session.Searching) client.Leave();
                else Connect("quick");
            };
            ShowCards(false);
        }

        public void Layout(Rect safeArea, float unitsPerPixel)
        {
            float scale = safeArea.height * unitsPerPixel / DesignHeight;
            if (scale <= 0) return;
            float width = Mathf.Min(safeArea.width * unitsPerPixel / scale, MaxWidth);
            shell.style.width = width;
            shell.style.height = DesignHeight;
            shell.style.scale = new Scale(new Vector3(scale, scale, 1));
            shell.style.left = safeArea.center.x * unitsPerPixel - width * scale / 2;
            shell.style.top = (Screen.height - safeArea.yMax) * unitsPerPixel;
        }

        public void BlurInputs()
        {
            foreach (var field in new[] { nameField, serverField, roomField })
            {
                if (field.touchScreenKeyboard != null) field.touchScreenKeyboard.active = false;
                field.Blur();
            }
        }

        public void Update()
        {
            var session = client.Session;
            bool blocked = session.Searching || session.Connecting || session.Recovering;
            if (busy != blocked)
            {
                busy = blocked;
                deckHand.SetEnabled(!busy);
                deckReserve.SetEnabled(!busy);
                resetDeck.SetEnabled(!busy);
                createRoom.SetEnabled(!busy);
                joinRoom.SetEnabled(!busy);
                nameField.SetEnabled(!busy);
                roomField.SetEnabled(!busy);
                serverField.SetEnabled(!busy);
            }
            quickMatch.SetEnabled(!busy || session.Searching);
            string quickText = session.Searching ? "取消配對" : "快速配對";
            if (quickMatch.text != quickText) quickMatch.text = quickText;
            if (status.text != session.Status) status.text = session.Status;
        }

        private void ShowCards(bool show)
        {
            deckPage.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            battlePage.style.display = show ? DisplayStyle.None : DisplayStyle.Flex;
            cardsTab.EnableInClassList("menu-tab-selected", show);
            battleTab.EnableInClassList("menu-tab-selected", !show);
        }

        private void Connect(string mode)
        {
            if (busy) return;
            closeKeyboard();
            client.Connect(serverField.value, mode, mode == "join" ? roomField.value : "", nameField.value, deck);
        }

        private void BuildDeckView(VisualElement catalogueRoot)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                int index = i;
                var tile = new CardTile(() => ChooseSlot(index));
                slots[i] = tile;
                (i < 4 ? deckHand : deckReserve).Add(tile.Button);
            }
            VisualElement row = null;
            for (int i = 0; i < client.Rules.cards.Length; i++)
            {
                if (i % 4 == 0)
                {
                    row = new VisualElement();
                    row.AddToClassList("menu-card-row");
                    catalogueRoot.Add(row);
                }
                CardDefinition card = client.Rules.cards[i];
                var tile = new CardTile(() => ChooseCard(card));
                tile.Show(card);
                catalogue.Add(card.id, tile);
                row.Add(tile.Button);
            }
        }

        private string[] LoadDeck()
        {
            string json = PlayerPrefs.GetString(DeckKey, "");
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var saved = JsonUtility.FromJson<SavedDeck>(json)?.cards;
                    if (saved != null && saved.Length == DuelRules.DeckSize)
                    {
                        var seen = new HashSet<string>();
                        foreach (string id in saved)
                            if (client.Rules.Find(id) == null || !seen.Add(id))
                                return (string[])defaults.Clone();
                        return saved;
                    }
                }
                catch (ArgumentException) { }
            }
            return (string[])defaults.Clone();
        }

        private void ResetDeck()
        {
            deck = (string[])defaults.Clone();
            CompleteChange();
            details.text = DeckGuide;
        }

        private void ChooseSlot(int index)
        {
            if (cardToAdd != null)
            {
                deck[index] = cardToAdd;
                CompleteChange();
            }
            else if (selectedSlot >= 0 && selectedSlot != index)
            {
                SwapSlots(selectedSlot, index);
                CompleteChange();
            }
            else
            {
                selectedSlot = selectedSlot == index ? -1 : index;
                RefreshDeck();
            }
            ShowDetails(client.Rules.Find(deck[index]));
        }

        private void ChooseCard(CardDefinition card)
        {
            if (busy)
            {
                ShowDetails(card);
                return;
            }
            int index = Array.IndexOf(deck, card.id);
            if (selectedSlot >= 0)
            {
                if (index >= 0) SwapSlots(selectedSlot, index);
                else deck[selectedSlot] = card.id;
                CompleteChange();
            }
            else
            {
                cardToAdd = index < 0 && cardToAdd != card.id ? card.id : null;
                RefreshDeck();
            }
            ShowDetails(card);
        }

        private void SwapSlots(int first, int second)
        {
            string card = deck[first];
            deck[first] = deck[second];
            deck[second] = card;
        }

        private void CompleteChange()
        {
            selectedSlot = -1;
            cardToAdd = null;
            PlayerPrefs.SetString(DeckKey, JsonUtility.ToJson(new SavedDeck { cards = deck }));
            PlayerPrefs.Save();
            RefreshDeck();
        }

        private void ShowDetails(CardDefinition card)
        {
            string description = card.name + " · " + card.cost.ToString("0") + " 能量\n" + card.description;
            if (cardToAdd != null) description += "\n點選上場欄位以替換。";
            else if (selectedSlot >= 0) description += "\n選擇另一張牌以交換。";
            details.text = description;
        }

        private void RefreshDeck()
        {
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i].Show(client.Rules.Find(deck[i]));
                slots[i].Button.EnableInClassList("menu-card-selected", i == selectedSlot);
            }
            foreach (var pair in catalogue)
            {
                pair.Value.Button.EnableInClassList("menu-card-selected", pair.Key == cardToAdd);
                pair.Value.SetInDeck(Array.IndexOf(deck, pair.Key) >= 0);
            }
        }

        private sealed class CardTile
        {
            public readonly Button Button;
            private readonly VisualElement art;
            private readonly Label name, cost, inDeck;

            public CardTile(Action click)
            {
                Button = new Button(click);
                Button.AddToClassList("menu-card");
                art = new VisualElement { pickingMode = PickingMode.Ignore };
                art.AddToClassList("menu-card-art");
                Button.Add(art);
                name = AddLabel("menu-card-name");
                cost = AddLabel("menu-card-cost");
                inDeck = AddLabel("menu-card-in-deck");
                inDeck.text = "已選";
                SetInDeck(false);
            }

            public void Show(CardDefinition card)
            {
                art.style.backgroundImage = DuelSpriteLibrary.Card(card.id);
                name.text = card.name;
                cost.text = card.cost.ToString("0");
                Button.tooltip = card.name + " · " + cost.text + " 能量\n" + card.description;
            }

            public void SetInDeck(bool selected)
            {
                inDeck.style.display = selected ? DisplayStyle.Flex : DisplayStyle.None;
            }

            private Label AddLabel(string className)
            {
                var label = new Label { pickingMode = PickingMode.Ignore, enableRichText = false };
                label.AddToClassList(className);
                Button.Add(label);
                return label;
            }
        }
    }
}
